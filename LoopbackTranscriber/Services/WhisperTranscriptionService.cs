using System.Diagnostics;
using System.Linq;
using NAudio.Wave;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Whisper.net;
using Whisper.net.Ggml;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LoopbackTranscriber.Services;

public sealed class WhisperTranscriptionService
{

    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromHours(1) };

    private const string HuggingFaceRepository = "https://huggingface.co/sandrohanea/whisper.net/resolve";
    private const string ModelVersion = "v5";

    private const int DownloadBufferSize = 1024 * 1024;

    public static GgmlType ParseModelSize(string modelSize) => modelSize.Trim().ToLowerInvariant() switch
    {
        "tiny" => GgmlType.Tiny,
        "base" => GgmlType.Base,
        "small" => GgmlType.Small,
        "medium" => GgmlType.Medium,
        "large" => GgmlType.LargeV3,
        "turbo" => GgmlType.LargeV3Turbo,
        _ => GgmlType.Small
    };

    private static string GetModelFileName(GgmlType type) => type switch
    {
        GgmlType.Tiny => "ggml-tiny",
        GgmlType.Base => "ggml-base",
        GgmlType.Small => "ggml-small",
        GgmlType.Medium => "ggml-medium",
        GgmlType.LargeV3 => "ggml-large-v3",
        GgmlType.LargeV3Turbo => "ggml-large-v3-turbo",
        _ => "ggml-small"
    };

    private static string GetModelUrl(GgmlType type) =>
        $"{HuggingFaceRepository}/{ModelVersion}/q5_1/{GetModelFileName(type)}.bin";

    internal static string FormatBytes(long bytes)
    {
        const double mb = 1024d * 1024d;
        var megabytes = bytes / mb;
        return megabytes >= 1024
            ? $"{megabytes / 1024:0.0} GB"
            : $"{megabytes:0} MB";
    }

    public async Task EnsureModelDownloadedAsync(string modelSize, string destinationPath,
        IProgress<string>? progress = null, IProgress<double>? percentProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (File.Exists(destinationPath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var ggmlType = ParseModelSize(modelSize);
        var tempPath = destinationPath + ".download";

        var downloadedWithProgress = await TryDownloadWithProgressAsync(ggmlType, modelSize, tempPath, progress,
            percentProgress, cancellationToken).ConfigureAwait(false);

        if (!downloadedWithProgress)
        {
            progress?.Report(Strings.Instance.Get("ProgressDownloadingModel"));

            await using (var modelStream = await WhisperGgmlDownloader.Default
                             .GetGgmlModelAsync(ggmlType, cancellationToken: cancellationToken)
                             .ConfigureAwait(false))
            await using (var fileStream = File.Create(tempPath))
            {
                await modelStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }
        }

        File.Move(tempPath, destinationPath, overwrite: true);
        percentProgress?.Report(100);
        progress?.Report(Strings.Instance.Get("ProgressModelDownloaded"));
    }

    private static async Task<bool> TryDownloadWithProgressAsync(GgmlType type, string modelSize, string tempPath,
        IProgress<string>? progress, IProgress<double>? percentProgress, CancellationToken cancellationToken)
    {
        try
        {
            var url = GetModelUrl(type);
            var resumeFrom = File.Exists(tempPath) ? new FileInfo(tempPath).Length : 0;

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (resumeFrom > 0)
            {
                request.Headers.Range = new RangeHeaderValue(resumeFrom, null);
            }

            using var response = await HttpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            var isResuming = resumeFrom > 0 && response.StatusCode == HttpStatusCode.PartialContent;
            if (!isResuming)
            {
                resumeFrom = 0;
            }

            response.EnsureSuccessStatusCode();

            long? totalBytes = isResuming
                ? response.Content.Headers.ContentRange?.Length ?? (resumeFrom + response.Content.Headers.ContentLength)
                : response.Content.Headers.ContentLength;

            await using var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var fileStream = isResuming
                ? new FileStream(tempPath, FileMode.Append, FileAccess.Write, FileShare.None, DownloadBufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan)
                : new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, DownloadBufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

            var buffer = new byte[DownloadBufferSize];
            long totalRead = resumeFrom;
            var stopwatch = Stopwatch.StartNew();
            int read;

            while ((read = await httpStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                totalRead += read;

                if (stopwatch.ElapsedMilliseconds < 200)
                {
                    continue;
                }

                stopwatch.Restart();

                if (totalBytes is > 0)
                {
                    var percent = Math.Min(100.0, totalRead * 100.0 / totalBytes.Value);
                    percentProgress?.Report(percent);
                    progress?.Report(Strings.Instance.Format("ProgressDownloadingWithPercent",
                        $"{percent:0}", FormatBytes(totalRead), FormatBytes(totalBytes.Value)));
                }
                else
                {
                    progress?.Report(Strings.Instance.Format("ProgressDownloadingBytes", FormatBytes(totalRead)));
                }
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"WhisperTranscriptionService: fallo la descarga con progreso del modelo \"{modelSize}\", se usara el descargador de respaldo", ex);

            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception cleanupEx)
            {
                AppLog.Warn($"WhisperTranscriptionService: no se pudo borrar el fichero temporal \"{tempPath}\"", cleanupEx);
            }

            return false;
        }
    }

    public static void DiscardPartialDownload(string destinationPath)
    {
        var tempPath = destinationPath + ".download";
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"WhisperTranscriptionService: no se pudo borrar el fichero temporal \"{tempPath}\"", ex);
        }
    }

    public sealed record TranscriptSegment(TimeSpan Start, TimeSpan End, string Text);

    private static readonly Regex NonSpeechTagRegex =
        new(@"^[\[\(][A-Za-z_ ]+[\]\)]$", RegexOptions.Compiled);

    private const float NoSpeechProbThreshold = 0.6f;

    private const float HallucinationNoSpeechSoftThreshold = 0.3f;

    private static readonly HashSet<string> KnownHallucinatedPhrases = new(StringComparer.OrdinalIgnoreCase)
    {
        "thank you",
        "thank you very much",
        "thank you so much",
        "thanks for watching",
        "thank you for watching",
        "please subscribe",
        "subscribe to my channel",
        "like and subscribe",
        "bye",
        "bye bye",
        "goodbye",
        "gracias",
        "muchas gracias",
        "gracias por ver el video",
        "subtitulos realizados por la comunidad de amara.org",
        "subtitles by the amara.org community",
        "you",
    };

    private static bool IsKnownHallucinatedPhrase(string text)
    {
        var normalized = text.Trim(WordTrimChars).Trim();
        return KnownHallucinatedPhrases.Contains(normalized);
    }

    private static readonly TimeSpan MaxMergeGap = TimeSpan.FromSeconds(2);
    private const int MaxMergedSegmentLength = 400;

    private static readonly char[] WordTrimChars = ['.', ',', '!', '?', '¡', '¿', ':', ';', '"', '\''];

    private static bool IsRepeatedWordLoop(string text)
    {
        var words = text
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim(WordTrimChars).ToLowerInvariant())
            .Where(w => w.Length > 0)
            .ToList();

        if (words.Count < 4)
        {
            return false;
        }

        var mostRepeatedCount = words.GroupBy(w => w).Max(g => g.Count());
        return mostRepeatedCount >= Math.Max(4, (int)(words.Count * 0.6));
    }

    private const int MaxLoopPhraseWords = 12;

    private static bool SameWordRun(string[] keys, int a, int b, int length)
    {
        for (var k = 0; k < length; k++)
        {
            if (keys[a + k] != keys[b + k])
            {
                return false;
            }
        }

        return true;
    }

    private static string RemovePhraseLoops(string text)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 3)
        {
            return text;
        }

        var keys = words.Select(w => w.Trim(WordTrimChars).ToLowerInvariant()).ToArray();
        var kept = new List<string>(words.Length);
        var changed = false;
        var i = 0;

        while (i < words.Length)
        {
            if (keys[i].Length == 0)
            {
                kept.Add(words[i]);
                i++;
                continue;
            }

            var loopLength = 0;
            var loopRepeats = 0;
            var maxLength = Math.Min(MaxLoopPhraseWords, (words.Length - i) / 2);

            for (var length = 1; length <= maxLength; length++)
            {
                var repeats = 1;
                while (i + (repeats + 1) * length <= words.Length && SameWordRun(keys, i, i + repeats * length, length))
                {
                    repeats++;
                }

                var minRepeats = length == 1 ? 4 : 3;
                if (repeats >= minRepeats)
                {
                    loopLength = length;
                    loopRepeats = repeats;
                    break;
                }
            }

            if (loopLength == 0)
            {
                kept.Add(words[i]);
                i++;
                continue;
            }

            changed = true;
            var removeCompletely = loopLength == 1 ? loopRepeats >= 6 : loopRepeats >= 4;
            if (!removeCompletely)
            {
                for (var k = 0; k < loopLength; k++)
                {
                    kept.Add(words[i + k]);
                }
            }

            i += loopLength * loopRepeats;
        }

        return changed ? string.Join(' ', kept) : text;
    }

    private static string SegmentKey(string text) => string.Join(' ', NormalizeWords(text));

    private static List<TranscriptSegment> RemoveRepeatedSegments(List<TranscriptSegment> segments)
    {
        var result = new List<TranscriptSegment>(segments.Count);
        var i = 0;

        while (i < segments.Count)
        {
            var key = SegmentKey(segments[i].Text);
            var j = i + 1;
            while (key.Length > 0 && j < segments.Count && SegmentKey(segments[j].Text) == key)
            {
                j++;
            }

            if (j - i < 3)
            {
                for (var k = i; k < j; k++)
                {
                    result.Add(segments[k]);
                }
            }

            i = j;
        }

        return result;
    }

    public static double WordOverlapRatio(string a, string b)
    {
        var wordsA = new HashSet<string>(NormalizeWords(a));
        var wordsB = new HashSet<string>(NormalizeWords(b));
        if (wordsA.Count == 0 || wordsB.Count == 0)
        {
            return 0.0;
        }

        var intersection = wordsA.Intersect(wordsB).Count();
        var union = wordsA.Union(wordsB).Count();
        return union == 0 ? 0.0 : (double)intersection / union;
    }

    private static List<string> NormalizeWords(string text)
    {
        return text
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim(WordTrimChars).ToLowerInvariant())
            .Where(w => w.Length > 0)
            .ToList();
    }

    private static List<TranscriptSegment> MergeCloseSegments(List<TranscriptSegment> segments)
    {
        var merged = new List<TranscriptSegment>();

        foreach (var segment in segments)
        {
            if (merged.Count > 0)
            {
                var previous = merged[^1];
                var gap = segment.Start - previous.End;

                if (gap <= MaxMergeGap && previous.Text.Length < MaxMergedSegmentLength)
                {
                    merged[^1] = previous with { End = segment.End, Text = previous.Text + " " + segment.Text };
                    continue;
                }
            }

            merged.Add(segment);
        }

        return merged;
    }

    public static int GetRecommendedThreadCount(int concurrentTranscriptions = 1)
    {
        var perInstance = Environment.ProcessorCount / 2 / Math.Max(1, concurrentTranscriptions);
        return Math.Clamp(perInstance, 2, 16);
    }

    private const double SilentAudioPeakDbfsThreshold = -50.0;

    private static bool IsEffectivelySilent(string wavPath)
    {
        try
        {
            using var reader = new AudioFileReader(wavPath);
            ISampleProvider sampleProvider = reader;

            var buffer = new float[4096];
            var peak = 0f;
            int read;
            while ((read = sampleProvider.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (var i = 0; i < read; i++)
                {
                    var abs = Math.Abs(buffer[i]);
                    if (abs > peak)
                    {
                        peak = abs;
                    }
                }
            }

            if (peak <= 0f)
            {
                return true;
            }

            var peakDbfs = 20.0 * Math.Log10(peak);
            return peakDbfs <= SilentAudioPeakDbfsThreshold;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"WhisperTranscriptionService.IsEffectivelySilent: no se pudo analizar el audio en {wavPath}", ex);
            return false;
        }
    }

    public async Task<List<TranscriptSegment>> TranscribeSegmentsAsync(string wavPath, string modelPath,
        string language, int threadCount, bool useGpu = true, IProgress<int>? percentProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (IsEffectivelySilent(wavPath))
        {
            percentProgress?.Report(100);
            return new List<TranscriptSegment>();
        }

        var job = new WhisperJob
        {
            WavPath = wavPath,
            ModelPath = modelPath,
            Language = language,
            ThreadCount = threadCount,
            UseGpu = useGpu
        };

        var resultJson = await WorkerProcess.RunAsync("whisper", JsonSerializer.Serialize(job),
                value => percentProgress?.Report((int)Math.Round(value)), cancellationToken)
            .ConfigureAwait(false);

        var result = JsonSerializer.Deserialize<WhisperJobResult>(resultJson)
                     ?? throw new InvalidOperationException(Strings.Instance.Get("ErrorBackgroundProcessStart"));

        if (!string.IsNullOrEmpty(result.Error))
        {
            throw new InvalidOperationException(result.Error);
        }

        MemoryTrimmer.ReleaseInBackground();

        return result.Segments
            .Select(s => new TranscriptSegment(TimeSpan.FromTicks(s.StartTicks), TimeSpan.FromTicks(s.EndTicks), s.Text))
            .ToList();
    }

    internal async Task<List<TranscriptSegment>> TranscribeSegmentsInProcessAsync(string wavPath, string modelPath,
        string language, int threadCount, bool useGpu = true, IProgress<int>? percentProgress = null,
        CancellationToken cancellationToken = default)
    {
        using var whisperFactory = WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions { UseGpu = useGpu });

        var lastReportedPercent = -1;

        using var processor = whisperFactory.CreateBuilder()
            .WithLanguage(language)
            .WithThreads(threadCount)
            .WithNoContext()
            .WithNoSpeechThreshold(NoSpeechProbThreshold)
            .WithProbabilities()
            .WithProgressHandler(percent =>
            {
                if (percent == lastReportedPercent)
                {
                    return;
                }

                lastReportedPercent = percent;
                percentProgress?.Report(percent);
            })
            .Build();

        await using var fileStream = File.OpenRead(wavPath);

        var segments = new List<TranscriptSegment>();
        await foreach (var result in processor.ProcessAsync(fileStream, cancellationToken).ConfigureAwait(false))
        {
            var text = result.Text?.Trim();
            if (string.IsNullOrEmpty(text) || NonSpeechTagRegex.IsMatch(text))
            {
                continue;
            }

            if (result.NoSpeechProbability >= NoSpeechProbThreshold)
            {
                continue;
            }

            if (result.NoSpeechProbability >= HallucinationNoSpeechSoftThreshold && IsKnownHallucinatedPhrase(text))
            {
                continue;
            }

            text = RemovePhraseLoops(text);
            if (string.IsNullOrWhiteSpace(text) || IsRepeatedWordLoop(text))
            {
                continue;
            }

            segments.Add(new TranscriptSegment(result.Start, result.End, text));
        }

        return MergeCloseSegments(RemoveRepeatedSegments(segments));
    }
}
