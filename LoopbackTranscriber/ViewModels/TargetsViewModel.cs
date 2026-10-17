using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using LoopbackTranscriber.Models;
using LoopbackTranscriber.Services;

namespace LoopbackTranscriber.ViewModels;

public sealed partial class TargetsViewModel : ObservableObject, IDisposable
{
    private readonly ITargetWatcher _watcher;
    private readonly ICustomAppsService _customApps;
    private readonly IMessenger _messenger;

    public TargetsViewModel(ITargetWatcher watcher, ICustomAppsService customApps, IMessenger messenger)
    {
        _watcher = watcher;
        _customApps = customApps;
        _messenger = messenger;

        foreach (var app in TargetApp.All.Concat(_customApps.CustomApps))
        {
            AddTarget(app);
        }

        _customApps.Changed += OnCustomAppsChanged;
        _watcher.StatusesChanged += OnWatcherStatusesChanged;
    }

    public ObservableCollection<TargetItemViewModel> Items { get; } = [];

    [ObservableProperty]
    private TargetItemViewModel? _selectedTarget;

    partial void OnSelectedTargetChanged(TargetItemViewModel? value) =>
        _messenger.Send(new TargetSelectionChangedMessage(value));

    public void Start()
    {
        _watcher.Start();
        SyncFromWatcher();
    }

    public void Select(TargetItemViewModel target)
    {
        if (!Items.Contains(target))
        {
            return;
        }

        if (ReferenceEquals(SelectedTarget, target))
        {
            _messenger.Send(new TargetSelectionChangedMessage(target));
            return;
        }

        SelectedTarget = target;
    }

    public void SelectFirstIfNone() => SelectedTarget ??= Items.FirstOrDefault();

    public void RefreshActiveStates(bool appViewActive)
    {
        var activeTarget = appViewActive ? SelectedTarget : null;

        foreach (var target in Items)
        {
            target.IsActive = target == activeTarget;
        }
    }

    private void OnWatcherStatusesChanged(object? sender, EventArgs e) => SyncFromWatcher();

    private void SyncFromWatcher()
    {
        foreach (var status in _watcher.Statuses)
        {
            var vm = Items.FirstOrDefault(t => t.App.Id == status.App.Id);
            vm?.UpdateFrom(status);
        }

        _messenger.Send(new TargetStatusesSyncedMessage());
    }

    private void AddTarget(TargetApp app)
    {
        Items.Add(new TargetItemViewModel(app));
        _watcher.AddTarget(app);
    }

    private void OnCustomAppsChanged(object? sender, EventArgs e)
    {
        foreach (var app in _customApps.CustomApps)
        {
            if (Items.All(t => t.App.Id != app.Id))
            {
                AddTarget(app);
            }
        }

        var builtInIds = new HashSet<string>(TargetApp.All.Select(a => a.Id));
        var currentCustomIds = new HashSet<string>(_customApps.CustomApps.Select(a => a.Id));

        var toRemove = Items
            .Where(t => !builtInIds.Contains(t.App.Id) && !currentCustomIds.Contains(t.App.Id))
            .ToList();

        foreach (var target in toRemove)
        {
            if (SelectedTarget == target)
            {
                SelectedTarget = null;
            }

            Items.Remove(target);
            _watcher.RemoveTarget(target.App.Id);
        }

        _messenger.Send(new TargetListChangedMessage());
    }

    public void Dispose()
    {
        _watcher.StatusesChanged -= OnWatcherStatusesChanged;
        _customApps.Changed -= OnCustomAppsChanged;
    }
}
