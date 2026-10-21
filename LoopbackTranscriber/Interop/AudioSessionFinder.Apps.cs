using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using LoopbackTranscriber.Models;

namespace LoopbackTranscriber.Interop;

internal static partial class AudioSessionFinder
{
    public static IReadOnlyList<DiscoveredApp> ListRunningApps()
    {
        var currentProcessName = Process.GetCurrentProcess().ProcessName;
        var seenDeviceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var byProcessName = new Dictionary<string, DiscoveredApp>(StringComparer.OrdinalIgnoreCase);

        foreach (var role in new[] { ERole.Console, ERole.Communications, ERole.Multimedia })
        {
            CollectRunningAppsOnRole(role, currentProcessName, seenDeviceIds, byProcessName);
        }

        return byProcessName.Values
            .OrderBy(app => app.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void CollectRunningAppsOnRole(ERole role, string currentProcessName,
        HashSet<string> seenDeviceIds, Dictionary<string, DiscoveredApp> byProcessName)
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
                return;
            }

            if (device.GetId(out var deviceId) >= 0 && !string.IsNullOrEmpty(deviceId))
            {
                if (!seenDeviceIds.Add(deviceId))
                {
                    return;
                }
            }

            var iid = typeof(IAudioSessionManager2).GUID;
            if (device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var sessionManagerObj) < 0 ||
                sessionManagerObj is not IAudioSessionManager2 manager)
            {
                return;
            }

            sessionManager = manager;

            if (sessionManager.GetSessionEnumerator(out sessionEnumerator) < 0 || sessionEnumerator is null)
            {
                return;
            }

            if (sessionEnumerator.GetCount(out var count) < 0)
            {
                return;
            }

            for (var i = 0; i < count; i++)
            {
                CollectSession(sessionEnumerator, i, currentProcessName, byProcessName);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidComObjectException or InvalidCastException)
        {
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

    private static void CollectSession(IAudioSessionEnumerator sessionEnumerator, int index,
        string currentProcessName, Dictionary<string, DiscoveredApp> byProcessName)
    {
        if (sessionEnumerator.GetSession(index, out var session) < 0 || session is null)
        {
            return;
        }

        try
        {
            if (session.IsSystemSoundsSession() == 0)
            {
                return;
            }

            if (session.GetState(out var state) < 0 || state == AudioSessionState.Expired)
            {
                return;
            }

            if (session.GetProcessId(out var sessionPid) < 0 || sessionPid == 0)
            {
                return;
            }

            string processName;
            try
            {
                using var proc = Process.GetProcessById((int)sessionPid);
                processName = proc.ProcessName;

                if (string.Equals(processName, currentProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (byProcessName.ContainsKey(processName))
                {
                    return;
                }

                byProcessName[processName] = new DiscoveredApp
                {
                    ProcessName = processName,
                    DisplayName = GetFriendlyName(proc) ?? processName
                };
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
        }
        finally
        {
            Marshal.ReleaseComObject(session);
        }
    }

    private static string? GetFriendlyName(Process process)
    {
        try
        {
            var fileName = process.MainModule?.FileName;
            if (string.IsNullOrEmpty(fileName))
            {
                return null;
            }

            var info = FileVersionInfo.GetVersionInfo(fileName);
            var name = info.FileDescription;
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
