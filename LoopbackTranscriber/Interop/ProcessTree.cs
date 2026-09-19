using System.Runtime.InteropServices;

namespace LoopbackTranscriber.Interop;

internal static class ProcessTree
{
    private const uint TH32CS_SNAPPROCESS = 0x00000002;

    public static uint FindRootProcessId(IReadOnlyList<int> candidatePids)
    {
        if (candidatePids.Count == 0)
        {
            return 0;
        }

        if (candidatePids.Count == 1)
        {
            return (uint)candidatePids[0];
        }

        var parentByPid = GetParentProcessIds();
        var candidateSet = new HashSet<int>(candidatePids);

        foreach (var pid in candidatePids)
        {
            if (!parentByPid.TryGetValue(pid, out var parentPid) || !candidateSet.Contains(parentPid))
            {
                return (uint)pid;
            }
        }

        return (uint)candidatePids[0];
    }

    private static Dictionary<int, int> GetParentProcessIds()
    {
        var result = new Dictionary<int, int>();
        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
        {
            return result;
        }

        try
        {
            var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            if (Process32First(snapshot, ref entry))
            {
                do
                {
                    result[(int)entry.th32ProcessID] = (int)entry.th32ParentProcessID;
                } while (Process32Next(snapshot, ref entry));
            }
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return result;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll")]
    private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll")]
    private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}
