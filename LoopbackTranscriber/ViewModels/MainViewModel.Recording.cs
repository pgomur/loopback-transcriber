using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoopbackTranscriber.Models;
using LoopbackTranscriber.Services;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LoopbackTranscriber.ViewModels;

public partial class MainViewModel
{
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
        IsBusy == false && !IsSummaryGenerating && (IsRecording || (SelectedTarget?.IsRunning ?? false));

    private int _summaryGenerationCount;

    public bool IsSummaryGenerating => _summaryGenerationCount > 0;

    public string? RecordDisabledReason =>
        IsSummaryGenerating && !IsRecording ? Strings.Instance.Get("RecordBlockedBySummary") : null;

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
        AppLog.Info($"MainViewModel: {closedAppName} se cerro durante la grabacion; se procesa como una parada normal");
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

    public bool CanStartRecordingFor(TargetItemViewModel target) =>
        !IsFirstRunActive && !IsRecording && !IsBusy && !IsSummaryGenerating && target.IsRunning;

    public async Task StartRecordingForAsync(TargetItemViewModel target)
    {
        if (!CanStartRecordingFor(target))
        {
            return;
        }

        SelectTargetCommand.Execute(target);

        if (ToggleRecordingCommand.CanExecute(null))
        {
            await ToggleRecordingCommand.ExecuteAsync(null);
        }
    }

    public async Task StopRecordingFromTrayAsync()
    {
        if (IsRecording && ToggleRecordingCommand.CanExecute(null))
        {
            await ToggleRecordingCommand.ExecuteAsync(null);
        }
    }

    private async Task StartRecordingAsync()
    {
        if (SelectedTarget is null || !SelectedTarget.IsRunning)
        {
            StatusText = Strings.Instance.Get("StatusOpenAndSelect");
            return;
        }

        try
        {
            await _capture.StartAsync(SelectedTarget.ProcessId);

            IsRecording = true;
            StatusText = Strings.Instance.Format("StatusListening", SelectedTarget.DisplayName);
            _recordingStartedAt = DateTime.Now;
            ElapsedText = "00:00";
            _elapsedTimer.Start();
        }
        catch (Exception ex)
        {
            StatusText = Strings.Instance.Format("StatusRecordFailed", ex.Message);
        }
        finally
        {
            OnPropertyChanged(nameof(CanToggleRecording));
            ToggleRecordingCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(RecordButtonText));
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

        var appName = SelectedTarget?.DisplayName ?? "reunion";
        var recordedAt = _recordingStartedAt;

        StoppedRecording? stopped = null;
        var keepOriginals = false;

        try
        {
            stopped = await _capture.StopAsync();

            if (stopped.SourceWavPath is null)
            {
                throw new InvalidOperationException(Strings.Instance.Get("ErrorAudioCapture"), stopped.ProcessStopError);
            }

            await ProcessRecordingAsync(stopped.SourceWavPath, stopped.MicWavPath, stopped.MicStartOffset, appName, recordedAt);
        }
        catch (Exception ex)
        {
            AppLog.Warn("MainViewModel.StopAndTranscribeAsync: fallo al procesar la grabacion", ex);

            var outcome = _capture.PreserveFailed(stopped, appName, recordedAt, ex.Message);
            keepOriginals = outcome.KeepOriginals;

            if (outcome.Saved is not null)
            {
                StatusText = Strings.Instance.Format("StatusErrorProcessingKept", ex.Message, outcome.Saved.Folder);
            }
            else
            {
                StatusText = string.IsNullOrEmpty(outcome.TempFolder)
                    ? Strings.Instance.Format("StatusErrorProcessing", ex.Message)
                    : Strings.Instance.Format("StatusErrorProcessingKept", ex.Message, outcome.TempFolder);
            }
        }
        finally
        {
            IsBusy = false;
            MemoryTrimmer.ReleaseInBackground();
            IsTranscribingActive = false;
            OnPropertyChanged(nameof(CanToggleRecording));
            ToggleRecordingCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(RecordButtonText));

            if (!keepOriginals)
            {
                _capture.DeleteOriginals(stopped);
            }

            RefreshFailedRecordings();
        }
    }

    private async Task ProcessRecordingAsync(string sourceWavPath, string? micRawWavPath, TimeSpan micStartOffset,
        string appName, DateTime recordedAt)
    {
        var settings = _settingsService.Load();
        var callbacks = new RecordingCallbacks(
            text => StatusText = text,
            percent => TranscriptionProgressPercent = percent,
            active => IsTranscribingActive = active);

        var text = await _recordingProcessor.ProcessAsync(sourceWavPath, micRawWavPath, micStartOffset, settings, callbacks);

        ShowAndSaveTranscript(text, settings, sourceWavPath, micRawWavPath, appName, recordedAt);
    }
}
