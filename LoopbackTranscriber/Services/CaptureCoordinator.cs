using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using LoopbackTranscriber.Models;

namespace LoopbackTranscriber.Services;

public interface IProcessCapture : IDisposable
{
    event Action<byte[], int>? AudioCaptured;

    Task StartAsync(uint processId, string outputWavPath, bool includeProcessTree = true);

    Task<string> StopAsync();

    double LastPeakAbs { get; }

    long? FirstSampleTimestamp { get; }
}

public interface IDeviceCapture : IDisposable
{
    void Start(string outputDirectory, string baseFileName);

    List<DeviceLoopbackRecorder.DeviceTrack> Stop();
}

public interface IMicCapture : IDisposable
{
    bool Start(string wavPath);

    string? Stop();

    long? FirstSampleTimestamp { get; }
}

public sealed record StoppedRecording(string? SourceWavPath, string? MicWavPath, TimeSpan MicStartOffset,
    IReadOnlyList<string> AllWavPaths, Exception? ProcessStopError, string? BaseName = null,
    IReadOnlyList<UserMessage>? Warnings = null);

public sealed record PreserveOutcome(FailedRecording? Saved, bool KeepOriginals, string? TempFolder);

public sealed class CaptureCoordinator : IDisposable
{
    public const double DeviceSignalThreshold = 0.01;

    private readonly IProcessCapture _process;
    private readonly IDeviceCapture _device;
    private readonly IMicCapture _mic;
    private readonly Func<DateTime> _utcNow;
    private readonly string _tempFolder;
    private readonly FailedRecordingStore _failedStore;
    private readonly ConcurrentDictionary<string, byte> _ownedBaseNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<UserMessage> _warnings = new();

    private string? _currentBaseName;
    private DateTime? _processStartedAt;
    private DateTime? _deviceStartedAt;
    private DateTime? _micStartedAt;

    public CaptureCoordinator(IProcessCapture process, IDeviceCapture device, IMicCapture mic,
        Func<DateTime>? utcNow = null, string? tempFolder = null, FailedRecordingStore? failedStore = null)
    {
        _process = process;
        _device = device;
        _mic = mic;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _tempFolder = tempFolder ?? Path.Combine(Path.GetTempPath(), "LoopbackTranscriber");
        _failedStore = failedStore ?? new FailedRecordingStore(Path.Combine(_tempFolder, "failed"));

        _process.AudioCaptured += (buffer, count) => AudioCaptured?.Invoke(buffer, count);
    }

    public event Action<byte[], int>? AudioCaptured;

    public async Task<IReadOnlyList<UserMessage>> StartAsync(uint processId, string? appName = null)
    {
        _warnings.Clear();
        _processStartedAt = null;
        _deviceStartedAt = null;
        _micStartedAt = null;

        Directory.CreateDirectory(_tempFolder);
        var recordedAt = DateTime.Now;
        var baseName = $"{RecordingNames.FilePrefix}{recordedAt:yyyyMMdd_HHmmss}";
        var wavPath = Path.Combine(_tempFolder, baseName + ".wav");

        _ownedBaseNames[baseName] = 0;
        try
        {
            await _process.StartAsync(processId, wavPath);
        }
        catch
        {
            _ownedBaseNames.TryRemove(baseName, out _);
            throw;
        }

        _processStartedAt = _utcNow();
        _currentBaseName = baseName;

        var session = new CaptureSessionInfo
        {
            AppName = appName ?? RecordingNames.DefaultAppName,
            RecordedAt = recordedAt,
            ProcessStartedUtc = _processStartedAt
        };
        if (!WriteSessionInfo(baseName, session))
        {
            AddWarning("WarningRecoveryFileNotWritten");
        }

        try
        {
            _device.Start(_tempFolder, baseName);
            _deviceStartedAt = _utcNow();
        }
        catch (Exception ex)
        {
            AppLog.Warn("CaptureCoordinator.StartAsync: failed to start device capture; continuing with process capture only", ex);
        }

        try
        {
            if (_mic.Start(Path.Combine(_tempFolder, baseName + "_mic.wav")))
            {
                _micStartedAt = _utcNow();
            }
            else
            {
                AddWarning(MicNotRecordedKey);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("CaptureCoordinator.StartAsync: failed to start microphone capture; recording without it", ex);
            AddWarning(MicNotRecordedKey);
        }

        session.DeviceStartedUtc = _deviceStartedAt;
        session.MicStartedUtc = _micStartedAt;
        WriteSessionInfo(baseName, session);

        return _warnings.ToList();
    }

    private const string MicNotRecordedKey = "WarningMicNotRecorded";

    private void AddWarning(string key)
    {
        if (!_warnings.Any(w => w.Key == key))
        {
            _warnings.Add(UserMessage.FromKey(key));
        }
    }

    public OrphanRecoveryResult RecoverOrphans(UserMessage reason) =>
        OrphanRecordingRecovery.Run(_tempFolder, _failedStore, reason,
            baseName => _ownedBaseNames.ContainsKey(baseName), DeviceSignalThreshold);

    private string SessionFilePath(string baseName) =>
        Path.Combine(_tempFolder, baseName + OrphanRecordingRecovery.SessionFileSuffix);

    private bool WriteSessionInfo(string baseName, CaptureSessionInfo session)
    {
        try
        {
            File.WriteAllText(SessionFilePath(baseName), JsonSerializer.Serialize(session));
            return true;
        }
        catch (Exception ex) when (ExpectedFailures.IsFile(ex))
        {
            AppLog.Warn("CaptureCoordinator: could not write the session file; the recording cannot be recovered if the app crashes", ex);
            return false;
        }
    }

    public async Task<StoppedRecording> StopAsync()
    {
        string? rawWavPath = null;
        Exception? processStopError = null;

        try
        {
            rawWavPath = await _process.StopAsync();
        }
        catch (Exception ex)
        {
            AppLog.Warn("CaptureCoordinator.StopAsync: failed to stop process capture; trying to continue with the other tracks", ex);
            processStopError = ex;
        }

        List<DeviceLoopbackRecorder.DeviceTrack> deviceTracks;
        try
        {
            deviceTracks = await Task.Run(() => _device.Stop());
        }
        catch (Exception ex)
        {
            AppLog.Warn("CaptureCoordinator.StopAsync: failed to stop device capture; using process capture only", ex);
            deviceTracks = new List<DeviceLoopbackRecorder.DeviceTrack>();
        }

        string? micWavPath;
        try
        {
            micWavPath = await Task.Run(() => _mic.Stop());
        }
        catch (Exception ex)
        {
            AppLog.Warn("CaptureCoordinator.StopAsync: failed to stop microphone capture; the transcript will only have the participants' audio", ex);
            micWavPath = null;
            AddWarning(MicNotRecordedKey);
        }

        var processHasSignal = rawWavPath is not null && _process.LastPeakAbs >= DeviceSignalThreshold;

        var bestDeviceTrack = processHasSignal
            ? null
            : deviceTracks
                .Where(t => t.PeakAbs >= DeviceSignalThreshold)
                .OrderByDescending(t => t.PeakAbs)
                .FirstOrDefault();

        if (bestDeviceTrack is not null)
        {
            AppLog.Info($"CaptureCoordinator.StopAsync: the process track has no signal; falling back to device track \"{bestDeviceTrack.Label}\"");
        }

        var sourceWavPath = bestDeviceTrack is not null ? bestDeviceTrack.WavPath : rawWavPath;

        var micStartOffset = TimeSpan.Zero;
        var sourceFirstSample = bestDeviceTrack is not null ? bestDeviceTrack.FirstSampleTimestamp : _process.FirstSampleTimestamp;
        var micFirstSample = _mic.FirstSampleTimestamp;
        var otherTrackStartedAt = bestDeviceTrack is not null ? _deviceStartedAt : _processStartedAt;
        if (micWavPath is not null && sourceFirstSample is not null && micFirstSample is not null)
        {
            micStartOffset = TimeSpan.FromTicks(micFirstSample.Value - sourceFirstSample.Value);
        }
        else if (micWavPath is not null && otherTrackStartedAt is not null && _micStartedAt is not null)
        {
            micStartOffset = _micStartedAt.Value - otherTrackStartedAt.Value;
        }

        var allPaths = new List<string>();
        if (rawWavPath is not null)
        {
            allPaths.Add(rawWavPath);
        }

        if (micWavPath is not null)
        {
            allPaths.Add(micWavPath);
        }

        allPaths.AddRange(deviceTracks.Select(t => t.WavPath));

        var baseName = _currentBaseName;
        _currentBaseName = null;

        return new StoppedRecording(sourceWavPath, micWavPath, micStartOffset,
            allPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), processStopError, baseName, _warnings.ToList());
    }

