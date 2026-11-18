using Binder.Library;
using Binder.Models;

namespace Binder.ViewModels;

/// <summary>One choice for a game's number: <see cref="Number"/> null is "None".</summary>
public sealed record GameKeyChoice(int? Number, string Label, bool IsCurrent);

/// <summary>
/// The choices of a game's number (spec §26) as the cover's menu and Game detail list them: None, then 1–9, each
/// with the game that has it ("3 · Forza Horizon 5"), "Free", or "This game". Pure, so it's unit-tested.
/// </summary>
public static class GameKeyChoices
{
    public static List<GameKeyChoice> For(Game game, IReadOnlyDictionary<string, string>? slots, IReadOnlyList<Game> games)
    {
        var names = new Dictionary<string, string>(games.Count);
        foreach (var g in games) names.TryAdd(g.Id, g.Name);
        int? current = GameKeys.SlotOf(slots, game.Id);
        var list = new List<GameKeyChoice> { new(null, Strings.None, current is null) };
        foreach (int number in GameKeys.UserSlots)
        {
            var holder = GameKeys.IdOn(slots, number);
            string who = holder == game.Id ? Strings.GameKeyThisGame
                : holder is not null && names.TryGetValue(holder, out var name) ? name
                : Strings.GameKeyFree;
            list.Add(new GameKeyChoice(number, Strings.Format(Strings.GameKeyChoice, number, who), current == number));
        }
        return list;
    }
}
