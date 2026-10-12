using System.Windows;
using LoopbackTranscriber.Services;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace LoopbackTranscriber
{

    public partial class App : Application
    {
        private static readonly Color AppAccentColor = Color.FromRgb(0x6A, 0x2B, 0xD9);

        static App()
        {
            var commandLineArgs = Environment.GetCommandLineArgs();
            if (WorkerHost.IsWorkerInvocation(commandLineArgs))
            {
                Environment.Exit(WorkerHost.Run(commandLineArgs));
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ApplicationThemeManager.Apply(ApplicationTheme.Dark);
            ApplicationAccentColorManager.Apply(AppAccentColor, ApplicationTheme.Dark);
        }
    }
}
