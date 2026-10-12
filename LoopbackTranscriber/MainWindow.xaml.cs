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
        private readonly SettingsService _settingsService;
        private TrayIconService? _trayIcon;
        private Views.TrayMenuWindow? _trayMenu;
        private bool _allowClose;
        private bool _hiddenToTray;
        private WindowState _stateBeforeMinimize = WindowState.Normal;

        public MainWindow()
        {
            var settingsService = new SettingsService();
            _settingsService = settingsService;
            settingsService.EnsureDefaultUserName();
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

            _trayIcon = new TrayIconService(RestoreFromTray, ShowTrayMenu, GetTrayTooltip());
            StateChanged += OnWindowStateChanged;
            ContentRendered += OnFirstContentRendered;
            Strings.Instance.LanguageChanged += OnTrayLanguageChanged;
            Application.Current.SessionEnding += OnSessionEnding;

            Closing += (_, e) =>
            {
                if (!_allowClose)
                {
                    e.Cancel = true;
                    HideToTray();
                    return;
                }

                if (_viewModel.IsSummaryGenerating)
                {
                    var answer = System.Windows.MessageBox.Show(
                        Strings.Instance.Get("CloseDuringSummaryBody"),
                        Strings.Instance.Get("CloseDuringSummaryTitle"),
                        System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);

                    if (answer != System.Windows.MessageBoxResult.Yes)
                    {
                        e.Cancel = true;
                        _allowClose = false;
                    }
                }
            };
            Closed += (_, _) =>
            {
                StateChanged -= OnWindowStateChanged;
                Strings.Instance.LanguageChanged -= OnTrayLanguageChanged;
                Application.Current.SessionEnding -= OnSessionEnding;
                _trayMenu?.CloseMenu();
                _trayIcon?.Dispose();
                _trayIcon = null;
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
            else if (e.PropertyName is nameof(MainViewModel.IsRecording) or nameof(MainViewModel.SelectedTarget))
            {
                _trayIcon?.SetRecording(_viewModel.IsRecording);
                _trayIcon?.SetTooltip(GetTrayTooltip());
            }
        }

        private void OnFirstContentRendered(object? sender, EventArgs e)
        {
            ContentRendered -= OnFirstContentRendered;

            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(4));
                MemoryTrimmer.Release();
            });
        }

        private void OnTrayLanguageChanged(object? sender, EventArgs e) => _trayIcon?.SetTooltip(GetTrayTooltip());

        private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e) => _allowClose = true;

        private void OnWindowStateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                _trayIcon?.SetTooltip(GetTrayTooltip());
                _trayIcon?.Show();
                MemoryTrimmer.ReleaseInBackground();
            }
            else
            {
                _stateBeforeMinimize = WindowState;

                if (!_hiddenToTray)
                {
                    _trayIcon?.Hide();
                }
            }
        }

        private string GetTrayTooltip()
        {
            return _viewModel.IsRecording && _viewModel.SelectedTarget is { } target
                ? Strings.Instance.Format("TrayTooltipRecording", target.DisplayName)
                : Strings.Instance.Get("TrayTooltipIdle");
        }

        private void HideToTray()
        {
            if (_hiddenToTray)
            {
                return;
            }

            _hiddenToTray = true;
            Hide();
            MemoryTrimmer.ReleaseInBackground();

            if (_trayIcon is null)
            {
                return;
            }

            _trayIcon.SetTooltip(GetTrayTooltip());
            _trayIcon.Show();
            ShowTrayHintOnce();
        }

        private void ShowTrayHintOnce()
        {
            var settings = _settingsService.Load();
            if (settings.TrayHintShown)
            {
                return;
            }

            settings.TrayHintShown = true;
            _settingsService.Save(settings);

            _trayIcon?.ShowBalloon(
                Strings.Instance.Get("TrayHintTitle"),
                Strings.Instance.Get("TrayHintBody"));
        }

        private void RestoreFromTray()
        {
            _hiddenToTray = false;
            _trayIcon?.Hide();

            if (!IsVisible)
            {
                Show();
            }

            if (WindowState == WindowState.Minimized)
            {
                WindowState = _stateBeforeMinimize;
            }

            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                if (IsIconic(handle))
                {
                    ShowWindow(handle, SwRestore);
                }

                SetForegroundWindow(handle);
            }

            Activate();
        }

        private const int SwRestore = 9;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private void ExitFromTray()
        {
            if (_viewModel.IsRecording)
            {
                var answer = System.Windows.MessageBox.Show(
                    Strings.Instance.Get("ExitDuringRecordingBody"),
                    Strings.Instance.Get("ExitDuringRecordingTitle"),
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);

                if (answer != System.Windows.MessageBoxResult.Yes)
                {
                    return;
                }
            }

            _allowClose = true;
            Close();
        }

        private void ShowTrayMenu(int cursorX, int cursorY)
        {
            _trayMenu?.CloseMenu();

            var menu = new Views.TrayMenuWindow(BuildTrayMenu());
            _trayMenu = menu;
            menu.Closed += (_, _) =>
            {
                if (ReferenceEquals(_trayMenu, menu))
                {
                    _trayMenu = null;
                }
            };

            menu.ShowAt(cursorX, cursorY);
        }

        private IReadOnlyList<TrayMenuEntry> BuildTrayMenu()
        {
            var entries = new List<TrayMenuEntry>
            {
                new(Strings.Instance.Get("TrayOpen"), true, RestoreFromTray, Icon: "WindowNew20", IsDefault: true),
                TrayMenuEntry.Separator
            };

            if (!_viewModel.IsFirstRunActive)
            {
                if (_viewModel.IsRecording)
                {
                    var recordingName = _viewModel.SelectedTarget?.DisplayName ?? string.Empty;
                    entries.Add(new(
                        Strings.Instance.Format("TrayRecordingStatus", recordingName),
                        false,
                        null,
                        IsStatus: true,
                        LiveText: () => _viewModel.ElapsedText));
                    entries.Add(new(
                        Strings.Instance.Format("TrayStopRecording", recordingName),
                        _viewModel.CanToggleRecording,
                        () =>
                        {
                            RestoreFromTray();
                            RunTrayAction(_viewModel.StopRecordingFromTrayAsync);
                        },
                        Icon: Views.TrayMenuWindow.StopGlyph));
                }
                else
                {
                    foreach (var target in _viewModel.Targets.ToList())
                    {
                        var captured = target;
                        entries.Add(new(
                            Strings.Instance.Format("TrayStartRecording", captured.DisplayName),
                            _viewModel.CanStartRecordingFor(captured),
                            () => RunTrayAction(() => _viewModel.StartRecordingForAsync(captured)),
                            Icon: Views.TrayMenuWindow.RecordGlyph));
                    }

                    if (_viewModel.RecordDisabledReason is { } reason)
                    {
                        entries.Add(new(reason, false, null));
                    }
                }

                entries.Add(TrayMenuEntry.Separator);
            }

            entries.Add(new(Strings.Instance.Get("TrayExit"), true, ExitFromTray, Icon: "ArrowExit20"));
            return entries;
        }

        private static async void RunTrayAction(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                AppLog.Warn("MainWindow.RunTrayAction: fallo al ejecutar la accion de la bandeja", ex);
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
            var symbol = target.App.Id == "teams" ? SymbolRegular.PeopleTeam24 : SymbolRegular.AppsAddIn24;

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
