using CommunityToolkit.Mvvm.Messaging;

namespace LoopbackTranscriber.Tests.ViewModels;

[Trait(TestCategory.Name, TestCategory.Integration)]
public sealed class HistoryViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lt-history-" + Guid.NewGuid().ToString("N"));
    private readonly WeakReferenceMessenger _messenger = new();
    private readonly List<HistoryViewModel> _created = [];

    public HistoryViewModelTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        foreach (var viewModel in _created)
        {
            viewModel.Dispose();
        }

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private HistoryViewModel CreateViewModel(bool confirmDeletes = true, FakeDialogs? dialogs = null)
    {
        var viewModel = new HistoryViewModel(
            new FakeSettingsStore(new AppSettings { TranscriptsFolder = _root }),
            new TranscriptHistoryService(),
            new TranscriptsFolderWatcher(),
            dialogs ?? new FakeDialogs(confirmDeletes),
            _messenger);
        _created.Add(viewModel);
        return viewModel;
    }

    private string AddRecording(string app, string stamp)
    {
        var name = $"{app}_{stamp}";
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name + ".txt");
        File.WriteAllText(path, "texto de prueba");
        return path;
    }

    [Fact]
    public void Reload_LoadsEveryRecording_AndFiltersTheAppListBySelectedApp()
    {
        AddRecording("Zoom", "20260101_100000");
        AddRecording("Zoom", "20260301_100000");
        AddRecording("Teams", "20260301_110000");
        var viewModel = CreateViewModel();

        viewModel.SetSelectedApp("Zoom");
        viewModel.Reload();

        Assert.Equal(3, viewModel.TranscriptHistory.Count);
        Assert.Equal(3, viewModel.FilteredTranscriptHistory.Count);
        Assert.Equal(2, viewModel.AppTranscriptHistory.Count);
        Assert.All(viewModel.AppTranscriptHistory, i => Assert.Equal("Zoom", i.AppName));
        Assert.True(viewModel.HasHistoryItems);
        Assert.True(viewModel.HasAppHistoryItems);
    }

    [Fact]
    public void SetSelectedApp_RefreshesTheAppListWithoutReloadingFromDisk()
    {
        AddRecording("Zoom", "20260101_100000");
        AddRecording("Teams", "20260301_110000");
        var viewModel = CreateViewModel();
        viewModel.Reload();

        viewModel.SetSelectedApp("Teams");
        Assert.Single(viewModel.AppTranscriptHistory);
        Assert.Equal("Teams", viewModel.AppTranscriptHistory[0].AppName);

        viewModel.SetSelectedApp(null);
        Assert.Empty(viewModel.AppTranscriptHistory);
        Assert.False(viewModel.HasAppHistoryItems);
    }

    [Fact]
    public void DateFilter_NarrowsTheGlobalListButNotTheLoadedHistory()
    {
        AddRecording("Zoom", "20260101_100000");
        AddRecording("Zoom", "20260301_100000");
        var viewModel = CreateViewModel();
        viewModel.Reload();

        viewModel.Filter.FromDate = new DateTime(2026, 2, 1);

        Assert.True(viewModel.Filter.HasActiveFilter);
        Assert.Equal(2, viewModel.TranscriptHistory.Count);
        Assert.Single(viewModel.FilteredTranscriptHistory);

        viewModel.Filter.ClearCommand.Execute(null);

        Assert.False(viewModel.Filter.HasActiveFilter);
        Assert.Equal(2, viewModel.FilteredTranscriptHistory.Count);
    }

    [Fact]
    public void RecordingSavedMessage_ReloadsTheHistory()
    {
        var viewModel = CreateViewModel();
        viewModel.Reload();
        Assert.Empty(viewModel.TranscriptHistory);

        AddRecording("Zoom", "20260101_100000");
        _messenger.Send(new RecordingSavedMessage());

        Assert.Single(viewModel.TranscriptHistory);
    }

    [Fact]
    public void Dispose_StopsListeningToMessages()
    {
        var viewModel = CreateViewModel();
        viewModel.Reload();
        viewModel.Dispose();

        AddRecording("Zoom", "20260101_100000");
        _messenger.Send(new RecordingSavedMessage());

        Assert.Empty(viewModel.TranscriptHistory);
    }

    [Fact]
    public void DeleteSelectedTranscripts_RemovesFilesAndNotifiesEachDeletedPath()
    {
        var keep = AddRecording("Zoom", "20260101_100000");
        var gone = AddRecording("Zoom", "20260301_100000");
        var viewModel = CreateViewModel();
        viewModel.Reload();
        var deleted = new List<string>();
        _messenger.Register<List<string>, TranscriptDeletedMessage>(deleted, static (list, message) => list.Add(message.FilePath));

        viewModel.FilteredTranscriptHistory.Single(i => i.FilePath == gone).IsSelected = true;
        Assert.True(viewModel.HasSelectedHistoryItems);
        viewModel.DeleteSelectedTranscriptsCommand.Execute(null);

        Assert.False(File.Exists(gone));
        Assert.True(File.Exists(keep));
        Assert.Single(deleted);
        Assert.Equal(gone, deleted[0]);
        Assert.Single(viewModel.TranscriptHistory);
        Assert.Single(viewModel.FilteredTranscriptHistory);
        Assert.False(viewModel.HasSelectedHistoryItems);
    }

    [Fact]
    public void DeleteSelectedTranscripts_DoesNothingWhenTheUserDeclines()
    {
        var path = AddRecording("Zoom", "20260101_100000");
        var dialogs = new FakeDialogs(confirm: false);
        var viewModel = CreateViewModel(dialogs: dialogs);
        viewModel.Reload();
        var deleted = new List<string>();
        _messenger.Register<List<string>, TranscriptDeletedMessage>(deleted, static (list, message) => list.Add(message.FilePath));

        viewModel.FilteredTranscriptHistory[0].IsSelected = true;
        viewModel.DeleteSelectedTranscriptsCommand.Execute(null);

        Assert.Equal(1, dialogs.Confirmations);
        Assert.True(File.Exists(path));
        Assert.Empty(deleted);
        Assert.Single(viewModel.TranscriptHistory);
    }

    [Fact]
    public void DeleteSelectedTranscripts_SkipsItemsBeingSummarized()
    {
        var path = AddRecording("Zoom", "20260101_100000");
        var dialogs = new FakeDialogs(confirm: true);
        var viewModel = CreateViewModel(dialogs: dialogs);
        viewModel.Reload();

        var item = viewModel.FilteredTranscriptHistory[0];
        item.IsSelected = true;
        item.IsGeneratingSummary = true;

        Assert.False(viewModel.CanDeleteSelected);
        Assert.NotNull(viewModel.DeleteSelectedDisabledReason);

        viewModel.DeleteSelectedTranscriptsCommand.Execute(null);

        Assert.Equal(0, dialogs.Confirmations);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void DeleteSelectedAppTranscripts_OnlyTouchesTheSelectedApp()
    {
        var zoom = AddRecording("Zoom", "20260101_100000");
        var teams = AddRecording("Teams", "20260301_110000");
        var viewModel = CreateViewModel();
        viewModel.SetSelectedApp("Zoom");
        viewModel.Reload();

        viewModel.AppTranscriptHistory.Single().IsSelected = true;
        Assert.True(viewModel.HasSelectedAppHistoryItems);
        viewModel.DeleteSelectedAppTranscriptsCommand.Execute(null);

        Assert.False(File.Exists(zoom));
        Assert.True(File.Exists(teams));
        Assert.Empty(viewModel.AppTranscriptHistory);
        Assert.Single(viewModel.TranscriptHistory);
    }

    [Fact]
    public void AppFilter_NarrowsOnlyTheAppList()
    {
        AddRecording("Zoom", "20260101_100000");
        AddRecording("Zoom", "20260301_100000");
        var viewModel = CreateViewModel();
        viewModel.SetSelectedApp("Zoom");
        viewModel.Reload();
        Assert.Equal(2, viewModel.AppTranscriptHistory.Count);

        viewModel.AppFilter.ToDate = new DateTime(2026, 1, 31);

        Assert.True(viewModel.AppFilter.HasActiveFilter);
        Assert.False(viewModel.Filter.HasActiveFilter);
        Assert.Single(viewModel.AppTranscriptHistory);
        Assert.Equal(2, viewModel.FilteredTranscriptHistory.Count);
    }

    [Fact]
    public void FindTranscriptRequest_IsAnsweredWithTheLoadedItem()
    {
        var path = AddRecording("Zoom", "20260101_100000");
        var viewModel = CreateViewModel();
        viewModel.Reload();

        var found = _messenger.Send(new FindTranscriptRequest(path.ToUpperInvariant()));
        var missing = _messenger.Send(new FindTranscriptRequest(Path.Combine(_root, "nada.txt")));

        Assert.Same(viewModel.TranscriptHistory[0], found.Response);
        Assert.Null(missing.Response);
    }
}
