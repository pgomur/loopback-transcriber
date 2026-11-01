using System.ComponentModel;

namespace LoopbackTranscriber.Tests.Fakes;

internal sealed class FakeLiveHost : IRecordingHost
{
    private bool _isRecording;
    private bool _isBusy;
    private string _statusText = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action<byte[], int>? AudioCaptured;

    public bool IsRecording
    {
        get => _isRecording;
        set
        {
            _isRecording = value;
            Raise(nameof(IsRecording));
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            _isBusy = value;
            Raise(nameof(IsBusy));
        }
    }

    public string StatusText
    {
        get => _statusText;
        set
        {
            _statusText = value;
            Raise(nameof(StatusText));
        }
    }

    public string ElapsedText { get; set; } = "00:00";

    public bool IsTranscribingActive { get; set; }

    public double TranscriptionProgressPercent { get; set; }

    public bool CanToggleRecording { get; set; } = true;

    public string? RecordDisabledReason { get; set; }

    public TargetItemViewModel? SelectedTarget { get; set; }

    public bool CanStart { get; set; } = true;

    public bool StartMakesTheHostRecord { get; set; }

    public bool FailStart { get; set; }

    public TaskCompletionSource? StartGate { get; set; }

    public Func<Task>? OnStop { get; set; }

    public List<TargetItemViewModel> Started { get; } = [];

    public int Stops { get; private set; }

    public bool CanStartRecordingFor(TargetItemViewModel target) => CanStart;

    public async Task StartRecordingForAsync(TargetItemViewModel target)
    {
        Started.Add(target);
        SelectedTarget = target;

        if (StartGate is not null)
        {
            await StartGate.Task;
        }

        if (FailStart)
        {
            throw new InvalidOperationException("fallo simulado al grabar");
        }

        if (StartMakesTheHostRecord)
        {
            IsRecording = true;
        }
    }

    public async Task StopRecordingAsync()
    {
        Stops++;
        if (OnStop is not null)
        {
            await OnStop();
        }

        IsRecording = false;
    }

    public void RaiseAudio(byte[] data) => AudioCaptured?.Invoke(data, data.Length);

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
