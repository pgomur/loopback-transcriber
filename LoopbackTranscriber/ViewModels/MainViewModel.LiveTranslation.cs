using CommunityToolkit.Mvvm.Input;

namespace LoopbackTranscriber.ViewModels;

public partial class MainViewModel
{
    public event EventHandler<TargetItemViewModel>? LiveTranslationToggleRequested;

    public event Action<byte[], int>? LiveAudioCaptured;

    public bool CanToggleLiveTranslation => SelectedTarget?.IsRunning ?? false;

    private void NotifyLiveTranslationAvailability()
    {
        OnPropertyChanged(nameof(CanToggleLiveTranslation));
        ToggleLiveTranslationCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanToggleLiveTranslation))]
    private void ToggleLiveTranslation()
    {
        if (SelectedTarget is { } target)
        {
            LiveTranslationToggleRequested?.Invoke(this, target);
        }
    }
}
