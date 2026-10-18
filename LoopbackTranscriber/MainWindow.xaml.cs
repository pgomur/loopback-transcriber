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
        private readonly ISettingsStore _settingsService;
        private readonly IRecordingHost _recordingHost;
        private readonly ILiveTranslationFactory _liveFactory;
        private TrayIconService? _trayIcon;
        private System.Threading.Timer? _backgroundTrimTimer;
        private Views.TrayMenuWindow? _trayMenu;
        private bool _allowClose;
        private bool _closingAfterCrash;
        private bool _hiddenToTray;
        private WindowState _stateBeforeMinimize = WindowState.Normal;

        public MainWindow(MainViewModel viewModel, ISettingsStore settingsService,
            IRecordingHost recordingHost, ILiveTranslationFactory liveFactory)
        {
            _settingsService = settingsService;
            _recordingHost = recordingHost;
            _liveFactory = liveFactory;
            _viewModel = viewModel;
            ApplyCalendarCulture(Strings.Instance.Language);

            InitializeComponent();

            DataContext = _viewModel;

            _translucentUiEnabled = settingsService.Load().TranslucentUi;
            ApplyTranslucency(_translucentUiEnabled);
            UiPreferences.TranslucentUiChanged += OnTranslucentUiChanged;

            foreach (var target in _viewModel.Targets.Items)
            {
                AppsNavigationView.MenuItems.Add(CreateTargetMenuItem(target));
            }

            _viewModel.Targets.Items.CollectionChanged += OnTargetsCollectionChanged;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.Recording.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.Targets.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.LiveTranslationToggleRequested += OnLiveTranslationToggleRequested;
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

                if (!_closingAfterCrash && _viewModel.Viewer.IsSummaryGenerating)
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
                _backgroundTrimTimer?.Dispose();
                _backgroundTrimTimer = null;
                _viewModel.Targets.Items.CollectionChanged -= OnTargetsCollectionChanged;
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                _viewModel.Recording.PropertyChanged -= OnViewModelPropertyChanged;
                _viewModel.Targets.PropertyChanged -= OnViewModelPropertyChanged;
                _viewModel.LiveTranslationToggleRequested -= OnLiveTranslationToggleRequested;
                _liveWindow?.Close();
                UiPreferences.TranslucentUiChanged -= OnTranslucentUiChanged;
                Strings.Instance.LanguageChanged -= OnAppLanguageChangedForCalendar;
            };
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.IsFirstRunActive))
            {
                ApplyFirstRunTitleBarOverrideIfNeeded();
            }
            else if (e.PropertyName is nameof(RecordingViewModel.IsRecording) or nameof(TargetsViewModel.SelectedTarget))
            {
                _trayIcon?.SetRecording(_viewModel.Recording.IsRecording);
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

        internal void CloseAfterCrash()
        {
            _closingAfterCrash = true;
            _allowClose = true;
            Close();
        }

        private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
        {
            _allowClose = true;
            _viewModel.Recording.PreserveInterruptedRecording(TimeSpan.FromSeconds(3));
        }
    }
}
