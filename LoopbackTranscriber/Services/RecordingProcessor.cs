using System.IO;
using LoopbackTranscriber.Models;
using NAudio.Wave;
using TranscriptSegment = LoopbackTranscriber.Services.WhisperTranscriptionService.TranscriptSegment;

namespace LoopbackTranscriber.Services;

public interface IWhisperTranscriber
{
    Task EnsureModelDownloadedAsync(string modelSize, string destinationPath,
        IProgress<string>? progress = null, IProgress<double>? percentProgress = null,
        CancellationToken cancellationToken = default);

    Task<List<TranscriptSegment>> TranscribeSegmentsAsync(string wavPath, string modelPath,
        string language, int threadCount, bool useGpu = true, IProgress<int>? percentProgress = null,
        CancellationToken cancellationToken = default);
}

public interface IAudioPreparer
{
    void ConvertToWhisperFormat(string sourceWavPath, string destWavPath);

    void ApplyNoiseGate(string wavPath);

    bool[]? GetActivityMap(string wavPath);

    double GetDurationSeconds(string wavPath);
}

public sealed class NaudioAudioPreparer : IAudioPreparer
{
    public void ConvertToWhisperFormat(string sourceWavPath, string destWavPath) =>
        AudioConverter.ConvertToWhisperFormat(sourceWavPath, destWavPath);

    public void ApplyNoiseGate(string wavPath) => AudioConverter.ApplyNoiseGate(wavPath);

    public bool[]? GetActivityMap(string wavPath) => AudioConverter.GetActivityMap(wavPath);

    public double GetDurationSeconds(string wavPath)
    {
        try
        {
            using var reader = new WaveFileReader(wavPath);
            return reader.TotalTime.TotalSeconds;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"NaudioAudioPreparer.GetDurationSeconds: no se pudo leer la duracion de \"{wavPath}\"", ex);
            return 0;
        }
    }
}

public sealed record RecordingCallbacks(Action<string> Status, Action<double> Percent, Action<bool> TranscribingActive);

public sealed class RecordingProcessor
{
    public const double MinSecondsForEmptyTranscriptToBeAnError = 20;

    private readonly IWhisperTranscriber _whisper;
    private readonly IAudioPreparer _audio;

    public RecordingProcessor(IWhisperTranscriber whisper, IAudioPreparer audio)
    {
        _whisper = whisper;
        _audio = audio;
    }

    public async Task<string> ProcessAsync(string sourceWavPath, string? micWavPath, TimeSpan micStartOffset,
        AppSettings settings, RecordingCallbacks callbacks, CancellationToken cancellationToken = default)
    {
        string? whisperWavPath = null;
        string? micWhisperWavPath = null;

        try
        {
            var modelPath = settings.GetModelFilePath();

            var progress = new Progress<string>(callbacks.Status);

            callbacks.Status(Strings.Instance.Get("StatusCheckingModel"));
            await _whisper.EnsureModelDownloadedAsync(settings.ModelSize, modelPath, progress, null, cancellationToken);

            callbacks.TranscribingActive(true);
            callbacks.Percent(0);
            callbacks.Status(Strings.Instance.Format("StatusTranscribingWithPercent", 0));

            whisperWavPath = DerivedPath(sourceWavPath);

            var convertedWhisperPath = whisperWavPath;
            await Task.Run(() => _audio.ConvertToWhisperFormat(sourceWavPath, convertedWhisperPath), cancellationToken);

            var hasMicTrack = micWavPath is not null;
            if (hasMicTrack)
            {
                micWhisperWavPath = DerivedPath(micWavPath!);

                var micSource = micWavPath!;
                var micConverted = micWhisperWavPath;
                await Task.Run(() =>
                {
                    _audio.ConvertToWhisperFormat(micSource, micConverted);

                    try
                    {
                        _audio.ApplyNoiseGate(micConverted);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warn("RecordingProcessor: fallo al aplicar la puerta de ruido al microfono, se transcribe sin filtrar", ex);
                    }
                }, cancellationToken);
            }

            var threadCount = WhisperTranscriptionService.GetRecommendedThreadCount(hasMicTrack ? 2 : 1);

            var (otherSegments, micSegments) = await TranscribeTracksAsync(
                whisperWavPath, micWhisperWavPath, hasMicTrack, modelPath, settings.Language, threadCount, callbacks, cancellationToken);

            callbacks.TranscribingActive(false);

            if (hasMicTrack && micSegments.Count > 0)
            {
                var micWavForActivity = micWhisperWavPath!;
                var micSegmentsToFilter = micSegments;
                micSegments = await Task.Run(() => FilterMicSegmentsByActivity(micSegmentsToFilter, micWavForActivity), cancellationToken);
            }

            var filteredMicSegments = hasMicTrack
                ? MeetingEchoFilter.RemoveEchoes(micSegments, otherSegments, micStartOffset)
                : micSegments;

            var text = TranscriptComposer.Compose(otherSegments, filteredMicSegments, micStartOffset, settings.UserName);

            if (string.IsNullOrWhiteSpace(text))
            {
                var audioSeconds = _audio.GetDurationSeconds(sourceWavPath);
                if (audioSeconds >= MinSecondsForEmptyTranscriptToBeAnError)
                {
                    throw new InvalidOperationException(Strings.Instance.Format("ErrorNoTextInAudio", (int)audioSeconds));
                }
            }

            return text;
        }
        finally
        {
            FileCleanup.TryDelete(whisperWavPath, "RecordingProcessor");
            FileCleanup.TryDelete(micWhisperWavPath, "RecordingProcessor");
        }
    }

