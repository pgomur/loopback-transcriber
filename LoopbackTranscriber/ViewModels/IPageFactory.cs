namespace LoopbackTranscriber.ViewModels;

public interface IPageFactory
{
    FirstRunViewModel CreateFirstRun();

    ConfigViewModel CreateConfig();

    ApplicationsViewModel CreateApplications();

    LiveTranslationSettingsViewModel CreateLiveSettings();
}
