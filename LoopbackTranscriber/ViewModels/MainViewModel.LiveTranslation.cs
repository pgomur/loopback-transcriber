using CommunityToolkit.Mvvm.Input;

namespace LoopbackTranscriber.ViewModels;

public sealed partial class MainViewModel
{
    public event EventHandler<TargetItemViewModel>? LiveTranslationToggleRequested;

    public bool CanToggleLiveTranslation => Targets.SelectedTarget?.IsRunning ?? false;

    private void NotifyLiveTranslationAvailability()
    {
        OnPropertyChanged(nameof(CanToggleLiveTranslation));
        ToggleLiveTranslationCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanToggleLiveTranslation))]
    private void ToggleLiveTranslation()
    {
        if (Targets.SelectedTarget is { } target)
        {
            LiveTranslationToggleRequested?.Invoke(this, target);
        }
    }
}
