using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using LLama;
using LLama.Common;
using LoopbackTranscriber.Models;

namespace LoopbackTranscriber.Services;

public sealed class SummarizationService : IDisposable
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromHours(1) };

    private const string ModelDownloadUrl =
        "https://huggingface.co/Qwen/Qwen3-4B-GGUF/resolve/main/Qwen3-4B-Q4_K_M.gguf";

    private const int ContextSize = 6144;
    private const int MaxTranscriptCharacters = 12000;
    private const int MaxOutputTokens = 700;

    private const int MaxChunkCharacters = 9000;
    private const int MaxChunkOutputTokens = 350;

    private static readonly TimeSpan IdleUnloadDelay = TimeSpan.FromMinutes(5);

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
            using var response = await HttpClient
                .GetAsync(ModelDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;

            await using var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var fileStream = File.Create(tempPath);

            var buffer = new byte[81920];
            long totalRead = 0;
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
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }

        File.Move(tempPath, destinationPath, overwrite: true);
        percentProgress?.Report(100);
        progress?.Report(Strings.Instance.Get("SummaryModelDownloaded"));
    }

    public Task<RecordingSummary> SummarizeAsync(string transcriptText, string modelPath,
        CancellationToken cancellationToken = default)
    {

        _idleUnloadTimer.Change(Timeout.Infinite, Timeout.Infinite);

        return Task.Run(async () =>
        {
            try
            {
                var executor = EnsureLoaded(modelPath);

                var condensed = await CondenseIfNeededAsync(executor, transcriptText, cancellationToken)
                    .ConfigureAwait(false);

                var prompt = BuildPrompt(condensed);
                var rawOutput = await RunInferenceAsync(executor, prompt, MaxOutputTokens, cancellationToken)
                    .ConfigureAwait(false);

                return ParseResult(rawOutput);
            }
            finally
            {
                _idleUnloadTimer.Change(IdleUnloadDelay, Timeout.InfiniteTimeSpan);
            }
        }, cancellationToken);
    }

    private static async Task<string> RunInferenceAsync(StatelessExecutor executor, string prompt, int maxTokens,
        CancellationToken cancellationToken)
    {
        var inferenceParams = new InferenceParams
        {
            MaxTokens = maxTokens,
            AntiPrompts = ["<|im_end|>", "</s>"]
        };

        var builder = new StringBuilder();

        await foreach (var token in executor.InferAsync(prompt, inferenceParams, cancellationToken)
                           .ConfigureAwait(false))
        {
            builder.Append(token);
        }

        return builder.ToString();
    }

    private static async Task<string> CondenseIfNeededAsync(StatelessExecutor executor, string transcriptText,
        CancellationToken cancellationToken)
    {
        if (transcriptText.Length <= MaxChunkCharacters)
        {
            return transcriptText;
        }

        var chunks = SplitIntoChunks(transcriptText, MaxChunkCharacters);
        var keyPointsBuilder = new StringBuilder();

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunkPrompt = BuildChunkPrompt(chunks[i], i + 1, chunks.Count);
            var chunkOutput = await RunInferenceAsync(executor, chunkPrompt, MaxChunkOutputTokens, cancellationToken)
                .ConfigureAwait(false);

            keyPointsBuilder.AppendLine($"Puntos clave (parte {i + 1} de {chunks.Count}):");
            keyPointsBuilder.AppendLine(StripThinkBlock(chunkOutput).Trim());
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

    private static string BuildChunkPrompt(string chunkText, int partNumber, int totalParts)
    {
        return "<|im_start|>system\n" +
               "Eres un asistente que extrae los puntos clave de fragmentos de una transcripcion larga. " +
               "Se te da UNA parte de la transcripcion completa (parte " + partNumber + " de " + totalParts + "). " +
               "Responde UNICAMENTE con una lista breve de los puntos, datos y decisiones mas importantes de ESTA parte, " +
               "en frases cortas, sin titulo, sin introduccion y sin markdown. " +
               "Responde en el mismo idioma en el que esta escrito el fragmento. /no_think<|im_end|>\n" +
               "<|im_start|>user\n" +
               "Fragmento:\n" + chunkText + "\n\n/no_think<|im_end|>\n" +
               "<|im_start|>assistant\n";
    }
.
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

            _weights.Dispose();
            _weights = null;
            _executor = null;
            _loadedModelPath = null;
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

            _weights?.Dispose();

            var threadCount = Math.Clamp(Environment.ProcessorCount / 2, 2, 6);

            var parameters = new ModelParams(modelPath)
            {
                ContextSize = ContextSize,
                GpuLayerCount = 0,
                Threads = threadCount
            };

            _weights = LLamaWeights.LoadFromFile(parameters);
            _executor = new StatelessExecutor(_weights, parameters);
            _loadedModelPath = modelPath;

            return _executor;
        }
    }

    private static string BuildPrompt(string transcript)
    {

        return "<|im_start|>system\n" +
               "Eres un asistente que resume transcripciones de reuniones o conversaciones grabadas. " +
               "Responde UNICAMENTE con un objeto JSON valido con las claves \"title\" y \"summary\", sin texto adicional y sin markdown. " +
               "\"title\" debe ser un titulo corto (maximo 8 palabras) que describa el contenido. " +
               "\"summary\" debe ser un resumen de 3 a 6 frases con los puntos mas importantes. " +
               "Responde siempre en el mismo idioma en el que esta escrita la transcripcion. /no_think<|im_end|>\n" +
               "<|im_start|>user\n" +
               "Transcripcion:\n" + transcript + "\n\n/no_think<|im_end|>\n" +
               "<|im_start|>assistant\n";
    }

    private static RecordingSummary ParseResult(string rawOutput)
    {

        rawOutput = StripThinkBlock(rawOutput);

        var jsonStart = rawOutput.IndexOf('{');
        var jsonEnd = rawOutput.LastIndexOf('}');

        if (jsonStart >= 0 && jsonEnd > jsonStart)
        {
            var jsonCandidate = rawOutput[jsonStart..(jsonEnd + 1)];

            try
            {
                var parsed = JsonSerializer.Deserialize<RawSummaryResult>(jsonCandidate,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (parsed is { Title.Length: > 0, Summary.Length: > 0 })
                {
                    return new RecordingSummary { Title = parsed.Title.Trim(), Summary = parsed.Summary.Trim() };
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("SummarizationService.ParseResult: la respuesta del modelo no era JSON valido, se usa el texto en bruto", ex);
            }
        }

        var fallbackTitle = rawOutput.Trim();
        if (fallbackTitle.Length > 60)
        {
            fallbackTitle = fallbackTitle[..60].TrimEnd() + "...";
        }

        return new RecordingSummary
        {
            Title = string.IsNullOrWhiteSpace(fallbackTitle) ? "Resumen" : fallbackTitle,
            Summary = rawOutput.Trim()
        };
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

    public void Dispose()
    {
        _idleUnloadTimer.Dispose();
        _weights?.Dispose();
    }
}
