namespace LoopbackTranscriber.Tests.Services;

[Trait(TestCategory.Name, TestCategory.Unit)]
public class LiveTextSplitterTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Split_EmptyInput_ReturnsNoPieces(string? text)
    {
        Assert.Empty(LiveTextSplitter.Split(text));
    }

    [Fact]
    public void Split_ShortText_IsReturnedUntouched()
    {
        Assert.Equal(new[] { "Hola mundo." }, LiveTextSplitter.Split("  Hola mundo.  "));
    }

    [Fact]
    public void Split_LongText_SplitsAtSentenceBoundaries()
    {
        const string text =
            "Great, so we agree to ship it on Thursday. Does anyone have questions before we wrap up the meeting today? Thanks everyone for joining.";

        Assert.Equal(
            new[]
            {
                "Great, so we agree to ship it on Thursday.",
                "Does anyone have questions before we wrap up the meeting today?",
                "Thanks everyone for joining."
            },
            LiveTextSplitter.Split(text));
    }

    [Fact]
    public void Split_VeryShortFirstSentence_IsMergedWithTheNext()
    {
        const string text =
            "Sí. Vale, entonces mañana por la mañana revisamos juntos todos los puntos pendientes del proyecto con el equipo completo.";

        var pieces = LiveTextSplitter.Split(text);

        Assert.Single(pieces);
        Assert.StartsWith("Sí. Vale,", pieces[0]);
    }

    [Fact]
    public void Split_VeryShortLastSentence_IsMergedWithThePrevious()
    {
        const string text =
            "Great, so we agree to ship it on Thursday. Does anyone have questions before we wrap up the meeting today? Ok.";

        var pieces = LiveTextSplitter.Split(text);

        Assert.Equal(2, pieces.Count);
        Assert.EndsWith("today? Ok.", pieces[1]);
    }

    [Fact]
    public void ToLines_JoinsPiecesWithNewlines()
    {
        const string text =
            "Great, so we agree to ship it on Thursday. Does anyone have questions before we wrap up the meeting today? Thanks everyone for joining.";

        Assert.Equal(2, LiveTextSplitter.ToLines(text).Count(c => c == '\n'));
    }
}
