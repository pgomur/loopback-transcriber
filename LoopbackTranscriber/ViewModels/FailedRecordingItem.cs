using LoopbackTranscriber.Services;

namespace LoopbackTranscriber.ViewModels;

public sealed class FailedRecordingItem(FailedRecording recording)
{
    public FailedRecording FailedRecording { get; } = recording;

    public string DetailText => FailedRecording.Duration is { } duration
        ? $"{FailedRecording.RecordedAt:g} · {FormatDuration(duration)}"
        : FailedRecording.RecordedAt.ToString("g");

    public string ReasonText => FailedRecording.Error ?? string.Empty;

    public bool HasReason => !string.IsNullOrWhiteSpace(FailedRecording.Error);

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1 ? duration.ToString(@"h\:mm\:ss") : duration.ToString(@"m\:ss");
}
