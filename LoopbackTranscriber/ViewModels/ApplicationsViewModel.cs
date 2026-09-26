using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoopbackTranscriber.Interop;
using LoopbackTranscriber.Models;
using LoopbackTranscriber.Services;

namespace LoopbackTranscriber.ViewModels;

public partial class ApplicationsViewModel : ObservableObject
{
    private readonly CustomAppsService _customAppsService;

    public ObservableCollection<DiscoveredApp> DiscoveredApps { get; } = [];

    public ObservableCollection<TargetApp> AddedApps { get; } = [];

    [ObservableProperty]
    private bool _hasNoDiscoveredApps = true;

    [ObservableProperty]
    private bool _hasNoAddedApps = true;

    public ApplicationsViewModel(CustomAppsService customAppsService)
    {
        _customAppsService = customAppsService;

        RefreshFromDisk();
    }

    public void RefreshFromDisk()
    {
        RefreshAddedApps();
        RefreshDetectedApps();
    }

    private void RefreshAddedApps()
    {
        AddedApps.Clear();
        foreach (var app in _customAppsService.CustomApps)
        {
            AddedApps.Add(app);
        }

        HasNoAddedApps = AddedApps.Count == 0;
    }

    private void RefreshDetectedApps()
    {
        DiscoveredApps.Clear();

        var alreadyTracked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in TargetApp.All)
        {
            foreach (var processName in app.ProcessNames)
            {
                alreadyTracked.Add(processName);
            }
        }

        foreach (var app in _customAppsService.CustomApps)
        {
            foreach (var processName in app.ProcessNames)
            {
                alreadyTracked.Add(processName);
            }
        }

        foreach (var discovered in AudioSessionFinder.ListRunningApps())
        {
            if (!alreadyTracked.Contains(discovered.ProcessName))
            {
                DiscoveredApps.Add(discovered);
            }
        }

        HasNoDiscoveredApps = DiscoveredApps.Count == 0;
    }

    [RelayCommand]
    private void AddApp(DiscoveredApp? app)
    {
        if (app is null)
        {
            return;
        }

        _customAppsService.AddApp(app.ProcessName, app.DisplayName);
        RefreshFromDisk();
    }

    [RelayCommand]
    private void RemoveApp(TargetApp? app)
    {
        if (app is null)
        {
            return;
        }

        _customAppsService.RemoveApp(app.Id);
        RefreshFromDisk();
    }

    [RelayCommand]
    private void Refresh()
    {
        RefreshFromDisk();
    }
}
