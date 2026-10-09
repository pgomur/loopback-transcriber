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

    public ObservableCollection<TargetItemViewModel> Targets { get; } = [];

    public ObservableCollection<TranscriptHistoryItem> TranscriptHistory { get; } = [];

    public ObservableCollection<TranscriptHistoryItem> AppTranscriptHistory { get; } = [];

    public ObservableCollection<TranscriptHistoryItem> FilteredTranscriptHistory { get; } = [];

    [ObservableProperty]
    private bool _hasSelectedAppHistoryItems;

    [ObservableProperty]
    private bool _hasAppHistoryItems;

    [ObservableProperty]
    private bool _isAppHistoryFilterOpen;

    [ObservableProperty]
    private DateTime? _appHistoryFilterFromDate;

    [ObservableProperty]
    private DateTime? _appHistoryFilterToDate;

    [ObservableProperty]
    private double? _appHistoryFilterMinDurationMinutes;

    public bool HasActiveAppHistoryFilter =>
        AppHistoryFilterFromDate is not null ||
        AppHistoryFilterToDate is not null ||
        AppHistoryFilterMinDurationMinutes is not null;

    partial void OnAppHistoryFilterFromDateChanged(DateTime? value) => OnAppHistoryFilterCriteriaChanged();

    partial void OnAppHistoryFilterToDateChanged(DateTime? value) => OnAppHistoryFilterCriteriaChanged();

    partial void OnAppHistoryFilterMinDurationMinutesChanged(double? value) => OnAppHistoryFilterCriteriaChanged();

    private void OnAppHistoryFilterCriteriaChanged()
    {
        OnPropertyChanged(nameof(HasActiveAppHistoryFilter));
        RefreshAppTranscriptHistory();
    }

    [RelayCommand]
    private void ToggleAppHistoryFilter() => IsAppHistoryFilterOpen = !IsAppHistoryFilterOpen;

    [RelayCommand]
    private void ApplyAppHistoryFilter()
    {
        RefreshAppTranscriptHistory();
        IsAppHistoryFilterOpen = false;
    }

    [RelayCommand]
    private void ClearAppHistoryFilter()
    {
        AppHistoryFilterFromDate = null;
        AppHistoryFilterToDate = null;
        AppHistoryFilterMinDurationMinutes = null;
    }

    [ObservableProperty]
    private bool _isHistoryFilterOpen;

    [ObservableProperty]
    private DateTime? _historyFilterFromDate;

    [ObservableProperty]
    private DateTime? _historyFilterToDate;

    [ObservableProperty]
    private double? _historyFilterMinDurationMinutes;

    public bool HasActiveHistoryFilter =>
        HistoryFilterFromDate is not null ||
        HistoryFilterToDate is not null ||
        HistoryFilterMinDurationMinutes is not null;

    partial void OnHistoryFilterFromDateChanged(DateTime? value) => OnHistoryFilterCriteriaChanged();

    partial void OnHistoryFilterToDateChanged(DateTime? value) => OnHistoryFilterCriteriaChanged();

    partial void OnHistoryFilterMinDurationMinutesChanged(double? value) => OnHistoryFilterCriteriaChanged();

    private void OnHistoryFilterCriteriaChanged()
    {
        OnPropertyChanged(nameof(HasActiveHistoryFilter));
        RefreshFilteredTranscriptHistory();
    }

    [RelayCommand]
    private void ToggleHistoryFilter() => IsHistoryFilterOpen = !IsHistoryFilterOpen;

    [RelayCommand]
    private void ApplyHistoryFilter()
    {
        RefreshFilteredTranscriptHistory();
        IsHistoryFilterOpen = false;
    }

    [RelayCommand]
    private void ClearHistoryFilter()
    {
        HistoryFilterFromDate = null;
        HistoryFilterToDate = null;
        HistoryFilterMinDurationMinutes = null;
    }

    private void RefreshFilteredTranscriptHistory()
    {
        FilteredTranscriptHistory.Clear();

        var matches = TranscriptHistory.AsEnumerable();

        if (HistoryFilterFromDate is { } fromDate)
        {
            matches = matches.Where(h => h.Timestamp.Date >= fromDate.Date);
        }

        if (HistoryFilterToDate is { } toDate)
        {
            matches = matches.Where(h => h.Timestamp.Date <= toDate.Date);
        }

        if (HistoryFilterMinDurationMinutes is { } minDurationMinutes)
        {
            matches = matches.Where(h => h.Duration is { } duration && duration.TotalMinutes >= minDurationMinutes);
        }

        foreach (var item in matches)
        {
            FilteredTranscriptHistory.Add(item);
        }

        HasSelectedHistoryItems = FilteredTranscriptHistory.Any(i => i.IsSelected);
        NotifyDeleteAvailabilityChanged();
    }

    [ObservableProperty]
    private bool _hasHistoryItems;

    [ObservableProperty]
    private bool _hasSelectedHistoryItems;

    [ObservableProperty]
    private TargetItemViewModel? _selectedTarget;

    [ObservableProperty]
    private MainViewMode _currentView = MainViewMode.AppView;

    [ObservableProperty]
    private FirstRunViewModel? _firstRunViewModel;

    [ObservableProperty]
    private ConfigViewModel? _configViewModel;

    [ObservableProperty]
    private ApplicationsViewModel? _applicationsViewModel;

    public bool IsFirstRunActive => CurrentView == MainViewMode.FirstRun;

    public bool IsAppViewActive => CurrentView == MainViewMode.AppView;

    public bool IsHistoryViewActive => CurrentView == MainViewMode.History;

    public bool IsConfigViewActive => CurrentView == MainViewMode.Config;

    public bool IsApplicationsViewActive => CurrentView == MainViewMode.Applications;

    partial void OnCurrentViewChanged(MainViewMode value)
    {
        OnPropertyChanged(nameof(IsFirstRunActive));
        OnPropertyChanged(nameof(IsAppViewActive));
        OnPropertyChanged(nameof(IsHistoryViewActive));
        OnPropertyChanged(nameof(IsConfigViewActive));
        OnPropertyChanged(nameof(IsApplicationsViewActive));
        ApplicationsViewModel?.SetActive(value == MainViewMode.Applications);
        RefreshTargetActiveStates();
    }

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = Strings.Instance.Get("StatusSelectApp");

    [ObservableProperty]
    private string _transcriptText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<TranscriptMessage> _transcriptMessages = new();

    [ObservableProperty]
    private bool _hasViewedSummary;

    [ObservableProperty]
    private string _viewedSummaryTitle = string.Empty;

    [ObservableProperty]
    private string _viewedSummaryText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<SummarySection> _viewedSummarySections = [];

    partial void OnViewedSummaryTextChanged(string value) =>
        ViewedSummarySections = SummaryFormatter.Parse(value);

    [ObservableProperty]
    private bool _isShowingSummaryTab;

    [ObservableProperty]
    private bool _isViewedSummaryGenerating;

    public bool ShowViewedIdleGenerate => !IsViewedSummaryGenerating && !HasViewedSummary;

    public bool ShowViewedRegenerate => !IsViewedSummaryGenerating && HasViewedSummary;

    public bool ShowViewedSummaryButton =>
        IsShowingSummaryTab && !string.IsNullOrWhiteSpace(TranscriptText);

    partial void OnIsShowingSummaryTabChanged(bool value) => OnPropertyChanged(nameof(ShowViewedSummaryButton));

    partial void OnHasViewedSummaryChanged(bool value) => NotifyViewedSummaryButtonState();

    partial void OnIsViewedSummaryGeneratingChanged(bool value) => NotifyViewedSummaryButtonState();

    private void NotifyViewedSummaryButtonState()
    {
        OnPropertyChanged(nameof(ShowViewedIdleGenerate));
        OnPropertyChanged(nameof(ShowViewedRegenerate));
    }

    [ObservableProperty]
    private string _elapsedText = "00:00";

    [ObservableProperty]
    private bool _isTranscribingActive;

    [ObservableProperty]
    private double _transcriptionProgressPercent;

    public bool CanToggleRecording =>
        IsBusy == false && !IsSummaryGenerating && (IsRecording || (SelectedTarget?.IsRunning ?? false));

    private int _summaryGenerationCount;

    public bool IsSummaryGenerating => _summaryGenerationCount > 0;

    public string? RecordDisabledReason =>
        IsSummaryGenerating && !IsRecording ? Strings.Instance.Get("RecordBlockedBySummary") : null;

    private void NotifySummaryActivityChanged()
    {
        OnPropertyChanged(nameof(IsSummaryGenerating));
        OnPropertyChanged(nameof(RecordDisabledReason));
        OnPropertyChanged(nameof(CanToggleRecording));
        ToggleRecordingCommand.NotifyCanExecuteChanged();
    }

    public string RecordButtonText => IsRecording ? Strings.Instance.Get("RecordStop") : Strings.Instance.Get("RecordStart");

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

    private void SyncTargetsFromWatcher()
    {
        foreach (var status in _watcher.Statuses)
        {
            var vm = Targets.FirstOrDefault(t => t.App.Id == status.App.Id);
            vm?.UpdateFrom(status);
        }

        if (IsRecording && SelectedTarget is not null && !SelectedTarget.IsRunning)
        {
            _ = CancelRecordingAsync(SelectedTarget.DisplayName);
            return;
        }

        if (!IsRecording && !IsBusy && SelectedTarget is not null)
        {
            StatusText = SelectedTarget.IsRunning
                ? Strings.Instance.Format("StatusDetected", SelectedTarget.DisplayName, Strings.Instance.Get("RecordStart"))
                : Strings.Instance.Format("StatusNotRunning", SelectedTarget.DisplayName);
        }

        OnPropertyChanged(nameof(CanToggleRecording));
        ToggleRecordingCommand.NotifyCanExecuteChanged();
    }

    private async Task CancelRecordingAsync(string closedAppName)
    {
        _elapsedTimer.Stop();
        IsRecording = false;

        string? rawWavPath = null;
        var deviceTracks = new List<DeviceLoopbackRecorder.DeviceTrack>();

        try
        {
            rawWavPath = await _recorder.StopAsync();
        }
        catch (Exception ex)
        {
            AppLog.Warn("MainViewModel.CancelRecordingAsync: fallo al parar la captura por proceso", ex);
        }

        try
        {
            deviceTracks = await Task.Run(() => _deviceRecorder.Stop());
        }
        catch (Exception ex)
        {
            AppLog.Warn("MainViewModel.CancelRecordingAsync: fallo al parar la captura por dispositivo", ex);
        }

        string? micWavPath = null;
        try
        {
            micWavPath = await Task.Run(() => _microphoneRecorder.Stop());
        }
        catch (Exception ex)
        {
            AppLog.Warn("MainViewModel.CancelRecordingAsync: fallo al parar la captura del microfono", ex);
        }

        TryDelete(rawWavPath);
        TryDelete(micWavPath);
        foreach (var track in deviceTracks)
        {
            TryDelete(track.WavPath);
        }

        StatusText = Strings.Instance.Format("StatusRecordingCancelledAppClosed", closedAppName);

        OnPropertyChanged(nameof(CanToggleRecording));
        ToggleRecordingCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(RecordButtonText));
    }

    private void AddTargetInternal(TargetApp app)
    {
        Targets.Add(new TargetItemViewModel(app));
        _watcher.AddTarget(app);
    }

    private void OnCustomAppsChanged(object? sender, EventArgs e)
    {
        foreach (var app in _customAppsService.CustomApps)
        {
            if (Targets.All(t => t.App.Id != app.Id))
            {
                AddTargetInternal(app);
            }
        }

        var builtInIds = new HashSet<string>(TargetApp.All.Select(a => a.Id));
        var currentCustomIds = new HashSet<string>(_customAppsService.CustomApps.Select(a => a.Id));

        var toRemove = Targets
            .Where(t => !builtInIds.Contains(t.App.Id) && !currentCustomIds.Contains(t.App.Id))
            .ToList();

        foreach (var target in toRemove)
        {
            if (SelectedTarget == target)
            {
                SelectedTarget = null;
            }

            Targets.Remove(target);
            _watcher.RemoveTarget(target.App.Id);
        }

        OnPropertyChanged(nameof(CanToggleRecording));
        ToggleRecordingCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedTargetChanged(TargetItemViewModel? value)
    {
        RefreshTargetActiveStates();

        TranscriptText = string.Empty;
        _viewedTranscriptFilePath = null;
        ClearViewedSummary();

        ApplyTargetSelection(value);
    }

    private void RefreshTargetActiveStates()
    {
        var activeTarget = CurrentView == MainViewMode.AppView ? SelectedTarget : null;

        foreach (var target in Targets)
        {
            target.IsActive = target == activeTarget;
        }
    }
    private void ApplyTargetSelection(TargetItemViewModel? value)
    {
        CurrentView = MainViewMode.AppView;

        LoadTranscriptHistoryFromDisk();

        if (value is null)
        {
            return;
        }

        StatusText = value.IsRunning
            ? Strings.Instance.Format("StatusDetected", value.DisplayName, Strings.Instance.Get("RecordStart"))
            : Strings.Instance.Format("StatusNotRunning", value.DisplayName);

        OnPropertyChanged(nameof(CanToggleRecording));
        ToggleRecordingCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void SelectTarget(TargetItemViewModel? target)
    {
        if (target is null)
        {
            return;
        }

        if (CurrentView != MainViewMode.AppView)
        {
            TranscriptText = string.Empty;
            _viewedTranscriptFilePath = null;
            ClearViewedSummary();
        }

        SelectedTarget = target;
        ApplyTargetSelection(target);
    }

    private void RefreshAppTranscriptHistory()
    {
        foreach (var existing in AppTranscriptHistory)
        {
            existing.PropertyChanged -= OnAppHistoryItemPropertyChanged;
        }

        AppTranscriptHistory.Clear();
        HasSelectedAppHistoryItems = false;

        if (SelectedTarget is null)
        {
            return;
        }

        var matches = TranscriptHistory.Where(h => h.AppName == SelectedTarget.DisplayName);

        if (AppHistoryFilterFromDate is { } fromDate)
        {
            matches = matches.Where(h => h.Timestamp.Date >= fromDate.Date);
        }

        if (AppHistoryFilterToDate is { } toDate)
        {
            matches = matches.Where(h => h.Timestamp.Date <= toDate.Date);
        }

        if (AppHistoryFilterMinDurationMinutes is { } minDurationMinutes)
        {
            matches = matches.Where(h => h.Duration is { } duration && duration.TotalMinutes >= minDurationMinutes);
        }

        foreach (var item in matches)
        {
            item.IsSelected = false;
            item.PropertyChanged += OnAppHistoryItemPropertyChanged;
            AppTranscriptHistory.Add(item);
        }
    }

    private void OnAppHistoryItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TranscriptHistoryItem.IsSelected))
        {
            HasSelectedAppHistoryItems = AppTranscriptHistory.Any(i => i.IsSelected);
        }

        if (e.PropertyName is nameof(TranscriptHistoryItem.IsSelected) or nameof(TranscriptHistoryItem.IsGeneratingSummary))
        {
            NotifyDeleteAvailabilityChanged();
        }
    }

    public void ClearAppHistorySelection()
    {
        foreach (var item in AppTranscriptHistory)
        {
            item.IsSelected = false;
        }
    }

    private void OnHistoryItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TranscriptHistoryItem.IsSelected))
        {
            HasSelectedHistoryItems = FilteredTranscriptHistory.Any(i => i.IsSelected);
        }

        if (e.PropertyName is nameof(TranscriptHistoryItem.IsSelected) or nameof(TranscriptHistoryItem.IsGeneratingSummary))
        {
            NotifyDeleteAvailabilityChanged();
        }
    }

    public void ClearHistorySelection()
    {
        foreach (var item in TranscriptHistory)
        {
            item.IsSelected = false;
        }
    }

    private void LoadTranscriptHistoryFromDisk()
    {
        foreach (var existing in TranscriptHistory)
        {
            existing.PropertyChanged -= OnHistoryItemPropertyChanged;
        }

        TranscriptHistory.Clear();
        HasSelectedHistoryItems = false;

        var settings = _settingsService.Load();
        EnsureTranscriptsWatcher(settings.TranscriptsFolder);

        foreach (var item in _transcriptHistoryService.LoadFromDisk(settings.TranscriptsFolder))
        {
            item.PropertyChanged += OnHistoryItemPropertyChanged;
            TranscriptHistory.Add(item);
        }

        RefreshAppTranscriptHistory();
        RefreshFilteredTranscriptHistory();
    }

    private void EnsureTranscriptsWatcher(string folder)
    {
        if (_transcriptsWatcher is not null &&
            string.Equals(_watchedTranscriptsFolderPath, folder, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _transcriptsWatcher?.Dispose();
        _transcriptsWatcher = null;
        _watchedTranscriptsFolderPath = folder;

        try
        {
            Directory.CreateDirectory(folder);

            var watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
            };

            watcher.Deleted += OnTranscriptsFolderChanged;
            watcher.Renamed += OnTranscriptsFolderChanged;
            watcher.Created += OnTranscriptsFolderChanged;
            watcher.Error += OnTranscriptsWatcherError;
            watcher.EnableRaisingEvents = true;

            _transcriptsWatcher = watcher;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"MainViewModel.EnsureTranscriptsWatcher: no se pudo observar \"{folder}\"", ex);
        }
    }

    private void OnTranscriptsFolderChanged(object? sender, FileSystemEventArgs e)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _transcriptsRefreshTimer.Stop();
            _transcriptsRefreshTimer.Start();
        });
    }

    private void OnTranscriptsWatcherError(object? sender, ErrorEventArgs e)
    {
        AppLog.Warn("MainViewModel: el observador de la carpeta de transcripciones fallo, se reiniciara", e.GetException());

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _watchedTranscriptsFolderPath = null;
            _transcriptsRefreshTimer.Stop();
            _transcriptsRefreshTimer.Start();
        });
    }

    [RelayCommand]
    private void ShowHistory()
    {
        TranscriptText = string.Empty;
        _viewedTranscriptFilePath = null;
        ClearViewedSummary();

        LoadTranscriptHistoryFromDisk();
        CurrentView = MainViewMode.History;
    }

    private void ShowFirstRunSetup()
    {
        if (FirstRunViewModel is null)
        {
            FirstRunViewModel = new FirstRunViewModel(_settingsService, _whisper, _summarizer);
            FirstRunViewModel.SetupCompleted += OnFirstRunSetupCompleted;
        }

        CurrentView = MainViewMode.FirstRun;
    }

    private void OnFirstRunSetupCompleted(object? sender, EventArgs e)
    {
        var settings = _settingsService.Load();
        settings.FirstRunSetupCompleted = true;
        _settingsService.Save(settings);

        ShowConfig();
    }

    [RelayCommand]
    private void ShowConfig()
    {
        if (ConfigViewModel is null)
        {
            ConfigViewModel = new ConfigViewModel(_settingsService, _whisper, _summarizer);
        }
        else
        {
            ConfigViewModel.RefreshFromDisk();
        }

        CurrentView = MainViewMode.Config;
    }

    [RelayCommand]
    private void ShowApplications()
    {
        if (ApplicationsViewModel is null)
        {
            ApplicationsViewModel = new ApplicationsViewModel(_customAppsService);
        }
        else
        {
            ApplicationsViewModel.RefreshFromDisk();
        }

        CurrentView = MainViewMode.Applications;
    }

    [RelayCommand(CanExecute = nameof(CanToggleRecording))]
    private async Task ToggleRecordingAsync()
    {
        if (IsRecording)
        {
            await StopAndTranscribeAsync();
        }
        else
        {
            await StartRecordingAsync();
        }
    }

    private async Task StartRecordingAsync()
    {
        if (SelectedTarget is null || !SelectedTarget.IsRunning)
        {
            StatusText = Strings.Instance.Get("StatusOpenAndSelect");
            return;
        }

        _processLoopbackStartedAt = null;
        _deviceLoopbackStartedAt = null;
        _microphoneStartedAt = null;

        try
        {
            var wavPath = Path.Combine(Path.GetTempPath(), "LoopbackTranscriber",
                $"grabacion_{DateTime.Now:yyyyMMdd_HHmmss}.wav");

            await _recorder.StartAsync(SelectedTarget.ProcessId, wavPath);
            _currentRawWavPath = wavPath;
            _processLoopbackStartedAt = DateTime.UtcNow;

            try
            {
                var deviceDir = Path.GetDirectoryName(wavPath)!;
                var deviceBaseName = Path.GetFileNameWithoutExtension(wavPath);
                _deviceRecorder.Start(deviceDir, deviceBaseName);
                _deviceLoopbackStartedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                AppLog.Warn("MainViewModel.StartRecordingAsync: fallo al iniciar la captura por dispositivo, se continua solo con la captura por proceso", ex);
            }

            try
            {
                var micWavPath = Path.Combine(Path.GetDirectoryName(wavPath)!,
                    Path.GetFileNameWithoutExtension(wavPath) + "_mic.wav");
                _microphoneRecorder.Start(micWavPath);
                _microphoneStartedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                AppLog.Warn("MainViewModel.StartRecordingAsync: fallo al iniciar la captura del microfono, el resumen solo identificara al interlocutor", ex);
            }

            IsRecording = true;
            StatusText = Strings.Instance.Format("StatusListening", SelectedTarget.DisplayName);
            _recordingStartedAt = DateTime.Now;
            ElapsedText = "00:00";
            _elapsedTimer.Start();
        }
        catch (Exception ex)
        {
            StatusText = Strings.Instance.Format("StatusRecordFailed", ex.Message);
        }
        finally
        {
            OnPropertyChanged(nameof(CanToggleRecording));
        ToggleRecordingCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(RecordButtonText));
        }
    }

    private async Task StopAndTranscribeAsync()
    {
        _elapsedTimer.Stop();
        IsRecording = false;

        var recordingDuration = DateTime.Now - _recordingStartedAt;
        IsBusy = true;
        StatusText = Strings.Instance.Get("StatusProcessingAudio");
        OnPropertyChanged(nameof(CanToggleRecording));
        ToggleRecordingCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(RecordButtonText));

        string? rawWavPath = null;
        string? whisperWavPath = null;
        string? micRawWavPath = null;
        string? micWhisperWavPath = null;
        var deviceTracks = new List<DeviceLoopbackRecorder.DeviceTrack>();

        try
        {
            rawWavPath = await _recorder.StopAsync();

            try
            {
                deviceTracks = await Task.Run(() => _deviceRecorder.Stop());
            }
            catch (Exception ex)
            {
                AppLog.Warn("MainViewModel.StopAndTranscribeAsync: fallo al parar la captura por dispositivo, se usa solo la captura por proceso", ex);
                deviceTracks = new List<DeviceLoopbackRecorder.DeviceTrack>();
            }

            try
            {
                micRawWavPath = await Task.Run(() => _microphoneRecorder.Stop());
            }
            catch (Exception ex)
            {
                AppLog.Warn("MainViewModel.StopAndTranscribeAsync: fallo al parar la captura del microfono, el resumen solo tendra el audio del interlocutor", ex);
                micRawWavPath = null;
            }

            const double deviceSignalThreshold = 0.01;
            var bestDeviceTrack = deviceTracks
                .Where(t => t.PeakAbs >= deviceSignalThreshold)
                .OrderByDescending(t => t.PeakAbs)
                .FirstOrDefault();

            var transcriptionSourceWavPath = bestDeviceTrack is not null
                ? bestDeviceTrack.WavPath
                : rawWavPath;

            var settings = _settingsService.Load();
            var modelPath = settings.GetModelFilePath();

            var progress = new Progress<string>(text => StatusText = text);

            StatusText = Strings.Instance.Get("StatusCheckingModel");
            await _whisper.EnsureModelDownloadedAsync(settings.ModelSize, modelPath, progress);

            IsTranscribingActive = true;
            TranscriptionProgressPercent = 0;
            StatusText = Strings.Instance.Format("StatusTranscribingWithPercent", 0);

            whisperWavPath = Path.Combine(Path.GetDirectoryName(transcriptionSourceWavPath)!,
                Path.GetFileNameWithoutExtension(transcriptionSourceWavPath) + ".16k.wav");
            AudioConverter.ConvertToWhisperFormat(transcriptionSourceWavPath, whisperWavPath);

            var hasMicTrack = micRawWavPath is not null;
            if (hasMicTrack)
            {
                micWhisperWavPath = Path.Combine(Path.GetDirectoryName(micRawWavPath)!,
                    Path.GetFileNameWithoutExtension(micRawWavPath) + ".16k.wav");
                AudioConverter.ConvertToWhisperFormat(micRawWavPath!, micWhisperWavPath);

                try
                {
                    AudioConverter.ApplyNoiseGate(micWhisperWavPath);
                }
                catch (Exception ex)
                {
                    AppLog.Warn("MainViewModel.StopAndTranscribeAsync: fallo al aplicar la puerta de ruido al microfono, se transcribe sin filtrar", ex);
                }
            }

            var threadCount = WhisperTranscriptionService.GetRecommendedThreadCount(hasMicTrack ? 2 : 1);

            var otherPercent = 0;
            var micPercent = 0;

            void ReportCombinedPercent()
            {
                var combined = hasMicTrack ? (otherPercent + micPercent) / 2 : otherPercent;
                TranscriptionProgressPercent = combined;
                StatusText = Strings.Instance.Format("StatusTranscribingWithPercent", combined);
            }

            var otherPercentProgress = new Progress<int>(percent => { otherPercent = percent; ReportCombinedPercent(); });
            var micPercentProgress = new Progress<int>(percent => { micPercent = percent; ReportCombinedPercent(); });

            var otherTask = _whisper.TranscribeSegmentsAsync(whisperWavPath, modelPath, settings.Language,
                threadCount, useGpu: true, percentProgress: otherPercentProgress);
            var micTask = hasMicTrack
                ? _whisper.TranscribeSegmentsAsync(micWhisperWavPath!, modelPath, settings.Language,
                    threadCount, useGpu: true, percentProgress: micPercentProgress)
                : Task.FromResult(new List<WhisperTranscriptionService.TranscriptSegment>());

            var transcriptionStopwatch = Stopwatch.StartNew();
            await Task.WhenAll(otherTask, micTask);
            transcriptionStopwatch.Stop();

            var otherSegments = await otherTask;
            var micSegments = await micTask;

            IsTranscribingActive = false;

            var otherTrackStartedAt = bestDeviceTrack is not null
                ? _deviceLoopbackStartedAt
                : _processLoopbackStartedAt;
            var micStartOffset = TimeSpan.Zero;
            if (hasMicTrack && otherTrackStartedAt is not null && _microphoneStartedAt is not null)
            {
                micStartOffset = _microphoneStartedAt.Value - otherTrackStartedAt.Value;
            }

            const double echoSimilarityThreshold = 0.6;
            const int echoMinWordCount = 3;

            var echoTimeMargin = TimeSpan.FromSeconds(3);

            bool IsLikelyMeetingEcho(WhisperTranscriptionService.TranscriptSegment micSegment)
            {
                var micWordCount = micSegment.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
                if (micWordCount < echoMinWordCount)
                {
                    return false;
                }

                var adjustedStart = micSegment.Start + micStartOffset;
                var adjustedEnd = micSegment.End + micStartOffset;

                var overlappingText = string.Join(" ", otherSegments
                    .Where(o => adjustedStart - echoTimeMargin < o.End && o.Start < adjustedEnd + echoTimeMargin)
                    .Select(o => o.Text));

                if (string.IsNullOrWhiteSpace(overlappingText))
                {
                    return false;
                }

                var otherWordCount = overlappingText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
                if (otherWordCount < echoMinWordCount)
                {
                    return false;
                }

                return WhisperTranscriptionService.WordOverlapRatio(micSegment.Text, overlappingText) >= echoSimilarityThreshold;
            }

            var filteredMicSegments = hasMicTrack
                ? micSegments.Where(s => !IsLikelyMeetingEcho(s)).ToList()
                : micSegments;

            var orderedSegments = otherSegments
                .Select(s => (s.Start, Speaker: "Participantes", s.Text))
                .Concat(filteredMicSegments.Select(s => (Start: s.Start + micStartOffset, Speaker: SelfSpeakerLabel(settings.UserName), s.Text)))
                .OrderBy(s => s.Start)
                .Where(s => s.Text.Trim().Length > 0)
                .ToList();

            var distinctSpeakers = orderedSegments.Select(s => s.Speaker).Distinct().Count();

            var textBuilder = new StringBuilder();

            if (distinctSpeakers <= 1)
            {
                foreach (var segment in orderedSegments)
                {
                    if (textBuilder.Length > 0)
                    {
                        textBuilder.Append(' ');
                    }

                    textBuilder.Append(segment.Text.Trim());
                }
            }
            else
            {
                string? currentSpeaker = null;

                foreach (var segment in orderedSegments)
                {
                    var segmentText = segment.Text.Trim();

                    if (segment.Speaker != currentSpeaker)
                    {
                        if (currentSpeaker is not null)
                        {
                            textBuilder.Append('\n').Append('\n');
                        }

                        textBuilder.Append(segment.Speaker).Append(": ");
                        currentSpeaker = segment.Speaker;
                    }
                    else
                    {
                        textBuilder.Append(' ');
                    }

                    textBuilder.Append(segmentText);
                }
            }

            var text = textBuilder.ToString();

            TranscriptText = string.IsNullOrWhiteSpace(text)
                ? Strings.Instance.Get("StatusNoTextDetected")
                : text;

            if (!string.IsNullOrWhiteSpace(text))
            {
                var recordedAt = DateTime.Now;
                var baseName = $"{SelectedTarget?.DisplayName ?? "reunion"}_{recordedAt:yyyyMMdd_HHmmss}";
                var appFolderName = TranscriptHistoryService.GetAppFolderName(SelectedTarget?.DisplayName ?? "reunion");
                var recordingFolder = Path.Combine(settings.TranscriptsFolder, appFolderName, baseName);
                Directory.CreateDirectory(recordingFolder);

                var markdownTitle = SelectedTarget?.DisplayName ?? "Reunion";
                var markdown = $"# {markdownTitle} - {recordedAt:dd/MM/yyyy HH:mm}\n\n{text}";
                File.WriteAllText(Path.Combine(recordingFolder, baseName + ".md"), markdown);

                if (transcriptionSourceWavPath is not null && File.Exists(transcriptionSourceWavPath))
                {
                    File.Copy(transcriptionSourceWavPath, Path.Combine(recordingFolder, baseName + ".wav"), overwrite: true);
                }

                if (micRawWavPath is not null && File.Exists(micRawWavPath))
                {
                    File.Copy(micRawWavPath, Path.Combine(recordingFolder, baseName + "_tu.wav"), overwrite: true);
                }

                _viewedTranscriptFilePath = Path.Combine(recordingFolder, baseName + ".md");

                StatusText = Strings.Instance.Get("StatusTranscriptReadySaved");

                LoadTranscriptHistoryFromDisk();
            }
            else
            {
                StatusText = Strings.Instance.Get("StatusTranscriptReady");
            }
        }
        catch (Exception ex)
        {
            StatusText = Strings.Instance.Format("StatusErrorProcessing", ex.Message);
        }
        finally
        {
            IsBusy = false;
            IsTranscribingActive = false;
            OnPropertyChanged(nameof(CanToggleRecording));
        ToggleRecordingCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(RecordButtonText));

            TryDelete(rawWavPath);
            TryDelete(whisperWavPath);
            TryDelete(micRawWavPath);
            TryDelete(micWhisperWavPath);
            foreach (var track in deviceTracks)
            {
                TryDelete(track.WavPath);
            }
        }
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"MainViewModel.TryDelete: no se pudo borrar el fichero temporal \"{path}\"", ex);
        }
    }

    [RelayCommand]
    private void OpenTranscriptsFolder()
    {
        var settings = _settingsService.Load();
        Directory.CreateDirectory(settings.TranscriptsFolder);
        Process.Start(new ProcessStartInfo(settings.TranscriptsFolder) { UseShellExecute = true });
    }

    [RelayCommand]
    private void ViewHistoryTranscript(TranscriptHistoryItem? item)
    {
        if (item is null || IsRecording || IsBusy)
        {
            return;
        }

        _viewedTranscriptFilePath = item.FilePath;
        ApplyViewedSummary(item);

        if (!item.HasTxt && !item.HasMarkdown)
        {
            TranscriptText = Strings.Instance.Get("HistoryAudioOnlyText");
            return;
        }

        try
        {
            TranscriptText = File.Exists(item.FilePath)
                ? File.ReadAllText(item.FilePath)
                : item.Preview;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"MainViewModel.ViewHistoryTranscript: no se pudo leer \"{item.FilePath}\", se muestra el resumen", ex);
            TranscriptText = item.Preview;
        }
    }

    private void ApplyViewedSummary(TranscriptHistoryItem item)
    {
        IsShowingSummaryTab = false;

        if (item.HasSummary)
        {
            HasViewedSummary = true;
            ViewedSummaryTitle = item.SummaryTitle ?? string.Empty;
            ViewedSummaryText = item.SummaryText ?? string.Empty;
        }
        else
        {
            HasViewedSummary = false;
            ViewedSummaryTitle = string.Empty;
            ViewedSummaryText = string.Empty;
        }
    }

    private void ClearViewedSummary()
    {
        HasViewedSummary = false;
        ViewedSummaryTitle = string.Empty;
        ViewedSummaryText = string.Empty;
        IsShowingSummaryTab = false;
    }

    [RelayCommand]
    private void ClearViewedTranscript()
    {
        TranscriptText = string.Empty;
        _viewedTranscriptFilePath = null;
        ClearViewedSummary();
    }

    [RelayCommand]
    private void ShowTranscriptTab() => IsShowingSummaryTab = false;

    [RelayCommand]
    private void ShowSummaryTab() => IsShowingSummaryTab = true;

    [RelayCommand]
    private async Task RegenerateViewedSummaryAsync()
    {
        if (_viewedTranscriptFilePath is null || IsViewedSummaryGenerating)
        {
            return;
        }

        var item = TranscriptHistory.FirstOrDefault(h =>
            string.Equals(h.FilePath, _viewedTranscriptFilePath, StringComparison.OrdinalIgnoreCase));

        if (item is null)
        {
            return;
        }

        IsViewedSummaryGenerating = true;
        try
        {
            await GenerateSummaryAsync(item);
        }
        finally
        {
            IsViewedSummaryGenerating = false;
        }
    }

    private void ClearViewedTranscriptIfDeleted(TranscriptHistoryItem item)
    {
        if (_viewedTranscriptFilePath is not null &&
            string.Equals(_viewedTranscriptFilePath, item.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            TranscriptText = string.Empty;
            _viewedTranscriptFilePath = null;
            ClearViewedSummary();
        }
    }

    [RelayCommand]
    private async Task GenerateSummaryAsync(TranscriptHistoryItem? item)
    {
        if (item is null || item.IsGeneratingSummary || IsRecording || IsBusy)
        {
            return;
        }

        if (!item.HasTxt && !item.HasMarkdown)
        {
            MessageBox.Show(Strings.Instance.Get("SummaryNoTextBody"), Strings.Instance.Get("SummaryNoTextTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var settings = _settingsService.Load();
        var modelPath = settings.GetSummaryModelFilePath();

        if (!File.Exists(modelPath))
        {
            MessageBox.Show(Strings.Instance.Get("SummaryModelMissingBody"), Strings.Instance.Get("SummaryModelMissingTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string transcriptText;
        try
        {
            transcriptText = File.Exists(item.FilePath) ? await File.ReadAllTextAsync(item.FilePath) : item.Preview;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"MainViewModel.GenerateSummaryAsync: no se pudo leer \"{item.FilePath}\"", ex);
            transcriptText = item.Preview;
        }

        item.IsGeneratingSummary = true;
        _summaryGenerationCount++;
        NotifySummaryActivityChanged();

        try
        {
            var summary = await _summarizer.SummarizeAsync(transcriptText, modelPath);

            _transcriptHistoryService.SaveSummary(item.SummaryFilePath, summary);

            item.HasSummary = true;
            item.SummaryTitle = summary.Title;
            item.SummaryText = summary.Summary;

            if (_viewedTranscriptFilePath is not null &&
                string.Equals(_viewedTranscriptFilePath, item.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                ApplyViewedSummary(item);
                IsShowingSummaryTab = true;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"MainViewModel.GenerateSummaryAsync: fallo al generar el resumen de \"{item.FilePath}\"", ex);
            MessageBox.Show(Strings.Instance.Format("SummaryErrorBody", ex.Message), Strings.Instance.Get("ErrorTitle"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            item.IsGeneratingSummary = false;
            _summaryGenerationCount--;
            NotifySummaryActivityChanged();
        }
    }

    private bool DeleteTranscriptFile(TranscriptHistoryItem item)
    {
        if (_transcriptHistoryService.TryDeleteTranscriptFile(item, out var error))
        {
            return true;
        }

        MessageBox.Show(
            Strings.Instance.Format("ErrorDeleteTranscriptBody", item.AppName, item.TimestampText, error!.Message),
            Strings.Instance.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        return false;
    }

    private static List<TranscriptHistoryItem> ExcludeItemsBeingSummarized(List<TranscriptHistoryItem> selected) =>
        selected.Where(i => !i.IsGeneratingSummary).ToList();

    public bool CanDeleteSelectedApp => !AppTranscriptHistory.Any(i => i.IsSelected && i.IsGeneratingSummary);

    public bool CanDeleteSelected => !FilteredTranscriptHistory.Any(i => i.IsSelected && i.IsGeneratingSummary);

    public string? DeleteSelectedAppDisabledReason =>
        CanDeleteSelectedApp ? null : Strings.Instance.Get("DeleteBlockedBySummaryBody");

    public string? DeleteSelectedDisabledReason =>
        CanDeleteSelected ? null : Strings.Instance.Get("DeleteBlockedBySummaryBody");

    private void NotifyDeleteAvailabilityChanged()
    {
        OnPropertyChanged(nameof(CanDeleteSelectedApp));
        OnPropertyChanged(nameof(CanDeleteSelected));
        OnPropertyChanged(nameof(DeleteSelectedAppDisabledReason));
        OnPropertyChanged(nameof(DeleteSelectedDisabledReason));
    }

    [RelayCommand]
    private void DeleteTranscript(TranscriptHistoryItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (item.IsGeneratingSummary)
        {
            return;
        }

        var result = MessageBox.Show(
            Strings.Instance.Format("ConfirmDeleteTranscript", item.AppName, item.TimestampText),
            Strings.Instance.Get("ConfirmDeleteTranscriptTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        if (!DeleteTranscriptFile(item))
        {
            return;
        }

        TranscriptHistory.Remove(item);
        ClearViewedTranscriptIfDeleted(item);
        RefreshAppTranscriptHistory();
        RefreshFilteredTranscriptHistory();
    }

    [RelayCommand]
    private void DeleteSelectedAppTranscripts()
    {
        var selected = ExcludeItemsBeingSummarized(AppTranscriptHistory.Where(i => i.IsSelected).ToList());
        if (selected.Count == 0)
        {
            return;
        }

        var message = selected.Count == 1
            ? Strings.Instance.Get("ConfirmDeleteSelectedOne")
            : Strings.Instance.Format("ConfirmDeleteSelectedMany", selected.Count);

        var result = MessageBox.Show(message, Strings.Instance.Get("ConfirmDeleteSelectedTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        foreach (var item in selected)
        {
            if (DeleteTranscriptFile(item))
            {
                TranscriptHistory.Remove(item);
                ClearViewedTranscriptIfDeleted(item);
            }
        }

        RefreshAppTranscriptHistory();
        RefreshFilteredTranscriptHistory();
    }

    [RelayCommand]
    private void DeleteSelectedTranscripts()
    {
        var selected = ExcludeItemsBeingSummarized(FilteredTranscriptHistory.Where(i => i.IsSelected).ToList());
        if (selected.Count == 0)
        {
            return;
        }

        var message = selected.Count == 1
            ? Strings.Instance.Get("ConfirmDeleteSelectedOne")
            : Strings.Instance.Format("ConfirmDeleteSelectedMany", selected.Count);

        var result = MessageBox.Show(message, Strings.Instance.Get("ConfirmDeleteSelectedTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        foreach (var item in selected)
        {
            if (DeleteTranscriptFile(item))
            {
                TranscriptHistory.Remove(item);
                ClearViewedTranscriptIfDeleted(item);
            }
        }

        RefreshAppTranscriptHistory();
        RefreshFilteredTranscriptHistory();
    }

    private static string SelfSpeakerLabel(string? userName)
    {
        var cleaned = string.Join(' ', (userName ?? string.Empty)
            .Replace(':', ' ')
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return cleaned.Length == 0 ? "Tu" : $"{cleaned} (Tú)";
    }

    private static readonly Regex ChatSpeakerPrefixRegex =
        new(@"^(Tu|Participantes|[^\r\n:]+? \(Tú\))\s*:\s*", RegexOptions.Compiled);

    partial void OnTranscriptTextChanged(string value)
    {
        TranscriptMessages = ParseTranscriptMessages(value);
        OnPropertyChanged(nameof(ShowViewedSummaryButton));
    }

    private static ObservableCollection<TranscriptMessage> ParseTranscriptMessages(string text)
    {
        var messages = new ObservableCollection<TranscriptMessage>();

        if (string.IsNullOrWhiteSpace(text))
        {
            return messages;
        }

        var lines = text.Split('\n');
        if (lines.Length > 0 && lines[0].TrimStart().StartsWith("# ", StringComparison.Ordinal))
        {
            text = string.Join('\n', lines.Skip(1)).TrimStart('\n', '\r').Trim();
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return messages;
        }

        var paragraphs = text.Split(new[] { "\n\n" }, StringSplitOptions.None);

        foreach (var rawParagraph in paragraphs)
        {
            var paragraph = rawParagraph.Trim();
            if (paragraph.Length == 0)
            {
                continue;
            }

            var match = ChatSpeakerPrefixRegex.Match(paragraph);
            if (match.Success)
            {
                var speaker = match.Groups[1].Value;
                var body = paragraph.Substring(match.Length).Trim();
                if (body.Length > 0)
                {
                    messages.Add(new TranscriptMessage { Speaker = speaker, Text = body });
                }
            }
            else
            {
                messages.Add(new TranscriptMessage { Speaker = null, Text = paragraph });
            }
        }

        return messages;
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
