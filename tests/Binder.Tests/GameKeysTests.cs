using Binder.Library;
using Binder.Models;
using Binder.Services;
using Binder.ViewModels;

namespace Binder.Tests;

static partial class Tests
{
    /// <summary>Spec §26: game shortcuts, the panel's shortcut then a number.</summary>
    static void GameKeysTests()
    {
        // Number row and number pad, nothing else.
        Check(GameKeys.DigitOf(0x30) == 0 && GameKeys.DigitOf(0x39) == 9 && GameKeys.DigitOf(0x60) == 0 && GameKeys.DigitOf(0x69) == 9,
            "game keys: 0–9 on the number row and the number pad");
        Check(GameKeys.DigitOf(0x47) is null && GameKeys.DigitOf(0x1B) is null && GameKeys.DigitOf(0x6A) is null && GameKeys.DigitOf(0x2F) is null,
            "game keys: letters, Esc and the pad's * aren't numbers");

        // A game has one number; taking a number moves it and frees the game that had it.
        var slots = new Dictionary<string, string>();
        Check(GameKeys.Assign(slots, "hades", 1) is null && GameKeys.IdOn(slots, 1) == "hades", "game keys: a free number is given");
        Check(GameKeys.Assign(slots, "hades", 4) is null && GameKeys.IdOn(slots, 1) is null && GameKeys.SlotOf(slots, "hades") == 4,
            "game keys: a game moves to its new number");
        GameKeys.Assign(slots, "forza", 3);
        Check(GameKeys.Assign(slots, "hades", 3) == "forza" && GameKeys.SlotOf(slots, "forza") is null && GameKeys.IdOn(slots, 4) is null,
            "game keys: a taken number replaces its game and says which");
        Check(GameKeys.Assign(slots, "hades", null) is null && slots.Count == 0, "game keys: None takes the number away");
        bool threw = false;
        try { GameKeys.Assign(slots, "hades", 0); } catch (ArgumentOutOfRangeException) { threw = true; }
        Check(threw, "game keys: 0 can't be given (it's the last game played)");

        // Hand-edited or stale entries are ignored and pruned.
        var file = new Dictionary<string, string> { ["0"] = "a", ["10"] = "a", ["x"] = "a", ["2"] = "", ["5"] = "gone", ["6"] = "kept" };
        Check(GameKeys.IdOn(file, 0) is null && GameKeys.IdOn(file, 2) is null && GameKeys.IdOn(file, 6) == "kept",
            "game keys: only numbers 1–9 with an id count");
        Check(GameKeys.Prune(file, ["kept", "a"]) && file.Count == 1 && file["6"] == "kept" && !GameKeys.Prune(file, ["kept"]),
            "game keys: games that left the library and bad entries are dropped");

        // 0 is the newest session among the games on the shelf.
        var t = new DateTimeOffset(2026, 11, 1, 20, 0, 0, TimeSpan.Zero);
        var shelf = new List<Game> { new() { Id = "hades", Name = "Hades II" }, new() { Id = "bg3", Name = "Baldur's Gate 3" } };
        var sessions = new List<Session>
        {
            new() { GameId = "bg3", Start = t, End = t.AddHours(2) },
            new() { GameId = "hades", Start = t.AddHours(3), End = t.AddHours(4) },
            new() { GameId = "hidden", Start = t.AddHours(5), End = t.AddHours(6) }, // not on the shelf
            new() { GameId = "bg3", Start = t.AddHours(-9), End = t.AddHours(-8) },
        };
        Check(GameKeys.Resolve(0, shelf, null, sessions)?.Id == "hades", "game keys: 0 plays the last game played on the shelf");
        Check(GameKeys.Resolve(0, shelf, null, []) is null, "game keys: 0 with no session plays nothing");
        var given = new Dictionary<string, string> { ["2"] = "bg3", ["3"] = "hidden" };
        Check(GameKeys.Resolve(2, shelf, given, sessions)?.Id == "bg3" && GameKeys.Resolve(3, shelf, given, sessions) is null &&
              GameKeys.Resolve(4, shelf, given, sessions) is null, "game keys: a number plays its game only while it's on the shelf");
        GameKeyViewModelTests(shelf);
    }

    /// <summary>Spec §26: where games get their number (the menu, Game detail, Settings).</summary>
    static void GameKeyViewModelTests(List<Game> shelf)
    {
        var slots = new Dictionary<string, string> { ["1"] = "hades", ["3"] = "bg3" };
        var choices = GameKeyChoices.For(shelf[0], slots, shelf);
        Check(choices.Count == 10 && choices[0].Number is null && !choices[0].IsCurrent && choices[1].IsCurrent &&
              choices[1].Label == "1 · This game" && choices[3].Label == "3 · Baldur's Gate 3" && choices[2].Label == "2 · Free",
            "game keys: the menu lists None and 1–9 with who has each");

        var detailSpy = new DetailActionsSpy { Games = shelf };
        var detail = new DetailViewModel(detailSpy);
        var settings = new Settings { GameKeySlots = slots };
        detail.Show(shelf[0], [], settings, []);
        Check(detail.SelectedGameKey == "1 · This game" && detail.GameKeyText == "Win+Shift+G, 1" && detail.HasGameKey,
            "game keys: Game detail shows the game's number and how it's pressed");
        detail.SelectedGameKey = detail.GameKeyLabels[3];
        detail.SelectedGameKey = detail.GameKeyLabels[1]; // already its number: nothing to do
        Check(detailSpy.Calls.SequenceEqual(["key hades 3"]), "game keys: Game detail gives the picked number");
        detail.SelectedGameKey = detail.GameKeyLabels[0];
        Check(detailSpy.Calls[^1] == "key hades ", "game keys: Game detail's None takes it away");

        var settingsSpy = new SettingsActionsSpy { LastPlayed = shelf[1] };
        var page = new SettingsViewModel(settingsSpy);
        var games = new List<Game>(shelf) { new() { Id = "hidden", Name = "Aaa", Hidden = true } };
        page.Load(settings, games, false, false, 0, [], null, true);
        Check(page.GameKeyRows.Length == 10 && page.GameKeyRows[0] is { Number: 0, GameName: "Baldur's Gate 3" } &&
              page.GameKeyRows[1] is { GameName: "Hades II", ChoiceIndex: 2 } && page.GameKeyRows[2].ChoiceIndex == 0,
            "game keys: Settings lists 0 (last played) and 1–9 with their games");
        Check(page.GameKeyGameNames.SequenceEqual(["None", "Baldur's Gate 3", "Hades II"]) && page.GameKeysText == "2 games",
            "game keys: Settings picks from the shelf's games by name (no hidden ones) and counts them");
        page.PickGameKey(2, 1);
        page.PickGameKey(1, 2); // unchanged
        page.PickGameKey(1, 0);
        page.PickGameKey(0, 1); // 0 can't be given
        Check(settingsSpy.Calls.SequenceEqual(["key bg3 2", "key hades "]), "game keys: Settings gives and takes numbers");
        page.GameKeysOn = false;
        Check(!settings.GameKeysOn && settingsSpy.Changes[^1] == SettingChange.GameKeys && page.GameKeysText == "Off",
            "game keys: the switch turns them off");
    }
}
