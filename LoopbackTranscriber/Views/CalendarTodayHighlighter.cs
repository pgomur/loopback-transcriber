using System.Windows;
using System.Windows.Media;

namespace LoopbackTranscriber.Views;

internal static class CalendarTodayHighlighter
{
    public static void OnCalendarOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement ownerElement)
        {
            return;
        }

        const int maxAttempts = 10;

        void HighlightToday(int attempt)
        {
            var popup = FindVisualChild<System.Windows.Controls.Primitives.Popup>(ownerElement);
            var popupContent = popup?.Child as DependencyObject;
            var found = false;

            if (popupContent is not null)
            {
                var todayBrush = Application.Current.TryFindResource("AccentFillColorTertiaryBrush") as Brush
                    ?? new SolidColorBrush(Color.FromRgb(0x4A, 0x1E, 0x98));
                var todayForeground = Brushes.White;

                foreach (var dayButton in FindVisualChildren<System.Windows.Controls.Primitives.CalendarDayButton>(popupContent))
                {
                    if (dayButton.IsToday)
                    {
                        dayButton.Background = todayBrush;
                        dayButton.Foreground = todayForeground;
                        dayButton.FontWeight = FontWeights.Bold;
                        found = true;
                    }
                }
            }

            if (!found && attempt < maxAttempts)
            {
                ownerElement.Dispatcher.BeginInvoke(new Action(() => HighlightToday(attempt + 1)), System.Windows.Threading.DispatcherPriority.ContextIdle);
            }
        }

        ownerElement.Dispatcher.BeginInvoke(new Action(() => HighlightToday(0)), System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var result = FindVisualChild<T>(child);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    private static System.Collections.Generic.IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                yield return typedChild;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
