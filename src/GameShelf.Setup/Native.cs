using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

// Every P/Invoke below is a Windows system DLL, loaded from System32 only (see Security rules).
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace GameShelf.Setup;

internal static unsafe partial class Native
{
    public const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800;

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetDefaultDllDirectories(uint flags);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr GetModuleHandle(string? name);

    // ---- Talking to the running widget ----------------------------------------------------------------

    public const int WM_COPYDATA = 0x004A;
    public const uint SMTO_ABORTIFHUNG = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    public struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr FindWindow(string className, string? windowName);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    public static partial IntPtr SendMessageTimeout(IntPtr hwnd, int msg, IntPtr wParam, ref COPYDATASTRUCT data,
        uint flags, uint timeout, out IntPtr result);

    // ---- Process paths (without opening more access than needed) ---------------------------------------

    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryFullProcessImageName(IntPtr process, uint flags, char* buffer, ref int size);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(IntPtr handle);

    public static string? ProcessPath(int pid)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            char* buffer = stackalloc char[1024];
            int size = 1024;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? new string(buffer, 0, size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    // ---- Shell ---------------------------------------------------------------------------------------

    public const int SHCNE_ASSOCCHANGED = 0x08000000;

    [LibraryImport("shell32.dll")]
    public static partial void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    [LibraryImport("ole32.dll")]
    public static partial int CoInitializeEx(IntPtr reserved, uint coInit);

    [LibraryImport("ole32.dll")]
    public static partial int CoCreateInstance(in Guid clsid, IntPtr outer, uint context, in Guid iid, out IntPtr instance);

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int MessageBox(IntPtr hwnd, string text, string caption, uint type);

    // ---- Task dialog (comctl32 v6, through the manifest) ---------------------------------------------

    // commctrl.h declares these packed (pshpack1.h) on every architecture.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TASKDIALOGCONFIG
    {
        public uint cbSize;
        public IntPtr hwndParent;
        public IntPtr hInstance;
        public int dwFlags;
        public int dwCommonButtons;
        public IntPtr pszWindowTitle;
        public IntPtr hMainIcon;
        public IntPtr pszMainInstruction;
        public IntPtr pszContent;
        public uint cButtons;
        public IntPtr pButtons;
        public int nDefaultButton;
        public uint cRadioButtons;
        public IntPtr pRadioButtons;
        public int nDefaultRadioButton;
        public IntPtr pszVerificationText;
        public IntPtr pszExpandedInformation;
        public IntPtr pszExpandedControlText;
        public IntPtr pszCollapsedControlText;
        public IntPtr hFooterIcon;
        public IntPtr pszFooter;
        public IntPtr pfCallback;
        public IntPtr lpCallbackData;
        public uint cxWidth;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TASKDIALOG_BUTTON
    {
        public int nButtonID;
        public IntPtr pszButtonText;
    }

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    public static partial IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("comctl32.dll")]
    public static partial int TaskDialogIndirect(TASKDIALOGCONFIG* config, int* button, int* radioButton, int* verificationChecked);
}

// ---- Shell links, through source-generated COM (works with Native AOT) ----------------------------------

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("000214F9-0000-0000-C000-000000000046")]
internal partial interface IShellLinkW
{
    void GetPath(IntPtr file, int size, IntPtr findData, uint flags);
    void GetIDList(out IntPtr idList);
    void SetIDList(IntPtr idList);
    void GetDescription(IntPtr name, int size);
    void SetDescription(string name);
    void GetWorkingDirectory(IntPtr dir, int size);
    void SetWorkingDirectory(string dir);
    void GetArguments(IntPtr args, int size);
    void SetArguments(string args);
    void GetHotkey(out ushort hotkey);
    void SetHotkey(ushort hotkey);
    void GetShowCmd(out int showCmd);
    void SetShowCmd(int showCmd);
    void GetIconLocation(IntPtr path, int size, out int index);
    void SetIconLocation(string path, int index);
    void SetRelativePath(string path, uint reserved);
    void Resolve(IntPtr hwnd, uint flags);
    void SetPath(string file);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("0000010b-0000-0000-C000-000000000046")]
internal partial interface IPersistFile
{
    void GetClassID(out Guid classId);
    [PreserveSig] int IsDirty();
    void Load(string fileName, uint mode);
    void Save(string fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);
    void SaveCompleted(string fileName);
    void GetCurFile(out IntPtr fileName);
}
