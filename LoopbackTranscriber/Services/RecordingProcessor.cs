using System.IO;
using LoopbackTranscriber.Models;
using NAudio.Wave;
using TranscriptSegment = LoopbackTranscriber.Services.WhisperTranscriptionService.TranscriptSegment;

namespace LoopbackTranscriber.Services;

public interface IWhisperTranscriber
{
    Task EnsureModelDownloadedAsync(string modelSize, string destinationPath,
        IProgress<UserMessage>? progress = null, IProgress<double>? percentProgress = null,
        CancellationToken cancellationToken = default);

    Task<TrackTranscription> TranscribeAsync(TranscriptionRequest request, IProgress<int>? percentProgress = null,
        CancellationToken cancellationToken = default);
}

public interface IAudioPreparer
{
    void ConvertToWhisperFormat(string sourceWavPath, string destWavPath);

    void ApplyNoiseGate(string wavPath);

    bool[]? GetActivityMap(string wavPath);

    double[]? GetLevelMap(string wavPath);

    double GetDurationSeconds(string wavPath);
}

public sealed class NaudioAudioPreparer : IAudioPreparer
{
    public void ConvertToWhisperFormat(string sourceWavPath, string destWavPath) =>
        AudioConverter.ConvertToWhisperFormat(sourceWavPath, destWavPath);

    public void ApplyNoiseGate(string wavPath) => AudioConverter.ApplyNoiseGate(wavPath);

    public bool[]? GetActivityMap(string wavPath) => AudioConverter.GetActivityMap(wavPath);

    public double[]? GetLevelMap(string wavPath) => AudioConverter.GetLevelMap(wavPath);

    public double GetDurationSeconds(string wavPath)
    {
        try
        {
            using var reader = new WaveFileReader(wavPath);
            return reader.TotalTime.TotalSeconds;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"NaudioAudioPreparer.GetDurationSeconds: could not read the duration of \"{wavPath}\"", ex);
            return 0;
        }
    }
}

