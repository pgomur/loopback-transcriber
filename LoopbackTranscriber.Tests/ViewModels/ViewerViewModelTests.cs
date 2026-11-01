using CommunityToolkit.Mvvm.Messaging;

namespace LoopbackTranscriber.Tests.ViewModels;

[Trait(TestCategory.Name, TestCategory.Integration)]
public sealed class ViewerViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lt-viewer-" + Guid.NewGuid().ToString("N"));
    private readonly WeakReferenceMessenger _messenger = new();
    private readonly FakeSummarizer _summarizer = new();
    private readonly ViewerViewModel _viewer;
    private readonly FakeDialogs _dialogs = new();
    private readonly AppSettings _appSettings;

    public ViewerViewModelTests()
    {
        Directory.CreateDirectory(_root);
        _appSettings = new AppSettings
        {
            TranscriptsFolder = Path.Combine(_root, "transcripts"),
            ModelsFolder = Path.Combine(_root, "models")
        };
        var settingsStore = new FakeSettingsStore(_appSettings);
        _viewer = new ViewerViewModel(settingsStore, _summarizer, new TranscriptHistoryService(), _dialogs, _messenger,
            new TranscriptRetranscriber(new RecordingProcessor(new FakeWhisper(), new FakeAudio()), settingsStore));
    }

    public void Dispose()
    {
        _viewer.Dispose();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string WriteTranscript(string text)
    {
        var path = Path.Combine(_root, "Zoom_20260101_100000.txt");
        File.WriteAllText(path, text);
        return path;
    }

    private static TranscriptHistoryItem CreateItem(string path, bool hasText = true, string? summaryText = null) => new()
    {
        AppName = "Zoom",
        Timestamp = new DateTime(2026, 1, 1, 10, 0, 0),
        Preview = "vista previa",
        FilePath = path,
        HasTxt = hasText,
        HasMarkdown = false,
        HasAudio = false,
        HasSummary = summaryText is not null,
        SummaryTitle = summaryText is null ? null : "Titulo",
        SummaryText = summaryText,
        SummaryFilePath = Path.ChangeExtension(path, ".summary.json"),
        Duration = null
    };

    [Fact]
    public void ViewHistoryTranscript_ShowsTheFileAndRemembersItsPath()
    {
        var path = WriteTranscript("hola mundo");

        _viewer.ViewHistoryTranscriptCommand.Execute(CreateItem(path));

        Assert.Equal("hola mundo", _viewer.TranscriptText);
        Assert.Equal(path, _viewer.ViewedFilePath);
        Assert.False(_viewer.HasViewedSummary);
    }

    [Fact]
    public void ViewHistoryTranscript_ShowsTheStoredSummary()
    {
        var path = WriteTranscript("hola mundo");

        _viewer.ViewHistoryTranscriptCommand.Execute(CreateItem(path, summaryText: "resumen"));

        Assert.True(_viewer.HasViewedSummary);
        Assert.Equal("Titulo", _viewer.ViewedSummaryTitle);
        Assert.Equal("resumen", _viewer.ViewedSummaryText);
        Assert.False(_viewer.IsShowingSummaryTab);
    }

    [Fact]
    public void ViewHistoryTranscript_IsIgnoredWhileCapturing()
    {
        var path = WriteTranscript("hola mundo");
        _messenger.Send(new CaptureStateChangedMessage(true));

        _viewer.ViewHistoryTranscriptCommand.Execute(CreateItem(path));

        Assert.Equal(string.Empty, _viewer.TranscriptText);
        Assert.Null(_viewer.ViewedFilePath);

        _messenger.Send(new CaptureStateChangedMessage(false));
        _viewer.ViewHistoryTranscriptCommand.Execute(CreateItem(path));

        Assert.Equal("hola mundo", _viewer.TranscriptText);
    }

    [Fact]
    public void TranscriptReadyMessage_ShowsTheTextAndRemembersTheFile()
    {
        _messenger.Send(new TranscriptReadyMessage("texto nuevo", "/tmp/x.md"));

        Assert.Equal("texto nuevo", _viewer.TranscriptText);
        Assert.Equal("/tmp/x.md", _viewer.ViewedFilePath);

        _messenger.Send(new TranscriptReadyMessage("solo texto", null));

        Assert.Equal("solo texto", _viewer.TranscriptText);
        Assert.Equal("/tmp/x.md", _viewer.ViewedFilePath);
    }

    [Fact]
    public void Clear_ResetsTextPathAndSummary()
    {
        var path = WriteTranscript("hola mundo");
        _viewer.ViewHistoryTranscriptCommand.Execute(CreateItem(path, summaryText: "resumen"));
        _viewer.ShowSummaryTabCommand.Execute(null);

        _viewer.ClearViewedTranscriptCommand.Execute(null);

        Assert.Equal(string.Empty, _viewer.TranscriptText);
        Assert.Null(_viewer.ViewedFilePath);
        Assert.False(_viewer.HasViewedSummary);
        Assert.Equal(string.Empty, _viewer.ViewedSummaryText);
        Assert.False(_viewer.IsShowingSummaryTab);
    }

    [Fact]
    public void TranscriptDeletedMessage_ClearsTheViewerOnlyForTheViewedFile()
    {
        var path = WriteTranscript("hola mundo");
        _viewer.ViewHistoryTranscriptCommand.Execute(CreateItem(path));

        _messenger.Send(new TranscriptDeletedMessage(Path.Combine(_root, "otra.txt")));
        Assert.Equal("hola mundo", _viewer.TranscriptText);

        _messenger.Send(new TranscriptDeletedMessage(path.ToUpperInvariant()));
        Assert.Equal(string.Empty, _viewer.TranscriptText);
        Assert.Null(_viewer.ViewedFilePath);
    }

    [Fact]
    public void SummaryButton_NeedsTheSummaryTabAndText_AndWarnsWhenTheModelIsMissing()
    {
        _viewer.RefreshSummaryModelAvailability();
        Assert.False(_viewer.IsSummaryModelAvailable);
        Assert.False(_viewer.ShowViewedSummaryButton);

        _viewer.TranscriptText = "hola";
        Assert.False(_viewer.ShowViewedSummaryButton);

        _viewer.ShowSummaryTabCommand.Execute(null);
        Assert.True(_viewer.ShowViewedSummaryButton);
        Assert.True(_viewer.ShowSummaryModelWarning);
        Assert.False(_viewer.CanGenerateViewedSummary);

        _viewer.ShowTranscriptTabCommand.Execute(null);
        Assert.False(_viewer.ShowViewedSummaryButton);
    }

    [Fact]
    public async Task GenerateSummary_WithoutTextExplainsWhy_AndDoesNotStartAnything()
    {
        var item = CreateItem(Path.Combine(_root, "x.wav"), hasText: false);

        await _viewer.GenerateSummaryCommand.ExecuteAsync(item);

        Assert.Single(_dialogs.Infos);
        Assert.False(item.IsGeneratingSummary);
        Assert.False(_viewer.IsSummaryGenerating);
    }

    [Fact]
    public async Task GenerateSummary_WithoutModelRefreshesAvailability_AndDoesNotStartAnything()
    {
        var path = WriteTranscript("hola mundo");
        var item = CreateItem(path);
        var notifications = 0;
        _messenger.Register<object, SummaryActivityChangedMessage>(this, (_, _) => notifications++);

        await _viewer.GenerateSummaryCommand.ExecuteAsync(item);

        Assert.False(_viewer.IsSummaryModelAvailable);
        Assert.False(item.IsGeneratingSummary);
        Assert.False(_viewer.IsSummaryGenerating);
        Assert.Equal(0, notifications);
        Assert.Empty(_dialogs.Errors);
    }

    private string CreateSummaryModel()
    {
        var modelPath = _appSettings.GetSummaryModelFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        File.WriteAllBytes(modelPath, [1]);
        return modelPath;
    }

    private void AnswerFindRequestsWith(TranscriptHistoryItem item) =>
        _messenger.Register<TranscriptHistoryItem, FindTranscriptRequest>(item, static (found, request) =>
            request.Reply(string.Equals(found.FilePath, request.FilePath, StringComparison.OrdinalIgnoreCase) ? found : null));

    [Fact]
    public async Task GenerateSummary_StoresTheSummary_AndAnnouncesTheActivity()
    {
        CreateSummaryModel();
        var path = WriteTranscript("hola mundo");
        var item = CreateItem(path);
        var activity = new List<bool>();
        _messenger.Register<List<bool>, SummaryActivityChangedMessage>(activity, static (list, m) => list.Add(m.IsGenerating));

        await _viewer.GenerateSummaryCommand.ExecuteAsync(item);

        Assert.Equal(1, _summarizer.Calls);
        Assert.Equal("hola mundo", _summarizer.LastTranscript);
        Assert.True(item.HasSummary);
        Assert.Equal("Titulo generado", item.SummaryTitle);
        Assert.Equal("Resumen generado", item.SummaryText);
        Assert.False(item.IsGeneratingSummary);
        Assert.False(_viewer.IsSummaryGenerating);
        Assert.Equal(new[] { true, false }, activity);
        Assert.Empty(_dialogs.Errors);
    }

    [Fact]
    public async Task GenerateSummary_ForTheViewedFile_SwitchesToTheSummaryTab()
    {
        CreateSummaryModel();
        var path = WriteTranscript("hola mundo");
        var item = CreateItem(path);
        _viewer.ViewHistoryTranscriptCommand.Execute(item);

        await _viewer.GenerateSummaryCommand.ExecuteAsync(item);

        Assert.True(_viewer.HasViewedSummary);
        Assert.Equal("Resumen generado", _viewer.ViewedSummaryText);
        Assert.True(_viewer.IsShowingSummaryTab);
    }

    [Fact]
    public async Task GenerateSummary_WhenTheSummarizerFails_ReportsTheErrorAndReleasesTheItem()
    {
        CreateSummaryModel();
        _summarizer.Fail = true;
        var item = CreateItem(WriteTranscript("hola mundo"));

        await _viewer.GenerateSummaryCommand.ExecuteAsync(item);

        Assert.Single(_dialogs.Errors);
        Assert.False(item.HasSummary);
        Assert.False(item.IsGeneratingSummary);
        Assert.False(_viewer.IsSummaryGenerating);
    }

    [Fact]
    public async Task GenerateSummary_IsIgnoredWhileCapturing()
    {
        CreateSummaryModel();
        var item = CreateItem(WriteTranscript("hola mundo"));
        _messenger.Send(new CaptureStateChangedMessage(true));

        await _viewer.GenerateSummaryCommand.ExecuteAsync(item);

        Assert.Equal(0, _summarizer.Calls);
    }

    [Fact]
    public async Task GenerateSummary_WhileRunning_MarksTheViewerAsGenerating()
    {
        CreateSummaryModel();
        _summarizer.Gate = new TaskCompletionSource();
        var item = CreateItem(WriteTranscript("hola mundo"));

        var running = _viewer.GenerateSummaryCommand.ExecuteAsync(item);
        await Eventually.Until(() => _summarizer.Calls == 1, "the summary started");

        Assert.True(item.IsGeneratingSummary);
        Assert.True(_viewer.IsSummaryGenerating);

        _summarizer.Gate.SetResult();
        await running;

        Assert.False(_viewer.IsSummaryGenerating);
    }

    [Fact]
    public async Task RegenerateViewedSummary_ResolvesTheViewedItemThroughTheMessenger()
    {
        CreateSummaryModel();
        var path = WriteTranscript("hola mundo");
        var item = CreateItem(path);
        AnswerFindRequestsWith(item);
        _messenger.Send(new TranscriptReadyMessage("hola mundo", path));

        await _viewer.RegenerateViewedSummaryCommand.ExecuteAsync(null);

        Assert.Equal(1, _summarizer.Calls);
        Assert.True(item.HasSummary);
        Assert.False(_viewer.IsViewedSummaryGenerating);
    }

    [Fact]
    public async Task RegenerateViewedSummary_DoesNothingWhenNobodyAnswersTheRequest()
    {
        CreateSummaryModel();
        _messenger.Send(new TranscriptReadyMessage("hola mundo", Path.Combine(_root, "huerfano.txt")));

        await _viewer.RegenerateViewedSummaryCommand.ExecuteAsync(null);

        Assert.Equal(0, _summarizer.Calls);
    }
}
