namespace LoopbackTranscriber.Tests.ViewModels;

[Trait(TestCategory.Name, TestCategory.Integration)]
public sealed class TrayMenuViewModelTests : IDisposable
{
    private readonly ShellRig _rig = new();
    private readonly FakeLiveHost _host = new();
    private readonly TrayMenuViewModel _tray;
    private readonly List<TargetItemViewModel> _liveToggles = [];
    private int _windowRestored;
    private int _exits;

    public TrayMenuViewModelTests()
    {
        _tray = new TrayMenuViewModel(_rig.Shell, _host, _rig.Pipeline.SettingsStore);
    }

    public void Dispose() => _rig.Dispose();

    private IReadOnlyList<TrayMenuEntry> Build(TargetItemViewModel? openLiveTranslation = null) =>
        _tray.Build(new TrayMenuActions(() => _windowRestored++, _liveToggles.Add, () => _exits++, openLiveTranslation));

    private static List<TrayMenuEntry> StartEntries(IReadOnlyList<TrayMenuEntry> entries) =>
        entries.Where(e => e.Icon == TrayMenuEntry.RecordGlyph).ToList();

    [Fact]
    public void DuringTheFirstRunSetup_OnlyOpenAndExitAreOffered()
    {
        using var firstRun = new ShellRig(modelsInstalled: false);
        var tray = new TrayMenuViewModel(firstRun.Shell, _host, firstRun.Pipeline.SettingsStore);

        var entries = tray.Build(new TrayMenuActions(() => { }, _ => { }, () => { }, null));

        Assert.Collection(
            entries,
            open => Assert.Equal(Strings.Instance.Get("TrayOpen"), open.Text),
            separator => Assert.True(separator.IsSeparator),
            exit => Assert.Equal(Strings.Instance.Get("TrayExit"), exit.Text));
    }

    [Fact]
    public void TheFirstEntry_OpensTheWindow()
    {
        var open = Build()[0];

        Assert.True(open.IsDefault);
        open.Invoke!();

        Assert.Equal(1, _windowRestored);
    }

    [Fact]
    public void WhenNotRecording_EachAppCanBeRecorded_OnlyIfTheHostAllowsIt()
    {
        var start = Assert.Single(StartEntries(Build()));
        Assert.Equal(Strings.Instance.Format("TrayStartRecording", _rig.TeamsItem.DisplayName), start.Text);
        Assert.True(start.Enabled);

        _host.CanStart = false;

        Assert.False(Assert.Single(StartEntries(Build())).Enabled);
    }

    [Fact]
    public void StartingFromTheTray_RecordsThatApp()
    {
        Assert.Single(StartEntries(Build())).Invoke!();

        Assert.Same(_rig.TeamsItem, Assert.Single(_host.Started));
    }

    [Fact]
    public async Task WhileATrayActionIsRunning_RecordingEntriesAreDisabled_AndExtraClicksAreIgnored()
    {
        _host.StartGate = new TaskCompletionSource();
        var start = Assert.Single(StartEntries(Build()));

        start.Invoke!();
        start.Invoke();

        Assert.True(_tray.RecordingActionCommand.IsRunning);
        Assert.False(Assert.Single(StartEntries(Build())).Enabled);
        Assert.Single(_host.Started);

        _host.StartGate.SetResult();
        await _tray.RecordingActionCommand.ExecutionTask!;

        Assert.False(_tray.RecordingActionCommand.IsRunning);
        Assert.True(Assert.Single(StartEntries(Build())).Enabled);
    }

    [Fact]
    public async Task AFailingTrayAction_DoesNotBreakTheMenu()
    {
        _host.FailStart = true;

        Assert.Single(StartEntries(Build())).Invoke!();
        await _tray.RecordingActionCommand.ExecutionTask!;

        Assert.False(_tray.RecordingActionCommand.IsRunning);
        Assert.True(Assert.Single(StartEntries(Build())).Enabled);
    }

    [Fact]
    public async Task Exit_CanOnlyRunOnceAtATime()
    {
        var exiting = new TaskCompletionSource();

        Assert.True(_tray.TryRunExit(() => exiting.Task));
        Assert.False(_tray.TryRunExit(() => Task.CompletedTask));
        Assert.False(Build()[^1].Enabled);

        exiting.SetResult();
        await _tray.ExitCommand.ExecutionTask!;

        Assert.True(Build()[^1].Enabled);
        Assert.True(_tray.TryRunExit(() => Task.CompletedTask));
    }

    [Fact]
    public void TheLastEntry_Exits()
    {
        var exit = Build()[^1];

        Assert.Equal(Strings.Instance.Get("TrayExit"), exit.Text);
        exit.Invoke!();

        Assert.Equal(1, _exits);
    }

    [Fact]
    public void LiveTranslation_CanBeOpenedForRunningApps_OrClosedWhenOpen()
    {
        var closedEntry = Build().Single(e => e.Text == Strings.Instance.Format("TrayLiveTranslationOpen", _rig.TeamsItem.DisplayName));
        Assert.False(closedEntry.Enabled);

        _rig.Watcher.SetRunning(TargetApp.Teams.Id, running: true);
        var openEntry = Build().Single(e => e.Text == Strings.Instance.Format("TrayLiveTranslationOpen", _rig.TeamsItem.DisplayName));
        Assert.True(openEntry.Enabled);
        openEntry.Invoke!();
        Assert.Same(_rig.TeamsItem, Assert.Single(_liveToggles));

        var close = Build(openLiveTranslation: _rig.TeamsItem)
            .Single(e => e.Text == Strings.Instance.Format("TrayLiveTranslationClose", _rig.TeamsItem.DisplayName));
        Assert.True(close.Enabled);
    }

    [Fact]
    public async Task WhileRecording_TheMenuShowsTheRecordingAndOffersToStopIt()
    {
        _rig.Watcher.SetRunning(TargetApp.Teams.Id, running: true);
        await _rig.Shell.Recording.ToggleRecordingCommand.ExecuteAsync(null);

        var entries = Build();

        var status = Assert.Single(entries, e => e.IsStatus);
        Assert.Equal(Strings.Instance.Format("TrayRecordingStatus", _rig.TeamsItem.DisplayName), status.Text);
        Assert.Equal(_rig.Shell.Recording.ElapsedText, status.LiveText!());
        Assert.Empty(StartEntries(entries));
        Assert.Equal(Strings.Instance.Format("TrayTooltipRecording", _rig.TeamsItem.DisplayName), _tray.Tooltip);

        var stop = Assert.Single(entries, e => e.Icon == TrayMenuEntry.StopGlyph);
        stop.Invoke!();
        await _tray.RecordingActionCommand.ExecutionTask!;

        Assert.Equal(1, _windowRestored);
        Assert.Equal(1, _host.Stops);
    }

    [Fact]
    public void WhenIdle_TheTooltipSaysSo()
    {
        Assert.Equal(Strings.Instance.Get("TrayTooltipIdle"), _tray.Tooltip);
    }

    [Fact]
    public void TheTrayHint_IsShownOnlyOnce_AndRemembered()
    {
        Assert.True(_tray.TryConsumeTrayHint());
        Assert.False(_tray.TryConsumeTrayHint());
        Assert.True(_rig.Pipeline.SettingsStore.Current.TrayHintShown);
    }
}