public sealed record RecordingCallbacks(Action<UserMessage> Status, Action<double> Percent, Action<bool> TranscribingActive);

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
        AppSettings settings, RecordingCallbacks callbacks, CancellationToken cancellationToken = default,
        string? workFolder = null)
    {
        string? whisperWavPath = null;
        string? micWhisperWavPath = null;

        try
        {
            var modelPath = settings.GetModelFilePath();

            var progress = new Progress<UserMessage>(callbacks.Status);

            callbacks.Status(UserMessage.FromKey("StatusCheckingModel"));
            await _whisper.EnsureModelDownloadedAsync(settings.ModelSize, modelPath, progress, null, cancellationToken);

            callbacks.TranscribingActive(true);
            callbacks.Percent(0);
            callbacks.Status(UserMessage.FromKey("StatusTranscribingWithPercent", 0));

            whisperWavPath = DerivedPath(sourceWavPath, workFolder);

            var convertedWhisperPath = whisperWavPath;
            await Task.Run(() => _audio.ConvertToWhisperFormat(sourceWavPath, convertedWhisperPath), cancellationToken);

            var hasMicTrack = micWavPath is not null;
            if (hasMicTrack)
            {
                micWhisperWavPath = DerivedPath(micWavPath!, workFolder);

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
                        AppLog.Warn("RecordingProcessor: failed to apply the noise gate to the microphone; transcribing unfiltered", ex);
                    }
                }, cancellationToken);
            }

            var (otherSegments, micSegments) = await TranscribeTracksAsync(
                whisperWavPath, micWhisperWavPath, modelPath, settings, callbacks, cancellationToken);

            callbacks.TranscribingActive(false);

            if (hasMicTrack && micSegments.Count > 0)
            {
                var micWavForActivity = micWhisperWavPath!;
                var micSegmentsToFilter = micSegments;
                micSegments = await Task.Run(() => FilterMicSegmentsByActivity(micSegmentsToFilter, micWavForActivity), cancellationToken);
            }

            if (hasMicTrack)
            {
                var micLevels = await Task.Run(() => ReadLevelMap(micWhisperWavPath!), cancellationToken);
                var resolution = MeetingEchoFilter.Resolve(micSegments, otherSegments, micStartOffset, MicLoudness.Create(micLevels));
                micSegments = resolution.MicSegments;
                otherSegments = resolution.OtherSegments;
            }

            var text = TranscriptComposer.Compose(otherSegments, micSegments, micStartOffset, settings.UserName);

            if (string.IsNullOrWhiteSpace(text))
            {
                var audioSeconds = _audio.GetDurationSeconds(sourceWavPath);
                if (audioSeconds >= MinSecondsForEmptyTranscriptToBeAnError)
                {
                    throw UserError.InvalidOperation("ErrorNoTextInAudio", (int)audioSeconds);
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

    private static string DerivedPath(string wavPath, string? workFolder)
    {
        var folder = workFolder ?? Path.GetDirectoryName(wavPath)!;
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, Path.GetFileNameWithoutExtension(wavPath) + ".16k.wav");
    }

    private async Task<(List<TranscriptSegment> Other, List<TranscriptSegment> Mic)> TranscribeTracksAsync(
        string whisperWavPath, string? micWhisperWavPath, string modelPath, AppSettings settings,
        RecordingCallbacks callbacks, CancellationToken cancellationToken)
    {
        var hasMicTrack = micWhisperWavPath is not null;
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
            callbacks.Status(UserMessage.FromKey("StatusTranscribingWithPercent", combined));
        }

        var otherPercentProgress = new Progress<int>(percent => Report(() => otherPercent = percent));
        var micPercentProgress = new Progress<int>(percent => Report(() => micPercent = percent));

        var meetingLanguage = settings.ResolveMeetingLanguage();
        var fallbackLanguage = settings.UiLanguage;
        var detectOnAppTrack = hasMicTrack &&
                               string.Equals(meetingLanguage, AppSettings.MeetingLanguageAuto, StringComparison.OrdinalIgnoreCase);

        TranscriptionRequest OtherRequest(int threads) =>
            new(whisperWavPath, modelPath, meetingLanguage, fallbackLanguage, threads, StrictNoSpeech: false);

        TranscriptionRequest MicRequest(string language, int threads) =>
            new(micWhisperWavPath!, modelPath, language, fallbackLanguage, threads, StrictNoSpeech: true);

        if (!hasMicTrack)
        {
            var single = await _whisper.TranscribeAsync(OtherRequest(WhisperTranscriptionService.GetRecommendedThreadCount(1)),
                otherPercentProgress, cancellationToken);
            LogLanguage(settings, single.Language, null);
            return (single.Segments, new List<TranscriptSegment>());
        }

        if (detectOnAppTrack)
        {
            var threads = WhisperTranscriptionService.GetRecommendedThreadCount(1);
            var other = await _whisper.TranscribeAsync(OtherRequest(threads), otherPercentProgress, cancellationToken);
            var micLanguage = other.Language ?? AppSettings.MeetingLanguageAuto;
            var mic = await _whisper.TranscribeAsync(MicRequest(micLanguage, threads), micPercentProgress, cancellationToken);
            LogLanguage(settings, other.Language, mic.Language);
            return (other.Segments, mic.Segments);
        }

        var parallelThreads = WhisperTranscriptionService.GetRecommendedThreadCount(2);
        var otherTask = _whisper.TranscribeAsync(OtherRequest(parallelThreads), otherPercentProgress, cancellationToken);
        var micTask = _whisper.TranscribeAsync(MicRequest(meetingLanguage, parallelThreads), micPercentProgress, cancellationToken);

        await Task.WhenAll(otherTask, micTask);

        var otherResult = await otherTask;
        var micResult = await micTask;
        LogLanguage(settings, otherResult.Language, micResult.Language);
        return (otherResult.Segments, micResult.Segments);
    }

    private static void LogLanguage(AppSettings settings, string? otherLanguage, string? micLanguage)
    {
        var setting = string.IsNullOrWhiteSpace(settings.MeetingLanguage) ? $"app ({settings.UiLanguage})" : settings.MeetingLanguage;
        AppLog.Info($"RecordingProcessor: meeting language setting \"{setting}\"; app track \"{otherLanguage ?? "-"}\", microphone \"{micLanguage ?? "-"}\"");
    }

    private double[]? ReadLevelMap(string wavPath)
    {
        try
        {
            return _audio.GetLevelMap(wavPath);
        }
        catch (Exception ex)
        {
            AppLog.Warn("RecordingProcessor: could not measure microphone levels; the echo filter will rely on timing only", ex);
            return null;
        }
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
            AppLog.Warn("RecordingProcessor: could not analyze microphone activity; keeping all segments", ex);
        }

        return micSegments;
    }
}
