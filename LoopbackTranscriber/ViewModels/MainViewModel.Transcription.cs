using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LoopbackTranscriber.Models;
using LoopbackTranscriber.Services;

namespace LoopbackTranscriber.ViewModels;

public partial class MainViewModel
{
    private void ShowAndSaveTranscript(string text, AppSettings settings, string? transcriptionSourceWavPath, string? micRawWavPath,
        string appName, DateTime recordedAt)
    {
        TranscriptText = string.IsNullOrWhiteSpace(text)
            ? Strings.Instance.Get("StatusNoTextDetected")
            : text;

        if (!string.IsNullOrWhiteSpace(text))
        {
            var baseName = $"{appName}_{recordedAt:yyyyMMdd_HHmmss}";
            var appFolderName = TranscriptHistoryService.GetAppFolderName(appName);
            var recordingFolder = Path.Combine(settings.TranscriptsFolder, appFolderName, baseName);
            Directory.CreateDirectory(recordingFolder);

            var markdownTitle = appName;
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
