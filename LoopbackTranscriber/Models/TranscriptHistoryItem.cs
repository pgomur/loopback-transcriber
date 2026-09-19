using CommunityToolkit.Mvvm.ComponentModel;

namespace LoopbackTranscriber.Models;

public sealed partial class TranscriptHistoryItem : ObservableObject
{
    public required string AppName { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string Preview { get; init; }
    public required string FilePath { get; init; }

    public string TimestampText => Timestamp.ToString("dd/MM HH:mm");

    [ObservableProperty]
    private bool _isSelected;
}
