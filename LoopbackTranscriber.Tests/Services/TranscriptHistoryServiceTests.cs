namespace LoopbackTranscriber.Tests.Services;

[Trait(TestCategory.Name, TestCategory.Integration)]
public sealed class TranscriptHistoryServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly UserNotices _notices = new();
    private readonly TranscriptHistoryService _service;
    private readonly string _root;

    public TranscriptHistoryServiceTests()
    {
        _service = new TranscriptHistoryService(_notices);
        _root = _temp.CreateFolder("transcripts");
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void WithoutTheFolder_TheHistoryIsEmpty()
    {
        Assert.Empty(_service.LoadFromDisk(_temp.Combine("does-not-exist")));
    }

    [Fact]
    public void RecordingsAreFoundInAppFolders_AndInTheOldFlatLayout_NewestFirst()
    {
        WriteRecording("Teams", "Teams_20260101_100000", md: "# Teams\n\nUno");
        WriteRecording(null, "Zoom_20260301_100000", txt: "Dos");
        WriteRecording("Zoom", "Zoom_20260201_100000", md: "# Zoom\n\nTres");

        var items = _service.LoadFromDisk(_root);

        Assert.Equal(new[] { "Zoom", "Zoom", "Teams" }, items.Select(i => i.AppName));
        Assert.Equal(new DateTime(2026, 3, 1, 10, 0, 0), items[0].Timestamp);
    }

    [Fact]
    public void AppNamesWithUnderscores_AreReadCorrectly()
    {
        WriteRecording("Google_Meet", "Google_Meet_20260101_100000", md: "Texto");

        Assert.Equal("Google_Meet", Assert.Single(_service.LoadFromDisk(_root)).AppName);
    }

    [Fact]
    public void ThePreviewSkipsTheHeaderAndTheSpeakerLabels()
    {
        WriteRecording("Teams", "Teams_20260101_100000",
            md: $"# Teams - 01/01/2026 10:00\n\n{SpeakerLabels.Others}: Buenos días.\n\npgomur (Tú): Hola.");

        var item = Assert.Single(_service.LoadFromDisk(_root));

        Assert.Equal("Buenos días. Hola.", item.Preview);
        Assert.True(item.HasMarkdown);
        Assert.False(item.HasTxt);
        Assert.EndsWith(".md", item.FilePath);
    }

    [Fact]
    public void ALongPreview_IsCutAtOneHundredCharacters()
    {
        WriteRecording("Teams", "Teams_20260101_100000", txt: new string('a', 150));

        Assert.Equal(new string('a', 100) + "...", Assert.Single(_service.LoadFromDisk(_root)).Preview);
    }

    [Fact]
    public void ATranscriptWithOnlyASpeakerLabel_HasAPlaceholderPreview()
    {
        WriteRecording("Teams", "Teams_20260101_100000", txt: SpeakerLabels.Others + ": ");

        Assert.Equal(Strings.Instance.Get("HistoryEmptyTranscript"), Assert.Single(_service.LoadFromDisk(_root)).Preview);
    }

    [Fact]
    public void ATxtTranscript_TakesPrecedenceOverMarkdown()
    {
        WriteRecording("Teams", "Teams_20260101_100000", md: "markdown", txt: "texto plano");

        var item = Assert.Single(_service.LoadFromDisk(_root));

        Assert.EndsWith(".txt", item.FilePath);
        Assert.Equal("texto plano", item.Preview);
    }

    [Fact]
    public void TheSummary_IsShownInThePreview_AndItsDetailsAreLoaded()
    {
        var folder = WriteRecording("Teams", "Teams_20260101_100000", md: "Transcripción");
        _service.SaveSummary(Path.Combine(folder, "Teams_20260101_100000.summary.json"),
            new RecordingSummary { Title = "Plan", Summary = "Se acordó el plan." });

        var item = Assert.Single(_service.LoadFromDisk(_root));

        Assert.True(item.HasSummary);
        Assert.Equal("Plan", item.SummaryTitle);
        Assert.Equal("Se acordó el plan.", item.SummaryText);
        Assert.Equal("Se acordó el plan.", item.Preview);
    }

    [Fact]
    public void ACorruptSummary_IsIgnored()
    {
        var folder = WriteRecording("Teams", "Teams_20260101_100000", md: "Transcripción");
        File.WriteAllText(Path.Combine(folder, "Teams_20260101_100000.summary.json"), "{ roto");

        var item = Assert.Single(_service.LoadFromDisk(_root));

        Assert.False(item.HasSummary);
        Assert.Equal("Transcripción", item.Preview);
    }

    [Fact]
    public void TheAudioDuration_IsRead()
    {
        var folder = WriteRecording("Teams", "Teams_20260101_100000", md: "Texto");
        WavFile.WriteTone(Path.Combine(folder, "Teams_20260101_100000.wav"), 1.5, 0.5);

        var item = Assert.Single(_service.LoadFromDisk(_root));

        Assert.True(item.HasAudio);
        Assert.Equal(TimeSpan.FromSeconds(1.5), item.Duration);
    }

    [Fact]
    public void AFolderWithOnlyAudio_IsStillListed()
    {
        var folder = _temp.CreateFolder("transcripts", "Teams", "Teams_20260101_100000");
        WavFile.WriteTone(Path.Combine(folder, "Teams_20260101_100000.wav"), 0.5, 0.5);

        var item = Assert.Single(_service.LoadFromDisk(_root));

        Assert.EndsWith(".wav", item.FilePath);
        Assert.Equal(string.Empty, item.Preview);
    }

    [Fact]
    public void FoldersWithoutTranscripts_AreIgnored()
    {
        _temp.CreateFolder("transcripts", "Teams", "vacia");
        _temp.WriteText(Path.Combine("transcripts", "Teams", "otra", "notas.txt"), "no es una transcripción");

        Assert.Empty(_service.LoadFromDisk(_root));
    }

    [Fact]
    public void AFolderNameWithoutDate_UsesTheNameAsApp()
    {
        WriteRecording(null, "notas", txt: "texto");

        Assert.Equal("notas", Assert.Single(_service.LoadFromDisk(_root)).AppName);
    }

    [Fact]
    public void HasTranscripts_FindsTheAppInBothLayouts()
    {
        WriteRecording("Teams", "Teams_20260101_100000", md: "uno");
        WriteRecording(null, "Zoom_20260101_100000", txt: "dos");

        Assert.True(_service.HasTranscripts(_root, "Teams"));
        Assert.True(_service.HasTranscripts(_root, "zoom"));
        Assert.False(_service.HasTranscripts(_root, "Meet"));
        Assert.False(_service.HasTranscripts(_temp.Combine("does-not-exist"), "Teams"));
    }

    [Fact]
    public void Deleting_RemovesTheRecordingFolder_AndTheAppFolderWhenItBecomesEmpty()
    {
        WriteRecording("Teams", "Teams_20260101_100000", md: "uno");
        WriteRecording("Teams", "Teams_20260201_100000", md: "dos");
        var items = _service.LoadFromDisk(_root);

        Assert.True(_service.TryDeleteTranscriptFile(items[0], out var firstError));
        Assert.Null(firstError);
        Assert.True(Directory.Exists(Path.Combine(_root, "Teams")));

        Assert.True(_service.TryDeleteTranscriptFile(items[1], out _));
        Assert.False(Directory.Exists(Path.Combine(_root, "Teams")));
        Assert.Empty(_service.LoadFromDisk(_root));
    }

    [Fact]
    public void Deleting_AnOldStyleLooseFile_AlsoRemovesItsAudio()
    {
        var transcript = _temp.WriteText(Path.Combine("transcripts", "suelto.txt"), "texto");
        var audio = WavFile.WriteTone(Path.Combine(_root, "suelto.wav"), 0.2, 0.5);
        var item = new TranscriptHistoryItem
        {
            AppName = "Teams",
            Timestamp = DateTime.Now,
            Preview = "texto",
            FilePath = transcript,
            HasTxt = true,
            HasMarkdown = false,
            HasAudio = true,
            SummaryFilePath = Path.ChangeExtension(transcript, ".summary.json"),
            Duration = null
        };

        Assert.True(_service.TryDeleteTranscriptFile(item, out _));
        Assert.False(File.Exists(transcript));
        Assert.False(File.Exists(audio));
    }

    [Fact]
    public void Deleting_AFileThatIsInUse_ReportsTheError()
    {
        var folder = WriteRecording("Teams", "Teams_20260101_100000", md: "uno");
        var item = Assert.Single(_service.LoadFromDisk(_root));

        using (new FileStream(Path.Combine(folder, "Teams_20260101_100000.md"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(_service.TryDeleteTranscriptFile(item, out var error));
            Assert.NotNull(error);
        }
    }

    [Theory]
    [InlineData("Teams", "Teams")]
    [InlineData("Meet: equipo/ventas", "Meet_ equipo_ventas")]
    [InlineData("Zoom.", "Zoom")]
    [InlineData("  ", "_")]
    [InlineData("...", "_")]
    public void GetAppFolderName_ProducesAValidFolderName(string appName, string expected)
    {
        Assert.Equal(expected, TranscriptHistoryService.GetAppFolderName(appName));
    }

    private string WriteRecording(string? appFolder, string baseName, string? md = null, string? txt = null)
    {
        var folder = appFolder is null
            ? _temp.CreateFolder("transcripts", baseName)
            : _temp.CreateFolder("transcripts", appFolder, baseName);

        if (md is not null)
        {
            File.WriteAllText(Path.Combine(folder, baseName + ".md"), md);
        }

        if (txt is not null)
        {
            File.WriteAllText(Path.Combine(folder, baseName + ".txt"), txt);
        }

        return folder;
    }
}
