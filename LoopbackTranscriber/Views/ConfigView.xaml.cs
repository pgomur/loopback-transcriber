using System.Diagnostics;
using System.IO;
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
        ThirdPartyLink.Visibility = File.Exists(ThirdPartyNoticesPath) ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string ThirdPartyNoticesPath =>
        Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt");

    private void OnThirdPartyLicensesClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(ThirdPartyNoticesPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn("ConfigView.OnThirdPartyLicensesClick: no se pudo abrir el fichero de licencias", ex);
        }
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
