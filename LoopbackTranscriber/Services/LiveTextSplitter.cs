using System.Text.RegularExpressions;

namespace LoopbackTranscriber.Services;

internal static class LiveTextSplitter
{
    private const int SplitThreshold = 90;

    private const int MinPieceChars = 28;

    private static readonly Regex Boundary = new(@"(?<=[.!?…])\s+", RegexOptions.Compiled);

    public static List<string> Split(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length < SplitThreshold)
        {
            return trimmed.Length == 0 ? [] : [trimmed];
        }

        var raw = Boundary.Split(trimmed)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        var pieces = new List<string>();
        for (var i = 0; i < raw.Count; i++)
        {
            var current = raw[i];
            while (current.Length < MinPieceChars && i + 1 < raw.Count)
            {
                i++;
                current += " " + raw[i];
            }

            pieces.Add(current);
        }

        if (pieces.Count > 1 && pieces[^1].Length < MinPieceChars)
        {
            pieces[^2] += " " + pieces[^1];
            pieces.RemoveAt(pieces.Count - 1);
        }

        return pieces;
    }

    public static string ToLines(string? text) => string.Join("\n", Split(text));
}
