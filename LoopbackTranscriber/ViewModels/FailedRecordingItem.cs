using System.Globalization;
using LoopbackTranscriber.Services;

namespace LoopbackTranscriber.ViewModels;

public sealed class FailedRecordingItem(FailedRecording recording)
{
    public FailedRecording FailedRecording { get; } = recording;

    public string DetailText => FailedRecording.Duration is { } duration
        ? $"{FailedRecording.RecordedAt:g} · {FormatDuration(duration)}"
        : FailedRecording.RecordedAt.ToString("g", CultureInfo.CurrentCulture);

    public string ReasonText => FailedRecording.ErrorKey is { } key
        ? Strings.Instance.Format(key, FailedRecording.ErrorArgs ?? Array.Empty<string>())
        : FailedRecording.Error ?? string.Empty;

    public bool HasReason => !string.IsNullOrWhiteSpace(FailedRecording.Error);

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1 ? duration.ToString(@"h\:mm\:ss", CultureInfo.CurrentCulture) : duration.ToString(@"m\:ss", CultureInfo.CurrentCulture);
}
