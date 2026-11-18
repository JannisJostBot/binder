using System.Runtime;
using System.Runtime.InteropServices;

namespace Binder.Services;

/// <summary>
/// "Stay ready" at the lowest footprint (spec §5): after the panel closes, compact once, give the memory back
/// to Windows, and run in efficiency mode until it opens again.
/// </summary>
internal static partial class Footprint
{
    /// <summary>Call after the window and its views are gone.</summary>
    public static void Shrink()
    {
        // Images, windows and other native resources are released by finalizers, which only run after a
        // collection; a second collection then frees what they held. Aggressive also decommits free GC memory.
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        SetProcessWorkingSetSizeEx(GetCurrentProcess(), -1, -1, 0); // trim the working set
        SetEfficiencyMode(true);
    }

    /// <summary>
    /// What the process holds, split into the managed heap and the rest (WPF's native rendering, fonts, code),
    /// written to %LOCALAPPDATA%\Binder\memory.txt. Only on request (<c>Binder.exe --memory-report</c>),
    /// for measuring the footprint; nothing is collected or sent anywhere.
    /// </summary>
    public static void WriteReport(string state)
    {
        var gc = GC.GetGCMemoryInfo();
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        static string Mb(long bytes) => (bytes / 1048576.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB";
        var lines = new[]
        {
            $"state: {state}",
            $"private bytes: {Mb(process.PrivateMemorySize64)}",
            $"working set: {Mb(process.WorkingSet64)}",
            $"managed heap (live): {Mb(gc.HeapSizeBytes - gc.FragmentedBytes)}",
            $"managed heap (committed): {Mb(gc.TotalCommittedBytes)}",
            $"not managed (private - committed): {Mb(process.PrivateMemorySize64 - gc.TotalCommittedBytes)}",
            $"threads: {process.Threads.Count}",
            $"panel windows alive: {PanelsAlive()}",
        };
        try
        {
            Directory.CreateDirectory(Storage.CacheDir);
            File.WriteAllLines(Path.Combine(Storage.CacheDir, "memory.txt"), lines);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // Weak references to the panel windows made so far, for the report: a closed one still alive after Shrink's
    // collections is held by something (an event, a static), and with it its views and covers.
    private static readonly List<WeakReference> Panels = [];

    /// <summary>Called when a panel window is made.</summary>
    public static void TrackPanel(object panel)
    {
        Panels.RemoveAll(p => !p.IsAlive);
        Panels.Add(new WeakReference(panel));
    }

    private static int PanelsAlive() => Panels.Count(p => p.IsAlive);

    /// <summary>
    /// "Open faster": the closed panel stays loaded, so nothing is collected. The working set is still trimmed:
    /// with very low memory priority those pages are the first Windows reuses, and while it doesn't they come
    /// back from RAM on open (measured: reopening takes ~3 ms either way).
    /// </summary>
    public static void Rest()
    {
        SetProcessWorkingSetSizeEx(GetCurrentProcess(), -1, -1, 0);
        SetEfficiencyMode(true);
    }

    /// <summary>Call when the panel opens.</summary>
    public static void Wake() => SetEfficiencyMode(false);

    /// <summary>EcoQoS (Windows 11 efficiency mode) plus very low memory priority while closed; undone on open.</summary>
    private static void SetEfficiencyMode(bool on)
    {
        var process = GetCurrentProcess();
        var throttling = new PROCESS_POWER_THROTTLING_STATE
        {
            Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
            ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
            StateMask = on ? PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0,
        };
        SetProcessInformation(process, ProcessPowerThrottling, ref throttling, Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());

        var priority = new MEMORY_PRIORITY_INFORMATION { MemoryPriority = on ? MEMORY_PRIORITY_VERY_LOW : MEMORY_PRIORITY_NORMAL };
        SetProcessInformation(process, ProcessMemoryPriority, ref priority, Marshal.SizeOf<MEMORY_PRIORITY_INFORMATION>());
    }

    private const int ProcessMemoryPriority = 0, ProcessPowerThrottling = 4;
    private const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1, PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 1;
    private const uint MEMORY_PRIORITY_VERY_LOW = 1, MEMORY_PRIORITY_NORMAL = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_POWER_THROTTLING_STATE { public uint Version, ControlMask, StateMask; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORY_PRIORITY_INFORMATION { public uint MemoryPriority; }

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetCurrentProcess();

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessWorkingSetSizeEx(IntPtr process, nint minimum, nint maximum, uint flags);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessInformation(IntPtr process, int infoClass, ref PROCESS_POWER_THROTTLING_STATE info, int size);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessInformation(IntPtr process, int infoClass, ref MEMORY_PRIORITY_INFORMATION info, int size);
}
