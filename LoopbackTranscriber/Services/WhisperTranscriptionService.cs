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

    private static readonly Regex NonSpeechTagRegex =
        new(@"^[\[\(][A-Za-z_ ]+[\]\)]$", RegexOptions.Compiled);

    private const float NoSpeechProbThreshold = 0.6f;

    private const float HallucinationNoSpeechSoftThreshold = 0.3f;

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
            AppLog.Warn($"WhisperTranscriptionService.IsEffectivelySilent: could not analyze the audio in {wavPath}", ex);
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
                     ?? throw UserError.InvalidOperation("ErrorBackgroundProcessStart");

        if (!string.IsNullOrEmpty(result.Error))
        {
            throw UserError.FromWorker(result.ErrorKey, result.ErrorArgs, result.Error);
        }

        MemoryTrimmer.ReleaseInBackground();

        return result.Segments
            .Select(s => new TranscriptSegment(TimeSpan.FromTicks(s.StartTicks), TimeSpan.FromTicks(s.EndTicks), s.Text))
            .ToList();
    }

    internal static string? FilterSegmentText(string? rawText, float noSpeechProbability)
    {
        var text = rawText?.Trim();
        if (string.IsNullOrEmpty(text) || NonSpeechTagRegex.IsMatch(text))
        {
            return null;
        }

        if (noSpeechProbability >= NoSpeechProbThreshold)
        {
            return null;
        }

        if (noSpeechProbability >= HallucinationNoSpeechSoftThreshold && TranscriptFilters.IsKnownHallucinatedPhrase(text))
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
            var text = FilterSegmentText(result.Text, result.NoSpeechProbability);
            if (text is null)
            {
                continue;
            }

            segments.Add(new TranscriptSegment(result.Start, result.End, text));
        }

        return TranscriptFilters.MergeCloseSegments(TranscriptFilters.RemoveRepeatedSegments(segments));
    }
}
