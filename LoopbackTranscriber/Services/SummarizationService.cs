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

public sealed partial class SummarizationService : ISummarizer, IDisposable
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromHours(1) };

    private const int ContextSize = 8192;
    private const int MaxTranscriptCharacters = 9000;
    private const int MaxOutputTokens = 1800;
    private const int MaxFollowUpOutputTokens = 300;
    private const int FollowUpExpectedTokens = 120;

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

    public async Task<RecordingSummary> SummarizeAsync(string transcriptText, string modelPath, string language,
        IProgress<double>? percentProgress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var job = new SummaryJob
            {
                TranscriptText = transcriptText,
                ModelPath = modelPath,
                UiLanguage = language
            };

            var resultJson = await WorkerProcess.RunAsync("summary", JsonSerializer.Serialize(job),
                    value => percentProgress?.Report(value), cancellationToken)
                .ConfigureAwait(false);

            var result = JsonSerializer.Deserialize<SummaryJobResult>(resultJson)
                         ?? throw UserError.InvalidOperation("ErrorBackgroundProcessStart");

            if (!string.IsNullOrEmpty(result.Error))
            {
                throw UserError.FromWorker(result.ErrorKey, result.ErrorArgs, result.Error);
            }

            return new RecordingSummary { Title = result.Title ?? string.Empty, Summary = result.Summary ?? string.Empty };
        }
        finally
        {
            MemoryTrimmer.ReleaseInBackground();
        }
    }

    internal Task<RecordingSummary> SummarizeInProcessAsync(string transcriptText, string modelPath, string language,
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

                var prompt = SummaryPrompts.Final(condensed, GetSummaryLanguageName(language));
                var rawOutput = await RunInferenceAsync(executor, prompt, MaxOutputTokens, FinalExpectedTokens, tracker,
                        cancellationToken)
                    .ConfigureAwait(false);

                var parsed = ParseResult(rawOutput);
                var summaryText = RemoveRepeatedLines(parsed.Summary);
                summaryText = await AddMissingSectionsAsync(executor, condensed, summaryText, language, cancellationToken)
                    .ConfigureAwait(false);

                var cleaned = new RecordingSummary
                {
                    Title = parsed.Title,
                    Summary = summaryText
                };

                var result = LocalizeSectionLabels(cleaned, language);
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

    private static string GetSummaryLanguageName(string languageCode) => languageCode switch
    {
        "en" => "ingles",
        "pt" => "portugues",
        "de" => "aleman",
        "ru" => "ruso",
        _ => "espanol"
    };

    public void Dispose()
    {
        _idleUnloadTimer.Dispose();
        _weights?.Dispose();
    }
}