    public PreserveOutcome PreserveFailed(StoppedRecording? stopped, string appName, DateTime recordedAt, UserMessage error)
    {
        if (stopped is null)
        {
            return new PreserveOutcome(null, KeepOriginals: true, TempFolder: null);
        }

        var preserveSource = stopped.SourceWavPath ?? stopped.MicWavPath;
        var preserveMic = stopped.SourceWavPath is null ? null : stopped.MicWavPath;

        var saved = _failedStore.TryPreserve(preserveSource, preserveMic, stopped.MicStartOffset,
            appName, recordedAt, error);
        if (saved is not null)
        {
            return new PreserveOutcome(saved, KeepOriginals: false, TempFolder: null);
        }

        return new PreserveOutcome(null, KeepOriginals: true, TempFolder: stopped.AllWavPaths.Count > 0 ? Path.GetDirectoryName(stopped.AllWavPaths[0]) : null);
    }

    public void DeleteOriginals(StoppedRecording? stopped)
    {
        if (stopped is not null)
        {
            foreach (var path in stopped.AllWavPaths)
            {
                FileCleanup.TryDelete(path, "CaptureCoordinator.DeleteOriginals");
            }

            FileCleanup.TryDelete(stopped.SourceWavPath, "CaptureCoordinator.DeleteOriginals");

            if (stopped.BaseName is not null)
            {
                FileCleanup.TryDelete(SessionFilePath(stopped.BaseName), "CaptureCoordinator.DeleteOriginals");
                _ownedBaseNames.TryRemove(stopped.BaseName, out _);
            }
        }

        DeleteStaleRecordings();
    }

    public void DeleteStaleRecordings()
    {
        try
        {
            if (!Directory.Exists(_tempFolder))
            {
                return;
            }

            var limit = DateTime.UtcNow.AddDays(-7);
            var staleFiles = RecordingNames.FilePrefixes
                .SelectMany(prefix => Directory.EnumerateFiles(_tempFolder, prefix + "*" + OrphanRecordingRecovery.DerivedSuffix));
            foreach (var file in staleFiles)
            {
                if (File.GetLastWriteTimeUtc(file) < limit)
                {
                    FileCleanup.TryDelete(file, "CaptureCoordinator.DeleteStaleRecordings");
                }
            }
        }
        catch (Exception ex) when (ExpectedFailures.IsFile(ex))
        {
            AppLog.Warn("CaptureCoordinator.DeleteStaleRecordings: could not clean the temp folder", ex);
        }
    }

    public void Dispose()
    {
        _process.Dispose();
        _device.Dispose();
        _mic.Dispose();
    }
}
