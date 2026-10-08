using CommunityToolkit.Mvvm.ComponentModel;
using LoopbackTranscriber.Models;
using LoopbackTranscriber.Services;

namespace LoopbackTranscriber.ViewModels;

public partial class TargetItemViewModel : ObservableObject
{
    public TargetApp App { get; }

    public string DisplayName => App.DisplayName;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private uint _processId;

    public TargetItemViewModel(TargetApp app)
    {
        App = app;
    }

    public void UpdateFrom(TargetProcessStatus status)
    {
        IsRunning = status.IsRunning;
        ProcessId = status.ProcessId;
    }
}
