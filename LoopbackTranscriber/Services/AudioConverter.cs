using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace LoopbackTranscriber.Services;

public static class AudioConverter
{
    public const int WhisperSampleRate = 16000;

    public static void ConvertToWhisperFormat(string sourceWavPath, string destWavPath)
    {
        using var reader = new AudioFileReader(sourceWavPath);
        ISampleProvider sampleProvider = reader;

        if (reader.WaveFormat.Channels > 1)
        {
            sampleProvider = new StereoToMonoSampleProvider(sampleProvider)
            {
                LeftVolume = 0.5f,
                RightVolume = 0.5f
            };
        }

        if (reader.WaveFormat.SampleRate != WhisperSampleRate)
        {
            sampleProvider = new WdlResamplingSampleProvider(sampleProvider, WhisperSampleRate);
        }

        WaveFileWriter.CreateWaveFile16(destWavPath, sampleProvider);
    }

    public const int ActivityWindowMs = 50;

    public static bool[]? GetActivityMap(string wavPath, double minRms = 4.0)
    {
        using var reader = new WaveFileReader(wavPath);
        var format = reader.WaveFormat;
        if (format.BitsPerSample != 16 || format.Channels != 1)
        {
            return null;
        }

        var windowSamples = Math.Max(1, format.SampleRate * ActivityWindowMs / 1000);
        var buffer = new byte[windowSamples * 2];
        var map = new List<bool>();

        while (true)
        {
            var read = 0;
            while (read < buffer.Length)
            {
                var n = reader.Read(buffer, read, buffer.Length - read);
                if (n <= 0)
                {
                    break;
                }

                read += n;
            }

            var sampleCount = read / 2;
            if (sampleCount == 0)
            {
                break;
            }

            double sumSquares = 0;
            for (var i = 0; i < sampleCount; i++)
            {
                var sample = (short)(buffer[i * 2] | (buffer[i * 2 + 1] << 8));
                sumSquares += (double)sample * sample;
            }

            map.Add(Math.Sqrt(sumSquares / sampleCount) >= minRms);

            if (read < buffer.Length)
            {
                break;
            }
        }

        return map.ToArray();
    }

    public static (double Fraction, double ActiveSeconds) MeasureActivity(bool[] map, TimeSpan start, TimeSpan end)
    {
        var first = Math.Clamp((int)(start.TotalMilliseconds / ActivityWindowMs), 0, map.Length);
        var last = Math.Clamp((int)Math.Ceiling(end.TotalMilliseconds / ActivityWindowMs), first, map.Length);
        var total = last - first;
        if (total <= 0)
        {
            return (1.0, Math.Max(0.0, (end - start).TotalSeconds));
        }

        var active = 0;
        for (var i = first; i < last; i++)
        {
            if (map[i])
            {
                active++;
            }
        }

        return ((double)active / total, active * ActivityWindowMs / 1000.0);
    }

    public static void ApplyNoiseGate(string wavPath, double gateBelowPeakDb = 34.0)
    {
        byte[] bytes;
        WaveFormat format;

        using (var reader = new WaveFileReader(wavPath))
        {
            format = reader.WaveFormat;
            bytes = new byte[reader.Length];
            var totalRead = 0;
            while (totalRead < bytes.Length)
            {
                var read = reader.Read(bytes, totalRead, bytes.Length - totalRead);
                if (read <= 0)
                {
                    break;
                }

                totalRead += read;
            }
        }

        if (format.BitsPerSample != 16 || format.Channels != 1)
        {
            return;
        }

        var sampleCount = bytes.Length / 2;
        var samples = new short[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            samples[i] = (short)(bytes[i * 2] | (bytes[i * 2 + 1] << 8));
        }

        var peak = 0;
        foreach (var s in samples)
        {
            var abs = Math.Abs((int)s);
            if (abs > peak)
            {
                peak = abs;
            }
        }

        if (peak <= 0)
        {
            return;
        }

        var threshold = peak * Math.Pow(10, -gateBelowPeakDb / 20.0);

        const int windowMs = 50;
        const int fadeMs = 10;
        var windowSize = Math.Max(1, format.SampleRate * windowMs / 1000);
        var fadeSize = Math.Max(1, format.SampleRate * fadeMs / 1000);
        var windowCount = (sampleCount + windowSize - 1) / windowSize;

        var gateOpen = new bool[windowCount];
        for (var w = 0; w < windowCount; w++)
        {
            var start = w * windowSize;
            var end = Math.Min(sampleCount, start + windowSize);
            double sumSquares = 0;
            for (var i = start; i < end; i++)
            {
                sumSquares += (double)samples[i] * samples[i];
            }

            var rms = Math.Sqrt(sumSquares / (end - start));
            gateOpen[w] = rms >= threshold;
        }

        var gain = new float[sampleCount];
        for (var w = 0; w < windowCount; w++)
        {
            var start = w * windowSize;
            var end = Math.Min(sampleCount, start + windowSize);
            var g = gateOpen[w] ? 1f : 0f;
            for (var i = start; i < end; i++)
            {
                gain[i] = g;
            }
        }

        for (var w = 1; w < windowCount; w++)
        {
            if (gateOpen[w] == gateOpen[w - 1])
            {
                continue;
            }

            var center = w * windowSize;
            var rampStart = Math.Max(0, center - fadeSize / 2);
            var rampEnd = Math.Min(sampleCount, center + fadeSize / 2);
            var fromGain = gateOpen[w - 1] ? 1f : 0f;
            var toGain = gateOpen[w] ? 1f : 0f;
            var span = Math.Max(1, rampEnd - rampStart);
            for (var i = rampStart; i < rampEnd; i++)
            {
                var t = (float)(i - rampStart) / span;
                gain[i] = fromGain + (toGain - fromGain) * t;
            }
        }

        for (var i = 0; i < sampleCount; i++)
        {
            var gated = samples[i] * gain[i];
            var clamped = Math.Clamp(gated, (float)short.MinValue, (float)short.MaxValue);
            var pcmValue = (short)clamped;
            bytes[i * 2] = (byte)(pcmValue & 0xFF);
            bytes[i * 2 + 1] = (byte)((pcmValue >> 8) & 0xFF);
        }

        using var writer = new WaveFileWriter(wavPath, format);
        writer.Write(bytes, 0, bytes.Length);
    }
}
