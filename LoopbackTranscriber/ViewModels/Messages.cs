using CommunityToolkit.Mvvm.Messaging.Messages;
using LoopbackTranscriber.Models;

namespace LoopbackTranscriber.ViewModels;

public sealed record RecordingSavedMessage;

public sealed record TranscriptReadyMessage(string Text, string? FilePath);

public sealed record CaptureStateChangedMessage(bool IsActive);

public sealed record TranscriptDeletedMessage(string FilePath);

public sealed record SummaryActivityChangedMessage(bool IsGenerating);

public sealed record TargetSelectionChangedMessage(TargetItemViewModel? Target);

public sealed record TargetStatusesSyncedMessage;

public sealed record TargetListChangedMessage;

public sealed record LiveTranslationPreferencesChangedMessage;

public sealed record TranslucentUiChangedMessage(bool Enabled);

public sealed class FindTranscriptRequest(string filePath) : RequestMessage<TranscriptHistoryItem?>
{
    public string FilePath { get; } = filePath;
}
