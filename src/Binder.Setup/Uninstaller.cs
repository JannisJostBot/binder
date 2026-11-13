using System.Diagnostics;
using Binder.Shared;
using Microsoft.Win32;

namespace Binder.Setup;

/// <summary>
/// Settings → Apps → Uninstall runs <c>Uninstall.exe /uninstall</c> from the install folder. An exe can't delete
/// itself while it runs, so that first copy only asks, then hands over to a copy of itself in a new temp folder
/// and exits. The temp copy does the work and finally removes its own folder.
/// </summary>
internal static class Uninstaller
{
    private const int UninstallButton = 100, StopTrackingButton = 101, PutBackRadio = 200, KeepRadio = 201;
    private const string TempPrefix = "Binder-uninstall-";

    /// <summary>The uninstall went through (not cancelled, no unexpected error): setup's log can go too.</summary>
    private static bool _done;

    public static int Run(Options options)
    {
        if (!options.Continue)
        {
            if (!options.Silent)
            {
                // Imported shortcuts: put back (the default) or not, asked only when there are some.
                bool hasShortcuts = HasImportedShortcuts();
                var confirm = Dialogs.Show(Text.UninstallTitle,
                    Text.UninstallSummary + (hasShortcuts ? "\n\n" + Text.UninstallShortcuts : "") +
                    (GameModeHelper.IsAllowed ? "\n\n" + Text.UninstallHelper : ""),
                    [(UninstallButton, Text.Uninstall)], Dialogs.CommonCancel, Dialogs.IdCancel,
                    verification: Text.AlsoDeleteData,
                    radios: hasShortcuts ? [(PutBackRadio, Text.PutShortcutsBack), (KeepRadio, Text.KeepShortcutsHere)] : null);
                if (confirm.Button != UninstallButton) return ExitCode.Cancelled;
                options = options with { RemoveData = confirm.Verified, KeepShortcuts = hasShortcuts && confirm.Radio == KeepRadio };
            }
            if (Locations.IsInside(Environment.ProcessPath, Locations.InstallDir)) return HandOver(options);
        }
        return Work(options);
    }

