using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LoopbackTranscriber.Services;

internal sealed class CaptureSessionInfo
{
    public string AppName { get; set; } = string.Empty;
    public DateTime RecordedAt { get; set; }
    public DateTime? ProcessStartedUtc { get; set; }
    public DateTime? DeviceStartedUtc { get; set; }
    public DateTime? MicStartedUtc { get; set; }
}

public sealed record OrphanRecoveryResult(IReadOnlyList<FailedRecording> Recovered, IReadOnlyList<string> Failed, int Deferred = 0);

internal static class OrphanRecordingRecovery
{
    public const string SessionFileSuffix = ".session.json";

    private const string MicSuffix = "_mic";
    public const string DerivedSuffix = ".16k.wav";

    public static OrphanRecoveryResult Run(string tempFolder, FailedRecordingStore store, UserMessage reason,
        Func<string, bool> isOwned, double signalThreshold)
    {
        var recovered = new List<FailedRecording>();
        var failed = new List<string>();
        var deferred = 0;

        if (!Directory.Exists(tempFolder))
        {
            return new OrphanRecoveryResult(recovered, failed);
        }

        string[] sessionFiles;
        try
        {
            sessionFiles = RecordingNames.FilePrefixes
                .SelectMany(prefix => Directory.GetFiles(tempFolder, prefix + "*" + SessionFileSuffix))
                .ToArray();
        }
        catch (Exception ex) when (ExpectedFailures.IsFile(ex))
        {
            AppLog.Warn("OrphanRecordingRecovery: could not read the temp folder", ex);
            failed.Add(tempFolder);
            return new OrphanRecoveryResult(recovered, failed);
        }

        var orphans = sessionFiles
            .Select(sessionFile => (BaseName: Path.GetFileName(sessionFile)[..^SessionFileSuffix.Length], SessionFile: (string?)sessionFile))
            .Concat(FindTracksWithoutSession(tempFolder, sessionFiles).Select(baseName => (BaseName: baseName, SessionFile: (string?)null)))
            .ToList();

        foreach (var (baseName, sessionFile) in orphans)
        {
            if (isOwned(baseName))
            {
                continue;
            }

            try
            {
                var wavs = Directory.GetFiles(tempFolder, baseName + "*.wav");
                if (wavs.Any(WavRepair.IsInUse))
                {
                    AppLog.Info($"OrphanRecordingRecovery: \"{baseName}\" has files in use; leaving it for later");
                    deferred++;
                    continue;
                }

                var saved = RecoverSession(tempFolder, baseName, sessionFile, wavs, store, reason, signalThreshold);
                if (saved is not null)
                {
                    recovered.Add(saved);
                }
            }
            catch (Exception ex) when (ex is JsonException || ExpectedFailures.IsAudioFile(ex))
            {
                AppLog.Warn($"OrphanRecordingRecovery: could not recover recording \"{baseName}\"", ex);
                failed.Add(Path.Combine(tempFolder, baseName));
            }
        }

        return new OrphanRecoveryResult(recovered, failed, deferred);
    }

    private static readonly Regex TrackBaseNamePattern =
        new(@"^((?:recording|grabacion)_(\d{8}_\d{6}))(_[^.]+)?\.wav$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static List<string> FindTracksWithoutSession(string tempFolder, string[] sessionFiles)
    {
        var withSession = new HashSet<string>(
            sessionFiles.Select(f => Path.GetFileName(f)[..^SessionFileSuffix.Length]), StringComparer.OrdinalIgnoreCase);

        return RecordingNames.FilePrefixes
            .SelectMany(prefix => Directory.GetFiles(tempFolder, prefix + "*.wav"))
            .Select(path => TrackBaseNamePattern.Match(Path.GetFileName(path)))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .Where(baseName => !withSession.Contains(baseName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static CaptureSessionInfo ReadSessionInfo(string baseName, string? sessionFile)
    {
        if (sessionFile is not null)
        {
            return JsonSerializer.Deserialize<CaptureSessionInfo>(File.ReadAllText(sessionFile))
                   ?? throw new InvalidDataException($"Empty session file: \"{sessionFile}\"");
        }

        AppLog.Info($"OrphanRecordingRecovery: \"{baseName}\" has no session file; recovering it with the date from its name");
        var match = TrackBaseNamePattern.Match(baseName + ".wav");
        var recordedAt = DateTime.TryParseExact(match.Groups[2].Value, "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed)
            ? parsed
            : DateTime.Now;

        return new CaptureSessionInfo { AppName = RecordingNames.DefaultAppName, RecordedAt = recordedAt };
    }

    private static FailedRecording? RecoverSession(string tempFolder, string baseName, string? sessionFile,
        string[] wavs, FailedRecordingStore store, UserMessage reason, double signalThreshold)
    {
        var info = ReadSessionInfo(baseName, sessionFile);

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
            AppLog.Info($"OrphanRecordingRecovery: \"{baseName}\" has no audio; discarding it");
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
            throw new IOException($"Could not move \"{baseName}\" to the failed recordings folder; it will be retried on next startup");
        }

        var leftovers = wavs.Where(p => p != source && p != mic);
        DeleteAll(leftovers, sessionFile);

        AppLog.Info($"OrphanRecordingRecovery: recovered \"{baseName}\" ({info.AppName}) from \"{Path.GetFileName(source)}\"");
        return saved;
    }

    private static bool NameIs(string path, string expected) =>
        string.Equals(Path.GetFileNameWithoutExtension(path), expected, StringComparison.OrdinalIgnoreCase);

    private static void DeleteAll(IEnumerable<string> paths, string? sessionFile)
    {
        foreach (var path in paths)
        {
            FileCleanup.TryDelete(path, "OrphanRecordingRecovery");
        }

        FileCleanup.TryDelete(sessionFile, "OrphanRecordingRecovery");
    }
}
