using System.Windows;
using LoopbackTranscriber.ViewModels;
using LoopbackTranscriber.Views;

namespace LoopbackTranscriber;

public partial class MainWindow
{
    private LiveTranslationWindow? _liveWindow;
    private TargetItemViewModel? _liveWindowTarget;

    private void OnLiveTranslationToggleRequested(object? sender, TargetItemViewModel target) =>
        ToggleLiveTranslationFor(target);

    private void ToggleLiveTranslationFor(TargetItemViewModel target)
    {
        var closeOnly = _liveWindow is not null && ReferenceEquals(_liveWindowTarget, target);

        _liveWindow?.Close();

        if (closeOnly)
        {
            return;
        }

        var liveViewModel = _liveFactory.Create(target, RestoreFromTray);
        var window = new LiveTranslationWindow { DataContext = liveViewModel };
        var workArea = SystemParameters.WorkArea;
        window.Height = Math.Min(window.Height, Math.Max(window.MinHeight, workArea.Height - 40));
        window.Left = workArea.Right - window.Width + 4;
        window.Top = workArea.Top + (workArea.Height - window.Height) / 2;
        window.Closed += (_, _) =>
        {
            liveViewModel.Dispose();

            if (ReferenceEquals(_liveWindow, window))
            {
                _liveWindow = null;
                _liveWindowTarget = null;
            }
        };

        _liveWindow = window;
        _liveWindowTarget = target;
        window.Show();
    }
}
