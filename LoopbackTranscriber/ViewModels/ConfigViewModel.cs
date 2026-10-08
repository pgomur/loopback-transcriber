using System.Diagnostics;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
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
    private readonly SummarizationService _summarizer;

    private const string FixedModelSize = "turbo";

    private FileSystemWatcher? _modelsFolderWatcher;

    public List<LanguageOption> AvailableUiLanguages { get; } =
    [
        new LanguageOption { Code = "es", DisplayName = "Español" },
        new LanguageOption { Code = "en", DisplayName = "English" },
        new LanguageOption { Code = "pt", DisplayName = "Português" },
        new LanguageOption { Code = "de", DisplayName = "Deutsch" },
        new LanguageOption { Code = "ru", DisplayName = "Русский" }
    ];

    [ObservableProperty]
    private string _uiLanguage;

    [ObservableProperty]
    private bool _translucentUi;

    [ObservableProperty]
    private string _userName;

    [ObservableProperty]
    private string _modelsFolder;

    [ObservableProperty]
    private string _transcriptsFolder;

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
    private string _summaryDownloadStatus = string.Empty;

    [ObservableProperty]
    private bool _isDownloadingSummaryModel;

    [ObservableProperty]
    private double _summaryDownloadProgressPercent;

    [ObservableProperty]
    private bool _isSummaryProgressIndeterminate;

    [ObservableProperty]
    private bool _isSummaryModelDownloaded;

    [ObservableProperty]
    private string _summaryModelStatusText = string.Empty;

    public bool CanDownloadModel => !IsDownloading && !IsModelDownloaded;

    public bool CanDownloadSummaryModel => !IsDownloadingSummaryModel && !IsSummaryModelDownloaded;

    partial void OnIsDownloadingChanged(bool value) => OnPropertyChanged(nameof(CanDownloadModel));

    partial void OnIsModelDownloadedChanged(bool value) => OnPropertyChanged(nameof(CanDownloadModel));

    partial void OnIsDownloadingSummaryModelChanged(bool value) => OnPropertyChanged(nameof(CanDownloadSummaryModel));

    partial void OnIsSummaryModelDownloadedChanged(bool value) => OnPropertyChanged(nameof(CanDownloadSummaryModel));

    public ConfigViewModel(SettingsService settingsService, WhisperTranscriptionService whisper, SummarizationService summarizer)
    {
        _settingsService = settingsService;
        _whisper = whisper;
        _summarizer = summarizer;

        var settings = _settingsService.Load();
        _modelsFolder = settings.ModelsFolder;
        _transcriptsFolder = settings.TranscriptsFolder;
        _uiLanguage = AvailableUiLanguages.Any(l => string.Equals(l.Code, settings.UiLanguage, StringComparison.OrdinalIgnoreCase))
            ? settings.UiLanguage
            : "es";
        _translucentUi = settings.TranslucentUi;
        _userName = settings.UserName ?? string.Empty;

        Strings.Instance.LanguageChanged += OnAppLanguageChanged;

        RefreshModelStatus();
        RefreshSummaryModelStatus();
        StartWatchingModelsFolder();
    }

    public void RefreshFromDisk()
    {
        RefreshModelStatus();
        RefreshSummaryModelStatus();
    }

    private void OnAppLanguageChanged(object? sender, EventArgs e)
    {
        RefreshModelStatus();
    }

    private void SaveSettings()
    {
        var settings = _settingsService.Load();
        settings.ModelSize = FixedModelSize;
        settings.ModelsFolder = ModelsFolder;
        settings.TranscriptsFolder = TranscriptsFolder;
        settings.UiLanguage = UiLanguage;
        settings.TranslucentUi = TranslucentUi;
        settings.UserName = (UserName ?? string.Empty).Trim();

        _settingsService.Save(settings);
    }

    partial void OnUserNameChanged(string value) => SaveSettings();

    partial void OnModelsFolderChanged(string value)
    {
        RefreshModelStatus();
        RefreshSummaryModelStatus();
        SaveSettings();
        StartWatchingModelsFolder();
    }

    private void StartWatchingModelsFolder()
    {
        StopWatchingModelsFolder();

        try
        {
            Directory.CreateDirectory(ModelsFolder);

            var watcher = new FileSystemWatcher(ModelsFolder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size
            };

            watcher.Created += OnModelsFolderFileChanged;
            watcher.Deleted += OnModelsFolderFileChanged;
            watcher.Renamed += OnModelsFolderFileChanged;
            watcher.EnableRaisingEvents = true;

            _modelsFolderWatcher = watcher;
        }
        catch
        {
            _modelsFolderWatcher = null;
        }
    }

    private void StopWatchingModelsFolder()
    {
        if (_modelsFolderWatcher is null)
        {
            return;
        }

        _modelsFolderWatcher.EnableRaisingEvents = false;
        _modelsFolderWatcher.Created -= OnModelsFolderFileChanged;
        _modelsFolderWatcher.Deleted -= OnModelsFolderFileChanged;
        _modelsFolderWatcher.Renamed -= OnModelsFolderFileChanged;
        _modelsFolderWatcher.Dispose();
        _modelsFolderWatcher = null;
    }

    private void OnModelsFolderFileChanged(object sender, FileSystemEventArgs e)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            RefreshModelStatus();
            RefreshSummaryModelStatus();
        });
    }

    partial void OnTranscriptsFolderChanged(string value)
    {
        SaveSettings();
    }

    partial void OnUiLanguageChanged(string value)
    {
        SaveSettings();
        Strings.Instance.SetLanguage(value);
    }

    partial void OnTranslucentUiChanged(bool value)
    {
        SaveSettings();
        UiPreferences.NotifyTranslucentUiChanged(value);
    }

    private void RefreshModelStatus()
    {
        var path = new AppSettings { ModelSize = FixedModelSize, ModelsFolder = ModelsFolder }.GetModelFilePath();

        if (File.Exists(path))
        {
            IsModelDownloaded = true;
            var sizeText = WhisperTranscriptionService.FormatBytes(new FileInfo(path).Length);
            ModelStatusText = Strings.Instance.Format("SummaryModelStatusDownloaded", sizeText);
        }
        else
        {
            IsModelDownloaded = false;
            ModelStatusText = Strings.Instance.Get("SummaryModelStatusNotDownloaded");
        }
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
            var settings = new AppSettings { ModelSize = FixedModelSize, ModelsFolder = ModelsFolder };
            var progress = new Progress<string>(text => DownloadStatus = text);
            var percentProgress = new Progress<double>(percent =>
            {
                IsProgressIndeterminate = false;
                DownloadProgressPercent = percent;
            });

            await _whisper.EnsureModelDownloadedAsync(FixedModelSize, settings.GetModelFilePath(), progress, percentProgress);

            DownloadProgressPercent = 100;
            DownloadStatus = File.Exists(settings.GetModelFilePath())
                ? Strings.Instance.Get("DownloadStatusReady")
                : Strings.Instance.Get("DownloadStatusFailed");
        }
        catch (Exception ex)
        {
            DownloadStatus = Strings.Instance.Format("DownloadStatusError", ex.Message);
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
    private void ChangeModelsFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = Strings.Instance.Get("ConfigModelsFolderLabel"),
            InitialDirectory = ModelsFolder
        };

        if (dialog.ShowDialog() == true)
        {
            ModelsFolder = dialog.FolderName;
        }
    }

    [RelayCommand]
    private void OpenTranscriptsFolder()
    {
        Directory.CreateDirectory(TranscriptsFolder);
        Process.Start(new ProcessStartInfo(TranscriptsFolder) { UseShellExecute = true });
    }

    [RelayCommand]
    private void ChangeTranscriptsFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = Strings.Instance.Get("ConfigTranscriptsFolderLabel"),
            InitialDirectory = TranscriptsFolder
        };

        if (dialog.ShowDialog() == true)
        {
            TranscriptsFolder = dialog.FolderName;
        }
    }

    private void RefreshSummaryModelStatus()
    {
        var path = new AppSettings { ModelsFolder = ModelsFolder }.GetSummaryModelFilePath();

        if (File.Exists(path))
        {
            IsSummaryModelDownloaded = true;
            var sizeText = WhisperTranscriptionService.FormatBytes(new FileInfo(path).Length);
            SummaryModelStatusText = Strings.Instance.Format("SummaryModelStatusDownloaded", sizeText);
        }
        else
        {
            IsSummaryModelDownloaded = false;
            SummaryModelStatusText = Strings.Instance.Get("SummaryModelStatusNotDownloaded");
        }
    }

    [RelayCommand]
    private async Task DownloadSummaryModelNowAsync()
    {
        if (IsDownloadingSummaryModel)
        {
            return;
        }

        IsDownloadingSummaryModel = true;
        IsSummaryProgressIndeterminate = true;
        SummaryDownloadProgressPercent = 0;
        try
        {
            var settings = new AppSettings { ModelsFolder = ModelsFolder };
            var progress = new Progress<string>(text => SummaryDownloadStatus = text);
            var percentProgress = new Progress<double>(percent =>
            {
                IsSummaryProgressIndeterminate = false;
                SummaryDownloadProgressPercent = percent;
            });

            var path = settings.GetSummaryModelFilePath();
            await _summarizer.EnsureModelDownloadedAsync(path, progress, percentProgress);

            SummaryDownloadProgressPercent = 100;
            SummaryDownloadStatus = File.Exists(path)
                ? Strings.Instance.Get("DownloadStatusReady")
                : Strings.Instance.Get("DownloadStatusFailed");
        }
        catch (Exception ex)
        {
            SummaryDownloadStatus = Strings.Instance.Format("DownloadStatusError", ex.Message);
        }
        finally
        {
            IsDownloadingSummaryModel = false;
            RefreshSummaryModelStatus();
        }
    }

}
