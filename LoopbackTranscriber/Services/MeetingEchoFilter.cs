using System.Linq;
using TranscriptSegment = LoopbackTranscriber.Services.WhisperTranscriptionService.TranscriptSegment;

namespace LoopbackTranscriber.Services;

internal sealed record EchoResolution(List<TranscriptSegment> MicSegments, List<TranscriptSegment> OtherSegments);

internal static class MeetingEchoFilter
{
    private const double EchoSimilarityThreshold = 0.6;
    private const double DuplicateContainmentThreshold = 0.6;
    private const int EchoMinWordCount = 3;
    private static readonly TimeSpan EchoTimeMargin = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MicLeadForOriginal = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan MicLeadToleranceWhenLoud = TimeSpan.FromMilliseconds(150);

    public static EchoResolution Resolve(
        IReadOnlyList<TranscriptSegment> micSegments,
        IReadOnlyList<TranscriptSegment> otherSegments,
        TimeSpan micStartOffset,
        Func<TranscriptSegment, bool> isMicSegmentLoud)
    {
        var keptMic = new List<TranscriptSegment>();
        var removedOther = new HashSet<int>();
        var droppedMic = 0;
        var keptAsSelf = 0;

        foreach (var micSegment in micSegments)
        {
            var overlapping = FindDuplicateCandidates(micSegment, otherSegments, micStartOffset);
            if (overlapping is null)
            {
                keptMic.Add(micSegment);
                continue;
            }

            var adjustedStart = micSegment.Start + micStartOffset;
            var micLead = overlapping.Min(i => otherSegments[i].Start) - adjustedStart;

            var micIsOriginal = micLead >= MicLeadForOriginal
                                || (micLead >= -MicLeadToleranceWhenLoud && isMicSegmentLoud(micSegment));

            if (!micIsOriginal)
            {
                droppedMic++;
                continue;
            }

            keptMic.Add(micSegment);
            keptAsSelf++;

            foreach (var index in overlapping)
            {
                if (TranscriptFilters.WordContainmentRatio(otherSegments[index].Text, micSegment.Text) >= DuplicateContainmentThreshold)
                {
                    removedOther.Add(index);
                }
            }
        }

        if (droppedMic > 0 || keptAsSelf > 0)
        {
            AppLog.Info($"MeetingEchoFilter: dropped {droppedMic} microphone segment(s) as echo of the app track; kept {keptAsSelf} as the user's own speech and removed {removedOther.Count} duplicate(s) from the app track");
        }

        var keptOther = otherSegments.Where((_, index) => !removedOther.Contains(index)).ToList();
        return new EchoResolution(keptMic, keptOther);
    }

    private static List<int>? FindDuplicateCandidates(
        TranscriptSegment micSegment,
        IReadOnlyList<TranscriptSegment> otherSegments,
        TimeSpan micStartOffset)
    {
        var micWordCount = micSegment.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        if (micWordCount < EchoMinWordCount)
        {
            return null;
        }

        var adjustedStart = micSegment.Start + micStartOffset;
        var adjustedEnd = micSegment.End + micStartOffset;

        var overlapping = Enumerable.Range(0, otherSegments.Count)
            .Where(i => adjustedStart - EchoTimeMargin < otherSegments[i].End && otherSegments[i].Start < adjustedEnd + EchoTimeMargin)
            .ToList();

        if (overlapping.Count == 0)
        {
            return null;
        }

        var overlappingText = string.Join(" ", overlapping.Select(i => otherSegments[i].Text));
        var otherWordCount = overlappingText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        if (otherWordCount < EchoMinWordCount)
        {
            return null;
        }

        return TranscriptFilters.WordOverlapRatio(micSegment.Text, overlappingText) >= EchoSimilarityThreshold
            ? overlapping
            : null;
    }
}
