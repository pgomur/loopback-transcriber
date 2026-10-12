using System.Runtime;
using System.Runtime.InteropServices;

namespace LoopbackTranscriber.Services;

public static class MemoryTrimmer
{
    private static int _running;

    public static void ReleaseInBackground() => _ = Task.Run(Release);

    public static void Release()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            return;
        }

        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

            HeapCompact(GetProcessHeap(), 0);
            SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
        }
        catch (Exception ex)
        {
            AppLog.Warn("MemoryTrimmer.Release: no se pudo devolver la memoria al sistema", ex);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetProcessHeap();

    [DllImport("kernel32.dll")]
    private static extern UIntPtr HeapCompact(IntPtr hHeap, uint dwFlags);

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);
}
