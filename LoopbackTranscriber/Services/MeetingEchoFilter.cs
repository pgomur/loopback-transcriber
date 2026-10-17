using System.Linq;
using TranscriptSegment = LoopbackTranscriber.Services.WhisperTranscriptionService.TranscriptSegment;

namespace LoopbackTranscriber.Services;

/// <summary>
/// Descarta los segmentos del microfono que son el eco de lo que ya suena en la reunion
/// (el micro recoge los altavoces), comparando su texto con el de la pista de los participantes.
/// </summary>
internal static class MeetingEchoFilter
{
    private const double EchoSimilarityThreshold = 0.6;
    private const int EchoMinWordCount = 3;
    private static readonly TimeSpan EchoTimeMargin = TimeSpan.FromSeconds(3);

    public static List<TranscriptSegment> RemoveEchoes(
        IReadOnlyList<TranscriptSegment> micSegments,
        IReadOnlyList<TranscriptSegment> otherSegments,
        TimeSpan micStartOffset)
    {
        return micSegments.Where(s => !IsLikelyMeetingEcho(s, otherSegments, micStartOffset)).ToList();
    }

    private static bool IsLikelyMeetingEcho(
        TranscriptSegment micSegment,
        IReadOnlyList<TranscriptSegment> otherSegments,
        TimeSpan micStartOffset)
    {
        var micWordCount = micSegment.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        if (micWordCount < EchoMinWordCount)
        {
            return false;
        }

        var adjustedStart = micSegment.Start + micStartOffset;
        var adjustedEnd = micSegment.End + micStartOffset;

        var overlappingText = string.Join(" ", otherSegments
            .Where(o => adjustedStart - EchoTimeMargin < o.End && o.Start < adjustedEnd + EchoTimeMargin)
            .Select(o => o.Text));

        if (string.IsNullOrWhiteSpace(overlappingText))
        {
            return false;
        }

        var otherWordCount = overlappingText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        if (otherWordCount < EchoMinWordCount)
        {
            return false;
        }

        return TranscriptFilters.WordOverlapRatio(micSegment.Text, overlappingText) >= EchoSimilarityThreshold;
    }
}
