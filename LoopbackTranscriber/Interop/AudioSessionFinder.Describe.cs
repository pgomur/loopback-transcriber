using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using LoopbackTranscriber.Models;

namespace LoopbackTranscriber.Interop;

internal static partial class AudioSessionFinder
{
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
}
