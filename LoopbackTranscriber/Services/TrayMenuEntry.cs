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
    public static TrayMenuEntry Separator { get; } = new(string.Empty, false, null, IsSeparator: true);
}
