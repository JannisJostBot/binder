using System.Runtime.InteropServices;
using System.Security;
using GameShelf.Library;
using Microsoft.Win32;

namespace GameShelf.Services;

/// <summary>
/// The PC facts for a crash's bug report and AI prompt: Windows version, graphics adapters with their driver,
/// memory. Read locally from the registry and Windows (no admin, no network), only when a text is copied.
/// </summary>
internal static partial class PcInfo
{
    private const string DisplayAdapters = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    public static PcFacts Read() => new(WindowsName(), Graphics(), MemoryGb());

    /// <summary>"Windows 11 Pro 24H2". ProductName still says Windows 10 on 11, so the build decides.</summary>
    private static string WindowsName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var product = key?.GetValue("ProductName") as string ?? "Windows";
            var display = key?.GetValue("DisplayVersion") as string;
            if (int.TryParse(key?.GetValue("CurrentBuildNumber") as string, out int build) && build >= 22000)
                product = product.Replace("Windows 10", "Windows 11", StringComparison.Ordinal);
            return display is null ? product : $"{product} {display}";
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException)
        {
            return "Windows";
        }
    }

    /// <summary>"NVIDIA GeForce RTX 5090 with driver 566.36" per real adapter (virtual and basic ones skipped).</summary>
    private static List<string> Graphics()
    {
        var list = new List<string>();
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(DisplayAdapters);
            if (root is null) return list;
            foreach (var name in root.GetSubKeyNames())
            {
                if (name.Length != 4 || !name.All(char.IsAsciiDigit)) continue; // 0000, 0001, … (skips "Properties")
                try
                {
                    using var adapter = root.OpenSubKey(name);
                    if (adapter?.GetValue("DriverDesc") is not string desc || desc.Length == 0) continue;
                    if (desc.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                        desc.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) ||
                        desc.Contains("Remote", StringComparison.OrdinalIgnoreCase) ||
                        desc.Contains("Indirect", StringComparison.OrdinalIgnoreCase)) continue;
                    var version = adapter.GetValue("DriverVersion") as string;
                    var text = version is null ? desc : $"{desc} with driver {CrashAnalysis.DriverVersion(desc, version)}";
                    if (!list.Contains(text)) list.Add(text);
                }
                catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException) { }
                if (list.Count >= 3) break;
            }
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException) { }
        return list;
    }

    /// <summary>Installed memory as the PC's specs give it (64 GB), not the slightly smaller amount Windows can use.</summary>
    private static int MemoryGb() =>
        GetPhysicallyInstalledSystemMemory(out ulong kb) ? (int)Math.Round(kb / (1024.0 * 1024)) : 0;

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);
}
