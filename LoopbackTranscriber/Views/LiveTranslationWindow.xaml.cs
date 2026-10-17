using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using LoopbackTranscriber.ViewModels;

namespace LoopbackTranscriber.Views;

public partial class LiveTranslationWindow : Window
{
    public LiveTranslationWindow()
    {
        InitializeComponent();
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnLinesScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange <= 0)
        {
            return;
        }

        var wasAtBottom = e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - e.ExtentHeightChange - 4;
        if (wasAtBottom)
        {
            LinesScroll.ScrollToBottom();
        }
    }

    private void OnOpacityClick(object sender, RoutedEventArgs e) => OpacityPopup.IsOpen = true;

    private CustomPopupPlacement[] OnOpacityPopupPlacement(Size popupSize, Size targetSize, Point offset) =>
        new[]
        {
            new CustomPopupPlacement(new Point(targetSize.Width - popupSize.Width, targetSize.Height), PopupPrimaryAxis.None)
        };

    private void OnOpacityPopupClosed(object? sender, EventArgs e) =>
        (DataContext as LiveTranslationViewModel)?.SaveWindowOpacity();

    protected override void OnClosed(EventArgs e)
    {
        (DataContext as LiveTranslationViewModel)?.SaveWindowOpacity();
        base.OnClosed(e);
    }

    private void OnResizeGripDragDelta(object sender, DragDeltaEventArgs e)
    {
        Width = Math.Max(MinWidth, ActualWidth + e.HorizontalChange);
        Height = Math.Max(MinHeight, ActualHeight + e.VerticalChange);
    }
}
