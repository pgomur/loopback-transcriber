namespace LoopbackTranscriber.Tests.Services;

[Trait(TestCategory.Name, TestCategory.Unit)]
public class CaptureTimelineTests
{
    private const int SampleRate = 1000;
    private const long TicksPerFrame = TimeSpan.TicksPerSecond / SampleRate;
    private const long Start = 50_000_000_000;

    [Fact]
    public void FirstPacket_SetsTheTimestampOfTheFirstSample_WithoutPadding()
    {
        var timeline = new CaptureTimeline(SampleRate);

        Assert.Equal(0, timeline.PaddingBefore(Start, 100));
        Assert.Equal(Start, timeline.FirstSampleTimestamp);
    }

    [Fact]
    public void ContiguousPackets_NeedNoPadding()
    {
        var timeline = new CaptureTimeline(SampleRate);
        timeline.PaddingBefore(Start, 100);

        Assert.Equal(0, timeline.PaddingBefore(Start + 100 * TicksPerFrame, 100));
        Assert.Equal(0, timeline.PaddedFrames);
    }

    [Fact]
    public void AGapLongerThanAQuarterSecond_IsFilledWithSilence()
    {
        var timeline = new CaptureTimeline(SampleRate);
        timeline.PaddingBefore(Start, 100);
        timeline.PaddingBefore(Start + 100 * TicksPerFrame, 100);

        var padding = timeline.PaddingBefore(Start + 200 * TicksPerFrame + TimeSpan.FromMilliseconds(500).Ticks, 100);

        Assert.Equal(500, padding);
        Assert.Equal(500, timeline.PaddedFrames);
        Assert.Equal(TimeSpan.FromMilliseconds(500), timeline.PaddedDuration);
    }

    [Fact]
    public void SmallJitter_IsNotPadded()
    {
        var timeline = new CaptureTimeline(SampleRate);
        timeline.PaddingBefore(Start, 100);

        var padding = timeline.PaddingBefore(Start + 100 * TicksPerFrame + TimeSpan.FromMilliseconds(200).Ticks, 100);

        Assert.Equal(0, padding);
    }

    [Fact]
    public void PaddingIsCounted_SoTheNextPacketOnTimeNeedsNoMore()
    {
        var timeline = new CaptureTimeline(SampleRate);
        timeline.PaddingBefore(Start, 100);
        timeline.PaddingBefore(Start + 100 * TicksPerFrame + TimeSpan.FromSeconds(1).Ticks, 100);

        var padding = timeline.PaddingBefore(Start + 1200 * TicksPerFrame, 100);

        Assert.Equal(0, padding);
        Assert.Equal(1000, timeline.PaddedFrames);
    }

    [Fact]
    public void FramesWrittenWithoutTimestamp_MoveTheFirstSampleBack()
    {
        var timeline = new CaptureTimeline(SampleRate);
        timeline.AddUntimed(50);

        timeline.PaddingBefore(Start, 10);

        Assert.Equal(Start - 50 * TicksPerFrame, timeline.FirstSampleTimestamp);
    }

    [Fact]
    public void PaddingBeforeArrival_TreatsTheArrivalAsTheEndOfThePacket()
    {
        var byStart = new CaptureTimeline(SampleRate);
        var byArrival = new CaptureTimeline(SampleRate);

        byStart.PaddingBefore(Start, 100);
        byArrival.PaddingBeforeArrival(Start + 100 * TicksPerFrame, 100);

        Assert.Equal(byStart.FirstSampleTimestamp, byArrival.FirstSampleTimestamp);
    }

    [Fact]
    public void WriteSilence_WritesExactlyTheRequestedZeroBytes()
    {
        using var stream = new MemoryStream();

        CaptureTimeline.WriteSilence(stream, 200_000);

        Assert.Equal(200_000, stream.Length);
        Assert.All(stream.ToArray(), b => Assert.Equal(0, b));
    }

    [Fact]
    public void Now_IsMonotonicAndInTicks()
    {
        var first = CaptureTimeline.Now();
        Thread.Sleep(20);
        var second = CaptureTimeline.Now();

        Assert.InRange(second - first, TimeSpan.FromMilliseconds(10).Ticks, TimeSpan.FromSeconds(5).Ticks);
    }
}
