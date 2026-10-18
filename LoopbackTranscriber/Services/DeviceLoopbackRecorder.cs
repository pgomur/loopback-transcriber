using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace LoopbackTranscriber.Services;
public sealed class DeviceLoopbackRecorder : IDeviceCapture
{
    public sealed class DeviceTrack
    {
        public required string Label;
        public required string WavPath;
        public double PeakAbs;
        public long BytesCaptured;
    }

    private sealed class Channel
    {
        public required string Label;
        public required WasapiLoopbackCapture Capture;
        public required WaveFileWriter Writer;
        public required string WavPath;
        public readonly object WriteLock = new();
        public readonly ManualResetEventSlim StoppedEvent = new(false);
        public double PeakAbs;
        public long BytesCaptured;
    }

    private readonly List<Channel> _channels = new();

    public void Start(string outputDirectory, string baseFileName)
    {
        _channels.Clear();
        Directory.CreateDirectory(outputDirectory);

        MMDeviceEnumerator enumerator;
        try
        {
            enumerator = new MMDeviceEnumerator();
        }
        catch (Exception ex)
        {
            AppLog.Warn("DeviceLoopbackRecorder.Start: no se pudo crear MMDeviceEnumerator, se omite la captura por dispositivo", ex);
            return;
        }

        var seenDeviceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (roleLabel, role) in new (string, Role)[]
                 {
                     ("consola", Role.Console),
                     ("comunicaciones", Role.Communications)
                 })
        {
            MMDevice? device;
            try
            {
                device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, role);
            }
            catch (Exception ex)
            {
                AppLog.Warn($"DeviceLoopbackRecorder.Start: no se pudo obtener el dispositivo de salida por defecto ({roleLabel})", ex);
                continue;
            }

            if (device is null || !seenDeviceIds.Add(device.ID))
            {
                continue;
            }

            WasapiLoopbackCapture capture;
            try
            {
                capture = new WasapiLoopbackCapture(device);
            }
            catch (Exception ex)
            {
                AppLog.Warn($"DeviceLoopbackRecorder.Start: no se pudo abrir captura WASAPI para \"{device.FriendlyName}\" ({roleLabel})", ex);
                continue;
            }

            var wavPath = Path.Combine(outputDirectory, $"{baseFileName}_{roleLabel}.wav");

            WaveFileWriter writer;
            try
            {
                writer = new WaveFileWriter(wavPath, capture.WaveFormat);
            }
            catch (Exception ex)
            {
                AppLog.Warn($"DeviceLoopbackRecorder.Start: no se pudo crear el fichero WAV \"{wavPath}\"", ex);
                capture.Dispose();
                continue;
            }

            var channel = new Channel
            {
                Label = $"{roleLabel} ({device.FriendlyName})",
                Capture = capture,
                Writer = writer,
                WavPath = wavPath
            };

            capture.DataAvailable += (_, e) => OnDataAvailable(channel, e);
            capture.RecordingStopped += (_, _) => channel.StoppedEvent.Set();

            try
            {
                capture.StartRecording();
                _channels.Add(channel);
            }
            catch (Exception ex)
            {
                AppLog.Warn($"DeviceLoopbackRecorder.Start: no se pudo iniciar la captura para \"{channel.Label}\"", ex);
                writer.Dispose();
                capture.Dispose();
                TryDeleteFile(wavPath);
            }
        }
    }

    private static void OnDataAvailable(Channel channel, WaveInEventArgs e)
    {
        lock (channel.WriteLock)
        {
            channel.Writer.Write(e.Buffer, 0, e.BytesRecorded);
        }

        channel.BytesCaptured += e.BytesRecorded;

        var format = channel.Capture.WaveFormat;

        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            for (var i = 0; i + 3 < e.BytesRecorded; i += 4)
            {
                var sample = BitConverter.ToSingle(e.Buffer, i);
                var abs = Math.Abs(sample);
                if (abs > channel.PeakAbs)
                {
                    channel.PeakAbs = abs;
                }
            }
        }
        else if (format.BitsPerSample == 16)
        {
            for (var i = 0; i + 1 < e.BytesRecorded; i += 2)
            {
                var sample = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
                var abs = Math.Abs((double)sample) / 32768.0;
                if (abs > channel.PeakAbs)
                {
                    channel.PeakAbs = abs;
                }
            }
        }
    }

    public List<DeviceTrack> Stop()
    {
        var results = new List<DeviceTrack>();

        foreach (var channel in _channels)
        {
            try
            {
                channel.Capture.StopRecording();
            }
            catch (Exception ex)
            {
                AppLog.Warn($"DeviceLoopbackRecorder.Stop: fallo al parar la captura de \"{channel.Label}\"", ex);
            }

            channel.StoppedEvent.Wait(TimeSpan.FromSeconds(3));

            lock (channel.WriteLock)
            {
                try
                {
                    channel.Writer.Flush();
                }
                catch (Exception ex)
                {
                    AppLog.Warn($"DeviceLoopbackRecorder.Stop: fallo al volcar el WAV de \"{channel.Label}\"", ex);
                }

                channel.Writer.Dispose();
            }

            channel.Capture.Dispose();
            channel.StoppedEvent.Dispose();

            results.Add(new DeviceTrack
            {
                Label = channel.Label,
                WavPath = channel.WavPath,
                PeakAbs = channel.PeakAbs,
                BytesCaptured = channel.BytesCaptured
            });
        }

        _channels.Clear();
        return results;
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
            AppLog.Warn($"DeviceLoopbackRecorder: no se pudo borrar \"{path}\"", ex);
        }
    }

    public void Dispose()
    {
        foreach (var channel in _channels)
        {
            try
            {
                channel.Writer.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Warn($"DeviceLoopbackRecorder.Dispose: fallo al cerrar el WAV de \"{channel.Label}\"", ex);
            }

            try
            {
                channel.Capture.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Warn($"DeviceLoopbackRecorder.Dispose: fallo al liberar la captura de \"{channel.Label}\"", ex);
            }

            channel.StoppedEvent.Dispose();
        }

        _channels.Clear();
    }
}
