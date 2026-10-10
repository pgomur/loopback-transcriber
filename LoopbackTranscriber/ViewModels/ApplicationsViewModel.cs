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
    private readonly SettingsService _settingsService;
    private readonly TranscriptHistoryService _transcriptHistoryService;

    public ObservableCollection<DiscoveredApp> DiscoveredApps { get; } = [];

    public ObservableCollection<AddedAppItem> AddedApps { get; } = [];

    [ObservableProperty]
    private bool _hasNoDiscoveredApps = true;

    [ObservableProperty]
    private bool _hasNoAddedApps = true;

    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(2);

    private readonly DispatcherTimer _autoRefreshTimer;

    public ApplicationsViewModel(
        CustomAppsService customAppsService,
        SettingsService settingsService,
        TranscriptHistoryService transcriptHistoryService)
    {
        _customAppsService = customAppsService;
        _settingsService = settingsService;
        _transcriptHistoryService = transcriptHistoryService;

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
        var transcriptsFolder = _settingsService.Load().TranscriptsFolder;
        foreach (var app in TargetApp.All)
        {
            AddedApps.Add(new AddedAppItem
            {
                App = app,
                CanRemove = false,
                IsBuiltIn = true
            });
        }

        foreach (var app in _customAppsService.CustomApps)
        {
            AddedApps.Add(new AddedAppItem
            {
                App = app,
                CanRemove = !_transcriptHistoryService.HasTranscripts(transcriptsFolder, app.DisplayName)
            });
        }

        HasNoAddedApps = AddedApps.Count == 0;
    }

    private void RefreshDetectedApps()
    {
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
    private void RemoveApp(AddedAppItem? item)
    {
        if (item is null || !item.CanRemove)
        {
            return;
        }

        if (_transcriptHistoryService.HasTranscripts(_settingsService.Load().TranscriptsFolder, item.DisplayName))
        {
            RefreshFromDisk();
            return;
        }

        _customAppsService.RemoveApp(item.App.Id);
        RefreshFromDisk();
    }

    [RelayCommand]
    private void Refresh()
    {
        RefreshFromDisk();
    }
}
