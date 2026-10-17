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
    private MainViewMode _currentView = MainViewMode.AppView;

    [ObservableProperty]
    private FirstRunViewModel? _firstRunViewModel;

    [ObservableProperty]
    private ConfigViewModel? _configViewModel;

    [ObservableProperty]
    private ApplicationsViewModel? _applicationsViewModel;

    public bool IsFirstRunActive => CurrentView == MainViewMode.FirstRun;

    public bool IsAppViewActive => CurrentView == MainViewMode.AppView;

    public bool IsHistoryViewActive => CurrentView == MainViewMode.History;

    public bool IsConfigViewActive => CurrentView == MainViewMode.Config;

    public bool IsApplicationsViewActive => CurrentView == MainViewMode.Applications;

    partial void OnCurrentViewChanged(MainViewMode value)
    {
        OnPropertyChanged(nameof(IsFirstRunActive));
        OnPropertyChanged(nameof(IsAppViewActive));
        OnPropertyChanged(nameof(IsHistoryViewActive));
        OnPropertyChanged(nameof(IsConfigViewActive));
        OnPropertyChanged(nameof(IsApplicationsViewActive));
        ApplicationsViewModel?.SetActive(value == MainViewMode.Applications);
        RefreshTargetActiveStates();
        RefreshSummaryModelAvailability();
    }

    [RelayCommand]
    private void ShowHistory()
    {
        TranscriptText = string.Empty;
        _viewedTranscriptFilePath = null;
        ClearViewedSummary();

        LoadTranscriptHistoryFromDisk();
        CurrentView = MainViewMode.History;
    }

    private void ShowFirstRunSetup()
    {
        if (FirstRunViewModel is null)
        {
            FirstRunViewModel = new FirstRunViewModel(_settingsService, _whisper, _summarizer);
            FirstRunViewModel.SetupCompleted += OnFirstRunSetupCompleted;
        }

        CurrentView = MainViewMode.FirstRun;
    }

    private void OnFirstRunSetupCompleted(object? sender, EventArgs e)
    {
        var settings = _settingsService.Load();
        settings.FirstRunSetupCompleted = true;
        _settingsService.Save(settings);

        ShowConfig();
    }

    [RelayCommand]
    private void ShowConfig()
    {
        if (ConfigViewModel is null)
        {
            ConfigViewModel = new ConfigViewModel(_settingsService, _whisper, _summarizer);
        }
        else
        {
            ConfigViewModel.RefreshFromDisk();
        }

        CurrentView = MainViewMode.Config;
    }

    [RelayCommand]
    private void ShowApplications()
    {
        if (ApplicationsViewModel is null)
        {
            ApplicationsViewModel = new ApplicationsViewModel(_customAppsService, _settingsService, _transcriptHistoryService);
        }
        else
        {
            ApplicationsViewModel.RefreshFromDisk();
        }

        CurrentView = MainViewMode.Applications;
    }
}
