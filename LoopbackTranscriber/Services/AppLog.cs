using System.Diagnostics;

namespace LoopbackTranscriber.Services;

internal static class AppLog
{
    [Conditional("DEBUG")]
    public static void Warn(string context, Exception ex)
    {
        Debug.WriteLine($"[LoopbackTranscriber] {context}: {ex}");
    }
}
