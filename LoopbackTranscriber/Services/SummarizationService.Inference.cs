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
    private sealed class ProgressTracker
    {
        private readonly IProgress<double>? _progress;
        private readonly double _total;
        private double _done;
        private int _lastReportedPercent = -1;

        public ProgressTracker(IProgress<double>? progress, double total)
        {
            _progress = progress;
            _total = Math.Max(1, total);
            _progress?.Report(0);
        }

        public void Advance(double units)
        {
            if (units <= 0)
            {
                return;
            }

            _done = Math.Min(_done + units, _total);

            var percent = (int)Math.Min(99, Math.Floor(_done * 100.0 / _total));
            if (percent != _lastReportedPercent)
            {
                _lastReportedPercent = percent;
                _progress?.Report(percent);
            }
        }

        public void Complete() => _progress?.Report(100);
    }

    private static async Task<string> RunInferenceAsync(StatelessExecutor executor, string prompt, int maxTokens,
        int expectedTokens, ProgressTracker? tracker, CancellationToken cancellationToken)
    {
        var firstAttempt = await GenerateAsync(executor, prompt, maxTokens, expectedTokens, tracker,
                strongPenalties: false, detectLoops: true, cancellationToken)
            .ConfigureAwait(false);

        if (!firstAttempt.LoopDetected)
        {
            return firstAttempt.Text;
        }

        Debug.WriteLine("[LoopbackTranscriber] SummarizationService: repetition loop detected; retrying with stronger penalties");

        var secondAttempt = await GenerateAsync(executor, prompt, maxTokens, expectedTokens, tracker: null,
                strongPenalties: true, detectLoops: false, cancellationToken)
            .ConfigureAwait(false);

        return secondAttempt.Text;
    }

    private static async Task<(string Text, bool LoopDetected)> GenerateAsync(StatelessExecutor executor,
        string prompt, int maxTokens, int expectedTokens, ProgressTracker? tracker, bool strongPenalties,
        bool detectLoops, CancellationToken cancellationToken)
    {
        var inferenceParams = new InferenceParams
        {
            MaxTokens = maxTokens,
            AntiPrompts = ["<|im_end|>", "</s>"],
            SamplingPipeline = new DefaultSamplingPipeline
            {
                Temperature = strongPenalties ? 0.8f : 0.6f,
                TopK = 30,
                TopP = 0.9f,
                RepeatPenalty = strongPenalties ? 1.25f : 1.12f,
                PenaltyCount = 512,
                FrequencyPenalty = strongPenalties ? 0.6f : 0.3f,
                PresencePenalty = strongPenalties ? 0.5f : 0.3f
            }
        };

        var builder = new StringBuilder();
        var phaseTotal = PrefillProgressUnits + expectedTokens;
        var phaseDone = 0.0;
        var tokenCount = 0;
        var firstToken = true;

        var recentLines = new List<HashSet<string>>();
        var lineStart = 0;
        var repeatedLines = 0;
        var loopDetected = false;

        await foreach (var token in executor.InferAsync(prompt, inferenceParams, cancellationToken)
                           .ConfigureAwait(false))
        {
            builder.Append(token);

            if (firstToken)
            {
                firstToken = false;
                tracker?.Advance(PrefillProgressUnits);
                phaseDone += PrefillProgressUnits;
            }

            if (tokenCount < expectedTokens * 0.97)
            {
                tokenCount++;
                tracker?.Advance(1);
                phaseDone += 1;
            }

            if (!detectLoops || !token.Contains('\n'))
            {
                continue;
            }

            var text = builder.ToString();
            int newline;
            while ((newline = text.IndexOf('\n', lineStart)) >= 0)
            {
                var words = WordSet(text[lineStart..newline]);
                lineStart = newline + 1;

                if (words.Count < 5)
                {
                    continue;
                }

                if (recentLines.Any(previous => IsNearDuplicate(previous, words)))
                {
                    repeatedLines++;
                }
                else
                {
                    recentLines.Add(words);
                    if (recentLines.Count > LoopWindowLines)
                    {
                        recentLines.RemoveAt(0);
                    }
                }
            }

            if (repeatedLines >= LoopRepeatedLinesLimit)
            {
                loopDetected = true;
                break;
            }
        }

        tracker?.Advance(phaseTotal - phaseDone);

        return (builder.ToString(), loopDetected);
    }

    private static HashSet<string> WordSet(string line) =>
        WordRegex.Matches(line.ToLowerInvariant()).Select(match => match.Value).ToHashSet();

    private static bool IsNearDuplicate(HashSet<string> first, HashSet<string> second)
    {
        if (first.Count < 4 || second.Count < 4)
        {
            return first.SetEquals(second);
        }

        var shared = first.Count(second.Contains);
        var union = first.Count + second.Count - shared;

        return shared / (double)union >= NearDuplicateSimilarity;
    }

    internal static string RemoveRepeatedLines(string text)
    {
        var kept = new List<string>();
        var seen = new List<HashSet<string>>();

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.TrimStart();

            if (SectionLabelRegex.IsMatch(line) || trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                seen.Clear();
                kept.Add(line);
                continue;
            }

            var words = WordSet(trimmed);
            if (words.Count >= 4)
            {
                if (seen.Any(previous => IsNearDuplicate(previous, words)))
                {
                    continue;
                }

                seen.Add(words);
            }

            kept.Add(line);
        }

        return string.Join('\n', kept);
    }
}
