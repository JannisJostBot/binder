using System.Runtime.InteropServices;

namespace Binder.Watcher;

/// <summary>
/// While the watcher is blocked on the game: compacted once, its memory given back to Windows, in efficiency mode
/// (EcoQoS) with very low memory priority, so it costs the game nothing. Undone when the game exits, before the
/// session is saved and Game mode is put back.
/// </summary>
internal static partial class Footprint
{
    /// <summary>Call right before blocking on the game's processes.</summary>
    public static void Rest()
    {
        _resting = true;
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        SetEfficiencyMode(true);
        SetProcessWorkingSetSizeEx(GetCurrentProcess(), -1, -1, 0); // trim the working set, last: nothing touches it after
    }

    /// <summary>Call when the game exits or Windows is ending the session: saving and restoring run at full speed.</summary>
    public static void Wake()
    {
        _resting = false;
        SetEfficiencyMode(false);
    }

    private static volatile bool _resting;

    /// <summary>Hands the working set back to Windows (after the ring's window and GDI+ are gone).</summary>
    public static void Trim() => SetProcessWorkingSetSizeEx(GetCurrentProcess(), -1, -1, 0);

    /// <summary>"End a stuck game" is being held (EndHold, on the system events thread): full speed while the ring
    /// draws and its sound plays, so neither stutters.</summary>
    public static void Pause()
    {
        if (_resting) SetEfficiencyMode(false);
    }

    /// <summary>The hold is over and the game still runs: back to rest (the ring's window trims the working set itself).</summary>
    public static void Resume()
    {
        if (_resting) SetEfficiencyMode(true);
    }

    private static void SetEfficiencyMode(bool on)
    {
        var process = GetCurrentProcess();
        var throttling = new PowerThrottlingState
        {
            Version = 1, // PROCESS_POWER_THROTTLING_CURRENT_VERSION
            ControlMask = ExecutionSpeed,
            StateMask = on ? ExecutionSpeed : 0,
        };
        SetProcessInformation(process, ProcessPowerThrottling, ref throttling, (uint)Marshal.SizeOf<PowerThrottlingState>());
        var priority = new MemoryPriorityInformation { MemoryPriority = on ? MemoryPriorityVeryLow : MemoryPriorityNormal };
        SetProcessInformation(process, ProcessMemoryPriority, ref priority, (uint)Marshal.SizeOf<MemoryPriorityInformation>());
    }

    private const int ProcessMemoryPriority = 0, ProcessPowerThrottling = 4;
    private const uint ExecutionSpeed = 1; // PROCESS_POWER_THROTTLING_EXECUTION_SPEED
    private const uint MemoryPriorityVeryLow = 1, MemoryPriorityNormal = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottlingState { public uint Version, ControlMask, StateMask; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryPriorityInformation { public uint MemoryPriority; }

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetCurrentProcess();

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessWorkingSetSizeEx(IntPtr process, nint minimum, nint maximum, uint flags);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessInformation(IntPtr process, int infoClass, ref PowerThrottlingState info, uint size);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessInformation(IntPtr process, int infoClass, ref MemoryPriorityInformation info, uint size);
}
