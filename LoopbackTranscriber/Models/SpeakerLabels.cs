using System.Text.RegularExpressions;

namespace LoopbackTranscriber.Models;

public static class SpeakerLabels
{
    public const string Others = "Participantes";

    public const string SelfAnonymous = "Tu";

    public const string SelfNamedSuffix = " (Tú)";

    public static readonly string PrefixPattern =
        Regex.Escape(SelfAnonymous) + "|" + Regex.Escape(Others) + @"|[^\r\n:]+?" + Regex.Escape(SelfNamedSuffix);

    public static string ForSelf(string? userName)
    {
        var cleaned = string.Join(' ', (userName ?? string.Empty)
            .Replace(':', ' ')
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return cleaned.Length == 0 ? SelfAnonymous : cleaned + SelfNamedSuffix;
    }

    public static bool IsSelf(string? speaker) =>
        speaker == SelfAnonymous || (speaker?.EndsWith(SelfNamedSuffix, StringComparison.Ordinal) ?? false);

    public static string? SelfName(string? speaker) =>
        speaker is not null && speaker != SelfAnonymous && speaker.EndsWith(SelfNamedSuffix, StringComparison.Ordinal)
            ? speaker[..^SelfNamedSuffix.Length]
            : null;

    public static string ApplyToPrompt(string template) => template
        .Replace("{{SELF_NAMED}}", "Nombre" + SelfNamedSuffix)
        .Replace("{{SELF}}", SelfAnonymous)
        .Replace("{{OTHERS}}", Others);
}
