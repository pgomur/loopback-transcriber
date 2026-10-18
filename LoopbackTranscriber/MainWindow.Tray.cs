using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using LoopbackTranscriber.Converters;
using LoopbackTranscriber.Services;
using LoopbackTranscriber.ViewModels;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace LoopbackTranscriber
{

    public partial class MainWindow
    {
        private void OnTrayLanguageChanged(object? sender, EventArgs e) => _trayIcon?.SetTooltip(GetTrayTooltip());

        private void OnWindowStateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                _trayIcon?.SetTooltip(GetTrayTooltip());
                _trayIcon?.Show();
                MemoryTrimmer.ReleaseInBackground();
                EnterBackgroundMode();
            }
            else
            {
                _stateBeforeMinimize = WindowState;

                if (!_hiddenToTray)
                {
                    _trayIcon?.Hide();
                    LeaveBackgroundMode();
                }
            }
        }

        private void EnterBackgroundMode()
        {
            var interval = TimeSpan.FromMinutes(5);
            if (_backgroundTrimTimer is null)
            {
                _backgroundTrimTimer = new System.Threading.Timer(_ => MemoryTrimmer.TrimWorkingSet(), null, interval, interval);
            }
            else
            {
                _backgroundTrimTimer.Change(interval, interval);
            }
        }

        private void LeaveBackgroundMode()
        {
            _backgroundTrimTimer?.Change(System.Threading.Timeout.InfiniteTimeSpan, System.Threading.Timeout.InfiniteTimeSpan);
        }

        private string GetTrayTooltip()
        {
            return _viewModel.Recording.IsRecording && _viewModel.Targets.SelectedTarget is { } target
                ? Strings.Instance.Format("TrayTooltipRecording", target.DisplayName)
                : Strings.Instance.Get("TrayTooltipIdle");
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
            EnterBackgroundMode();

            if (_trayIcon is null)
            {
                return;
            }

            _trayIcon.SetTooltip(GetTrayTooltip());
            _trayIcon.Show();
            ShowTrayHintOnce();
        }

        private void ShowTrayHintOnce()
        {
            var settings = _settingsService.Load();
            if (settings.TrayHintShown)
            {
                return;
            }

            settings.TrayHintShown = true;
            _settingsService.Save(settings);

            _trayIcon?.ShowBalloon(
                Strings.Instance.Get("TrayHintTitle"),
                Strings.Instance.Get("TrayHintBody"));
        }

        private void RestoreFromTray()
        {
            _hiddenToTray = false;
            _trayIcon?.Hide();
            LeaveBackgroundMode();

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
                if (IsIconic(handle))
                {
                    ShowWindow(handle, SwRestore);
                }

                SetForegroundWindow(handle);
            }

            Activate();
        }

        private const int SwRestore = 9;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private bool _exitPending;

        private void ExitFromTray() => RunTrayAction(ExitFromTrayAsync);

        private async Task ExitFromTrayAsync()
        {
            if (_exitPending)
            {
                return;
            }

            var recording = _viewModel.Recording;

            if (recording.IsRecording)
            {
                var answer = System.Windows.MessageBox.Show(
                    Strings.Instance.Get("ExitDuringRecordingBody"),
                    Strings.Instance.Get("ExitDuringRecordingTitle"),
                    System.Windows.MessageBoxButton.YesNoCancel, System.Windows.MessageBoxImage.Warning);

                if (answer == System.Windows.MessageBoxResult.Yes)
                {
                    _exitPending = true;
                    try
                    {
                        RestoreFromTray();
                        await _recordingHost.StopRecordingAsync();
                    }
                    finally
                    {
                        _exitPending = false;
                    }
                }
                else if (answer != System.Windows.MessageBoxResult.No)
                {
                    return;
                }

                if (recording.IsRecording)
                {
                    recording.PreserveInterruptedRecording(TimeSpan.FromSeconds(10));
                }
            }
            else if (recording.IsBusy)
            {
                var answer = System.Windows.MessageBox.Show(
                    Strings.Instance.Get("ExitWhileProcessingBody"),
                    Strings.Instance.Get("ExitWhileProcessingTitle"),
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);

                if (answer != System.Windows.MessageBoxResult.Yes)
                {
                    return;
                }

                _exitPending = true;
                try
                {
                    RestoreFromTray();
                    await WaitUntilNotBusyAsync(recording);
                }
                finally
                {
                    _exitPending = false;
                }
            }

            _allowClose = true;
            Close();
        }

        private static Task WaitUntilNotBusyAsync(RecordingViewModel recording)
        {
            if (!recording.IsBusy)
            {
                return Task.CompletedTask;
            }

            var completion = new TaskCompletionSource();

            void OnChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(RecordingViewModel.IsBusy) && !recording.IsBusy)
                {
                    recording.PropertyChanged -= OnChanged;
                    completion.TrySetResult();
                }
            }

            recording.PropertyChanged += OnChanged;
            return completion.Task;
        }

        private void ShowTrayMenu(int cursorX, int cursorY)
        {
            _trayMenu?.CloseMenu();

            var menu = new Views.TrayMenuWindow(BuildTrayMenu());
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

        private IReadOnlyList<TrayMenuEntry> BuildTrayMenu()
        {
            var entries = new List<TrayMenuEntry>
            {
                new(Strings.Instance.Get("TrayOpen"), true, RestoreFromTray, Icon: "WindowNew20", IsDefault: true),
                TrayMenuEntry.Separator
            };

            if (!_viewModel.IsFirstRunActive)
            {
                if (_viewModel.Recording.IsRecording)
                {
                    var recordingName = _viewModel.Targets.SelectedTarget?.DisplayName ?? string.Empty;
                    entries.Add(new(
                        Strings.Instance.Format("TrayRecordingStatus", recordingName),
                        false,
                        null,
                        IsStatus: true,
                        LiveText: () => _viewModel.Recording.ElapsedText));
                    entries.Add(new(
                        Strings.Instance.Format("TrayStopRecording", recordingName),
                        _viewModel.Recording.CanToggleRecording,
                        () =>
                        {
                            RestoreFromTray();
                            RunTrayAction(_recordingHost.StopRecordingAsync);
                        },
                        Icon: Views.TrayMenuWindow.StopGlyph));
                }
                else
                {
                    foreach (var target in _viewModel.Targets.Items.ToList())
                    {
                        var captured = target;
                        entries.Add(new(
                            Strings.Instance.Format("TrayStartRecording", captured.DisplayName),
                            _recordingHost.CanStartRecordingFor(captured),
                            () => RunTrayAction(() => _recordingHost.StartRecordingForAsync(captured)),
                            Icon: Views.TrayMenuWindow.RecordGlyph));
                    }

                    if (_viewModel.Recording.RecordDisabledReason is { } reason)
                    {
                        entries.Add(new(reason, false, null));
                    }
                }

                if (_liveWindow is not null && _liveWindowTarget is { } liveTarget)
                {
                    entries.Add(new(
                        Strings.Instance.Format("TrayLiveTranslationClose", liveTarget.DisplayName),
                        true,
                        () => ToggleLiveTranslationFor(liveTarget),
                        Icon: "Translate24"));
                }
                else
                {
                    foreach (var target in _viewModel.Targets.Items.ToList())
                    {
                        var capturedTarget = target;
                        entries.Add(new(
                            Strings.Instance.Format("TrayLiveTranslationOpen", capturedTarget.DisplayName),
                            capturedTarget.IsRunning,
                            () => ToggleLiveTranslationFor(capturedTarget),
                            Icon: "Translate24"));
                    }
                }

                entries.Add(TrayMenuEntry.Separator);
            }

            entries.Add(new(Strings.Instance.Get("TrayExit"), true, ExitFromTray, Icon: "ArrowExit20"));
            return entries;
        }

        private static async void RunTrayAction(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                AppLog.Warn("MainWindow.RunTrayAction: fallo al ejecutar la accion de la bandeja", ex);
            }
        }
    }
}
