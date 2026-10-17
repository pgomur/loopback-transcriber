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

public partial class MainViewModel
{
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

    [ObservableProperty]
    private bool _isSummaryModelAvailable = true;

    public bool CanGenerateViewedSummary => !IsViewedSummaryGenerating && IsSummaryModelAvailable;

    public bool ShowSummaryModelWarning => ShowViewedSummaryButton && !IsSummaryModelAvailable;

    partial void OnIsSummaryModelAvailableChanged(bool value)
    {
        OnPropertyChanged(nameof(CanGenerateViewedSummary));
        OnPropertyChanged(nameof(ShowSummaryModelWarning));
    }

    private void RefreshSummaryModelAvailability()
    {
        try
        {
            IsSummaryModelAvailable = File.Exists(_settingsService.Load().GetSummaryModelFilePath());
        }
        catch (Exception ex)
        {
            AppLog.Warn("MainViewModel.RefreshSummaryModelAvailability: no se pudo comprobar el modelo de resumen", ex);
        }
    }

    partial void OnIsShowingSummaryTabChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowViewedSummaryButton));
        OnPropertyChanged(nameof(ShowSummaryModelWarning));
    }

    partial void OnHasViewedSummaryChanged(bool value) => NotifyViewedSummaryButtonState();

    partial void OnIsViewedSummaryGeneratingChanged(bool value)
    {
        if (!value)
        {
            ViewedSummaryProgressPercent = 0;
        }

        NotifyViewedSummaryButtonState();
        OnPropertyChanged(nameof(CanGenerateViewedSummary));
    }

    [ObservableProperty]
    private double _viewedSummaryProgressPercent;

    private void NotifyViewedSummaryButtonState()
    {
        OnPropertyChanged(nameof(ShowViewedIdleGenerate));
        OnPropertyChanged(nameof(ShowViewedRegenerate));
    }

    [RelayCommand]
    private void ViewHistoryTranscript(TranscriptHistoryItem? item)
    {
        if (item is null || IsRecording || IsBusy)
        {
            return;
        }

        _viewedTranscriptFilePath = item.FilePath;
        RefreshSummaryModelAvailability();
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
            _dialogs.ShowInfo(Strings.Instance.Get("SummaryNoTextBody"), Strings.Instance.Get("SummaryNoTextTitle"));
            return;
        }

        var settings = _settingsService.Load();
        var modelPath = settings.GetSummaryModelFilePath();

        if (!File.Exists(modelPath))
        {
            RefreshSummaryModelAvailability();
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

        item.SummaryProgressPercent = 0;
        item.IsGeneratingSummary = true;
        _summaryGenerationCount++;
        NotifySummaryActivityChanged();

        try
        {
            var percentProgress = new Progress<double>(percent =>
            {
                item.SummaryProgressPercent = percent;

                if (IsViewedSummaryGenerating &&
                    string.Equals(_viewedTranscriptFilePath, item.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    ViewedSummaryProgressPercent = percent;
                }
            });

            var summary = await _summarizer.SummarizeAsync(transcriptText, modelPath, percentProgress);

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
            _dialogs.ShowError(Strings.Instance.Format("SummaryErrorBody", ex.Message), Strings.Instance.Get("ErrorTitle"));
        }
        finally
        {
            item.IsGeneratingSummary = false;
            _summaryGenerationCount--;
            NotifySummaryActivityChanged();
        }
    }

    partial void OnTranscriptTextChanged(string value)
    {
        TranscriptMessages = TranscriptMessageParser.Parse(value);
        OnPropertyChanged(nameof(ShowViewedSummaryButton));
        OnPropertyChanged(nameof(ShowSummaryModelWarning));
    }
}
