using CommunityToolkit.Mvvm.ComponentModel;

namespace LoopbackTranscriber.Models;

public sealed partial class TranscriptHistoryItem : ObservableObject
{
    public required string AppName { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string Preview { get; init; }
    public required string FilePath { get; init; }
    public required bool HasTxt { get; init; }
    public required bool HasMarkdown { get; init; }
    public required bool HasAudio { get; init; }
    public required string SummaryFilePath { get; init; }

    public string TimestampText => Timestamp.ToString("dd/MM HH:mm");

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isGeneratingSummary;

    [ObservableProperty]
    private bool _hasSummary;

    [ObservableProperty]
    private string? _summaryTitle;

    [ObservableProperty]
    private string? _summaryText;

    public bool ShowIdleGenerate => !IsGeneratingSummary && !HasSummary;

    public bool ShowRegenerate => !IsGeneratingSummary && HasSummary;

    public string DisplayTitle => HasSummary && !string.IsNullOrWhiteSpace(SummaryTitle) ? SummaryTitle! : AppName;

    partial void OnIsGeneratingSummaryChanged(bool value) => NotifySummaryButtonState();

    partial void OnHasSummaryChanged(bool value)
    {
        NotifySummaryButtonState();
        OnPropertyChanged(nameof(DisplayTitle));
    }

    partial void OnSummaryTitleChanged(string? value) => OnPropertyChanged(nameof(DisplayTitle));

    private void NotifySummaryButtonState()
    {
        OnPropertyChanged(nameof(ShowIdleGenerate));
        OnPropertyChanged(nameof(ShowRegenerate));
    }
}
