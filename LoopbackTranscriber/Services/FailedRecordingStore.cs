using System.IO;
using System.Text.Json;
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

    [JsonIgnore] public string Folder { get; set; } = string.Empty;
    [JsonIgnore] public string SourceWavPath => Path.Combine(Folder, SourceWav);
    [JsonIgnore] public string? MicWavPath => string.IsNullOrEmpty(MicWav) ? null : Path.Combine(Folder, MicWav);
    [JsonIgnore] public TimeSpan MicStartOffset => TimeSpan.FromTicks(MicStartOffsetTicks);
}

public static class FailedRecordingStore
{
    private const string ManifestName = "manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string RootFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LoopbackTranscriber", "failed");

    public static FailedRecording? TryPreserve(string? sourceWavPath, string? micWavPath, TimeSpan micStartOffset,
        string appName, DateTime recordedAt, string error)
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
                SourceWav = "reunion.wav",
                MicWav = !string.IsNullOrEmpty(micWavPath) && File.Exists(micWavPath) ? "microfono.wav" : null,
                MicStartOffsetTicks = micStartOffset.Ticks,
                Error = error,
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
                catch (Exception ex)
                {
                    AppLog.Warn("FailedRecordingStore.TryPreserve: no se pudo mover el WAV del microfono, se guarda solo la reunion", ex);
                    recording.MicWav = null;
                    WriteManifest(recording);
                }
            }

            return recording;
        }
        catch (Exception ex)
        {
            AppLog.Warn("FailedRecordingStore.TryPreserve: no se pudo guardar la grabacion fallida", ex);

            try
            {
                if (folder is not null)
                {
                    var movedSource = Path.Combine(folder, "reunion.wav");
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
            catch (Exception rollbackEx)
            {
                AppLog.Warn("FailedRecordingStore.TryPreserve: no se pudo deshacer el guardado a medias", rollbackEx);
            }

            return null;
        }
    }

    private static void WriteManifest(FailedRecording recording) =>
        File.WriteAllText(Path.Combine(recording.Folder, ManifestName), JsonSerializer.Serialize(recording, JsonOptions));

    public static List<FailedRecording> List()
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
                        result.Add(recording);
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Warn($"FailedRecordingStore.List: manifiesto ilegible en \"{folder}\"", ex);
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("FailedRecordingStore.List: no se pudo leer la carpeta de grabaciones fallidas", ex);
        }

        return result.OrderByDescending(r => r.RecordedAt).ToList();
    }

    public static void Delete(FailedRecording recording)
    {
        try
        {
            if (!string.IsNullOrEmpty(recording.Folder) && Directory.Exists(recording.Folder))
            {
                Directory.Delete(recording.Folder, recursive: true);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"FailedRecordingStore.Delete: no se pudo borrar \"{recording.Folder}\"", ex);
        }
    }
}
