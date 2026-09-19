using System.Windows;
using LoopbackTranscriber.ViewModels;

namespace LoopbackTranscriber.Views
{

    public partial class ConfigWindow
    {
        private readonly ConfigViewModel _viewModel = new();

        public ConfigWindow()
        {
            InitializeComponent();
            DataContext = _viewModel;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.SaveCommand.Execute(null);
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
