using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace GameShelf.Setup;

/// <summary>Install and update: same steps, the update just finds files to replace and an app to restart.</summary>
internal static class Installer
{
    private const int InstallButton = 100;

    /// <summary>The full disclaimer, as in the README.</summary>
    private const string Disclaimer =
        "Game Shelf is vibe-coded. Its code was written by an AI (Anthropic's Claude), directed and tested by Jannis Jost, " +
        "but not written or reviewed line by line by a human developer.\n\n" +
        "The software is provided \"as is\", without warranty of any kind, express or implied. That includes, but isn't " +
        "limited to, the warranties of merchantability, fitness for a particular purpose and non-infringement. In no event " +
        "shall the author or anyone involved be liable for any claim, damages or other liability arising from, out of, or " +
        "in connection with the software or its use. You use it at your own risk.\n\n" +
        "Game Shelf isn't affiliated with or endorsed by Valve, Epic Games, CD PROJEKT (GOG), Electronic Arts, Ubisoft, Rockstar Games, " +
        "Hypixel Studios, Microsoft or any other " +
        "company whose launchers it works with. Their names and icons belong to their owners.";

    public static int Run(Options options)
    {
        var version = Program.Version;
        using var payload = Payload.Read();
        if (payload is null)
        {
            Locations.Log("no payload: built without publish.ps1");
            if (!options.Silent)
                Dialogs.Error(Text.Incomplete, Text.IncompleteDetail);
            return ExitCode.Error;
        }

        var installed = Locations.InstalledVersion();
        bool update = installed is not null;
        if (!options.Silent)
        {
            var (instruction, button) = installed switch
            {
                null => (Text.InstallTitle, Text.Install),
                _ when installed < version => (Text.UpdateTitle(Program.VersionText), Text.Update),
                _ when installed == version => (Text.AlreadyInstalled(Program.VersionText), Text.Reinstall),
                _ => (Text.NewerInstalled(installed!.ToString(3)), Text.ReplaceWith(Program.VersionText)),
            };
            var summary = update
                ? Text.UpdateSummary
                : Text.InstallSummary;
            var note = update
                ? Text.Versions(installed!.ToString(3), Program.VersionText)
                : Text.InstallsTo(Locations.InstallDir);
            // The README's disclaimer in full, with the button disabled until it's confirmed (/S implies it:
            // whoever scripts setup has read it). A classic setup window; Windows' task dialog if it can't open.
            bool? accepted = ConfirmWindow.Show(new ConfirmWindow.Content(instruction, summary, Disclaimer, note, button));
            if (accepted is null)
            {
                var confirm = Dialogs.Show(instruction, summary + "\n\n" + note, [(InstallButton, button)], Dialogs.CommonCancel,
                    InstallButton, verification: Text.AcceptDisclaimer, warning: installed > version,
                    required: true, details: (Disclaimer, Text.ShowDisclaimer, Text.HideDisclaimer));
                accepted = confirm.Button == InstallButton;
            }
            if (accepted != true) return ExitCode.Cancelled;
        }
        else if (installed > version && !options.AllowDowngrade)
        {
            Locations.Log($"refusing a silent downgrade from {installed} to {version}");
            return ExitCode.Downgrade;
        }

        Locations.Log(update ? $"updating {installed} -> {version}" : $"installing {version}");
        try
        {
            CheckFolders();

            // A running widget is closed and restarted on the new version. A running watcher (a game is being
            // played) keeps going on its old file, which is moved aside below; it brings back the new widget.
            bool widgetWasRunning = Native.FindWindow("GameShelf.MessageWindow", null) != IntPtr.Zero;
            if (!RunningApp.QuitWidget()) Locations.Log("the widget didn't quit; its file is moved aside instead");

            Directory.CreateDirectory(Locations.InstallDir);
            var previous = InstalledFiles.Read();
            RemoveOldFiles(previous);
            foreach (var file in payload.Files)
                Place(file.Path, stream =>
                {
                    using var source = payload.Open(file);
                    source.CopyTo(stream);
                }, file.Sha256);
            Place(Locations.UninstallerExe, CopySelf, null);

            // Files of the previous version this one no longer has. The list is written first, so a failure
            // after it still leaves every file we wrote listed for the next setup or the uninstaller.
            var current = payload.Files.Select(f => f.Path).Append(Locations.UninstallerExe).Append(InstalledFiles.ListName).ToList();
            InstalledFiles.Write(current);
            var kept = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
            InstalledFiles.Remove(previous.Where(p => !kept.Contains(p)), TryDeleteFile);
            RemoveOldFiles(current); // the ones that weren't in use any more

            ShellLink.Create(Locations.StartMenuLink, Locations.Widget, Locations.InstallDir, Text.StartMenuDescription);
            WriteUninstallEntry(payload);
            Native.SHChangeNotify(Native.SHCNE_ASSOCCHANGED, 0, IntPtr.Zero, IntPtr.Zero);
            Locations.Log("done");

            // A widget that was running comes back as it was: waiting in the tray, or on the desktop in desktop mode.
            if (widgetWasRunning) StartWidget("--background");
            if (!options.Silent)
            {
                var done = Dialogs.Show(update ? Text.UpToDate : Text.Installed,
                    update ? Text.VersionLine(Program.VersionText) : Text.InStartMenu,
                    [], Dialogs.CommonClose, Dialogs.IdClose,
                    verification: widgetWasRunning ? null : Text.OpenNow, verified: !update && !widgetWasRunning);
                if (done.Verified && !widgetWasRunning) StartWidget(); // a second start would just toggle it
            }
            else if (options.Launch && !widgetWasRunning)
            {
                StartWidget();
            }
            return ExitCode.Ok;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SetupException or CryptographicException)
        {
            Locations.Log("failed: " + ex);
            if (!options.Silent)
                Dialogs.Error(update ? Text.UpdateFailed : Text.InstallFailed,
                    ex.Message + "\n\n" + Text.NothingLost);
            return ExitCode.Error;
        }
    }

