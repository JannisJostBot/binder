using System.Diagnostics;
using Binder.Library;
using Binder.Models;

namespace Binder.Services;

/// <summary>
/// "Open game log" on the crash card (milestone 11): Unity's Player.log and Unreal's Saved\Logs, found from the
/// engine detection. Names read from the game's folder are untrusted, so they must be plain folder names, and
/// only an existing .log file is ever opened (with its default app, Notepad).
/// </summary>
internal static class GameLogs
{
    public static string? Find(Game game)
    {
        var folder = GameActions.InstallFolder(game);
        if (folder is null) return null;
        try
        {
            return game.Engine switch
            {
                GameEngine.Unity => UnityLog(folder),
                GameEngine.Unreal => UnrealLog(folder),
                _ => null,
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    public static void Open(string path)
    {
        if (!path.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return;
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
    }

    /// <summary>%USERPROFILE%\AppData\LocalLow\{company}\{product}\Player.log, named in {Game}_Data\app.info.</summary>
    private static string? UnityLog(string folder)
    {
        foreach (var data in Directory.EnumerateDirectories(folder, "*_Data").Take(4))
        {
            var info = Path.Combine(data, "app.info");
            if (!File.Exists(info) || new FileInfo(info).Length > 1024) continue;
            var lines = File.ReadAllLines(info);
            if (lines.Length < 2 || !IsPlainName(lines[0]) || !IsPlainName(lines[1])) continue;
            var localLow = Path.Combine(Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))!, "LocalLow");
            var log = Path.Combine(localLow, lines[0].Trim(), lines[1].Trim(), "Player.log");
            if (File.Exists(log)) return log;
        }
        return null;
    }

    /// <summary>
    /// {Project}\Binaries\Win64\{Project}-Win64-Shipping.exe names the project; its log is
    /// %LOCALAPPDATA%\{Project}\Saved\Logs\{Project}.log, or the same under the install folder.
    /// </summary>
    private static string? UnrealLog(string folder)
    {
        foreach (var dir in Directory.EnumerateDirectories(folder).Take(64))
        {
            var project = Path.GetFileName(dir);
            if (project.Equals("Engine", StringComparison.OrdinalIgnoreCase) || !IsPlainName(project)) continue;
            var binaries = Path.Combine(dir, "Binaries", "Win64");
            if (!Directory.Exists(binaries) || !Directory.EnumerateFiles(binaries, "*-Win64-Shipping.exe").Any()) continue;
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), folder })
            {
                var log = Path.Combine(root, project, "Saved", "Logs", project + ".log");
                if (File.Exists(log)) return log;
            }
        }
        return null;
    }

    /// <summary>A single folder name: no separators, no "..", nothing Windows rejects in a name.</summary>
    private static bool IsPlainName(string name)
    {
        name = name.Trim();
        return name.Length is > 0 and <= 100 && name != "." && name != ".." &&
               name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }
}
