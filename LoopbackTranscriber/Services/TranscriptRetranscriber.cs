using System.IO;
using NAudio.Wave;

namespace LoopbackTranscriber.Services;

public sealed record RetranscribedTranscript(string FilePath, string FileText);

public sealed class TranscriptRetranscriber
{
    private readonly RecordingProcessor _processor;
    private readonly ISettingsStore _settings;

    public TranscriptRetranscriber(RecordingProcessor processor, ISettingsStore settings)
    {
        _processor = processor;
        _settings = settings;
    }

    public static bool HasAudio(string? transcriptPath) =>
        transcriptPath is not null && File.Exists(GetAudioPath(transcriptPath));

    private static string GetAudioPath(string transcriptPath) => Path.ChangeExtension(transcriptPath, ".wav");

    public async Task<RetranscribedTranscript> RetranscribeAsync(string transcriptPath, RecordingCallbacks callbacks,
        CancellationToken cancellationToken = default)
    {
        var folder = Path.GetDirectoryName(transcriptPath)!;
        var baseName = Path.GetFileNameWithoutExtension(transcriptPath);
        var sourceWavPath = GetAudioPath(transcriptPath);

        if (!File.Exists(sourceWavPath))
        {
            throw UserError.IO("ErrorRetranscribeNoAudio");
        }

        var micCandidate = Path.Combine(folder, baseName + TranscriptSaver.MicTrackSuffix + ".wav");
        var micWavPath = File.Exists(micCandidate) ? micCandidate : null;

        var micStartOffset = TimeSpan.Zero;
        if (micWavPath is not null)
        {
            var metadata = RecordingMetadata.TryRead(Path.Combine(folder, baseName + TranscriptSaver.MetadataExtension));
            if (metadata is not null)
            {
                micStartOffset = TimeSpan.FromTicks(metadata.MicStartOffsetTicks);
            }
            else
            {
                AppLog.Info($"TranscriptRetranscriber: no microphone offset saved for \"{baseName}\"; assuming 0");
            }
        }

        var workFolder = Path.Combine(Path.GetTempPath(), "LoopbackTranscriber", "retranscribe", Guid.NewGuid().ToString("N"));

        string text;
        try
        {
            text = await _processor.ProcessAsync(sourceWavPath, micWavPath, micStartOffset, _settings.Load(), callbacks,
                cancellationToken, workFolder);
        }
        finally
        {
            TryDeleteFolder(workFolder);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw UserError.InvalidOperation("ErrorNoTextInAudio", (int)GetDurationSeconds(sourceWavPath));
        }

        var targetPath = string.Equals(Path.GetExtension(transcriptPath), ".txt", StringComparison.OrdinalIgnoreCase)
            ? transcriptPath
            : Path.Combine(folder, baseName + ".md");

        var fileText = targetPath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            ? text
            : TranscriptSaver.BuildMarkdown(ReadHeader(targetPath) ?? $"# {baseName}", text);

        await File.WriteAllTextAsync(targetPath, fileText, cancellationToken);
        AppLog.Info($"TranscriptRetranscriber: rewrote \"{Path.GetFileName(targetPath)}\"");

        return new RetranscribedTranscript(targetPath, fileText);
    }

    private static string? ReadHeader(string markdownPath)
    {
        try
        {
            if (!File.Exists(markdownPath))
            {
                return null;
            }

            var firstLine = File.ReadLines(markdownPath).FirstOrDefault()?.Trim();
            return firstLine is not null && firstLine.StartsWith("# ", StringComparison.Ordinal) ? firstLine : null;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"TranscriptRetranscriber: could not read the header of \"{markdownPath}\"", ex);
            return null;
        }
    }

    private static double GetDurationSeconds(string wavPath)
    {
        try
        {
            using var reader = new WaveFileReader(wavPath);
            return reader.TotalTime.TotalSeconds;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"TranscriptRetranscriber: could not read the duration of \"{wavPath}\"", ex);
            return 0;
        }
    }

    private static void TryDeleteFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"TranscriptRetranscriber: could not delete \"{folder}\"", ex);
        }
    }
}
