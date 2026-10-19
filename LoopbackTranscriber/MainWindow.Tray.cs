using System.Windows;
using LoopbackTranscriber.Interop;
using LoopbackTranscriber.Services;
using LoopbackTranscriber.ViewModels;

namespace LoopbackTranscriber
{

    public partial class MainWindow
    {
        private void OnTrayLanguageChanged(object? sender, EventArgs e) => _trayIcon?.SetTooltip(_trayMenuViewModel.Tooltip);

        private void OnWindowStateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                _trayIcon?.SetTooltip(_trayMenuViewModel.Tooltip);
                _trayIcon?.Show();
                MemoryTrimmer.ReleaseInBackground();
                _backgroundTrimmer.Enter();
            }
            else
            {
                _stateBeforeMinimize = WindowState;

                if (!_hiddenToTray)
                {
                    _trayIcon?.Hide();
                    _backgroundTrimmer.Leave();
                }
            }
        }

        private void HideToTray()
        {
            if (_hiddenToTray)
            {
                return;
            }

            _hiddenToTray = true;
            Hide();
            MemoryTrimmer.ReleaseInBackground();
            _backgroundTrimmer.Enter();

            if (_trayIcon is null)
            {
                return;
            }

            _trayIcon.SetTooltip(_trayMenuViewModel.Tooltip);
            _trayIcon.Show();
            ShowTrayHintOnce();
        }

        private void ShowTrayHintOnce()
        {
            if (!_trayMenuViewModel.TryConsumeTrayHint())
            {
                return;
            }

            _trayIcon?.ShowBalloon(
                Strings.Instance.Get("TrayHintTitle"),
                Strings.Instance.Get("TrayHintBody"));
        }

        internal void ShowFromOtherInstance() => RestoreFromTray();

        private void RestoreFromTray()
        {
            _hiddenToTray = false;
            _trayIcon?.Hide();
            _backgroundTrimmer.Leave();

            if (!IsVisible)
            {
                Show();
            }

            if (WindowState == WindowState.Minimized)
            {
                WindowState = _stateBeforeMinimize;
            }

            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                if (User32.IsIconic(handle))
                {
                    User32.ShowWindow(handle, User32.SwRestore);
                }

                User32.SetForegroundWindow(handle);
            }

            Activate();
        }

        private void ExitFromTray() => TrayMenuViewModel.RunAction(ExitFromTrayAsync);

        private async Task ExitFromTrayAsync()
        {
            if (!await _exitWorkflow.ConfirmExitAsync(RestoreFromTray))
            {
                return;
            }

            _allowClose = true;
            Close();
        }

        private void ShowTrayMenu(int cursorX, int cursorY)
        {
            _trayMenu?.CloseMenu();

            var actions = new TrayMenuActions(
                RestoreFromTray,
                ToggleLiveTranslationFor,
                ExitFromTray,
                _liveWindow is not null ? _liveWindowTarget : null);

            var menu = new Views.TrayMenuWindow(_trayMenuViewModel.Build(actions));
            _trayMenu = menu;
            menu.Closed += (_, _) =>
            {
                if (ReferenceEquals(_trayMenu, menu))
                {
                    _trayMenu = null;
                }
            };

            menu.ShowAt(cursorX, cursorY);
        }
    }
}
