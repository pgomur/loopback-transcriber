using System.IO;
using NAudio.Wave;
using Whisper.net;

namespace LoopbackTranscriber.Services;

internal static class LanguageDetector
{
    public const float MinConfidence = 0.5f;

    private const int SampleRate = 16000;
    private const int WindowSeconds = 30;
    private const int StepSeconds = 5;
    private const int MaxWindows = 3;
    private const int MinActiveSecondsPerWindow = 8;
    private const double ActiveSecondRms = 0.01;

    public static (string? Language, float Confidence, int Windows) Detect(WhisperProcessor processor, string wavPath)
    {
        var secondLevels = ReadSecondLevels(wavPath);
        var windows = PickWindows(secondLevels);
        if (windows.Count == 0)
        {
            return (null, 0f, 0);
        }

        var scores = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        var buffer = new float[SampleRate * WindowSeconds];

        foreach (var startSecond in windows)
        {
            var read = ReadWindow(wavPath, startSecond, buffer);
            if (read < SampleRate)
            {
                continue;
            }

            var (language, probability) = processor.DetectLanguageWithProbability(buffer.AsSpan(0, read));
            if (language is null)
            {
                continue;
            }

            scores[language] = scores.GetValueOrDefault(language) + probability;
        }

        if (scores.Count == 0)
        {
            return (null, 0f, windows.Count);
        }

        var best = scores.MaxBy(kv => kv.Value);
        return (best.Key, best.Value / windows.Count, windows.Count);
    }

    private static List<double> ReadSecondLevels(string wavPath)
    {
        using var reader = new WaveFileReader(wavPath);
        var provider = reader.ToSampleProvider();
        var channels = provider.WaveFormat.Channels;
        var samplesPerSecond = provider.WaveFormat.SampleRate * channels;

        var levels = new List<double>();
        var buffer = new float[samplesPerSecond];
        int read;
        while ((read = ReadFull(provider, buffer)) > 0)
        {
            double sum = 0;
            for (var i = 0; i < read; i++)
            {
                sum += buffer[i] * buffer[i];
            }

            levels.Add(Math.Sqrt(sum / read));
        }

        return levels;
    }

    private static List<int> PickWindows(List<double> secondLevels)
    {
        var active = secondLevels.Select(level => level >= ActiveSecondRms ? 1 : 0).ToArray();
        var candidates = new List<(int Start, int Score)>();

        var lastStart = Math.Max(0, active.Length - WindowSeconds);
        for (var start = 0; start <= lastStart; start += StepSeconds)
        {
            var end = Math.Min(active.Length, start + WindowSeconds);
            var score = 0;
            for (var i = start; i < end; i++)
            {
                score += active[i];
            }

            if (score >= MinActiveSecondsPerWindow)
            {
                candidates.Add((start, score));
            }
        }

        var picked = new List<int>();
        foreach (var candidate in candidates.OrderByDescending(c => c.Score).ThenBy(c => c.Start))
        {
            if (picked.All(p => Math.Abs(p - candidate.Start) >= WindowSeconds))
            {
                picked.Add(candidate.Start);
                if (picked.Count == MaxWindows)
                {
                    break;
                }
            }
        }

        return picked;
    }

    private static int ReadWindow(string wavPath, int startSecond, float[] buffer)
    {
        using var reader = new WaveFileReader(wavPath);
        if (reader.WaveFormat.SampleRate != SampleRate || reader.WaveFormat.Channels != 1)
        {
            throw new InvalidDataException($"Language detection expects 16 kHz mono audio, got {reader.WaveFormat}.");
        }

        reader.CurrentTime = TimeSpan.FromSeconds(startSecond);
        return ReadFull(reader.ToSampleProvider(), buffer);
    }

    private static int ReadFull(ISampleProvider provider, float[] buffer)
    {
        var total = 0;
        int read;
        while (total < buffer.Length && (read = provider.Read(buffer, total, buffer.Length - total)) > 0)
        {
            total += read;
        }

        return total;
    }
}
