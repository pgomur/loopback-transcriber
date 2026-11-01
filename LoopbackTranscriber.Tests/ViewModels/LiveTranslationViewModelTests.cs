namespace LoopbackTranscriber.Tests.ViewModels;

[Trait(TestCategory.Name, TestCategory.Unit)]
public sealed class LiveTranslationViewModelTests : IDisposable
{
    private readonly FakeLiveHost _host = new();
    private readonly TargetItemViewModel _teams = new(TargetApp.Teams);
    private readonly LiveTranslationViewModel _live;
    private int _openedMainWindow;

    public LiveTranslationViewModelTests()
    {
        _teams.UpdateFrom(new TargetProcessStatus { App = TargetApp.Teams, IsRunning = true, ProcessId = 10 });
        _live = new LiveTranslationViewModel(
            _host, _teams, new FakeSettingsStore(new AppSettings()), () => _openedMainWindow++);
    }

    public void Dispose() => _live.Dispose();

    [Fact]
    public void Idle_WhenTheHostIsNotRecording_AndTheAppCanBeStarted()
    {
        Assert.Equal(LiveWindowState.Idle, _live.State);
        Assert.True(_live.StartStopCommand.CanExecute(null));
        Assert.Null(_live.DisabledReason);
    }

    [Fact]
    public void CannotStart_WhenTheHostRefuses_AndExplainsWhy()
    {
        _host.CanStart = false;
        _host.StatusText = "cualquier cambio";

        Assert.False(_live.StartStopCommand.CanExecute(null));
    }

    [Fact]
    public async Task StartStop_AsksTheHostToStartRecordingTheTarget()
    {
        await _live.StartStopCommand.ExecuteAsync(null);

        Assert.Same(_teams, Assert.Single(_host.Started));
    }

    [Fact]
    public void Busy_WhenTheHostIsProcessing_ShowingTheHostStatus()
    {
        _host.StatusText = "Transcribiendo";
        _host.IsBusy = true;

        Assert.Equal(LiveWindowState.Busy, _live.State);
        Assert.Equal("Transcribiendo", _live.StatusLine);
        Assert.False(_live.StartStopCommand.CanExecute(null));
    }

    [Fact]
    public void Finished_WhenTheHostStopsBeingBusy_KeepingTheFinalStatus()
    {
        _host.IsBusy = true;
        _host.StatusText = "Transcripcion guardada";
        _host.IsBusy = false;

        Assert.Equal(LiveWindowState.Finished, _live.State);
        Assert.Equal("Transcripcion guardada", _live.StatusLine);
        Assert.True(_live.StartStopCommand.CanExecute(null));
    }

    [Fact]
    public void ProgressAndOpenMainWindow_AreDelegated()
    {
        _host.IsTranscribingActive = true;
        _host.TranscriptionProgressPercent = 42;

        Assert.True(_live.ShowProgress);
        Assert.Equal(42, _live.Progress);

        _live.OpenMainWindowCommand.Execute(null);
        Assert.Equal(1, _openedMainWindow);
    }

    [Fact]
    public void Dispose_StopsListeningToTheHost()
    {
        _live.Dispose();
        var notifications = 0;
        _live.PropertyChanged += (_, _) => notifications++;

        _host.IsBusy = true;

        Assert.Equal(0, notifications);
    }
}
