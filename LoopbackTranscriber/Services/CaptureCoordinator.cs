using System.IO;
using LoopbackTranscriber.Models;

namespace LoopbackTranscriber.Services;

public interface IProcessCapture : IDisposable
{
    event Action<byte[], int>? AudioCaptured;

    Task StartAsync(uint processId, string outputWavPath, bool includeProcessTree = true);

    Task<string> StopAsync();
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
    IReadOnlyList<string> AllWavPaths, Exception? ProcessStopError);

public sealed record PreserveOutcome(FailedRecording? Saved, bool KeepOriginals, string? TempFolder);

public sealed class CaptureCoordinator : IDisposable
{
    public const double DeviceSignalThreshold = 0.01;

    private readonly IProcessCapture _process;
    private readonly IDeviceCapture _device;
    private readonly IMicCapture _mic;
    private readonly Func<DateTime> _utcNow;
    private readonly string _tempFolder;

    private DateTime? _processStartedAt;
    private DateTime? _deviceStartedAt;
    private DateTime? _micStartedAt;

    public CaptureCoordinator(IProcessCapture process, IDeviceCapture device, IMicCapture mic,
        Func<DateTime>? utcNow = null, string? tempFolder = null)
    {
        _process = process;
        _device = device;
        _mic = mic;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _tempFolder = tempFolder ?? Path.Combine(Path.GetTempPath(), "LoopbackTranscriber");

        _process.AudioCaptured += (buffer, count) => AudioCaptured?.Invoke(buffer, count);
    }

    public event Action<byte[], int>? AudioCaptured;

    public async Task StartAsync(uint processId)
    {
        _processStartedAt = null;
        _deviceStartedAt = null;
        _micStartedAt = null;

        Directory.CreateDirectory(_tempFolder);
        var baseName = $"grabacion_{DateTime.Now:yyyyMMdd_HHmmss}";
        var wavPath = Path.Combine(_tempFolder, baseName + ".wav");

        await _process.StartAsync(processId, wavPath);
        _processStartedAt = _utcNow();

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

        var bestDeviceTrack = deviceTracks
            .Where(t => t.PeakAbs >= DeviceSignalThreshold)
            .OrderByDescending(t => t.PeakAbs)
            .FirstOrDefault();

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

        return new StoppedRecording(sourceWavPath, micWavPath, micStartOffset,
            allPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), processStopError);
    }

    public PreserveOutcome PreserveFailed(StoppedRecording? stopped, string appName, DateTime recordedAt, string error)
    {
        if (stopped is null)
        {
            return new PreserveOutcome(null, KeepOriginals: true, TempFolder: null);
        }

        var preserveSource = stopped.SourceWavPath ?? stopped.MicWavPath;
        var preserveMic = stopped.SourceWavPath is null ? null : stopped.MicWavPath;

        var saved = FailedRecordingStore.TryPreserve(preserveSource, preserveMic, stopped.MicStartOffset,
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
            foreach (var file in Directory.EnumerateFiles(_tempFolder, "grabacion_*.wav"))
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
