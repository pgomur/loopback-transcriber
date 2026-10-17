using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using LoopbackTranscriber.Converters;
using LoopbackTranscriber.Services;
using LoopbackTranscriber.ViewModels;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace LoopbackTranscriber
{

    public partial class MainWindow
    {
        private void OnTranslucentUiChanged(object? sender, bool enabled)
        {
            _translucentUiEnabled = enabled;

            if (!_viewModel.IsFirstRunActive)
            {
                ApplyTranslucency(enabled);
            }
        }

        private void ApplyTranslucency(bool enabled)
        {
            WindowBackdropType = enabled ? WindowBackdropType.Mica : WindowBackdropType.None;

            var background = enabled
                ? System.Windows.Media.Brushes.Transparent
                : (System.Windows.Media.Brush)FindResource("ApplicationBackgroundBrush");

            RootTitleBar.Background = background;
            ContentBackgroundGrid.Background = background;
            AppsNavigationView.Background = background;
        }

        private void ApplyFirstRunTitleBarOverrideIfNeeded()
        {
            if (_viewModel.IsFirstRunActive)
            {
                WindowBackdropType = WindowBackdropType.None;
                RootTitleBar.Background = FirstRunTitleBarBrush;
            }
            else
            {
                ApplyTranslucency(_translucentUiEnabled);
            }
        }
    }
}
