using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoopbackTranscriber.Models;
using LoopbackTranscriber.Services;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LoopbackTranscriber.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly TargetProcessWatcher _watcher;
    private readonly ProcessLoopbackRecorder _recorder;
    private readonly DeviceLoopbackRecorder _deviceRecorder;
    private readonly MicrophoneRecorder _microphoneRecorder;
    private readonly WhisperTranscriptionService _whisper;
    private readonly SummarizationService _summarizer;
    private readonly SettingsService _settingsService;
    private readonly TranscriptHistoryService _transcriptHistoryService;
    private readonly CustomAppsService _customAppsService;
    private readonly DispatcherTimer _elapsedTimer;

    private DateTime _recordingStartedAt;
    private string? _currentRawWavPath;

    private DateTime? _processLoopbackStartedAt;
    private DateTime? _deviceLoopbackStartedAt;
    private DateTime? _microphoneStartedAt;
    private FileSystemWatcher? _transcriptsWatcher;
    private string? _watchedTranscriptsFolderPath;
    private string? _viewedTranscriptFilePath;
    private readonly DispatcherTimer _transcriptsRefreshTimer;

    public MainViewModel(
        TargetProcessWatcher watcher,
        ProcessLoopbackRecorder recorder,
        DeviceLoopbackRecorder deviceRecorder,
        MicrophoneRecorder microphoneRecorder,
        WhisperTranscriptionService whisper,
        SummarizationService summarizer,
        SettingsService settingsService,
        TranscriptHistoryService transcriptHistoryService,
        CustomAppsService customAppsService)
    {
        _watcher = watcher;
        _recorder = recorder;
        _deviceRecorder = deviceRecorder;
        _microphoneRecorder = microphoneRecorder;
        _whisper = whisper;
        _summarizer = summarizer;
        _settingsService = settingsService;
        _transcriptHistoryService = transcriptHistoryService;
        _customAppsService = customAppsService;

        HistorySearch = new HistorySearchState(() => TranscriptHistory.ToList(), RefreshFilteredTranscriptHistory);
        AppHistorySearch = new HistorySearchState(() => TranscriptHistory.ToList(), RefreshAppTranscriptHistory);

        foreach (var app in TargetApp.All.Concat(_customAppsService.CustomApps))
        {
            AddTargetInternal(app);
        }

        _customAppsService.Changed += OnCustomAppsChanged;

        AppTranscriptHistory.CollectionChanged += (_, _) => HasAppHistoryItems = AppTranscriptHistory.Count > 0;
        TranscriptHistory.CollectionChanged += (_, _) => HasHistoryItems = TranscriptHistory.Count > 0;

        _watcher.StatusesChanged += OnWatcherStatusesChanged;

        Strings.Instance.LanguageChanged += OnAppLanguageChanged;

        _elapsedTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _elapsedTimer.Tick += (_, _) =>
        {
            var elapsed = DateTime.Now - _recordingStartedAt;
            ElapsedText = elapsed.ToString(@"mm\:ss");
        };

        _transcriptsRefreshTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _transcriptsRefreshTimer.Tick += (_, _) =>
        {
            _transcriptsRefreshTimer.Stop();
            LoadTranscriptHistoryFromDisk();
        };

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

            SelectedTarget ??= Targets.FirstOrDefault();
            CurrentView = MainViewMode.AppView;
            return;
        }

        ShowFirstRunSetup();
    }

    public void Start()
    {
        _watcher.Start();
        SyncTargetsFromWatcher();
        LoadTranscriptHistoryFromDisk();
    }

    private void OnWatcherStatusesChanged(object? sender, EventArgs e) => SyncTargetsFromWatcher();

    private void OnAppLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(RecordButtonText));
        ViewedSummarySections = SummaryFormatter.Parse(ViewedSummaryText);

        if (!IsRecording && !IsBusy && SelectedTarget is not null)
        {
            StatusText = SelectedTarget.IsRunning
                ? Strings.Instance.Format("StatusDetected", SelectedTarget.DisplayName, Strings.Instance.Get("RecordStart"))
                : Strings.Instance.Format("StatusNotRunning", SelectedTarget.DisplayName);
        }
    }

    public void Dispose()
    {
        _watcher.StatusesChanged -= OnWatcherStatusesChanged;
        _customAppsService.Changed -= OnCustomAppsChanged;
        Strings.Instance.LanguageChanged -= OnAppLanguageChanged;
        _transcriptsRefreshTimer.Stop();
        _transcriptsWatcher?.Dispose();
        _watcher.Dispose();
        _recorder.Dispose();
        _deviceRecorder.Dispose();
        _microphoneRecorder.Dispose();
        _summarizer.Dispose();
    }
}
