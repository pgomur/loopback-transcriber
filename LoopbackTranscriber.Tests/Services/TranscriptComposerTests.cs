namespace LoopbackTranscriber.Tests.Services;

[Trait(TestCategory.Name, TestCategory.Unit)]
public class TranscriptComposerTests
{
    [Fact]
    public void WithoutMicrophone_TheTextHasNoSpeakerLabels()
    {
        var other = new List<TranscriptSegment> { Segment(0, 2, "Hola a todos."), Segment(2.5, 4, "Empezamos.") };

        var text = TranscriptComposer.Compose(other, [], TimeSpan.Zero, "pgomur");

        Assert.Equal("Hola a todos. Empezamos.", text);
    }

    [Fact]
    public void ALongPause_StartsANewParagraph()
    {
        var other = new List<TranscriptSegment> { Segment(0, 2, "Primera idea."), Segment(5, 6, "Segunda idea.") };

        var text = TranscriptComposer.Compose(other, [], TimeSpan.Zero, null);

        Assert.Equal("Primera idea.\n\nSegunda idea.", text);
    }

    [Fact]
    public void APauseJustUnderThreeSeconds_KeepsTheSameParagraph()
    {
        var other = new List<TranscriptSegment> { Segment(0, 2, "Primera idea."), Segment(4.9, 6, "Sigue.") };

        Assert.Equal("Primera idea. Sigue.", TranscriptComposer.Compose(other, [], TimeSpan.Zero, null));
    }

    [Fact]
    public void AVeryLongParagraph_IsSplitEvenWithoutPauses()
    {
        var chunk = new string('a', 400);
        var other = new List<TranscriptSegment> { Segment(0, 1, chunk), Segment(1, 2, chunk), Segment(2, 3, chunk) };

        var text = TranscriptComposer.Compose(other, [], TimeSpan.Zero, null);

        Assert.Equal($"{chunk} {chunk}\n\n{chunk}", text);
    }

    [Fact]
    public void WithMicrophone_EveryParagraphIsLabelled_AndTheSpeakerChangeStartsAParagraph()
    {
        var other = new List<TranscriptSegment> { Segment(0, 2, "¿Empezamos?"), Segment(6, 7, "Perfecto.") };
        var mic = new List<TranscriptSegment> { Segment(3, 5, "Sí, adelante.") };

        var text = TranscriptComposer.Compose(other, mic, TimeSpan.Zero, "pgomur");

        Assert.Equal(
            $"{SpeakerLabels.Others}: ¿Empezamos?\n\npgomur (Tú): Sí, adelante.\n\n{SpeakerLabels.Others}: Perfecto.",
            text);
    }

    [Fact]
    public void MicrophoneSegments_AreShiftedByTheOffsetBeforeOrdering()
    {
        var other = new List<TranscriptSegment> { Segment(5, 6, "Primero la reunión.") };
        var mic = new List<TranscriptSegment> { Segment(0, 1, "Después yo.") };

        var text = TranscriptComposer.Compose(other, mic, TimeSpan.FromSeconds(10), null);

        Assert.Equal($"{SpeakerLabels.Others}: Primero la reunión.\n\n{SpeakerLabels.SelfAnonymous}: Después yo.", text);
    }

    [Fact]
    public void BlankSegments_AreIgnored_AndTextIsTrimmed()
    {
        var other = new List<TranscriptSegment> { Segment(0, 1, "   "), Segment(1, 2, "  Hola.  ") };

        Assert.Equal("Hola.", TranscriptComposer.Compose(other, [], TimeSpan.Zero, null));
    }

    [Fact]
    public void NothingSaid_GivesAnEmptyText()
    {
        Assert.Equal(string.Empty, TranscriptComposer.Compose([], [], TimeSpan.Zero, "pgomur"));
    }

    private static TranscriptSegment Segment(double start, double end, string text) =>
        new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);
}