    /// <summary>The install folder must be a real folder in the user's profile, not a link to somewhere else.</summary>
    private static void CheckFolders()
    {
        foreach (var dir in new[] { Locations.ProgramsRoot, Locations.InstallDir })
            if (Locations.IsReparsePoint(dir))
                throw new SetupException(Text.LinkedFolder(dir));
    }

    /// <summary>
    /// Writes one file next to its final place, checks it, then swaps it in. A file in use (a watcher tracking a
    /// game, or a widget that didn't quit) can't be replaced but can be renamed, so it's moved to .old first.
    /// </summary>
    private static void Place(string name, Action<Stream> write, string? sha256)
    {
        var target = InstalledFiles.FullPath(name) ?? throw new SetupException(Text.LinkOnTheWay(name));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (InstalledFiles.FullPath(name) is null) throw new SetupException(Text.LinkOnTheWay(name));
        var temp = target + ".new";
        File.Delete(temp);
        using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough))
            write(stream);

        if (sha256 is not null)
        {
            using var check = File.OpenRead(temp);
            var actual = Convert.ToHexString(SHA256.HashData(check));
            if (!actual.Equals(sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(temp);
                throw new SetupException(Text.CheckFailed(name));
            }
        }

        try
        {
            File.Move(temp, target, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Locations.Log($"{name} is in use; moving it aside");
            File.Move(target, target + ".old", overwrite: true);
            File.Move(temp, target);
        }
    }

    /// <summary>Leftovers of earlier updates: our own file names with .old or .new, nothing else.</summary>
    public static void RemoveOldFiles(IEnumerable<string> files)
    {
        foreach (var name in files)
        foreach (var suffix in new[] { ".old", ".new" })
            if (InstalledFiles.FullPath(name + suffix) is { } path) TryDeleteFile(path);
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // still running
    }

    private static void CopySelf(Stream destination)
    {
        var self = Environment.ProcessPath ?? throw new SetupException(Text.OwnFileNotFound);
        using var source = File.OpenRead(self);
        source.CopyTo(destination);
    }

    private static void WriteUninstallEntry(Payload payload)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Locations.UninstallKey)
            ?? throw new SetupException(Text.AppsEntryFailed);
        var uninstaller = Path.Combine(Locations.InstallDir, Locations.UninstallerExe);
        key.SetValue("DisplayName", "Game Shelf");
        key.SetValue("DisplayVersion", Program.VersionText);
        key.SetValue("Publisher", Program.Publisher);
        key.SetValue("DisplayIcon", $"\"{Locations.Widget}\",0");
        key.SetValue("InstallLocation", Locations.InstallDir);
        key.SetValue("UninstallString", $"\"{uninstaller}\" /uninstall");
        key.SetValue("QuietUninstallString", $"\"{uninstaller}\" /uninstall /S");
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        key.SetValue("EstimatedSize", (int)Math.Max(1, (payload.TotalBytes + new FileInfo(uninstaller).Length) / 1024), RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private static void StartWidget(params string[] args)
    {
        var info = new ProcessStartInfo(Locations.Widget) { UseShellExecute = false, WorkingDirectory = Locations.InstallDir };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        try
        {
            Process.Start(info)?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Locations.Log("couldn't start the widget: " + ex.Message);
        }
    }
}

internal sealed class SetupException(string message) : Exception(message);
