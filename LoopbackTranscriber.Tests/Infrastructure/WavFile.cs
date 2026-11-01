using System.Buffers.Binary;
using System.Text;
using NAudio.Wave;

namespace LoopbackTranscriber.Tests.Infrastructure;

public static class WavFile
{
    private const double Frequency = 440;

    public static string WritePcm16(string path, int sampleRate, int channels, params (double Seconds, double Amplitude)[] parts)
    {
        var bytes = Pcm16(sampleRate, channels, parts);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new WaveFileWriter(path, new WaveFormat(sampleRate, 16, channels));
        writer.Write(bytes, 0, bytes.Length);
        return path;
    }

    public static string WriteTone(string path, double seconds, double amplitude, int sampleRate = 16000, int channels = 1) =>
        WritePcm16(path, sampleRate, channels, (seconds, amplitude));

    public static string WriteSilence(string path, double seconds, int sampleRate = 16000, int channels = 1) =>
        WritePcm16(path, sampleRate, channels, (seconds, 0));

    public static string WriteFloat(string path, double seconds, double amplitude, int sampleRate, int channels)
    {
        var frames = (int)Math.Round(seconds * sampleRate);
        var bytes = new byte[frames * channels * 4];
        for (var frame = 0; frame < frames; frame++)
        {
            var value = (float)(amplitude * Math.Sin(2 * Math.PI * Frequency * frame / sampleRate));
            for (var channel = 0; channel < channels; channel++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan((frame * channels + channel) * 4, 4), value);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels));
        writer.Write(bytes, 0, bytes.Length);
        return path;
    }

    public static byte[] Pcm16(int sampleRate, int channels, params (double Seconds, double Amplitude)[] parts)
    {
        var frames = parts.Sum(p => (int)Math.Round(p.Seconds * sampleRate));
        var bytes = new byte[frames * channels * 2];
        var frame = 0;

        foreach (var (seconds, amplitude) in parts)
        {
            var partFrames = (int)Math.Round(seconds * sampleRate);
            for (var i = 0; i < partFrames; i++, frame++)
            {
                var value = (short)Math.Round(amplitude * short.MaxValue * Math.Sin(2 * Math.PI * Frequency * i / sampleRate));
                for (var channel = 0; channel < channels; channel++)
                {
                    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan((frame * channels + channel) * 2, 2), value);
                }
            }
        }

        return bytes;
    }

    public static void BreakHeaderSizes(string path)
    {
        var bytes = File.ReadAllBytes(path);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 0);
        var data = IndexOfChunk(bytes, "data");
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(data + 4, 4), 0);
        File.WriteAllBytes(path, bytes);
    }

    public static uint ReadDataChunkSize(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(IndexOfChunk(bytes, "data") + 4, 4));
    }

    public static int DataOffset(string path) => IndexOfChunk(File.ReadAllBytes(path), "data") + 8;

    public static TimeSpan Duration(string path)
    {
        using var reader = new WaveFileReader(path);
        return reader.TotalTime;
    }

    public static WaveFormat Format(string path)
    {
        using var reader = new WaveFileReader(path);
        return reader.WaveFormat;
    }

    public static short[] ReadPcm16(string path)
    {
        using var reader = new WaveFileReader(path);
        var bytes = new byte[reader.Length];
        var read = 0;
        while (read < bytes.Length)
        {
            var n = reader.Read(bytes, read, bytes.Length - read);
            if (n <= 0)
            {
                break;
            }

            read += n;
        }

        var samples = new short[read / 2];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i * 2, 2));
        }

        return samples;
    }

    private static int IndexOfChunk(byte[] bytes, string id)
    {
        var pattern = Encoding.ASCII.GetBytes(id);
        var index = bytes.AsSpan(12).IndexOf(pattern);
        if (index < 0)
        {
            throw new InvalidDataException("The WAV file has no '" + id + "' chunk.");
        }

        return index + 12;
    }
}
