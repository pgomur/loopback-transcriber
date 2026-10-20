using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LoopbackTranscriber.Models;
using LoopbackTranscriber.Services;

namespace LoopbackTranscriber.ViewModels;

public sealed partial class ViewerViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsStore _settings;
    private readonly ISummarizer _summarizer;
    private readonly TranscriptHistoryService _historyService;
    private readonly IDialogService _dialogs;
    private readonly IMessenger _messenger;
    private readonly TranscriptRetranscriber _retranscriber;

    public ViewerViewModel(
        ISettingsStore settings,
        ISummarizer summarizer,
        TranscriptHistoryService historyService,
        IDialogService dialogs,
        IMessenger messenger,
        TranscriptRetranscriber retranscriber)
    {
        _settings = settings;
        _summarizer = summarizer;
        _historyService = historyService;
        _dialogs = dialogs;
        _messenger = messenger;
        _retranscriber = retranscriber;

        _messenger.Register<ViewerViewModel, TranscriptDeletedMessage>(this, static (recipient, message) => recipient.ClearViewedTranscriptIfDeleted(message.FilePath));
        _messenger.Register<ViewerViewModel, TranscriptReadyMessage>(this, static (recipient, message) => recipient.ShowTranscript(message.Text, message.FilePath));
        _messenger.Register<ViewerViewModel, CaptureStateChangedMessage>(this, static (recipient, message) => recipient.OnCaptureStateChanged(message.IsActive));
    }

    public void Dispose() => _messenger.UnregisterAll(this);

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

    public void RefreshSummaryModelAvailability()
    {
        try
        {
            IsSummaryModelAvailable = File.Exists(_settings.Load().GetSummaryModelFilePath());
        }
        catch (Exception ex)
        {
            AppLog.Warn("ViewerViewModel.RefreshSummaryModelAvailability: could not check the summary model", ex);
        }
    }

    partial void OnIsShowingSummaryTabChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowRetranscribeButton));
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
        if (item is null || _captureActive)
        {
            return;
        }

        ViewedFilePath = item.FilePath;
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
            AppLog.Warn($"ViewerViewModel.ViewHistoryTranscript: could not read \"{item.FilePath}\"; showing the summary", ex);
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
    private void ClearViewedTranscript() => Clear();

    public void Clear()
    {
        TranscriptText = string.Empty;
        ViewedFilePath = null;
        ClearViewedSummary();
    }

    private void ShowTranscript(string text, string? filePath)
    {
        TranscriptText = text;

        if (filePath is not null)
        {
            ViewedFilePath = filePath;
        }
    }

    public void RefreshLanguage() => ViewedSummarySections = SummaryFormatter.Parse(ViewedSummaryText);

    private string? _viewedFilePath;

    public string? ViewedFilePath
    {
        get => _viewedFilePath;
        private set
        {
            _viewedFilePath = value;
            ViewedHasAudio = TranscriptRetranscriber.HasAudio(value);
        }
    }

    private bool _captureActive;

    private void OnCaptureStateChanged(bool isActive)
    {
        _captureActive = isActive;
        OnPropertyChanged(nameof(CanRetranscribeViewed));
    }

    [ObservableProperty]
    private bool _viewedHasAudio;

    [ObservableProperty]
    private bool _isRetranscribing;

    [ObservableProperty]
    private double _retranscribeProgressPercent;

    public bool ShowRetranscribeButton => !IsShowingSummaryTab && (ViewedHasAudio || IsRetranscribing);

    public bool CanRetranscribeViewed => ViewedHasAudio && !IsRetranscribing && !_captureActive;

    partial void OnViewedHasAudioChanged(bool value) => NotifyRetranscribeState();

    partial void OnIsRetranscribingChanged(bool value) => NotifyRetranscribeState();

    private void NotifyRetranscribeState()
    {
        OnPropertyChanged(nameof(ShowRetranscribeButton));
        OnPropertyChanged(nameof(CanRetranscribeViewed));
    }

    [RelayCommand]
    private async Task RetranscribeViewedAsync()
    {
        if (!CanRetranscribeViewed || ViewedFilePath is null)
        {
            return;
        }

        if (!_dialogs.Confirm(Strings.Instance.Get("ConfirmRetranscribeBody"), Strings.Instance.Get("ConfirmRetranscribeTitle")))
        {
            return;
        }

        var filePath = ViewedFilePath;
        RetranscribeProgressPercent = 0;
        IsRetranscribing = true;

        try
        {
            var callbacks = new RecordingCallbacks(
                _ => { },
                percent => RetranscribeProgressPercent = percent,
                _ => { });

            var result = await _retranscriber.RetranscribeAsync(filePath, callbacks);

            if (ViewedFilePath is not null && string.Equals(ViewedFilePath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                ViewedFilePath = result.FilePath;
                TranscriptText = result.FileText;
            }

            _messenger.Send(new RecordingSavedMessage());
        }
        catch (Exception ex)
        {
            AppLog.Warn($"ViewerViewModel.RetranscribeViewedAsync: failed to re-transcribe \"{filePath}\"", ex);
            _dialogs.ShowError(Strings.Instance.Format("RetranscribeErrorBody", ex.ToUserMessage()), Strings.Instance.Get("ErrorTitle"));
        }
        finally
        {
            IsRetranscribing = false;
        }
    }

    private int _summaryGenerationCount;

    public bool IsSummaryGenerating => _summaryGenerationCount > 0;

    private void NotifySummaryActivityChanged()
    {
        OnPropertyChanged(nameof(IsSummaryGenerating));
        _messenger.Send(new SummaryActivityChangedMessage(IsSummaryGenerating));
    }

    [RelayCommand]
    private void ShowTranscriptTab() => IsShowingSummaryTab = false;

    [RelayCommand]
    private void ShowSummaryTab() => IsShowingSummaryTab = true;

    [RelayCommand]
    private async Task RegenerateViewedSummaryAsync()
    {
        if (ViewedFilePath is null || IsViewedSummaryGenerating)
        {
            return;
        }

        var item = FindHistoryItem(ViewedFilePath);

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

    private TranscriptHistoryItem? FindHistoryItem(string filePath)
    {
        var request = _messenger.Send(new FindTranscriptRequest(filePath));
        return request.HasReceivedResponse ? request.Response : null;
    }

    private void ClearViewedTranscriptIfDeleted(string filePath)
    {
        if (ViewedFilePath is not null &&
            string.Equals(ViewedFilePath, filePath, StringComparison.OrdinalIgnoreCase))
        {
            TranscriptText = string.Empty;
            ViewedFilePath = null;
            ClearViewedSummary();
        }
    }

    [RelayCommand]
    private async Task GenerateSummaryAsync(TranscriptHistoryItem? item)
    {
        if (item is null || item.IsGeneratingSummary || _captureActive)
        {
            return;
        }

        if (!item.HasTxt && !item.HasMarkdown)
        {
            _dialogs.ShowInfo(Strings.Instance.Get("SummaryNoTextBody"), Strings.Instance.Get("SummaryNoTextTitle"));
            return;
        }

        var settings = _settings.Load();
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
            AppLog.Warn($"ViewerViewModel.GenerateSummaryAsync: could not read \"{item.FilePath}\"", ex);
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
                    string.Equals(ViewedFilePath, item.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    ViewedSummaryProgressPercent = percent;
                }
            });

            var summary = await _summarizer.SummarizeAsync(transcriptText, modelPath, Strings.Instance.Language, percentProgress);

            _historyService.SaveSummary(item.SummaryFilePath, summary);

            item.HasSummary = true;
            item.SummaryTitle = summary.Title;
            item.SummaryText = summary.Summary;

            if (ViewedFilePath is not null &&
                string.Equals(ViewedFilePath, item.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                ApplyViewedSummary(item);
                IsShowingSummaryTab = true;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"ViewerViewModel.GenerateSummaryAsync: failed to generate the summary of \"{item.FilePath}\"", ex);
            _dialogs.ShowError(Strings.Instance.Format("SummaryErrorBody", ex.ToUserMessage()), Strings.Instance.Get("ErrorTitle"));
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
