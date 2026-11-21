using Binder.Library;
using Binder.Models;
using Binder.Services;
using Binder.ViewModels;

namespace Binder;

/// <summary>
/// Restart after a crash (spec §17): after the crash check of a game that has "Restart if it crashes", the restart
/// pill counts down 5 s, then the game is played again the ordinary way. Cancel, Esc or opening the panel shows the
/// crash card instead, so a crash is never hidden.
/// </summary>
public sealed partial class App : IRestartActions
{
    private Views.GameModeToast? _restartToast;
    private (Game Game, CrashRecord Crash)? _restarting;

    /// <summary>
    /// Started by the watcher only for a possible restart ("--crashed", with "Come back" off): the widget wasn't
    /// running, so it exits again once there's nothing to show.
    /// </summary>
    private bool _crashedOnly;

    /// <summary>
    /// After the crash check of the game that just ended: shows the restart pill and returns true when this crash
    /// is one to restart from. Not while a Game mode message waits for the user (the pill would cover it).
    /// </summary>
    private bool TryRestart(CrashRecord? crash, string? returnedGame)
    {
        if (crash is null || crash.GameId != returnedGame || _restarting is not null) return false;
        if (State.Games.FirstOrDefault(g => g.Id == crash.GameId) is not { } game) return false;
        if (!CrashAnalysis.ShouldRestart(game, crash, State.Sessions, State.Crashes)) return false;
        if (_gameModeToast is { WaitsForUser: true }) return false;

        _restarting = (game, crash);
        _gameModeToast?.Close(); // "Back to normal" gives way; Game mode is applied again with the game
        var toast = new Views.GameModeToast(new RestartViewModel(this, game.Name));
        Views.WindowClosed.Once(toast, () =>
        {
            if (ReferenceEquals(_restartToast, toast)) _restartToast = null;
        });
        _restartToast = toast;
        toast.ShowQuietly();
        return true;
    }

    private (Game Game, CrashRecord Crash)? EndRestart()
    {
        var restarting = _restarting;
        _restarting = null;
        var toast = _restartToast;
        _restartToast = null;
        toast?.Close();
        return restarting;
    }

    void IRestartActions.RestartNow()
    {
        if (EndRestart() is not { } r) return;
        CrashStore.MarkSeen(State.Crashes, r.Crash); // like Play again on the card
        var game = r.Game;
        if (_panel is { IsClosing: false } panel)
        {
            panel.PlayAfterCrash(game); // the panel is up: its own launch toast and countdown
            return;
        }
        if (game.AntiCheatReminder && game.AntiCheats is { Count: > 0 } && PcChecks.RunningConflicts() is { Count: > 0 })
        {
            OpenPanel(); // the question about conflicting programs needs the panel
            _panel?.PlayAfterCrash(game);
            return;
        }

        // No panel: start it and exit right away (nothing to count down on); a problem opens the panel instead.
        if (Launcher.StartGame(game) is { } launchError)
        {
            OpenPanel();
            _panel?.ShowNotice(Strings.Format(Strings.CouldntStart, game.Name), launchError);
            return;
        }
        string? watchProblem = !Launcher.CanWatch(game) ? Strings.StayingOpenNoWatch
            : Launcher.StartWatcher(game, Settings.ComeBackOnClose) is { } watcherError ? Strings.Format(Strings.StayingOpenNoWatcher, watcherError)
            : null;
        if (watchProblem is null)
        {
            Quit(); // during the game only the watcher remains
            return;
        }
        OpenPanel(); // nothing would bring the widget back, so stay
        _panel?.ShowNotice(Strings.Format(Strings.StartedGame, game.Name), watchProblem);
    }

    void IRestartActions.RestartCancelled()
    {
        if (EndRestart() is not { } r) return;
        OpenPanel();
        _panel?.CrashesChecked(r.Crash);
    }

    /// <summary>The panel opened another way (tray, shortcut) while the pill counts: no restart; the crash card shows.</summary>
    private void RestartGivesWayToPanel()
    {
        if (EndRestart() is { } r) _panel?.CrashesChecked(r.Crash);
    }

    /// <summary>Exits once nothing is on screen or pending (a "--crashed" or "--restored" start that's done).</summary>
    private void QuitIfIdle()
    {
        if (_quitting || _panel is not null || _kept is not null || _restarting is not null || _crashChecks > 0 || _gameModeToast is not null) return;
        if (_sound != 0) _quitAfterSound = true; // after the sound has played
        else Quit();
    }
}
