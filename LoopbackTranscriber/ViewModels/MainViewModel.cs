using System.Collections.ObjectModel;
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
    private readonly TargetProcessWatcher _watcher = new();
    private readonly ProcessLoopbackRecorder _recorder = new();
    private readonly WhisperTranscriptionService _whisper = new();
    private readonly SettingsService _settingsService = new();
    private readonly DispatcherTimer _elapsedTimer;

    private DateTime _recordingStartedAt;
    private string? _currentRawWavPath;

    public ObservableCollection<TargetItemViewModel> Targets { get; } = [];

    public ObservableCollection<TranscriptHistoryItem> TranscriptHistory { get; } =
    [
        new TranscriptHistoryItem
        {
            AppName = "Teams",
            Timestamp = DateTime.Now.AddHours(-2),
            Preview = "Repasamos el estado del sprint y quedamos en revisar el backlog el viernes..."
        },
        new TranscriptHistoryItem
        {
            AppName = "Zoom",
            Timestamp = DateTime.Now.AddDays(-1),
            Preview = "Llamada con el cliente sobre los requisitos de la nueva funcionalidad..."
        },
        new TranscriptHistoryItem
        {
            AppName = "Discord",
            Timestamp = DateTime.Now.AddDays(-2),
            Preview = "Sesion rapida para coordinar el despliegue de la version 1.2..."
        }
    ];

    public ObservableCollection<TranscriptHistoryItem> AppTranscriptHistory { get; } = [];

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

    public MainViewModel()
    {
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
    }

    public void Start()
    {
        _watcher.Start();
        SyncTargetsFromWatcher();
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
        CurrentView = MainViewMode.AppView;
        RefreshAppTranscriptHistory();

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

    private void RefreshAppTranscriptHistory()
    {
        AppTranscriptHistory.Clear();

        if (SelectedTarget is null)
        {
            return;
        }

        foreach (var item in TranscriptHistory.Where(h => h.AppName == SelectedTarget.DisplayName))
        {
            AppTranscriptHistory.Add(item);
        }
    }

    [RelayCommand]
    private void ShowHistory()
    {
        CurrentView = MainViewMode.History;
    }

    [RelayCommand]
    private void ShowConfig()
    {
        var configViewModel = new ConfigViewModel();
        configViewModel.SettingsSaved += OnConfigClosed;
        configViewModel.CancelRequested += OnConfigClosed;

        ConfigViewModel = configViewModel;
        CurrentView = MainViewMode.Config;
    }

    private void OnConfigClosed(object? sender, EventArgs e)
    {
        if (sender is ConfigViewModel configViewModel)
        {
            configViewModel.SettingsSaved -= OnConfigClosed;
            configViewModel.CancelRequested -= OnConfigClosed;
        }

        CurrentView = MainViewMode.AppView;
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

        try
        {
            rawWavPath = await _recorder.StopAsync();

            var settings = _settingsService.Load();
            var modelPath = settings.GetModelFilePath();

            var progress = new Progress<string>(text => StatusText = text);

            StatusText = "Comprobando el modelo de transcripcion...";
            await _whisper.EnsureModelDownloadedAsync(settings.ModelSize, modelPath, progress);

            StatusText = "Transcribiendo...";
            whisperWavPath = Path.Combine(Path.GetDirectoryName(rawWavPath)!,
                Path.GetFileNameWithoutExtension(rawWavPath) + ".16k.wav");
            AudioConverter.ConvertToWhisperFormat(rawWavPath, whisperWavPath);

            var text = await _whisper.TranscribeAsync(whisperWavPath, modelPath, settings.Language, progress);
            TranscriptText = string.IsNullOrWhiteSpace(text)
                ? "(No se detecto ningun texto en el audio)"
                : text;

            if (settings.AutoSaveTranscripts && !string.IsNullOrWhiteSpace(text))
            {
                Directory.CreateDirectory(settings.TranscriptsFolder);
                var fileName = $"{SelectedTarget?.DisplayName ?? "reunion"}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                File.WriteAllText(Path.Combine(settings.TranscriptsFolder, fileName), text);
                StatusText = $"Transcripcion lista y guardada en {settings.TranscriptsFolder}.";
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
        catch
        {

        }
    }

    [RelayCommand]
    private void CopyTranscript()
    {
        if (!string.IsNullOrEmpty(TranscriptText))
        {
            Clipboard.SetText(TranscriptText);
        }
    }

    [RelayCommand]
    private void OpenTranscriptsFolder()
    {
        var settings = _settingsService.Load();
        Directory.CreateDirectory(settings.TranscriptsFolder);
        Process.Start(new ProcessStartInfo(settings.TranscriptsFolder) { UseShellExecute = true });
    }

    public void Dispose()
    {
        _watcher.StatusesChanged -= OnWatcherStatusesChanged;
        _watcher.Dispose();
        _recorder.Dispose();
    }
}
