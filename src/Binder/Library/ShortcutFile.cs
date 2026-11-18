using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Binder.Library;

/// <summary>What a desktop shortcut points at. Read without ever running it.</summary>
public sealed record ShortcutFile(string Path, string Name, string? TargetPath, string? Url)
{
    /// <summary>.url files are tiny INI files; anything bigger isn't one.</summary>
    public const long MaxUrlFileBytes = 64 * 1024;

    /// <summary>Steam appid for steam://rungameid/123 links, else null.</summary>
    public string? SteamAppId
    {
        get
        {
            const string prefix = "steam://rungameid/";
            if (Url is null || !Url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            var id = Url[prefix.Length..].TrimEnd('/');
            return id.Length is > 0 and <= 10 && id.All(char.IsAsciiDigit) ? id : null;
        }
    }

    /// <summary>Epic AppName for com.epicgames.launcher://apps/... links, else null.</summary>
    public string? EpicAppName
    {
        get
        {
            const string prefix = "com.epicgames.launcher://apps/";
            if (Url is null || !Url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            var rest = Url[prefix.Length..];
            int query = rest.IndexOf('?');
            if (query >= 0) rest = rest[..query];
            // apps/{namespace}:{item}:{AppName} (':' usually escaped as %3A), or just apps/{AppName}.
            var parts = Uri.UnescapeDataString(rest).Split(':');
            var app = parts[^1];
            return EpicSource.IsEpicId(app) ? app : null;
        }
    }

    /// <summary>Ubisoft game id for uplay://launch/4553/0 links, else null.</summary>
    public string? UbisoftId
    {
        get
        {
            const string prefix = "uplay://launch/";
            if (Url is null || !Url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            var id = Url[prefix.Length..].Split('/')[0];
            return id.Length is > 0 and <= 10 && id.All(char.IsAsciiDigit) ? id : null;
        }
    }

    /// <summary>True if the shortcut starts a program (an .exe) or opens a link; false for documents and folders.</summary>
    public bool CanBeGame =>
        Url is not null ||
        (TargetPath is not null && TargetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

    /// <summary>Reads a .lnk or .url file. Returns null for anything that isn't a readable shortcut.</summary>
    public static ShortcutFile? Read(string path)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        var ext = System.IO.Path.GetExtension(path);
        try
        {
            if (ext.Equals(".url", StringComparison.OrdinalIgnoreCase))
                return ReadUrlFile(path) is { } url ? new ShortcutFile(path, name, null, url) : null;
            if (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                return ShellLink.ReadTarget(path) is { } target ? new ShortcutFile(path, name, target, null) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or COMException or InvalidCastException) { }
        return null;
    }

    /// <summary>The URL= line of the [InternetShortcut] section.</summary>
    public static string? ReadUrlFile(string path) => ReadUrlValue(path, "URL");

    /// <summary>A key of the [InternetShortcut] section (URL, IconFile), or null. Bounded like <see cref="ReadUrlFile"/>.</summary>
    public static string? ReadUrlValue(string path, string key)
    {
        if (new FileInfo(path).Length > MaxUrlFileBytes) return null;
        var prefix = key + "=";
        bool inSection = false;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                inSection = line.Equals("[InternetShortcut]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (inSection && line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var value = line[prefix.Length..].Trim();
                return value.Length is > 0 and <= 2048 ? value : null;
            }
        }
        return null;
    }

    /// <summary>All .lnk and .url files directly on the user's and the shared (Public) desktop.</summary>
    public static List<ShortcutFile> ScanDesktops()
    {
        var found = new List<ShortcutFile>();
        foreach (var folder in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                 })
        {
            if (string.IsNullOrEmpty(folder)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(folder))
                {
                    var ext = System.IO.Path.GetExtension(file);
                    if (!ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) &&
                        !ext.Equals(".url", StringComparison.OrdinalIgnoreCase)) continue;
                    if (Read(file) is { CanBeGame: true } shortcut) found.Add(shortcut);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return found;
    }
}

/// <summary>
/// Reads a .lnk file's target through the Shell's own ShellLink object (never resolves or runs it). Source-generated
/// COM, so it works in the Native AOT build.
/// </summary>
internal static unsafe partial class ShellLink
{
    private const uint STGM_READ = 0, CLSCTX_INPROC_SERVER = 1;
    private static readonly Guid ClsidShellLink = new("00021401-0000-0000-C000-000000000046");
    private static readonly StrategyBasedComWrappers Wrappers = new();

    public static string? ReadTarget(string path)
    {
        var clsid = ClsidShellLink;
        var iid = typeof(IShellLinkW).GUID;
        if (CoCreateInstance(in clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, in iid, out var unknown) < 0 || unknown == IntPtr.Zero)
            return null;
        try
        {
            var link = (IShellLinkW)Wrappers.GetOrCreateObjectForComInstance(unknown, CreateObjectFlags.UniqueInstance);
            ((IPersistFile)link).Load(path, STGM_READ);
            var buffer = stackalloc char[1024];
            buffer[0] = '\0';
            link.GetPath(buffer, 1024, IntPtr.Zero, 0);
            var target = new string(buffer);
            return target.Length > 0 ? target : null;
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    /// <summary>
    /// Writes a new .lnk (spec §22 c: Binder's own Start menu folder only). Throws <see cref="COMException"/> or an
    /// IO exception when it can't.
    /// </summary>
    public static void Write(string path, string target, string arguments, string description, string iconPath)
    {
        var clsid = ClsidShellLink;
        var iid = typeof(IShellLinkW).GUID;
        int hr = CoCreateInstance(in clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, in iid, out var unknown);
        if (hr < 0 || unknown == IntPtr.Zero) throw new COMException("ShellLink", hr);
        try
        {
            var link = (IShellLinkW)Wrappers.GetOrCreateObjectForComInstance(unknown, CreateObjectFlags.UniqueInstance);
            link.SetPath(target);
            link.SetArguments(arguments);
            link.SetDescription(description);
            link.SetIconLocation(iconPath, 0);
            link.SetWorkingDirectory(System.IO.Path.GetDirectoryName(target) ?? "");
            ((IPersistFile)link).Save(path, true);
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, IntPtr outer, uint context, in Guid iid, out IntPtr instance);

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16), Guid("000214F9-0000-0000-C000-000000000046")]
    internal partial interface IShellLinkW
    {
        void GetPath(char* file, int maxPath, IntPtr findData, uint flags);
        // The rest of the vtable, unused here, in order.
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription(char* name, int maxName);
        void SetDescription(string name);
        void GetWorkingDirectory(char* dir, int maxPath);
        void SetWorkingDirectory(string dir);
        void GetArguments(char* args, int maxPath);
        void SetArguments(string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation(char* iconPath, int maxPath, out int iconIndex);
        void SetIconLocation(string iconPath, int iconIndex);
        void SetRelativePath(string relativePath, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath(string file);
    }

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16), Guid("0000010b-0000-0000-C000-000000000046")]
    internal partial interface IPersistFile
    {
        void GetClassID(out Guid clsid);
        [PreserveSig] int IsDirty();
        void Load(string fileName, uint mode);
        void Save(string? fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);
        void SaveCompleted(string fileName);
        void GetCurFile(out IntPtr fileName);
    }
}
