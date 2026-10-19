namespace LoopbackTranscriber.Services;

public sealed record TrayMenuEntry(
    string Text,
    bool Enabled,
    Action? Invoke,
    string? Icon = null,
    bool IsDefault = false,
    bool IsSeparator = false,
    bool IsStatus = false,
    Func<string>? LiveText = null)
{
    public const string RecordGlyph = "glyph:record";
    public const string StopGlyph = "glyph:stop";

    public static TrayMenuEntry Separator { get; } = new(string.Empty, false, null, IsSeparator: true);
}
