using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LoopbackTranscriber.Services;

namespace LoopbackTranscriber.Views;

public partial class ConfigView : UserControl
{
    public ConfigView()
    {
        InitializeComponent();
    }

    private void OnGitHubCardClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/pgomur") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn("ConfigView.OnGitHubCardClick: no se pudo abrir GitHub", ex);
        }
    }
}
