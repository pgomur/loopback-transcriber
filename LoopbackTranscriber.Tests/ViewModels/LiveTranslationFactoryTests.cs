namespace LoopbackTranscriber.Tests.ViewModels;

[Trait(TestCategory.Name, TestCategory.Unit)]
public sealed class LiveTranslationFactoryTests
{
    [Fact]
    public void Create_BuildsAViewModelForTheGivenTarget_WiredToTheHost()
    {
        var host = new FakeLiveHost();
        var factory = new LiveTranslationFactory(host, new FakeSettingsStore(new AppSettings()));
        var target = new TargetItemViewModel(TargetApp.Teams);
        var opened = 0;

        using var live = factory.Create(target, () => opened++);

        Assert.Same(target, live.Target);
        live.OpenMainWindowCommand.Execute(null);
        Assert.Equal(1, opened);

        host.IsBusy = true;
        Assert.Equal(LiveWindowState.Busy, live.State);
    }
}
