using System.IO;
using NAudio.CoreAudioApi;
using System.Runtime.InteropServices;
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

    public long? FirstSampleTimestamp { get; private set; }

    public bool Start(string wavPath)
    {
        if (_isRecording)
        {
            throw UserError.InvalidOperation("ErrorRecordingAlreadyRunning");
        }

        WasapiCapture? capture = null;
        WaveFileWriter? writer = null;

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var micDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);

            capture = new WasapiCapture(micDevice);
            writer = new WaveFileWriter(wavPath, capture.WaveFormat);

            var stoppedEvent = new ManualResetEventSlim(false);
            _capture = capture;
            _writer = writer;
            _wavPath = wavPath;
            _peakAbs = 0;
            _timeline = new CaptureTimeline(capture.WaveFormat.SampleRate);
            FirstSampleTimestamp = null;
            _stoppedEvent = stoppedEvent;

            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += (_, _) => stoppedEvent.Set();

            capture.StartRecording();
            _isRecording = true;
            return true;
        }
        catch (Exception ex) when (ExpectedFailures.IsDeviceOrFile(ex))
        {
            AppLog.Warn("MicrophoneRecorder.Start: could not start the microphone; recording without it", ex);
            ReleaseFailedStart(capture, writer, wavPath);
            return false;
        }
        catch
        {
            ReleaseFailedStart(capture, writer, wavPath);
            throw;
        }
    }

    private void ReleaseFailedStart(WasapiCapture? capture, WaveFileWriter? writer, string wavPath)
    {
        writer?.Dispose();
        capture?.Dispose();
        _stoppedEvent?.Dispose();

        if (writer is not null)
        {
            FileCleanup.TryDelete(wavPath, "MicrophoneRecorder.Start");
        }

        _capture = null;
        _writer = null;
        _wavPath = null;
        _timeline = null;
        _stoppedEvent = null;
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

        var wavPath = _wavPath;
        try
        {
            try
            {
                _capture.StopRecording();
            }
            catch (COMException ex)
            {
                AppLog.Warn("MicrophoneRecorder.Stop: failed to stop microphone capture", ex);
            }

            _stoppedEvent.Wait(TimeSpan.FromSeconds(3));

            lock (_writeLock)
            {
                _writer.Flush();
            }
        }
        finally
        {
            Release();
        }

        if (_timeline is not null)
        {
            FirstSampleTimestamp = _timeline.FirstSampleTimestamp;
            if (_timeline.PaddedFrames > 0)
            {
                AppLog.Info($"MicrophoneRecorder: gaps filled with silence = {_timeline.PaddedDuration.TotalSeconds:0.0}s");
            }
        }

        _timeline = null;

        if (_peakAbs <= SignalThreshold)
        {
            FileCleanup.TryDelete(wavPath, "MicrophoneRecorder.Stop");
            return null;
        }

        return wavPath;
    }

    private void Release()
    {
        lock (_writeLock)
        {
            try
            {
                _writer?.Dispose();
            }
            catch (Exception ex) when (ExpectedFailures.IsFile(ex))
            {
                AppLog.Warn("MicrophoneRecorder: failed to close the microphone WAV", ex);
            }
        }

        try
        {
            _capture?.Dispose();
        }
        catch (COMException ex)
        {
            AppLog.Warn("MicrophoneRecorder: failed to release the microphone capture", ex);
        }

        _stoppedEvent?.Dispose();

        _capture = null;
        _writer = null;
        _wavPath = null;
        _stoppedEvent = null;
        _isRecording = false;
    }

    public void Dispose()
    {
        Release();
        _timeline = null;
    }
}
