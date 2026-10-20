using System.Globalization;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Windows.Threading;
using LoopbackTranscriber.Services;

namespace LoopbackTranscriber.ViewModels;

public sealed partial class RecordingViewModel : ObservableObject, IDisposable
{
    private readonly RecordingWorkflow _workflow;
    private readonly IDialogService _dialogs;
    private readonly IMessenger _messenger;
    private readonly UserNotices? _notices;
    private readonly DispatcherTimer _elapsedTimer;
    private DateTime _recordingStartedAt;
    private TargetItemViewModel? _selectedTarget;

    public RecordingViewModel(
        RecordingWorkflow workflow,
        IDialogService dialogs,
        IMessenger messenger,
        UserNotices? notices = null)
    {
        _workflow = workflow;
        _dialogs = dialogs;
        _messenger = messenger;
        _notices = notices;

        _workflow.AudioCaptured += OnAudioCaptured;
        _messenger.Register<RecordingViewModel, TargetSelectionChangedMessage>(this, static (recipient, message) => recipient.OnTargetSelected(message.Target));
        _messenger.Register<RecordingViewModel, SummaryActivityChangedMessage>(this, static (recipient, message) => recipient.OnSummaryActivityChanged(message.IsGenerating));

        _elapsedTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _elapsedTimer.Tick += (_, _) =>
        {
            var elapsed = DateTime.Now - _recordingStartedAt;
            ElapsedText = elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss", CultureInfo.CurrentCulture) : elapsed.ToString(@"mm\:ss", CultureInfo.CurrentCulture);
        };

        RefreshFailedRecordings();
    }

    public event Action<byte[], int>? LiveAudioCaptured;

    private void OnAudioCaptured(byte[] buffer, int count) => LiveAudioCaptured?.Invoke(buffer, count);

    public void OnTargetStatusesSynced()
    {
        var selected = _selectedTarget;

        if (IsRecording && selected is not null && !selected.IsRunning)
        {
            _ = StopAfterAppClosedAsync(selected.DisplayName);
            return;
        }

        if (!IsRecording && !IsBusy && selected is not null)
        {
            StatusText = DescribeIdleState(selected);
        }

        NotifyToggleAvailability();
    }

    public void OnTargetListChanged() => NotifyToggleAvailability();

    public TargetItemViewModel? SelectedTarget => _selectedTarget;

    public void OnTargetSelected(TargetItemViewModel? target)
    {
        _selectedTarget = target;

        if (target is null)
        {
            NotifyToggleAvailability();
            return;
        }

        StatusText = DescribeIdleState(target);
        ApplyFailedRecordingFilter();
        NotifyToggleAvailability();
    }

