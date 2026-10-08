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
        private static readonly Brush FirstRunTitleBarBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D));

        private readonly MainViewModel _viewModel;
        private bool _translucentUiEnabled;

        public MainWindow()
        {
            var settingsService = new SettingsService();
            Strings.Instance.SetLanguage(settingsService.Load().UiLanguage);
            ApplyCalendarCulture(Strings.Instance.Language);

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

            _translucentUiEnabled = settingsService.Load().TranslucentUi;
            ApplyTranslucency(_translucentUiEnabled);
            UiPreferences.TranslucentUiChanged += OnTranslucentUiChanged;

            foreach (var target in _viewModel.Targets)
            {
                AppsNavigationView.MenuItems.Add(CreateTargetMenuItem(target));
            }

            _viewModel.Targets.CollectionChanged += OnTargetsCollectionChanged;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            Strings.Instance.LanguageChanged += OnAppLanguageChangedForCalendar;

            ApplyFirstRunTitleBarOverrideIfNeeded();

            _viewModel.Start();
            Closing += (_, e) =>
            {
                if (_viewModel.IsSummaryGenerating)
                {
                    var answer = System.Windows.MessageBox.Show(
                        Strings.Instance.Get("CloseDuringSummaryBody"),
                        Strings.Instance.Get("CloseDuringSummaryTitle"),
                        System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);

                    if (answer != System.Windows.MessageBoxResult.Yes)
                    {
                        e.Cancel = true;
                    }
                }
            };
            Closed += (_, _) =>
            {
                _viewModel.Targets.CollectionChanged -= OnTargetsCollectionChanged;
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                UiPreferences.TranslucentUiChanged -= OnTranslucentUiChanged;
                Strings.Instance.LanguageChanged -= OnAppLanguageChangedForCalendar;
                _viewModel.Dispose();
            };
        }

        private void OnAppLanguageChangedForCalendar(object? sender, EventArgs e) => ApplyCalendarCulture(Strings.Instance.Language);

        private static readonly System.Collections.Generic.Dictionary<string, string> LanguageToCulture = new()
        {
            ["es"] = "es-ES",
            ["en"] = "en-US",
            ["pt"] = "pt-PT",
            ["de"] = "de-DE",
            ["ru"] = "ru-RU",
        };

        private void ApplyCalendarCulture(string language)
        {
            var cultureName = LanguageToCulture.TryGetValue(language, out var mapped) ? mapped : "en-US";

            try
            {
                var culture = new CultureInfo(cultureName);
                System.Threading.Thread.CurrentThread.CurrentCulture = culture;
                System.Threading.Thread.CurrentThread.CurrentUICulture = culture;

                Language = XmlLanguage.GetLanguage(culture.IetfLanguageTag);
            }
            catch (CultureNotFoundException)
            {
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.IsFirstRunActive))
            {
                ApplyFirstRunTitleBarOverrideIfNeeded();
            }
        }

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

            item.SetBinding(NavigationViewItem.IsActiveProperty, new Binding(nameof(TargetItemViewModel.IsActive))
            {
                Source = target
            });

            return item;
        }

        private void HistoryDatePicker_CalendarOpened(object? sender, RoutedEventArgs e)
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
                    Dispatcher.BeginInvoke(new Action(() => HighlightToday(attempt + 1)), System.Windows.Threading.DispatcherPriority.ContextIdle);
                }
            }

            Dispatcher.BeginInvoke(new Action(() => HighlightToday(0)), System.Windows.Threading.DispatcherPriority.ContextIdle);
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
}
