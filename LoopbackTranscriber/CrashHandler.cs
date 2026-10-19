using System.Windows;
using System.Windows.Threading;
using LoopbackTranscriber.Services;
using LoopbackTranscriber.ViewModels;

namespace LoopbackTranscriber;

internal static class CrashHandler
{
    private static readonly TimeSpan PreserveTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan UiStartTimeout = TimeSpan.FromSeconds(2);

    private enum RecordingRescue
    {
        NotRecording,
        Preserved,
        Lost
    }

    private static int _crashing;
    private static RecordingViewModel? _recording;
    private static MainWindow? _window;

    public static void Install(Application app)
    {
        app.DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    public static void Attach(RecordingViewModel recording, MainWindow window)
    {
        _recording = recording;
        _window = window;
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;

        if (Interlocked.Exchange(ref _crashing, 1) != 0)
        {
            AppLog.Error($"CrashHandler: unhandled exception on the UI thread while handling another one: {e.Exception}");
            return;
        }

        AppLog.Error($"CrashHandler: unhandled exception on the UI thread: {e.Exception}");

        var rescue = RescueRecording();
        ShowCrashMessage(rescue);
        CloseApplication();
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        AppLog.Error($"CrashHandler: unhandled exception on a background thread (terminating={e.IsTerminating}): {e.ExceptionObject}");

        if (Interlocked.Exchange(ref _crashing, 1) != 0)
        {
            return;
        }

        var rescue = RescueRecordingOnUiThread();
        ShowCrashMessage(rescue);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLog.Warn("CrashHandler: unobserved task exception", e.Exception);
        e.SetObserved();
    }

    private static RecordingRescue RescueRecording()
    {
        var recording = _recording;
        if (recording is null || !recording.IsRecording)
        {
            return RecordingRescue.NotRecording;
        }

        try
        {
            return recording.PreserveInterruptedRecording(PreserveTimeout)
                ? RecordingRescue.Preserved
                : RecordingRescue.Lost;
        }
        catch (Exception ex)
        {
            AppLog.Warn("CrashHandler: failed to preserve the recording in progress", ex);
            return RecordingRescue.Lost;
        }
    }

    private static RecordingRescue RescueRecordingOnUiThread()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return RescueRecording();
        }

        RecordingRescue? rescue = null;
        try
        {
            dispatcher.Invoke(() => { rescue = RescueRecording(); }, DispatcherPriority.Send, CancellationToken.None, UiStartTimeout);
        }
        catch (Exception ex)
        {
            AppLog.Warn("CrashHandler: could not preserve the recording from the UI thread", ex);
        }

        if (rescue is null)
        {
            AppLog.Error("CrashHandler: the UI thread did not respond in time to preserve the recording");
        }

        return rescue ?? (_recording?.IsRecording == true ? RecordingRescue.Lost : RecordingRescue.NotRecording);
    }

    private static void ShowCrashMessage(RecordingRescue rescue)
    {
        var bodyKey = rescue switch
        {
            RecordingRescue.Preserved => "CrashBodyPreserved",
            RecordingRescue.Lost => "CrashBodyLost",
            _ => "CrashBody"
        };

        try
        {
            MessageBox.Show(Strings.Instance.Format(bodyKey, AppLog.LogPath), Strings.Instance.Get("CrashTitle"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            AppLog.Warn("CrashHandler: could not show the error dialog", ex);
        }
    }

    private static void CloseApplication()
    {
        try
        {
            _window?.CloseAfterCrash();
        }
        catch (Exception ex)
        {
            AppLog.Warn("CrashHandler: failed to close the main window", ex);
        }

        try
        {
            Application.Current?.Shutdown(1);
        }
        catch (Exception ex)
        {
            AppLog.Warn("CrashHandler: failed to shut down the application; forcing exit", ex);
            Environment.Exit(1);
        }
    }
}
