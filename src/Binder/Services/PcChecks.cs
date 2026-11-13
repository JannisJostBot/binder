using System.Diagnostics;
using System.Runtime.InteropServices;
using Binder.Library;
using Microsoft.Win32;

namespace Binder.Services;

/// <summary>
/// Reads the PC settings anti-cheats care about. Everything here works without admin rights and only reads:
/// Binder never changes these settings. Run when the detail view opens, never in the background.
/// </summary>
internal static unsafe partial class PcChecks
{
    public static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

    /// <summary>True, false, or null when it can't be told.</summary>
    public static bool? Meets(PcRequirement requirement) => requirement switch
    {
        PcRequirement.SecureBoot => SecureBootOn(),
        PcRequirement.Tpm20 => HasTpm20(),
        PcRequirement.NoTestSigning => CodeIntegrityOptions() is { } ci ? (ci & CODEINTEGRITY_OPTION_TESTSIGN) == 0 : null,
        PcRequirement.NoKernelDebugging => KernelDebuggerEnabled() is { } on ? !on : null,
        PcRequirement.VanguardLoaded => VanguardLoaded(),
        _ => null,
    };

    private static bool? SecureBootOn()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            if (key is null) return false; // legacy BIOS boot: no Secure Boot
            return key.GetValue("UEFISecureBootEnabled") is int value ? value == 1 : null;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static bool? HasTpm20()
    {
        TPM_DEVICE_INFO info;
        uint result = Tbsi_GetDeviceInfo((uint)sizeof(TPM_DEVICE_INFO), &info);
        if (result == TBS_E_TPM_NOT_FOUND) return false;
        return result == 0 ? info.tpmVersion == TPM_VERSION_20 : null;
    }

    private static uint? CodeIntegrityOptions()
    {
        var info = new SYSTEM_CODEINTEGRITY_INFORMATION { Length = (uint)sizeof(SYSTEM_CODEINTEGRITY_INFORMATION) };
        return NtQuerySystemInformation(SystemCodeIntegrityInformation, &info, info.Length, out _) == 0 ? info.CodeIntegrityOptions : null;
    }

    private static bool? KernelDebuggerEnabled()
    {
        SYSTEM_KERNEL_DEBUGGER_INFORMATION info;
        return NtQuerySystemInformation(SystemKernelDebuggerInformation, &info, (uint)sizeof(SYSTEM_KERNEL_DEBUGGER_INFORMATION), out _) == 0
            ? info.KernelDebuggerEnabled != 0
            : null;
    }

