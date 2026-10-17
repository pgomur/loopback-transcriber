using System.Windows;
using System.Windows.Controls;

namespace LoopbackTranscriber.Views;

public partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();
    }

    private void HistoryDatePicker_CalendarOpened(object? sender, RoutedEventArgs e) =>
        CalendarTodayHighlighter.OnCalendarOpened(sender, e);
}
