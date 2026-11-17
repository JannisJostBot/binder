using Binder.Library;
using Binder.Models;
using Binder.Services;

namespace Binder;

/// <summary>
/// "Binder.exe --play &lt;game id&gt;" (spec §22 c): what the Start menu's game shortcuts run. Plays the game as the Play
/// button does (silent Steam, the watcher, Game mode, playtime, crash help), without opening the panel. Only an id of
/// a game in the library is played; anything else opens the panel as an ordinary start would.
/// </summary>
public sealed partial class App
{
    /// <summary>The id after "--play", or null. Its shape is checked against the library, not trusted.</summary>
    private static string? PlayArgument(string[] args)
    {
        int i = Array.IndexOf(args, "--play");
        return i >= 0 && i + 1 < args.Length && args[i + 1].Length is > 0 and <= 256 ? args[i + 1] : null;
    }

    private void PlayFromStart(string id, bool firstStart)
    {
        Game? game = null;
        foreach (var g in State.Games)
            if (g.Id == id && !g.Hidden && Settings.IsSourceEnabled(g.Source))
            {
                game = g;
                break;
            }
        if (game is null)
        {
            OpenPanel(); // not (or no longer) on the shelf: show it instead of doing nothing
            return;
        }

        // What needs the panel goes through it, exactly as Play there: a game that's gone (it leaves the shelf with a
        // notice), or the anti-cheat reminder's question.
        if (GameFiles.IsGone(game) ||
            (game.AntiCheatReminder && game.AntiCheats is { Count: > 0 } && PcChecks.RunningConflicts() is { Count: > 0 }))
        {
            OpenPanel();
            _panel?.PlayFromStart(game);
            return;
        }

        if (MainWindow.Repeats.IsRepeat(game.Id, Environment.TickCount64)) return; // a double-click in Start
        CancelCountdown();
        if (Launcher.StartGame(game) is { } launchError)
        {
            ShowLaunchProblem(Strings.Format(Strings.CouldntStart, game.Name), launchError);
            return;
        }
        MainWindow.Repeats.Started(game.Id, Environment.TickCount64);
        if (!Launcher.CanWatch(game))
        {
            // Nothing tells when it closes: the panel isn't needed, but a running widget stays as it is.
            if (firstStart) Quit();
            return;
        }
        if (Launcher.StartWatcher(game, Settings.ComeBackOnClose) is { } watcherError)
        {
            // Without a watcher nothing would bring the widget back, so it stays and says why.
            ShowLaunchProblem(Strings.Format(Strings.StartedGame, game.Name), Strings.Format(Strings.StayingOpenNoWatcher, watcherError));
            return;
        }
        Quit(); // nothing was shown, so no countdown: during the game only the watcher remains
    }

    private void ShowLaunchProblem(string title, string detail)
    {
        OpenPanel();
        _panel?.ShowNotice(title, detail);
    }
}
