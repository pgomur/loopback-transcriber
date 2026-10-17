using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using LoopbackTranscriber.Models;

namespace LoopbackTranscriber.Services;

internal static class TranscriptMessageParser
{
    private static readonly Regex ChatSpeakerPrefixRegex =
        new("^(" + SpeakerLabels.PrefixPattern + @")\s*:\s*", RegexOptions.Compiled);

    public static ObservableCollection<TranscriptMessage> Parse(string text)
    {
        var messages = new ObservableCollection<TranscriptMessage>();

        if (string.IsNullOrWhiteSpace(text))
        {
            return messages;
        }

        var lines = text.Split('\n');
        if (lines.Length > 0 && lines[0].TrimStart().StartsWith("# ", StringComparison.Ordinal))
        {
            text = string.Join('\n', lines.Skip(1)).TrimStart('\n', '\r').Trim();
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return messages;
        }

        var paragraphs = text.Split(new[] { "\n\n" }, StringSplitOptions.None);

        foreach (var rawParagraph in paragraphs)
        {
            var paragraph = rawParagraph.Trim();
            if (paragraph.Length == 0)
            {
                continue;
            }

            var match = ChatSpeakerPrefixRegex.Match(paragraph);
            if (match.Success)
            {
                var speaker = match.Groups[1].Value;
                var body = paragraph.Substring(match.Length).Trim();
                if (body.Length > 0)
                {
                    messages.Add(new TranscriptMessage { Speaker = speaker, Text = body });
                }
            }
            else
            {
                messages.Add(new TranscriptMessage { Speaker = null, Text = paragraph });
            }
        }

        return messages;
    }
}
