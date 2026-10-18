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
}

public interface IDeviceCapture : IDisposable
{
    void Start(string outputDirectory, string baseFileName);

    List<DeviceLoopbackRecorder.DeviceTrack> Stop();
}

public interface IMicCapture : IDisposable
{
    void Start(string wavPath);

    string? Stop();
}

public sealed record StoppedRecording(string? SourceWavPath, string? MicWavPath, TimeSpan MicStartOffset,
    IReadOnlyList<string> AllWavPaths, Exception? ProcessStopError, string? BaseName = null);

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
        _failedStore = failedStore ?? FailedRecordingStore.Default;

        _process.AudioCaptured += (buffer, count) => AudioCaptured?.Invoke(buffer, count);
    }

    public event Action<byte[], int>? AudioCaptured;

    public async Task StartAsync(uint processId, string? appName = null)
    {
        _processStartedAt = null;
        _deviceStartedAt = null;
        _micStartedAt = null;

        Directory.CreateDirectory(_tempFolder);
        var recordedAt = DateTime.Now;
        var baseName = $"grabacion_{recordedAt:yyyyMMdd_HHmmss}";
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
            AppName = appName ?? "reunion",
            RecordedAt = recordedAt,
            ProcessStartedUtc = _processStartedAt
        };
        WriteSessionInfo(baseName, session);

        try
        {
            _device.Start(_tempFolder, baseName);
            _deviceStartedAt = _utcNow();
        }
        catch (Exception ex)
        {
            AppLog.Warn("CaptureCoordinator.StartAsync: fallo al iniciar la captura por dispositivo, se continua solo con la captura por proceso", ex);
        }

        try
        {
            _mic.Start(Path.Combine(_tempFolder, baseName + "_mic.wav"));
            _micStartedAt = _utcNow();
        }
        catch (Exception ex)
        {
            AppLog.Warn("CaptureCoordinator.StartAsync: fallo al iniciar la captura del microfono, el resumen solo identificara al interlocutor", ex);
        }

        session.DeviceStartedUtc = _deviceStartedAt;
        session.MicStartedUtc = _micStartedAt;
        WriteSessionInfo(baseName, session);
    }

    public IReadOnlyList<FailedRecording> RecoverOrphans(string reason) =>
        OrphanRecordingRecovery.Run(_tempFolder, _failedStore, reason,
            baseName => _ownedBaseNames.ContainsKey(baseName), DeviceSignalThreshold);

    private string SessionFilePath(string baseName) =>
        Path.Combine(_tempFolder, baseName + OrphanRecordingRecovery.SessionFileSuffix);

    private void WriteSessionInfo(string baseName, CaptureSessionInfo session)
    {
        try
        {
            File.WriteAllText(SessionFilePath(baseName), JsonSerializer.Serialize(session));
        }
        catch (Exception ex)
        {
            AppLog.Warn("CaptureCoordinator: no se pudo escribir el fichero de sesion; la grabacion no se podra recuperar si la app se cierra de golpe", ex);
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
            AppLog.Warn("CaptureCoordinator.StopAsync: fallo al parar la captura por proceso, se intenta seguir con las demas pistas", ex);
            processStopError = ex;
        }

        List<DeviceLoopbackRecorder.DeviceTrack> deviceTracks;
        try
        {
            deviceTracks = await Task.Run(() => _device.Stop());
        }
        catch (Exception ex)
        {
            AppLog.Warn("CaptureCoordinator.StopAsync: fallo al parar la captura por dispositivo, se usa solo la captura por proceso", ex);
            deviceTracks = new List<DeviceLoopbackRecorder.DeviceTrack>();
        }

        string? micWavPath;
        try
        {
            micWavPath = await Task.Run(() => _mic.Stop());
        }
        catch (Exception ex)
        {
            AppLog.Warn("CaptureCoordinator.StopAsync: fallo al parar la captura del microfono, el resumen solo tendra el audio del interlocutor", ex);
            micWavPath = null;
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
            AppLog.Info($"CaptureCoordinator.StopAsync: la pista del proceso no tiene senal; se usa la del dispositivo \"{bestDeviceTrack.Label}\" como respaldo");
        }

        var sourceWavPath = bestDeviceTrack is not null ? bestDeviceTrack.WavPath : rawWavPath;

        var micStartOffset = TimeSpan.Zero;
        var otherTrackStartedAt = bestDeviceTrack is not null ? _deviceStartedAt : _processStartedAt;
        if (micWavPath is not null && otherTrackStartedAt is not null && _micStartedAt is not null)
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
            allPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), processStopError, baseName);
    }

    public PreserveOutcome PreserveFailed(StoppedRecording? stopped, string appName, DateTime recordedAt, string error)
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

        return new PreserveOutcome(null, KeepOriginals: true, TempFolder: Path.GetDirectoryName(stopped.AllWavPaths.FirstOrDefault()));
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
            var staleFiles = Directory.EnumerateFiles(_tempFolder, "grabacion_*.wav")
                .Concat(Directory.EnumerateFiles(_tempFolder, "grabacion_*" + OrphanRecordingRecovery.SessionFileSuffix));
            foreach (var file in staleFiles)
            {
                if (File.GetLastWriteTimeUtc(file) < limit)
                {
                    FileCleanup.TryDelete(file, "CaptureCoordinator.DeleteStaleRecordings");
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("CaptureCoordinator.DeleteStaleRecordings: no se pudo limpiar la carpeta temporal", ex);
        }
    }

    public void Dispose()
    {
        _process.Dispose();
        _device.Dispose();
        _mic.Dispose();
    }
}
