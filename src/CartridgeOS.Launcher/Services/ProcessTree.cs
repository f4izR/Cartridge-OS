using System.Runtime.InteropServices;

namespace CartridgeOS.Launcher.Services;

/// <summary>Windows exposes no managed API for "is anything from this process's launch tree still alive",
/// so this P/Invokes CreateToolhelp32Snapshot for it. Replaces same-exe-name matching (App.xaml.cs used to
/// assume a stub's replacement process keeps the same name) which only covers apps that relaunch under
/// their own exe — most games don't: a launcher/updater stub spawns a differently-named real game exe
/// (Unreal's "-Win64-Shipping.exe", Unity's own build name, EA/Ubisoft wrapper exes, etc), so name
/// matching found nothing, treated the game as closed, and popped the launcher window back over it.
/// Process-tree ancestry survives a rename because it's keyed on PID, not exe name.</summary>
internal static class ProcessTree
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int priClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    private const uint TH32CS_SNAPPROCESS = 0x00000002;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);
    [DllImport("kernel32.dll")]
    private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);
    [DllImport("kernel32.dll")]
    private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    private static Dictionary<uint, uint>? SnapshotParents()
    {
        IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1)) return null;

        try
        {
            var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            var parentOf = new Dictionary<uint, uint>();
            if (!Process32First(snapshot, ref entry)) return parentOf;
            do
            {
                parentOf[entry.th32ProcessID] = entry.th32ParentProcessID;
            } while (Process32Next(snapshot, ref entry));
            return parentOf;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    /// <summary>True if rootPid, or any process descended from it however many stub-relaunch hops deep, is
    /// still alive right now.</summary>
    public static bool IsTreeAlive(int rootPid)
    {
        var parentOf = SnapshotParents();
        if (parentOf is null) return false;
        if (parentOf.ContainsKey((uint)rootPid)) return true;

        // Walk every live process's ancestry back toward rootPid — parentOf is only ever as big as the
        // system's current process count, so this is cheap even done per-candidate.
        foreach (uint pid in parentOf.Keys)
        {
            uint current = pid;
            for (int hops = 0; hops < 32 && parentOf.TryGetValue(current, out uint parent); hops++)
            {
                if (parent == rootPid) return true;
                current = parent;
            }
        }
        return false;
    }

    /// <summary>Every currently-live PID descended from rootPid (root included, if still alive) — used to
    /// actually kill "Quit Game" rather than just the stub that already exited.</summary>
    public static List<int> GetTreePids(int rootPid)
    {
        var result = new List<int>();
        var parentOf = SnapshotParents();
        if (parentOf is null) return result;
        if (parentOf.ContainsKey((uint)rootPid)) result.Add(rootPid);

        foreach (uint pid in parentOf.Keys)
        {
            uint current = pid;
            for (int hops = 0; hops < 32 && parentOf.TryGetValue(current, out uint parent); hops++)
            {
                if (parent == rootPid) { result.Add((int)pid); break; }
                current = parent;
            }
        }
        return result;
    }
}
