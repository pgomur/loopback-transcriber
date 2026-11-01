namespace LoopbackTranscriber.Tests.Fakes;

internal sealed class FakePageFactory(Func<FirstRunViewModel> createFirstRun) : IPageFactory
{
    public int FirstRunPagesCreated { get; private set; }

    public FirstRunViewModel CreateFirstRun()
    {
        FirstRunPagesCreated++;
        return createFirstRun();
    }

    public ConfigViewModel CreateConfig() => throw new NotSupportedException("The test did not expect the settings page.");

    public ApplicationsViewModel CreateApplications() => throw new NotSupportedException("The test did not expect the applications page.");

    public LiveTranslationSettingsViewModel CreateLiveSettings() => throw new NotSupportedException("The test did not expect the live translation settings page.");
}
