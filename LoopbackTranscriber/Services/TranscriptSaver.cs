using System.IO;
using LoopbackTranscriber.Models;

namespace LoopbackTranscriber.Services;

public sealed class TranscriptSaver
{
    public string? Save(string text, AppSettings settings, string? sourceWavPath, string? micWavPath,
        string appName, DateTime recordedAt)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var baseName = $"{appName}_{recordedAt:yyyyMMdd_HHmmss}";
        var appFolderName = TranscriptHistoryService.GetAppFolderName(appName);
        var recordingFolder = Path.Combine(settings.TranscriptsFolder, appFolderName, baseName);
        Directory.CreateDirectory(recordingFolder);

        var markdown = $"# {appName} - {recordedAt:dd/MM/yyyy HH:mm}\n\n{text}";
        var markdownPath = Path.Combine(recordingFolder, baseName + ".md");
        File.WriteAllText(markdownPath, markdown);

        if (sourceWavPath is not null && File.Exists(sourceWavPath))
        {
            File.Copy(sourceWavPath, Path.Combine(recordingFolder, baseName + ".wav"), overwrite: true);
        }

        if (micWavPath is not null && File.Exists(micWavPath))
        {
            File.Copy(micWavPath, Path.Combine(recordingFolder, baseName + "_tu.wav"), overwrite: true);
        }

        return markdownPath;
    }
}
