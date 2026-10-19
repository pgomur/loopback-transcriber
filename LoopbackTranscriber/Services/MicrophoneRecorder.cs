using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace LoopbackTranscriber.Services;

public sealed class MicrophoneRecorder : IMicCapture
{
    private WasapiCapture? _capture;
    private WaveFileWriter? _writer;
    private readonly object _writeLock = new();
    private ManualResetEventSlim? _stoppedEvent;
    private CaptureTimeline? _timeline;
    private string? _wavPath;
    private double _peakAbs;
    private bool _isRecording;

    private const double SignalThreshold = 0.01;

    public bool IsAvailable { get; private set; } = true;

    public string? LastUsedDeviceName { get; private set; }

    public long? FirstSampleTimestamp { get; private set; }

    public void Start(string wavPath)
    {
        if (_isRecording)
        {
            throw UserError.InvalidOperation("ErrorRecordingAlreadyRunning");
        }

        MMDevice micDevice;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            micDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            AppLog.Warn("MicrophoneRecorder.Start: could not get the default microphone; skipping the microphone track", ex);
            return;
        }

        try
        {
            LastUsedDeviceName = micDevice.FriendlyName;
        }
        catch (Exception ex)
        {
            LastUsedDeviceName = null;
            AppLog.Warn("MicrophoneRecorder.Start: could not read the microphone device name", ex);
        }

        WasapiCapture capture;
        try
        {
            capture = new WasapiCapture(micDevice);
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            AppLog.Warn($"MicrophoneRecorder.Start: could not open WASAPI capture for \"{micDevice.FriendlyName}\"", ex);
            return;
        }

        WaveFileWriter writer;
        try
        {
            writer = new WaveFileWriter(wavPath, capture.WaveFormat);
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            AppLog.Warn($"MicrophoneRecorder.Start: could not create WAV file \"{wavPath}\"", ex);
            capture.Dispose();
            return;
        }

        _capture = capture;
        _writer = writer;
        _wavPath = wavPath;
        _peakAbs = 0;
        _timeline = new CaptureTimeline(capture.WaveFormat.SampleRate);
        FirstSampleTimestamp = null;
        _stoppedEvent = new ManualResetEventSlim(false);

        capture.DataAvailable += OnDataAvailable;
        capture.RecordingStopped += (_, _) => _stoppedEvent.Set();

        try
        {
            capture.StartRecording();
            _isRecording = true;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            AppLog.Warn("MicrophoneRecorder.Start: could not start microphone recording", ex);
            writer.Dispose();
            capture.Dispose();
            TryDeleteFile(wavPath);
            _capture = null;
            _writer = null;
            _wavPath = null;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var arrival = CaptureTimeline.Now();
        var format = _capture?.WaveFormat;
        if (format is null)
        {
            return;
        }

        lock (_writeLock)
        {
            if (_writer is not null && _timeline is not null && e.BytesRecorded > 0)
            {
                var blockAlign = Math.Max(1, format.BlockAlign);
                var paddingFrames = _timeline.PaddingBeforeArrival(arrival, e.BytesRecorded / blockAlign);
                if (paddingFrames > 0)
                {
                    CaptureTimeline.WriteSilence(_writer, paddingFrames * blockAlign);
                }

                _writer.Write(e.Buffer, 0, e.BytesRecorded);
            }
        }

        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            for (var i = 0; i + 3 < e.BytesRecorded; i += 4)
            {
                var sample = BitConverter.ToSingle(e.Buffer, i);
                var abs = Math.Abs(sample);
                if (abs > _peakAbs)
                {
                    _peakAbs = abs;
                }
            }
        }
        else if (format.BitsPerSample == 16)
        {
            for (var i = 0; i + 1 < e.BytesRecorded; i += 2)
            {
                var sample = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
                var abs = Math.Abs((double)sample) / 32768.0;
                if (abs > _peakAbs)
                {
                    _peakAbs = abs;
                }
            }
        }
    }

    public string? Stop()
    {
        if (!_isRecording || _capture is null || _writer is null || _wavPath is null || _stoppedEvent is null)
        {
            return null;
        }

        try
        {
            _capture.StopRecording();
        }
        catch (Exception ex)
        {
            AppLog.Warn("MicrophoneRecorder.Stop: failed to stop microphone capture", ex);
        }

        _stoppedEvent.Wait(TimeSpan.FromSeconds(3));

        lock (_writeLock)
        {
            try
            {
                _writer.Flush();
            }
            catch (Exception ex)
            {
                AppLog.Warn("MicrophoneRecorder.Stop: failed to flush the microphone WAV", ex);
            }

            _writer.Dispose();
        }

        _capture.Dispose();
        _stoppedEvent.Dispose();

        var wavPath = _wavPath;
        var hasSignal = _peakAbs > SignalThreshold;

        if (_timeline is not null)
        {
            FirstSampleTimestamp = _timeline.FirstSampleTimestamp;
            if (_timeline.PaddedFrames > 0)
            {
                AppLog.Info($"MicrophoneRecorder: gaps filled with silence = {_timeline.PaddedDuration.TotalSeconds:0.0}s");
            }
        }

        _timeline = null;

        _capture = null;
        _writer = null;
        _wavPath = null;
        _stoppedEvent = null;
        _isRecording = false;

        if (!hasSignal)
        {
            TryDeleteFile(wavPath);
            return null;
        }

        return wavPath;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"MicrophoneRecorder: could not delete \"{path}\"", ex);
        }
    }

    public void Dispose()
    {
        if (_writer is not null)
        {
            try
            {
                _writer.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Warn("MicrophoneRecorder.Dispose: failed to close the microphone WAV", ex);
            }
        }

        if (_capture is not null)
        {
            try
            {
                _capture.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Warn("MicrophoneRecorder.Dispose: failed to release microphone capture", ex);
            }
        }

        _stoppedEvent?.Dispose();

        _capture = null;
        _writer = null;
        _wavPath = null;
        _stoppedEvent = null;
        _isRecording = false;
    }
}
