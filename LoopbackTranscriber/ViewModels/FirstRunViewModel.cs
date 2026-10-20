using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;
using LoopbackTranscriber.Models;
using LoopbackTranscriber.Services;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LoopbackTranscriber.ViewModels;

public sealed partial class FirstRunViewModel : ObservableObject, IDisposable
{
    private const string FixedModelSize = "turbo";

    private readonly ISettingsStore _settingsService;
    private readonly IWhisperTranscriber _whisper;
    private readonly ISummarizer _summarizer;

    private CancellationTokenSource? _modelDownloadCts;
    private CancellationTokenSource? _summaryModelDownloadCts;
    private bool _modelDownloadDiscardRequested;
    private bool _summaryModelDownloadDiscardRequested;

    public event EventHandler? SetupCompleted;

    [ObservableProperty]
    private string _downloadStatus = string.Empty;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private bool _isModelDownloadPaused;

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
    private bool _isSummaryModelDownloadPaused;

    [ObservableProperty]
    private double _summaryDownloadProgressPercent;

    [ObservableProperty]
    private bool _isSummaryProgressIndeterminate;

    [ObservableProperty]
    private bool _isSummaryModelDownloaded;

    [ObservableProperty]
    private string _summaryModelStatusText = string.Empty;

    public bool CanContinue => IsModelDownloaded && IsSummaryModelDownloaded;

    public bool IsDownloadingAny => IsDownloading || IsDownloadingSummaryModel;

    public bool IsModelDownloadActive => IsDownloading || IsModelDownloadPaused;

    public bool IsModelDownloadIdle => !IsModelDownloadActive;

    public bool IsSummaryModelDownloadActive => IsDownloadingSummaryModel || IsSummaryModelDownloadPaused;

    public bool IsSummaryModelDownloadIdle => !IsSummaryModelDownloadActive;

    public FirstRunViewModel(ISettingsStore settingsService, IWhisperTranscriber whisper, ISummarizer summarizer)
    {
        _settingsService = settingsService;
        _whisper = whisper;
        _summarizer = summarizer;

        RefreshModelStatus();
        RefreshSummaryModelStatus();
    }

    private void RefreshModelStatus()
    {
        var settings = _settingsService.Load();
        var path = new AppSettings { ModelSize = FixedModelSize, ModelsFolder = settings.ModelsFolder }.GetModelFilePath();

        if (File.Exists(path))
        {
            IsModelDownloaded = true;
            var sizeText = WhisperTranscriptionService.FormatBytes(new FileInfo(path).Length);
            ModelStatusText = Strings.Instance.Format("ModelStatusDownloaded", FixedModelSize, sizeText);
        }
        else
        {
            IsModelDownloaded = false;
            ModelStatusText = Strings.Instance.Format("ModelStatusNotDownloaded", FixedModelSize);
        }

        NotifyCanContinueChanged();
    }

    private void RefreshSummaryModelStatus()
    {
        var settings = _settingsService.Load();
        var path = new AppSettings { ModelsFolder = settings.ModelsFolder }.GetSummaryModelFilePath();

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

        NotifyCanContinueChanged();
    }

