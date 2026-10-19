using System.Text.RegularExpressions;
using LoopbackTranscriber.Models;
using LoopbackTranscriber.Services;
using Wpf.Ui.Controls;

namespace LoopbackTranscriber.ViewModels;

public static class SummaryFormatter
{
    private static readonly Dictionary<string, string> LabelToKey = new(StringComparer.OrdinalIgnoreCase)
    {
        ["proposito"] = "SummaryLabelPurpose",
        ["propósito"] = "SummaryLabelPurpose",
        ["purpose"] = "SummaryLabelPurpose",
        ["zweck"] = "SummaryLabelPurpose",
        ["цель"] = "SummaryLabelPurpose",

        ["puntos clave"] = "SummaryLabelKeyPoints",
        ["key points"] = "SummaryLabelKeyPoints",
        ["pontos-chave"] = "SummaryLabelKeyPoints",
        ["kernpunkte"] = "SummaryLabelKeyPoints",
        ["ключевые моменты"] = "SummaryLabelKeyPoints",

        ["temas"] = "SummaryLabelTopics",
        ["topics"] = "SummaryLabelTopics",
        ["themen"] = "SummaryLabelTopics",
        ["темы"] = "SummaryLabelTopics",

        ["objetivos"] = "SummaryLabelGoals",
        ["objetivo"] = "SummaryLabelGoals",
        ["goal"] = "SummaryLabelGoals",
        ["ziel"] = "SummaryLabelGoals",
        ["goals"] = "SummaryLabelGoals",
        ["ziele"] = "SummaryLabelGoals",
        ["цели"] = "SummaryLabelGoals",

        ["proximos pasos"] = "SummaryLabelNextSteps",
        ["próximos pasos"] = "SummaryLabelNextSteps",
        ["next steps"] = "SummaryLabelNextSteps",
        ["next step"] = "SummaryLabelNextSteps",
        ["próximo paso"] = "SummaryLabelNextSteps",
        ["proximo paso"] = "SummaryLabelNextSteps",
        ["siguientes pasos"] = "SummaryLabelNextSteps",
        ["siguiente paso"] = "SummaryLabelNextSteps",
        ["próximo passo"] = "SummaryLabelNextSteps",
        ["proximo passo"] = "SummaryLabelNextSteps",
        ["nächster schritt"] = "SummaryLabelNextSteps",
        ["следующий шаг"] = "SummaryLabelNextSteps",
        ["próximos passos"] = "SummaryLabelNextSteps",
        ["proximos passos"] = "SummaryLabelNextSteps",
        ["nächste schritte"] = "SummaryLabelNextSteps",
        ["следующие шаги"] = "SummaryLabelNextSteps",

        ["tareas"] = "SummaryLabelTasks",
        ["tasks"] = "SummaryLabelTasks",
        ["tarefas"] = "SummaryLabelTasks",
        ["aufgaben"] = "SummaryLabelTasks",
        ["задачи"] = "SummaryLabelTasks",
    };

    private static readonly Dictionary<string, SymbolRegular> KeyToIcon = new()
    {
        ["SummaryLabelPurpose"] = SymbolRegular.DocumentText20,
        ["SummaryLabelKeyPoints"] = SymbolRegular.TextBulletListLtr20,
        ["SummaryLabelTopics"] = SymbolRegular.Clipboard20,
        ["SummaryLabelGoals"] = SymbolRegular.Flag20,
        ["SummaryLabelNextSteps"] = SymbolRegular.ArrowCircleRight20,
        ["SummaryLabelTasks"] = SymbolRegular.TaskListSquareLtr20,
    };

    private static readonly Regex HeaderRegex =
        new(@"^[ \t]*(?:[-*•][ \t]+)?(?<label>[^:\r\n]{2,30}):[ \t]*(?<rest>.*)$", RegexOptions.Compiled);

    private static readonly Regex BulletRegex =
        new(@"^[ \t]*[-•][ \t]+(?<text>.+)$", RegexOptions.Compiled);

    private static readonly Regex TopicRegex =
        new(@"^[ \t]*\*[ \t]+(?<text>.+)$", RegexOptions.Compiled);

    public static List<SummarySection> Parse(string? summaryText)
    {
        var sections = new List<SummarySection>();

        if (string.IsNullOrWhiteSpace(summaryText))
        {
            return sections;
        }

        string? currentKey = null;
        var currentLines = new List<SummaryLine>();
        var hasOpenSection = false;
        var insideTopic = false;

        void Flush()
        {
            if (!hasOpenSection || (currentKey is null && currentLines.Count == 0))
            {
                return;
            }

            sections.Add(new SummarySection
            {
                Heading = currentKey is null ? string.Empty : Strings.Instance.Get(currentKey),
                Icon = currentKey is not null && KeyToIcon.TryGetValue(currentKey, out var icon) ? icon : SymbolRegular.DocumentText20,
                HasHeading = currentKey is not null,
                Lines = currentLines
            });
        }

        foreach (var rawLine in summaryText.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var header = HeaderRegex.Match(line);
            if (header.Success && LabelToKey.TryGetValue(NormalizeLabel(header.Groups["label"].Value), out var key))
            {
                Flush();
                currentKey = key;
                currentLines = new List<SummaryLine>();
                hasOpenSection = true;
                insideTopic = false;

                var rest = header.Groups["rest"].Value.Trim().Trim('*', '_').Trim();
                if (rest.Length > 0)
                {
                    currentLines.Add(ToLine(rest, false));
                }

                continue;
            }

            hasOpenSection = true;

            var topic = TopicRegex.Match(line);
            if (topic.Success)
            {
                insideTopic = true;
                currentLines.Add(new SummaryLine { Text = topic.Groups["text"].Value.Trim(), IsBullet = false, IsTopic = true });
                continue;
            }

            currentLines.Add(ToLine(line, insideTopic));
        }

        Flush();
        return sections;
    }

    private static string NormalizeLabel(string label) => label.Trim().Trim('*', '#', '_', ' ', '\t').Trim();

    private static SummaryLine ToLine(string text, bool underTopic)
    {
        var bullet = BulletRegex.Match(text);
        return bullet.Success
            ? new SummaryLine { Text = bullet.Groups["text"].Value.Trim(), IsBullet = true, Level = underTopic ? 1 : 0 }
            : new SummaryLine { Text = text, IsBullet = false, Level = underTopic ? 1 : 0 };
    }
}
