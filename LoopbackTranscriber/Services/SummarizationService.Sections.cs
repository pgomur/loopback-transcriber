using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LLama;
using LLama.Common;
using LLama.Sampling;
using LoopbackTranscriber.Models;

namespace LoopbackTranscriber.Services;

public sealed partial class SummarizationService
{
    private static async Task<string> CondenseIfNeededAsync(StatelessExecutor executor, string transcriptText,
        ProgressTracker tracker, CancellationToken cancellationToken)
    {
        if (transcriptText.Length <= MaxChunkCharacters)
        {
            return transcriptText;
        }

        var chunks = SplitIntoChunks(transcriptText, MaxChunkCharacters);
        var keyPointsBuilder = new StringBuilder();
        var perPartLimit = Math.Max(300, (MaxTranscriptCharacters - chunks.Count * 60) / chunks.Count);

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunkPrompt = SummaryPrompts.Chunk(chunks[i], i + 1, chunks.Count);
            var chunkOutput = await RunInferenceAsync(executor, chunkPrompt, MaxChunkOutputTokens,
                    ChunkExpectedTokens, tracker, cancellationToken)
                .ConfigureAwait(false);

            keyPointsBuilder.AppendLine(CultureInfo.InvariantCulture, $"Puntos clave (parte {i + 1} de {chunks.Count}):");
            keyPointsBuilder.AppendLine(FitPointsToLimit(RemoveRepeatedLines(StripThinkBlock(chunkOutput).Trim()), perPartLimit));
            keyPointsBuilder.AppendLine();
        }

        var combined = keyPointsBuilder.ToString();

        return combined.Length > MaxTranscriptCharacters
            ? combined[..MaxTranscriptCharacters] + "..."
            : combined;
    }

    private static bool HasSection(string summaryText, Func<string, bool> isLabel) =>
        SectionLabelRegex.Matches(summaryText).Any(match => isLabel(match.Groups["label"].Value));

    private static async Task<string> AddMissingSectionsAsync(StatelessExecutor executor, string source,
        string summaryText, string language, CancellationToken cancellationToken)
    {
        var languageName = GetSummaryLanguageName(language);
        var result = summaryText;

        if (!HasSection(result, label => label.StartsWith("objetiv", StringComparison.OrdinalIgnoreCase)))
        {
            var goals = await ExtractSectionItemsAsync(executor, SummaryPrompts.Goals(source, languageName),
                cancellationToken).ConfigureAwait(false);

            if (goals is not null)
            {
                result = result.TrimEnd() + "\nObjetivos:\n" + goals;
            }
        }

        if (!HasSection(result, label => label.Contains("paso", StringComparison.OrdinalIgnoreCase)))
        {
            var nextSteps = await ExtractSectionItemsAsync(executor, SummaryPrompts.NextSteps(source, languageName),
                cancellationToken).ConfigureAwait(false);

            if (nextSteps is not null)
            {
                result = result.TrimEnd() + "\nProximos pasos:\n" + nextSteps;
            }
        }

        return result;
    }

    private static async Task<string?> ExtractSectionItemsAsync(StatelessExecutor executor, string prompt,
        CancellationToken cancellationToken)
    {
        var output = await RunInferenceAsync(executor, prompt, MaxFollowUpOutputTokens, FollowUpExpectedTokens,
                tracker: null, cancellationToken)
            .ConfigureAwait(false);

        var text = StripThinkBlock(output).Trim();
        if (text.Length == 0 ||
            text.StartsWith("NINGUN", StringComparison.OrdinalIgnoreCase) ||
            (text.Length < 40 && text.Contains("ningun", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var lines = RemoveRepeatedLines(text)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Select(line => line.StartsWith("- ", StringComparison.Ordinal) ? line
                : line.StartsWith("* ", StringComparison.Ordinal) || line.StartsWith("• ", StringComparison.Ordinal) ? "- " + line[2..].Trim()
                : "- " + line)
            .Where(line => line.Length > 8)
            .Take(8)
            .ToList();

        return lines.Count == 0 ? null : string.Join('\n', lines);
    }

    private static bool IsMarkedPoint(string line) =>
        line.Contains("[OBJETIVO]", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("[PROXIMO_PASO]", StringComparison.OrdinalIgnoreCase);

    internal static string FitPointsToLimit(string points, int maxChars)
    {
        if (points.Length <= maxChars)
        {
            return points;
        }

        var lines = points.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0).ToList();
        var remaining = maxChars - lines.Where(IsMarkedPoint).Sum(line => line.Length + 1);
        var kept = new List<string>();

        foreach (var line in lines)
        {
            if (IsMarkedPoint(line))
            {
                kept.Add(line);
                continue;
            }

            if (line.Length + 1 <= remaining)
            {
                kept.Add(line);
                remaining -= line.Length + 1;
            }
        }

        return string.Join('\n', kept);
    }

    internal static List<string> SplitIntoChunks(string text, int maxChunkCharacters)
    {
        var chunks = new List<string>();
        var start = 0;

        while (start < text.Length)
        {
            var remaining = text.Length - start;
            if (remaining <= maxChunkCharacters)
            {
                chunks.Add(text[start..]);
                break;
            }

            var cutAt = start + maxChunkCharacters;

            var lastBreak = text.LastIndexOfAny(['\n', '.'], cutAt - 1, maxChunkCharacters);
            var effectiveCut = lastBreak > start ? lastBreak + 1 : cutAt;

            chunks.Add(text[start..effectiveCut]);
            start = effectiveCut;
        }

        return chunks;
    }
}
