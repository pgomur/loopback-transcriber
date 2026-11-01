namespace LoopbackTranscriber.Tests.ViewModels;

[Trait(TestCategory.Name, TestCategory.Unit)]
public class LiveTranscriptModelTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0);

    [Fact]
    public void AddLine_WithoutTranslation_ShowsTheOriginalOnly()
    {
        var model = new LiveTranscriptModel();

        var line = model.AddLine(1, "hola a todos", Now, showOriginal: true);

        Assert.False(line.ShowTranslation);
        Assert.True(line.ShowOriginalMain);
    }

    [Fact]
    public void AddLine_WithTranslation_UsesTheDraftTranslationAsProvisional_AndClearsDrafts()
    {
        var model = new LiveTranscriptModel { TranslationAvailable = true };
        model.SetDraft("hola a todos los presentes");
        model.SetDraftTranslation("hello everyone present");

        var line = model.AddLine(1, "hola a todos los presentes", Now, showOriginal: true);

        Assert.True(line.ShowTranslation);
        Assert.Equal("hello everyone present", line.TranslationDisplay);
        Assert.Equal(string.Empty, model.DraftText);
        Assert.Equal(string.Empty, model.DraftTranslationText);
    }

    [Fact]
    public void SetTranslation_ReplacesTheProvisionalOne_ForThatLineOnly()
    {
        var model = new LiveTranscriptModel { TranslationAvailable = true };
        var first = model.AddLine(1, "uno", Now, true);
        var second = model.AddLine(2, "dos", Now, true);

        model.SetTranslation(2, "two");

        Assert.Equal("two", second.TranslationDisplay);
        Assert.False(first.ShowTranslation);
    }

    [Fact]
    public void AddLine_KeepsOnlyTheLatestMaxLines()
    {
        var model = new LiveTranscriptModel();

        for (var i = 0; i < LiveTranscriptModel.MaxLines + 5; i++)
        {
            model.AddLine(i, "linea " + i, Now, true);
        }

        Assert.Equal(LiveTranscriptModel.MaxLines, model.Lines.Count);
        Assert.Equal(5, model.Lines[0].Id);
    }

    [Fact]
    public void SetDraft_IgnoresATextMuchShorterThanTheCurrentLongOne()
    {
        var model = new LiveTranscriptModel();
        var current = new string('a', 30);
        model.SetDraft(current);

        Assert.False(model.SetDraft(new string('b', 10)));
        Assert.Equal(current, model.DraftText);

        Assert.True(model.SetDraft(new string('c', 25)));
        Assert.Equal(new string('c', 25), model.DraftText);
    }

    [Fact]
    public void SetDraft_AcceptsAnyTextWhileTheCurrentOneIsShort()
    {
        var model = new LiveTranscriptModel();
        model.SetDraft("hola que tal");

        Assert.True(model.SetDraft("hola"));
        Assert.Equal("hola", model.DraftText);
    }

    [Fact]
    public void SetDraft_Empty_ClearsTheDraftAndItsTranslation()
    {
        var model = new LiveTranscriptModel { TranslationAvailable = true };
        model.SetDraft("hola a todos");
        model.SetDraftTranslation("hello all");

        Assert.True(model.SetDraft(string.Empty));

        Assert.Equal(string.Empty, model.DraftText);
        Assert.Equal(string.Empty, model.DraftTranslationText);
    }

    [Fact]
    public void SetDraftTranslation_NeedsADraft_AndIgnoresAMuchShorterOne()
    {
        var model = new LiveTranscriptModel { TranslationAvailable = true };

        Assert.False(model.SetDraftTranslation("hello"));

        model.SetDraft("una frase bastante larga para el borrador");
        Assert.True(model.SetDraftTranslation("a fairly long sentence for the draft"));
        Assert.False(model.SetDraftTranslation("a fairly"));
        Assert.Equal("a fairly long sentence for the draft", model.DraftTranslationText);
    }

    [Fact]
    public void DraftMainText_FollowsWhetherTranslationIsAvailable()
    {
        var model = new LiveTranscriptModel();
        model.SetDraft("hola");
        Assert.Equal("hola", model.DraftMainText);
        Assert.Equal(string.Empty, model.DraftOriginalText);

        model.TranslationAvailable = true;
        model.SetDraftTranslation("hello");
        Assert.Equal("hello", model.DraftMainText);
        Assert.Equal("hola", model.DraftOriginalText);
    }

    [Fact]
    public void TranslationFailed_StopsTranslating_AndReleasesThePendingLines()
    {
        var model = new LiveTranscriptModel { TranslationAvailable = true };
        var pending = model.AddLine(1, "uno", Now, true);
        var translated = model.AddLine(2, "dos", Now, true);
        model.SetTranslation(2, "two");
        Assert.Equal(0.6, pending.MainOpacity);

        model.TranslationFailed();

        Assert.False(model.TranslationAvailable);
        Assert.Equal(1.0, pending.MainOpacity);
        Assert.Equal("two", translated.TranslationDisplay);
    }

    [Fact]
    public void UpdateEmphasis_FadesOlderLines_AndCountsAVisibleDraftAsTheLatest()
    {
        var model = new LiveTranscriptModel();
        model.AddLine(1, "uno", Now, true);
        model.AddLine(2, "dos", Now, true);
        model.AddLine(3, "tres", Now, true);

        model.UpdateEmphasis(draftShown: false);
        Assert.True(model.Lines[2].IsCurrent);
        Assert.Equal(1.0, model.Lines[2].Emphasis);
        Assert.Equal(0.75, model.Lines[1].Emphasis);
        Assert.Equal(0.6, model.Lines[0].Emphasis);

        model.UpdateEmphasis(draftShown: true);
        Assert.False(model.Lines[2].IsCurrent);
        Assert.Equal(0.75, model.Lines[2].Emphasis);
    }

    [Fact]
    public void Reset_ClearsEverything()
    {
        var model = new LiveTranscriptModel { TranslationAvailable = true };
        model.AddLine(1, "uno", Now, true);
        model.SetDraft("borrador");

        model.Reset();

        Assert.Empty(model.Lines);
        Assert.Equal(string.Empty, model.DraftText);
        Assert.False(model.TranslationAvailable);
    }
}
