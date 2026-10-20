using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LoopbackTranscriber.Models;
using LoopbackTranscriber.Services;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LoopbackTranscriber.ViewModels;

public sealed partial class MainViewModel
{
    private void OnTargetStatusesSynced()
    {
        NotifyLiveTranslationAvailability();
        Recording.OnTargetStatusesSynced();
    }

    private void OnTargetListChanged()
    {
        Recording.OnTargetListChanged();
        NotifyLiveTranslationAvailability();
    }

    private void OnTargetSelectionChanged(TargetItemViewModel? value)
    {
        History.SetSelectedApp(value?.DisplayName);
        NotifyLiveTranslationAvailability();
        RefreshTargetActiveStates();

        Viewer.Clear();

        ApplyTargetSelection();
    }

    private void RefreshTargetActiveStates() => Targets.RefreshActiveStates(CurrentView == MainViewMode.AppView);

    private void ApplyTargetSelection()
    {
        CurrentView = MainViewMode.AppView;

        History.Reload();
    }

    [RelayCommand]
    private void SelectTarget(TargetItemViewModel? target)
    {
        if (target is null)
        {
            return;
        }

        if (CurrentView != MainViewMode.AppView)
        {
            Viewer.Clear();
        }

        Targets.SelectedTarget = target;
        ApplyTargetSelection();
    }
}
