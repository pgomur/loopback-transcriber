using System.IO;
using System.Text.Json;

namespace LoopbackTranscriber.Services;

internal sealed class CaptureSessionInfo
{
    public string AppName { get; set; } = string.Empty;
    public DateTime RecordedAt { get; set; }
    public DateTime? ProcessStartedUtc { get; set; }
    public DateTime? DeviceStartedUtc { get; set; }
    public DateTime? MicStartedUtc { get; set; }
}

internal static class OrphanRecordingRecovery
{
    public const string SessionFileSuffix = ".session.json";

    private const string MicSuffix = "_mic";
    private const string DerivedSuffix = ".16k.wav";

    public static List<FailedRecording> Run(string tempFolder, FailedRecordingStore store, string reason,
        Func<string, bool> isOwned, double signalThreshold)
    {
        var recovered = new List<FailedRecording>();

        string[] sessionFiles;
        try
        {
            if (!Directory.Exists(tempFolder))
            {
                return recovered;
            }

            sessionFiles = Directory.GetFiles(tempFolder, "grabacion_*" + SessionFileSuffix);
        }
        catch (Exception ex)
        {
            AppLog.Warn("OrphanRecordingRecovery: no se pudo leer la carpeta temporal", ex);
            return recovered;
        }

        foreach (var sessionFile in sessionFiles)
        {
            var baseName = Path.GetFileName(sessionFile)[..^SessionFileSuffix.Length];
            if (isOwned(baseName))
            {
                continue;
            }

            try
            {
                var saved = RecoverSession(tempFolder, baseName, sessionFile, store, reason, signalThreshold);
                if (saved is not null)
                {
                    recovered.Add(saved);
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn($"OrphanRecordingRecovery: no se pudo recuperar la grabacion \"{baseName}\"", ex);
            }
        }

        return recovered;
    }

    private static FailedRecording? RecoverSession(string tempFolder, string baseName, string sessionFile,
        FailedRecordingStore store, string reason, double signalThreshold)
    {
        var info = JsonSerializer.Deserialize<CaptureSessionInfo>(File.ReadAllText(sessionFile))
                   ?? throw new InvalidDataException($"Fichero de sesion vacio: \"{sessionFile}\"");

        var wavs = Directory.GetFiles(tempFolder, baseName + "*.wav");
        if (wavs.Any(WavRepair.IsInUse))
        {
            AppLog.Info($"OrphanRecordingRecovery: \"{baseName}\" tiene ficheros en uso; se deja para mas tarde");
            return null;
        }

        var derived = wavs.Where(p => p.EndsWith(DerivedSuffix, StringComparison.OrdinalIgnoreCase)).ToList();
        var tracks = wavs.Except(derived).Where(WavRepair.HasAudioData).ToList();

        foreach (var track in tracks)
        {
            WavRepair.TryRepairHeader(track);
        }

        var processWav = tracks.FirstOrDefault(p => NameIs(p, baseName));
        var micWav = tracks.FirstOrDefault(p => NameIs(p, baseName + MicSuffix));
        var deviceWavs = tracks.Where(p => p != processWav && p != micWav).ToList();

        var processHasSignal = processWav is not null && WavRepair.MeasurePeak(processWav, signalThreshold) >= signalThreshold;

        var deviceSource = processHasSignal
            ? null
            : deviceWavs
                .Select(p => (Path: p, Peak: WavRepair.MeasurePeak(p, 1.0)))
                .Where(t => t.Peak >= signalThreshold)
                .OrderByDescending(t => t.Peak)
                .Select(t => t.Path)
                .FirstOrDefault();

        var source = deviceSource ?? processWav;
        var mic = micWav;
        if (source is null)
        {
            source = micWav;
            mic = null;
        }

        if (source is null)
        {
            AppLog.Info($"OrphanRecordingRecovery: \"{baseName}\" no tiene audio; se descarta");
            DeleteAll(wavs, sessionFile);
            return null;
        }

        var sourceStartedUtc = deviceSource is not null ? info.DeviceStartedUtc : info.ProcessStartedUtc;
        var micStartOffset = mic is not null && info.MicStartedUtc is not null && sourceStartedUtc is not null
            ? info.MicStartedUtc.Value - sourceStartedUtc.Value
            : TimeSpan.Zero;

        var saved = store.TryPreserve(source, mic, micStartOffset, info.AppName, info.RecordedAt, reason);
        if (saved is null)
        {
            AppLog.Error($"OrphanRecordingRecovery: no se pudo conservar \"{baseName}\"; se reintentara en el proximo arranque");
            return null;
        }

        var leftovers = wavs.Where(p => p != source && p != mic);
        DeleteAll(leftovers, sessionFile);

        AppLog.Info($"OrphanRecordingRecovery: recuperada \"{baseName}\" ({info.AppName}) desde \"{Path.GetFileName(source)}\"");
        return saved;
    }

    private static bool NameIs(string path, string expected) =>
        string.Equals(Path.GetFileNameWithoutExtension(path), expected, StringComparison.OrdinalIgnoreCase);

    private static void DeleteAll(IEnumerable<string> paths, string sessionFile)
    {
        foreach (var path in paths)
        {
            FileCleanup.TryDelete(path, "OrphanRecordingRecovery");
        }

        FileCleanup.TryDelete(sessionFile, "OrphanRecordingRecovery");
    }
}
