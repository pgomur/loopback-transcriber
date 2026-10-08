using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
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

    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(2);

    private readonly DispatcherTimer _autoRefreshTimer;

    public ApplicationsViewModel(CustomAppsService customAppsService)
    {
        _customAppsService = customAppsService;

        _autoRefreshTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = AutoRefreshInterval };
        _autoRefreshTimer.Tick += (_, _) => RefreshDetectedApps();

        RefreshFromDisk();
    }

    public void SetActive(bool isActive)
    {
        if (isActive)
        {
            _autoRefreshTimer.Start();
        }
        else
        {
            _autoRefreshTimer.Stop();
        }
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

        var found = AudioSessionFinder.ListRunningApps()
            .Where(discovered => !alreadyTracked.Contains(discovered.ProcessName))
            .ToList();

        if (found.Select(a => a.ProcessName).SequenceEqual(
                DiscoveredApps.Select(a => a.ProcessName), StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        DiscoveredApps.Clear();
        foreach (var discovered in found)
        {
            DiscoveredApps.Add(discovered);
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
    private void Refresh()
    {
        RefreshFromDisk();
    }
}
