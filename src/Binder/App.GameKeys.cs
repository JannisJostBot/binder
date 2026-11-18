using Binder.Library;
using Binder.Models;
using Binder.Services;
using Binder.Views;
using Microsoft.UI.Dispatching;

namespace Binder;

/// <summary>
/// Game shortcuts (spec §26, boards 35–37): the panel's shortcut, then a number, plays a game without opening the
/// panel. The game's logo flashes above the taskbar for <see cref="GameKeys.Wait"/>; Esc in that time cancels, the
/// same number again starts it at once, another number switches. Then it plays as "--play" does (App.Play): no
/// panel, the watcher, Game mode. A one-shot timer exists only while the flash waits.
/// </summary>
public sealed partial class App
{
    private Views.GameKeyFlash? _flash;
    private Game? _flashGame;
    private int _flashNumber = -1;
    private DispatcherQueueTimer? _flashWait;

    /// <summary>Hooks the message window's game shortcuts up; called once, after it's made.</summary>
    private void InitGameKeys()
    {
        if (_messages is null) return;
        _messages.GameKeyPressed += OnGameKey;
        _messages.GameKeyEscape += CancelFlash;
        ApplyGameKeys();
    }

    /// <summary>The setting or the shortcut changed.</summary>
    public void ApplyGameKeys()
    {
        if (_messages is not null) _messages.GameKeysOn = Settings.GameKeysOn && Settings.Hotkey != Hotkey.Off;
    }

    /// <summary>The games a number can play: the shelf's (not hidden, their launcher on).</summary>
    private static List<Game> ShelfGames() => State.Games.Where(g => !g.Hidden && Settings.IsSourceEnabled(g.Source)).ToList();

    private void OnGameKey(int number)
    {
        if (_quitting) return;
        var game = GameKeys.Resolve(number, ShelfGames(), Settings.GameKeySlots, State.Sessions);
        if (_flash is not null && game is not null && number == _flashNumber && game.Id == _flashGame?.Id)
        {
            PlayFlashed(); // the same number again: no need to wait
            return;
        }
        CloseFlash();
        Footprint.Wake(); // out of efficiency mode while something shows
        if (game is null)
        {
            var (title, detail) = number == GameKeys.LastPlayed
                ? (Strings.NoGamePlayedYet, (string?)null)
                : (Strings.Format(Strings.NothingOnNumber, number), Strings.NothingOnNumberDetail);
            var notice = new Views.GameKeyFlash(number, title, detail);
            notice.Closed += (_, _) => RestIfIdle();
            notice.ShowQuietly();
            return;
        }

        _flashGame = game;
        _flashNumber = number;
        var flash = new Views.GameKeyFlash(game, number, Format.HotkeyLetter(Settings.Hotkey));
        flash.Closed += (_, _) =>
        {
            if (!ReferenceEquals(_flash, flash)) return;
            _flash = null;
            RestIfIdle();
        };
        _flash = flash;
        flash.ShowQuietly();
        flash.StartWait(GameKeys.Wait);
        if (_messages is not null) _messages.CatchEscape = true;
        _flashWait = _dispatcher.CreateTimer();
        _flashWait.Interval = GameKeys.Wait;
        _flashWait.IsRepeating = false;
        _flashWait.Tick += (_, _) => PlayFlashed();
        _flashWait.Start();
    }

    /// <summary>The wait is over (or the number was pressed again): the game plays, and the flash goes.</summary>
    private void PlayFlashed()
    {
        var game = _flashGame;
        CloseFlash();
        if (game is not null) PlayFromStart(game.Id, firstStart: false);
    }

    /// <summary>Esc while the flash waits: nothing starts, and it says so.</summary>
    private void CancelFlash()
    {
        if (_flash is not { } flash) return;
        StopFlashWait();
        _flash = null; // it closes by itself after showing "Cancelled"
        flash.Closed += (_, _) => RestIfIdle();
        flash.ShowCancelled();
    }

    /// <summary>Closes the flash at once (another number, Play, the panel opening, quitting).</summary>
    private void CloseFlash()
    {
        StopFlashWait();
        var flash = _flash;
        _flash = null;
        flash?.Close();
    }

    private void StopFlashWait()
    {
        _flashWait?.Stop();
        _flashWait = null;
        _flashGame = null;
        _flashNumber = -1;
        if (_messages is not null) _messages.CatchEscape = false;
    }

    /// <summary>Back to efficiency mode after a flash, unless the panel is open (it would have woken us anyway).</summary>
    private void RestIfIdle()
    {
        if (!_quitting && _panel is null && _flash is null) Footprint.Rest();
    }
}
