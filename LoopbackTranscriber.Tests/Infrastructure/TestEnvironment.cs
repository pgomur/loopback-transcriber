using System.Globalization;
using System.Runtime.CompilerServices;

namespace LoopbackTranscriber.Tests.Infrastructure;

internal static class TestEnvironment
{
    public static string Root { get; } = Path.Combine(
        Path.GetTempPath(), "LoopbackTranscriber.Tests", Environment.ProcessId.ToString(CultureInfo.InvariantCulture));

    [ModuleInitializer]
    internal static void Initialize()
    {
        Directory.CreateDirectory(Root);

        AppLog.LogPath = Path.Combine(Root, "logs", "app.log");

        Strings.Instance.SetLanguage("es");
    }
}
