using System.Windows;
using Wpf.Ui.Appearance;

// pgomur
namespace LoopbackTranscriber
{

    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ApplicationThemeManager.Apply(ApplicationTheme.Dark);
        }
    }
}
