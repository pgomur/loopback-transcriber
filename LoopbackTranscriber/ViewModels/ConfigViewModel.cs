using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoopbackTranscriber.Models;
using LoopbackTranscriber.Services;
using System.IO;
using System.Threading.Tasks;

namespace LoopbackTranscriber.ViewModels;

public partial class ConfigViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly WhisperTranscriptionService _whisper;

    public List<string> AvailableModelSizes { get; } = ["medium", "large"];

    public List<LanguageOption> AvailableLanguages { get; } =
    [
        new LanguageOption { Code = "auto", DisplayName = "Automatico (detecta el idioma)" },
        new LanguageOption { Code = "es", DisplayName = "Español" },
        new LanguageOption { Code = "en", DisplayName = "English" }
    ];

    public ObservableCollection<InstalledModelInfo> InstalledModels { get; } = [];

    [ObservableProperty]
    private string _modelSize;

    [ObservableProperty]
    private string _language;

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

    [ObservableProperty]
    private bool _hasNoInstalledModels = true;

    public ConfigViewModel(SettingsService settingsService, WhisperTranscriptionService whisper)
    {
        _settingsService = settingsService;
        _whisper = whisper;

        var settings = _settingsService.Load();
        _modelSize = AvailableModelSizes.Contains(settings.ModelSize, StringComparer.OrdinalIgnoreCase)
            ? settings.ModelSize
            : AvailableModelSizes[0];
        _modelsFolder = settings.ModelsFolder;
        _transcriptsFolder = settings.TranscriptsFolder;
        _autoSaveTranscripts = settings.AutoSaveTranscripts;
        _language = AvailableLanguages.Any(l => string.Equals(l.Code, settings.Language, StringComparison.OrdinalIgnoreCase))
            ? settings.Language
            : "auto";

        RefreshModelStatus();
        RefreshInstalledModels();
    }

    public void RefreshFromDisk()
    {
        RefreshModelStatus();
        RefreshInstalledModels();
    }

    private void SaveSettings()
    {
        var settings = _settingsService.Load();
        settings.ModelSize = ModelSize;
        settings.ModelsFolder = ModelsFolder;
        settings.TranscriptsFolder = TranscriptsFolder;
        settings.AutoSaveTranscripts = AutoSaveTranscripts;
        settings.Language = Language;

        _settingsService.Save(settings);
    }

    partial void OnModelSizeChanged(string value)
    {
        RefreshModelStatus();
        RefreshInstalledModels();
        SaveSettings();
    }

    partial void OnModelsFolderChanged(string value)
    {
        RefreshModelStatus();
        RefreshInstalledModels();
        SaveSettings();
    }

    partial void OnTranscriptsFolderChanged(string value)
    {
        SaveSettings();
    }

    partial void OnAutoSaveTranscriptsChanged(bool value)
    {
        SaveSettings();
    }

    partial void OnLanguageChanged(string value)
    {
        SaveSettings();
    }

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

    private void RefreshInstalledModels()
    {
        InstalledModels.Clear();

        if (!Directory.Exists(ModelsFolder))
        {
            HasNoInstalledModels = true;
            return;
        }

        IEnumerable<FileInfo> files;
        try
        {
            files = Directory.GetFiles(ModelsFolder, "ggml-*.bin")
                .Select(path => new FileInfo(path))
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"ConfigViewModel.RefreshInstalledModels: no se pudo listar \"{ModelsFolder}\"", ex);
            HasNoInstalledModels = true;
            return;
        }

        foreach (var file in files)
        {
            var sizeName = Path.GetFileNameWithoutExtension(file.Name);
            if (sizeName.StartsWith("ggml-", StringComparison.OrdinalIgnoreCase))
            {
                sizeName = sizeName["ggml-".Length..];
            }

            InstalledModels.Add(new InstalledModelInfo
            {
                ModelSize = sizeName,
                FilePath = file.FullName,
                FileSizeBytes = file.Length,
                FileSizeText = WhisperTranscriptionService.FormatBytes(file.Length),
                IsActive = string.Equals(sizeName, ModelSize, StringComparison.OrdinalIgnoreCase)
            });
        }

        HasNoInstalledModels = InstalledModels.Count == 0;
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
            RefreshInstalledModels();
        }
    }

    [RelayCommand]
    private void UseModel(InstalledModelInfo? model)
    {
        if (model is null)
        {
            return;
        }

        var match = AvailableModelSizes.FirstOrDefault(
            size => string.Equals(size, model.ModelSize, StringComparison.OrdinalIgnoreCase));

        ModelSize = match ?? model.ModelSize;
    }

    [RelayCommand]
    private void DeleteModel(InstalledModelInfo? model)
    {
        if (model is null)
        {
            return;
        }

        var result = MessageBox.Show(
            $"Eliminar el modelo \"{model.DisplayName}\" ({model.FileSizeText})? Tendras que volver a descargarlo si quieres usarlo de nuevo.",
            "Eliminar modelo",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            if (File.Exists(model.FilePath))
            {
                File.Delete(model.FilePath);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo eliminar el modelo: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        RefreshInstalledModels();
        RefreshModelStatus();
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
