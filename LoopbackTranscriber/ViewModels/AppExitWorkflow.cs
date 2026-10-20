using System.ComponentModel;
using LoopbackTranscriber.Services;

namespace LoopbackTranscriber.ViewModels;

public sealed class AppExitWorkflow
{
    private static readonly TimeSpan PreserveTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PendingStartTimeout = TimeSpan.FromSeconds(10);

    private readonly RecordingViewModel _recording;
    private readonly IRecordingHost _host;
    private readonly IDialogService _dialogs;
    private bool _exitPending;

    public AppExitWorkflow(RecordingViewModel recording, IRecordingHost host, IDialogService dialogs)
    {
        _recording = recording;
        _host = host;
        _dialogs = dialogs;
    }

    public async Task<bool> ConfirmExitAsync(Action restoreWindow)
    {
        if (_exitPending)
        {
            return false;
        }

        _exitPending = true;
        try
        {
            await _recording.WaitForPendingStartAsync(PendingStartTimeout);
        }
        finally
        {
            _exitPending = false;
        }

        if (_recording.IsRecording)
        {
            var answer = _dialogs.AskYesNoCancel(
                Strings.Instance.Get("ExitDuringRecordingBody"),
                Strings.Instance.Get("ExitDuringRecordingTitle"));

            if (answer == DialogAnswer.Yes)
            {
                _exitPending = true;
                try
                {
                    restoreWindow();
                    await _host.StopRecordingAsync();
                }
                finally
                {
                    _exitPending = false;
                }
            }
            else if (answer != DialogAnswer.No)
            {
                return false;
            }

            if (_recording.IsRecording)
            {
                _exitPending = true;
                try
                {
                    await _recording.PreserveInterruptedRecordingAsync(PreserveTimeout);
                }
                finally
                {
                    _exitPending = false;
                }
            }
        }
        else if (_recording.IsBusy)
        {
            var confirmed = _dialogs.Confirm(
                Strings.Instance.Get("ExitWhileProcessingBody"),
                Strings.Instance.Get("ExitWhileProcessingTitle"));

            if (!confirmed)
            {
                return false;
            }

            _exitPending = true;
            try
            {
                restoreWindow();
                await WaitUntilNotBusyAsync(_recording);
            }
            finally
            {
                _exitPending = false;
            }
        }

        return true;
    }

    private static Task WaitUntilNotBusyAsync(RecordingViewModel recording)
    {
        if (!recording.IsBusy)
        {
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource();

        void OnChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RecordingViewModel.IsBusy) && !recording.IsBusy)
            {
                recording.PropertyChanged -= OnChanged;
                completion.TrySetResult();
            }
        }

        recording.PropertyChanged += OnChanged;
        return completion.Task;
    }
}
