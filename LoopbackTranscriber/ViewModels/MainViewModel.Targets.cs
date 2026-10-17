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
    public ObservableCollection<TargetItemViewModel> Targets { get; } = [];

    [ObservableProperty]
    private TargetItemViewModel? _selectedTarget;

    private void SyncTargetsFromWatcher()
    {
        foreach (var status in _watcher.Statuses)
        {
            var vm = Targets.FirstOrDefault(t => t.App.Id == status.App.Id);
            vm?.UpdateFrom(status);
        }

        NotifyLiveTranslationAvailability();

        if (IsRecording && SelectedTarget is not null && !SelectedTarget.IsRunning)
        {
            _ = StopAfterAppClosedAsync(SelectedTarget.DisplayName);
            return;
        }

        if (!IsRecording && !IsBusy && SelectedTarget is not null)
        {
            StatusText = SelectedTarget.IsRunning
                ? Strings.Instance.Format("StatusDetected", SelectedTarget.DisplayName, Strings.Instance.Get("RecordStart"))
                : Strings.Instance.Format("StatusNotRunning", SelectedTarget.DisplayName);
        }

        OnPropertyChanged(nameof(CanToggleRecording));
        ToggleRecordingCommand.NotifyCanExecuteChanged();
    }

    private void AddTargetInternal(TargetApp app)
    {
        Targets.Add(new TargetItemViewModel(app));
        _watcher.AddTarget(app);
    }

    private void OnCustomAppsChanged(object? sender, EventArgs e)
    {
        foreach (var app in _customAppsService.CustomApps)
        {
            if (Targets.All(t => t.App.Id != app.Id))
            {
                AddTargetInternal(app);
            }
        }

        var builtInIds = new HashSet<string>(TargetApp.All.Select(a => a.Id));
        var currentCustomIds = new HashSet<string>(_customAppsService.CustomApps.Select(a => a.Id));

        var toRemove = Targets
            .Where(t => !builtInIds.Contains(t.App.Id) && !currentCustomIds.Contains(t.App.Id))
            .ToList();

        foreach (var target in toRemove)
        {
            if (SelectedTarget == target)
            {
                SelectedTarget = null;
            }

            Targets.Remove(target);
            _watcher.RemoveTarget(target.App.Id);
        }

        OnPropertyChanged(nameof(CanToggleRecording));
        ToggleRecordingCommand.NotifyCanExecuteChanged();
        NotifyLiveTranslationAvailability();
    }

    partial void OnSelectedTargetChanged(TargetItemViewModel? value)
    {
        NotifyLiveTranslationAvailability();
        RefreshTargetActiveStates();

        TranscriptText = string.Empty;
        _viewedTranscriptFilePath = null;
        ClearViewedSummary();

        ApplyTargetSelection(value);
    }

    private void RefreshTargetActiveStates()
    {
        var activeTarget = CurrentView == MainViewMode.AppView ? SelectedTarget : null;

        foreach (var target in Targets)
        {
            target.IsActive = target == activeTarget;
        }
    }
    private void ApplyTargetSelection(TargetItemViewModel? value)
    {
        CurrentView = MainViewMode.AppView;

        LoadTranscriptHistoryFromDisk();

        if (value is null)
        {
            return;
        }

        StatusText = value.IsRunning
            ? Strings.Instance.Format("StatusDetected", value.DisplayName, Strings.Instance.Get("RecordStart"))
            : Strings.Instance.Format("StatusNotRunning", value.DisplayName);

        OnPropertyChanged(nameof(CanToggleRecording));
        ToggleRecordingCommand.NotifyCanExecuteChanged();
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
            TranscriptText = string.Empty;
            _viewedTranscriptFilePath = null;
            ClearViewedSummary();
        }

        SelectedTarget = target;
        ApplyTargetSelection(target);
    }
}
