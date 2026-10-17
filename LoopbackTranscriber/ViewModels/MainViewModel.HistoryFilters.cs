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
    private bool _isAppHistoryFilterOpen;

    [ObservableProperty]
    private DateTime? _appHistoryFilterFromDate;

    [ObservableProperty]
    private DateTime? _appHistoryFilterToDate;

    [ObservableProperty]
    private double? _appHistoryFilterMinDurationMinutes;

    public bool HasActiveAppHistoryFilter =>
        AppHistoryFilterFromDate is not null ||
        AppHistoryFilterToDate is not null ||
        AppHistoryFilterMinDurationMinutes is not null;

    partial void OnAppHistoryFilterFromDateChanged(DateTime? value) => OnAppHistoryFilterCriteriaChanged();

    partial void OnAppHistoryFilterToDateChanged(DateTime? value) => OnAppHistoryFilterCriteriaChanged();

    partial void OnAppHistoryFilterMinDurationMinutesChanged(double? value) => OnAppHistoryFilterCriteriaChanged();

    private void OnAppHistoryFilterCriteriaChanged()
    {
        OnPropertyChanged(nameof(HasActiveAppHistoryFilter));
        RefreshAppTranscriptHistory();
    }

    [RelayCommand]
    private void ToggleAppHistoryFilter() => IsAppHistoryFilterOpen = !IsAppHistoryFilterOpen;

    [RelayCommand]
    private void ApplyAppHistoryFilter()
    {
        RefreshAppTranscriptHistory();
        IsAppHistoryFilterOpen = false;
    }

    [RelayCommand]
    private void ClearAppHistoryFilter()
    {
        AppHistoryFilterFromDate = null;
        AppHistoryFilterToDate = null;
        AppHistoryFilterMinDurationMinutes = null;
    }

    [ObservableProperty]
    private bool _isHistoryFilterOpen;

    [ObservableProperty]
    private DateTime? _historyFilterFromDate;

    [ObservableProperty]
    private DateTime? _historyFilterToDate;

    [ObservableProperty]
    private double? _historyFilterMinDurationMinutes;

    public bool HasActiveHistoryFilter =>
        HistoryFilterFromDate is not null ||
        HistoryFilterToDate is not null ||
        HistoryFilterMinDurationMinutes is not null;

    partial void OnHistoryFilterFromDateChanged(DateTime? value) => OnHistoryFilterCriteriaChanged();

    partial void OnHistoryFilterToDateChanged(DateTime? value) => OnHistoryFilterCriteriaChanged();

    partial void OnHistoryFilterMinDurationMinutesChanged(double? value) => OnHistoryFilterCriteriaChanged();

    private void OnHistoryFilterCriteriaChanged()
    {
        OnPropertyChanged(nameof(HasActiveHistoryFilter));
        RefreshFilteredTranscriptHistory();
    }

    [RelayCommand]
    private void ToggleHistoryFilter() => IsHistoryFilterOpen = !IsHistoryFilterOpen;

    [RelayCommand]
    private void ApplyHistoryFilter()
    {
        RefreshFilteredTranscriptHistory();
        IsHistoryFilterOpen = false;
    }

    [RelayCommand]
    private void ClearHistoryFilter()
    {
        HistoryFilterFromDate = null;
        HistoryFilterToDate = null;
        HistoryFilterMinDurationMinutes = null;
    }
}
