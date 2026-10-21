namespace LoopbackTranscriber.Models;

public sealed class TranscriptMessage
{
    public string? Speaker { get; init; }

    public string Text { get; init; } = string.Empty;

    public bool IsSelf => SpeakerLabels.IsSelf(Speaker);

    public string DisplaySpeaker => LoopbackTranscriber.Services.SpeakerDisplay.Localize(Speaker);
}
