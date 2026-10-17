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
    private string? _wavPath;
    private double _peakAbs;
    private bool _isRecording;

    private const double SignalThreshold = 0.01;

    public bool IsAvailable { get; private set; } = true;

    public string? LastUsedDeviceName { get; private set; }

    public void Start(string wavPath)
    {
        if (_isRecording)
        {
            throw new InvalidOperationException(Strings.Instance.Get("ErrorRecordingAlreadyRunning"));
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
            AppLog.Warn("MicrophoneRecorder.Start: no se pudo obtener el microfono por defecto, se omite la pista de microfono", ex);
            return;
        }

        try
        {
            LastUsedDeviceName = micDevice.FriendlyName;
        }
        catch (Exception ex)
        {
            LastUsedDeviceName = null;
            AppLog.Warn("MicrophoneRecorder.Start: no se pudo leer el nombre del dispositivo de microfono", ex);
        }

        WasapiCapture capture;
        try
        {
            capture = new WasapiCapture(micDevice);
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            AppLog.Warn($"MicrophoneRecorder.Start: no se pudo abrir captura WASAPI para \"{micDevice.FriendlyName}\"", ex);
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
            AppLog.Warn($"MicrophoneRecorder.Start: no se pudo crear el fichero WAV \"{wavPath}\"", ex);
            capture.Dispose();
            return;
        }

        _capture = capture;
        _writer = writer;
        _wavPath = wavPath;
        _peakAbs = 0;
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
            AppLog.Warn("MicrophoneRecorder.Start: no se pudo iniciar la grabacion del microfono", ex);
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
        lock (_writeLock)
        {
            _writer?.Write(e.Buffer, 0, e.BytesRecorded);
        }

        var format = _capture?.WaveFormat;
        if (format is null)
        {
            return;
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
            AppLog.Warn("MicrophoneRecorder.Stop: fallo al parar la captura del microfono", ex);
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
                AppLog.Warn("MicrophoneRecorder.Stop: fallo al volcar el WAV del microfono", ex);
            }

            _writer.Dispose();
        }

        _capture.Dispose();
        _stoppedEvent.Dispose();

        var wavPath = _wavPath;
        var hasSignal = _peakAbs > SignalThreshold;

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
            AppLog.Warn($"MicrophoneRecorder: no se pudo borrar \"{path}\"", ex);
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
                AppLog.Warn("MicrophoneRecorder.Dispose: fallo al cerrar el WAV del microfono", ex);
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
                AppLog.Warn("MicrophoneRecorder.Dispose: fallo al liberar la captura del microfono", ex);
            }
        }

        _stoppedEvent?.Dispose();

        if (_wavPath is not null)
        {
            TryDeleteFile(_wavPath);
        }

        _capture = null;
        _writer = null;
        _wavPath = null;
        _stoppedEvent = null;
        _isRecording = false;
    }
}
