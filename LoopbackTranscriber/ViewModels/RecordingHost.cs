using System.ComponentModel;

namespace LoopbackTranscriber.ViewModels;

public sealed class RecordingHost : IRecordingHost, IDisposable
{
    private readonly RecordingViewModel _recording;
    private readonly TargetsViewModel _targets;

    public RecordingHost(RecordingViewModel recording, TargetsViewModel targets)
    {
        _recording = recording;
        _targets = targets;

        _recording.PropertyChanged += OnSourcePropertyChanged;
        _targets.PropertyChanged += OnSourcePropertyChanged;
        _recording.LiveAudioCaptured += OnAudioCaptured;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action<byte[], int>? AudioCaptured;

    public bool IsRecording => _recording.IsRecording;

    public bool IsBusy => _recording.IsBusy;

    public string StatusText => _recording.StatusText;

    public string ElapsedText => _recording.ElapsedText;

    public bool IsTranscribingActive => _recording.IsTranscribingActive;

    public double TranscriptionProgressPercent => _recording.TranscriptionProgressPercent;

    public bool CanToggleRecording => _recording.CanToggleRecording;

    public string? RecordDisabledReason => _recording.RecordDisabledReason;

    public TargetItemViewModel? SelectedTarget => _targets.SelectedTarget;

    public bool CanStartRecordingFor(TargetItemViewModel target) => _recording.CanStartRecording(target);

    public async Task StartRecordingForAsync(TargetItemViewModel target)
    {
        if (!CanStartRecordingFor(target))
        {
            return;
        }

        _targets.Select(target);

        if (!ReferenceEquals(_targets.SelectedTarget, target) || !ReferenceEquals(_recording.SelectedTarget, target))
        {
            return;
        }

        if (_recording.ToggleRecordingCommand.CanExecute(null))
        {
            await _recording.ToggleRecordingCommand.ExecuteAsync(null);
        }
    }

    public Task StopRecordingAsync() => _recording.StopIfRecordingAsync();

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e) => PropertyChanged?.Invoke(this, e);

    private void OnAudioCaptured(byte[] buffer, int count) => AudioCaptured?.Invoke(buffer, count);

    public void Dispose()
    {
        _recording.PropertyChanged -= OnSourcePropertyChanged;
        _targets.PropertyChanged -= OnSourcePropertyChanged;
        _recording.LiveAudioCaptured -= OnAudioCaptured;
    }
}
