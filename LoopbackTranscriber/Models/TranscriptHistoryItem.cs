namespace LoopbackTranscriber.Models;

public sealed class TranscriptHistoryItem
{
    public required string AppName { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string Preview { get; init; }

    public string TimestampText => Timestamp.ToString("dd/MM HH:mm");
}
