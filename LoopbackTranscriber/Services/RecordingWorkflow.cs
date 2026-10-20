using LoopbackTranscriber.Models;

namespace LoopbackTranscriber.Services;

public sealed record SavedTranscript(string Text, string? MarkdownPath);

public sealed record RecordingResult(SavedTranscript? Transcript, UserMessage? ErrorMessage, FailedRecording? Preserved, string? TempFolder)
{
    public string? Error => ErrorMessage?.Localize();

    public bool Succeeded => Transcript is not null;
}

public sealed class RecordingWorkflow
{
    private readonly CaptureCoordinator _capture;
    private readonly RecordingProcessor _processor;
    private readonly TranscriptSaver _saver;
    private readonly ISettingsStore _settings;
    private readonly FailedRecordingStore _failedStore;

    public RecordingWorkflow(CaptureCoordinator capture, RecordingProcessor processor, TranscriptSaver saver,
        ISettingsStore settings, FailedRecordingStore failedStore)
    {
        _capture = capture;
        _processor = processor;
        _saver = saver;
        _settings = settings;
        _failedStore = failedStore;

        _capture.AudioCaptured += (buffer, count) => AudioCaptured?.Invoke(buffer, count);
    }

    public event Action<byte[], int>? AudioCaptured;

    public Task StartAsync(uint processId, string? appName = null) => _capture.StartAsync(processId, appName);

    public IReadOnlyList<FailedRecording> RecoverOrphans() =>
        _capture.RecoverOrphans(UserMessage.FromKey("RecordingRecoveredReason"));

    public async Task<RecordingResult> StopAndProcessAsync(string appName, DateTime recordedAt,
        RecordingCallbacks callbacks, CancellationToken cancellationToken = default)
    {
        StoppedRecording? stopped = null;

        try
        {
            stopped = await _capture.StopAsync();

            if (stopped.SourceWavPath is null)
            {
                throw UserError.InvalidOperation(stopped.ProcessStopError, "ErrorAudioCapture");
            }

            var saved = await ProcessAndSaveAsync(stopped.SourceWavPath, stopped.MicWavPath, stopped.MicStartOffset,
                appName, recordedAt, callbacks, cancellationToken);

            _capture.DeleteOriginals(stopped);
            return new RecordingResult(saved, null, null, null);
        }
        catch (Exception ex)
        {
            AppLog.Warn("RecordingWorkflow.StopAndProcessAsync: failed to process the recording", ex);

            return Preserve(stopped, appName, recordedAt, UserMessage.From(ex));
        }
    }

    public async Task<RecordingResult> StopAndPreserveAsync(string appName, DateTime recordedAt, UserMessage reason)
    {
        StoppedRecording? stopped = null;

        try
        {
            stopped = await _capture.StopAsync();
        }
        catch (Exception ex)
        {
            AppLog.Warn("RecordingWorkflow.StopAndPreserveAsync: failed to stop capture", ex);
        }

        return Preserve(stopped, appName, recordedAt, reason);
    }

    private RecordingResult Preserve(StoppedRecording? stopped, string appName, DateTime recordedAt, UserMessage error)
    {
        var outcome = _capture.PreserveFailed(stopped, appName, recordedAt, error);

        if (!outcome.KeepOriginals)
        {
            _capture.DeleteOriginals(stopped);
        }

        return new RecordingResult(null, error, outcome.Saved, outcome.TempFolder);
    }

    public async Task<RecordingResult> RetryAsync(FailedRecording recording, RecordingCallbacks callbacks,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var saved = await ProcessAndSaveAsync(recording.SourceWavPath, recording.MicWavPath, recording.MicStartOffset,
                recording.AppName, recording.RecordedAt, callbacks, cancellationToken);

            _failedStore.Delete(recording);
            return new RecordingResult(saved, null, null, null);
        }
        catch (Exception ex)
        {
            AppLog.Warn("RecordingWorkflow.RetryAsync: the retry failed too", ex);
            return new RecordingResult(null, UserMessage.From(ex), null, null);
        }
    }

    public IReadOnlyList<FailedRecording> ListFailed() => _failedStore.List();

    public void DiscardFailed(FailedRecording recording) => _failedStore.Delete(recording);

    private async Task<SavedTranscript> ProcessAndSaveAsync(string sourceWavPath, string? micWavPath, TimeSpan micStartOffset,
        string appName, DateTime recordedAt, RecordingCallbacks callbacks, CancellationToken cancellationToken)
    {
        var settings = _settings.Load();

        var text = await _processor.ProcessAsync(sourceWavPath, micWavPath, micStartOffset, settings, callbacks, cancellationToken);
        var markdownPath = _saver.Save(text, settings, sourceWavPath, micWavPath, micStartOffset, appName, recordedAt);

        return new SavedTranscript(text, markdownPath);
    }
}
