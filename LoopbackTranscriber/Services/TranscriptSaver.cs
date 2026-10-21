using System.IO;
using System.Text.Json;
using LoopbackTranscriber.Models;

namespace LoopbackTranscriber.Services;

public sealed class TranscriptSaver
{
    public const string MicTrackSuffix = "_mic";
    public const string MetadataExtension = ".recording.json";

    public string? Save(string text, AppSettings settings, string? sourceWavPath, string? micWavPath,
        TimeSpan micStartOffset, string appName, DateTime recordedAt)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var baseName = $"{appName}_{recordedAt:yyyyMMdd_HHmmss}";
        var appFolderName = TranscriptHistoryService.GetAppFolderName(appName);
        var recordingFolder = Path.Combine(settings.TranscriptsFolder, appFolderName, baseName);
        Directory.CreateDirectory(recordingFolder);

        var markdownPath = Path.Combine(recordingFolder, baseName + ".md");
        File.WriteAllText(markdownPath, BuildMarkdown(BuildHeader(appName, recordedAt), text));

        if (sourceWavPath is not null && File.Exists(sourceWavPath))
        {
            File.Copy(sourceWavPath, Path.Combine(recordingFolder, baseName + ".wav"), overwrite: true);
        }

        if (micWavPath is not null && File.Exists(micWavPath))
        {
            File.Copy(micWavPath, Path.Combine(recordingFolder, baseName + MicTrackSuffix + ".wav"), overwrite: true);
            RecordingMetadata.Write(Path.Combine(recordingFolder, baseName + MetadataExtension),
                new RecordingMetadata { MicStartOffsetTicks = micStartOffset.Ticks });
        }

        return markdownPath;
    }

    public static string BuildHeader(string appName, DateTime recordedAt) => $"# {appName} - {recordedAt:dd/MM/yyyy HH:mm}";

    public static string BuildMarkdown(string header, string text) => $"{header}\n\n{text}";
}

public sealed class RecordingMetadata
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public long MicStartOffsetTicks { get; set; }

    public static void Write(string path, RecordingMetadata metadata)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(metadata, JsonOptions));
        }
        catch (Exception ex) when (ExpectedFailures.IsFile(ex))
        {
            AppLog.Warn($"RecordingMetadata: could not write \"{path}\"", ex);
        }
    }

    public static RecordingMetadata? TryRead(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RecordingMetadata>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is JsonException || ExpectedFailures.IsFile(ex))
        {
            AppLog.Warn($"RecordingMetadata: could not read \"{path}\"", ex);
            return null;
        }
    }
}
