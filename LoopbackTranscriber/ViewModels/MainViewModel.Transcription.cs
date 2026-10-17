using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LoopbackTranscriber.Models;
using LoopbackTranscriber.Services;

namespace LoopbackTranscriber.ViewModels;

// Pasos de StopAndTranscribeAsync: parar capturas, transcribir pistas, filtrar, componer y guardar el texto.
public partial class MainViewModel
{
    private async Task<(string RawWavPath, List<DeviceLoopbackRecorder.DeviceTrack> DeviceTracks, string? MicRawWavPath)> StopCapturesAsync()
    {
        List<DeviceLoopbackRecorder.DeviceTrack> deviceTracks;
        string? micRawWavPath;

        var rawWavPath = await _recorder.StopAsync();

        try
        {
            deviceTracks = await Task.Run(() => _deviceRecorder.Stop());
        }
        catch (Exception ex)
        {
            AppLog.Warn("MainViewModel.StopAndTranscribeAsync: fallo al parar la captura por dispositivo, se usa solo la captura por proceso", ex);
            deviceTracks = new List<DeviceLoopbackRecorder.DeviceTrack>();
        }

        try
        {
            micRawWavPath = await Task.Run(() => _microphoneRecorder.Stop());
        }
        catch (Exception ex)
        {
            AppLog.Warn("MainViewModel.StopAndTranscribeAsync: fallo al parar la captura del microfono, el resumen solo tendra el audio del interlocutor", ex);
            micRawWavPath = null;
        }

        return (rawWavPath, deviceTracks, micRawWavPath);
    }

    private async Task<(List<WhisperTranscriptionService.TranscriptSegment> Other, List<WhisperTranscriptionService.TranscriptSegment> Mic)> TranscribeTracksAsync(
        string whisperWavPath, string? micWhisperWavPath, bool hasMicTrack, string modelPath, string language, int threadCount)
    {
        var otherPercent = 0;
        var micPercent = 0;

        void ReportCombinedPercent()
        {
            var combined = hasMicTrack ? (otherPercent + micPercent) / 2 : otherPercent;
            TranscriptionProgressPercent = combined;
            StatusText = Strings.Instance.Format("StatusTranscribingWithPercent", combined);
        }

        var otherPercentProgress = new Progress<int>(percent => { otherPercent = percent; ReportCombinedPercent(); });
        var micPercentProgress = new Progress<int>(percent => { micPercent = percent; ReportCombinedPercent(); });

        var otherTask = _whisper.TranscribeSegmentsAsync(whisperWavPath, modelPath, language,
            threadCount, useGpu: true, percentProgress: otherPercentProgress);
        var micTask = hasMicTrack
            ? _whisper.TranscribeSegmentsAsync(micWhisperWavPath!, modelPath, language,
                threadCount, useGpu: true, percentProgress: micPercentProgress)
            : Task.FromResult(new List<WhisperTranscriptionService.TranscriptSegment>());

        var transcriptionStopwatch = Stopwatch.StartNew();
        await Task.WhenAll(otherTask, micTask);
        transcriptionStopwatch.Stop();

        var otherSegments = await otherTask;
        var micSegments = await micTask;

        return (otherSegments, micSegments);
    }

    private static List<WhisperTranscriptionService.TranscriptSegment> FilterMicSegmentsByActivity(List<WhisperTranscriptionService.TranscriptSegment> micSegments, string micWhisperWavPath)
    {
        try
        {
            var micActivity = AudioConverter.GetActivityMap(micWhisperWavPath);
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
            AppLog.Warn("MainViewModel.StopAndTranscribeAsync: no se pudo analizar la actividad del microfono, se conservan todos los segmentos", ex);
        }

        return micSegments;
    }

    private static string ComposeTranscriptText(
        List<WhisperTranscriptionService.TranscriptSegment> otherSegments, List<WhisperTranscriptionService.TranscriptSegment> filteredMicSegments, TimeSpan micStartOffset, string? userName)
    {
        var orderedSegments = otherSegments
            .Select(s => (s.Start, Speaker: "Participantes", s.Text))
            .Concat(filteredMicSegments.Select(s => (Start: s.Start + micStartOffset, Speaker: SelfSpeakerLabel(userName), s.Text)))
            .OrderBy(s => s.Start)
            .Where(s => s.Text.Trim().Length > 0)
            .ToList();

        var distinctSpeakers = orderedSegments.Select(s => s.Speaker).Distinct().Count();

        var textBuilder = new StringBuilder();

        if (distinctSpeakers <= 1)
        {
            foreach (var segment in orderedSegments)
            {
                if (textBuilder.Length > 0)
                {
                    textBuilder.Append(' ');
                }

                textBuilder.Append(segment.Text.Trim());
            }
        }
        else
        {
            string? currentSpeaker = null;

            foreach (var segment in orderedSegments)
            {
                var segmentText = segment.Text.Trim();

                if (segment.Speaker != currentSpeaker)
                {
                    if (currentSpeaker is not null)
                    {
                        textBuilder.Append('\n').Append('\n');
                    }

                    textBuilder.Append(segment.Speaker).Append(": ");
                    currentSpeaker = segment.Speaker;
                }
                else
                {
                    textBuilder.Append(' ');
                }

                textBuilder.Append(segmentText);
            }
        }

        return textBuilder.ToString();
    }

    private void ShowAndSaveTranscript(string text, AppSettings settings, string? transcriptionSourceWavPath, string? micRawWavPath)
    {
        TranscriptText = string.IsNullOrWhiteSpace(text)
            ? Strings.Instance.Get("StatusNoTextDetected")
            : text;

        if (!string.IsNullOrWhiteSpace(text))
        {
            var recordedAt = DateTime.Now;
            var baseName = $"{SelectedTarget?.DisplayName ?? "reunion"}_{recordedAt:yyyyMMdd_HHmmss}";
            var appFolderName = TranscriptHistoryService.GetAppFolderName(SelectedTarget?.DisplayName ?? "reunion");
            var recordingFolder = Path.Combine(settings.TranscriptsFolder, appFolderName, baseName);
            Directory.CreateDirectory(recordingFolder);

            var markdownTitle = SelectedTarget?.DisplayName ?? "Reunion";
            var markdown = $"# {markdownTitle} - {recordedAt:dd/MM/yyyy HH:mm}\n\n{text}";
            File.WriteAllText(Path.Combine(recordingFolder, baseName + ".md"), markdown);

            if (transcriptionSourceWavPath is not null && File.Exists(transcriptionSourceWavPath))
            {
                File.Copy(transcriptionSourceWavPath, Path.Combine(recordingFolder, baseName + ".wav"), overwrite: true);
            }

            if (micRawWavPath is not null && File.Exists(micRawWavPath))
            {
                File.Copy(micRawWavPath, Path.Combine(recordingFolder, baseName + "_tu.wav"), overwrite: true);
            }

            _viewedTranscriptFilePath = Path.Combine(recordingFolder, baseName + ".md");

            StatusText = Strings.Instance.Get("StatusTranscriptReadySaved");

            LoadTranscriptHistoryFromDisk();
        }
        else
        {
            StatusText = Strings.Instance.Get("StatusTranscriptReady");
        }
    }
}
