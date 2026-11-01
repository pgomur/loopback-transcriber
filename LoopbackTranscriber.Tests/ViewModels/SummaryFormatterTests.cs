namespace LoopbackTranscriber.Tests.ViewModels;

[Trait(TestCategory.Name, TestCategory.Unit)]
public class SummaryFormatterTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  ")]
    public void NoText_GivesNoSections(string? text)
    {
        Assert.Empty(SummaryFormatter.Parse(text));
    }

    [Fact]
    public void KnownLabels_BecomeSectionsWithHeadings_InTheOrderTheyAppear()
    {
        var sections = SummaryFormatter.Parse("Propósito: revisar el plan\nPuntos clave:\n- uno\nPróximos pasos:\n- enviar acta");

        Assert.Equal(
            new[] { "SummaryLabelPurpose", "SummaryLabelKeyPoints", "SummaryLabelNextSteps" }.Select(Strings.Instance.Get),
            sections.Select(s => s.Heading));
        Assert.All(sections, s => Assert.True(s.HasHeading));
        Assert.Equal("revisar el plan", Assert.Single(sections[0].Lines).Text);
        Assert.Equal("enviar acta", Assert.Single(sections[2].Lines).Text);
    }

    [Fact]
    public void BulletsTopicsAndNestedBullets_AreRecognised()
    {
        var section = Assert.Single(SummaryFormatter.Parse("Puntos clave:\n- uno\n* Tema A\n- detalle\nlínea suelta"));

        Assert.Collection(
            section.Lines,
            line => { Assert.Equal("uno", line.Text); Assert.True(line.IsBullet); Assert.Equal(0, line.Level); },
            line => { Assert.Equal("Tema A", line.Text); Assert.True(line.IsTopic); },
            line => { Assert.Equal("detalle", line.Text); Assert.True(line.IsBullet); Assert.Equal(1, line.Level); },
            line => { Assert.Equal("línea suelta", line.Text); Assert.False(line.IsBullet); Assert.Equal(1, line.Level); });
    }

    [Theory]
    [InlineData("Key points:")]
    [InlineData("**Key points**:")]
    [InlineData("- Puntos clave:")]
    [InlineData("## Puntos clave:")]
    [InlineData("Kernpunkte:")]
    [InlineData("Ключевые моменты:")]
    public void LabelsInAnyLanguageAndWithMarkdown_AreRecognised(string header)
    {
        var section = Assert.Single(SummaryFormatter.Parse(header + "\n- uno"));

        Assert.Equal(Strings.Instance.Get("SummaryLabelKeyPoints"), section.Heading);
    }

    [Fact]
    public void TextBeforeTheFirstLabel_IsASectionWithoutHeading()
    {
        var sections = SummaryFormatter.Parse("Una introducción.\nTemas:\n- presupuesto");

        Assert.Equal(2, sections.Count);
        Assert.False(sections[0].HasHeading);
        Assert.Equal(string.Empty, sections[0].Heading);
        Assert.Equal("Una introducción.", Assert.Single(sections[0].Lines).Text);
        Assert.Equal(Strings.Instance.Get("SummaryLabelTopics"), sections[1].Heading);
    }

    [Fact]
    public void AnUnknownLabel_IsKeptAsPlainText()
    {
        var section = Assert.Single(SummaryFormatter.Parse("Temas:\nNota: no es una sección"));

        Assert.Equal("Nota: no es una sección", Assert.Single(section.Lines).Text);
    }

    [Fact]
    public void ALabelWithBoldContentOnTheSameLine_KeepsTheContentWithoutMarkdown()
    {
        var section = Assert.Single(SummaryFormatter.Parse("Propósito: **cerrar el presupuesto**"));

        Assert.Equal("cerrar el presupuesto", Assert.Single(section.Lines).Text);
    }

    [Fact]
    public void WindowsLineEndings_AreHandled()
    {
        var sections = SummaryFormatter.Parse("Temas:\r\n- uno\r\nTareas:\r\n- dos");

        Assert.Equal(2, sections.Count);
        Assert.Equal("dos", Assert.Single(sections[1].Lines).Text);
    }
}
