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

using TranscriptSegment = LoopbackTranscriber.Services.WhisperTranscriptionService.TranscriptSegment;

namespace LoopbackTranscriber.Services;

internal static class TranscriptFilters
{

    private static readonly HashSet<string> KnownHallucinatedPhrases = new(StringComparer.OrdinalIgnoreCase)
    {
        "thank you",
        "thank you very much",
        "thank you so much",
        "thanks for watching",
        "thank you for watching",
        "please subscribe",
        "subscribe to my channel",
        "like and subscribe",
        "bye",
        "bye bye",
        "goodbye",
        "gracias",
        "muchas gracias",
        "gracias por ver el video",
        "subtitulos realizados por la comunidad de amara.org",
        "subtitles by the amara.org community",
        "you",
    };

    internal static bool IsKnownHallucinatedPhrase(string text)
    {
        var normalized = text.Trim(WordTrimChars).Trim();
        return KnownHallucinatedPhrases.Contains(normalized);
    }

    private static readonly TimeSpan MaxMergeGap = TimeSpan.FromSeconds(2);
    private const int MaxMergedSegmentLength = 400;

    private static readonly char[] WordTrimChars = ['.', ',', '!', '?', '¡', '¿', ':', ';', '"', '\''];

    internal static bool IsRepeatedWordLoop(string text)
    {
        var words = text
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim(WordTrimChars).ToLowerInvariant())
            .Where(w => w.Length > 0)
            .ToList();

        if (words.Count < 4)
        {
            return false;
        }

        var mostRepeatedCount = words.GroupBy(w => w).Max(g => g.Count());
        return mostRepeatedCount >= Math.Max(4, (int)(words.Count * 0.6));
    }

    private const int MaxLoopPhraseWords = 12;

    private static bool SameWordRun(string[] keys, int a, int b, int length)
    {
        for (var k = 0; k < length; k++)
        {
            if (keys[a + k] != keys[b + k])
            {
                return false;
            }
        }

        return true;
    }

    internal static string RemovePhraseLoops(string text)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 3)
        {
            return text;
        }

        var keys = words.Select(w => w.Trim(WordTrimChars).ToLowerInvariant()).ToArray();
        var kept = new List<string>(words.Length);
        var changed = false;
        var i = 0;

        while (i < words.Length)
        {
            if (keys[i].Length == 0)
            {
                kept.Add(words[i]);
                i++;
                continue;
            }

            var loopLength = 0;
            var loopRepeats = 0;
            var maxLength = Math.Min(MaxLoopPhraseWords, (words.Length - i) / 2);

            for (var length = 1; length <= maxLength; length++)
            {
                var repeats = 1;
                while (i + (repeats + 1) * length <= words.Length && SameWordRun(keys, i, i + repeats * length, length))
                {
                    repeats++;
                }

                var minRepeats = length == 1 ? 4 : 3;
                if (repeats >= minRepeats)
                {
                    loopLength = length;
                    loopRepeats = repeats;
                    break;
                }
            }

            if (loopLength == 0)
            {
                kept.Add(words[i]);
                i++;
                continue;
            }

            changed = true;
            var removeCompletely = loopLength == 1 ? loopRepeats >= 6 : loopRepeats >= 4;
            if (!removeCompletely)
            {
                for (var k = 0; k < loopLength; k++)
                {
                    kept.Add(words[i + k]);
                }
            }

            i += loopLength * loopRepeats;
        }

        return changed ? string.Join(' ', kept) : text;
    }

    private static string SegmentKey(string text) => string.Join(' ', NormalizeWords(text));

    internal static List<TranscriptSegment> RemoveRepeatedSegments(List<TranscriptSegment> segments)
    {
        var result = new List<TranscriptSegment>(segments.Count);
        var i = 0;

        while (i < segments.Count)
        {
            var key = SegmentKey(segments[i].Text);
            var j = i + 1;
            while (key.Length > 0 && j < segments.Count && SegmentKey(segments[j].Text) == key)
            {
                j++;
            }

            if (j - i < 3)
            {
                for (var k = i; k < j; k++)
                {
                    result.Add(segments[k]);
                }
            }

            i = j;
        }

        return result;
    }

    public static double WordOverlapRatio(string a, string b)
    {
        var wordsA = new HashSet<string>(NormalizeWords(a));
        var wordsB = new HashSet<string>(NormalizeWords(b));
        if (wordsA.Count == 0 || wordsB.Count == 0)
        {
            return 0.0;
        }

        var intersection = wordsA.Intersect(wordsB).Count();
        var union = wordsA.Union(wordsB).Count();
        return union == 0 ? 0.0 : (double)intersection / union;
    }

    private static List<string> NormalizeWords(string text)
    {
        return text
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim(WordTrimChars).ToLowerInvariant())
            .Where(w => w.Length > 0)
            .ToList();
    }

    internal static List<TranscriptSegment> MergeCloseSegments(List<TranscriptSegment> segments)
    {
        var merged = new List<TranscriptSegment>();

        foreach (var segment in segments)
        {
            if (merged.Count > 0)
            {
                var previous = merged[^1];
                var gap = segment.Start - previous.End;

                if (gap <= MaxMergeGap && previous.Text.Length < MaxMergedSegmentLength)
                {
                    merged[^1] = previous with { End = segment.End, Text = previous.Text + " " + segment.Text };
                    continue;
                }
            }

            merged.Add(segment);
        }

        return merged;
    }
}
