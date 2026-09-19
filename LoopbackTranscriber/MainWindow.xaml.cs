using LoopbackTranscriber.ViewModels;

namespace LoopbackTranscriber
{

    public partial class MainWindow
    {
        private readonly MainViewModel _viewModel = new();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = _viewModel;
            _viewModel.Start();
            Closed += (_, _) => _viewModel.Dispose();
        }
    }
}
