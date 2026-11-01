using CommunityToolkit.Mvvm.Messaging;

namespace LoopbackTranscriber.Tests.ViewModels;

[Trait(TestCategory.Name, TestCategory.Integration)]
public sealed class RecordingHostTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lt-host-" + Guid.NewGuid().ToString("N"));
    private readonly WeakReferenceMessenger _messenger = new();
    private readonly FakeProcess _process = new();
    private readonly FakeTargetWatcher _watcher = new();
    private readonly FakeCustomApps _customApps = new();
    private readonly TargetsViewModel _targets;
    private readonly RecordingViewModel _recording;
    private readonly RecordingHost _host;
    private readonly TargetItemViewModel _teams;
    private readonly List<TargetItemViewModel?> _selections = [];

    public RecordingHostTests()
    {
        var failed = new FailedRecordingStore(Path.Combine(_root, "failed"));
        var settings = new FakeSettingsStore(new AppSettings
        {
            TranscriptsFolder = Path.Combine(_root, "transcripts"),
            ModelsFolder = Path.Combine(_root, "models"),
            UserName = "pgomur"
        });
        var capture = new CaptureCoordinator(_process, new FakeDevice(), new FakeMic(),
            tempFolder: Path.Combine(_root, "tmp"), failedStore: failed);
        var workflow = new RecordingWorkflow(capture, new RecordingProcessor(new FakeWhisper(), new FakeAudio()),
            new TranscriptSaver(), settings, failed);

        _targets = new TargetsViewModel(_watcher, _customApps, _messenger);
        _recording = new RecordingViewModel(workflow, new FakeDialogs(), _messenger);
        _host = new RecordingHost(_recording, _targets);

        _teams = _targets.Items.First(t => t.App.Id == "teams");

        _messenger.Register<RecordingHostTests, TargetSelectionChangedMessage>(this, static (test, m) => test._selections.Add(m.Target));
    }

    public void Dispose()
    {
        _host.Dispose();
        _recording.Dispose();
        _targets.Dispose();
        _messenger.UnregisterAll(this);

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private TargetItemViewModel OtherTarget()
    {
        var app = _customApps.AddApp("zoom", "Zoom");
        return _targets.Items.First(t => t.App.Id == app.Id);
    }

    private void TeamsIsRunning(bool running) => _watcher.SetRunning("teams", running);

    [Fact]
    public void CannotStart_WhileTheAppIsNotRunning()
    {
        TeamsIsRunning(false);

        Assert.False(_host.CanStartRecordingFor(_teams));
    }

    [Fact]
    public async Task StartRecordingFor_SelectsTheTarget_AndStartsRecording_WithoutAnyShell()
    {
        TeamsIsRunning(true);
        Assert.True(_host.CanStartRecordingFor(_teams));

        await _host.StartRecordingForAsync(_teams);

        Assert.Same(_teams, Assert.Single(_selections));
        Assert.Same(_teams, _recording.SelectedTarget);
        Assert.Same(_teams, _host.SelectedTarget);
        Assert.True(_host.IsRecording);
        Assert.False(_host.CanStartRecordingFor(_teams));
    }

    [Fact]
    public async Task StartRecordingFor_DoesNothingWhenTheAppIsNotRunning()
    {
        TeamsIsRunning(false);

        await _host.StartRecordingForAsync(_teams);

        Assert.Empty(_selections);
        Assert.False(_host.IsRecording);
    }

    [Fact]
    public async Task StartRecordingFor_AnnouncesTheSelectionEvenWhenTheTargetWasAlreadySelected()
    {
        TeamsIsRunning(true);
        _targets.SelectedTarget = _teams;
        _selections.Clear();

        await _host.StartRecordingForAsync(_teams);

        Assert.Same(_teams, Assert.Single(_selections));
        Assert.True(_host.IsRecording);
    }

    [Fact]
    public async Task StartRecordingFor_SwitchesFromAnotherSelectedTarget()
    {
        TeamsIsRunning(true);
        var other = OtherTarget();
        _targets.SelectedTarget = other;

        await _host.StartRecordingForAsync(_teams);

        Assert.Same(_teams, _host.SelectedTarget);
        Assert.Same(_teams, _recording.SelectedTarget);
        Assert.True(_host.IsRecording);
    }

    [Fact]
    public async Task StartRecordingFor_DoesNotRecordATargetThatCannotBeSelected()
    {
        var foreign = new TargetItemViewModel(TargetApp.Teams);
        foreign.UpdateFrom(new TargetProcessStatus { App = TargetApp.Teams, IsRunning = true, ProcessId = 7 });
        var previous = OtherTarget();
        _targets.SelectedTarget = previous;
        _watcher.SetRunning(previous.App.Id, true);

        await _host.StartRecordingForAsync(foreign);

        Assert.False(_host.IsRecording);
        Assert.Same(previous, _host.SelectedTarget);
    }

    [Fact]
    public async Task StopRecording_StopsAnActiveRecording()
    {
        TeamsIsRunning(true);
        await _host.StartRecordingForAsync(_teams);

        await _host.StopRecordingAsync();

        Assert.False(_host.IsRecording);
        Assert.False(_host.IsBusy);
    }

    [Fact]
    public async Task PropertyChanges_OfRecordingAndTargets_AreReRaised()
    {
        var names = new List<string?>();
        _host.PropertyChanged += (_, e) => names.Add(e.PropertyName);
        TeamsIsRunning(true);

        await _host.StartRecordingForAsync(_teams);

        Assert.Contains(nameof(IRecordingHost.IsRecording), names);
        Assert.Contains(nameof(IRecordingHost.SelectedTarget), names);
    }

    [Fact]
    public async Task AudioCaptured_IsForwarded_AndStopsAfterDispose()
    {
        TeamsIsRunning(true);
        var received = 0;
        _host.AudioCaptured += (_, count) => received += count;
        await _host.StartRecordingForAsync(_teams);

        _process.Raise([1, 2, 3]);
        Assert.Equal(3, received);

        _host.Dispose();
        _process.Raise([1, 2]);
        Assert.Equal(3, received);
    }
}
