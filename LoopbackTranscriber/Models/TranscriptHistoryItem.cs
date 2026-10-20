using System.Globalization;
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
    public required TimeSpan? Duration { get; init; }

    public string TimestampText => Duration is { } duration
        ? $"{Timestamp:dd/MM HH:mm} \u00b7 {FormatDuration(duration)}"
        : Timestamp.ToString("dd/MM HH:mm", CultureInfo.CurrentCulture);

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1 ? duration.ToString(@"h\:mm\:ss", CultureInfo.CurrentCulture) : duration.ToString(@"m\:ss", CultureInfo.CurrentCulture);

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isGeneratingSummary;

    [ObservableProperty]
    private double _summaryProgressPercent;

    [ObservableProperty]
    private bool _hasSummary;

    [ObservableProperty]
    private string? _summaryTitle;

    [ObservableProperty]
    private string? _summaryText;

    public bool ShowIdleGenerate => !IsGeneratingSummary && !HasSummary;

    public bool ShowRegenerate => !IsGeneratingSummary && HasSummary;

    public string DisplayTitle => HasSummary && !string.IsNullOrWhiteSpace(SummaryTitle) ? SummaryTitle! : AppName;

    public bool ShowAppName => HasSummary && !string.IsNullOrWhiteSpace(SummaryTitle);

    partial void OnIsGeneratingSummaryChanged(bool value) => NotifySummaryButtonState();

    partial void OnHasSummaryChanged(bool value)
    {
        NotifySummaryButtonState();
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(ShowAppName));
    }

    partial void OnSummaryTitleChanged(string? value)
    {
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(ShowAppName));
    }

    private void NotifySummaryButtonState()
    {
        OnPropertyChanged(nameof(ShowIdleGenerate));
        OnPropertyChanged(nameof(ShowRegenerate));
    }
}