    /// <summary>Vanguard's driver is installed but not running: Windows needs a restart before Riot games start.</summary>
    private static bool? VanguardLoaded()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\vgk");
            if (key is null) return null; // not installed yet; the game's installer adds it
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
        return ActiveDrivers() is { } drivers ? drivers.Contains("vgk") : null;
    }

    /// <summary>Names of the kernel drivers running now (lower case), or null if Windows didn't tell us.</summary>
    public static HashSet<string>? ActiveDrivers()
    {
        var manager = OpenSCManagerW(null, null, SC_MANAGER_ENUMERATE_SERVICE);
        if (manager == IntPtr.Zero) return null;
        try
        {
            uint resume = 0;
            EnumServicesStatusExW(manager, SC_ENUM_PROCESS_INFO, SERVICE_DRIVER, SERVICE_ACTIVE, null, 0, out uint needed, out _, ref resume, null);
            if (needed == 0 || needed > 4 * 1024 * 1024) return null;
            var buffer = new byte[needed];
            resume = 0;
            fixed (byte* p = buffer)
            {
                if (!EnumServicesStatusExW(manager, SC_ENUM_PROCESS_INFO, SERVICE_DRIVER, SERVICE_ACTIVE, p, needed, out _, out uint count, ref resume, null))
                    return null;
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var entries = (ENUM_SERVICE_STATUS_PROCESSW*)p;
                for (int i = 0; i < count; i++)
                    if (entries[i].lpServiceName != null) names.Add(new string(entries[i].lpServiceName).ToLowerInvariant());
                return names;
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }

    /// <summary>A running program that anti-cheats commonly refuse to start next to.</summary>
    public sealed record Conflict(string Name, int[] ProcessIds);

    public static List<Conflict> RunningConflicts()
    {
        var found = new Dictionary<string, List<int>>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (AntiCheat.ConflictingPrograms.TryGetValue(process.ProcessName.ToLowerInvariant(), out var name))
                {
                    if (!found.TryGetValue(name, out var ids)) found[name] = ids = [];
                    ids.Add(process.Id);
                }
            }
        }
        return found.Select(f => new Conflict(f.Key, [.. f.Value])).ToList();
    }

    /// <summary>Tools whose loaded driver some anti-cheats block (a tip to update them).</summary>
    public static List<string> BlockedDriverTools()
    {
        var tools = new List<string>();
        if (ActiveDrivers() is not { } drivers) return tools;
        foreach (var (prefix, tool) in AntiCheat.BlockedDrivers)
            if (drivers.Any(d => d.StartsWith(prefix, StringComparison.Ordinal)) && !tools.Contains(tool)) tools.Add(tool);
        return tools;
    }

    /// <summary>Asks a program to close, like clicking its X: WM_CLOSE to its windows. Never ends it forcibly.</summary>
    public static void AskToClose(Conflict conflict)
    {
        var ids = conflict.ProcessIds;
        fixed (int* idList = ids)
        {
            var state = new CloseState { Ids = idList, Count = ids.Length };
            EnumWindows(&CloseWindowsOf, (IntPtr)(&state));
        }
    }

    private struct CloseState
    {
        public int* Ids;
        public int Count;
    }

    [UnmanagedCallersOnly]
    private static int CloseWindowsOf(IntPtr hwnd, IntPtr lParam)
    {
        var state = (CloseState*)lParam;
        GetWindowThreadProcessId(hwnd, out uint pid);
        for (int i = 0; i < state->Count; i++)
        {
            if (state->Ids[i] == (int)pid && IsWindowVisible(hwnd))
            {
                PostMessageW(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                break;
            }
        }
        return 1; // keep enumerating
    }

    private const uint WM_CLOSE = 0x0010;
    private const uint TBS_E_TPM_NOT_FOUND = 0x8028400F;
    private const uint TPM_VERSION_20 = 2;
    private const int SystemKernelDebuggerInformation = 35;
    private const int SystemCodeIntegrityInformation = 103;
    private const uint CODEINTEGRITY_OPTION_TESTSIGN = 0x02;
    private const uint SC_MANAGER_ENUMERATE_SERVICE = 0x0004;
    private const int SC_ENUM_PROCESS_INFO = 0;
    private const uint SERVICE_DRIVER = 0x0000000B;
    private const uint SERVICE_ACTIVE = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct TPM_DEVICE_INFO { public uint structVersion, tpmVersion, tpmInterfaceType, tpmImpRevision; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_CODEINTEGRITY_INFORMATION { public uint Length, CodeIntegrityOptions; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_KERNEL_DEBUGGER_INFORMATION { public byte KernelDebuggerEnabled, KernelDebuggerNotPresent; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ENUM_SERVICE_STATUS_PROCESSW
    {
        public char* lpServiceName;
        public char* lpDisplayName;
        public uint dwServiceType, dwCurrentState, dwControlsAccepted, dwWin32ExitCode, dwServiceSpecificExitCode,
            dwCheckPoint, dwWaitHint, dwProcessId, dwServiceFlags;
    }

    [LibraryImport("tbs.dll")]
    private static partial uint Tbsi_GetDeviceInfo(uint size, void* info);

    [LibraryImport("ntdll.dll")]
    private static partial int NtQuerySystemInformation(int infoClass, void* info, uint length, out uint returnLength);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr OpenSCManagerW(string? machine, string? database, uint access);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumServicesStatusExW(IntPtr manager, int infoLevel, uint serviceType, uint serviceState,
        byte* services, uint bufSize, out uint bytesNeeded, out uint servicesReturned, ref uint resumeHandle, string? groupName);

    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseServiceHandle(IntPtr handle);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(delegate* unmanaged<IntPtr, IntPtr, int> callback, IntPtr lParam);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
