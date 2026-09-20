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
    private readonly SettingsService _settingsService;
    private readonly TranscriptHistoryService _transcriptHistoryService;
    private readonly DispatcherTimer _elapsedTimer;

    private DateTime _recordingStartedAt;
    private string? _currentRawWavPath;

    public ObservableCollection<TargetItemViewModel> Targets { get; } = [];

    public ObservableCollection<TranscriptHistoryItem> TranscriptHistory { get; } = [];

    public ObservableCollection<TranscriptHistoryItem> AppTranscriptHistory { get; } = [];

    [ObservableProperty]
    private bool _hasSelectedAppHistoryItems;

    [ObservableProperty]
    private TargetItemViewModel? _selectedTarget;

    [ObservableProperty]
    private MainViewMode _currentView = MainViewMode.AppView;

    [ObservableProperty]
    private ConfigViewModel? _configViewModel;

    public bool IsAppViewActive => CurrentView == MainViewMode.AppView;

    public bool IsHistoryViewActive => CurrentView == MainViewMode.History;

    public bool IsConfigViewActive => CurrentView == MainViewMode.Config;

    partial void OnCurrentViewChanged(MainViewMode value)
    {
        OnPropertyChanged(nameof(IsAppViewActive));
        OnPropertyChanged(nameof(IsHistoryViewActive));
        OnPropertyChanged(nameof(IsConfigViewActive));
    }

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Selecciona una aplicacion para empezar a escuchar.";

    [ObservableProperty]
    private string _transcriptText = string.Empty;

    [ObservableProperty]
    private string _elapsedText = "00:00";

    public bool CanToggleRecording => IsBusy == false && (IsRecording || (SelectedTarget?.IsRunning ?? false));

    public string RecordButtonText => IsRecording ? "Detener grabacion" : "Iniciar grabacion";

    public MainViewModel(
        TargetProcessWatcher watcher,
        ProcessLoopbackRecorder recorder,
        DeviceLoopbackRecorder deviceRecorder,
        WhisperTranscriptionService whisper,
        SettingsService settingsService,
        TranscriptHistoryService transcriptHistoryService)
    {
        _watcher = watcher;
        _recorder = recorder;
        _deviceRecorder = deviceRecorder;
        _whisper = whisper;
        _settingsService = settingsService;
        _transcriptHistoryService = transcriptHistoryService;

        foreach (var app in TargetApp.All)
        {
            Targets.Add(new TargetItemViewModel(app));
        }

        _watcher.StatusesChanged += OnWatcherStatusesChanged;

        _elapsedTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _elapsedTimer.Tick += (_, _) =>
        {
            var elapsed = DateTime.Now - _recordingStartedAt;
            ElapsedText = elapsed.ToString(@"mm\:ss");
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

    private void SyncTargetsFromWatcher()
    {
        foreach (var status in _watcher.Statuses)
        {
            var vm = Targets.FirstOrDefault(t => t.App.Id == status.App.Id);
            vm?.UpdateFrom(status);
        }

        OnPropertyChanged(nameof(CanToggleRecording));
        ToggleRecordingCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedTargetChanged(TargetItemViewModel? value)
    {
        TranscriptText = string.Empty;

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
            ? $"{value.DisplayName} detectado. Pulsa \"Iniciar grabacion\" para empezar."
            : $"{value.DisplayName} no parece estar abierto todavia. Abrelo y vuelve a seleccionarlo.";

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

    private void LoadTranscriptHistoryFromDisk()
    {
        TranscriptHistory.Clear();

        var settings = _settingsService.Load();
        foreach (var item in _transcriptHistoryService.LoadFromDisk(settings.TranscriptsFolder))
        {
            TranscriptHistory.Add(item);
        }

        RefreshAppTranscriptHistory();
    }

    [RelayCommand]
    private void ShowHistory()
    {
        LoadTranscriptHistoryFromDisk();
        CurrentView = MainViewMode.History;
    }

    [RelayCommand]
    private void ShowConfig()
    {
        if (ConfigViewModel is null)
        {
            ConfigViewModel = new ConfigViewModel(_settingsService, _whisper);
        }
        else
        {
            ConfigViewModel.RefreshFromDisk();
        }

        CurrentView = MainViewMode.Config;
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
            StatusText = "Abre la aplicacion y selecciona una que este en ejecucion.";
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
            StatusText = $"Escuchando {SelectedTarget.DisplayName}...";
            _recordingStartedAt = DateTime.Now;
            ElapsedText = "00:00";
            _elapsedTimer.Start();
        }
        catch (Exception ex)
        {
            StatusText = $"No se pudo iniciar la grabacion: {ex.Message}";
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
        StatusText = "Procesando audio...";
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

            StatusText = "Comprobando el modelo de transcripcion...";
            await _whisper.EnsureModelDownloadedAsync(settings.ModelSize, modelPath, progress);

            StatusText = "Transcribiendo...";
            whisperWavPath = Path.Combine(Path.GetDirectoryName(transcriptionSourceWavPath)!,
                Path.GetFileNameWithoutExtension(transcriptionSourceWavPath) + ".16k.wav");
            AudioConverter.ConvertToWhisperFormat(transcriptionSourceWavPath, whisperWavPath);

            var transcriptProgress = new Progress<string>(text => TranscriptText = text);
            var text = await _whisper.TranscribeAsync(whisperWavPath, modelPath, settings.Language, transcriptProgress);
            TranscriptText = string.IsNullOrWhiteSpace(text)
                ? "(No se detecto ningun texto en el audio)"
                : text;

            if (settings.AutoSaveTranscripts && !string.IsNullOrWhiteSpace(text))
            {
                Directory.CreateDirectory(settings.TranscriptsFolder);
                var baseName = $"{SelectedTarget?.DisplayName ?? "reunion"}_{DateTime.Now:yyyyMMdd_HHmmss}";
                File.WriteAllText(Path.Combine(settings.TranscriptsFolder, baseName + ".txt"), text);

                if (whisperWavPath is not null && File.Exists(whisperWavPath))
                {
                    File.Copy(whisperWavPath, Path.Combine(settings.TranscriptsFolder, baseName + ".wav"), overwrite: true);
                }

                StatusText = "Transcripcion lista y guardada";
                LoadTranscriptHistoryFromDisk();
            }
            else
            {
                StatusText = "Transcripcion lista.";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error al procesar la grabacion: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
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

    private bool DeleteTranscriptFile(TranscriptHistoryItem item)
    {
        if (_transcriptHistoryService.TryDeleteTranscriptFile(item, out var error))
        {
            return true;
        }

        MessageBox.Show(
            $"No se pudo eliminar la transcripcion de \"{item.AppName}\" del {item.TimestampText}: {error!.Message}",
            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            $"Eliminar la transcripcion de \"{item.AppName}\" del {item.TimestampText}? Esta accion no se puede deshacer.",
            "Eliminar transcripcion",
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
            ? "Eliminar la transcripcion seleccionada? Esta accion no se puede deshacer."
            : $"Eliminar las {selected.Count} transcripciones seleccionadas? Esta accion no se puede deshacer.";

        var result = MessageBox.Show(message, "Eliminar transcripciones",
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
            }
        }

        RefreshAppTranscriptHistory();
    }

    public void Dispose()
    {
        _watcher.StatusesChanged -= OnWatcherStatusesChanged;
        _watcher.Dispose();
        _recorder.Dispose();
        _deviceRecorder.Dispose();
    }
}
