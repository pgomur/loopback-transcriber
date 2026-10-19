using TranscriptSegment = LoopbackTranscriber.Services.WhisperTranscriptionService.TranscriptSegment;

namespace LoopbackTranscriber.Services;

internal static class MicLoudness
{
    private const double VoicedMinRms = 4.0;
    private const double LoudMarginDb = 10.0;
    private const int MinVoicedWindows = 20;

    public static Func<TranscriptSegment, bool> Create(double[]? windowRms)
    {
        if (windowRms is null)
        {
            return _ => false;
        }

        var voiced = windowRms.Where(r => r >= VoicedMinRms).Select(ToDb).OrderBy(d => d).ToArray();
        if (voiced.Length < MinVoicedWindows)
        {
            return _ => false;
        }

        var reference = voiced[(int)(voiced.Length * 0.9)];

        return segment =>
        {
            var first = Math.Clamp((int)(segment.Start.TotalMilliseconds / AudioConverter.ActivityWindowMs), 0, windowRms.Length);
            var last = Math.Clamp((int)Math.Ceiling(segment.End.TotalMilliseconds / AudioConverter.ActivityWindowMs), first, windowRms.Length);

            var levels = new List<double>();
            for (var i = first; i < last; i++)
            {
                if (windowRms[i] >= VoicedMinRms)
                {
                    levels.Add(ToDb(windowRms[i]));
                }
            }

            if (levels.Count == 0)
            {
                return false;
            }

            levels.Sort();
            return levels[(int)(levels.Count * 0.75)] >= reference - LoudMarginDb;
        };
    }

    private static double ToDb(double rms) => 20 * Math.Log10(Math.Max(rms, 1e-9) / 32768.0);
}
