namespace LoopbackTranscriber.Services;

public static class RecordingNames
{
    public const string FilePrefix = "recording_";
    public const string LegacyFilePrefix = "grabacion_";
    public const string DefaultAppName = "Meeting";

    private const string LegacyDefaultAppName = "reunion";

    public static IReadOnlyList<string> FilePrefixes { get; } = [FilePrefix, LegacyFilePrefix];

    public static bool IsDefaultAppName(string? appName) =>
        string.Equals(appName, DefaultAppName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(appName, LegacyDefaultAppName, StringComparison.OrdinalIgnoreCase);

    public static string Display(string appName) =>
        IsDefaultAppName(appName) ? Strings.Instance.Get("DefaultAppName") : appName;
}
