using System.IO;

namespace LoopbackTranscriber.Services;

internal sealed record LiveChunkMetrics(
    double AudioSeconds,
    double WhisperSeconds,
    double LatencySeconds,
    int Backlog,
    int Dropped,
    int TextChars,
    bool IsDraft = false,
    bool Reused = false)
{
    public double RealTimeFactor => AudioSeconds <= 0 ? 0 : WhisperSeconds / AudioSeconds;
}

internal sealed record LiveTranslationMetrics(int Chars, double Seconds, double LatencySeconds, int Backlog, bool IsDraft = false);

internal static class LiveSessionSummary
{
    public static void Write(LiveWhisperConfig config, DateTime startedAtUtc, LiveChunkMetrics[] all, int chunksDropped,
        LiveTranslationMetrics[] translations, int translationsDropped)
    {
        var finals = all.Where(m => !m.IsDraft).ToArray();
        var drafts = all.Where(m => m.IsDraft).ToArray();
        if (all.Length == 0)
        {
            LiveMetricsLog.Write($"session with no transcribed chunks ({(DateTime.UtcNow - startedAtUtc).TotalSeconds:0} s)");
            return;
        }

        var model = Path.GetFileName(config.ModelPath);

        var reused = finals.Count(m => m.Reused);
        var line = FormattableString.Invariant(
            $"SUMMARY model={model} gpu={config.UseGpu} ctx={config.AudioContext} flash={config.FlashAttention} ");
        line += FormattableString.Invariant($"dropped={chunksDropped} reused={reused}");
        line += Describe("chunks", finals);
        line += Describe("drafts", drafts);
        line += DescribeTranslations(translations, translationsDropped);
        LiveMetricsLog.Write(line);
    }

    private static string DescribeTranslations(LiveTranslationMetrics[] items, int translationsDropped) =>
        DescribeTranslationSeries("translations", items.Where(m => !m.IsDraft).ToArray())
        + DescribeTranslationSeries("draft_translations", items.Where(m => m.IsDraft).ToArray())
        + (items.Length > 0
            ? FormattableString.Invariant($" dropped={translationsDropped}")
            : string.Empty);

    private static string DescribeTranslationSeries(string name, LiveTranslationMetrics[] items)
    {
        if (items.Length == 0)
        {
            return string.Empty;
        }

        var model = Series(items.Select(m => m.Seconds));
        var latency = Series(items.Select(m => m.LatencySeconds));

        return FormattableString.Invariant(
            $" | {name}={items.Length} model_p50={model.P50:0.0}s p90={model.P90:0.0}s latency_p50={latency.P50:0.0}s p90={latency.P90:0.0}s");
    }

    private static (double P50, double P90) Series(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        return (sorted[sorted.Length / 2], sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.9))]);
    }

    private static string Describe(string name, LiveChunkMetrics[] items)
    {
        if (items.Length == 0)
        {
            return string.Empty;
        }

        var audio = items.Sum(m => m.AudioSeconds);
        var whisper = items.Sum(m => m.WhisperSeconds);
        var rtf = audio > 0 ? whisper / audio : 0;
        var latencies = items.Select(m => m.LatencySeconds).OrderBy(v => v).ToArray();
        var p50 = latencies[latencies.Length / 2];
        var p90 = latencies[Math.Min(latencies.Length - 1, (int)(latencies.Length * 0.9))];
        var max = latencies[^1];

        return FormattableString.Invariant(
            $" | {name}={items.Length} audio={audio:0.0}s whisper={whisper:0.0}s rtf={rtf:0.00} latency_p50={p50:0.0}s p90={p90:0.0}s max={max:0.0}s");
    }
}

internal static class LiveMetricsLog
{
    private static readonly object Gate = new();

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LoopbackTranscriber", "live-metrics.log");

    public static void WriteChunk(LiveChunkMetrics m)
    {
        var label = m.IsDraft ? "draft" : m.Reused ? "chunk-reused" : "chunk";
        var first = FormattableString.Invariant(
            $"{label} audio={m.AudioSeconds:0.0}s whisper={m.WhisperSeconds:0.00}s rtf={m.RealTimeFactor:0.00} ");
        var second = FormattableString.Invariant(
            $"latency={m.LatencySeconds:0.0}s queue={m.Backlog} dropped={m.Dropped} chars={m.TextChars}");
        Write(first + second);
    }

    public static void WriteTranslation(LiveTranslationMetrics m)
    {
        var label = m.IsDraft ? "draft-translation" : "translation";
        var first = FormattableString.Invariant(
            $"{label} chars={m.Chars} model={m.Seconds:0.00}s ");
        var second = FormattableString.Invariant($"latency={m.LatencySeconds:0.0}s queue={m.Backlog}");
        Write(first + second);
    }

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                var path = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024)
                {
                    File.Delete(path);
                }

                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ExpectedFailures.IsFile(ex))
        {
            AppLog.Warn("LiveMetricsLog: could not write the log", ex);
        }
    }
}
