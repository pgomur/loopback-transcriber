using System.IO;

namespace LoopbackTranscriber.Services;

internal static class FileCleanup
{
    public static void TryDelete(string? path, string context)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ExpectedFailures.IsFile(ex))
        {
            AppLog.Warn($"{context}: could not delete \"{path}\"", ex);
        }
    }

    public static void TryDeleteFolder(string? folder, string context)
    {
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ExpectedFailures.IsFile(ex))
        {
            AppLog.Warn($"{context}: could not delete \"{folder}\"", ex);
        }
    }
}
