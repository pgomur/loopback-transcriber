using System.Text;
using LoopbackTranscriber.Models;
using TranscriptSegment = LoopbackTranscriber.Services.WhisperTranscriptionService.TranscriptSegment;

namespace LoopbackTranscriber.Services;

public static class TranscriptComposer
{
    public static string Compose(
        List<TranscriptSegment> otherSegments, List<TranscriptSegment> filteredMicSegments, TimeSpan micStartOffset, string? userName)
    {
        var orderedSegments = otherSegments
            .Select(s => (s.Start, Speaker: SpeakerLabels.Others, s.Text))
            .Concat(filteredMicSegments.Select(s => (Start: s.Start + micStartOffset, Speaker: SpeakerLabels.ForSelf(userName), s.Text)))
            .OrderBy(s => s.Start)
            .Where(s => s.Text.Trim().Length > 0)
            .ToList();

        var distinctSpeakers = orderedSegments.Select(s => s.Speaker).Distinct().Count();

        var textBuilder = new StringBuilder();

        if (distinctSpeakers <= 1)
        {
            foreach (var segment in orderedSegments)
            {
                if (textBuilder.Length > 0)
                {
                    textBuilder.Append(' ');
                }

                textBuilder.Append(segment.Text.Trim());
            }
        }
        else
        {
            string? currentSpeaker = null;

            foreach (var segment in orderedSegments)
            {
                var segmentText = segment.Text.Trim();

                if (segment.Speaker != currentSpeaker)
                {
                    if (currentSpeaker is not null)
                    {
                        textBuilder.Append('\n').Append('\n');
                    }

                    textBuilder.Append(segment.Speaker).Append(": ");
                    currentSpeaker = segment.Speaker;
                }
                else
                {
                    textBuilder.Append(' ');
                }

                textBuilder.Append(segmentText);
            }
        }

        return textBuilder.ToString();
    }
}
