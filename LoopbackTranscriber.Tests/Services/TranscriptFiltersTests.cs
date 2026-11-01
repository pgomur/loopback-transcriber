namespace LoopbackTranscriber.Tests.Services;

[Trait(TestCategory.Name, TestCategory.Unit)]
public class TranscriptFiltersTests
{
    [Theory]
    [InlineData("Thank you.", true)]
    [InlineData("Gracias!", true)]
    [InlineData("  bye bye  ", true)]
    [InlineData("¿Gracias?", true)]
    [InlineData("Thank you for the update on the budget", false)]
    [InlineData("", false)]
    public void IsKnownHallucinatedPhrase_MatchesOnlyWholeKnownPhrases(string text, bool expected)
    {
        Assert.Equal(expected, TranscriptFilters.IsKnownHallucinatedPhrase(text));
    }

    [Theory]
    [InlineData("...", true)]
    [InlineData(" ¿? ", true)]
    [InlineData("", true)]
    [InlineData("ok", false)]
    [InlineData("42", false)]
    public void HasNoWords_IsTrueOnlyWithoutLettersOrDigits(string text, bool expected)
    {
        Assert.Equal(expected, TranscriptFilters.HasNoWords(text));
    }

    [Theory]
    [InlineData("Hola a todos", "es", false)]
    [InlineData("Привет всем", "es", true)]
    [InlineData("Привет всем", "ru", false)]
    [InlineData("Hello everyone", "ru", false)]
    [InlineData("こんにちは皆さん", "en", true)]
    [InlineData("こんにちは皆さん", "ja", false)]
    [InlineData("Привет всем", null, false)]
    [InlineData("123 456", "es", false)]
    public void IsForeignScript_FlagsTextWrittenInAnotherAlphabet(string text, string? language, bool expected)
    {
        Assert.Equal(expected, TranscriptFilters.IsForeignScript(text, language));
    }

    [Fact]
    public void IsForeignScript_ToleratesAFewForeignLetters()
    {
        Assert.False(TranscriptFilters.IsForeignScript("Hablamos con Иван ayer por la tarde sobre el presupuesto", "es"));
    }

    [Theory]
    [InlineData("no no no no no", true)]
    [InlineData("no no no", false)]
    [InlineData("vale vale vale vale", true)]
    [InlineData("Vale, vale. VALE vale", true)]
    [InlineData("vale vale vale bien bien", false)]
    [InlineData("this is a perfectly normal sentence about work", false)]
    public void IsRepeatedWordLoop_DetectsDominantRepeatedWord(string text, bool expected)
    {
        Assert.Equal(expected, TranscriptFilters.IsRepeatedWordLoop(text));
    }

    [Fact]
    public void RemovePhraseLoops_DropsAWordRepeatedSixOrMoreTimes()
    {
        Assert.Equal(string.Empty, TranscriptFilters.RemovePhraseLoops("sí sí sí sí sí sí sí"));
    }

    [Fact]
    public void RemovePhraseLoops_KeepsOneCopyOfAWordRepeatedFourOrFiveTimes()
    {
        Assert.Equal("hola ja adios", TranscriptFilters.RemovePhraseLoops("hola ja ja ja ja adios"));
    }

    [Fact]
    public void RemovePhraseLoops_LeavesAWordRepeatedThreeTimesAlone()
    {
        const string text = "muy muy muy bien";
        Assert.Equal(text, TranscriptFilters.RemovePhraseLoops(text));
    }

    [Fact]
    public void RemovePhraseLoops_KeepsOneCopyOfAPhraseRepeatedThreeTimes()
    {
        Assert.Equal(
            "vale y entonces seguimos",
            TranscriptFilters.RemovePhraseLoops("vale y entonces y entonces y entonces seguimos"));
    }

    [Fact]
    public void RemovePhraseLoops_DropsAPhraseRepeatedFourOrMoreTimes()
    {
        Assert.Equal(
            "inicio fin",
            TranscriptFilters.RemovePhraseLoops("inicio lo vemos lo vemos lo vemos lo vemos fin"));
    }

    [Fact]
    public void RemovePhraseLoops_ComparesWordsIgnoringCaseAndPunctuation()
    {
        Assert.Equal("Hola.", TranscriptFilters.RemovePhraseLoops("Hola. hola, HOLA hola!"));
    }

