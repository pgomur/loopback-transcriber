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

    [ObservableProperty]
    private LiveTranslationSettingsViewModel? _liveSettingsViewModel;

    public bool IsFirstRunActive => CurrentView == MainViewMode.FirstRun;

    public bool IsAppViewActive => CurrentView == MainViewMode.AppView;

    public bool IsHistoryViewActive => CurrentView == MainViewMode.History;

    public bool IsConfigViewActive => CurrentView == MainViewMode.Config;

    public bool IsApplicationsViewActive => CurrentView == MainViewMode.Applications;

    public bool IsLiveSettingsViewActive => CurrentView == MainViewMode.LiveTranslation;

    partial void OnCurrentViewChanged(MainViewMode value)
    {
        OnPropertyChanged(nameof(IsFirstRunActive));
        OnPropertyChanged(nameof(IsAppViewActive));
        OnPropertyChanged(nameof(IsHistoryViewActive));
        OnPropertyChanged(nameof(IsConfigViewActive));
        OnPropertyChanged(nameof(IsApplicationsViewActive));
        OnPropertyChanged(nameof(IsLiveSettingsViewActive));
        ApplicationsViewModel?.SetActive(value == MainViewMode.Applications);
        RefreshTargetActiveStates();
        Viewer.RefreshSummaryModelAvailability();
    }

    [RelayCommand]
    private void ShowHistory()
    {
        Viewer.Clear();

        History.Reload();
        CurrentView = MainViewMode.History;
    }

    private void ShowFirstRunSetup()
    {
        if (FirstRunViewModel is null)
        {
            FirstRunViewModel = _pages.CreateFirstRun();
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
            ConfigViewModel = _pages.CreateConfig();
        }
        else
        {
            ConfigViewModel.RefreshFromDisk();
        }

        CurrentView = MainViewMode.Config;
    }

    [RelayCommand]
    private void ShowLiveSettings()
    {
        if (LiveSettingsViewModel is null)
        {
            LiveSettingsViewModel = _pages.CreateLiveSettings();
        }
        else
        {
            LiveSettingsViewModel.RefreshFromDisk();
        }

        CurrentView = MainViewMode.LiveTranslation;
    }

    [RelayCommand]
    private void ShowApplications()
    {
        if (ApplicationsViewModel is null)
        {
            ApplicationsViewModel = _pages.CreateApplications();
        }
        else
        {
            ApplicationsViewModel.RefreshFromDisk();
        }

        CurrentView = MainViewMode.Applications;
    }
}
