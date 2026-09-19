using System.Windows;
using LoopbackTranscriber.ViewModels;
using LoopbackTranscriber.Views;

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

        private void ConfigButton_Click(object sender, RoutedEventArgs e)
        {
            var configWindow = new ConfigWindow { Owner = this };
            configWindow.ShowDialog();
        }
    }
}
