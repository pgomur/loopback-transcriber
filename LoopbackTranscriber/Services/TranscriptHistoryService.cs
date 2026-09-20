using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LoopbackTranscriber.Models;

namespace LoopbackTranscriber.Services;

public sealed class TranscriptHistoryService
{
    private static readonly Regex TranscriptFileNamePattern =
        new(@"^(?<app>.+)_(?<date>\d{8})_(?<time>\d{6})$", RegexOptions.Compiled);

    public List<TranscriptHistoryItem> LoadFromDisk(string transcriptsFolder)
    {
        if (!Directory.Exists(transcriptsFolder))
        {
            return new List<TranscriptHistoryItem>();
        }

        var items = new List<TranscriptHistoryItem>();

        foreach (var path in Directory.GetFiles(transcriptsFolder, "*.txt"))
        {
            var item = TryReadTranscriptFile(path);
            if (item is not null)
            {
                items.Add(item);
            }
        }

        return items.OrderByDescending(i => i.Timestamp).ToList();
    }

    private static TranscriptHistoryItem? TryReadTranscriptFile(string path)
    {
        try
        {
            var fileName = Path.GetFileNameWithoutExtension(path);
            var match = TranscriptFileNamePattern.Match(fileName);

            string appName;
            DateTime timestamp;

            if (match.Success && DateTime.TryParseExact(
                    match.Groups["date"].Value + match.Groups["time"].Value,
                    "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                appName = match.Groups["app"].Value;
                timestamp = parsed;
            }
            else
            {
                appName = fileName;
                timestamp = File.GetLastWriteTime(path);
            }

            var content = File.ReadAllText(path).Trim();
            var preview = content.Length > 160 ? content[..160].TrimEnd() + "..." : content;
            if (string.IsNullOrWhiteSpace(preview))
            {
                preview = "(Transcripcion vacia)";
            }

            return new TranscriptHistoryItem
            {
                AppName = appName,
                Timestamp = timestamp,
                Preview = preview,
                FilePath = path
            };
        }
        catch (Exception ex)
        {
            AppLog.Warn($"TranscriptHistoryService: no se pudo leer/parsear \"{path}\", se omite del historial", ex);
            return null;
        }
    }

    public bool TryDeleteTranscriptFile(TranscriptHistoryItem item, out Exception? error)
    {
        try
        {
            if (File.Exists(item.FilePath))
            {
                File.Delete(item.FilePath);
            }
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }

        TryDeleteFile(Path.ChangeExtension(item.FilePath, ".wav"));

        error = null;
        return true;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"TranscriptHistoryService: no se pudo borrar \"{path}\"", ex);
        }
    }
}
