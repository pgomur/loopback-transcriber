using LoopbackTranscriber.Services;
using LoopbackTranscriber.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace LoopbackTranscriber;

internal static class AppServices
{
    public static ServiceProvider Build(ISettingsStore settings)
    {
        var services = new ServiceCollection();

        services.AddSingleton(settings);

        services.AddSingleton<ITargetWatcher, TargetProcessWatcher>();
        services.AddSingleton<ICustomAppsService, CustomAppsService>();
        services.AddSingleton<TranscriptHistoryService>();
        services.AddSingleton<TranscriptsFolderWatcher>();
        services.AddSingleton<ISummarizer, SummarizationService>();
        services.AddSingleton<IModelVerifier, ModelVerifier>();

        services.AddSingleton<WhisperTranscriptionService>();
        services.AddSingleton<IWhisperTranscriber>(sp => sp.GetRequiredService<WhisperTranscriptionService>());
        services.AddSingleton<IAudioPreparer, NaudioAudioPreparer>();
        services.AddSingleton<RecordingProcessor>();
        services.AddSingleton<TranscriptSaver>();
        services.AddSingleton<TranscriptRetranscriber>();
        services.AddSingleton(new FailedRecordingStore(FailedRecordingStore.DefaultRootFolder));

        services.AddSingleton(sp => new CaptureCoordinator(
            new ProcessLoopbackRecorder(), new DeviceLoopbackRecorder(), new MicrophoneRecorder(),
            failedStore: sp.GetRequiredService<FailedRecordingStore>()));
        services.AddSingleton<RecordingWorkflow>();

        services.AddSingleton<IDialogService, WpfDialogService>();
        services.AddSingleton<IUiDispatcher>(WpfUiDispatcher.Instance);
        services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<ViewerViewModel>();
        services.AddSingleton<TargetsViewModel>();
        services.AddSingleton<RecordingViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<IRecordingHost, RecordingHost>();
        services.AddSingleton<ILiveTranslationFactory, LiveTranslationFactory>();
        services.AddSingleton<TrayMenuViewModel>();
        services.AddSingleton<AppExitWorkflow>();

        services.AddTransient<FirstRunViewModel>();
        services.AddTransient<ConfigViewModel>();
        services.AddTransient<ApplicationsViewModel>();
        services.AddTransient<LiveTranslationSettingsViewModel>();
        services.AddSingleton<IPageFactory, ServiceProviderPageFactory>();

        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }
}

internal sealed class ServiceProviderPageFactory(IServiceProvider provider) : IPageFactory
{
    public FirstRunViewModel CreateFirstRun() => provider.GetRequiredService<FirstRunViewModel>();

    public ConfigViewModel CreateConfig() => provider.GetRequiredService<ConfigViewModel>();

    public ApplicationsViewModel CreateApplications() => provider.GetRequiredService<ApplicationsViewModel>();

    public LiveTranslationSettingsViewModel CreateLiveSettings() => provider.GetRequiredService<LiveTranslationSettingsViewModel>();
}
