using System.IO;
using System.Text;
using NAudio.Wave;

namespace LoopbackTranscriber.Services;

internal static class WavRepair
{
    private const int MinimumWavBytes = 44;
    private const long MaxHeaderBytes = 4096;

    public static bool HasAudioData(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

            var dataStart = FindDataStart(stream, reader);
            return dataStart is null
                ? stream.Length > MinimumWavBytes
                : stream.Length > dataStart.Value;
        }
        catch (Exception ex) when (ExpectedFailures.IsAudioFile(ex))
        {
            AppLog.Warn($"WavRepair.HasAudioData: could not read \"{path}\"", ex);
            return false;
        }
    }

    public static bool IsInUse(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    public static bool TryRepairHeader(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
            using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

            var length = stream.Length;
            if (length < MinimumWavBytes || ReadChunkId(reader) != "RIFF")
            {
                return false;
            }

            reader.ReadUInt32();
            if (ReadChunkId(reader) != "WAVE")
            {
                return false;
            }

            var blockAlign = 1;
            var scanLimit = Math.Min(length, MaxHeaderBytes);

            while (stream.Position + 8 <= scanLimit)
            {
                var id = ReadChunkId(reader);
                var size = reader.ReadUInt32();
                var chunkStart = stream.Position;

                if (id == "data")
                {
                    var available = length - chunkStart;
                    available -= available % blockAlign;

                    if (size != 0 && size <= available)
                    {
                        return true;
                    }

                    stream.Position = chunkStart - 4;
                    writer.Write((uint)Math.Min(available, uint.MaxValue));
                    stream.Position = 4;
                    writer.Write((uint)Math.Min(length - 8, uint.MaxValue));
                    writer.Flush();

                    AppLog.Info($"WavRepair: header repaired in \"{path}\" ({available} bytes of audio)");
                    return true;
                }

                if (id == "fmt " && size >= 14)
                {
                    stream.Position = chunkStart + 12;
                    blockAlign = Math.Max(1, (int)reader.ReadUInt16());
                }

                stream.Position = chunkStart + size + (size & 1);
            }

            return false;
        }
        catch (Exception ex) when (ExpectedFailures.IsAudioFile(ex))
        {
            AppLog.Warn($"WavRepair.TryRepairHeader: could not repair \"{path}\"", ex);
            return false;
        }
    }

    public static double MeasurePeak(string path, double stopAt)
    {
        try
        {
            using var reader = new AudioFileReader(path);
            var buffer = new float[Math.Max(1, reader.WaveFormat.SampleRate * reader.WaveFormat.Channels)];
            var peak = 0.0;

            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (var i = 0; i < read; i++)
                {
                    var abs = Math.Abs(buffer[i]);
                    if (abs > peak)
                    {
                        peak = abs;
                        if (peak >= stopAt)
                        {
                            return peak;
                        }
                    }
                }
            }

            return peak;
        }
        catch (Exception ex) when (ExpectedFailures.IsAudioFile(ex))
        {
            AppLog.Warn($"WavRepair.MeasurePeak: could not read \"{path}\"", ex);
            return 0;
        }
    }

    private static long? FindDataStart(FileStream stream, BinaryReader reader)
    {
        var length = stream.Length;
        if (length < 12 || ReadChunkId(reader) != "RIFF")
        {
            return null;
        }

        reader.ReadUInt32();
        if (ReadChunkId(reader) != "WAVE")
        {
            return null;
        }

        var scanLimit = Math.Min(length, MaxHeaderBytes);
        while (stream.Position + 8 <= scanLimit)
        {
            var id = ReadChunkId(reader);
            var size = reader.ReadUInt32();
            if (id == "data")
            {
                return stream.Position;
            }

            stream.Position += size + (size & 1);
        }

        return null;
    }

    private static string ReadChunkId(BinaryReader reader) => Encoding.ASCII.GetString(reader.ReadBytes(4));
}
