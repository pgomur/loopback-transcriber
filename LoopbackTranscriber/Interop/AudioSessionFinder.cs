using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using LoopbackTranscriber.Models;

namespace LoopbackTranscriber.Interop;

internal static partial class AudioSessionFinder
{
    private const int CLSCTX_ALL = 23;

    public static uint FindActiveProcessId(IReadOnlyList<string> candidateProcessNames)
    {
        foreach (var role in new[] { ERole.Console, ERole.Communications, ERole.Multimedia })
        {
            var pid = TryFindOnRole(candidateProcessNames, role);
            if (pid != 0)
            {
                return pid;
            }
        }

        return 0;
    }

    private static uint TryFindOnRole(IReadOnlyList<string> candidateProcessNames, ERole role)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioSessionManager2? sessionManager = null;
        IAudioSessionEnumerator? sessionEnumerator = null;

        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();

            if (enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, role, out device) < 0 || device is null)
            {
                return 0;
            }

            var iid = typeof(IAudioSessionManager2).GUID;
            if (device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var sessionManagerObj) < 0 ||
                sessionManagerObj is not IAudioSessionManager2 manager)
            {
                return 0;
            }

            sessionManager = manager;

            if (sessionManager.GetSessionEnumerator(out sessionEnumerator) < 0 || sessionEnumerator is null)
            {
                return 0;
            }

            if (sessionEnumerator.GetCount(out var count) < 0)
            {
                return 0;
            }

            for (var i = 0; i < count; i++)
            {
                if (TryMatchSession(sessionEnumerator, i, candidateProcessNames, out var pid))
                {
                    return pid;
                }
            }

            return 0;
        }
        catch (Exception ex) when (ex is COMException or InvalidComObjectException or InvalidCastException)
        {
            return 0;
        }
        finally
        {
            if (sessionEnumerator is not null)
            {
                Marshal.ReleaseComObject(sessionEnumerator);
            }

            if (sessionManager is not null)
            {
                Marshal.ReleaseComObject(sessionManager);
            }

            if (device is not null)
            {
                Marshal.ReleaseComObject(device);
            }

            if (enumerator is not null)
            {
                Marshal.ReleaseComObject(enumerator);
            }
        }
    }

    private static bool TryMatchSession(IAudioSessionEnumerator sessionEnumerator, int index,
        IReadOnlyList<string> candidateProcessNames, out uint pid)
    {
        pid = 0;

        if (sessionEnumerator.GetSession(index, out var session) < 0 || session is null)
        {
            return false;
        }

        try
        {
            if (session.IsSystemSoundsSession() == 0)
            {
                return false;
            }

            if (session.GetState(out var state) < 0 || state != AudioSessionState.Active)
            {
                return false;
            }

            if (session.GetProcessId(out var sessionPid) < 0 || sessionPid == 0)
            {
                return false;
            }

            string processName;
            try
            {
                using var proc = Process.GetProcessById((int)sessionPid);
                processName = proc.ProcessName;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return false;
            }

            foreach (var candidate in candidateProcessNames)
            {
                if (string.Equals(processName, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    pid = sessionPid;
                    return true;
                }
            }

            return false;
        }
        finally
        {
            Marshal.ReleaseComObject(session);
        }
    }
}
