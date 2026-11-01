using CommunityToolkit.Mvvm.Messaging;

namespace LoopbackTranscriber.Tests.ViewModels;

[Trait(TestCategory.Name, TestCategory.Integration)]
public sealed class RecordingViewModelTests : IDisposable
{
    private readonly RecordingRig _rig = new();
    private readonly ViewerViewModel _viewer;
    private readonly List<bool> _captureStates = [];
    private int _saved;

    public RecordingViewModelTests()
    {
        _viewer = _rig.CreateViewer();
        _rig.Messenger.Register<RecordingViewModelTests, RecordingSavedMessage>(this, static (test, _) => test._saved++);
        _rig.Messenger.Register<RecordingViewModelTests, CaptureStateChangedMessage>(this, static (test, m) => test._captureStates.Add(m.IsActive));
    }

    private RecordingViewModel Recording => _rig.Recording;

    public void Dispose()
    {
        _viewer.Dispose();
        _rig.Dispose();
    }

    [Fact]
    public void CannotToggle_WhileTheSelectedAppIsNotRunning()
    {
        _rig.TeamsIsRunning(false);
        Recording.OnTargetListChanged();

        Assert.False(Recording.CanToggleRecording);
        Assert.False(Recording.ToggleRecordingCommand.CanExecute(null));
        Assert.False(Recording.CanStartRecording(_rig.Teams));
    }

    [Fact]
    public async Task StartAndStop_ShowsTheTranscriptInTheViewer_AndAnnouncesTheSavedRecording()
    {
        _rig.TeamsIsRunning(true);
        Assert.True(Recording.CanToggleRecording);

        await Recording.ToggleRecordingCommand.ExecuteAsync(null);

        Assert.True(Recording.IsRecording);
        Assert.False(Recording.CanStartRecording(_rig.Teams));
        Assert.Equal(Strings.Instance.Get("RecordStop"), Recording.RecordButtonText);

        _rig.WhisperSays("Hola a todos.");
        await Recording.ToggleRecordingCommand.ExecuteAsync(null);

        Assert.False(Recording.IsRecording);
        Assert.False(Recording.IsBusy);
        Assert.Equal("Hola a todos.", _viewer.TranscriptText);
        Assert.EndsWith(".md", _viewer.ViewedFilePath);
        Assert.Equal(1, _saved);
        Assert.False(Recording.HasFailedRecording);
        Assert.Equal(Strings.Instance.Get("StatusTranscriptReadySaved"), Recording.StatusText);
    }

    [Fact]
    public async Task ARecordingWithoutSpeech_IsReportedWithoutSavingAnything()
    {
        _rig.Audio.DurationSeconds = 3;
        await _rig.StartRecordingAsync();

        await Recording.ToggleRecordingCommand.ExecuteAsync(null);

        Assert.Equal(Strings.Instance.Get("StatusNoTextDetected"), _viewer.TranscriptText);
        Assert.Equal(Strings.Instance.Get("StatusTranscriptReady"), Recording.StatusText);
        Assert.Equal(0, _saved);
    }

    [Fact]
    public async Task AStartFailure_IsShownInTheStatus()
    {
        _rig.Process.FailStart = true;

        await _rig.StartRecordingAsync();

        Assert.False(Recording.IsRecording);
        Assert.Equal(Strings.Instance.Format("StatusRecordFailed", new InvalidOperationException("no arranca").ToUserMessage()),
            Recording.StatusText);
        Assert.True(Recording.CanToggleRecording);
    }

    [Fact]
    public async Task AMissingMicrophone_IsShownAsAWarning()
    {
        _rig.Mic.StartSucceeds = false;

        await _rig.StartRecordingAsync();

        Assert.True(Recording.HasRecordingWarning);
        Assert.Equal(Strings.Instance.Get("WarningMicNotRecorded"), Recording.RecordingWarning);
    }

    [Fact]
    public async Task EngineFailure_KeepsTheRecording_AndRetryRecoversIt()
    {
        await _rig.StartRecordingAsync();
        _rig.Whisper.Fail = true;

        await Recording.ToggleRecordingCommand.ExecuteAsync(null);

        var failed = Assert.Single(Recording.FailedRecordings).FailedRecording;
        Assert.True(Recording.HasFailedRecording);
        Assert.True(Recording.RetryFailedRecordingCommand.CanExecute(failed));
        Assert.False(Recording.RetryFailedRecordingCommand.CanExecute(null));
        Assert.Equal(0, _saved);

        _rig.Whisper.Fail = false;
        _rig.Whisper.ByFileName.Clear();
        _rig.Whisper.ByFileName["meeting.16k.wav"] = [new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(2), "Texto recuperado.")];
        await Recording.RetryFailedRecordingCommand.ExecuteAsync(failed);