    private static string DerivedPath(string wavPath) =>
        Path.Combine(Path.GetDirectoryName(wavPath)!, Path.GetFileNameWithoutExtension(wavPath) + ".16k.wav");

    private async Task<(List<TranscriptSegment> Other, List<TranscriptSegment> Mic)> TranscribeTracksAsync(
        string whisperWavPath, string? micWhisperWavPath, bool hasMicTrack, string modelPath, string language,
        int threadCount, RecordingCallbacks callbacks, CancellationToken cancellationToken)
    {
        var gate = new object();
        var otherPercent = 0;
        var micPercent = 0;

        void Report(Action update)
        {
            int combined;
            lock (gate)
            {
                update();
                combined = hasMicTrack ? (otherPercent + micPercent) / 2 : otherPercent;
            }

            callbacks.Percent(combined);
            callbacks.Status(Strings.Instance.Format("StatusTranscribingWithPercent", combined));
        }

        var otherPercentProgress = new Progress<int>(percent => Report(() => otherPercent = percent));
        var micPercentProgress = new Progress<int>(percent => Report(() => micPercent = percent));

        var otherTask = _whisper.TranscribeSegmentsAsync(whisperWavPath, modelPath, language,
            threadCount, useGpu: true, percentProgress: otherPercentProgress, cancellationToken: cancellationToken);
        var micTask = hasMicTrack
            ? _whisper.TranscribeSegmentsAsync(micWhisperWavPath!, modelPath, language,
                threadCount, useGpu: true, percentProgress: micPercentProgress, cancellationToken: cancellationToken)
            : Task.FromResult(new List<TranscriptSegment>());

        await Task.WhenAll(otherTask, micTask);

        return (await otherTask, await micTask);
    }

    private List<TranscriptSegment> FilterMicSegmentsByActivity(List<TranscriptSegment> micSegments, string micWhisperWavPath)
    {
        try
        {
            var micActivity = _audio.GetActivityMap(micWhisperWavPath);
            if (micActivity is not null)
            {
                micSegments = micSegments.Where(s =>
                {
                    var (fraction, activeSeconds) = AudioConverter.MeasureActivity(micActivity, s.Start, s.End);
                    return fraction >= 0.15 && activeSeconds >= 0.15;
                }).ToList();
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("RecordingProcessor: no se pudo analizar la actividad del microfono, se conservan todos los segmentos", ex);
        }

        return micSegments;
    }
}
