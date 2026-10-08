using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace LoopbackTranscriber
{

    public partial class App : Application
    {
        private static readonly Color AppAccentColor = Color.FromRgb(0x6A, 0x2B, 0xD9);

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ApplicationThemeManager.Apply(ApplicationTheme.Dark);
            ApplicationAccentColorManager.Apply(AppAccentColor, ApplicationTheme.Dark);
        }
    }
}
