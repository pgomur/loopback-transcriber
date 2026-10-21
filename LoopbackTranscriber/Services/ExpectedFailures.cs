using System.IO;
using System.Runtime.InteropServices;

namespace LoopbackTranscriber.Services;

internal static class ExpectedFailures
{
    public static bool IsDeviceOrFile(Exception ex) =>
        ex is COMException or InvalidComObjectException or IOException or UnauthorizedAccessException;

    public static bool IsFile(Exception ex) => ex is IOException or UnauthorizedAccessException;

    public static bool IsAudioFile(Exception ex) => IsFile(ex) || ex is FormatException or InvalidDataException;
}
