using Binder.Library;
using Binder.Models;
using Binder.Views;

namespace Binder.ViewModels;

/// <summary>One number on Settings' "Game shortcuts" card: 0 is the last game played; 1–9 hold the game picked for them.</summary>
public sealed record GameKeyRow(int Number, string? GameName, int ChoiceIndex);

/// <summary>Settings › Opening Binder › Game shortcuts (spec §26, board 36).</summary>
public sealed partial class SettingsViewModel
{
    private static readonly string[] GameKeyTexts = [nameof(GameKeysText), nameof(GameKeyRows), nameof(GameKeyGameNames)];
    private List<Game> _keyGames = []; // the games a number can be given to, by name, in the order of GameKeyGameNames

    public bool GameKeysOn
    {
        get => _settings.GameKeysOn;
        set => Set(_settings.GameKeysOn, value, v => _settings.GameKeysOn = v, SettingChange.GameKeys, GameKeyTexts, nameof(GameKeysOn));
    }

    /// <summary>The card's second line: how many games have a number.</summary>
    public string GameKeysText
    {
        get
        {
            if (!_settings.GameKeysOn) return Strings.Off;
            int count = GameKeyRows.Count(r => r.ChoiceIndex > 0);
            return Strings.Plural(count, Strings.GameCountOne, Strings.GameCount);
        }
    }

    /// <summary>"None", then the shelf's games by name: the list each number 1–9 picks from.</summary>
    public string[] GameKeyGameNames { get; private set; } = [];

    /// <summary>0 (the last game played, not changeable), then 1–9 with the index of their game in <see cref="GameKeyGameNames"/>.</summary>
    public GameKeyRow[] GameKeyRows { get; private set; } = [];

    private void LoadGameKeys()
    {
        var byName = StringComparer.Create(System.Globalization.CultureInfo.CurrentCulture, ignoreCase: true);
        _keyGames = [.. _games.Where(g => !g.Hidden && _settings.IsSourceEnabled(g.Source)).OrderBy(g => g.Name, byName)];
        var index = new Dictionary<string, int>(_keyGames.Count);
        for (int i = 0; i < _keyGames.Count; i++) index.TryAdd(_keyGames[i].Id, i + 1);
        GameKeyGameNames = [Strings.None, .. _keyGames.Select(g => g.Name)];
        var rows = new List<GameKeyRow> { new(GameKeys.LastPlayed, _actions.LastPlayedGame()?.Name, -1) };
        foreach (int number in GameKeys.UserSlots)
        {
            int choice = GameKeys.IdOn(_settings.GameKeySlots, number) is { } id && index.TryGetValue(id, out var i) ? i : 0;
            rows.Add(new GameKeyRow(number, choice > 0 ? _keyGames[choice - 1].Name : null, choice));
        }
        GameKeyRows = [.. rows];
    }

    /// <summary>A number's list changed: gives it the picked game, or takes it away ("None").</summary>
    public void PickGameKey(int number, int choiceIndex)
    {
        if (number is < 1 or > 9 || choiceIndex < 0 || choiceIndex >= GameKeyGameNames.Length) return;
        var row = GameKeyRows.FirstOrDefault(r => r.Number == number);
        if (row is null || row.ChoiceIndex == choiceIndex) return;
        if (choiceIndex == 0)
        {
            if (GameKeys.IdOn(_settings.GameKeySlots, number) is { } id) _actions.SetGameKey(id, null);
            return;
        }
        _actions.SetGameKey(_keyGames[choiceIndex - 1].Id, number);
    }

    /// <summary>"Win+Shift+G, 3" for a row's tooltip and screen-reader name.</summary>
    public string GameKeyLabel(int number) => Format.GameKeyLabel(_settings.Hotkey, number);
}
