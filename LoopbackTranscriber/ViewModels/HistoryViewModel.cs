using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LoopbackTranscriber.Models;
using LoopbackTranscriber.Services;

namespace LoopbackTranscriber.ViewModels;

public sealed partial class HistoryViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsStore _settings;
    private readonly TranscriptHistoryService _historyService;
    private readonly TranscriptsFolderWatcher _folderWatcher;
    private readonly IDialogService _dialogs;
    private readonly IMessenger _messenger;
    private string? _selectedAppName;

    public HistoryViewModel(
        ISettingsStore settings,
        TranscriptHistoryService historyService,
        TranscriptsFolderWatcher folderWatcher,
        IDialogService dialogs,
        IMessenger messenger)
    {
        _settings = settings;
        _historyService = historyService;
        _folderWatcher = folderWatcher;
        _dialogs = dialogs;
        _messenger = messenger;

        HistorySearch = new HistorySearchState(() => TranscriptHistory.ToList(), RefreshFilteredTranscriptHistory);
        AppHistorySearch = new HistorySearchState(() => TranscriptHistory.ToList(), RefreshAppTranscriptHistory);
        Filter = new HistoryFilterState(RefreshFilteredTranscriptHistory);
        AppFilter = new HistoryFilterState(RefreshAppTranscriptHistory);

        AppTranscriptHistory.CollectionChanged += (_, _) => HasAppHistoryItems = AppTranscriptHistory.Count > 0;
        TranscriptHistory.CollectionChanged += (_, _) => HasHistoryItems = TranscriptHistory.Count > 0;

        _folderWatcher.Changed += OnFolderChanged;

        _messenger.Register<HistoryViewModel, RecordingSavedMessage>(this, static (recipient, _) => recipient.Reload());
        _messenger.Register<HistoryViewModel, FindTranscriptRequest>(this, static (recipient, request) => request.Reply(recipient.FindByPath(request.FilePath)));
    }

    public ObservableCollection<TranscriptHistoryItem> TranscriptHistory { get; } = [];

    public ObservableCollection<TranscriptHistoryItem> AppTranscriptHistory { get; } = [];

    public ObservableCollection<TranscriptHistoryItem> FilteredTranscriptHistory { get; } = [];

    public HistorySearchState HistorySearch { get; }

    public HistorySearchState AppHistorySearch { get; }

    public HistoryFilterState Filter { get; }

    public HistoryFilterState AppFilter { get; }

    public bool ShowHistoryNoResults => HistorySearch.Matches is not null && FilteredTranscriptHistory.Count == 0 && !HistorySearch.SearchFailed;

    public bool ShowHistorySearchFailed => HistorySearch.SearchFailed;

    public bool ShowAppHistoryNoResults => AppHistorySearch.Matches is not null && AppTranscriptHistory.Count == 0 && !AppHistorySearch.SearchFailed;

    public bool ShowAppHistorySearchFailed => AppHistorySearch.SearchFailed;

    [ObservableProperty]
    private bool _hasSelectedAppHistoryItems;

    [ObservableProperty]
    private bool _hasAppHistoryItems;

    [ObservableProperty]
    private bool _hasHistoryItems;

    [ObservableProperty]
    private bool _hasSelectedHistoryItems;

    public bool CanDeleteSelectedApp => !AppTranscriptHistory.Any(i => i.IsSelected && i.IsGeneratingSummary);

    public bool CanDeleteSelected => !FilteredTranscriptHistory.Any(i => i.IsSelected && i.IsGeneratingSummary);

    public string? DeleteSelectedAppDisabledReason =>
        CanDeleteSelectedApp ? null : Strings.Instance.Get("DeleteBlockedBySummaryBody");

    public string? DeleteSelectedDisabledReason =>
        CanDeleteSelected ? null : Strings.Instance.Get("DeleteBlockedBySummaryBody");

    public void SetSelectedApp(string? appName)
    {
        if (string.Equals(_selectedAppName, appName, StringComparison.Ordinal))
        {
            return;
        }

        _selectedAppName = appName;
        RefreshAppTranscriptHistory();
    }

    public void Reload()
    {
        foreach (var existing in TranscriptHistory)
        {
            existing.PropertyChanged -= OnHistoryItemPropertyChanged;
        }

        TranscriptHistory.Clear();
        HasSelectedHistoryItems = false;

        var settings = _settings.Snapshot();
        _folderWatcher.Watch(settings.TranscriptsFolder);

        foreach (var item in _historyService.LoadFromDisk(settings.TranscriptsFolder))
        {
            item.PropertyChanged += OnHistoryItemPropertyChanged;
            TranscriptHistory.Add(item);
        }

        RefreshAppTranscriptHistory();
        RefreshFilteredTranscriptHistory();

        HistorySearch.Recompute();
        AppHistorySearch.Recompute();
    }

    public void ClearHistorySelection()
    {
        foreach (var item in TranscriptHistory)
        {
            item.IsSelected = false;
        }
    }

    public void ClearAppHistorySelection()
    {
        foreach (var item in AppTranscriptHistory)
        {
            item.IsSelected = false;
        }
    }

    private TranscriptHistoryItem? FindByPath(string filePath) =>
        TranscriptHistory.FirstOrDefault(h => string.Equals(h.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

    private void OnFolderChanged(object? sender, EventArgs e) => Reload();

    private void RefreshFilteredTranscriptHistory()
    {
        FilteredTranscriptHistory.Clear();

        var matches = Filter.Filter(TranscriptHistory);

        if (HistorySearch.Matches is { } searchMatches)
        {
            matches = matches.Where(h => searchMatches.Contains(h.FilePath));
        }

        foreach (var item in matches)
        {
            FilteredTranscriptHistory.Add(item);
        }

        HasSelectedHistoryItems = FilteredTranscriptHistory.Any(i => i.IsSelected);
        OnPropertyChanged(nameof(ShowHistoryNoResults));
        OnPropertyChanged(nameof(ShowHistorySearchFailed));
        NotifyDeleteAvailabilityChanged();
    }

    private void RefreshAppTranscriptHistory()
    {
        foreach (var existing in AppTranscriptHistory)
        {
            existing.PropertyChanged -= OnAppHistoryItemPropertyChanged;
        }

        AppTranscriptHistory.Clear();
        HasSelectedAppHistoryItems = false;
        OnPropertyChanged(nameof(ShowAppHistorySearchFailed));

        if (_selectedAppName is null)
        {
            return;
        }

        var matches = AppFilter.Filter(TranscriptHistory.Where(h => h.AppName == _selectedAppName));

        if (AppHistorySearch.Matches is { } appSearchMatches)
        {
            matches = matches.Where(h => appSearchMatches.Contains(h.FilePath));
        }

        foreach (var item in matches)
        {
            item.IsSelected = false;
            item.PropertyChanged += OnAppHistoryItemPropertyChanged;
            AppTranscriptHistory.Add(item);
        }

        OnPropertyChanged(nameof(ShowAppHistoryNoResults));
    }

    private void OnAppHistoryItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TranscriptHistoryItem.IsSelected))
        {
            HasSelectedAppHistoryItems = AppTranscriptHistory.Any(i => i.IsSelected);
        }

        if (e.PropertyName is nameof(TranscriptHistoryItem.IsSelected) or nameof(TranscriptHistoryItem.IsGeneratingSummary))
        {
            NotifyDeleteAvailabilityChanged();
        }
    }

    private void OnHistoryItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TranscriptHistoryItem.IsSelected))
        {
            HasSelectedHistoryItems = FilteredTranscriptHistory.Any(i => i.IsSelected);
        }

        if (e.PropertyName is nameof(TranscriptHistoryItem.IsSelected) or nameof(TranscriptHistoryItem.IsGeneratingSummary))
        {
            NotifyDeleteAvailabilityChanged();
        }
    }

    private void NotifyDeleteAvailabilityChanged()
    {
        OnPropertyChanged(nameof(CanDeleteSelectedApp));
        OnPropertyChanged(nameof(CanDeleteSelected));
        OnPropertyChanged(nameof(DeleteSelectedAppDisabledReason));
        OnPropertyChanged(nameof(DeleteSelectedDisabledReason));
    }

    [RelayCommand]
    private void OpenTranscriptsFolder()
    {
        var settings = _settings.Snapshot();
        Directory.CreateDirectory(settings.TranscriptsFolder);
        Process.Start(new ProcessStartInfo(settings.TranscriptsFolder) { UseShellExecute = true });
    }

    [RelayCommand]
    private void DeleteTranscript(TranscriptHistoryItem? item)
    {
        if (item is null || item.IsGeneratingSummary)
        {
            return;
        }

        var confirmed = _dialogs.Confirm(
            Strings.Instance.Format("ConfirmDeleteTranscript", item.AppName, item.TimestampText),
            Strings.Instance.Get("ConfirmDeleteTranscriptTitle"));

        if (!confirmed || !DeleteTranscriptFile(item))
        {
            return;
        }

        RemoveFromHistory(item);
        RefreshAppTranscriptHistory();
        RefreshFilteredTranscriptHistory();
    }

    [RelayCommand]
    private void DeleteSelectedAppTranscripts() => DeleteSelected(AppTranscriptHistory);

    [RelayCommand]
    private void DeleteSelectedTranscripts() => DeleteSelected(FilteredTranscriptHistory);

    private void DeleteSelected(IEnumerable<TranscriptHistoryItem> source)
    {
        var selected = source.Where(i => i.IsSelected && !i.IsGeneratingSummary).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        var message = selected.Count == 1
            ? Strings.Instance.Get("ConfirmDeleteSelectedOne")
            : Strings.Instance.Format("ConfirmDeleteSelectedMany", selected.Count);

        if (!_dialogs.Confirm(message, Strings.Instance.Get("ConfirmDeleteSelectedTitle")))
        {
            return;
        }

        foreach (var item in selected)
        {
            if (DeleteTranscriptFile(item))
            {
                RemoveFromHistory(item);
            }
        }

        RefreshAppTranscriptHistory();
        RefreshFilteredTranscriptHistory();
    }

    private void RemoveFromHistory(TranscriptHistoryItem item)
    {
        TranscriptHistory.Remove(item);
        _messenger.Send(new TranscriptDeletedMessage(item.FilePath));
    }

    private bool DeleteTranscriptFile(TranscriptHistoryItem item)
    {
        if (_historyService.TryDeleteTranscriptFile(item, out var error))
        {
            return true;
        }

        _dialogs.ShowError(
            Strings.Instance.Format("ErrorDeleteTranscriptBody", item.AppName, item.TimestampText, error!.ToUserMessage()),
            Strings.Instance.Get("ErrorTitle"));
        return false;
    }

    public void Dispose()
    {
        _messenger.UnregisterAll(this);
        _folderWatcher.Changed -= OnFolderChanged;
    }
}
