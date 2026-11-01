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
    internal static string StripThinkBlock(string rawOutput)
    {
        var thinkEnd = rawOutput.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);
        return thinkEnd >= 0 ? rawOutput[(thinkEnd + "</think>".Length)..] : rawOutput;
    }

    private static readonly Regex SectionLabelRegex = new(
        @"^(?<lead>[ \t]*(?:[-*][ \t]+)?(?:\*\*|__)?)(?<label>Prop[oó]sito|Puntos clave|Temas|Objetivos?|Pr[oó]ximos? pasos?|Siguientes? pasos?|Tareas)(?:\*\*|__)?[ \t]*:",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    internal static RecordingSummary LocalizeSectionLabels(RecordingSummary summary, string languageCode)
    {
        if (languageCode == "es")
        {
            return summary;
        }

        var localized = SectionLabelRegex.Replace(summary.Summary, match =>
        {
            var key = match.Groups["label"].Value.ToLowerInvariant() switch
            {
                "proposito" or "propósito" => "SummaryLabelPurpose",
                "puntos clave" => "SummaryLabelKeyPoints",
                "temas" => "SummaryLabelTopics",
                "objetivos" or "objetivo" => "SummaryLabelGoals",
                "proximos pasos" or "próximos pasos" or "proximo paso" or "próximo paso"
                    or "siguientes pasos" or "siguiente paso" => "SummaryLabelNextSteps",
                _ => "SummaryLabelTasks"
            };

            return match.Groups["lead"].Value + Strings.Instance.GetIn(languageCode, key) + ":";
        });

        return new RecordingSummary { Title = summary.Title, Summary = localized };
    }

    private static readonly Regex TituloLabelRegex =
        new(@"\**\s*T[IÍ]TULO\s*\**\s*:\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ResumenLabelRegex =
        new(@"\**\s*RES[UÚ]MEN\s*\**\s*:\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static RecordingSummary ParseResult(string rawOutput)
    {
        rawOutput = StripThinkBlock(rawOutput).Trim();

        var plainFormat = TryParsePlainFormat(rawOutput);
        if (plainFormat is not null)
        {
            return plainFormat;
        }

        var jsonResult = TryParseJsonFormat(rawOutput);
        if (jsonResult is not null)
        {
            return jsonResult;
        }

        var fallbackTitle = rawOutput.Split('\n', 2)[0].Trim();
        if (fallbackTitle.Length > 60)
        {
            fallbackTitle = fallbackTitle[..60].TrimEnd() + "...";
        }

        return new RecordingSummary
        {
            Title = string.IsNullOrWhiteSpace(fallbackTitle) ? "Resumen" : fallbackTitle,
            Summary = string.IsNullOrWhiteSpace(rawOutput) ? "Resumen" : rawOutput
        };
    }

    private static RecordingSummary? TryParsePlainFormat(string text)
    {
        var resumenMatch = ResumenLabelRegex.Match(text);
        if (!resumenMatch.Success)
        {
            return null;
        }

        var beforeResumen = text[..resumenMatch.Index];
        var summary = text[(resumenMatch.Index + resumenMatch.Length)..].Trim();

        var tituloMatch = TituloLabelRegex.Match(beforeResumen);
        var title = tituloMatch.Success
            ? beforeResumen[(tituloMatch.Index + tituloMatch.Length)..]
            : beforeResumen;

        var titleFirstLine = title
            .Split('\n')
            .Select(line => line.Trim().Trim('*').Trim())
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));

        if (string.IsNullOrWhiteSpace(titleFirstLine) || string.IsNullOrWhiteSpace(summary))
        {
            return null;
        }

        return new RecordingSummary { Title = titleFirstLine, Summary = summary };
    }

    private static readonly JsonSerializerOptions CaseInsensitiveJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static RecordingSummary? TryParseJsonFormat(string rawOutput)
    {
        var jsonCandidate = ExtractFirstJsonObject(rawOutput);
        if (jsonCandidate is null)
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<RawSummaryResult>(jsonCandidate, CaseInsensitiveJsonOptions);

            return parsed is { Title.Length: > 0, Summary.Length: > 0 }
                ? new RecordingSummary { Title = parsed.Title.Trim(), Summary = parsed.Summary.Trim() }
                : null;
        }
        catch (Exception ex) when (ex is JsonException)
        {
            AppLog.Warn("SummarizationService.ParseResult: the response was not valid JSON either; using the raw text", ex);
            return null;
        }
    }

    private static string? ExtractFirstJsonObject(string text)
    {
        var start = text.IndexOf('{');

        while (start >= 0)
        {
            var end = FindMatchingBrace(text, start);
            if (end > start)
            {
                return text[start..(end + 1)];
            }

            start = text.IndexOf('{', start + 1);
        }

        return null;
    }

    private static int FindMatchingBrace(string text, int startIndex)
    {
        var depth = 0;
        var inString = false;

        for (var i = startIndex; i < text.Length; i++)
        {
            var c = text[i];

            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (c == '"')
            {
                inString = true;
            }
            else if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private sealed class RawSummaryResult
    {
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
    }
}
