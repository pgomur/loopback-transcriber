namespace LoopbackTranscriber.Tests.Services;

[Trait(TestCategory.Name, TestCategory.Unit)]
public class StringsTests
{
    [Fact]
    public void UnknownKey_IsReturnedAsIs_SoAMissingTranslationIsVisibleInsteadOfBlank()
    {
        Assert.Equal("ThisKeyDoesNotExist", Strings.Instance.Get("ThisKeyDoesNotExist"));
    }

    [Fact]
    public void GetIn_ReturnsTheRequestedLanguage()
    {
        Assert.Equal("Löschen", Strings.Instance.GetIn("de", "Delete"));
        Assert.Equal("Delete", Strings.Instance.GetIn("en", "Delete"));
    }

    [Fact]
    public void GetIn_FallsBackToEnglishForAnUnsupportedLanguage()
    {
        Assert.Equal(Strings.Instance.GetIn("en", "Delete"), Strings.Instance.GetIn("fr", "Delete"));
    }

    [Fact]
    public void FormatIn_FillsThePlaceholders()
    {
        Assert.Equal("Unexpected error (boom)", Strings.Instance.FormatIn("en", "ErrorUnexpected", "boom"));
    }

    [Fact]
    public void Indexer_IsTheSameAsGet()
    {
        Assert.Equal(Strings.Instance.Get("Delete"), Strings.Instance["Delete"]);
    }
}
