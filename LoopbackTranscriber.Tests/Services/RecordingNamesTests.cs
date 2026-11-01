namespace LoopbackTranscriber.Tests.Services;

[Trait(TestCategory.Name, TestCategory.Unit)]
public class RecordingNamesTests
{
    [Fact]
    public void FilePrefixes_ListTheCurrentPrefixFirst_AndKeepTheLegacyOne()
    {
        Assert.Equal(new[] { "recording_", "grabacion_" }, RecordingNames.FilePrefixes);
    }

    [Theory]
    [InlineData("Meeting", true)]
    [InlineData("meeting", true)]
    [InlineData("reunion", true)]
    [InlineData("REUNION", true)]
    [InlineData("Teams", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsDefaultAppName_RecognisesTheCurrentAndTheLegacyName(string? name, bool expected)
    {
        Assert.Equal(expected, RecordingNames.IsDefaultAppName(name));
    }

    [Fact]
    public void Display_TranslatesOnlyTheDefaultName()
    {
        Assert.Equal(Strings.Instance.Get("DefaultAppName"), RecordingNames.Display("Meeting"));
        Assert.Equal("Teams", RecordingNames.Display("Teams"));
    }
}
