using System.Diagnostics;
using System.IO;

namespace LoopbackTranscriber.Services;

internal sealed class CaptureTimeline
{
    private const long TicksPerSecond = TimeSpan.TicksPerSecond;
    private static readonly TimeSpan GapThreshold = TimeSpan.FromMilliseconds(250);
    private static readonly byte[] Zeros = new byte[64 * 1024];

    private readonly int _sampleRate;
    private readonly long _gapThresholdFrames;
    private long _framesWritten;

    public CaptureTimeline(int sampleRate)
    {
        _sampleRate = Math.Max(1, sampleRate);
        _gapThresholdFrames = _sampleRate * GapThreshold.Ticks / TicksPerSecond;
    }

    public long? FirstSampleTimestamp { get; private set; }

    public long PaddedFrames { get; private set; }

    public TimeSpan PaddedDuration => TimeSpan.FromTicks(PaddedFrames * TicksPerSecond / _sampleRate);

    public static long Now()
    {
        var timestamp = Stopwatch.GetTimestamp();
        var frequency = Stopwatch.Frequency;
        return timestamp / frequency * TicksPerSecond + timestamp % frequency * TicksPerSecond / frequency;
    }

    public long PaddingBefore(long packetStartTimestamp, long packetFrames)
    {
        long padding = 0;

        if (FirstSampleTimestamp is null)
        {
            FirstSampleTimestamp = packetStartTimestamp - _framesWritten * TicksPerSecond / _sampleRate;
        }
        else
        {
            var expectedFrames = (packetStartTimestamp - FirstSampleTimestamp.Value) * _sampleRate / TicksPerSecond;
            var deficit = expectedFrames - _framesWritten;
            if (deficit > _gapThresholdFrames)
            {
                padding = deficit;
                PaddedFrames += padding;
            }
        }

        _framesWritten += padding + packetFrames;
        return padding;
    }

    public long PaddingBeforeArrival(long arrivalTimestamp, long packetFrames) =>
        PaddingBefore(arrivalTimestamp - packetFrames * TicksPerSecond / _sampleRate, packetFrames);

    public void AddUntimed(long packetFrames) => _framesWritten += packetFrames;

    public static void WriteSilence(Stream stream, long bytes)
    {
        while (bytes > 0)
        {
            var chunk = (int)Math.Min(bytes, Zeros.Length);
            stream.Write(Zeros, 0, chunk);
            bytes -= chunk;
        }
    }
}
