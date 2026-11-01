namespace LoopbackTranscriber.Tests.Services;

[Trait(TestCategory.Name, TestCategory.Unit)]
public class MeetingEchoFilterTests
{
    private const string Sentence = "vamos a revisar el presupuesto";

    private static readonly Func<TranscriptSegment, bool> NeverLoud = _ => false;
    private static readonly Func<TranscriptSegment, bool> AlwaysLoud = _ => true;

    [Fact]
    public void MicrophoneEchoOfTheMeeting_IsDropped_AndTheMeetingKeepsIt()
    {
        var other = new List<TranscriptSegment> { Segment(0, 3, Sentence) };
        var mic = new List<TranscriptSegment> { Segment(0.5, 3.5, Sentence) };

        var result = MeetingEchoFilter.Resolve(mic, other, TimeSpan.Zero, NeverLoud);

        Assert.Empty(result.MicSegments);
        Assert.Single(result.OtherSegments);
    }

    [Fact]
    public void WhenTheUserSpokeFirst_TheMicrophoneIsTheOriginal_AndTheMeetingDuplicateIsRemoved()
    {
        var other = new List<TranscriptSegment> { Segment(1, 4, Sentence) };
        var mic = new List<TranscriptSegment> { Segment(0, 3, Sentence) };

        var result = MeetingEchoFilter.Resolve(mic, other, TimeSpan.Zero, NeverLoud);

        Assert.Single(result.MicSegments);
        Assert.Empty(result.OtherSegments);
    }

    [Fact]
    public void AlmostSimultaneousLoudMicrophone_IsTheOriginal()
    {
        var other = new List<TranscriptSegment> { Segment(1.0, 4, Sentence) };
        var mic = new List<TranscriptSegment> { Segment(1.1, 4, Sentence) };

        var result = MeetingEchoFilter.Resolve(mic, other, TimeSpan.Zero, AlwaysLoud);

        Assert.Single(result.MicSegments);
        Assert.Empty(result.OtherSegments);
    }

    [Fact]
    public void AlmostSimultaneousQuietMicrophone_IsTheEcho()
    {
        var other = new List<TranscriptSegment> { Segment(1.0, 4, Sentence) };
        var mic = new List<TranscriptSegment> { Segment(1.1, 4, Sentence) };

        var result = MeetingEchoFilter.Resolve(mic, other, TimeSpan.Zero, NeverLoud);

        Assert.Empty(result.MicSegments);
        Assert.Single(result.OtherSegments);
    }

    [Fact]
    public void ShortMicrophoneSegments_AreNeverTreatedAsEcho()
    {
        var other = new List<TranscriptSegment> { Segment(0, 1, "sí vale") };
        var mic = new List<TranscriptSegment> { Segment(0.5, 1.5, "sí vale") };

        var result = MeetingEchoFilter.Resolve(mic, other, TimeSpan.Zero, NeverLoud);

        Assert.Single(result.MicSegments);
        Assert.Single(result.OtherSegments);
    }

    [Fact]
    public void DifferentWordsAtTheSameTime_AreBothKept()
    {
        var other = new List<TranscriptSegment> { Segment(0, 3, Sentence) };
        var mic = new List<TranscriptSegment> { Segment(0.5, 3.5, "ayer cerramos el contrato nuevo") };

        var result = MeetingEchoFilter.Resolve(mic, other, TimeSpan.Zero, NeverLoud);

        Assert.Single(result.MicSegments);
        Assert.Single(result.OtherSegments);
    }

    [Fact]
    public void SameWordsFarApartInTime_AreBothKept()
    {
        var other = new List<TranscriptSegment> { Segment(0, 3, Sentence) };
        var mic = new List<TranscriptSegment> { Segment(20, 23, Sentence) };

        var result = MeetingEchoFilter.Resolve(mic, other, TimeSpan.Zero, NeverLoud);

        Assert.Single(result.MicSegments);
        Assert.Single(result.OtherSegments);
    }

    [Fact]
    public void TheMicrophoneOffset_IsAppliedBeforeComparingTimes()
    {
        var other = new List<TranscriptSegment> { Segment(10.5, 13.5, Sentence) };
        var mic = new List<TranscriptSegment> { Segment(0, 3, Sentence) };

        var aligned = MeetingEchoFilter.Resolve(mic, other, TimeSpan.FromSeconds(10), NeverLoud);
        var unaligned = MeetingEchoFilter.Resolve(mic, other, TimeSpan.Zero, NeverLoud);

        Assert.Single(aligned.MicSegments);
        Assert.Empty(aligned.OtherSegments);
        Assert.Single(unaligned.MicSegments);
        Assert.Single(unaligned.OtherSegments);
    }

    [Fact]
    public void OnlyTheMeetingSegmentsContainedInTheUsersSpeech_AreRemoved()
    {
        var duplicate = Segment(0.5, 3, Sentence);
        var unrelated = Segment(3, 4, "luego vacaciones");
        var other = new List<TranscriptSegment> { duplicate, unrelated };
        var mic = new List<TranscriptSegment> { Segment(0, 3, Sentence) };

        var result = MeetingEchoFilter.Resolve(mic, other, TimeSpan.Zero, NeverLoud);

        Assert.Single(result.MicSegments);
        Assert.Equal(unrelated, Assert.Single(result.OtherSegments));
    }

    [Fact]
    public void WithoutMicrophoneSegments_TheMeetingIsUnchanged()
    {
        var other = new List<TranscriptSegment> { Segment(0, 3, Sentence), Segment(4, 6, "otra frase distinta aqui") };

        var result = MeetingEchoFilter.Resolve([], other, TimeSpan.Zero, NeverLoud);

        Assert.Empty(result.MicSegments);
        Assert.Equal(other, result.OtherSegments);
    }

    private static TranscriptSegment Segment(double start, double end, string text) =>
        new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);
}
