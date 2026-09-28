using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoopbackTranscriber.Models;
using LoopbackTranscriber.Services;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LoopbackTranscriber.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly TargetProcessWatcher _watcher;
    private readonly ProcessLoopbackRecorder _recorder;
    private readonly DeviceLoopbackRecorder _deviceRecorder;
    private readonly WhisperTranscriptionService _whisper;
    private readonly SummarizationService _summarizer;
    private readonly SettingsService _settingsService;
    private readonly TranscriptHistoryService _transcriptHistoryService;
    private readonly CustomAppsService _customAppsService;
    private readonly DispatcherTimer _elapsedTimer;

    private DateTime _recordingStartedAt;
    private string? _currentRawWavPath;
    private FileSystemWatcher? _transcriptsWatcher;
    private string? _watchedTranscriptsFolderPath;
    private string? _viewedTranscriptFilePath;
    private readonly DispatcherTimer _transcriptsRefreshTimer;

    public ObservableCollection<TargetItemViewModel> Targets { get; } = [];

    public ObservableCollection<TranscriptHistoryItem> TranscriptHistory { get; } = [];

    public ObservableCollection<TranscriptHistoryItem> AppTranscriptHistory { get; } = [];

    [ObservableProperty]
    private bool _hasSelectedAppHistoryItems;

    [ObservableProperty]
    private bool _hasSelectedHistoryItems;

    [ObservableProperty]
    private TargetItemViewModel? _selectedTarget;

    [ObservableProperty]
    private MainViewMode _currentView = MainViewMode.AppView;

    [ObservableProperty]
    private ConfigViewModel? _configViewModel;

    [ObservableProperty]
    private ApplicationsViewModel? _applicationsViewModel;

    public bool IsAppViewActive => CurrentView == MainViewMode.AppView;

    public bool IsHistoryViewActive => CurrentView == MainViewMode.History;

    public bool IsConfigViewActive => CurrentView == MainViewMode.Config;

    public bool IsApplicationsViewActive => CurrentView == MainViewMode.Applications;

    partial void OnCurrentViewChanged(MainViewMode value)
    {
        OnPropertyChanged(nameof(IsAppViewActive));
        OnPropertyChanged(nameof(IsHistoryViewActive));
        OnPropertyChanged(nameof(IsConfigViewActive));
        OnPropertyChanged(nameof(IsApplicationsViewActive));
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
    private bool _hasViewedSummary;

    [ObservableProperty]
    private string _viewedSummaryTitle = string.Empty;

    [ObservableProperty]
    private string _viewedSummaryText = string.Empty;

    [ObservableProperty]
    private bool _isShowingSummaryTab;

    [ObservableProperty]
    private string _elapsedText = "00:00";

    [ObservableProperty]
    private bool _isTranscribingActive;

    [ObservableProperty]
    private double _transcriptionProgressPercent;

    [ObservableProperty]
    private string _transcriptionProgressText = string.Empty;

    public bool CanToggleRecording => IsBusy == false && (IsRecording || (SelectedTarget?.IsRunning ?? false));

    public string RecordButtonText => IsRecording ? Strings.Instance.Get("RecordStop") : Strings.Instance.Get("RecordStart");

    public MainViewModel(
        TargetProcessWatcher watcher,
        ProcessLoopbackRecorder recorder,
        DeviceLoopbackRecorder deviceRecorder,
        WhisperTranscriptionService whisper,
        SummarizationService summarizer,
        SettingsService settingsService,
        TranscriptHistoryService transcriptHistoryService,
        CustomAppsService customAppsService)
    {
        _watcher = watcher;
        _recorder = recorder;
        _deviceRecorder = deviceRecorder;
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

        ShowConfig();
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

        TryDelete(rawWavPath);
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
        Targets.Add(new TargetItemViewModel(app, _settingsService));
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
        TranscriptText = string.Empty;
        _viewedTranscriptFilePath = null;
        ClearViewedSummary();

        ApplyTargetSelection(value);
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

        foreach (var item in TranscriptHistory.Where(h => h.AppName == SelectedTarget.DisplayName))
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
    }

    private void OnHistoryItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TranscriptHistoryItem.IsSelected))
        {
            HasSelectedHistoryItems = TranscriptHistory.Any(i => i.IsSelected);
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

        try
        {
            var wavPath = Path.Combine(Path.GetTempPath(), "LoopbackTranscriber",
                $"grabacion_{DateTime.Now:yyyyMMdd_HHmmss}.wav");

            await _recorder.StartAsync(SelectedTarget.ProcessId, wavPath);
            _currentRawWavPath = wavPath;

            try
            {
                var deviceDir = Path.GetDirectoryName(wavPath)!;
                var deviceBaseName = Path.GetFileNameWithoutExtension(wavPath);
                _deviceRecorder.Start(deviceDir, deviceBaseName);
            }
            catch (Exception ex)
            {
                AppLog.Warn("MainViewModel.StartRecordingAsync: fallo al iniciar la captura por dispositivo, se continua solo con la captura por proceso", ex);
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
        IsBusy = true;
        StatusText = Strings.Instance.Get("StatusProcessingAudio");
        OnPropertyChanged(nameof(CanToggleRecording));
        ToggleRecordingCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(RecordButtonText));

        string? rawWavPath = null;
        string? whisperWavPath = null;
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

            StatusText = Strings.Instance.Get("StatusTranscribing");

            IsTranscribingActive = true;
            TranscriptionProgressPercent = 0;
            TranscriptionProgressText = Strings.Instance.Format("TranscriptionProgressText", 0, "--:--");
            var transcriptionStopwatch = Stopwatch.StartNew();

            whisperWavPath = Path.Combine(Path.GetDirectoryName(transcriptionSourceWavPath)!,
                Path.GetFileNameWithoutExtension(transcriptionSourceWavPath) + ".16k.wav");
            AudioConverter.ConvertToWhisperFormat(transcriptionSourceWavPath, whisperWavPath);

            var transcriptProgress = new Progress<string>(text => TranscriptText = text);

            var transcriptPercentProgress = new Progress<int>(percent =>
            {
                TranscriptionProgressPercent = percent;

                if (percent <= 0)
                {
                    return;
                }

                var elapsed = transcriptionStopwatch.Elapsed;
                var estimatedTotal = TimeSpan.FromTicks((long)(elapsed.Ticks * 100.0 / percent));
                var remaining = estimatedTotal - elapsed;
                if (remaining < TimeSpan.Zero)
                {
                    remaining = TimeSpan.Zero;
                }

                TranscriptionProgressText = Strings.Instance.Format("TranscriptionProgressText", percent,
                    remaining.ToString(@"mm\:ss"));
            });

            var text = await _whisper.TranscribeAsync(whisperWavPath, modelPath, settings.Language,
                transcriptProgress, transcriptPercentProgress);
            IsTranscribingActive = false;

            TranscriptText = string.IsNullOrWhiteSpace(text)
                ? Strings.Instance.Get("StatusNoTextDetected")
                : text;

            var saveAudio = SelectedTarget?.SaveAudio ?? true;
            var saveTxt = SelectedTarget?.SaveTxt ?? true;
            var saveMarkdown = SelectedTarget?.SaveMarkdown ?? true;

            if (!string.IsNullOrWhiteSpace(text) && (saveAudio || saveTxt || saveMarkdown))
            {
                var recordedAt = DateTime.Now;
                var baseName = $"{SelectedTarget?.DisplayName ?? "reunion"}_{recordedAt:yyyyMMdd_HHmmss}";
                var recordingFolder = Path.Combine(settings.TranscriptsFolder, baseName);
                Directory.CreateDirectory(recordingFolder);

                if (saveTxt)
                {
                    File.WriteAllText(Path.Combine(recordingFolder, baseName + ".txt"), text);
                }

                if (saveMarkdown)
                {
                    var markdownTitle = SelectedTarget?.DisplayName ?? "Reunion";
                    var markdown = $"# {markdownTitle} - {recordedAt:dd/MM/yyyy HH:mm}\n\n{text}";
                    File.WriteAllText(Path.Combine(recordingFolder, baseName + ".md"), markdown);
                }

                if (saveAudio && whisperWavPath is not null && File.Exists(whisperWavPath))
                {
                    File.Copy(whisperWavPath, Path.Combine(recordingFolder, baseName + ".wav"), overwrite: true);
                }

                _viewedTranscriptFilePath = saveTxt
                    ? Path.Combine(recordingFolder, baseName + ".txt")
                    : saveMarkdown
                        ? Path.Combine(recordingFolder, baseName + ".md")
                        : Path.Combine(recordingFolder, baseName + ".wav");

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
    private void ShowTranscriptTab() => IsShowingSummaryTab = false;

    [RelayCommand]
    private void ShowSummaryTab() => IsShowingSummaryTab = true;

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

    [RelayCommand]
    private void DeleteTranscript(TranscriptHistoryItem? item)
    {
        if (item is null)
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
        HasSelectedHistoryItems = TranscriptHistory.Any(i => i.IsSelected);
        RefreshAppTranscriptHistory();
    }

    [RelayCommand]
    private void DeleteSelectedAppTranscripts()
    {
        var selected = AppTranscriptHistory.Where(i => i.IsSelected).ToList();
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

        HasSelectedHistoryItems = TranscriptHistory.Any(i => i.IsSelected);
        RefreshAppTranscriptHistory();
    }

    [RelayCommand]
    private void DeleteSelectedTranscripts()
    {
        var selected = TranscriptHistory.Where(i => i.IsSelected).ToList();
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

        HasSelectedHistoryItems = TranscriptHistory.Any(i => i.IsSelected);
        RefreshAppTranscriptHistory();
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
        _summarizer.Dispose();
    }
}
