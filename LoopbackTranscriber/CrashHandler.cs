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
            AppLog.Error($"CrashHandler: excepcion no controlada en el hilo de la interfaz mientras se gestionaba otra: {e.Exception}");
            return;
        }

        AppLog.Error($"CrashHandler: excepcion no controlada en el hilo de la interfaz: {e.Exception}");

        var rescue = RescueRecording();
        ShowCrashMessage(rescue);
        CloseApplication();
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        AppLog.Error($"CrashHandler: excepcion no controlada en un hilo de fondo (terminando={e.IsTerminating}): {e.ExceptionObject}");

        if (Interlocked.Exchange(ref _crashing, 1) != 0)
        {
            return;
        }

        var rescue = RescueRecordingOnUiThread();
        ShowCrashMessage(rescue);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLog.Warn("CrashHandler: excepcion no observada en una tarea", e.Exception);
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
            AppLog.Warn("CrashHandler: fallo al conservar la grabacion en curso", ex);
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
            AppLog.Warn("CrashHandler: no se pudo conservar la grabacion desde el hilo de la interfaz", ex);
        }

        if (rescue is null)
        {
            AppLog.Error("CrashHandler: el hilo de la interfaz no respondio a tiempo para conservar la grabacion");
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
            AppLog.Warn("CrashHandler: no se pudo mostrar el aviso de error", ex);
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
            AppLog.Warn("CrashHandler: fallo al cerrar la ventana principal", ex);
        }

        try
        {
            Application.Current?.Shutdown(1);
        }
        catch (Exception ex)
        {
            AppLog.Warn("CrashHandler: fallo al cerrar la aplicacion; se fuerza la salida", ex);
            Environment.Exit(1);
        }
    }
}
