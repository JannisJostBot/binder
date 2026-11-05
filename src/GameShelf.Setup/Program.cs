using System.Reflection;

namespace GameShelf.Setup;

internal static class ExitCode
{
    public const int Ok = 0, Cancelled = 1, Error = 2, NeedsRuntime = 3, Downgrade = 4, BadArguments = 5;
}

/// <summary>
/// Command line:
///   GameShelfSetup.exe                       install or update, with dialogs
///   GameShelfSetup.exe /S [/launch]          silent; /launch starts the app afterwards
///   GameShelfSetup.exe /S /allowdowngrade    silent, even over a newer version
///   Uninstall.exe /uninstall [/S] [/removedata]
/// </summary>
internal sealed record Options(bool Uninstall, bool Silent, bool RemoveData, bool Launch, bool AllowDowngrade, bool Continue)
{
    public static Options? Parse(string[] args)
    {
        var o = new Options(false, false, false, false, false, false);
        foreach (var arg in args)
        {
            o = arg.ToLowerInvariant() switch
            {
                "/uninstall" => o with { Uninstall = true },
                "/s" or "/silent" => o with { Silent = true },
                "/removedata" => o with { RemoveData = true },
                "/launch" => o with { Launch = true },
                "/allowdowngrade" => o with { AllowDowngrade = true },
                "/continue" => o with { Uninstall = true, Continue = true }, // internal: the temp copy
                _ => null!,
            };
            if (o is null) return null;
        }
        return o;
    }

    public IEnumerable<string> ToArguments(bool continuing)
    {
        yield return continuing ? "/continue" : "/uninstall";
        if (Silent) yield return "/S";
        if (RemoveData) yield return "/removedata";
    }
}

internal static class Program
{
    /// <summary>Settings → Apps "publisher": the Company from Directory.Build.props, so there's one place to change it.</summary>
    public static readonly string Publisher =
        typeof(Program).Assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? "Game Shelf";

    /// <summary>Major.Minor.Patch from the build (Directory.Build.props), comparable with Settings → Apps.</summary>
    public static readonly Version Version = Normalize(typeof(Program).Assembly.GetName().Version);
    public static string VersionText => Version.ToString(3);

    private static Version Normalize(Version? v) => v is null ? new Version(0, 0, 0) : new Version(v.Major, v.Minor, Math.Max(0, v.Build));

    [STAThread]
    private static int Main(string[] args)
    {
        // Any DLL loaded from here on comes from System32, never from the Downloads folder setup runs in.
        Native.SetDefaultDllDirectories(Native.LOAD_LIBRARY_SEARCH_SYSTEM32);

        var options = Options.Parse(args);
        if (options is null)
        {
            Dialogs.Error("Unknown option", "Run GameShelfSetup.exe without options to install, or with /S to install silently.");
            return ExitCode.BadArguments;
        }
        Locations.Log($"setup {VersionText} started: {string.Join(' ', args)}");
        try
        {
            return options.Uninstall ? Uninstaller.Run(options) : Installer.Run(options);
        }
        catch (Exception ex)
        {
            Locations.Log("unexpected: " + ex);
            if (!options.Silent) Dialogs.Error("Something went wrong", ex.Message + "\n\nDetails: " + Locations.LogFile);
            return ExitCode.Error;
        }
    }
}
