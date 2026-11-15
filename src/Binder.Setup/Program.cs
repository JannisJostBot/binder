using System.Reflection;

namespace Binder.Setup;

internal static class ExitCode
{
    public const int Ok = 0, Cancelled = 1, Error = 2, NeedsRuntime = 3, Downgrade = 4, BadArguments = 5, Busy = 6;
}

/// <summary>
/// Command line:
///   BinderSetup.exe                       install or update, with dialogs
///   BinderSetup.exe /S [/launch]          silent; /launch starts the app afterwards
///   BinderSetup.exe /S /allowdowngrade    silent, even over a newer version
///   BinderSetup.exe /S /update            from the app's updater (spec §25): afterwards the widget is started again
///                                         with --updated --background, also after a failure (it then sees the old
///                                         version and says the update couldn't be installed)
///   Uninstall.exe /uninstall [/S] [/removedata] [/keepshortcuts]
/// </summary>
internal sealed record Options(bool Uninstall, bool Silent, bool RemoveData, bool Launch, bool AllowDowngrade, bool Continue,
    bool KeepShortcuts = false, bool Update = false)
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
                "/keepshortcuts" => o with { KeepShortcuts = true }, // uninstall: don't put imported shortcuts back
                "/launch" => o with { Launch = true },
                "/allowdowngrade" => o with { AllowDowngrade = true },
                "/update" => o with { Update = true }, // the app started us and has exited: start it again with --updated
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
        if (KeepShortcuts) yield return "/keepshortcuts";
    }
}

internal static class Program
{
    /// <summary>Settings → Apps "publisher": the Company from Directory.Build.props, so there's one place to change it.</summary>
    public static readonly string Publisher =
        typeof(Program).Assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? "Binder";

    /// <summary>Major.Minor.Patch from the build (Directory.Build.props), comparable with Settings → Apps.</summary>
    public static readonly Version Version = Normalize(typeof(Program).Assembly.GetName().Version);
    public static string VersionText => Version.ToString(3);

    /// <summary>
    /// Waits for another setup to finish; true once it's this one's turn. A silent run (the updater) and the
    /// uninstaller's hand-over wait up to a minute; one opened by hand says at once that another is running.
    /// </summary>
    private static bool TakeTurn(Mutex mutex, int waitMs)
    {
        try
        {
            return mutex.WaitOne(waitMs);
        }
        catch (AbandonedMutexException)
        {
            return true; // the other one ended without releasing it
        }
    }

    private static Version Normalize(Version? v) => v is null ? new Version(0, 0, 0) : new Version(v.Major, v.Minor, Math.Max(0, v.Build));

    [STAThread]
    private static int Main(string[] args)
    {
        // Any DLL loaded from here on comes from System32, never from the Downloads folder setup runs in.
        Native.SetDefaultDllDirectories(Native.LOAD_LIBRARY_SEARCH_SYSTEM32);

        var options = Options.Parse(args);
        if (options is null)
        {
            Dialogs.Error(Text.UnknownOption, Text.Usage);
            return ExitCode.BadArguments;
        }
        Locations.Log($"setup {VersionText} started: {string.Join(' ', args)}");

        // One setup or uninstaller at a time (a double-click on the setup, the updater and a manual run): two would
        // replace the same files at once. The uninstaller's temp copy waits here for the copy that started it.
        using var running = new Mutex(false, @"Local\Binder.Setup");
        if (!TakeTurn(running, options.Silent || options.Continue ? 60_000 : 1_000))
        {
            Locations.Log("another setup is running");
            if (!options.Silent) Dialogs.Show(Text.AlreadyRunning, Text.AlreadyRunningDetail, [], Dialogs.CommonClose, Dialogs.IdClose);
            if (options.Update && !options.Uninstall) Installer.RestartAfterUpdate();
            return ExitCode.Busy;
        }
        try
        {
            return options.Uninstall ? Uninstaller.Run(options) : Installer.Run(options);
        }
        catch (Exception ex)
        {
            Locations.Log("unexpected: " + ex);
            if (!options.Silent) Dialogs.Error(Text.SomethingWentWrong, ex.Message + "\n\n" + Text.Details(Locations.LogFile));
            return ExitCode.Error;
        }
        finally
        {
            running.ReleaseMutex(); // the next one (the uninstaller's temp copy) goes on at once
            if (options.Update && !options.Uninstall) Installer.RestartAfterUpdate(); // the app is back, updated or not
        }
    }
}
