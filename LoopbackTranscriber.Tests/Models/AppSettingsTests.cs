namespace LoopbackTranscriber.Tests.Models;

[Trait(TestCategory.Name, TestCategory.Unit)]
public class AppSettingsTests
{
    [Theory]
    [InlineData("turbo", "turbo")]
    [InlineData("  Turbo ", "turbo")]
    [InlineData("TURBO", "turbo")]
    [InlineData("", "turbo")]
    [InlineData(null, "turbo")]
    public void ModelSize_IsNormalisedOnAssignment(string? input, string expected)
    {
        var settings = new AppSettings { ModelSize = input! };

        Assert.Equal(expected, settings.ModelSize);
        Assert.Equal($"ggml-{expected}.bin", settings.GetModelFileName());
    }

    [Fact]
    public void ModelPaths_LiveInTheirOwnSubfoldersOfTheModelsFolder()
    {
        var settings = new AppSettings { ModelsFolder = Path.Combine("C:", "modelos") };

        Assert.Equal(Path.Combine("C:", "modelos", "whisper", "ggml-turbo.bin"), settings.GetModelFilePath());
        Assert.Equal(Path.Combine("C:", "modelos", "llm", AppSettings.SummaryModelFileName), settings.GetSummaryModelFilePath());
    }

    [Theory]
    [InlineData("", "de", "de")]
    [InlineData("   ", "pt", "pt")]
    [InlineData(" EN ", "es", "en")]
    [InlineData("auto", "es", AppSettings.MeetingLanguageAuto)]
    public void ResolveMeetingLanguage_FollowsTheInterfaceLanguageUnlessOneIsChosen(string meeting, string ui, string expected)
    {
        var settings = new AppSettings { MeetingLanguage = meeting, UiLanguage = ui };

        Assert.Equal(expected, settings.ResolveMeetingLanguage());
    }

    [Fact]
    public void UiLanguage_DefaultsToOneOfTheSupportedLanguages()
    {
        Assert.Contains(new AppSettings().UiLanguage, new[] { "es", "en", "pt", "de", "ru" });
    }
}
