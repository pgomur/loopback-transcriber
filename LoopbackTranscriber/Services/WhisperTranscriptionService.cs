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

public sealed partial class WhisperTranscriptionService : IWhisperTranscriber
{

    public sealed record TranscriptSegment(TimeSpan Start, TimeSpan End, string Text);

    public const string AutoLanguage = "auto";

    private static readonly Regex NonSpeechTagRegex =
        new(@"^[\[\(][A-Za-z_ ]+[\]\)]$", RegexOptions.Compiled);

    private const float NoSpeechProbThreshold = 0.6f;

    private const float HallucinationNoSpeechSoftThreshold = 0.3f;

    private const float StrictNoSpeechProbThreshold = 0.45f;

    private const float StrictHallucinationNoSpeechSoftThreshold = 0.2f;

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

            var buffer = new float[4096];
            var peak = 0f;
            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
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
        catch (Exception ex) when (ExpectedFailures.IsAudioFile(ex))
        {
            AppLog.Warn($"WhisperTranscriptionService.IsEffectivelySilent: could not analyze the audio in {wavPath}", ex);
            return false;
        }
    }

    public async Task<TrackTranscription> TranscribeAsync(TranscriptionRequest request,
        IProgress<int>? percentProgress = null, CancellationToken cancellationToken = default)
    {
        if (IsEffectivelySilent(request.WavPath))
        {
            percentProgress?.Report(100);
            return new TrackTranscription(new List<TranscriptSegment>(), null);
        }

        var job = new WhisperJob
        {
            WavPath = request.WavPath,
            ModelPath = request.ModelPath,
            Language = request.Language,
            FallbackLanguage = request.FallbackLanguage,
            ThreadCount = request.ThreadCount,
            StrictNoSpeech = request.StrictNoSpeech,
            UseGpu = request.UseGpu
        };

        var resultJson = await WorkerProcess.RunAsync("whisper", JsonSerializer.Serialize(job),
                value => percentProgress?.Report((int)Math.Round(value)), cancellationToken)
            .ConfigureAwait(false);

        var result = JsonSerializer.Deserialize<WhisperJobResult>(resultJson)
                     ?? throw UserError.InvalidOperation("ErrorBackgroundProcessStart");

        if (!string.IsNullOrEmpty(result.Error))
        {
            throw UserError.FromWorker(result.ErrorKey, result.ErrorArgs, result.Error);
        }

        MemoryTrimmer.ReleaseInBackground();

        var segments = result.Segments
            .Select(s => new TranscriptSegment(TimeSpan.FromTicks(s.StartTicks), TimeSpan.FromTicks(s.EndTicks), s.Text))
            .ToList();

        return new TrackTranscription(segments, string.IsNullOrWhiteSpace(result.Language) ? null : result.Language);
    }

    internal static string? FilterSegmentText(string? rawText, float noSpeechProbability, string? language = null,
        bool strictNoSpeech = false)
    {
        var text = rawText?.Trim();
        if (string.IsNullOrEmpty(text) || NonSpeechTagRegex.IsMatch(text) || TranscriptFilters.HasNoWords(text))
        {
            return null;
        }

        var hardThreshold = strictNoSpeech ? StrictNoSpeechProbThreshold : NoSpeechProbThreshold;
        if (noSpeechProbability >= hardThreshold)
        {
            return null;
        }

        var softThreshold = strictNoSpeech ? StrictHallucinationNoSpeechSoftThreshold : HallucinationNoSpeechSoftThreshold;
        if (noSpeechProbability >= softThreshold && TranscriptFilters.IsKnownHallucinatedPhrase(text))
        {
            return null;
        }

        if (TranscriptFilters.IsForeignScript(text, language))
        {
            return null;
        }

        text = TranscriptFilters.RemovePhraseLoops(text);
        if (string.IsNullOrWhiteSpace(text) || TranscriptFilters.IsRepeatedWordLoop(text))
        {
            return null;
        }

        return text;
    }

    internal static WhisperFactory LoadFactory(string modelPath, WhisperFactoryOptions options)
    {
        try
        {
            return WhisperFactory.FromPath(modelPath, options);
        }
        catch (WhisperModelLoadException ex)
        {
            throw ModelIntegrity.LoadFailed(ex, modelPath);
        }
    }

    internal static async Task<TrackTranscription> TranscribeInProcessAsync(WhisperJob job, IProgress<int>? percentProgress = null,
        CancellationToken cancellationToken = default)
    {
        using var whisperFactory = LoadFactory(job.ModelPath, new WhisperFactoryOptions { UseGpu = job.UseGpu });

        var lastReportedPercent = -1;
        var requestedLanguage = string.IsNullOrWhiteSpace(job.Language) ? AutoLanguage : job.Language;

        using var processor = whisperFactory.CreateBuilder()
            .WithLanguage(requestedLanguage)
            .WithThreads(job.ThreadCount)
            .WithNoContext()
            .WithNoSpeechThreshold(job.StrictNoSpeech ? StrictNoSpeechProbThreshold : NoSpeechProbThreshold)
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

        string? effectiveLanguage = requestedLanguage;
        if (string.Equals(requestedLanguage, AutoLanguage, StringComparison.OrdinalIgnoreCase))
        {
            effectiveLanguage = ResolveAutoLanguage(processor, job.WavPath, job.FallbackLanguage);
            processor.ChangeLanguage(effectiveLanguage ?? AutoLanguage);
        }

        var filterLanguage = string.Equals(effectiveLanguage, AutoLanguage, StringComparison.OrdinalIgnoreCase)
            ? null
            : effectiveLanguage;

        await using var fileStream = File.OpenRead(job.WavPath);

        var segments = new List<TranscriptSegment>();
        await foreach (var result in processor.ProcessAsync(fileStream, cancellationToken).ConfigureAwait(false))
        {
            var text = FilterSegmentText(result.Text, result.NoSpeechProbability, filterLanguage, job.StrictNoSpeech);
            if (text is null)
            {
                continue;
            }

            segments.Add(new TranscriptSegment(result.Start, result.End, text));
        }

        return new TrackTranscription(
            TranscriptFilters.MergeCloseSegments(TranscriptFilters.RemoveRepeatedSegments(segments)),
            filterLanguage);
    }

    private static string? ResolveAutoLanguage(WhisperProcessor processor, string wavPath, string? fallbackLanguage)
    {
        var fallback = string.IsNullOrWhiteSpace(fallbackLanguage) ? null : fallbackLanguage;

        try
        {
            var (language, confidence, windows) = LanguageDetector.Detect(processor, wavPath);
            if (language is not null && confidence >= LanguageDetector.MinConfidence)
            {
                AppLog.Info($"WhisperTranscriptionService: detected language \"{language}\" (confidence {confidence:0.00} over {windows} window(s)) in \"{Path.GetFileName(wavPath)}\"");
                return language;
            }

            AppLog.Info($"WhisperTranscriptionService: language detection inconclusive (\"{language ?? "-"}\", confidence {confidence:0.00} over {windows} window(s)) in \"{Path.GetFileName(wavPath)}\"; using \"{fallback ?? AutoLanguage}\"");
        }
        catch (Exception ex) when (ExpectedFailures.IsAudioFile(ex))
        {
            AppLog.Warn($"WhisperTranscriptionService: language detection failed in \"{Path.GetFileName(wavPath)}\"; using \"{fallback ?? AutoLanguage}\"", ex);
        }

        return fallback;
    }
}