    private void NotifyCanContinueChanged()
    {
        OnPropertyChanged(nameof(CanContinue));
        ContinueCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsDownloadingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsDownloadingAny));
        OnPropertyChanged(nameof(IsModelDownloadActive));
        OnPropertyChanged(nameof(IsModelDownloadIdle));
    }

    partial void OnIsModelDownloadPausedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsModelDownloadActive));
        OnPropertyChanged(nameof(IsModelDownloadIdle));
    }

    partial void OnIsDownloadingSummaryModelChanged(bool value)
    {
        OnPropertyChanged(nameof(IsDownloadingAny));
        OnPropertyChanged(nameof(IsSummaryModelDownloadActive));
        OnPropertyChanged(nameof(IsSummaryModelDownloadIdle));
    }

    partial void OnIsSummaryModelDownloadPausedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSummaryModelDownloadActive));
        OnPropertyChanged(nameof(IsSummaryModelDownloadIdle));
    }

    private async Task DownloadModelNowAsync()
    {
        if (IsDownloading)
        {
            return;
        }

        IsModelDownloadPaused = false;
        IsDownloading = true;
        IsProgressIndeterminate = true;
        _modelDownloadCts?.Dispose();
        _modelDownloadCts = new CancellationTokenSource();
        try
        {
            var settings = _settingsService.Load();
            var modelSettings = new AppSettings { ModelSize = FixedModelSize, ModelsFolder = settings.ModelsFolder };
            var progress = new Progress<UserMessage>(message => DownloadStatus = message.Localize());
            var percentProgress = new Progress<double>(percent =>
            {
                IsProgressIndeterminate = false;
                DownloadProgressPercent = percent;
            });

            await _whisper.EnsureModelDownloadedAsync(FixedModelSize, modelSettings.GetModelFilePath(), progress,
                percentProgress, _modelDownloadCts.Token);

            DownloadProgressPercent = 100;
            DownloadStatus = File.Exists(modelSettings.GetModelFilePath())
                ? Strings.Instance.Get("DownloadStatusReady")
                : Strings.Instance.Get("DownloadStatusFailed");
        }
        catch (OperationCanceledException)
        {
            if (_modelDownloadDiscardRequested)
            {
                _modelDownloadDiscardRequested = false;

                var settings = _settingsService.Load();
                var modelPath = new AppSettings { ModelSize = FixedModelSize, ModelsFolder = settings.ModelsFolder }.GetModelFilePath();
                WhisperTranscriptionService.DiscardPartialDownload(modelPath);

                IsModelDownloadPaused = false;
                DownloadProgressPercent = 0;
                DownloadStatus = string.Empty;
            }
            else
            {
                IsModelDownloadPaused = true;
                DownloadStatus = Strings.Instance.Get("DownloadStatusPaused");
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("FirstRunViewModel.DownloadModelNowAsync: the download failed", ex);
            DownloadStatus = Strings.Instance.Format("DownloadStatusError", ex.ToUserMessage());
        }
        finally
        {
            IsDownloading = false;
            IsProgressIndeterminate = false;
            RefreshModelStatus();
        }
    }

    private async Task DownloadSummaryModelNowAsync()
    {
        if (IsDownloadingSummaryModel)
        {
            return;
        }

        IsSummaryModelDownloadPaused = false;
        IsDownloadingSummaryModel = true;
        IsSummaryProgressIndeterminate = true;
        _summaryModelDownloadCts?.Dispose();
        _summaryModelDownloadCts = new CancellationTokenSource();
        try
        {
            var settings = _settingsService.Load();
            var modelSettings = new AppSettings { ModelsFolder = settings.ModelsFolder };
            var progress = new Progress<UserMessage>(message => SummaryDownloadStatus = message.Localize());
            var percentProgress = new Progress<double>(percent =>
            {
                IsSummaryProgressIndeterminate = false;
                SummaryDownloadProgressPercent = percent;
            });

            var path = modelSettings.GetSummaryModelFilePath();
            await _summarizer.EnsureModelDownloadedAsync(path, progress, percentProgress, _summaryModelDownloadCts.Token);

            SummaryDownloadProgressPercent = 100;
            SummaryDownloadStatus = File.Exists(path)
                ? Strings.Instance.Get("DownloadStatusReady")
                : Strings.Instance.Get("DownloadStatusFailed");
        }
        catch (OperationCanceledException)
        {
            if (_summaryModelDownloadDiscardRequested)
            {
                _summaryModelDownloadDiscardRequested = false;

                var settings = _settingsService.Load();
                var summaryPath = new AppSettings { ModelsFolder = settings.ModelsFolder }.GetSummaryModelFilePath();
                SummarizationService.DiscardPartialDownload(summaryPath);

                IsSummaryModelDownloadPaused = false;
                SummaryDownloadProgressPercent = 0;
                SummaryDownloadStatus = string.Empty;
            }
            else
            {
                IsSummaryModelDownloadPaused = true;
                SummaryDownloadStatus = Strings.Instance.Get("DownloadStatusPaused");
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("FirstRunViewModel.DownloadSummaryModelNowAsync: the download failed", ex);
            SummaryDownloadStatus = Strings.Instance.Format("DownloadStatusError", ex.ToUserMessage());
        }
        finally
        {
            IsDownloadingSummaryModel = false;
            IsSummaryProgressIndeterminate = false;
            RefreshSummaryModelStatus();
        }
    }

    [RelayCommand]
    private async Task DownloadModelsNowAsync()
    {
        if (IsDownloading || IsDownloadingSummaryModel)
        {
            return;
        }

        var downloads = new List<Task>();

        if (!IsModelDownloaded)
        {
            downloads.Add(DownloadModelNowAsync());
        }

        if (!IsSummaryModelDownloaded)
        {
            downloads.Add(DownloadSummaryModelNowAsync());
        }

        await Task.WhenAll(downloads);
    }

    [RelayCommand]
    private Task ResumeModelDownloadAsync() => DownloadModelNowAsync();

    [RelayCommand]
    private Task ResumeSummaryModelDownloadAsync() => DownloadSummaryModelNowAsync();

    [RelayCommand]
    private void PauseModelDownload() => _modelDownloadCts?.Cancel();

    [RelayCommand]
    private void PauseSummaryModelDownload() => _summaryModelDownloadCts?.Cancel();

    [RelayCommand]
    private void CancelModelDownload()
    {
        if (IsDownloading)
        {
            _modelDownloadDiscardRequested = true;
            _modelDownloadCts?.Cancel();
            return;
        }

        var settings = _settingsService.Load();
        var modelPath = new AppSettings { ModelSize = FixedModelSize, ModelsFolder = settings.ModelsFolder }.GetModelFilePath();
        WhisperTranscriptionService.DiscardPartialDownload(modelPath);

        IsModelDownloadPaused = false;
        DownloadProgressPercent = 0;
        DownloadStatus = string.Empty;
    }

    [RelayCommand]
    private void CancelSummaryModelDownload()
    {
        if (IsDownloadingSummaryModel)
        {
            _summaryModelDownloadDiscardRequested = true;
            _summaryModelDownloadCts?.Cancel();
            return;
        }

        var settings = _settingsService.Load();
        var summaryPath = new AppSettings { ModelsFolder = settings.ModelsFolder }.GetSummaryModelFilePath();
        SummarizationService.DiscardPartialDownload(summaryPath);

        IsSummaryModelDownloadPaused = false;
        SummaryDownloadProgressPercent = 0;
        SummaryDownloadStatus = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private void Continue()
    {
        SetupCompleted?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        CancelAndDispose(ref _modelDownloadCts);
        CancelAndDispose(ref _summaryModelDownloadCts);
    }

    private static void CancelAndDispose(ref CancellationTokenSource? source)
    {
        if (source is null)
        {
            return;
        }

        try
        {
            source.Cancel();
        }
        catch (AggregateException ex)
        {
            AppLog.Warn("FirstRunViewModel.Dispose: a cancellation callback failed", ex);
        }

        source.Dispose();
        source = null;
    }
}
