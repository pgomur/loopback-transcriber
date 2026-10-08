namespace LoopbackTranscriber.Models;

public sealed class TranscriptMessage
{
    public string? Speaker { get; init; }

    public string Text { get; init; } = string.Empty;

    public bool IsSelf => Speaker == "Tu" || (Speaker?.EndsWith(" (Tú)", StringComparison.Ordinal) ?? false);

    public string DisplaySpeaker => !IsSelf ? "Participantes" : (Speaker == "Tu" ? "Tú" : Speaker!);
}
