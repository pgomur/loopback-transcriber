using LoopbackTranscriber.Interop;
using LoopbackTranscriber.Services;

namespace LoopbackTranscriber;

internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\LoopbackTranscriber-B6E1F6C4-6E8B-4A6E-9C2A-6C7B9A9E9C3D";
    private const string ActivateEventName = @"Local\LoopbackTranscriber-B6E1F6C4-6E8B-4A6E-9C2A-6C7B9A9E9C3D-Activate";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activateEvent;
    private RegisteredWaitHandle? _registration;

    private SingleInstance(Mutex mutex, EventWaitHandle activateEvent)
    {
        _mutex = mutex;
        _activateEvent = activateEvent;
    }

    public static SingleInstance? TryAcquire()
    {
        var activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        var mutex = new Mutex(false, MutexName);

        if (TryTake(mutex))
        {
            return new SingleInstance(mutex, activateEvent);
        }

        User32.AllowSetForegroundWindow(User32.AsfwAny);
        activateEvent.Set();

        mutex.Dispose();
        activateEvent.Dispose();
        return null;
    }

    public void ListenForActivation(Action onActivate)
    {
        _registration = ThreadPool.RegisterWaitForSingleObject(_activateEvent, (_, _) =>
        {
            try
            {
                onActivate();
            }
            catch (Exception ex)
            {
                AppLog.Warn("SingleInstance: failed to show the window at another instance's request", ex);
            }
        }, null, Timeout.Infinite, executeOnlyOnce: false);
    }

    private static bool TryTake(Mutex mutex)
    {
        try
        {
            return mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            AppLog.Info("SingleInstance: the previous instance exited without releasing the mutex; taking over");
            return true;
        }
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _activateEvent.Dispose();

        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException ex)
        {
            AppLog.Warn("SingleInstance: could not release the mutex", ex);
        }

        _mutex.Dispose();
    }
}
