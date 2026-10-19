using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using LoopbackTranscriber.Models;
using NAudio.Wave;

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

        foreach (var directory in Directory.GetDirectories(transcriptsFolder))
        {
            var item = TryReadRecordingFolder(directory);
            if (item is not null)
            {
                items.Add(item);
                continue;
            }

            foreach (var recordingDirectory in SafeGetDirectories(directory))
            {
                var nested = TryReadRecordingFolder(recordingDirectory);
                if (nested is not null)
                {
                    items.Add(nested);
                }
            }
        }

        return items.OrderByDescending(i => i.Timestamp).ToList();
    }

    public static string GetAppFolderName(string appName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(appName.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return cleaned.Length == 0 ? "_" : cleaned;
    }

    public bool HasTranscripts(string transcriptsFolder, string appName)
    {
        try
        {
            if (!Directory.Exists(transcriptsFolder))
            {
                return false;
            }

            var appFolder = Path.Combine(transcriptsFolder, GetAppFolderName(appName));
            if (Directory.Exists(appFolder) && Directory.EnumerateDirectories(appFolder).Any())
            {
                return true;
            }

            foreach (var directory in Directory.EnumerateDirectories(transcriptsFolder))
            {
                var match = TranscriptFileNamePattern.Match(Path.GetFileName(directory));
                if (match.Success &&
                    string.Equals(match.Groups["app"].Value, appName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"TranscriptHistoryService.HasTranscripts: could not check \"{appName}\"", ex);
        }

        return false;
    }

    private static string[] SafeGetDirectories(string directory)
    {
        try
        {
            return Directory.GetDirectories(directory);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"TranscriptHistoryService: could not list \"{directory}\"", ex);
            return [];
        }
    }

    private static TranscriptHistoryItem? TryReadRecordingFolder(string directory)
    {
        try
        {
            var baseName = Path.GetFileName(directory);

            var txtPath = Path.Combine(directory, baseName + ".txt");
            var mdPath = Path.Combine(directory, baseName + ".md");
            var wavPath = Path.Combine(directory, baseName + ".wav");
            var summaryPath = Path.Combine(directory, baseName + ".summary.json");

            var hasTxt = File.Exists(txtPath);
            var hasMarkdown = File.Exists(mdPath);
            var hasAudio = File.Exists(wavPath);

            if (!hasTxt && !hasMarkdown && !hasAudio)
            {
                return null;
            }

            var summary = TryReadSummary(summaryPath);
            var duration = hasAudio ? TryGetAudioDuration(wavPath) : null;

            var match = TranscriptFileNamePattern.Match(baseName);

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
                appName = baseName;
                timestamp = Directory.GetLastWriteTime(directory);
            }

            string filePath;
            string transcriptPreviewSource;

            if (hasTxt)
            {
                filePath = txtPath;
                transcriptPreviewSource = File.ReadAllText(txtPath).Trim();
            }
            else if (hasMarkdown)
            {
                filePath = mdPath;
                transcriptPreviewSource = StripMarkdownHeader(File.ReadAllText(mdPath).Trim());
            }
            else
            {
                filePath = wavPath;
                transcriptPreviewSource = string.Empty;
            }

            var preview = summary is not null && !string.IsNullOrWhiteSpace(summary.Summary)
                ? BuildPreview(summary.Summary)
                : !string.IsNullOrWhiteSpace(transcriptPreviewSource)
                    ? BuildPreview(TimestampSpeakerPrefixRegex.Replace(transcriptPreviewSource, string.Empty))
                    : string.Empty;

            return new TranscriptHistoryItem
            {
                AppName = appName,
                Timestamp = timestamp,
                Preview = preview,
                FilePath = filePath,
                HasTxt = hasTxt,
                HasMarkdown = hasMarkdown,
                HasAudio = hasAudio,
                HasSummary = summary is not null,
                SummaryTitle = summary?.Title,
                SummaryText = summary?.Summary,
                SummaryFilePath = summaryPath,
                Duration = duration
            };
        }
        catch (Exception ex)
        {
            AppLog.Warn($"TranscriptHistoryService: could not read \"{directory}\"; skipping it in the history", ex);
            return null;
        }
    }

    private static TimeSpan? TryGetAudioDuration(string wavPath)
    {
        try
        {
            using var reader = new WaveFileReader(wavPath);
            return reader.TotalTime;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"TranscriptHistoryService: could not read the duration of \"{wavPath}\"", ex);
            return null;
        }
    }

    private static readonly JsonSerializerOptions SummaryJsonOptions = new() { WriteIndented = true };

    private static RecordingSummary? TryReadSummary(string summaryPath)
    {
        if (!File.Exists(summaryPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(summaryPath);
            return JsonSerializer.Deserialize<RecordingSummary>(json);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"TranscriptHistoryService: could not read the summary \"{summaryPath}\"", ex);
            return null;
        }
    }

    public void SaveSummary(string summaryFilePath, RecordingSummary summary)
    {
        var json = JsonSerializer.Serialize(summary, SummaryJsonOptions);
        File.WriteAllText(summaryFilePath, json);
    }

    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    private static readonly Regex TimestampSpeakerPrefixRegex =
        new("^(?:" + SpeakerLabels.PrefixPattern + @")\s*:\s*", RegexOptions.Multiline | RegexOptions.Compiled);

    private static string BuildPreview(string content)
    {
        var singleLine = WhitespaceRegex.Replace(content, " ").Trim();
        var preview = singleLine.Length > 100 ? singleLine[..100].TrimEnd() + "..." : singleLine;
        return string.IsNullOrWhiteSpace(preview) ? "(Transcripcion vacia)" : preview;
    }

    private static string StripMarkdownHeader(string content)
    {
        var lines = content.Split('\n');
        if (lines.Length > 0 && lines[0].TrimStart().StartsWith("# ", StringComparison.Ordinal))
        {
            var rest = string.Join('\n', lines.Skip(1)).TrimStart('\n', '\r').Trim();
            return string.IsNullOrWhiteSpace(rest) ? content : rest;
        }

        return content;
    }

    public bool TryDeleteTranscriptFile(TranscriptHistoryItem item, out Exception? error)
    {
        try
        {
            var directory = Path.GetDirectoryName(item.FilePath);
            var baseName = Path.GetFileNameWithoutExtension(item.FilePath);

            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory) &&
                string.Equals(Path.GetFileName(directory), baseName, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(directory, recursive: true);

                var parent = Path.GetDirectoryName(directory);
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent) &&
                    string.Equals(Path.GetFileName(parent), GetAppFolderName(item.AppName), StringComparison.OrdinalIgnoreCase) &&
                    !Directory.EnumerateFileSystemEntries(parent).Any())
                {
                    Directory.Delete(parent);
                }
            }
            else
            {
                if (File.Exists(item.FilePath))
                {
                    File.Delete(item.FilePath);
                }

                TryDeleteFile(Path.ChangeExtension(item.FilePath, ".wav"));
            }
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }

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
            AppLog.Warn($"TranscriptHistoryService: could not delete \"{path}\"", ex);
        }
    }
}
