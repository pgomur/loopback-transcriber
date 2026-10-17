using System.Windows;
using LoopbackTranscriber.Services;
using Microsoft.Extensions.DependencyInjection;
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

        private ServiceProvider? _services;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var settingsService = new SettingsService();
            settingsService.EnsureDefaultUserName();
            Strings.Instance.SetLanguage(settingsService.Load().UiLanguage);

            ApplicationThemeManager.Apply(ApplicationTheme.Dark);
            ApplicationAccentColorManager.Apply(AppAccentColor, ApplicationTheme.Dark);

            _services = AppServices.Build(settingsService);

            var window = _services.GetRequiredService<global::LoopbackTranscriber.MainWindow>();
            MainWindow = window;
            window.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _services?.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Warn("App.OnExit: fallo al liberar los servicios", ex);
            }

            base.OnExit(e);
        }
    }
}
