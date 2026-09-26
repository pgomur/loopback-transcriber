using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using LoopbackTranscriber.Models;

namespace LoopbackTranscriber.Interop;

internal enum EDataFlow
{
    Render = 0,
    Capture = 1,
    All = 2
}

internal enum ERole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2
}

internal enum AudioSessionState
{
    Inactive = 0,
    Active = 1,
    Expired = 2
}

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject
{
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IntPtr ppDevices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);

    [PreserveSig]
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);

    [PreserveSig]
    int RegisterEndpointNotificationCallback(IntPtr pClient);

    [PreserveSig]
    int UnregisterEndpointNotificationCallback(IntPtr pClient);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig]
    int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams,
        [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);

    [PreserveSig]
    int OpenPropertyStore(uint stgmAccess, out IntPtr ppProperties);

    [PreserveSig]
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);

    [PreserveSig]
    int GetState(out uint pdwState);
}

[ComImport]
[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    [PreserveSig]
    int GetAudioSessionControl(IntPtr audioSessionGuid, int streamFlags, out IntPtr sessionControl);

    [PreserveSig]
    int GetSimpleAudioVolume(IntPtr audioSessionGuid, int streamFlags, out IntPtr audioVolume);

    [PreserveSig]
    int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);

    [PreserveSig]
    int RegisterSessionNotification(IntPtr sessionNotification);

    [PreserveSig]
    int UnregisterSessionNotification(IntPtr sessionNotification);

    [PreserveSig]
    int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionID, IntPtr duckNotification);

    [PreserveSig]
    int UnregisterDuckNotification(IntPtr duckNotification);
}

[ComImport]
[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    [PreserveSig]
    int GetCount(out int sessionCount);

    [PreserveSig]
    int GetSession(int sessionIndex, out IAudioSessionControl2 session);
}

// Interfaz "plana" (no heredada de IAudioSessionControl) con los 14 metodos en el orden real
// de su vtable nativa: los 9 de IAudioSessionControl seguidos de los 5 propios de la version 2.
[ComImport]
[Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    [PreserveSig]
    int GetState(out AudioSessionState pRetVal);

    [PreserveSig]
    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

    [PreserveSig]
    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, IntPtr eventContext);

    [PreserveSig]
    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

    [PreserveSig]
    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, IntPtr eventContext);

    [PreserveSig]
    int GetGroupingParam(out Guid pRetVal);

    [PreserveSig]
    int SetGroupingParam(ref Guid over, IntPtr eventContext);

    [PreserveSig]
    int RegisterAudioSessionNotification(IntPtr newNotifications);

    [PreserveSig]
    int UnregisterAudioSessionNotification(IntPtr newNotifications);

    [PreserveSig]
    int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

    [PreserveSig]
    int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

    [PreserveSig]
    int GetProcessId(out uint pRetVal);

    [PreserveSig]
    int IsSystemSoundsSession();

    [PreserveSig]
    int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
}

[ComImport]
[Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISimpleAudioVolume
{
    [PreserveSig]
    int SetMasterVolume(float level, IntPtr eventContext);

    [PreserveSig]
    int GetMasterVolume(out float level);

    [PreserveSig]
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, IntPtr eventContext);

    [PreserveSig]
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}
internal static class AudioSessionFinder
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
        catch
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
            catch
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

    public static string DescribeSessions(IReadOnlyList<string> candidateProcessNames)
    {
        var seenDeviceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = new List<string>();

        foreach (var role in new[] { ERole.Console, ERole.Communications, ERole.Multimedia })
        {
            DescribeSessionsOnRole(candidateProcessNames, role, seenDeviceIds, lines);
        }

        return lines.Count == 0
            ? "no se encontraron sesiones de audio para este proceso"
            : string.Join(" | ", lines);
    }

    private static void DescribeSessionsOnRole(IReadOnlyList<string> candidateProcessNames, ERole role,
        HashSet<string> seenDeviceIds, List<string> lines)
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
                var line = DescribeSession(sessionEnumerator, i, candidateProcessNames, role);
                if (line is not null)
                {
                    lines.Add(line);
                }
            }
        }
        catch
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

    private static string? DescribeSession(IAudioSessionEnumerator sessionEnumerator, int index,
        IReadOnlyList<string> candidateProcessNames, ERole role)
    {
        if (sessionEnumerator.GetSession(index, out var session) < 0 || session is null)
        {
            return null;
        }

        try
        {
            if (session.IsSystemSoundsSession() == 0)
            {
                return null;
            }

            if (session.GetProcessId(out var sessionPid) < 0 || sessionPid == 0)
            {
                return null;
            }

            string processName;
            try
            {
                using var proc = Process.GetProcessById((int)sessionPid);
                processName = proc.ProcessName;
            }
            catch
            {
                return null;
            }

            var matches = false;
            foreach (var candidate in candidateProcessNames)
            {
                if (string.Equals(processName, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    matches = true;
                    break;
                }
            }

            if (!matches)
            {
                return null;
            }

            var state = "?";
            if (session.GetState(out var sessionState) >= 0)
            {
                state = sessionState.ToString();
            }

            var volumePercent = "?";
            var mutedText = "?";

            try
            {
                var volume = (ISimpleAudioVolume)session;
                if (volume.GetMasterVolume(out var level) >= 0)
                {
                    volumePercent = $"{level * 100:0}%";
                }

                if (volume.GetMute(out var muted) >= 0)
                {
                    mutedText = muted ? "si" : "no";
                }
            }
            catch
            {
            }

            return $"[{role}] PID={sessionPid} proc={processName} estado={state} volumen={volumePercent} muteado={mutedText}";
        }
        finally
        {
            Marshal.ReleaseComObject(session);
        }
    }

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
        catch
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
            catch
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
        catch
        {
            return null;
        }
    }
}
