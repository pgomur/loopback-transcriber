namespace LoopbackTranscriber.Tests.Services;

[Trait(TestCategory.Name, TestCategory.Integration)]
public sealed class TranscriptSaverTests : IDisposable
{
    private static readonly DateTime RecordedAt = new(2026, 10, 18, 9, 30, 15);

    private readonly TempDirectory _temp = new();
    private readonly AppSettings _settings;
    private readonly TranscriptSaver _saver = new();

    public TranscriptSaverTests()
    {
        _settings = TestSettings.In(_temp.Path);
    }

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData("")]
    [InlineData("   \n ")]
    public void AnEmptyTranscript_IsNotSaved(string text)
    {
        Assert.Null(_saver.Save(text, _settings, null, null, TimeSpan.Zero, "Teams", RecordedAt));
        Assert.False(Directory.Exists(_settings.TranscriptsFolder));
    }

    [Fact]
    public void TheTranscriptIsSavedInItsOwnFolderInsideTheAppFolder_WithAHeader()
    {
        var path = _saver.Save("Hola a todos.", _settings, null, null, TimeSpan.Zero, "Teams", RecordedAt);

        Assert.Equal(Path.Combine(_settings.TranscriptsFolder, "Teams", "Teams_20261018_093015", "Teams_20261018_093015.md"), path);
        Assert.Equal(TranscriptSaver.BuildHeader("Teams", RecordedAt) + "\n\nHola a todos.", File.ReadAllText(path!));
        Assert.StartsWith("# Teams - ", TranscriptSaver.BuildHeader("Teams", RecordedAt));
    }

    [Fact]
    public void BothAudioTracksAreCopied_AndTheMicrophoneOffsetIsRemembered()
    {
        var source = WavFile.WriteTone(_temp.Combine("tmp", "meeting.wav"), 0.2, 0.5);
        var mic = WavFile.WriteTone(_temp.Combine("tmp", "mic.wav"), 0.2, 0.5);

        var path = _saver.Save("Texto.", _settings, source, mic, TimeSpan.FromMilliseconds(320), "Teams", RecordedAt)!;

        var folder = Path.GetDirectoryName(path)!;
        Assert.True(File.Exists(Path.Combine(folder, "Teams_20261018_093015.wav")));
        Assert.True(File.Exists(Path.Combine(folder, "Teams_20261018_093015_mic.wav")));
        Assert.True(File.Exists(source), "The originals are left for the caller to delete");
        var metadata = RecordingMetadata.TryRead(Path.Combine(folder, "Teams_20261018_093015" + TranscriptSaver.MetadataExtension));
        Assert.NotNull(metadata);
        Assert.Equal(TimeSpan.FromMilliseconds(320).Ticks, metadata.MicStartOffsetTicks);
    }

    [Fact]
    public void WithoutMicrophone_NoMetadataIsWritten()
    {
        var source = WavFile.WriteTone(_temp.Combine("tmp", "meeting.wav"), 0.2, 0.5);

        var path = _saver.Save("Texto.", _settings, source, null, TimeSpan.Zero, "Teams", RecordedAt)!;

        Assert.Equal(2, Directory.GetFiles(Path.GetDirectoryName(path)!).Length);
    }

    [Fact]
    public void AnAppNameWithCharactersNotAllowedInFileNames_IsStillSaved()
    {
        var path = _saver.Save("Texto.", _settings, null, null, TimeSpan.Zero, "Meet: equipo/ventas", RecordedAt);

        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.Equal(_settings.TranscriptsFolder, Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(path))));
    }

    [Fact]
    public void SavingTheSameRecordingAgain_OverwritesIt()
    {
        _saver.Save("Primera versión.", _settings, null, null, TimeSpan.Zero, "Teams", RecordedAt);

        var path = _saver.Save("Segunda versión.", _settings, null, null, TimeSpan.Zero, "Teams", RecordedAt)!;

        Assert.EndsWith("Segunda versión.", File.ReadAllText(path));
    }

    [Fact]
    public void RecordingMetadata_ReadsNothingFromAMissingOrCorruptFile()
    {
        Assert.Null(RecordingMetadata.TryRead(_temp.Combine("missing.recording.json")));
        Assert.Null(RecordingMetadata.TryRead(_temp.WriteText("broken.recording.json", "{ roto")));
    }
}
