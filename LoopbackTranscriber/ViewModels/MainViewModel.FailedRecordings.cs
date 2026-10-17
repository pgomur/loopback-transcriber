using CommunityToolkit.Mvvm.Input;
using LoopbackTranscriber.Services;

namespace LoopbackTranscriber.ViewModels;

public partial class MainViewModel
{
    private FailedRecording? _failedRecording;
    private int _failedRecordingCount;

    public bool HasFailedRecording => _failedRecording is not null;

    public string FailedRecordingText
    {
        get
        {
            if (_failedRecording is null)
            {
                return string.Empty;
            }

            var text = Strings.Instance.Format("FailedRecordingBanner", _failedRecording.AppName,
                _failedRecording.RecordedAt.ToString("g"));
            return _failedRecordingCount > 1 ? $"{text} (+{_failedRecordingCount - 1})" : text;
        }
    }

    partial void OnIsBusyChanged(bool value) => NotifyFailedRecordingCommands();

    partial void OnIsRecordingChanged(bool value) => NotifyFailedRecordingCommands();

    private void RefreshFailedRecordings()
    {
        var all = FailedRecordingStore.List();
        _failedRecording = all.FirstOrDefault();
        _failedRecordingCount = all.Count;

        OnPropertyChanged(nameof(HasFailedRecording));
        OnPropertyChanged(nameof(FailedRecordingText));
        NotifyFailedRecordingCommands();
    }

    private void NotifyFailedRecordingCommands()
    {
        RetryFailedRecordingCommand.NotifyCanExecuteChanged();
        DiscardFailedRecordingCommand.NotifyCanExecuteChanged();
    }

    private bool CanRetryFailedRecording() =>
        _failedRecording is not null && !IsBusy && !IsRecording && !IsSummaryGenerating;

    private bool CanDiscardFailedRecording() => _failedRecording is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRetryFailedRecording))]
    private async Task RetryFailedRecordingAsync()
    {
        var recording = _failedRecording;
        if (recording is null)
        {
            return;
        }

        IsBusy = true;
        StatusText = Strings.Instance.Get("StatusProcessingAudio");
        OnPropertyChanged(nameof(CanToggleRecording));
        ToggleRecordingCommand.NotifyCanExecuteChanged();

        try
        {
            await ProcessRecordingAsync(recording.SourceWavPath, recording.MicWavPath, recording.MicStartOffset,
                recording.AppName, recording.RecordedAt);

            FailedRecordingStore.Delete(recording);
        }
        catch (Exception ex)
        {
            AppLog.Warn("MainViewModel.RetryFailedRecordingAsync: el reintento tambien fallo", ex);
            StatusText = Strings.Instance.Format("StatusErrorProcessing", ex.Message);
        }
        finally
        {
            IsBusy = false;
            IsTranscribingActive = false;
            MemoryTrimmer.ReleaseInBackground();
            OnPropertyChanged(nameof(CanToggleRecording));
            ToggleRecordingCommand.NotifyCanExecuteChanged();
            RefreshFailedRecordings();
        }
    }

    [RelayCommand(CanExecute = nameof(CanDiscardFailedRecording))]
    private void DiscardFailedRecording()
    {
        var recording = _failedRecording;
        if (recording is null)
        {
            return;
        }

        var confirmed = _dialogs.Confirm(
            Strings.Instance.Get("FailedRecordingDiscardConfirm"),
            Strings.Instance.Get("FailedRecordingDiscardTitle"));

        if (!confirmed)
        {
            return;
        }

        FailedRecordingStore.Delete(recording);
        RefreshFailedRecordings();
    }
}