    [Fact]
    public void RemovePhraseLoops_LeavesNormalTextAlone()
    {
        const string text = "esto es una frase normal sin repeticiones";
        Assert.Same(text, TranscriptFilters.RemovePhraseLoops(text));
    }

    [Fact]
    public void RemoveRepeatedSegments_DropsRunsOfThreeOrMoreIdenticalSegments()
    {
        var segments = new List<TranscriptSegment>
        {
            Segment(0, "Okay"), Segment(1, "okay."), Segment(2, "Okay"),
            Segment(3, "Real content here")
        };

        var result = TranscriptFilters.RemoveRepeatedSegments(segments);

        Assert.Equal("Real content here", Assert.Single(result).Text);
    }

    [Fact]
    public void RemoveRepeatedSegments_KeepsRunsOfTwo()
    {
        var segments = new List<TranscriptSegment> { Segment(0, "Okay"), Segment(1, "Okay") };

        Assert.Equal(2, TranscriptFilters.RemoveRepeatedSegments(segments).Count);
    }

    [Fact]
    public void RemoveRepeatedSegments_OnlyCollapsesConsecutiveRepeats()
    {
        var segments = new List<TranscriptSegment>
        {
            Segment(0, "Okay"), Segment(1, "Otra cosa"), Segment(2, "Okay"), Segment(3, "Okay")
        };

        Assert.Equal(4, TranscriptFilters.RemoveRepeatedSegments(segments).Count);
    }

    [Fact]
    public void RemoveRepeatedSegments_NeverCollapsesSegmentsWithoutWords()
    {
        var segments = new List<TranscriptSegment> { Segment(0, "..."), Segment(1, "..."), Segment(2, "...") };

        Assert.Equal(3, TranscriptFilters.RemoveRepeatedSegments(segments).Count);
    }

    [Fact]
    public void WordOverlapRatio_IsOneForSameWordsAndZeroForDisjointOnes()
    {
        Assert.Equal(1.0, TranscriptFilters.WordOverlapRatio("Hello world", "world, HELLO"));
        Assert.Equal(0.0, TranscriptFilters.WordOverlapRatio("one two", "three four"));
        Assert.Equal(0.0, TranscriptFilters.WordOverlapRatio("", "three four"));
    }

    [Fact]
    public void WordOverlapRatio_IsIntersectionOverUnion()
    {
        Assert.Equal(0.5, TranscriptFilters.WordOverlapRatio("uno dos tres", "dos tres cuatro"), precision: 6);
    }

    [Fact]
    public void WordContainmentRatio_MeasuresHowMuchOfTheFirstTextIsInTheSecond()
    {
        Assert.Equal(1.0, TranscriptFilters.WordContainmentRatio("dos tres", "uno dos tres cuatro"));
        Assert.Equal(0.5, TranscriptFilters.WordContainmentRatio("uno cinco", "uno dos tres"));
        Assert.Equal(0.0, TranscriptFilters.WordContainmentRatio("uno", ""));
    }

    [Fact]
    public void MergeCloseSegments_MergesWithinTwoSecondsOnly()
    {
        var segments = new List<TranscriptSegment>
        {
            new(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(1), "uno"),
            new(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), "dos"),
            new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(11), "tres")
        };

        var result = TranscriptFilters.MergeCloseSegments(segments);

        Assert.Equal(2, result.Count);
        Assert.Equal("uno dos", result[0].Text);
        Assert.Equal(TimeSpan.Zero, result[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(3), result[0].End);
        Assert.Equal("tres", result[1].Text);
    }

    [Fact]
    public void MergeCloseSegments_StopsGrowingASegmentPastFourHundredCharacters()
    {
        var longText = new string('a', 400);
        var segments = new List<TranscriptSegment>
        {
            new(TimeSpan.Zero, TimeSpan.FromSeconds(1), longText),
            new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "siguiente")
        };

        var result = TranscriptFilters.MergeCloseSegments(segments);

        Assert.Equal(2, result.Count);
        Assert.Equal("siguiente", result[1].Text);
    }

    private static TranscriptSegment Segment(int second, string text) =>
        new(TimeSpan.FromSeconds(second), TimeSpan.FromSeconds(second + 1), text);
}
