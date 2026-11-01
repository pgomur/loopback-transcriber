namespace LoopbackTranscriber.Tests.Services;

[Trait(TestCategory.Name, TestCategory.Unit)]
public class SummarizationServiceParsingTests
{
    [Fact]
    public void ParseResult_ReadsThePlainTitleAndSummaryFormat()
    {
        var result = SummarizationService.ParseResult("TITULO: Reunión de presupuesto\nRESUMEN:\nPropósito: revisar el plan");

        Assert.Equal("Reunión de presupuesto", result.Title);
        Assert.Equal("Propósito: revisar el plan", result.Summary);
    }

    [Fact]
    public void ParseResult_AcceptsAccentsAndBoldLabels()
    {
        var result = SummarizationService.ParseResult("**Título**: Plan del trimestre\n**Resumen**: Se acordó lanzar el jueves.");

        Assert.Equal("Plan del trimestre", result.Title);
        Assert.Equal("Se acordó lanzar el jueves.", result.Summary);
    }

    [Fact]
    public void ParseResult_IgnoresTheReasoningBlock()
    {
        var result = SummarizationService.ParseResult("<think>Primero pienso en el TITULO: falso</think>\nTITULO: Real\nRESUMEN: Texto real");

        Assert.Equal("Real", result.Title);
        Assert.Equal("Texto real", result.Summary);
    }

    [Fact]
    public void ParseResult_ReadsAJsonAnswer_EvenWithBracesInsideTheStrings()
    {
        var result = SummarizationService.ParseResult(
            "Aquí tienes: {\"title\": \"Plan\", \"summary\": \"Usar {llaves} y \\\"comillas\\\"\"} y nada más");

        Assert.Equal("Plan", result.Title);
        Assert.Equal("Usar {llaves} y \"comillas\"", result.Summary);
    }

    [Fact]
    public void ParseResult_WithAnUnrecognisedAnswer_UsesTheFirstLineAsTitle()
    {
        var firstLine = new string('x', 80);

        var result = SummarizationService.ParseResult(firstLine + "\nresto del texto");

        Assert.Equal(new string('x', 60) + "...", result.Title);
        Assert.Equal(firstLine + "\nresto del texto", result.Summary);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<think>solo razonamiento</think>")]
    public void ParseResult_WithNothingUseful_StillReturnsATitleAndASummary(string raw)
    {
        var result = SummarizationService.ParseResult(raw);

        Assert.Equal("Resumen", result.Title);
        Assert.Equal("Resumen", result.Summary);
    }

    [Fact]
    public void StripThinkBlock_LeavesTextWithoutReasoningUntouched()
    {
        Assert.Equal("texto", SummarizationService.StripThinkBlock("texto"));
        Assert.Equal(" texto", SummarizationService.StripThinkBlock("<THINK>x</THINK> texto"));
    }

    [Fact]
    public void LocalizeSectionLabels_LeavesSpanishSummariesAsTheyAre()
    {
        var summary = new RecordingSummary { Title = "T", Summary = "Propósito: algo" };

        Assert.Same(summary, SummarizationService.LocalizeSectionLabels(summary, "es"));
    }

    [Fact]
    public void LocalizeSectionLabels_TranslatesEverySectionLabel_KeepingBulletsAndContent()
    {
        var summary = new RecordingSummary
        {
            Title = "Título",
            Summary = "Propósito: revisar\nPuntos clave:\n- uno\nTemas:\nObjetivos:\n- Proximos pasos: enviar acta\nTareas: nada"
        };

        var result = SummarizationService.LocalizeSectionLabels(summary, "en");

        string Label(string key) => Strings.Instance.GetIn("en", key);
        Assert.Equal("Título", result.Title);
        Assert.Equal(
            $"{Label("SummaryLabelPurpose")}: revisar\n{Label("SummaryLabelKeyPoints")}:\n- uno\n{Label("SummaryLabelTopics")}:\n" +
            $"{Label("SummaryLabelGoals")}:\n- {Label("SummaryLabelNextSteps")}: enviar acta\n{Label("SummaryLabelTasks")}: nada",
            result.Summary);
    }

    [Fact]
    public void LocalizeSectionLabels_OnlyTouchesLabelsAtTheStartOfALine()
    {
        var summary = new RecordingSummary { Title = "T", Summary = "- Hablamos de los temas: varios" };

        Assert.Equal("- Hablamos de los temas: varios", SummarizationService.LocalizeSectionLabels(summary, "en").Summary);
    }

    [Fact]
    public void SplitIntoChunks_CutsAtTheLastSentenceEnd_AndLosesNothing()
    {
        const string text = "Uno. Dos. Tres.";

        var chunks = SummarizationService.SplitIntoChunks(text, 8);

        Assert.Equal(new[] { "Uno.", " Dos.", " Tres." }, chunks);
        Assert.Equal(text, string.Concat(chunks));
    }

    [Fact]
    public void SplitIntoChunks_WithoutSentenceEnds_CutsAtTheLimit()
    {
        Assert.Equal(new[] { "abcd", "efgh", "ij" }, SummarizationService.SplitIntoChunks("abcdefghij", 4));
    }

    [Fact]
    public void SplitIntoChunks_ShortText_IsASingleChunk()
    {
        Assert.Equal(new[] { "corto" }, SummarizationService.SplitIntoChunks("corto", 100));
    }

    [Fact]
    public void FitPointsToLimit_AlwaysKeepsMarkedGoalsAndNextSteps()
    {
        const string points = "- normal uno largo\n- [OBJETIVO] lanzar\n- normal dos";

        Assert.Equal("- [OBJETIVO] lanzar\n- normal dos", SummarizationService.FitPointsToLimit(points, 35));
        Assert.Equal(points, SummarizationService.FitPointsToLimit(points, 500));
    }

    [Fact]
    public void RemoveRepeatedLines_DropsNearDuplicates_WithinEachSection()
    {
        const string text =
            "Puntos clave:\n- Hablamos del presupuesto anual\n- Hablamos del presupuesto anual\n- Otra cosa distinta aquí\n" +
            "Temas:\n- Hablamos del presupuesto anual";

        Assert.Equal(
            "Puntos clave:\n- Hablamos del presupuesto anual\n- Otra cosa distinta aquí\nTemas:\n- Hablamos del presupuesto anual",
            SummarizationService.RemoveRepeatedLines(text));
    }

    [Fact]
    public void RemoveRepeatedLines_KeepsShortLines()
    {
        Assert.Equal("- Sí\n- Sí", SummarizationService.RemoveRepeatedLines("- Sí\n- Sí"));
    }
}
