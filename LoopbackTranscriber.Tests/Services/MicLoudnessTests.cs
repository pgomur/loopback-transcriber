namespace LoopbackTranscriber.Tests.Services;

[Trait(TestCategory.Name, TestCategory.Unit)]
public class MicLoudnessTests
{
    private const double Loud = 3000;
    private const double Quiet = 300;

    [Fact]
    public void WithoutLevels_NothingIsLoud()
    {
        var isLoud = MicLoudness.Create(null);

        Assert.False(isLoud(Segment(0, 1)));
    }

    [Fact]
    public void WithTooLittleSpeech_NothingIsLoud()
    {
        var isLoud = MicLoudness.Create(Enumerable.Repeat(Loud, 10).ToArray());

        Assert.False(isLoud(Segment(0, 0.5)));
    }

    [Fact]
    public void ASegmentNearTheUsersUsualLevel_IsLoud_AndAMuchQuieterOne_IsNot()
    {
        var levels = Enumerable.Repeat(Loud, 20).Concat(Enumerable.Repeat(Quiet, 20)).ToArray();
        var isLoud = MicLoudness.Create(levels);

        Assert.True(isLoud(Segment(0, 1)));
        Assert.False(isLoud(Segment(1, 2)));
    }

    [Fact]
    public void ASegmentWithoutVoice_IsNotLoud()
    {
        var levels = Enumerable.Repeat(Loud, 30).Concat(Enumerable.Repeat(1.0, 20)).ToArray();
        var isLoud = MicLoudness.Create(levels);

        Assert.False(isLoud(Segment(1.5, 2.5)));
    }

    [Fact]
    public void ASegmentOutsideTheRecording_IsNotLoud()
    {
        var isLoud = MicLoudness.Create(Enumerable.Repeat(Loud, 40).ToArray());

        Assert.False(isLoud(Segment(10, 11)));
    }

    private static TranscriptSegment Segment(double start, double end) =>
        new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), "texto");
}