    private static int HandOver(Options options)
    {
        var dir = Path.Combine(Path.GetTempPath(), TempPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var copy = Path.Combine(dir, Locations.UninstallerExe);
        File.Copy(Environment.ProcessPath!, copy);
        var info = new ProcessStartInfo(copy) { UseShellExecute = false, WorkingDirectory = dir };
        foreach (var arg in options.ToArguments(continuing: true)) info.ArgumentList.Add(arg);
        Process.Start(info)?.Dispose();
        Locations.Log("uninstall handed over to " + copy);
        return ExitCode.Ok;
    }

    private static int Work(Options options)
    {
        Locations.Log($"uninstalling (remove data: {options.RemoveData}, keep shortcuts: {options.KeepShortcuts})");

        if (RunningApp.WatcherRunning())
        {
            Locations.Log("a game is still being tracked");
            if (!options.Silent)
            {
                var answer = Dialogs.Show(Text.GameRunning, Text.GameRunningDetail,
                    [(StopTrackingButton, Text.UninstallAnyway)], Dialogs.CommonCancel, Dialogs.IdCancel, warning: true);
                Locations.Log($"answer: {answer.Button}");
                if (answer.Button != StopTrackingButton) return Finish(ExitCode.Cancelled);
            }
            RunningApp.StopWatcher();
        }
        RunningApp.QuitWidget();
        // Before the app's files go: Game mode's own changes (power mode, Game Bar, closed apps, OneDrive) left by
        // a game that was still running or a session that didn't end normally, then the admin part.
        RestoreGameMode();
        bool helperLeft = !GameModeHelper.Remove(options.Silent);

        // Shortcuts put back unless the user chose not to; those that couldn't go back are never deleted below.
        bool shortcutsLeft = !options.KeepShortcuts && !RestoreShortcuts();
        bool shortcutsKept = options.KeepShortcuts && HasImportedShortcuts();

        // Start with Windows: only our own entry (one that starts the installed exe).
        using (var run = Registry.CurrentUser.OpenSubKey(Locations.RunKey, writable: true))
        {
            if (run?.GetValue(Locations.RunValue) is string command && command.Contains(Locations.InstallDir, StringComparison.OrdinalIgnoreCase))
                run.DeleteValue(Locations.RunValue, throwOnMissingValue: false);
        }
        TryDelete(Locations.StartMenuLink);
        Registry.CurrentUser.DeleteSubKeyTree(Locations.UninstallKey, throwOnMissingSubKey: false);

        // Only the files setup put there; anything else the user added stays, and so does the folder then.
        // The list the last setup wrote, plus what this copy itself installs (in case the list is gone).
        var files = InstalledFiles.Read();
        using (var payload = Payload.Read())
            if (payload is not null) files.AddRange(payload.Files.Select(f => f.Path));
        files.AddRange([.. Locations.LegacyFiles, InstalledFiles.ListName]);
        files = files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Installer.RemoveOldFiles(files);
        InstalledFiles.Remove(files, DeleteWithRetry);
        try
        {
            if (Directory.Exists(Locations.InstallDir) && !Locations.IsReparsePoint(Locations.InstallDir) &&
                !Directory.EnumerateFileSystemEntries(Locations.InstallDir).Any())
                Directory.Delete(Locations.InstallDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        // The cache always goes: covers, icons, logs, Game mode's journal and results (put back above), the update
        // downloads. Nothing in it is the user's own; it's all made again if Binder is installed again.
        DeleteTree(Locations.CacheDir, keep: []);
        if (options.RemoveData)
        {
            // Never the imported shortcuts if they couldn't go back: they're the user's files. Ones the user chose not
            // to put back go with the data, as the dialog said.
            DeleteTree(Locations.DataDir, keep: shortcutsLeft ? [Locations.ImportedShortcutsDir, Path.Combine(Locations.DataDir, "imported-shortcuts.json")] : []);
        }
        Native.SHChangeNotify(Native.SHCNE_ASSOCCHANGED, 0, IntPtr.Zero, IntPtr.Zero);
        Locations.Log("uninstalled" + (shortcutsLeft ? "; some imported shortcuts are still in " + Locations.ImportedShortcutsDir : ""));
        _done = true;

        if (!options.Silent)
        {
            var content = shortcutsLeft
                ? Text.ShortcutsLeft(Locations.ImportedShortcutsDir)
                : options.RemoveData ? Text.DataDeleted
                : Text.DataKept;
            if (shortcutsKept && !options.RemoveData) content += "\n\n" + Text.ShortcutsKept(Locations.ImportedShortcutsDir);
            if (helperLeft)
                content += "\n\n" + Text.HelperLeft(ElevatedPart.InstallDir);
            Dialogs.Show(Text.Removed, content, [], Dialogs.CommonClose, Dialogs.IdClose);
        }
        return Finish(ExitCode.Ok);
    }

    /// <summary>
    /// Runs the installed watcher's <c>--restore --quiet</c> when Game mode's journal is there (a game was being
    /// played, or a session didn't end normally): it puts back what's still applied, without starting the widget.
    /// </summary>
    private static void RestoreGameMode()
    {
        var journal = GameModeInfo.JournalPath;
        var watcher = Path.Combine(Locations.InstallDir, Locations.WatcherExe);
        if (!File.Exists(journal) || !File.Exists(watcher) || Locations.IsReparsePoint(watcher)) return;
        var info = new ProcessStartInfo(watcher) { UseShellExecute = false, WorkingDirectory = Locations.InstallDir };
        info.ArgumentList.Add("--restore");
        info.ArgumentList.Add("--quiet");
        try
        {
            using var process = Process.Start(info);
            if (process is null || !process.WaitForExit(120_000))
            {
                Locations.Log("Game mode restore didn't finish in time");
                return;
            }
            Locations.Log($"Game mode restore: exit code {process.ExitCode}");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Locations.Log("couldn't restore Game mode: " + ex.Message);
        }
    }

    /// <summary>There are shortcuts in our folder that came from a desktop (an import), so the question is worth asking.</summary>
    private static bool HasImportedShortcuts()
    {
        try
        {
            return Directory.Exists(Locations.ImportedShortcutsDir) &&
                   Directory.EnumerateFiles(Locations.ImportedShortcutsDir).Any(f =>
                       f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".url", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Asks the installed widget to move every imported shortcut back. True when none is left in our folder.</summary>
    private static bool RestoreShortcuts()
    {
        if (!Directory.Exists(Locations.ImportedShortcutsDir) || !Directory.EnumerateFiles(Locations.ImportedShortcutsDir).Any()) return true;
        if (!File.Exists(Locations.Widget)) return false;
        var info = new ProcessStartInfo(Locations.Widget) { UseShellExecute = false, WorkingDirectory = Locations.InstallDir };
        info.ArgumentList.Add("--restore-shortcuts");
        try
        {
            using var process = Process.Start(info);
            if (process is null || !process.WaitForExit(30_000)) return false;
            Locations.Log($"restore shortcuts: exit code {process.ExitCode}");
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Locations.Log("couldn't restore shortcuts: " + ex.Message);
            return false;
        }
    }

    /// <summary>Deletes a folder of ours without following links out of it: a link is removed, not entered.</summary>
    private static void DeleteTree(string dir, string[] keep)
    {
        if (!Directory.Exists(dir)) return;
        try
        {
            var info = new DirectoryInfo(dir);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                info.Delete();
                return;
            }
            foreach (var entry in info.EnumerateFileSystemInfos())
            {
                if (keep.Contains(entry.FullName, StringComparer.OrdinalIgnoreCase)) continue;
                if (entry is DirectoryInfo sub) DeleteTree(sub.FullName, keep);
                else
                {
                    entry.Attributes = FileAttributes.Normal;
                    entry.Delete();
                }
            }
            if (!info.EnumerateFileSystemInfos().Any()) info.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Locations.Log($"couldn't delete all of {dir}: {ex.Message}");
        }
    }

    /// <summary>Uninstall.exe in the install folder is still running for a moment after it handed over.</summary>
    private static void DeleteWithRetry(string path)
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(200);
            }
        }
        Locations.Log("couldn't delete " + path);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// The temp copy removes its own folder once it has exited, through cmd from System32 a few seconds later.
    /// Only for a folder we made, and only when its path has no characters cmd would treat specially.
    /// </summary>
    private static int Finish(int code)
    {
        var self = Environment.ProcessPath;
        var dir = self is null ? null : Path.GetDirectoryName(self);
        if (dir is null || !Path.GetFileName(dir).StartsWith(TempPrefix, StringComparison.Ordinal) ||
            !Locations.IsInside(dir, Path.GetTempPath()) || !dir.All(c => char.IsLetterOrDigit(c) || c is '\\' or ':' or '.' or '_' or '-' or ' '))
            return code;

        // After a completed uninstall, setup's own log goes too (kept after a failure or a cancel, to look at).
        var log = Locations.LogFile;
        bool dropLog = _done && Locations.IsInside(log, Path.GetTempPath()) && Path.GetFileName(log) == "BinderSetup.log" &&
                       log.All(c => char.IsLetterOrDigit(c) || c is '\\' or ':' or '.' or '_' or '-' or ' ');
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var info = new ProcessStartInfo(cmd, $"/d /c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"{dir}\"" + (dropLog ? $" & del /f /q \"{log}\"" : ""))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath(),
        };
        try { Process.Start(info)?.Dispose(); }
        catch (System.ComponentModel.Win32Exception) { } // left for Windows' temp clean-up
        return code;
    }
}
