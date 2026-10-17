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
    private bool DeleteTranscriptFile(TranscriptHistoryItem item)
    {
        if (_transcriptHistoryService.TryDeleteTranscriptFile(item, out var error))
        {
            return true;
        }

        MessageBox.Show(
            Strings.Instance.Format("ErrorDeleteTranscriptBody", item.AppName, item.TimestampText, error!.Message),
            Strings.Instance.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        return false;
    }

    private static List<TranscriptHistoryItem> ExcludeItemsBeingSummarized(List<TranscriptHistoryItem> selected) =>
        selected.Where(i => !i.IsGeneratingSummary).ToList();

    public bool CanDeleteSelectedApp => !AppTranscriptHistory.Any(i => i.IsSelected && i.IsGeneratingSummary);

    public bool CanDeleteSelected => !FilteredTranscriptHistory.Any(i => i.IsSelected && i.IsGeneratingSummary);

    public string? DeleteSelectedAppDisabledReason =>
        CanDeleteSelectedApp ? null : Strings.Instance.Get("DeleteBlockedBySummaryBody");

    public string? DeleteSelectedDisabledReason =>
        CanDeleteSelected ? null : Strings.Instance.Get("DeleteBlockedBySummaryBody");

    private void NotifyDeleteAvailabilityChanged()
    {
        OnPropertyChanged(nameof(CanDeleteSelectedApp));
        OnPropertyChanged(nameof(CanDeleteSelected));
        OnPropertyChanged(nameof(DeleteSelectedAppDisabledReason));
        OnPropertyChanged(nameof(DeleteSelectedDisabledReason));
    }

    [RelayCommand]
    private void DeleteTranscript(TranscriptHistoryItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (item.IsGeneratingSummary)
        {
            return;
        }

        var result = MessageBox.Show(
            Strings.Instance.Format("ConfirmDeleteTranscript", item.AppName, item.TimestampText),
            Strings.Instance.Get("ConfirmDeleteTranscriptTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        if (!DeleteTranscriptFile(item))
        {
            return;
        }

        TranscriptHistory.Remove(item);
        ClearViewedTranscriptIfDeleted(item);
        RefreshAppTranscriptHistory();
        RefreshFilteredTranscriptHistory();
    }

    [RelayCommand]
    private void DeleteSelectedAppTranscripts()
    {
        var selected = ExcludeItemsBeingSummarized(AppTranscriptHistory.Where(i => i.IsSelected).ToList());
        if (selected.Count == 0)
        {
            return;
        }

        var message = selected.Count == 1
            ? Strings.Instance.Get("ConfirmDeleteSelectedOne")
            : Strings.Instance.Format("ConfirmDeleteSelectedMany", selected.Count);

        var result = MessageBox.Show(message, Strings.Instance.Get("ConfirmDeleteSelectedTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        foreach (var item in selected)
        {
            if (DeleteTranscriptFile(item))
            {
                TranscriptHistory.Remove(item);
                ClearViewedTranscriptIfDeleted(item);
            }
        }

        RefreshAppTranscriptHistory();
        RefreshFilteredTranscriptHistory();
    }

    [RelayCommand]
    private void DeleteSelectedTranscripts()
    {
        var selected = ExcludeItemsBeingSummarized(FilteredTranscriptHistory.Where(i => i.IsSelected).ToList());
        if (selected.Count == 0)
        {
            return;
        }

        var message = selected.Count == 1
            ? Strings.Instance.Get("ConfirmDeleteSelectedOne")
            : Strings.Instance.Format("ConfirmDeleteSelectedMany", selected.Count);

        var result = MessageBox.Show(message, Strings.Instance.Get("ConfirmDeleteSelectedTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        foreach (var item in selected)
        {
            if (DeleteTranscriptFile(item))
            {
                TranscriptHistory.Remove(item);
                ClearViewedTranscriptIfDeleted(item);
            }
        }

        RefreshAppTranscriptHistory();
        RefreshFilteredTranscriptHistory();
    }
}
