namespace LoopbackTranscriber.Tests.ViewModels;

[Trait(TestCategory.Name, TestCategory.Integration)]
public sealed class AppExitWorkflowTests : IDisposable
{
    private readonly RecordingRig _rig = new();
    private readonly FakeLiveHost _host = new();
    private readonly AppExitWorkflow _exit;
    private int _windowRestored;

    public AppExitWorkflowTests()
    {
        _host.OnStop = () => _rig.Recording.StopIfRecordingAsync();
        _exit = new AppExitWorkflow(_rig.Recording, _host, _rig.Dialogs);
    }

    public void Dispose() => _rig.Dispose();

    private void RestoreWindow() => _windowRestored++;

    [Fact]
    public async Task WhenIdle_TheAppExitsWithoutAsking()
    {
        Assert.True(await _exit.ConfirmExitAsync(RestoreWindow));

        Assert.Equal(0, _rig.Dialogs.Confirmations);
        Assert.Equal(0, _windowRestored);
    }

    [Fact]
    public async Task WhileRecording_Cancel_KeepsRecording()
    {
        await _rig.StartRecordingAsync();
        _rig.Dialogs.YesNoCancelAnswer = DialogAnswer.Cancel;

        Assert.False(await _exit.ConfirmExitAsync(RestoreWindow));

        Assert.True(_rig.Recording.IsRecording);
        Assert.Equal(0, _host.Stops);
    }

    [Fact]
    public async Task WhileRecording_Yes_StopsAndProcessesTheRecordingWithTheWindowVisible()
    {
        await _rig.StartRecordingAsync();
        _rig.WhisperSays("Última frase.");
        _rig.Dialogs.YesNoCancelAnswer = DialogAnswer.Yes;

        Assert.True(await _exit.ConfirmExitAsync(RestoreWindow));

        Assert.Equal(1, _windowRestored);
        Assert.Equal(1, _host.Stops);
        Assert.False(_rig.Recording.IsRecording);
        Assert.Empty(_rig.Workflow.ListFailed());
        Assert.Single(Directory.GetDirectories(Path.Combine(_rig.Settings.TranscriptsFolder,
            TranscriptHistoryService.GetAppFolderName(_rig.Teams.DisplayName))));
    }

    [Fact]
    public async Task WhileRecording_No_ExitsAtOnce_KeepingTheAudioToProcessLater()
    {
        await _rig.StartRecordingAsync();
        _rig.Dialogs.YesNoCancelAnswer = DialogAnswer.No;

        Assert.True(await _exit.ConfirmExitAsync(RestoreWindow));

        Assert.False(_rig.Recording.IsRecording);
        Assert.Equal(0, _host.Stops);
        Assert.Equal(0, _windowRestored);
        Assert.Equal("RecordingInterruptedReason", Assert.Single(_rig.Workflow.ListFailed()).ErrorKey);
    }

    [Fact]
    public async Task WhileProcessing_DecliningToWait_KeepsTheAppOpen()
    {
        await StartProcessingThatWaitsAsync();
        _rig.Dialogs.ConfirmAnswer = false;

        Assert.False(await _exit.ConfirmExitAsync(RestoreWindow));

        Assert.True(_rig.Recording.IsBusy);
        _rig.Whisper.Gate!.SetResult();
        await Eventually.Until(() => !_rig.Recording.IsBusy, "the processing finished");
    }

    [Fact]
    public async Task WhileProcessing_AgreeingToWait_ExitsOnlyWhenTheProcessingEnds()
    {
        await StartProcessingThatWaitsAsync();

        var exiting = _exit.ConfirmExitAsync(RestoreWindow);

        Assert.False(exiting.IsCompleted);
        Assert.Equal(1, _windowRestored);

        _rig.Whisper.Gate!.SetResult();

        Assert.True(await exiting.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(_rig.Recording.IsBusy);
    }

    [Fact]
    public async Task ASecondExitRequest_WhileTheFirstIsWaiting_IsIgnored()
    {
        await StartProcessingThatWaitsAsync();
        var first = _exit.ConfirmExitAsync(RestoreWindow);

        Assert.False(await _exit.ConfirmExitAsync(RestoreWindow));

        _rig.Whisper.Gate!.SetResult();
        Assert.True(await first.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task ExitingWhileARecordingIsStillStarting_WaitsForIt_AndThenAsks()
    {
        _rig.TeamsIsRunning(true);
        _rig.Process.StartGate = new TaskCompletionSource();
        var starting = _rig.Recording.ToggleRecordingCommand.ExecuteAsync(null);
        _rig.Dialogs.YesNoCancelAnswer = DialogAnswer.Cancel;

        var exiting = _exit.ConfirmExitAsync(RestoreWindow);
        Assert.False(exiting.IsCompleted);

        _rig.Process.StartGate.SetResult();
        await starting;

        Assert.False(await exiting.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, _rig.Dialogs.Confirmations);
        Assert.True(_rig.Recording.IsRecording);
    }

    private async Task StartProcessingThatWaitsAsync()
    {
        await _rig.StartRecordingAsync();
        _rig.Whisper.Gate = new TaskCompletionSource();
        _ = _rig.Recording.ToggleRecordingCommand.ExecuteAsync(null);
        await Eventually.Until(() => _rig.Whisper.Requests.Count > 0, "the transcription started");
        Assert.True(_rig.Recording.IsBusy);
    }
}
