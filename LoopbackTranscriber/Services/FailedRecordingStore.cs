using System.IO;
using System.Text.Json;
using NAudio.Wave;
using System.Text.Json.Serialization;

namespace LoopbackTranscriber.Services;

public sealed class FailedRecording
{
    public string AppName { get; set; } = string.Empty;
    public DateTime RecordedAt { get; set; }
    public string SourceWav { get; set; } = string.Empty;
    public string? MicWav { get; set; }
    public long MicStartOffsetTicks { get; set; }
    public string? Error { get; set; }
    public string? ErrorKey { get; set; }
    public string[]? ErrorArgs { get; set; }

    [JsonIgnore] public string Folder { get; set; } = string.Empty;
    [JsonIgnore] public string SourceWavPath => Path.Combine(Folder, SourceWav);
    [JsonIgnore] public string? MicWavPath => string.IsNullOrEmpty(MicWav) ? null : Path.Combine(Folder, MicWav);
    [JsonIgnore] public TimeSpan MicStartOffset => TimeSpan.FromTicks(MicStartOffsetTicks);
    [JsonIgnore] public TimeSpan? Duration { get; set; }
}

public sealed class FailedRecordingStore
{
    private const string ManifestName = "manifest.json";
    private const string SourceFileName = "meeting.wav";
    private const string MicFileName = "microphone.wav";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultRootFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LoopbackTranscriber", "failed");

    private readonly UserNotices? _notices;

    public FailedRecordingStore(string rootFolder, UserNotices? notices = null)
    {
        RootFolder = rootFolder;
        _notices = notices;
    }

    public string RootFolder { get; }

    public FailedRecording? TryPreserve(string? sourceWavPath, string? micWavPath, TimeSpan micStartOffset,
        string appName, DateTime recordedAt, UserMessage error)
    {
        if (string.IsNullOrEmpty(sourceWavPath) || !File.Exists(sourceWavPath))
        {
            return null;
        }

        string? folder = null;
        try
        {
            folder = Path.Combine(RootFolder, $"{recordedAt:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"[..24]);
            Directory.CreateDirectory(folder);

            var recording = new FailedRecording
            {
                AppName = appName,
                RecordedAt = recordedAt,
                SourceWav = SourceFileName,
                MicWav = !string.IsNullOrEmpty(micWavPath) && File.Exists(micWavPath) ? MicFileName : null,
                MicStartOffsetTicks = micStartOffset.Ticks,
                Error = error.Text,
                ErrorKey = error.Key,
                ErrorArgs = error.Key is null ? null : UserError.ToTextArgs(error.Args),
                Folder = folder
            };

            WriteManifest(recording);

            File.Move(sourceWavPath, recording.SourceWavPath, overwrite: true);

            if (recording.MicWavPath is not null)
            {
                try
                {
                    File.Move(micWavPath!, recording.MicWavPath, overwrite: true);
                }
                catch (Exception ex) when (ExpectedFailures.IsFile(ex))
                {
                    AppLog.Warn("FailedRecordingStore.TryPreserve: could not move the microphone WAV; keeping only the meeting audio", ex);
                    recording.MicWav = null;
                    WriteManifest(recording);
                    _notices?.Add(UserMessage.FromKey("WarningMicNotRecorded"));
                }
            }

            return recording;
        }
        catch (Exception ex) when (ExpectedFailures.IsFile(ex))
        {
            AppLog.Warn("FailedRecordingStore.TryPreserve: could not save the failed recording", ex);

            try
            {
                if (folder is not null)
                {
                    var movedSource = Path.Combine(folder, SourceFileName);
                    if (File.Exists(movedSource) && !File.Exists(sourceWavPath))
                    {
                        File.Move(movedSource, sourceWavPath);
                    }

                    if (!File.Exists(movedSource))
                    {
                        Directory.Delete(folder, recursive: true);
                    }
                }
            }
            catch (Exception rollbackEx) when (ExpectedFailures.IsFile(rollbackEx))
            {
                AppLog.Warn("FailedRecordingStore.TryPreserve: could not roll back the partial save", rollbackEx);
            }

            return null;
        }
    }

    private static void WriteManifest(FailedRecording recording) =>
        File.WriteAllText(Path.Combine(recording.Folder, ManifestName), JsonSerializer.Serialize(recording, JsonOptions));

    public List<FailedRecording> List()
    {
        var result = new List<FailedRecording>();

        try
        {
            if (!Directory.Exists(RootFolder))
            {
                return result;
            }

            foreach (var folder in Directory.EnumerateDirectories(RootFolder))
            {
                try
                {
                    var manifestPath = Path.Combine(folder, ManifestName);
                    if (!File.Exists(manifestPath))
                    {
                        continue;
                    }

                    var recording = JsonSerializer.Deserialize<FailedRecording>(File.ReadAllText(manifestPath));
                    if (recording is null)
                    {
                        continue;
                    }

                    recording.Folder = folder;

                    if (recording.MicWavPath is not null && !File.Exists(recording.MicWavPath))
                    {
                        recording.MicWav = null;
                    }

                    if (File.Exists(recording.SourceWavPath))
                    {
                        recording.Duration = TryReadDuration(recording.SourceWavPath);
                        result.Add(recording);
                    }
                }
                catch (Exception ex) when (ex is JsonException || ExpectedFailures.IsFile(ex))
                {
                    AppLog.Warn($"FailedRecordingStore.List: unreadable manifest in \"{folder}\"", ex);
                    _notices?.AddOnce(folder, UserMessage.FromKey("NoticeUnreadableFailedRecording", folder));
                }
            }
        }
        catch (Exception ex) when (ExpectedFailures.IsFile(ex))
        {
            AppLog.Warn("FailedRecordingStore.List: could not read the failed recordings folder", ex);
            _notices?.AddOnce(RootFolder, UserMessage.FromKey("NoticeUnreadableFailedRecording", RootFolder));
        }

        return result.OrderByDescending(r => r.RecordedAt).ToList();
    }

    private static TimeSpan? TryReadDuration(string wavPath)
    {
        try
        {
            using var reader = new WaveFileReader(wavPath);
            return reader.TotalTime;
        }
        catch (Exception ex) when (ExpectedFailures.IsAudioFile(ex))
        {
            AppLog.Warn($"FailedRecordingStore: could not read the duration of \"{wavPath}\"", ex);
            return null;
        }
    }

    public void Delete(FailedRecording recording)
    {
        if (!string.IsNullOrEmpty(recording.Folder) && Directory.Exists(recording.Folder))
        {
            Directory.Delete(recording.Folder, recursive: true);
        }
    }

    public void TryDelete(FailedRecording recording) => FileCleanup.TryDeleteFolder(recording.Folder, "FailedRecordingStore");
}
