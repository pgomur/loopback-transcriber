using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LoopbackTranscriber.Models;
using LoopbackTranscriber.Services;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LoopbackTranscriber.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IPageFactory _pages;
    private readonly ISettingsStore _settingsService;
    private readonly IMessenger _messenger;
    private readonly IModelVerifier _modelVerifier;
    private readonly UserNotices? _notices;

    public TargetsViewModel Targets { get; }

    public RecordingViewModel Recording { get; }

    public HistoryViewModel History { get; }

    public ViewerViewModel Viewer { get; }

    public MainViewModel(
        TargetsViewModel targets,
        RecordingViewModel recording,
        HistoryViewModel history,
        ViewerViewModel viewer,
        IPageFactory pages,
        ISettingsStore settingsService,
        IMessenger messenger,
        IModelVerifier modelVerifier,
        UserNotices? notices = null)
    {
        _notices = notices;
        Targets = targets;
        Recording = recording;
        History = history;
        Viewer = viewer;
        _pages = pages;
        _settingsService = settingsService;
        _messenger = messenger;
        _modelVerifier = modelVerifier;

        _messenger.Register<MainViewModel, TargetSelectionChangedMessage>(this, static (r, m) => r.OnTargetSelectionChanged(m.Target));
        _messenger.Register<MainViewModel, TargetStatusesSyncedMessage>(this, static (r, _) => r.OnTargetStatusesSynced());
        _messenger.Register<MainViewModel, TargetListChangedMessage>(this, static (r, _) => r.OnTargetListChanged());

        Strings.Instance.LanguageChanged += OnAppLanguageChanged;

        InitializeFirstRunOrShowApp();
    }

    private void InitializeFirstRunOrShowApp()
    {
        var settings = _settingsService.Load();

        var transcriptionModelPath = new AppSettings { ModelSize = "turbo", ModelsFolder = settings.ModelsFolder }.GetModelFilePath();
        var summaryModelPath = new AppSettings { ModelsFolder = settings.ModelsFolder }.GetSummaryModelFilePath();

        if (File.Exists(transcriptionModelPath) && File.Exists(summaryModelPath))
        {
            if (!settings.FirstRunSetupCompleted)
            {
                settings.FirstRunSetupCompleted = true;
                _settingsService.Save(settings);
            }

            Targets.SelectFirstIfNone();
            CurrentView = MainViewMode.AppView;
            return;
        }

        ShowFirstRunSetup();
    }

    public void Start()
    {
        Targets.Start();
        History.Reload();
        _ = Recording.RecoverOrphanRecordingsAsync();
        _ = VerifyInstalledModelsAsync();
    }

    private async Task VerifyInstalledModelsAsync()
    {
        try
        {
            var result = await Task.Run(() => _modelVerifier.VerifyInstalledModelsAsync());
            if (!result.AnyRemoved)
            {
                return;
            }

            Viewer.RefreshSummaryModelAvailability();

            if (CurrentView != MainViewMode.FirstRun && !Recording.IsRecording && !Recording.IsBusy)
            {
                ShowFirstRunSetup();
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("MainViewModel.VerifyInstalledModelsAsync: failed to verify installed models", ex);
            _notices?.Add(UserMessage.FromKey("NoticeModelsVerificationFailed", ex.ToUserMessage()));
        }
    }

    private void OnAppLanguageChanged(object? sender, EventArgs e)
    {
        Viewer.RefreshLanguage();
        Recording.OnLanguageChanged();
    }

    public void Dispose()
    {
        _messenger.UnregisterAll(this);
        Strings.Instance.LanguageChanged -= OnAppLanguageChanged;
    }
}
