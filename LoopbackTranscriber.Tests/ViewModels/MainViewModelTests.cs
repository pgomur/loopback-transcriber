namespace LoopbackTranscriber.Tests.ViewModels;

[Trait(TestCategory.Name, TestCategory.Integration)]
public sealed class MainViewModelTests
{
    [Fact]
    public void WithBothModelsInstalled_TheAppOpensDirectly_AndAnAppIsSelectedForRecording()
    {
        using var rig = new ShellRig(modelsInstalled: true);

        Assert.Equal(MainViewMode.AppView, rig.Shell.CurrentView);
        Assert.False(rig.Shell.IsFirstRunActive);
        Assert.Equal(0, rig.Pages.FirstRunPagesCreated);
        Assert.True(rig.Pipeline.SettingsStore.Current.FirstRunSetupCompleted);
        Assert.NotNull(rig.Targets.SelectedTarget);
        Assert.Same(rig.Targets.SelectedTarget, rig.Shell.Recording.SelectedTarget);
    }

    [Fact]
    public void WithAModelMissing_TheFirstRunSetupIsShown()
    {
        using var rig = new ShellRig(modelsInstalled: false);

        Assert.True(rig.Shell.IsFirstRunActive);
        Assert.NotNull(rig.Shell.FirstRunViewModel);
        Assert.Equal(1, rig.Pages.FirstRunPagesCreated);
        Assert.False(rig.Pipeline.SettingsStore.Current.FirstRunSetupCompleted);
    }

    [Fact]
    public async Task Start_WatchesTheApps_AndChecksTheInstalledModels()
    {
        using var rig = new ShellRig();

        rig.Shell.Start();

        Assert.True(rig.Watcher.Started);
        await Eventually.Until(() => rig.Verifier.Calls == 1, "the installed models were checked");
        Assert.False(rig.Shell.IsFirstRunActive);
    }

    [Fact]
    public async Task WhenACorruptModelIsRemoved_TheFirstRunSetupIsShownAgain()
    {
        using var rig = new ShellRig();
        rig.Verifier.Result = new ModelVerificationResult(TranscriptionModelRemoved: true, SummaryModelRemoved: false);

        rig.Shell.Start();

        await Eventually.Until(() => rig.Shell.IsFirstRunActive, "the first-run setup was shown");
        Assert.Equal(1, rig.Pages.FirstRunPagesCreated);
    }

    [Fact]
    public async Task WhenTheModelsCannotBeChecked_TheUserIsTold_AndTheAppStaysOpen()
    {
        using var rig = new ShellRig();
        rig.Verifier.Failure = new IOException("disco ocupado");

        rig.Shell.Start();

        await Eventually.Until(() => rig.Verifier.Calls == 1, "the installed models were checked");
        await Eventually.Until(
            () => rig.Pipeline.Notices.TakeAll().Any(n => n.Key == "NoticeModelsVerificationFailed"),
            "the user was told");
        Assert.False(rig.Shell.IsFirstRunActive);
    }

    [Fact]
    public async Task Start_RecoversRecordingsInterruptedByACrash()
    {
        using var rig = new ShellRig();
        rig.Pipeline.LeaveOrphan("recording_20261018_093000", rig.TeamsItem.DisplayName);

        rig.Shell.Start();

        await Eventually.Until(() => rig.Shell.Recording.HasFailedRecording, "the interrupted recording was recovered");
    }

    [Fact]
    public void SelectingAnotherApp_IsForwardedToTheRecording()
    {
        using var rig = new ShellRig();
        var zoom = rig.CustomApps.AddApp("zoom", "Zoom");
        var zoomItem = rig.Targets.Items.First(t => t.App.Id == zoom.Id);

        rig.Targets.Select(zoomItem);

        Assert.Same(zoomItem, rig.Shell.Recording.SelectedTarget);
    }
}
