using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LLama;
using LLama.Common;
using LLama.Sampling;
using LoopbackTranscriber.Models;

namespace LoopbackTranscriber.Services;

public sealed class SummarizationService : IDisposable
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromHours(1) };

    private const string ModelDownloadUrl =
        "https://huggingface.co/Qwen/Qwen3-4B-GGUF/resolve/main/Qwen3-4B-Q4_K_M.gguf";

    private const int DownloadBufferSize = 1024 * 1024;

    private const int ContextSize = 8192;
    private const int MaxTranscriptCharacters = 9000;
    private const int MaxOutputTokens = 1400;

    private const int MaxChunkCharacters = 9000;
    private const int MaxChunkOutputTokens = 600;

    private const double LoadProgressUnits = 150;
    private const double PrefillProgressUnits = 100;
    private const int ChunkExpectedTokens = 450;
    private const int FinalExpectedTokens = 900;

    private const int LoopWindowLines = 10;
    private const int LoopRepeatedLinesLimit = 3;
    private const double NearDuplicateSimilarity = 0.72;

    private static readonly Regex WordRegex = new(@"[\p{L}\p{N}]{3,}", RegexOptions.Compiled);

    private static readonly TimeSpan IdleUnloadDelay = TimeSpan.FromMinutes(2);

    private readonly object _loadLock = new();
    private readonly Timer _idleUnloadTimer;
    private LLamaWeights? _weights;
    private StatelessExecutor? _executor;
    private string? _loadedModelPath;

    public SummarizationService()
    {
        _idleUnloadTimer = new Timer(_ => UnloadIfIdle(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public async Task EnsureModelDownloadedAsync(string destinationPath,
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

        var tempPath = destinationPath + ".download";

        try
        {
            var resumeFrom = File.Exists(tempPath) ? new FileInfo(tempPath).Length : 0;

            using var request = new HttpRequestMessage(HttpMethod.Get, ModelDownloadUrl);
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

            await using var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
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
                    progress?.Report(Strings.Instance.Format("SummaryModelDownloadingWithPercent",
                        $"{percent:0}", FormatBytes(totalRead), FormatBytes(totalBytes.Value)));
                }
                else
                {
                    progress?.Report(Strings.Instance.Format("SummaryModelDownloadingBytes", FormatBytes(totalRead)));
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }

        File.Move(tempPath, destinationPath, overwrite: true);
        percentProgress?.Report(100);
        progress?.Report(Strings.Instance.Get("SummaryModelDownloaded"));
    }

    public async Task<RecordingSummary> SummarizeAsync(string transcriptText, string modelPath,
        IProgress<double>? percentProgress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var job = new SummaryJob
            {
                TranscriptText = transcriptText,
                ModelPath = modelPath,
                UiLanguage = Strings.Instance.Language
            };

            var resultJson = await WorkerProcess.RunAsync("summary", JsonSerializer.Serialize(job),
                    value => percentProgress?.Report(value), cancellationToken)
                .ConfigureAwait(false);

            var result = JsonSerializer.Deserialize<SummaryJobResult>(resultJson)
                         ?? throw new InvalidOperationException(Strings.Instance.Get("ErrorBackgroundProcessStart"));

            if (!string.IsNullOrEmpty(result.Error))
            {
                throw new InvalidOperationException(result.Error);
            }

            return new RecordingSummary { Title = result.Title ?? string.Empty, Summary = result.Summary ?? string.Empty };
        }
        finally
        {
            MemoryTrimmer.ReleaseInBackground();
        }
    }

    internal Task<RecordingSummary> SummarizeInProcessAsync(string transcriptText, string modelPath,
        IProgress<double>? percentProgress = null, CancellationToken cancellationToken = default)
    {

        _idleUnloadTimer.Change(Timeout.Infinite, Timeout.Infinite);

        return Task.Run(async () =>
        {
            try
            {
                var chunkCount = transcriptText.Length <= MaxChunkCharacters
                    ? 0
                    : SplitIntoChunks(transcriptText, MaxChunkCharacters).Count;
                var needsLoad = !IsLoaded(modelPath);

                var tracker = new ProgressTracker(percentProgress,
                    (needsLoad ? LoadProgressUnits : 0)
                    + chunkCount * (PrefillProgressUnits + ChunkExpectedTokens)
                    + PrefillProgressUnits + FinalExpectedTokens);

                var executor = EnsureLoaded(modelPath);
                if (needsLoad)
                {
                    tracker.Advance(LoadProgressUnits);
                }

                var condensed = await CondenseIfNeededAsync(executor, transcriptText, tracker, cancellationToken)
                    .ConfigureAwait(false);

                var prompt = SummaryPrompts.Final(condensed, GetSummaryLanguageName(Strings.Instance.Language));
                var rawOutput = await RunInferenceAsync(executor, prompt, MaxOutputTokens, FinalExpectedTokens, tracker,
                        cancellationToken)
                    .ConfigureAwait(false);

                var parsed = ParseResult(rawOutput);
                var cleaned = new RecordingSummary
                {
                    Title = parsed.Title,
                    Summary = RemoveRepeatedLines(parsed.Summary)
                };

                var result = LocalizeSectionLabels(cleaned, Strings.Instance.Language);
                tracker.Complete();
                return result;
            }
            finally
            {
                _idleUnloadTimer.Change(IdleUnloadDelay, Timeout.InfiniteTimeSpan);
                MemoryTrimmer.ReleaseInBackground();
            }
        }, cancellationToken);
    }

    private bool IsLoaded(string modelPath)
    {
        lock (_loadLock)
        {
            return _executor is not null &&
                   string.Equals(_loadedModelPath, modelPath, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class ProgressTracker
    {
        private readonly IProgress<double>? _progress;
        private readonly double _total;
        private double _done;
        private int _lastReportedPercent = -1;

        public ProgressTracker(IProgress<double>? progress, double total)
        {
            _progress = progress;
            _total = Math.Max(1, total);
            _progress?.Report(0);
        }

        public void Advance(double units)
        {
            if (units <= 0)
            {
                return;
            }

            _done = Math.Min(_done + units, _total);

            var percent = (int)Math.Min(99, Math.Floor(_done * 100.0 / _total));
            if (percent != _lastReportedPercent)
            {
                _lastReportedPercent = percent;
                _progress?.Report(percent);
            }
        }

        public void Complete() => _progress?.Report(100);
    }

    private static async Task<string> RunInferenceAsync(StatelessExecutor executor, string prompt, int maxTokens,
        int expectedTokens, ProgressTracker tracker, CancellationToken cancellationToken)
    {
        var firstAttempt = await GenerateAsync(executor, prompt, maxTokens, expectedTokens, tracker,
                strongPenalties: false, detectLoops: true, cancellationToken)
            .ConfigureAwait(false);

        if (!firstAttempt.LoopDetected)
        {
            return firstAttempt.Text;
        }

        Debug.WriteLine("[LoopbackTranscriber] SummarizationService: bucle de repeticion detectado, se reintenta con penalizaciones mas fuertes");

        var secondAttempt = await GenerateAsync(executor, prompt, maxTokens, expectedTokens, tracker: null,
                strongPenalties: true, detectLoops: false, cancellationToken)
            .ConfigureAwait(false);

        return secondAttempt.Text;
    }

    private static async Task<(string Text, bool LoopDetected)> GenerateAsync(StatelessExecutor executor,
        string prompt, int maxTokens, int expectedTokens, ProgressTracker? tracker, bool strongPenalties,
        bool detectLoops, CancellationToken cancellationToken)
    {
        var inferenceParams = new InferenceParams
        {
            MaxTokens = maxTokens,
            AntiPrompts = ["<|im_end|>", "</s>"],
            SamplingPipeline = new DefaultSamplingPipeline
            {
                Temperature = strongPenalties ? 0.8f : 0.6f,
                TopK = 30,
                TopP = 0.9f,
                RepeatPenalty = strongPenalties ? 1.25f : 1.12f,
                PenaltyCount = 512,
                FrequencyPenalty = strongPenalties ? 0.6f : 0.3f,
                PresencePenalty = strongPenalties ? 0.5f : 0.3f
            }
        };

        var builder = new StringBuilder();
        var phaseTotal = PrefillProgressUnits + expectedTokens;
        var phaseDone = 0.0;
        var tokenCount = 0;
        var firstToken = true;

        var recentLines = new List<HashSet<string>>();
        var lineStart = 0;
        var repeatedLines = 0;
        var loopDetected = false;

        await foreach (var token in executor.InferAsync(prompt, inferenceParams, cancellationToken)
                           .ConfigureAwait(false))
        {
            builder.Append(token);

            if (firstToken)
            {
                firstToken = false;
                tracker?.Advance(PrefillProgressUnits);
                phaseDone += PrefillProgressUnits;
            }

            if (tokenCount < expectedTokens * 0.97)
            {
                tokenCount++;
                tracker?.Advance(1);
                phaseDone += 1;
            }

            if (!detectLoops || !token.Contains('\n'))
            {
                continue;
            }

            var text = builder.ToString();
            int newline;
            while ((newline = text.IndexOf('\n', lineStart)) >= 0)
            {
                var words = WordSet(text[lineStart..newline]);
                lineStart = newline + 1;

                if (words.Count < 5)
                {
                    continue;
                }

                if (recentLines.Any(previous => IsNearDuplicate(previous, words)))
                {
                    repeatedLines++;
                }
                else
                {
                    recentLines.Add(words);
                    if (recentLines.Count > LoopWindowLines)
                    {
                        recentLines.RemoveAt(0);
                    }
                }
            }

            if (repeatedLines >= LoopRepeatedLinesLimit)
            {
                loopDetected = true;
                break;
            }
        }

        tracker?.Advance(phaseTotal - phaseDone);

        return (builder.ToString(), loopDetected);
    }

    private static HashSet<string> WordSet(string line) =>
        WordRegex.Matches(line.ToLowerInvariant()).Select(match => match.Value).ToHashSet();

    private static bool IsNearDuplicate(HashSet<string> first, HashSet<string> second)
    {
        if (first.Count < 4 || second.Count < 4)
        {
            return first.SetEquals(second);
        }

        var shared = first.Count(second.Contains);
        var union = first.Count + second.Count - shared;

        return shared / (double)union >= NearDuplicateSimilarity;
    }

    private static string RemoveRepeatedLines(string text)
    {
        var kept = new List<string>();
        var seen = new List<HashSet<string>>();

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.TrimStart();

            if (SectionLabelRegex.IsMatch(line) || trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                seen.Clear();
                kept.Add(line);
                continue;
            }

            var words = WordSet(trimmed);
            if (words.Count >= 4)
            {
                if (seen.Any(previous => IsNearDuplicate(previous, words)))
                {
                    continue;
                }

                seen.Add(words);
            }

            kept.Add(line);
        }

        return string.Join('\n', kept);
    }

    private static async Task<string> CondenseIfNeededAsync(StatelessExecutor executor, string transcriptText,
        ProgressTracker tracker, CancellationToken cancellationToken)
    {
        if (transcriptText.Length <= MaxChunkCharacters)
        {
            return transcriptText;
        }

        var chunks = SplitIntoChunks(transcriptText, MaxChunkCharacters);
        var keyPointsBuilder = new StringBuilder();

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunkPrompt = SummaryPrompts.Chunk(chunks[i], i + 1, chunks.Count);
            var chunkOutput = await RunInferenceAsync(executor, chunkPrompt, MaxChunkOutputTokens,
                    ChunkExpectedTokens, tracker, cancellationToken)
                .ConfigureAwait(false);

            keyPointsBuilder.AppendLine($"Puntos clave (parte {i + 1} de {chunks.Count}):");
            keyPointsBuilder.AppendLine(RemoveRepeatedLines(StripThinkBlock(chunkOutput).Trim()));
            keyPointsBuilder.AppendLine();
        }

        var combined = keyPointsBuilder.ToString();

        return combined.Length > MaxTranscriptCharacters
            ? combined[..MaxTranscriptCharacters] + "..."
            : combined;
    }

    private static List<string> SplitIntoChunks(string text, int maxChunkCharacters)
    {
        var chunks = new List<string>();
        var start = 0;

        while (start < text.Length)
        {
            var remaining = text.Length - start;
            if (remaining <= maxChunkCharacters)
            {
                chunks.Add(text[start..]);
                break;
            }

            var cutAt = start + maxChunkCharacters;

            var lastBreak = text.LastIndexOfAny(['\n', '.'], cutAt - 1, maxChunkCharacters);
            var effectiveCut = lastBreak > start ? lastBreak + 1 : cutAt;

            chunks.Add(text[start..effectiveCut]);
            start = effectiveCut;
        }

        return chunks;
    }

    private static string StripThinkBlock(string rawOutput)
    {
        var thinkEnd = rawOutput.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);
        return thinkEnd >= 0 ? rawOutput[(thinkEnd + "</think>".Length)..] : rawOutput;
    }

    private void UnloadIfIdle()
    {
        lock (_loadLock)
        {
            if (_weights is null)
            {
                return;
            }

            Debug.WriteLine("[LoopbackTranscriber] SummarizationService: modelo de resumen descargado de la RAM por inactividad");

            DisposeExecutorContext(_executor);
            _weights.Dispose();
            _weights = null;
            _executor = null;
            _loadedModelPath = null;
        }

        MemoryTrimmer.Release();
    }

    private static void DisposeExecutorContext(StatelessExecutor? executor)
    {
        if (executor is null)
        {
            return;
        }

        try
        {
            (executor.GetType().GetProperty("Context")?.GetValue(executor) as IDisposable)?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Warn("SummarizationService.DisposeExecutorContext: no se pudo liberar el contexto del modelo", ex);
        }
    }

    private StatelessExecutor EnsureLoaded(string modelPath)
    {
        lock (_loadLock)
        {
            if (_executor is not null && string.Equals(_loadedModelPath, modelPath, StringComparison.OrdinalIgnoreCase))
            {
                return _executor;
            }

            DisposeExecutorContext(_executor);
            _weights?.Dispose();

            var threadCount = Math.Clamp(Environment.ProcessorCount / 2, 2, 6);

            const int offloadAllLayersToGpu = 999;

            var parameters = new ModelParams(modelPath)
            {
                ContextSize = ContextSize,
                GpuLayerCount = offloadAllLayersToGpu,
                Threads = threadCount
            };

            _weights = LLamaWeights.LoadFromFile(parameters);
            _executor = new StatelessExecutor(_weights, parameters);
            _loadedModelPath = modelPath;

            return _executor;
        }
    }

    private static string GetSummaryLanguageName(string languageCode) => languageCode switch
    {
        "en" => "ingles",
        "pt" => "portugues",
        "de" => "aleman",
        "ru" => "ruso",
        _ => "espanol"
    };

    private static readonly Regex SectionLabelRegex = new(
        @"^(?<lead>[ \t]*(?:[-*][ \t]+)?)(?<label>Prop[oó]sito|Puntos clave|Temas|Objetivos|Pr[oó]ximos pasos|Tareas)[ \t]*:",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static RecordingSummary LocalizeSectionLabels(RecordingSummary summary, string languageCode)
    {
        if (languageCode == "es")
        {
            return summary;
        }

        var localized = SectionLabelRegex.Replace(summary.Summary, match =>
        {
            var key = match.Groups["label"].Value.ToLowerInvariant() switch
            {
                "proposito" or "propósito" => "SummaryLabelPurpose",
                "puntos clave" => "SummaryLabelKeyPoints",
                "temas" => "SummaryLabelTopics",
                "objetivos" => "SummaryLabelGoals",
                "proximos pasos" or "próximos pasos" => "SummaryLabelNextSteps",
                _ => "SummaryLabelTasks"
            };

            return match.Groups["lead"].Value + Strings.Instance.Get(key) + ":";
        });

        return new RecordingSummary { Title = summary.Title, Summary = localized };
    }

    private static readonly Regex TituloLabelRegex =
        new(@"\**\s*T[IÍ]TULO\s*\**\s*:\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ResumenLabelRegex =
        new(@"\**\s*RES[UÚ]MEN\s*\**\s*:\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static RecordingSummary ParseResult(string rawOutput)
    {
        rawOutput = StripThinkBlock(rawOutput).Trim();

        var plainFormat = TryParsePlainFormat(rawOutput);
        if (plainFormat is not null)
        {
            return plainFormat;
        }

        var jsonResult = TryParseJsonFormat(rawOutput);
        if (jsonResult is not null)
        {
            return jsonResult;
        }

        var fallbackTitle = rawOutput.Split('\n', 2)[0].Trim();
        if (fallbackTitle.Length > 60)
        {
            fallbackTitle = fallbackTitle[..60].TrimEnd() + "...";
        }

        return new RecordingSummary
        {
            Title = string.IsNullOrWhiteSpace(fallbackTitle) ? "Resumen" : fallbackTitle,
            Summary = string.IsNullOrWhiteSpace(rawOutput) ? "Resumen" : rawOutput
        };
    }

    private static RecordingSummary? TryParsePlainFormat(string text)
    {
        var resumenMatch = ResumenLabelRegex.Match(text);
        if (!resumenMatch.Success)
        {
            return null;
        }

        var beforeResumen = text[..resumenMatch.Index];
        var summary = text[(resumenMatch.Index + resumenMatch.Length)..].Trim();

        var tituloMatch = TituloLabelRegex.Match(beforeResumen);
        var title = tituloMatch.Success
            ? beforeResumen[(tituloMatch.Index + tituloMatch.Length)..]
            : beforeResumen;

        var titleFirstLine = title
            .Split('\n')
            .Select(line => line.Trim().Trim('*').Trim())
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));

        if (string.IsNullOrWhiteSpace(titleFirstLine) || string.IsNullOrWhiteSpace(summary))
        {
            return null;
        }

        return new RecordingSummary { Title = titleFirstLine, Summary = summary };
    }

    private static RecordingSummary? TryParseJsonFormat(string rawOutput)
    {
        var jsonCandidate = ExtractFirstJsonObject(rawOutput);
        if (jsonCandidate is null)
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<RawSummaryResult>(jsonCandidate,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return parsed is { Title.Length: > 0, Summary.Length: > 0 }
                ? new RecordingSummary { Title = parsed.Title.Trim(), Summary = parsed.Summary.Trim() }
                : null;
        }
        catch (Exception ex)
        {
            AppLog.Warn("SummarizationService.ParseResult: la respuesta tampoco era JSON valido, se usa el texto en bruto", ex);
            return null;
        }
    }

    private static string? ExtractFirstJsonObject(string text)
    {
        var start = text.IndexOf('{');

        while (start >= 0)
        {
            var end = FindMatchingBrace(text, start);
            if (end > start)
            {
                return text[start..(end + 1)];
            }

            start = text.IndexOf('{', start + 1);
        }

        return null;
    }

    private static int FindMatchingBrace(string text, int startIndex)
    {
        var depth = 0;
        var inString = false;

        for (var i = startIndex; i < text.Length; i++)
        {
            var c = text[i];

            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (c == '"')
            {
                inString = true;
            }
            else if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private sealed class RawSummaryResult
    {
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
    }

    internal static string FormatBytes(long bytes)
    {
        const double mb = 1024d * 1024d;
        var megabytes = bytes / mb;
        return megabytes >= 1024
            ? $"{megabytes / 1024:0.0} GB"
            : $"{megabytes:0} MB";
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
            AppLog.Warn($"SummarizationService: no se pudo borrar el fichero temporal \"{path}\"", ex);
        }
    }

    public static void DiscardPartialDownload(string destinationPath)
    {
        TryDeleteFile(destinationPath + ".download");
    }

    public void Dispose()
    {
        _idleUnloadTimer.Dispose();
        _weights?.Dispose();
    }
}
