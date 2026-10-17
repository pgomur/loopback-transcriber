using System.Globalization;
using LoopbackTranscriber.Services;

namespace LoopbackTranscriber.ViewModels;

internal sealed record LiveMetricsView(string Signal, string Chunk, string Translation, string Combined)
{
    public static LiveMetricsView Empty { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty);
}

internal static class LiveMetricsPresenter
{
    private const double NoSignalDb = -75;

    public static LiveMetricsView Format(double levelDb, LiveChunkMetrics? chunk, LiveTranslationMetrics? translation,
        CultureInfo culture)
    {
        var signal = levelDb <= NoSignalDb
            ? Strings.Instance.Get("LiveWindowNoSignal")
            : $"{levelDb.ToString("0", culture)} dB";
        var signalText = Strings.Instance.Format("LiveWindowSignal", signal);

        var chunkText = string.Empty;
        if (chunk is { } m)
        {
            chunkText = Strings.Instance.Format(m.IsDraft ? "LiveWindowMetricsDraft" : "LiveWindowMetrics",
                m.AudioSeconds.ToString("0.0", culture),
                m.WhisperSeconds.ToString("0.0", culture),
                m.LatencySeconds.ToString("0.0", culture));
        }

        var translationText = string.Empty;
        if (translation is { } t)
        {
            translationText = Strings.Instance.Format("LiveWindowTranslationMetrics",
                t.Seconds.ToString("0.0", culture),
                t.LatencySeconds.ToString("0.0", culture));
        }

        var combined = signalText;
        if (chunkText.Length > 0)
        {
            combined += " · " + chunkText;
        }

        if (translationText.Length > 0)
        {
            combined += " · " + translationText;
        }

        return new LiveMetricsView(signalText, chunkText, translationText, combined);
    }
}
