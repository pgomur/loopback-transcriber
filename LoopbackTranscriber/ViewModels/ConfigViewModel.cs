using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoopbackTranscriber.Models;
using LoopbackTranscriber.Services;
using System.IO;
using System.Threading.Tasks;

namespace LoopbackTranscriber.ViewModels;

public partial class ConfigViewModel : ObservableObject
{
    private readonly SettingsService _settingsService = new();
    private readonly WhisperTranscriptionService _whisper = new();

    public List<string> AvailableModelSizes { get; } = ["tiny", "base", "small", "medium", "large"];

    [ObservableProperty]
    private string _modelSize;

    [ObservableProperty]
    private string _modelsFolder;

    [ObservableProperty]
    private string _transcriptsFolder;

    [ObservableProperty]
    private bool _autoSaveTranscripts;

    [ObservableProperty]
    private string _downloadStatus = string.Empty;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _downloadProgressPercent;

    [ObservableProperty]
    private bool _isProgressIndeterminate;

    [ObservableProperty]
    private bool _isModelDownloaded;

    [ObservableProperty]
    private string _modelStatusText = string.Empty;

    public event EventHandler? SettingsSaved;

    public ConfigViewModel()
    {
        var settings = _settingsService.Load();
        _modelSize = settings.ModelSize;
        _modelsFolder = settings.ModelsFolder;
        _transcriptsFolder = settings.TranscriptsFolder;
        _autoSaveTranscripts = settings.AutoSaveTranscripts;

        RefreshModelStatus();
    }

    partial void OnModelSizeChanged(string value) => RefreshModelStatus();

    partial void OnModelsFolderChanged(string value) => RefreshModelStatus();

    private void RefreshModelStatus()
    {
        var path = new AppSettings { ModelSize = ModelSize, ModelsFolder = ModelsFolder }.GetModelFilePath();

        if (File.Exists(path))
        {
            IsModelDownloaded = true;
            var sizeText = WhisperTranscriptionService.FormatBytes(new FileInfo(path).Length);
            ModelStatusText = $"Modelo \"{ModelSize}\" ya descargado ({sizeText}).";
        }
        else
        {
            IsModelDownloaded = false;
            ModelStatusText = $"El modelo \"{ModelSize}\" todavia no esta descargado.";
        }
    }

    [RelayCommand]
    private void Save()
    {
        var settings = new AppSettings
        {
            ModelSize = ModelSize,
            ModelsFolder = ModelsFolder,
            TranscriptsFolder = TranscriptsFolder,
            AutoSaveTranscripts = AutoSaveTranscripts
        };

        _settingsService.Save(settings);
        SettingsSaved?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task DownloadModelNowAsync()
    {
        if (IsDownloading)
        {
            return;
        }

        IsDownloading = true;
        IsProgressIndeterminate = true;
        DownloadProgressPercent = 0;
        try
        {
            var settings = new AppSettings { ModelSize = ModelSize, ModelsFolder = ModelsFolder };
            var progress = new Progress<string>(text => DownloadStatus = text);
            var percentProgress = new Progress<double>(percent =>
            {
                IsProgressIndeterminate = false;
                DownloadProgressPercent = percent;
            });

            await _whisper.EnsureModelDownloadedAsync(ModelSize, settings.GetModelFilePath(), progress, percentProgress);

            DownloadProgressPercent = 100;
            DownloadStatus = File.Exists(settings.GetModelFilePath())
                ? "Modelo listo para usarse."
                : "No se pudo descargar el modelo.";
        }
        catch (Exception ex)
        {
            DownloadStatus = $"Error al descargar el modelo: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
            RefreshModelStatus();
        }
    }

    [RelayCommand]
    private void OpenModelsFolder()
    {
        Directory.CreateDirectory(ModelsFolder);
        Process.Start(new ProcessStartInfo(ModelsFolder) { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenTranscriptsFolder()
    {
        Directory.CreateDirectory(TranscriptsFolder);
        Process.Start(new ProcessStartInfo(TranscriptsFolder) { UseShellExecute = true });
    }
}
