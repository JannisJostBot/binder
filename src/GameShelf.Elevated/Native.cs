using System.Runtime.InteropServices;

namespace GameShelf.Elevated;

internal static unsafe partial class Native
{
    // ---- Token ------------------------------------------------------------------------------------------

    /// <summary>This process runs with an elevated (full admin) token.</summary>
    public static bool IsElevated()
    {
        const int TokenElevation = 20;
        uint elevated = 0;
        return GetTokenInformation(-4, TokenElevation, &elevated, sizeof(uint), out _) && elevated != 0;
    }

    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetTokenInformation(nint token, int infoClass, void* info, int length, out int returned);

    [LibraryImport("advapi32.dll", EntryPoint = "ConvertSidToStringSidW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ConvertSidToStringSidW(IntPtr sid, out IntPtr text);

    [LibraryImport("advapi32.dll", EntryPoint = "ConvertStringSidToSidW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ConvertStringSidToSidW(string text, out IntPtr sid);

    [LibraryImport("kernel32.dll")]
    public static partial IntPtr LocalFree(IntPtr mem);

    // ---- Files and their security ----------------------------------------------------------------------

    public const int SE_FILE_OBJECT = 1;
    public const uint OWNER_SECURITY_INFORMATION = 0x1, DACL_SECURITY_INFORMATION = 0x4;
    public const uint PROTECTED_DACL_SECURITY_INFORMATION = 0x80000000;
    public const uint MOVEFILE_DELAY_UNTIL_REBOOT = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    public struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public int bInheritHandle;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW",
        StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string sddl, uint revision, out IntPtr sd, IntPtr size);

    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetSecurityDescriptorDacl(IntPtr sd, out int present, out IntPtr dacl, out int defaulted);

    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetSecurityDescriptorOwner(IntPtr sd, out IntPtr owner, out int defaulted);

    [LibraryImport("advapi32.dll", EntryPoint = "GetNamedSecurityInfoW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint GetNamedSecurityInfoW(string name, int type, uint info, out IntPtr owner, IntPtr group,
        IntPtr dacl, IntPtr sacl, out IntPtr sd);

    [LibraryImport("advapi32.dll", EntryPoint = "SetNamedSecurityInfoW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint SetNamedSecurityInfoW(string name, int type, uint info, IntPtr owner, IntPtr group,
        IntPtr dacl, IntPtr sacl);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateDirectoryW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CreateDirectoryW(string path, in SECURITY_ATTRIBUTES security);

    [LibraryImport("kernel32.dll", EntryPoint = "MoveFileExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool MoveFileExW(string from, string? to, uint flags);

    // ---- Events -----------------------------------------------------------------------------------------

    public const uint EVENT_MODIFY_STATE = 0x0002;

    [LibraryImport("kernel32.dll", EntryPoint = "OpenEventW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial IntPtr OpenEventW(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, string name);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetEvent(IntPtr handle);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(IntPtr handle);

    // ---- Services ---------------------------------------------------------------------------------------

    public const uint SERVICE_STOP = 0x20, SERVICE_START = 0x10, SERVICE_ENUMERATE_DEPENDENTS = 0x8;
    public const uint SERVICE_CONTROL_STOP = 1, SERVICE_ACTIVE = 1, SERVICE_ACCEPT_STOP = 1;
    public const uint SERVICE_START_PENDING = 2, SERVICE_STOP_PENDING = 3;
    public const int ERROR_MORE_DATA = 234, ERROR_SERVICE_ALREADY_RUNNING = 1056;

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ControlService(IntPtr service, uint control, uint* status);

    [LibraryImport("advapi32.dll", EntryPoint = "StartServiceW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool StartServiceW(IntPtr service, uint argCount, IntPtr args);

    [LibraryImport("advapi32.dll", EntryPoint = "EnumDependentServicesW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumDependentServicesW(IntPtr service, uint state, IntPtr buffer, uint size, out uint needed, out uint count);

    // ---- COM ----------------------------------------------------------------------------------------------

    public const uint COINIT_MULTITHREADED = 0x0, CLSCTX_INPROC_SERVER = 0x1;
    public const uint RPC_C_AUTHN_LEVEL_DEFAULT = 0, RPC_C_AUTHN_LEVEL_CALL = 3, RPC_C_IMP_LEVEL_IMPERSONATE = 3;
    public const uint RPC_C_AUTHN_WINNT = 10, RPC_C_AUTHZ_NONE = 0, EOAC_NONE = 0;

    [LibraryImport("ole32.dll")]
    public static partial int CoInitializeEx(IntPtr reserved, uint coInit);

    [LibraryImport("ole32.dll")]
    public static partial int CoInitializeSecurity(IntPtr sd, int authServices, IntPtr authList, IntPtr reserved1,
        uint authnLevel, uint impLevel, IntPtr authList2, uint capabilities, IntPtr reserved3);

    [LibraryImport("ole32.dll")]
    public static partial int CoCreateInstance(in Guid clsid, IntPtr outer, uint context, in Guid iid, out IntPtr instance);

    [LibraryImport("ole32.dll")]
    public static partial int CoSetProxyBlanket(IntPtr proxy, uint authnService, uint authzService, IntPtr serverName,
        uint authnLevel, uint impLevel, IntPtr authInfo, uint capabilities);

    [LibraryImport("oleaut32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr SysAllocString(string text);

    [LibraryImport("oleaut32.dll")]
    public static partial void SysFreeString(IntPtr bstr);

    [LibraryImport("oleaut32.dll")]
    public static partial int VariantClear(Variant* value);

    /// <summary>A VARIANT (24 bytes on x64) for the few types WMI needs here.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct Variant
    {
        public const ushort VT_I2 = 2, VT_I4 = 3, VT_BSTR = 8;
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr pointer;
        [FieldOffset(8)] public int i4;
        [FieldOffset(8)] public short i2;
    }
}
