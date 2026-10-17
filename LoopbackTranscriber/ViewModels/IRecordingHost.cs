using System.ComponentModel;

namespace LoopbackTranscriber.ViewModels;

public interface IRecordingHost : INotifyPropertyChanged
{
    bool IsRecording { get; }

    bool IsBusy { get; }

    string StatusText { get; }

    string ElapsedText { get; }

    bool IsTranscribingActive { get; }

    double TranscriptionProgressPercent { get; }

    bool CanToggleRecording { get; }

    string? RecordDisabledReason { get; }

    TargetItemViewModel? SelectedTarget { get; }

    event Action<byte[], int>? AudioCaptured;

    bool CanStartRecordingFor(TargetItemViewModel target);

    Task StartRecordingForAsync(TargetItemViewModel target);

    Task StopRecordingAsync();
}
