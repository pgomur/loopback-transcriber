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

    private async Task CancelRecordingAsync(string closedAppName)
    {
        _elapsedTimer.Stop();
        IsRecording = false;

        string? rawWavPath = null;
        var deviceTracks = new List<DeviceLoopbackRecorder.DeviceTrack>();

        try
        {
            rawWavPath = await _recorder.StopAsync();
        }
        catch (Exception ex)
        {
            AppLog.Warn("MainViewModel.CancelRecordingAsync: fallo al parar la captura por proceso", ex);
        }

        try
        {
            deviceTracks = await Task.Run(() => _deviceRecorder.Stop());
        }
        catch (Exception ex)
        {
            AppLog.Warn("MainViewModel.CancelRecordingAsync: fallo al parar la captura por dispositivo", ex);
        }

        string? micWavPath = null;
        try
        {
            micWavPath = await Task.Run(() => _microphoneRecorder.Stop());
        }
        catch (Exception ex)
        {
            AppLog.Warn("MainViewModel.CancelRecordingAsync: fallo al parar la captura del microfono", ex);
        }

        TryDelete(rawWavPath);
        TryDelete(micWavPath);
        foreach (var track in deviceTracks)
        {
            TryDelete(track.WavPath);
        }

        StatusText = Strings.Instance.Format("StatusRecordingCancelledAppClosed", closedAppName);

        OnPropertyChanged(nameof(CanToggleRecording));
        ToggleRecordingCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(RecordButtonText));
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

        _processLoopbackStartedAt = null;
        _deviceLoopbackStartedAt = null;
        _microphoneStartedAt = null;

        try
        {
            var wavPath = Path.Combine(Path.GetTempPath(), "LoopbackTranscriber",
                $"grabacion_{DateTime.Now:yyyyMMdd_HHmmss}.wav");

            await _recorder.StartAsync(SelectedTarget.ProcessId, wavPath);
            _currentRawWavPath = wavPath;
            _processLoopbackStartedAt = DateTime.UtcNow;

            try
            {
                var deviceDir = Path.GetDirectoryName(wavPath)!;
                var deviceBaseName = Path.GetFileNameWithoutExtension(wavPath);
                _deviceRecorder.Start(deviceDir, deviceBaseName);
                _deviceLoopbackStartedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                AppLog.Warn("MainViewModel.StartRecordingAsync: fallo al iniciar la captura por dispositivo, se continua solo con la captura por proceso", ex);
            }

            try
            {
                var micWavPath = Path.Combine(Path.GetDirectoryName(wavPath)!,
                    Path.GetFileNameWithoutExtension(wavPath) + "_mic.wav");
                _microphoneRecorder.Start(micWavPath);
                _microphoneStartedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                AppLog.Warn("MainViewModel.StartRecordingAsync: fallo al iniciar la captura del microfono, el resumen solo identificara al interlocutor", ex);
            }

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

        var recordingDuration = DateTime.Now - _recordingStartedAt;
        IsBusy = true;
        StatusText = Strings.Instance.Get("StatusProcessingAudio");
        OnPropertyChanged(nameof(CanToggleRecording));
        ToggleRecordingCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(RecordButtonText));

        string? rawWavPath = null;
        string? whisperWavPath = null;
        string? micRawWavPath = null;
        string? micWhisperWavPath = null;
        var deviceTracks = new List<DeviceLoopbackRecorder.DeviceTrack>();

        try
        {
            (rawWavPath, deviceTracks, micRawWavPath) = await StopCapturesAsync();

            const double deviceSignalThreshold = 0.01;
            var bestDeviceTrack = deviceTracks
                .Where(t => t.PeakAbs >= deviceSignalThreshold)
                .OrderByDescending(t => t.PeakAbs)
                .FirstOrDefault();

            var transcriptionSourceWavPath = bestDeviceTrack is not null
                ? bestDeviceTrack.WavPath
                : rawWavPath;

            var settings = _settingsService.Load();
            var modelPath = settings.GetModelFilePath();

            var progress = new Progress<string>(text => StatusText = text);

            StatusText = Strings.Instance.Get("StatusCheckingModel");
            await _whisper.EnsureModelDownloadedAsync(settings.ModelSize, modelPath, progress);

            IsTranscribingActive = true;
            TranscriptionProgressPercent = 0;
            StatusText = Strings.Instance.Format("StatusTranscribingWithPercent", 0);

            whisperWavPath = Path.Combine(Path.GetDirectoryName(transcriptionSourceWavPath)!,
                Path.GetFileNameWithoutExtension(transcriptionSourceWavPath) + ".16k.wav");
            AudioConverter.ConvertToWhisperFormat(transcriptionSourceWavPath, whisperWavPath);

            var hasMicTrack = micRawWavPath is not null;
            if (hasMicTrack)
            {
                micWhisperWavPath = Path.Combine(Path.GetDirectoryName(micRawWavPath)!,
                    Path.GetFileNameWithoutExtension(micRawWavPath) + ".16k.wav");
                AudioConverter.ConvertToWhisperFormat(micRawWavPath!, micWhisperWavPath);

                try
                {
                    AudioConverter.ApplyNoiseGate(micWhisperWavPath);
                }
                catch (Exception ex)
                {
                    AppLog.Warn("MainViewModel.StopAndTranscribeAsync: fallo al aplicar la puerta de ruido al microfono, se transcribe sin filtrar", ex);
                }
            }

            var threadCount = WhisperTranscriptionService.GetRecommendedThreadCount(hasMicTrack ? 2 : 1);

            var (otherSegments, micSegments) = await TranscribeTracksAsync(
                whisperWavPath, micWhisperWavPath, hasMicTrack, modelPath, settings.Language, threadCount);

            IsTranscribingActive = false;

            if (hasMicTrack && micSegments.Count > 0)
            {
                micSegments = FilterMicSegmentsByActivity(micSegments, micWhisperWavPath!);
            }

            var otherTrackStartedAt = bestDeviceTrack is not null
                ? _deviceLoopbackStartedAt
                : _processLoopbackStartedAt;
            var micStartOffset = TimeSpan.Zero;
            if (hasMicTrack && otherTrackStartedAt is not null && _microphoneStartedAt is not null)
            {
                micStartOffset = _microphoneStartedAt.Value - otherTrackStartedAt.Value;
            }

            var filteredMicSegments = hasMicTrack
                ? MeetingEchoFilter.RemoveEchoes(micSegments, otherSegments, micStartOffset)
                : micSegments;

            var text = ComposeTranscriptText(otherSegments, filteredMicSegments, micStartOffset, settings.UserName);

            ShowAndSaveTranscript(text, settings, transcriptionSourceWavPath, micRawWavPath);
        }
        catch (Exception ex)
        {
            StatusText = Strings.Instance.Format("StatusErrorProcessing", ex.Message);
        }
        finally
        {
            IsBusy = false;
            MemoryTrimmer.ReleaseInBackground();
            IsTranscribingActive = false;
            OnPropertyChanged(nameof(CanToggleRecording));
        ToggleRecordingCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(RecordButtonText));

            TryDelete(rawWavPath);
            TryDelete(whisperWavPath);
            TryDelete(micRawWavPath);
            TryDelete(micWhisperWavPath);
            foreach (var track in deviceTracks)
            {
                TryDelete(track.WavPath);
            }
        }
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"MainViewModel.TryDelete: no se pudo borrar el fichero temporal \"{path}\"", ex);
        }
    }

    private static string SelfSpeakerLabel(string? userName)
    {
        var cleaned = string.Join(' ', (userName ?? string.Empty)
            .Replace(':', ' ')
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return cleaned.Length == 0 ? "Tu" : $"{cleaned} (Tú)";
    }
}
