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
    public ObservableCollection<TranscriptHistoryItem> TranscriptHistory { get; } = [];

    public ObservableCollection<TranscriptHistoryItem> AppTranscriptHistory { get; } = [];

    public ObservableCollection<TranscriptHistoryItem> FilteredTranscriptHistory { get; } = [];

    public HistorySearchState HistorySearch { get; }

    public HistorySearchState AppHistorySearch { get; }

    public bool ShowHistoryNoResults => HistorySearch.Matches is not null && FilteredTranscriptHistory.Count == 0;

    public bool ShowAppHistoryNoResults => AppHistorySearch.Matches is not null && AppTranscriptHistory.Count == 0;

    [ObservableProperty]
    private bool _hasSelectedAppHistoryItems;

    [ObservableProperty]
    private bool _hasAppHistoryItems;

    private void RefreshFilteredTranscriptHistory()
    {
        FilteredTranscriptHistory.Clear();

        var matches = TranscriptHistory.AsEnumerable();

        if (HistoryFilterFromDate is { } fromDate)
        {
            matches = matches.Where(h => h.Timestamp.Date >= fromDate.Date);
        }

        if (HistoryFilterToDate is { } toDate)
        {
            matches = matches.Where(h => h.Timestamp.Date <= toDate.Date);
        }

        if (HistoryFilterMinDurationMinutes is { } minDurationMinutes)
        {
            matches = matches.Where(h => h.Duration is { } duration && duration.TotalMinutes >= minDurationMinutes);
        }

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
        NotifyDeleteAvailabilityChanged();
    }

    [ObservableProperty]
    private bool _hasHistoryItems;

    [ObservableProperty]
    private bool _hasSelectedHistoryItems;

    private void RefreshAppTranscriptHistory()
    {
        foreach (var existing in AppTranscriptHistory)
        {
            existing.PropertyChanged -= OnAppHistoryItemPropertyChanged;
        }

        AppTranscriptHistory.Clear();
        HasSelectedAppHistoryItems = false;

        if (SelectedTarget is null)
        {
            return;
        }

        var matches = TranscriptHistory.Where(h => h.AppName == SelectedTarget.DisplayName);

        if (AppHistoryFilterFromDate is { } fromDate)
        {
            matches = matches.Where(h => h.Timestamp.Date >= fromDate.Date);
        }

        if (AppHistoryFilterToDate is { } toDate)
        {
            matches = matches.Where(h => h.Timestamp.Date <= toDate.Date);
        }

        if (AppHistoryFilterMinDurationMinutes is { } minDurationMinutes)
        {
            matches = matches.Where(h => h.Duration is { } duration && duration.TotalMinutes >= minDurationMinutes);
        }

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

    public void ClearAppHistorySelection()
    {
        foreach (var item in AppTranscriptHistory)
        {
            item.IsSelected = false;
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

    public void ClearHistorySelection()
    {
        foreach (var item in TranscriptHistory)
        {
            item.IsSelected = false;
        }
    }

    private void LoadTranscriptHistoryFromDisk()
    {
        foreach (var existing in TranscriptHistory)
        {
            existing.PropertyChanged -= OnHistoryItemPropertyChanged;
        }

        TranscriptHistory.Clear();
        HasSelectedHistoryItems = false;

        var settings = _settingsService.Load();
        EnsureTranscriptsWatcher(settings.TranscriptsFolder);

        foreach (var item in _transcriptHistoryService.LoadFromDisk(settings.TranscriptsFolder))
        {
            item.PropertyChanged += OnHistoryItemPropertyChanged;
            TranscriptHistory.Add(item);
        }

        RefreshAppTranscriptHistory();
        RefreshFilteredTranscriptHistory();

        HistorySearch.Recompute();
        AppHistorySearch.Recompute();
    }

    private void EnsureTranscriptsWatcher(string folder)
    {
        if (_transcriptsWatcher is not null &&
            string.Equals(_watchedTranscriptsFolderPath, folder, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _transcriptsWatcher?.Dispose();
        _transcriptsWatcher = null;
        _watchedTranscriptsFolderPath = folder;

        try
        {
            Directory.CreateDirectory(folder);

            var watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
            };

            watcher.Deleted += OnTranscriptsFolderChanged;
            watcher.Renamed += OnTranscriptsFolderChanged;
            watcher.Created += OnTranscriptsFolderChanged;
            watcher.Error += OnTranscriptsWatcherError;
            watcher.EnableRaisingEvents = true;

            _transcriptsWatcher = watcher;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"MainViewModel.EnsureTranscriptsWatcher: no se pudo observar \"{folder}\"", ex);
        }
    }

    private void OnTranscriptsFolderChanged(object? sender, FileSystemEventArgs e)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _transcriptsRefreshTimer.Stop();
            _transcriptsRefreshTimer.Start();
        });
    }

    private void OnTranscriptsWatcherError(object? sender, ErrorEventArgs e)
    {
        AppLog.Warn("MainViewModel: el observador de la carpeta de transcripciones fallo, se reiniciara", e.GetException());

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _watchedTranscriptsFolderPath = null;
            _transcriptsRefreshTimer.Stop();
            _transcriptsRefreshTimer.Start();
        });
    }

    [RelayCommand]
    private void OpenTranscriptsFolder()
    {
        var settings = _settingsService.Load();
        Directory.CreateDirectory(settings.TranscriptsFolder);
        Process.Start(new ProcessStartInfo(settings.TranscriptsFolder) { UseShellExecute = true });
    }
}
