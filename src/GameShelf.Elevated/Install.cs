using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using GameShelf.Shared;
using static GameShelf.Elevated.Native;

namespace GameShelf.Elevated;

/// <summary>
/// %ProgramData%\GameShelf: only admins and SYSTEM may write, users may read (the watcher reads results there).
/// Users can create folders in ProgramData, so one that is there already counts as ours only if its owner is
/// Administrators or SYSTEM (which a user can't set) and it isn't a link.
/// </summary>
internal static class DataFolder
{
    private const string Sddl = "O:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;FRFX;;;BU)";

    public static bool IsOurs()
    {
        var dir = ElevatedPart.DataDir;
        try
        {
            var info = new DirectoryInfo(dir);
            if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        if (GetNamedSecurityInfoW(dir, SE_FILE_OBJECT, OWNER_SECURITY_INFORMATION, out var owner, IntPtr.Zero, IntPtr.Zero,
                IntPtr.Zero, out var sd) != 0) return false;
        try
        {
            if (!ConvertSidToStringSidW(owner, out var text)) return false;
            try
            {
                var sid = Marshal(text);
                return sid is "S-1-5-32-544" or "S-1-5-18"; // Administrators, SYSTEM
            }
            finally
            {
                LocalFree(text);
            }
        }
        finally
        {
            LocalFree(sd);
        }
    }

    /// <summary>Creates the folder with its ACL, or resets the ACL of ours; one that isn't ours is moved aside first.</summary>
    public static bool Ensure()
    {
        var dir = ElevatedPart.DataDir;
        if (Directory.Exists(dir) && !IsOurs())
        {
            var aside = dir + ".old-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            if (!MoveFileExW(dir, aside, 0)) return false;
        }
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(Sddl, 1, out var sd, IntPtr.Zero)) return false;
        try
        {
            if (!Directory.Exists(dir))
            {
                var attributes = new SECURITY_ATTRIBUTES { nLength = System.Runtime.InteropServices.Marshal.SizeOf<SECURITY_ATTRIBUTES>(), lpSecurityDescriptor = sd };
                if (!CreateDirectoryW(dir, in attributes)) return false;
            }
            else
            {
                if (!GetSecurityDescriptorDacl(sd, out _, out var dacl, out _) || !GetSecurityDescriptorOwner(sd, out var owner, out _)) return false;
                if (SetNamedSecurityInfoW(dir, SE_FILE_OBJECT, OWNER_SECURITY_INFORMATION | DACL_SECURITY_INFORMATION | PROTECTED_DACL_SECURITY_INFORMATION,
                        owner, IntPtr.Zero, dacl, IntPtr.Zero) != 0) return false;
            }
        }
        finally
        {
            LocalFree(sd);
        }
        return IsOurs();
    }

    private static unsafe string Marshal(IntPtr text) => new((char*)text);
}

