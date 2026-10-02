using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
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
        private readonly MainViewModel _viewModel;
        private bool _isHistoryExpanded;
        private bool _isHistoryPageExpanded;

        public MainWindow()
        {
            var settingsService = new SettingsService();
            Strings.Instance.SetLanguage(settingsService.Load().UiLanguage);

            InitializeComponent();

            var whisper = new WhisperTranscriptionService();
            var summarizer = new SummarizationService();
            var transcriptHistoryService = new TranscriptHistoryService();
            var customAppsService = new CustomAppsService(settingsService);

            _viewModel = new MainViewModel(
                new TargetProcessWatcher(),
                new ProcessLoopbackRecorder(),
                new DeviceLoopbackRecorder(),
                new MicrophoneRecorder(),
                whisper,
                summarizer,
                settingsService,
                transcriptHistoryService,
                customAppsService);

            DataContext = _viewModel;

            ApplyTranslucency(settingsService.Load().TranslucentUi);
            UiPreferences.TranslucentUiChanged += OnTranslucentUiChanged;

            foreach (var target in _viewModel.Targets)
            {
                AppsNavigationView.MenuItems.Add(CreateTargetMenuItem(target));
            }

            _viewModel.Targets.CollectionChanged += OnTargetsCollectionChanged;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

            _viewModel.Start();
            Closed += (_, _) =>
            {
                _viewModel.Targets.CollectionChanged -= OnTargetsCollectionChanged;
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                UiPreferences.TranslucentUiChanged -= OnTranslucentUiChanged;
                _viewModel.Dispose();
            };
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.HasAppHistoryItems) &&
                !_viewModel.HasAppHistoryItems && _isHistoryExpanded)
            {
                _isHistoryExpanded = false;
                AnimateExpander(HistoryExpanderContent, HistoryExpanderChevronRotation, expand: false, maxContentHeight: 280);
            }
            else if (e.PropertyName == nameof(MainViewModel.HasHistoryItems) &&
                     !_viewModel.HasHistoryItems && _isHistoryPageExpanded)
            {
                _isHistoryPageExpanded = false;
                AnimateExpander(HistoryPageExpanderContent, HistoryPageExpanderChevronRotation, expand: false, maxContentHeight: 280);
            }
        }

        private void OnTranslucentUiChanged(object? sender, bool enabled) => ApplyTranslucency(enabled);

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

        private void OnTargetsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
            {
                foreach (TargetItemViewModel target in e.NewItems)
                {
                    AppsNavigationView.MenuItems.Add(CreateTargetMenuItem(target));
                }
            }
            else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems is not null)
            {
                foreach (TargetItemViewModel target in e.OldItems)
                {
                    var item = AppsNavigationView.MenuItems
                        .OfType<NavigationViewItem>()
                        .FirstOrDefault(i => ReferenceEquals(i.Tag, target));

                    if (item is not null)
                    {
                        AppsNavigationView.MenuItems.Remove(item);
                    }
                }
            }
        }

        private NavigationViewItem CreateTargetMenuItem(TargetItemViewModel target)
        {
            var symbol = target.App.Id == "teams" ? SymbolRegular.PeopleTeam24 : SymbolRegular.AppGeneric24;

            var icon = new SymbolIcon
            {
                Symbol = symbol,
                FontSize = 20,
                UseLayoutRounding = true,
                SnapsToDevicePixels = true
            };
            TextOptions.SetTextFormattingMode(icon, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(icon, TextRenderingMode.ClearType);
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);

            var statusDot = new Ellipse
            {
                Width = 8,
                Height = 8,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            statusDot.SetBinding(Shape.FillProperty, new Binding(nameof(TargetItemViewModel.IsRunning))
            {
                Source = target,
                Converter = new BoolToStatusBrushConverter()
            });

            var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            text.SetBinding(TextBlock.TextProperty, new Binding(nameof(TargetItemViewModel.DisplayName))
            {
                Source = target
            });

            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(statusDot);
            panel.Children.Add(text);

            var item = new NavigationViewItem
            {
                Tag = target,
                Icon = icon,
                Content = panel
            };

            item.SetBinding(NavigationViewItem.CommandProperty, new Binding(nameof(MainViewModel.SelectTargetCommand))
            {
                Source = _viewModel
            });
            item.CommandParameter = target;

            item.SetBinding(UIElement.IsEnabledProperty, new Binding(nameof(MainViewModel.IsRecording))
            {
                Source = _viewModel,
                Converter = new InverseBooleanConverter()
            });

            return item;
        }

        private void HistoryExpanderHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_viewModel.HasAppHistoryItems)
            {
                return;
            }

            _isHistoryExpanded = !_isHistoryExpanded;

            if (!_isHistoryExpanded)
            {
                _viewModel.ClearAppHistorySelection();
            }

            AnimateExpander(HistoryExpanderContent, HistoryExpanderChevronRotation, _isHistoryExpanded, maxContentHeight: 280);
        }

        private void HistoryPageExpanderHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_viewModel.HasHistoryItems)
            {
                return;
            }

            _isHistoryPageExpanded = !_isHistoryPageExpanded;

            if (!_isHistoryPageExpanded)
            {
                _viewModel.ClearHistorySelection();
            }

            AnimateExpander(HistoryPageExpanderContent, HistoryPageExpanderChevronRotation, _isHistoryPageExpanded, maxContentHeight: 280);
        }

        private static void AnimateExpander(FrameworkElement content, RotateTransform chevronRotation, bool expand, double maxContentHeight)
        {
            const int durationMs = 220;

            content.BeginAnimation(HeightProperty, null);

            var startHeight = content.ActualHeight;
            double targetHeight;

            if (expand)
            {
                content.Height = double.NaN;
                content.Measure(new Size(
                    content.ActualWidth > 0 ? content.ActualWidth : double.PositiveInfinity,
                    double.PositiveInfinity));
                targetHeight = System.Math.Min(content.DesiredSize.Height, maxContentHeight);
                content.Height = startHeight;
            }
            else
            {
                targetHeight = 0;
            }

            var heightAnimation = new DoubleAnimation
            {
                From = startHeight,
                To = targetHeight,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.HoldEnd
            };
            content.BeginAnimation(HeightProperty, heightAnimation);

            var rotateAnimation = new DoubleAnimation
            {
                To = expand ? 180.0 : 0.0,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            chevronRotation.BeginAnimation(RotateTransform.AngleProperty, rotateAnimation);
        }
    }
}
