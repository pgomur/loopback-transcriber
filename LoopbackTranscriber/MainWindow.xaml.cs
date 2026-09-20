using LoopbackTranscriber.Services;
using LoopbackTranscriber.ViewModels;

namespace LoopbackTranscriber
{

    public partial class MainWindow
    {
        private readonly MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();

            var settingsService = new SettingsService();
            var whisper = new WhisperTranscriptionService();
            var transcriptHistoryService = new TranscriptHistoryService();

            _viewModel = new MainViewModel(
                new TargetProcessWatcher(),
                new ProcessLoopbackRecorder(),
                new DeviceLoopbackRecorder(),
                whisper,
                settingsService,
                transcriptHistoryService);

            DataContext = _viewModel;
            _viewModel.Start();
            Closed += (_, _) => _viewModel.Dispose();
        }
    }
}
