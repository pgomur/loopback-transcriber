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
        private System.Threading.Timer? _backgroundTrimTimer;
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
                new CaptureCoordinator(new ProcessLoopbackRecorder(), new DeviceLoopbackRecorder(), new MicrophoneRecorder()),
                whisper,
                new RecordingProcessor(whisper, new NaudioAudioPreparer()),
                summarizer,
                settingsService,
                transcriptHistoryService,
                customAppsService,
                new WpfDialogService());

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
                _backgroundTrimTimer?.Dispose();
                _backgroundTrimTimer = null;
                _viewModel.Targets.CollectionChanged -= OnTargetsCollectionChanged;
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                _viewModel.LiveTranslationToggleRequested -= OnLiveTranslationToggleRequested;
                _liveWindow?.Close();
                UiPreferences.TranslucentUiChanged -= OnTranslucentUiChanged;
                Strings.Instance.LanguageChanged -= OnAppLanguageChangedForCalendar;
                _viewModel.Dispose();
            };
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

        private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e) => _allowClose = true;
    }
}
