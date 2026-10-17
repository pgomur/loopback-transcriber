using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoopbackTranscriber.Models;

namespace LoopbackTranscriber.ViewModels;

public sealed partial class HistoryFilterState : ObservableObject
{
    private readonly Action _onChanged;

    public HistoryFilterState(Action onChanged) => _onChanged = onChanged;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private DateTime? _fromDate;

    [ObservableProperty]
    private DateTime? _toDate;

    [ObservableProperty]
    private double? _minDurationMinutes;

    public bool HasActiveFilter => FromDate is not null || ToDate is not null || MinDurationMinutes is not null;

    public IEnumerable<TranscriptHistoryItem> Filter(IEnumerable<TranscriptHistoryItem> items)
    {
        var matches = items;

        if (FromDate is { } fromDate)
        {
            matches = matches.Where(h => h.Timestamp.Date >= fromDate.Date);
        }

        if (ToDate is { } toDate)
        {
            matches = matches.Where(h => h.Timestamp.Date <= toDate.Date);
        }

        if (MinDurationMinutes is { } minDurationMinutes)
        {
            matches = matches.Where(h => h.Duration is { } duration && duration.TotalMinutes >= minDurationMinutes);
        }

        return matches;
    }

    partial void OnFromDateChanged(DateTime? value) => OnCriteriaChanged();

    partial void OnToDateChanged(DateTime? value) => OnCriteriaChanged();

    partial void OnMinDurationMinutesChanged(double? value) => OnCriteriaChanged();

    private void OnCriteriaChanged()
    {
        OnPropertyChanged(nameof(HasActiveFilter));
        _onChanged();
    }

    [RelayCommand]
    private void Toggle() => IsOpen = !IsOpen;

    [RelayCommand]
    private void Apply()
    {
        _onChanged();
        IsOpen = false;
    }

    [RelayCommand]
    private void Clear()
    {
        FromDate = null;
        ToDate = null;
        MinDurationMinutes = null;
    }
}
