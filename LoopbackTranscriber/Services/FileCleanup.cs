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
        catch (Exception ex)
        {
            AppLog.Warn($"{context}: no se pudo borrar el fichero temporal \"{path}\"", ex);
        }
    }
}
