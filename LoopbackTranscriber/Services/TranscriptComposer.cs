using System.Text;
using LoopbackTranscriber.Models;
using TranscriptSegment = LoopbackTranscriber.Services.WhisperTranscriptionService.TranscriptSegment;

namespace LoopbackTranscriber.Services;

public static class TranscriptComposer
{
    internal static readonly TimeSpan ParagraphPause = TimeSpan.FromSeconds(3);

    internal const int MaxParagraphLength = 700;

    public static string Compose(
        List<TranscriptSegment> otherSegments, List<TranscriptSegment> filteredMicSegments, TimeSpan micStartOffset, string? userName)
    {
        var orderedSegments = otherSegments
            .Select(s => (s.Start, s.End, Speaker: SpeakerLabels.Others, Text: s.Text.Trim()))
            .Concat(filteredMicSegments.Select(s =>
                (Start: s.Start + micStartOffset, End: s.End + micStartOffset, Speaker: SpeakerLabels.ForSelf(userName), Text: s.Text.Trim())))
            .Where(s => s.Text.Length > 0)
            .OrderBy(s => s.Start)
            .ToList();

        var labeled = orderedSegments.Any(s => s.Speaker != SpeakerLabels.Others);

        var textBuilder = new StringBuilder();
        string? currentSpeaker = null;
        var previousEnd = TimeSpan.Zero;
        var paragraphLength = 0;

        foreach (var segment in orderedSegments)
        {
            var startsParagraph = currentSpeaker is null ||
                                  segment.Speaker != currentSpeaker ||
                                  segment.Start - previousEnd >= ParagraphPause ||
                                  paragraphLength >= MaxParagraphLength;

            if (startsParagraph)
            {
                if (currentSpeaker is not null)
                {
                    textBuilder.Append("\n\n");
                }

                if (labeled)
                {
                    textBuilder.Append(segment.Speaker).Append(": ");
                }

                currentSpeaker = segment.Speaker;
                paragraphLength = 0;
            }
            else
            {
                textBuilder.Append(' ');
            }

            textBuilder.Append(segment.Text);
            paragraphLength += segment.Text.Length;
            previousEnd = segment.End;
        }

        return textBuilder.ToString();
    }
}
