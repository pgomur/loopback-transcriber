namespace LoopbackTranscriber.Tests.Infrastructure;

internal sealed class ShellRig : IDisposable
{
    private readonly List<FirstRunViewModel> _firstRunPages = [];

    public ShellRig(bool modelsInstalled = true)
    {
        if (modelsInstalled)
        {
            TestSettings.CreateModelFiles(Pipeline.Settings);
        }

        Targets = new TargetsViewModel(Watcher, CustomApps, Pipeline.Messenger);
        History = new HistoryViewModel(Pipeline.SettingsStore, new TranscriptHistoryService(), FolderWatcher, Pipeline.Dialogs,
            Pipeline.Messenger);
        Viewer = Pipeline.CreateViewer();
        Pages = new FakePageFactory(() =>
        {
            var page = new FirstRunViewModel(Pipeline.SettingsStore, Pipeline.Whisper, Pipeline.Summarizer);
            _firstRunPages.Add(page);
            return page;
        });
        Shell = new MainViewModel(Targets, Pipeline.Recording, History, Viewer, Pages, Pipeline.SettingsStore, Pipeline.Messenger,
            Verifier, Pipeline.Notices);
    }

    public RecordingRig Pipeline { get; } = new();

    public FakeTargetWatcher Watcher { get; } = new();

    public FakeCustomApps CustomApps { get; } = new();

    public FakeModelVerifier Verifier { get; } = new();

    public TranscriptsFolderWatcher FolderWatcher { get; } = new();

    public FakePageFactory Pages { get; }

    public TargetsViewModel Targets { get; }

    public HistoryViewModel History { get; }

    public ViewerViewModel Viewer { get; }

    public MainViewModel Shell { get; }

    public TargetItemViewModel TeamsItem => Targets.Items.First(t => t.App.Id == TargetApp.Teams.Id);

    public void Dispose()
    {
        Shell.Dispose();
        foreach (var page in _firstRunPages)
        {
            page.Dispose();
        }

        Viewer.Dispose();
        History.Dispose();
        FolderWatcher.Dispose();
        Targets.Dispose();
        Pipeline.Dispose();
    }
}
