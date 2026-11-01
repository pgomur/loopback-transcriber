namespace LoopbackTranscriber.Tests.Services;

[Trait(TestCategory.Name, TestCategory.Unit)]
public class TranscriptMessageParserTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   \n\n  ")]
    [InlineData("# Teams - 18/10/2026 09:30")]
    [InlineData("# Teams - 18/10/2026 09:30\n\n")]
    public void NoContent_GivesNoMessages(string text)
    {
        Assert.Empty(TranscriptMessageParser.Parse(text));
    }

    [Fact]
    public void TheMarkdownHeader_IsNotAMessage()
    {
        var messages = TranscriptMessageParser.Parse("# Teams - 18/10/2026 09:30\n\nHola a todos.");

        var message = Assert.Single(messages);
        Assert.Null(message.Speaker);
        Assert.Equal("Hola a todos.", message.Text);
    }

    [Fact]
    public void LabelledParagraphs_KeepTheirSpeakers()
    {
        var messages = TranscriptMessageParser.Parse(
            $"{SpeakerLabels.Others}: Buenos días.\n\npgomur (Tú): Hola.\n\n{SpeakerLabels.SelfAnonymous}: Seguimos.");

        Assert.Equal(new[] { SpeakerLabels.Others, "pgomur (Tú)", SpeakerLabels.SelfAnonymous }, messages.Select(m => m.Speaker));
        Assert.Equal(new[] { "Buenos días.", "Hola.", "Seguimos." }, messages.Select(m => m.Text));
        Assert.Equal(new[] { false, true, true }, messages.Select(m => m.IsSelf));
    }

    [Fact]
    public void AParagraphWithAnUnknownPrefix_IsPlainText()
    {
        var message = Assert.Single(TranscriptMessageParser.Parse("María: esto lo dijo otra persona"));

        Assert.Null(message.Speaker);
        Assert.Equal("María: esto lo dijo otra persona", message.Text);
    }

    [Fact]
    public void ALabelWithoutText_IsDropped()
    {
        var messages = TranscriptMessageParser.Parse($"{SpeakerLabels.Others}:   \n\n{SpeakerLabels.Others}: Algo.");

        Assert.Equal("Algo.", Assert.Single(messages).Text);
    }

    [Fact]
    public void LinesInsideAParagraph_StayTogether()
    {
        var message = Assert.Single(TranscriptMessageParser.Parse("primera línea\nsegunda línea"));

        Assert.Equal("primera línea\nsegunda línea", message.Text);
    }
}
