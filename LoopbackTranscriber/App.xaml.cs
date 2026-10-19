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

        private ServiceProvider? _services;
        private SingleInstance? _singleInstance;

        protected override void OnStartup(StartupEventArgs e)
        {
            CrashHandler.Install(this);

            base.OnStartup(e);

            _singleInstance = SingleInstance.TryAcquire();
            if (_singleInstance is null)
            {
                AppLog.Info("App.OnStartup: another instance is already running; asking it to show itself and exiting");
                Shutdown();
                return;
            }

            var settingsService = new SettingsService();
            settingsService.EnsureDefaultUserName();
            Strings.Instance.SetLanguage(settingsService.Load().UiLanguage);

            ApplicationThemeManager.Apply(ApplicationTheme.Dark);
            ApplicationAccentColorManager.Apply(AppAccentColor, ApplicationTheme.Dark);

            _services = AppServices.Build(settingsService);

            var window = _services.GetRequiredService<global::LoopbackTranscriber.MainWindow>();
            MainWindow = window;
            CrashHandler.Attach(_services.GetRequiredService<ViewModels.RecordingViewModel>(), window);
            _singleInstance.ListenForActivation(() => Dispatcher.InvokeAsync(window.ShowFromOtherInstance));
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
                AppLog.Warn("App.OnExit: failed to dispose services", ex);
            }

            _singleInstance?.Dispose();

            base.OnExit(e);
        }
    }
}
