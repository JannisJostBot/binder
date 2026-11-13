using System.Runtime.InteropServices;
using Microsoft.Win32;
using static Binder.Elevated.Native;

namespace Binder.Elevated;

/// <summary>
/// Programs whose uploads are never slowed, whatever the request says (it comes from the user's side, and the tasks
/// run without a UAC prompt). Beyond the fixed list of Windows' and Defender's own (<c>ElevatedPart.IsSystemApp</c>),
/// found each time Apply runs:
/// <list type="bullet">
/// <item>every Windows service's program: antivirus and EDR agents send from their services;</item>
/// <item>a running program of that name that's a protected process (antimalware runs as one) or runs as another
/// account than the user (SYSTEM, a service account), or that can't be checked;</item>
/// <item>the antivirus products Windows Security knows (WMI <c>root\SecurityCenter2</c>, <c>AntiVirusProduct</c>;
/// not on Windows Server, where the first two still apply).</item>
/// </list>
/// Only the names in the request are looked at; nothing is changed. If the services can't be read, every name counts
/// as protected (fail closed).
/// </summary>
internal static unsafe partial class ProtectedApps
{
    private const int MaxServices = 4096, MaxProducts = 64;

    /// <summary>The names in <paramref name="names"/> that must not be throttled, for the user <paramref name="sid"/>.</summary>
    public static HashSet<string> Among(IReadOnlyCollection<string> names, string sid)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0) return found;
        var wanted = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

        if (ServicePrograms() is not { } services)
        {
            Helper.Log("Network: couldn't read the list of services, uploads are left alone");
            found.UnionWith(wanted);
            return found;
        }
        var antivirus = AntivirusPrograms();
        found.UnionWith(wanted.Where(n => services.Contains(n) || antivirus.Contains(n)));
        foreach (var name in RunningNotTheUsers(wanted, sid)) found.Add(name);
        return found;
    }

    // ---- Services ---------------------------------------------------------------------------------------------

    /// <summary>The exe file names of every Win32 service (own or shared process, user services too); null if unreadable.</summary>
    private static HashSet<string>? ServicePrograms()
    {
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (root is null) return null;
            var programs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in root.GetSubKeyNames().Take(MaxServices))
            {
                try
                {
                    using var service = root.OpenSubKey(name);
                    // SERVICE_WIN32_OWN_PROCESS 0x10, SERVICE_WIN32_SHARE_PROCESS 0x20 (user services add 0x40).
                    if (service?.GetValue("Type") is not int type || (type & 0x30) == 0) continue;
                    if (ExeName(service.GetValue("ImagePath", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string) is { } exe)
                        programs.Add(exe);
                }
                catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
            }
            return programs;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// The program's file name from a service's ImagePath or a product's path: quoted ("C:\A B\x.exe" -k y), unquoted
    /// up to ".exe" (C:\A B\x.exe -k y), with environment names or the \??\ prefix. Null when there's no .exe.
    /// </summary>
    internal static string? ExeName(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var text = command.Trim();
        if (text.StartsWith('"'))
        {
            int end = text.IndexOf('"', 1);
            text = end < 0 ? text[1..] : text[1..end];
        }
        else
        {
            int exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exe < 0) return null;
            text = text[..(exe + 4)];
        }
        var name = text[(text.LastIndexOfAny(['\\', '/']) + 1)..];
        return name.Length > 4 && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : null;
    }

    // ---- Windows Security ---------------------------------------------------------------------------------------

    private static readonly Guid ClsidLocator = new("4590f811-1d3a-11d0-891f-00aa004b2e24");
    private static readonly Guid IidLocator = new("dc12a687-737f-11cf-884d-00aa004b2e24");
    private const int WBEM_FLAG_FORWARD_ONLY = 0x20, WBEM_FLAG_RETURN_IMMEDIATELY = 0x10, NextTimeoutMs = 5000;

    /// <summary>The programs of the antivirus products registered with Windows Security; empty when there's none or no Security Center.</summary>
    private static HashSet<string> AntivirusPrograms()
    {
        var programs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (CoCreateInstance(in ClsidLocator, IntPtr.Zero, CLSCTX_INPROC_SERVER, in IidLocator, out var locator) < 0) return programs;
        IntPtr services = IntPtr.Zero, enumerator = IntPtr.Zero;
        var ns = SysAllocString(@"ROOT\SecurityCenter2");
        var language = SysAllocString("WQL");
        var query = SysAllocString("SELECT pathToSignedProductExe, pathToSignedReportingExe FROM AntiVirusProduct");
        try
        {
            // IWbemLocator::ConnectServer(resource, user, password, locale, flags, authority, context, out services)
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, IntPtr, IntPtr, IntPtr*, int>)Qos.Slot(locator, 3))(
                locator, ns, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero, &services);
            if (hr < 0) return programs; // Windows Server has no Security Center
            if (CoSetProxyBlanket(services, RPC_C_AUTHN_WINNT, RPC_C_AUTHZ_NONE, IntPtr.Zero, RPC_C_AUTHN_LEVEL_CALL,
                    RPC_C_IMP_LEVEL_IMPERSONATE, IntPtr.Zero, EOAC_NONE) < 0) return programs;
            // IWbemServices::ExecQuery(language, query, flags, context, out enumerator)
            hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, int, IntPtr, IntPtr*, int>)Qos.Slot(services, 20))(
                services, language, query, WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY, IntPtr.Zero, &enumerator);
            if (hr < 0) return programs;
            for (int i = 0; i < MaxProducts; i++)
            {
                IntPtr item = IntPtr.Zero;
                uint returned = 0;
                // IEnumWbemClassObject::Next(timeout, count, out objects, out returned)
                hr = ((delegate* unmanaged[Stdcall]<IntPtr, int, uint, IntPtr*, uint*, int>)Qos.Slot(enumerator, 4))(
                    enumerator, NextTimeoutMs, 1, &item, &returned);
                if (hr < 0 || returned == 0) break;
                try
                {
                    foreach (var property in new[] { "pathToSignedProductExe", "pathToSignedReportingExe" })
                        if (ExeName(Qos.GetString(item, property)) is { } exe) programs.Add(exe);
                }
                finally
                {
                    Qos.Release(item);
                }
            }
            if (programs.Count > 0) Helper.Log($"Network: security software found: {string.Join(", ", programs)}");
            return programs;
        }
        finally
        {
            SysFreeString(ns);
            SysFreeString(language);
            SysFreeString(query);
            Qos.Release(enumerator);
            Qos.Release(services);
            Qos.Release(locator);
        }
    }

    // ---- Running processes ----------------------------------------------------------------------------------

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, TOKEN_QUERY = 0x8;
    private const int ProcessProtectionLevelInfo = 7, TokenUser = 1;
    private const uint PROTECTION_LEVEL_NONE = 0xFFFFFFFE;

    /// <summary>
    /// The wanted names that run as a protected process or as another account than <paramref name="sid"/>, or
    /// whose owner can't be read.
    /// </summary>
    private static List<string> RunningNotTheUsers(HashSet<string> wanted, string sid)
    {
        var found = new List<string>();
        const int MaxPids = 4096;
        var pids = stackalloc uint[MaxPids];
        if (!EnumProcesses(pids, MaxPids * sizeof(uint), out var bytes)) return found;
        var path = stackalloc char[1024];
        for (int i = 0; i < (int)(bytes / sizeof(uint)); i++)
        {
            if (pids[i] == 0) continue;
            var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, 0, pids[i]);
            if (h == IntPtr.Zero) continue;
            try
            {
                uint length = 1024;
                if (!QueryFullProcessImageNameW(h, 0, path, ref length)) continue;
                var full = new string(path, 0, (int)length);
                var name = full[(full.LastIndexOf('\\') + 1)..];
                if (!wanted.Contains(name) || found.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                if (IsProtected(h) || OwnerSid(h) != sid)
                {
                    Helper.Log($"Network: {name} runs as a protected process or another account");
                    found.Add(name);
                }
            }
            finally
            {
                CloseHandle(h);
            }
        }
        return found;
    }

    private static bool IsProtected(IntPtr process)
    {
        uint level = PROTECTION_LEVEL_NONE;
        // Not available before Windows 10 1709: then only the owner decides.
        return GetProcessInformation(process, ProcessProtectionLevelInfo, &level, sizeof(uint)) && level != PROTECTION_LEVEL_NONE;
    }

    /// <summary>The process's user SID as text, or null when it can't be read (counted as not the user's).</summary>
    private static string? OwnerSid(IntPtr process)
    {
        if (!OpenProcessToken(process, TOKEN_QUERY, out var token)) return null;
        try
        {
            var buffer = stackalloc byte[256];
            if (!GetTokenInformation(token, TokenUser, buffer, 256, out _)) return null;
            if (!ConvertSidToStringSidW(*(IntPtr*)buffer, out var text) || text == IntPtr.Zero) return null;
            try
            {
                return new string((char*)text);
            }
            finally
            {
                LocalFree(text);
            }
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [LibraryImport("psapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumProcesses(uint* pids, int size, out uint returned);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr OpenProcess(uint access, int inherit, uint pid);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryFullProcessImageNameW(IntPtr process, uint flags, char* name, ref uint size);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessInformation(IntPtr process, int infoClass, void* info, uint size);

    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
}
