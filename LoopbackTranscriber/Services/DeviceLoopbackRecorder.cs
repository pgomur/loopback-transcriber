using System.IO;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace LoopbackTranscriber.Services;
public sealed class DeviceLoopbackRecorder : IDeviceCapture
{
    public sealed class DeviceTrack
    {
        public required string Label { get; init; }
        public required string WavPath { get; init; }
        public double PeakAbs { get; init; }
        public long BytesCaptured { get; init; }
        public long? FirstSampleTimestamp { get; init; }
    }

    private sealed class Channel : IDisposable
    {
        public required string Label;
        public required WasapiLoopbackCapture Capture;
        public required WaveFileWriter Writer;
        public required string WavPath;
        public required CaptureTimeline Timeline;
        public readonly object WriteLock = new();
        public readonly ManualResetEventSlim StoppedEvent = new(false);
        public double PeakAbs;
        public long BytesCaptured;

        public void Dispose() => StoppedEvent.Dispose();
    }

    private readonly List<Channel> _channels = new();

    public void Start(string outputDirectory, string baseFileName)
    {
        _channels.Clear();
        Directory.CreateDirectory(outputDirectory);

        using var enumerator = new MMDeviceEnumerator();
        var seenDeviceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (roleLabel, role) in new (string, Role)[]
                 {
                     ("console", Role.Console),
                     ("communications", Role.Communications)
                 })
        {
            WasapiLoopbackCapture? capture = null;
            WaveFileWriter? writer = null;
            var wavPath = Path.Combine(outputDirectory, $"{baseFileName}_{roleLabel}.wav");

            try
            {
                var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, role);
                if (device is null || !seenDeviceIds.Add(device.ID))
                {
                    continue;
                }

                capture = new WasapiLoopbackCapture(device);
                writer = new WaveFileWriter(wavPath, capture.WaveFormat);

                var channel = new Channel
                {
                    Label = $"{roleLabel} ({device.FriendlyName})",
                    Capture = capture,
                    Writer = writer,
                    WavPath = wavPath,
                    Timeline = new CaptureTimeline(capture.WaveFormat.SampleRate)
                };

                capture.DataAvailable += (_, e) => OnDataAvailable(channel, e);
                capture.RecordingStopped += (_, _) => channel.StoppedEvent.Set();

                capture.StartRecording();
                _channels.Add(channel);
            }
            catch (Exception ex) when (ExpectedFailures.IsDeviceOrFile(ex))
            {
                AppLog.Warn($"DeviceLoopbackRecorder.Start: could not capture the default output device ({roleLabel}); skipping it", ex);
                writer?.Dispose();
                capture?.Dispose();
                if (writer is not null)
                {
                    FileCleanup.TryDelete(wavPath, "DeviceLoopbackRecorder.Start");
                }
            }
        }
    }

    private static void OnDataAvailable(Channel channel, WaveInEventArgs e)
    {
        var arrival = CaptureTimeline.Now();
        var format = channel.Capture.WaveFormat;

        lock (channel.WriteLock)
        {
            if (e.BytesRecorded > 0)
            {
                var blockAlign = Math.Max(1, format.BlockAlign);
                var paddingFrames = channel.Timeline.PaddingBeforeArrival(arrival, e.BytesRecorded / blockAlign);
                if (paddingFrames > 0)
                {
                    CaptureTimeline.WriteSilence(channel.Writer, paddingFrames * blockAlign);
                }

                channel.Writer.Write(e.Buffer, 0, e.BytesRecorded);
            }
        }

        channel.BytesCaptured += e.BytesRecorded;

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

        try
        {
            foreach (var channel in _channels)
            {
                try
                {
                    channel.Capture.StopRecording();
                }
                catch (COMException ex)
                {
                    AppLog.Warn($"DeviceLoopbackRecorder.Stop: failed to stop capture for \"{channel.Label}\"", ex);
                }

                channel.StoppedEvent.Wait(TimeSpan.FromSeconds(3));

                lock (channel.WriteLock)
                {
                    try
                    {
                        channel.Writer.Flush();
                    }
                    catch (IOException ex)
                    {
                        AppLog.Warn($"DeviceLoopbackRecorder.Stop: failed to flush the WAV for \"{channel.Label}\"", ex);
                    }
                }

                if (channel.Timeline.PaddedFrames > 0)
                {
                    AppLog.Info($"DeviceLoopbackRecorder: gaps filled with silence in \"{channel.Label}\" = {channel.Timeline.PaddedDuration.TotalSeconds:0.0}s");
                }

                results.Add(new DeviceTrack
                {
                    Label = channel.Label,
                    WavPath = channel.WavPath,
                    PeakAbs = channel.PeakAbs,
                    BytesCaptured = channel.BytesCaptured,
                    FirstSampleTimestamp = channel.Timeline.FirstSampleTimestamp
                });
            }
        }
        finally
        {
            ReleaseChannels();
        }

        return results;
    }

    private void ReleaseChannels()
    {
        foreach (var channel in _channels)
        {
            lock (channel.WriteLock)
            {
                try
                {
                    channel.Writer.Dispose();
                }
                catch (Exception ex) when (ExpectedFailures.IsFile(ex))
                {
                    AppLog.Warn($"DeviceLoopbackRecorder: failed to close the WAV for \"{channel.Label}\"", ex);
                }
            }

            try
            {
                channel.Capture.Dispose();
            }
            catch (COMException ex)
            {
                AppLog.Warn($"DeviceLoopbackRecorder: failed to release the capture for \"{channel.Label}\"", ex);
            }

            channel.Dispose();
        }

        _channels.Clear();
    }

    public void Dispose() => ReleaseChannels();
}
