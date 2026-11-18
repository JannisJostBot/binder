// DEBUG BUILDS ONLY. Everything in src/Binder/Dev is left out of Release builds (Binder.csproj), so none of it ships;
// publish.ps1 stops if DevPanel.Marker turns up in the published files. Nothing here writes the user's data.
using Binder.Models;
using Binder.ViewModels;

namespace Binder;

/// <summary>The debug panel's actions: the real toasts and cards, shown with made-up data.</summary>
public sealed partial class App
{
    private Views.GameModeToast? _devRestartPill;

    partial void DevCommand(string[] args, ref bool handled)
    {
        if (!args.Contains("--dev")) return;
        handled = true;
        Dev.DevPanel.Show(this);
    }

    /// <summary>The first game in the library, for the cards that need a real one.</summary>
    internal Game? DevGame => State.Games.FirstOrDefault();

    /// <summary>"Game mode on", as the watcher reports it with "--applied".</summary>
    internal void DevApplied(Shared.RestoreResult result) => ShowGameModeToast(Views.GameModeToast.Kind.Applied, result);

    /// <summary>"Back to normal", as the watcher reports it with "--restored" (with its sound, if that's on).</summary>
    internal void DevRestored(Shared.RestoreResult result) => ShowRestored(result.Encode(), exitWhenClosed: false);

    /// <summary>The debug panel's "Quarter speed": the Game mode toasts melt four times slower while it's ticked.</summary>
    internal bool DevSlowToasts { get; set; }

    partial void DevToastSpeed(ref float slow)
    {
        if (DevSlowToasts) slow = 4;
    }

    /// <summary>The restart pill after a crash; its countdown and Cancel only close it, nothing is started.</summary>
    internal void DevRestartPill()
    {
        _devRestartPill?.Close();
        Views.GameModeToast? toast = null;
        var actions = new Dev.DevRestartActions(() => toast?.Close());
        toast = new Views.GameModeToast(new RestartViewModel(actions, DevGame?.Name ?? "Test game"));
        toast.Closed += (_, _) =>
        {
            if (ReferenceEquals(_devRestartPill, toast)) _devRestartPill = null;
        };
        _devRestartPill = toast;
        toast.ShowQuietly();
    }

    /// <summary>Opens the panel, then shows something in it.</summary>
    internal void DevInPanel(Action<MainWindow> show)
    {
        OpenPanel();
        if (_panel is { } panel) show(panel);
    }

    private Views.GameKeyFlash? _devFlash;

    /// <summary>A Steam game if there is one (its banner and logo show in the flash), else the first.</summary>
    internal Game? DevSteamGame => State.Games.FirstOrDefault(g => g.Source == GameSource.Steam) ?? DevGame;

    /// <summary>A game that isn't Steam's (the cover beside the name), else the first.</summary>
    internal Game? DevOtherGame => State.Games.FirstOrDefault(g => g.Source != GameSource.Steam) ?? DevGame;

    /// <summary>
    /// Game shortcuts' flash (spec §26) through its real window: the bar empties over the real wait, then it closes,
    /// or halfway it shows "Cancelled" as Esc does. Nothing is started and no keyboard hook is made.
    /// </summary>
    internal void DevGameKeyFlash(Game game, int number, bool cancel)
    {
        _devFlash?.Close();
        var flash = new Views.GameKeyFlash(game, number, Views.Format.HotkeyLetter(Settings.Hotkey));
        flash.Closed += (_, _) =>
        {
            if (ReferenceEquals(_devFlash, flash)) _devFlash = null;
        };
        _devFlash = flash;
        flash.ShowQuietly();
        flash.StartWait(Library.GameKeys.Wait);
        var timer = _dispatcher.CreateTimer();
        timer.Interval = cancel ? Library.GameKeys.Wait / 2 : Library.GameKeys.Wait;
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            if (!ReferenceEquals(_devFlash, flash)) return;
            if (cancel) flash.ShowCancelled(); // it closes by itself after a moment
            else flash.Close();
        };
        timer.Start();
    }

    /// <summary>The pill for a number without a game: "Nothing on 4 …", or "No game played yet" for 0.</summary>
    internal void DevGameKeyNothing(int number)
    {
        _devFlash?.Close();
        var (title, detail) = number == Library.GameKeys.LastPlayed
            ? (Strings.NoGamePlayedYet, (string?)null)
            : (Strings.Format(Strings.NothingOnNumber, number), Strings.NothingOnNumberDetail);
        var notice = new Views.GameKeyFlash(number, title, detail);
        notice.Closed += (_, _) =>
        {
            if (ReferenceEquals(_devFlash, notice)) _devFlash = null;
        };
        _devFlash = notice;
        notice.ShowQuietly();
    }

    /// <summary>The list the held shortcut shows (spec §26), with the real numbers; it closes after 4 s.</summary>
    internal void DevGameKeyList(bool empty)
    {
        _devFlash?.Close();
        var shelf = State.Games.Where(g => !g.Hidden && Settings.IsSourceEnabled(g.Source)).ToList();
        var list = new Views.GameKeyFlash(Library.GameKeys.LastPlayedGame(shelf, State.Sessions),
            empty ? [] : Library.GameKeys.Given(shelf, Settings.GameKeySlots));
        list.Closed += (_, _) =>
        {
            if (ReferenceEquals(_devFlash, list)) _devFlash = null;
        };
        _devFlash = list;
        list.ShowQuietly();
        var timer = _dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(4);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => list.Close();
        timer.Start();
    }

    internal void DevCloseToasts()
    {
        _devFlash?.Close();
        _gameModeToast?.Close();
        _devRestartPill?.Close();
        _panel?.DevCloseToast();
    }
}