/// <summary>
/// "Allow…" (<c>--install --user &lt;sid&gt;</c>, with the one UAC prompt) and its removal (<c>--uninstall</c>, from the
/// installer's uninstall or Settings). Paths come from known folders only, never from the user's side.
/// </summary>
internal static class Install
{
    public static int Run(string sid)
    {
        if (!DataFolder.Ensure())
        {
            return 10;
        }
        Helper.Log($"Allow: for {sid}");
        if (!CopySelf())
        {
            Helper.Log("Allow: couldn't put the helper into Program Files");
            return 11;
        }
        if (!Register(sid, ElevatedPart.ApplyTask(sid), "--apply", atLogon: false) ||
            !Register(sid, ElevatedPart.RestoreTask(sid), "--restore", atLogon: true))
        {
            Helper.Log("Allow: couldn't register the tasks");
            return 12;
        }
        File.WriteAllText(ElevatedPart.MarkerPath(sid), ElevatedPart.HelperVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Helper.Log($"Allow: done (helper version {ElevatedPart.HelperVersion})");
        return 0;
    }

    public static int Remove(string sid)
    {
        bool ours = DataFolder.IsOurs();
        if (ours && Journal.Load() is { } journal && journal.Sid == sid)
        {
            Helper.Log("Remove: putting back changes still applied");
            Helper.Restore(journal);
        }
        Schtasks("/delete", "/tn", ElevatedPart.ApplyTask(sid), "/f");
        Schtasks("/delete", "/tn", ElevatedPart.RestoreTask(sid), "/f");
        if (ours)
        {
            TryDelete(ElevatedPart.MarkerPath(sid));
            TryDelete(ElevatedPart.ResultPath(sid));
            if (Directory.EnumerateFiles(ElevatedPart.DataDir, "allowed-*.txt").Any())
            {
                Helper.Log($"Remove: done for {sid}; other users still use the helper");
                return 0;
            }
            // The last user: the folder holds only our own files (nobody else can write there).
            try { Directory.Delete(ElevatedPart.DataDir, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        RemoveProgramFiles();
        return 0;
    }

    /// <summary>
    /// Copies this exe to Program Files and checks the copy's hash; an in-use old copy is moved aside first. The
    /// same exe already there is left alone; any other one (an older helper version) is replaced.
    /// </summary>
    private static bool CopySelf()
    {
        var source = Environment.ProcessPath;
        var target = ElevatedPart.InstalledExe;
        if (source is null) return false;
        if (string.Equals(Path.GetFullPath(source), target, StringComparison.OrdinalIgnoreCase)) return true;
        try
        {
            var bytes = File.ReadAllBytes(source);
            var hash = SHA256.HashData(bytes);
            var dir = new DirectoryInfo(ElevatedPart.InstallDir);
            if (dir.Exists && dir.Attributes.HasFlag(FileAttributes.ReparsePoint)) return false;
            var installed = new FileInfo(target);
            if (installed.Exists && !installed.Attributes.HasFlag(FileAttributes.ReparsePoint) && installed.Length == bytes.Length &&
                SHA256.HashData(File.ReadAllBytes(target)).AsSpan().SequenceEqual(hash))
            {
                Helper.Log("Allow: the helper in Program Files is already this one");
                return true;
            }
            dir.Create();
            var tmp = target + ".new";
            using (var file = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                file.Write(bytes);
                file.Flush(flushToDisk: true);
            }
            if (!SHA256.HashData(File.ReadAllBytes(tmp)).AsSpan().SequenceEqual(hash))
            {
                File.Delete(tmp);
                return false;
            }
            try
            {
                File.Move(tmp, target, overwrite: true);
            }
            catch (IOException)
            {
                // Running right now (a task): a running exe can be renamed, not replaced.
                var old = target + ".old";
                File.Move(target, old, overwrite: true);
                MoveFileExW(old, null, MOVEFILE_DELAY_UNTIL_REBOOT);
                File.Move(tmp, target);
            }
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Helper.Log("Allow: " + e.Message);
            return false;
        }
    }

    private static void RemoveProgramFiles()
    {
        var dir = ElevatedPart.InstallDir;
        if (!Directory.Exists(dir) || new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint)) return;
        foreach (var name in new[] { ElevatedPart.ExeName, ElevatedPart.ExeName + ".old", ElevatedPart.ExeName + ".new" })
        {
            var path = Path.Combine(dir, name);
            if (!File.Exists(path)) continue;
            try { File.Delete(path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                MoveFileExW(path, null, MOVEFILE_DELAY_UNTIL_REBOOT); // this exe itself, while it runs
            }
        }
        try { Directory.Delete(dir); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MoveFileExW(dir, null, MOVEFILE_DELAY_UNTIL_REBOOT); // removed once it's empty, at the next restart
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// One task, run as this user with highest privileges, only while they're signed in. Its security lets the
    /// user run it (read and execute) but not change it.
    /// </summary>
    private static bool Register(string sid, string name, string argument, bool atLogon)
    {
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.3" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Author>Game Shelf</Author>
                <Description>Game Shelf Game mode: {(atLogon ? "puts back what Game mode changed" : "applies Game mode while a game runs")}. Remove it in Game Shelf's Settings, Game mode.</Description>
                <SecurityDescriptor>D:P(A;;FA;;;BA)(A;;FA;;;SY)(A;;GRGX;;;{sid})</SecurityDescriptor>
              </RegistrationInfo>
              <Triggers>{(atLogon ? $"<LogonTrigger><UserId>{sid}</UserId></LogonTrigger>" : "")}</Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{sid}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>Queue</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT5M</ExecutionTimeLimit>
                <Priority>4</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>"{ElevatedPart.InstalledExe}"</Command>
                  <Arguments>{argument}</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
        // Written into our admin-only folder, so nothing can swap it before schtasks reads it.
        var file = Path.Combine(ElevatedPart.DataDir, "task.xml");
        try
        {
            File.WriteAllText(file, xml, Encoding.Unicode);
            int code = Schtasks("/create", "/tn", name, "/xml", file, "/f");
            if (code != 0) Helper.Log($"Allow: schtasks /create {name} failed ({code})");
            return code == 0;
        }
        finally
        {
            TryDelete(file);
        }
    }

    /// <summary>Runs System32\schtasks.exe without a window; its exit code, or -1.</summary>
    private static int Schtasks(params string[] args)
    {
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe");
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(psi);
            if (process is null) return -1;
            process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0 && error.Length > 0) Helper.Log("schtasks: " + error.Trim());
            return process.ExitCode;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Helper.Log("schtasks: " + e.Message);
            return -1;
        }
    }

}