    public void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(RecordButtonText));

        if (!IsRecording && !IsBusy && _selectedTarget is { } selected)
        {
            StatusText = DescribeIdleState(selected);
        }
    }

    private static string DescribeIdleState(TargetItemViewModel target) =>
        target.IsRunning
            ? Strings.Instance.Format("StatusDetected", target.DisplayName, Strings.Instance.Get("RecordStart"))
            : Strings.Instance.Format("StatusNotRunning", target.DisplayName);

    private void NotifyToggleAvailability()
    {
        OnPropertyChanged(nameof(CanToggleRecording));
        ToggleRecordingCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        _workflow.AudioCaptured -= OnAudioCaptured;
        _elapsedTimer.Stop();
        _messenger.UnregisterAll(this);
    }

    [ObservableProperty]
    private string? _recordingWarning;

    public bool HasRecordingWarning => !string.IsNullOrEmpty(RecordingWarning);

    partial void OnRecordingWarningChanged(string? value) => OnPropertyChanged(nameof(HasRecordingWarning));

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = Strings.Instance.Get("StatusSelectApp");

    [ObservableProperty]
    private string _elapsedText = "00:00";

    [ObservableProperty]
    private bool _isTranscribingActive;

    [ObservableProperty]
    private double _transcriptionProgressPercent;

    public bool CanToggleRecording =>
        IsBusy == false && !IsSummaryGenerating && (IsRecording || (_selectedTarget?.IsRunning ?? false));

    public bool IsSummaryGenerating { get; private set; }

    public string? RecordDisabledReason =>
        IsSummaryGenerating && !IsRecording ? Strings.Instance.Get("RecordBlockedBySummary") : null;

    private void OnSummaryActivityChanged(bool isGenerating)
    {
        IsSummaryGenerating = isGenerating;
        NotifySummaryActivityChanged();
    }

    private void NotifySummaryActivityChanged()
    {
        OnPropertyChanged(nameof(IsSummaryGenerating));
        OnPropertyChanged(nameof(RecordDisabledReason));
        OnPropertyChanged(nameof(CanToggleRecording));
        ToggleRecordingCommand.NotifyCanExecuteChanged();
    }

    public string RecordButtonText => IsRecording ? Strings.Instance.Get("RecordStop") : Strings.Instance.Get("RecordStart");

    private async Task StopAfterAppClosedAsync(string closedAppName)
    {
        AppLog.Info($"RecordingViewModel: {closedAppName} closed during the recording; processing it as a normal stop");
        await StopAndTranscribeAsync();
    }

    [RelayCommand(CanExecute = nameof(CanToggleRecording))]
    private async Task ToggleRecordingAsync()
    {
        if (IsRecording)
        {
            await StopAndTranscribeAsync();
        }
        else
        {
            await StartRecordingAsync();
        }
    }

    public bool CanStartRecording(TargetItemViewModel target) =>
        !IsRecording && !IsBusy && !IsSummaryGenerating && target.IsRunning;

    public async Task StopIfRecordingAsync()
    {
        if (IsRecording && ToggleRecordingCommand.CanExecute(null))
        {
            await ToggleRecordingCommand.ExecuteAsync(null);
        }
    }

    public async Task<bool> PreserveInterruptedRecordingAsync(TimeSpan timeout)
    {
        if (!TryBeginPreserve(out var appName, out var recordedAt))
        {
            return false;
        }

        bool? preserved;
        try
        {
            preserved = await PreserveCoreAsync(appName, recordedAt).WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            preserved = null;
        }

        return EndPreserve(preserved);
    }

    /// <summary>
    /// Only for callers that cannot await (unhandled-exception handler, Windows session ending).
    /// The work runs on the thread pool and never needs the calling thread, so the bounded wait cannot deadlock.
    /// </summary>
    public bool PreserveInterruptedRecording(TimeSpan timeout)
    {
        if (!TryBeginPreserve(out var appName, out var recordedAt))
        {
            return false;
        }

        var preserving = Task.Run(() => PreserveCoreAsync(appName, recordedAt));
        bool? preserved = preserving.Wait(timeout) ? preserving.Result : null;

        return EndPreserve(preserved);
    }

    private bool TryBeginPreserve(out string appName, out DateTime recordedAt)
    {
        appName = _selectedTarget?.DisplayName ?? "reunion";
        recordedAt = _recordingStartedAt;

        if (!IsRecording)
        {
            return false;
        }

        _elapsedTimer.Stop();
        return true;
    }

    private async Task<bool> PreserveCoreAsync(string appName, DateTime recordedAt)
    {
        try
        {
            var result = await _workflow.StopAndPreserveAsync(appName, recordedAt,
                UserMessage.FromKey("RecordingInterruptedReason")).ConfigureAwait(false);
            return result.Preserved is not null;
        }
        catch (Exception ex)
        {
            AppLog.Warn("RecordingViewModel.PreserveInterruptedRecording: failed to preserve the recording", ex);
            return false;
        }
    }

    private bool EndPreserve(bool? preserved)
    {
        IsRecording = false;

        if (preserved is null)
        {
            AppLog.Error("RecordingViewModel.PreserveInterruptedRecording: could not preserve the recording in time");
            return false;
        }

        RefreshFailedRecordings();
        return preserved.Value;
    }

    public async Task RecoverOrphanRecordingsAsync()
    {
        try
        {
            var result = await Task.Run(_workflow.RecoverOrphans);
            if (result.Recovered.Count > 0)
            {
                RefreshFailedRecordings();
            }

            foreach (var failed in result.Failed)
            {
                _notices?.Add(UserMessage.FromKey("NoticeOrphanRecoveryFailed", failed));
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("RecordingViewModel.RecoverOrphanRecordingsAsync: failed to recover orphan recordings", ex);
            _notices?.Add(UserMessage.FromKey("NoticeOrphanRecoveryFailed", ex.ToUserMessage()));
        }
    }

    private Task _pendingStart = Task.CompletedTask;

    public async Task WaitForPendingStartAsync(TimeSpan timeout)
    {
        try
        {
            await _pendingStart.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            AppLog.Error($"RecordingViewModel.WaitForPendingStartAsync: the recording did not finish starting within {timeout.TotalSeconds:0}s");
        }
    }

    private async Task StartRecordingAsync()
    {
        var target = _selectedTarget;
        if (target is null || !target.IsRunning)
        {
            StatusText = Strings.Instance.Get("StatusOpenAndSelect");
            return;
        }

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingStart = started.Task;

        try
        {
            RecordingWarning = null;
            var warnings = await _workflow.StartAsync(target.ProcessId, target.DisplayName);
            RecordingWarning = JoinWarnings(warnings);

            IsRecording = true;
            StatusText = Strings.Instance.Format("StatusListening", target.DisplayName);
            _recordingStartedAt = DateTime.Now;
            ElapsedText = "00:00";
            _elapsedTimer.Start();
        }
        catch (Exception ex)
        {
            AppLog.Warn("RecordingViewModel.StartRecordingAsync: failed to start recording", ex);
            StatusText = Strings.Instance.Format("StatusRecordFailed", ex.ToUserMessage());
        }
        finally
        {
            OnPropertyChanged(nameof(CanToggleRecording));
            ToggleRecordingCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(RecordButtonText));
            started.TrySetResult();
        }
    }

    private async Task StopAndTranscribeAsync()
    {
        _elapsedTimer.Stop();
        IsRecording = false;

        IsBusy = true;
        StatusText = Strings.Instance.Get("StatusProcessingAudio");
        OnPropertyChanged(nameof(CanToggleRecording));
        ToggleRecordingCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(RecordButtonText));

        var appName = _selectedTarget?.DisplayName ?? "reunion";
        var recordedAt = _recordingStartedAt;

        try
        {
            var result = await _workflow.StopAndProcessAsync(appName, recordedAt, CreateRecordingCallbacks());
            ApplyRecordingResult(result);
        }
        finally
        {
            IsBusy = false;
            MemoryTrimmer.ReleaseInBackground();
            IsTranscribingActive = false;
            OnPropertyChanged(nameof(CanToggleRecording));
            ToggleRecordingCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(RecordButtonText));

            RefreshFailedRecordings();
        }
    }

    private RecordingCallbacks CreateRecordingCallbacks() => new(
        message => StatusText = message.Localize(),
        percent => TranscriptionProgressPercent = percent,
        active => IsTranscribingActive = active);

    private IReadOnlyList<FailedRecording> _allFailedRecordings = [];

    public ObservableCollection<FailedRecordingItem> FailedRecordings { get; } = new();

    public bool HasFailedRecording => FailedRecordings.Count > 0;

    partial void OnIsBusyChanged(bool value)
    {
        NotifyFailedRecordingCommands();
        PublishCaptureState();
    }

    partial void OnIsRecordingChanged(bool value)
    {
        NotifyFailedRecordingCommands();
        PublishCaptureState();
    }

    private void PublishCaptureState() => _messenger.Send(new CaptureStateChangedMessage(IsRecording || IsBusy));

    private void RefreshFailedRecordings()
    {
        _allFailedRecordings = _workflow.ListFailed();
        ApplyFailedRecordingFilter();
    }

    private void ApplyFailedRecordingFilter()
    {
        var appName = _selectedTarget?.DisplayName;

        FailedRecordings.Clear();
        if (appName is not null)
        {
            foreach (var recording in _allFailedRecordings.Where(r => string.Equals(r.AppName, appName, StringComparison.OrdinalIgnoreCase)))
            {
                FailedRecordings.Add(new FailedRecordingItem(recording));
            }
        }

        OnPropertyChanged(nameof(HasFailedRecording));
        NotifyFailedRecordingCommands();
    }

    private void NotifyFailedRecordingCommands()
    {
        RetryFailedRecordingCommand.NotifyCanExecuteChanged();
        DiscardFailedRecordingCommand.NotifyCanExecuteChanged();
    }

    private bool CanRetryFailedRecording(FailedRecording? recording) =>
        recording is not null && !IsBusy && !IsRecording && !IsSummaryGenerating;

    private bool CanDiscardFailedRecording(FailedRecording? recording) => recording is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRetryFailedRecording))]
    private async Task RetryFailedRecordingAsync(FailedRecording? recording)
    {
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
            var result = await _workflow.RetryAsync(recording, CreateRecordingCallbacks());
            ApplyRecordingResult(result);
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
    private void DiscardFailedRecording(FailedRecording? recording)
    {
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

        try
        {
            _workflow.DiscardFailed(recording);
        }
        catch (Exception ex) when (ExpectedFailures.IsFile(ex))
        {
            AppLog.Warn($"RecordingViewModel.DiscardFailedRecording: could not delete \"{recording.Folder}\"", ex);
            _dialogs.ShowError(Strings.Instance.Format("ErrorDiscardFailedRecording", ex.ToUserMessage()), Strings.Instance.Get("ErrorTitle"));
        }

        RefreshFailedRecordings();
    }

    private void ApplyRecordingResult(RecordingResult result)
    {
        try
        {
            ApplyRecordingResultCore(result);
        }
        catch (Exception ex)
        {
            AppLog.Warn("RecordingViewModel.ApplyRecordingResult: could not show the recording result", ex);
            StatusText = Strings.Instance.Format("StatusErrorProcessing", ex.ToUserMessage());
        }
    }

    private static string? JoinWarnings(IReadOnlyList<UserMessage>? warnings) =>
        warnings is { Count: > 0 } ? string.Join(" ", warnings.Select(w => w.Localize())) : null;

    private void ApplyRecordingResultCore(RecordingResult result)
    {
        RecordingWarning = JoinWarnings(result.Warnings);

        if (!result.Succeeded)
        {
            StatusText = result.Preserved is not null
                ? Strings.Instance.Format("StatusErrorProcessingKept", result.Error, result.Preserved.Folder)
                : string.IsNullOrEmpty(result.TempFolder)
                    ? Strings.Instance.Format("StatusErrorProcessing", result.Error)
                    : Strings.Instance.Format("StatusErrorProcessingKept", result.Error, result.TempFolder);
            return;
        }

        var transcript = result.Transcript!;

        var shownText = string.IsNullOrWhiteSpace(transcript.Text)
            ? Strings.Instance.Get("StatusNoTextDetected")
            : transcript.Text;
        _messenger.Send(new TranscriptReadyMessage(shownText, transcript.MarkdownPath));

        if (transcript.MarkdownPath is null)
        {
            StatusText = Strings.Instance.Get("StatusTranscriptReady");
            return;
        }

        StatusText = Strings.Instance.Get("StatusTranscriptReadySaved");
        _messenger.Send(new RecordingSavedMessage());
    }
}
