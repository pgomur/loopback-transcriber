using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

namespace LoopbackTranscriber.Services;

internal static class WorkerProtocol
{
    public const string ProgressPrefix = "@@P ";
    public const string LiveMessagePrefix = "@@L ";

    public static string FormatProgress(double value) =>
        ProgressPrefix + value.ToString("0.###", CultureInfo.InvariantCulture);

    public static bool TryParseProgress(string? line, out double value)
    {
        value = 0;
        return line is not null
               && line.StartsWith(ProgressPrefix, StringComparison.Ordinal)
               && double.TryParse(line.AsSpan(ProgressPrefix.Length), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    public static string FormatLiveMessage(LiveWorkerMessage message) =>
        LiveMessagePrefix + JsonSerializer.Serialize(message);

    public static bool IsLiveMessage([NotNullWhen(true)] string? line) =>
        line is not null && line.StartsWith(LiveMessagePrefix, StringComparison.Ordinal);

    public static LiveWorkerMessage? ParseLiveMessage(string line) =>
        JsonSerializer.Deserialize<LiveWorkerMessage>(line.AsSpan(LiveMessagePrefix.Length));
}