        Assert.False(Recording.HasFailedRecording);
        Assert.Equal("Texto recuperado.", _viewer.TranscriptText);
        Assert.Equal(1, _saved);
    }

    [Fact]
    public async Task DiscardingAFailedRecording_AsksFirst_AndThenDeletesIt()
    {
        await _rig.StartRecordingAsync();
        _rig.Whisper.Fail = true;
        await Recording.ToggleRecordingCommand.ExecuteAsync(null);
        var failed = Assert.Single(Recording.FailedRecordings).FailedRecording;

        Recording.DiscardFailedRecordingCommand.Execute(failed);

        Assert.Equal(1, _rig.Dialogs.Confirmations);
        Assert.False(Recording.HasFailedRecording);
        Assert.Empty(_rig.Workflow.ListFailed());
    }

    [Fact]
    public async Task FailedRecordings_AreShownOnlyForTheirApp()
    {
        await _rig.StartRecordingAsync();
        _rig.Whisper.Fail = true;
        await Recording.ToggleRecordingCommand.ExecuteAsync(null);
        Assert.True(Recording.HasFailedRecording);

        var zoom = new TargetApp { Id = "zoom", DisplayName = "Zoom", ProcessNames = ["zoom"] };
        Recording.OnTargetSelected(new TargetItemViewModel(zoom));

        Assert.False(Recording.HasFailedRecording);
    }

    [Fact]
    public async Task ClosingTheRecordedAppWhileRecording_StopsAndProcessesTheRecording()
    {
        await _rig.StartRecordingAsync();
        _rig.WhisperSays("Antes de cerrar.");

        _rig.TeamsIsRunning(false);
        Recording.OnTargetStatusesSynced();

        await Eventually.Until(() => !Recording.IsRecording && !Recording.IsBusy, "the recording was processed");
        Assert.Equal("Antes de cerrar.", _viewer.TranscriptText);
    }

    [Fact]
    public async Task PreserveInterruptedRecording_StopsAndKeepsTheAudioForLater()
    {
        await _rig.StartRecordingAsync();

        var preserved = Recording.PreserveInterruptedRecording(TimeSpan.FromSeconds(5));

        Assert.True(preserved);
        Assert.True(_rig.Process.Stopped);
        Assert.False(Recording.IsRecording);
        var failed = Assert.Single(Recording.FailedRecordings).FailedRecording;
        Assert.Equal("RecordingInterruptedReason", failed.ErrorKey);
        Assert.Equal(0, _saved);
    }

    [Fact]
    public async Task PreserveInterruptedRecordingAsync_StopsAndKeepsTheAudioForLater()
    {
        await _rig.StartRecordingAsync();

        Assert.True(await Recording.PreserveInterruptedRecordingAsync(TimeSpan.FromSeconds(5)));

        Assert.False(Recording.IsRecording);
        Assert.Single(Recording.FailedRecordings);
    }

    [Fact]
    public async Task PreserveInterruptedRecording_DoesNothingWhenNotRecording()
    {
        _rig.TeamsIsRunning(true);

        Assert.False(Recording.PreserveInterruptedRecording(TimeSpan.FromSeconds(1)));
        Assert.False(await Recording.PreserveInterruptedRecordingAsync(TimeSpan.FromSeconds(1)));
        Assert.False(_rig.Process.Stopped);
        Assert.False(Recording.HasFailedRecording);
    }

    [Fact]
    public async Task WaitForPendingStartAsync_WaitsForARecordingThatIsStillStarting()
    {
        _rig.TeamsIsRunning(true);
        _rig.Process.StartGate = new TaskCompletionSource();

        var starting = Recording.ToggleRecordingCommand.ExecuteAsync(null);
        var waiting = Recording.WaitForPendingStartAsync(TimeSpan.FromSeconds(5));

        Assert.False(waiting.IsCompleted);
        _rig.Process.StartGate.SetResult();
        await waiting;
        await starting;

        Assert.True(Recording.IsRecording);
    }

    [Fact]
    public async Task WaitForPendingStartAsync_GivesUpAfterTheTimeout()
    {
        _rig.TeamsIsRunning(true);
        _rig.Process.StartGate = new TaskCompletionSource();
        var starting = Recording.ToggleRecordingCommand.ExecuteAsync(null);

        await Recording.WaitForPendingStartAsync(TimeSpan.FromMilliseconds(50));

        Assert.False(Recording.IsRecording);
        _rig.Process.StartGate.SetResult();
        await starting;
    }

    [Fact]
    public async Task WaitForPendingStartAsync_ReturnsAtOnceWhenNothingIsStarting()
    {
        await Recording.WaitForPendingStartAsync(TimeSpan.FromSeconds(5)).WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task RecoverOrphanRecordingsAsync_ShowsTheRecoveredRecording()
    {
        _rig.LeaveOrphan("recording_20261018_093000", _rig.Teams.DisplayName);

        await Recording.RecoverOrphanRecordingsAsync();

        Assert.Equal("RecordingRecoveredReason", Assert.Single(Recording.FailedRecordings).FailedRecording.ErrorKey);
        Assert.Empty(_rig.Notices.TakeAll());
    }

    [Fact]
    public async Task RecoverOrphanRecordingsAsync_TellsTheUserWhenARecordingCannotBeRecovered()
    {
        _rig.LeaveOrphan("recording_20261018_093000", "Teams", sessionContent: "{ roto");

        await Recording.RecoverOrphanRecordingsAsync();

        Assert.Equal("NoticeOrphanRecoveryFailed", Assert.Single(_rig.Notices.TakeAll()).Key);
        Assert.False(Recording.HasFailedRecording);
    }

    [Fact]
    public async Task CaptureState_IsAnnouncedWhileRecordingAndReleasedAfterwards()
    {
        await _rig.StartRecordingAsync();

        Assert.True(_captureStates[^1]);

        _rig.WhisperSays("Fin.");
        await Recording.ToggleRecordingCommand.ExecuteAsync(null);

        Assert.False(_captureStates[^1]);
    }

    [Fact]
    public async Task TheViewer_IgnoresHistoryClicksWhileRecording()
    {
        await _rig.StartRecordingAsync();
        var path = _rig.Temp.WriteText("viejo.txt", "contenido viejo");
        var item = new TranscriptHistoryItem
        {
            AppName = "Teams",
            Timestamp = DateTime.Now,
            Preview = "viejo",
            FilePath = path,
            HasTxt = true,
            HasMarkdown = false,
            HasAudio = false,
            SummaryFilePath = Path.ChangeExtension(path, ".summary.json"),
            Duration = null
        };

        _viewer.ViewHistoryTranscriptCommand.Execute(item);

        Assert.Equal(string.Empty, _viewer.TranscriptText);
    }

    [Fact]
    public void RunningSummary_BlocksANewRecording_UntilItFinishes()
    {
        _rig.TeamsIsRunning(true);
        Assert.True(Recording.CanToggleRecording);

        _rig.Messenger.Send(new SummaryActivityChangedMessage(true));

        Assert.True(Recording.IsSummaryGenerating);
        Assert.False(Recording.CanToggleRecording);
        Assert.NotNull(Recording.RecordDisabledReason);

        _rig.Messenger.Send(new SummaryActivityChangedMessage(false));

        Assert.False(Recording.IsSummaryGenerating);
        Assert.True(Recording.CanToggleRecording);
        Assert.Null(Recording.RecordDisabledReason);
    }

    [Fact]
    public void TargetSelectionChangedMessage_SetsTheTargetToRecord()
    {
        var zoom = new TargetApp { Id = "zoom", DisplayName = "Zoom", ProcessNames = ["zoom"] };
        var other = new TargetItemViewModel(zoom);
        other.UpdateFrom(new TargetProcessStatus { App = zoom, IsRunning = true, ProcessId = 9 });

        _rig.Messenger.Send(new TargetSelectionChangedMessage(other));

        Assert.Same(other, Recording.SelectedTarget);
        Assert.True(Recording.CanToggleRecording);
    }

    [Fact]
    public void ClearingTheSelectedTarget_DisablesRecording()
    {
        _rig.TeamsIsRunning(true);
        Assert.True(Recording.CanToggleRecording);

        Recording.OnTargetSelected(null);

        Assert.False(Recording.CanToggleRecording);
    }

    [Fact]
    public void RecordDisabledReason_IsNullWhenNoSummaryIsRunning()
    {
        Assert.False(Recording.IsSummaryGenerating);
        Assert.Null(Recording.RecordDisabledReason);
    }

    [Fact]
    public async Task AudioCaptured_IsForwardedToTheLiveTranslation()
    {
        var received = 0;
        Recording.LiveAudioCaptured += (_, count) => received += count;
        await _rig.StartRecordingAsync();

        _rig.Process.Raise([1, 2, 3]);

        Assert.Equal(3, received);
    }

    [Fact]
    public async Task Dispose_StopsForwardingLiveAudio()
    {
        var received = 0;
        Recording.LiveAudioCaptured += (_, count) => received += count;
        await _rig.StartRecordingAsync();

        Recording.Dispose();
        _rig.Process.Raise([1, 2, 3]);

        Assert.Equal(0, received);
    }
}
