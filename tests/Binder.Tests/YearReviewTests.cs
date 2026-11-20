using Binder.Library;
using Binder.Models;
using Binder.ViewModels;

namespace Binder.Tests;

static partial class Tests
{
    /// <summary>Year in review (spec §28): the facts, the story, the themes' rules, the page's view model and the shelf's offer.</summary>
    static void YearReviewTests()
    {
        YearFactsTests();
        YearStoryTests();
        YearThemeTests();
        YearReviewViewModelTests();
        YearOfferTests();
        YearReviewSpeedTest();
    }

    static Session YS(string id, DateTime start, double hours, bool resumed = false) =>
        new() { GameId = id, Start = new DateTimeOffset(start), End = new DateTimeOffset(start.AddHours(hours)), Resumed = resumed };

    static GameSource? YSource(string id) => id switch
    {
        "a" or "b" => GameSource.Steam,
        "c" => GameSource.Epic,
        "d" => GameSource.Manual,
        _ => null,
    };

    static void YearFactsTests()
    {
        var now = new DateTime(2027, 3, 1);
        var sessions = new List<Session>
        {
            YS("a", new DateTime(2025, 5, 3, 16, 0, 0), 8),       // before the year: 8 h of a (9 h with New Year's part)
            YS("x", new DateTime(2025, 6, 1, 20, 0, 0), 2),       // before the year, then back after a long break
            YS("a", new DateTime(2025, 12, 31, 23, 0, 0), 2),     // over New Year: 1 h in 2025, 1 h in 2026
            YS("a", new DateTime(2026, 1, 2, 19, 0, 0), 1),       // a passes 10 h here
            YS("a", new DateTime(2026, 1, 3, 19, 0, 0), 3),
            YS("b", new DateTime(2026, 1, 4, 14, 0, 0), 2),       // Sunday afternoon
            YS("b", new DateTime(2026, 1, 4, 18, 0, 0), 1, resumed: true), // after the PC slept: the same session
            YS("c", new DateTime(2026, 7, 10, 21, 0, 0), 0.5),
            YS("c", new DateTime(2026, 7, 11, 21, 0, 0), 0.02),   // under 2 min: doesn't count
            YS("h", new DateTime(2026, 7, 12, 21, 0, 0), 5),      // a hidden game: doesn't count
            YS("x", new DateTime(2026, 9, 1, 20, 0, 0), 1),       // back after 15 months, Binder no longer knows it
        };
        bool Counts(string id) => id != "h";
        var f = YearFacts.Build(sessions, 2026, Counts, YSource, now);
        Check(f.Total == TimeSpan.FromHours(9.5) && f.Sessions == 6, "year facts: total and sessions, New Year clipped, short and hidden left out, resumed parts one session");
        Check(f.Games == 4 && f.Top[0].GameId == "a" && f.Top[0].Time == TimeSpan.FromHours(5) && f.Top[1].GameId == "b" && f.Top[1].Time == TimeSpan.FromHours(3),
            "year facts: games by time");
        Check(f.Months[0] == TimeSpan.FromHours(8) && f.Months[6] == TimeSpan.FromHours(0.5) && f.Months[8] == TimeSpan.FromHours(1) && f.BusiestMonth == 0,
            "year facts: time per month (the sleep isn't playtime)");
        Check(f.Days == 6, "year facts: days with play");
        Check(f.Longest is { GameId: "a", Length.TotalHours: 3 }, "year facts: the longest session");
        Check(f.Streak is { Days: 4 } streak && streak.First == new DateTime(2026, 1, 1) && streak.Last == new DateTime(2026, 1, 4),
            "year facts: the most days in a row (from New Year's part)");
        Check(f.Milestones is [{ GameId: "a", Hours: 10 }], "year facts: a milestone passed in the year");
        Check(f.NewIds.SequenceEqual(["b", "c"]), "year facts: new games are those first played in the year, in order");
        Check(f.Back is { GameId: "x", Months: 15 } back && back.LastPlayed == new DateTime(2025, 6, 1) && back.Back == new DateTime(2026, 9, 1),
            "year facts: the longest comeback");
        Check(f.Launchers.Select(l => l.Source).SequenceEqual([GameSource.Steam, GameSource.Manual, GameSource.Epic]) && f.Launchers.Sum(l => l.Percent) == 100,
            "year facts: launchers, unknown and manual games together as other, percents add up to 100");
        Check(f.LastYear == TimeSpan.FromHours(11) && f.Since is null && !f.SoFar, "year facts: last year's total; not a first or current year");
        Check(f.Hours[YearFacts.Cell(DayOfWeek.Friday, 19)] == 4 && f.Hours[YearFacts.Cell(DayOfWeek.Monday, 3)] == 0,
            "year facts: the hour grid in levels, Monday first");

        var first = YearFacts.Build(sessions, 2025, Counts, YSource, now);
        Check(first.Since == new DateTime(2025, 5, 3) && first.NewIds.Length == 0 && first.LastYear is null,
            "year facts: the first year starts at the first session, nothing is 'new' in it");
        var current = YearFacts.Build(sessions, 2027, Counts, YSource, now);
        Check(current.SoFar && current.Total == TimeSpan.Zero && current.Top.Length == 0, "year facts: a year without play is empty");
        Check(YearFacts.Percents([1, 1, 1]).Sum() == 100 && YearFacts.Percents([]).Length == 0, "year facts: whole percents always add up");

        // Which years can be shown: enough play, the current one from December, never a later one.
        var many = Enumerable.Range(0, 12).Select(i => YS("a", new DateTime(2025, 2, 1 + i, 20, 0, 0), 0.5)).ToList();
        Check(YearFacts.Years(many, _ => true, now).SequenceEqual([2025]), "year list: 12 sessions and 6 hours count");
        Check(YearFacts.Years(many.Take(9).ToList(), _ => true, now).Length == 0, "year list: under 10 sessions doesn't");
        var thisYear = Enumerable.Range(0, 12).Select(i => YS("a", new DateTime(2027, 1, 1 + i, 20, 0, 0), 1)).ToList();
        Check(YearFacts.Years(thisYear, _ => true, now).Length == 0 && YearFacts.Years(thisYear, _ => true, new DateTime(2027, 12, 1)).SequenceEqual([2027]),
            "year list: the current year only from December 1");
        Check(YearFacts.Years(thisYear, _ => true, new DateTime(2026, 12, 20)).Length == 0, "year list: a year after now (a wrong clock) never");
    }

    /// <summary>A full year with many games for the story and theme checks.</summary>
    static (List<Session> Sessions, Dictionary<string, Game> Games) YearSample(int year, int games, bool before)
    {
        var sessions = new List<Session>();
        var map = new Dictionary<string, Game>();
        var sources = new[] { GameSource.Steam, GameSource.Epic, GameSource.Gog, GameSource.Xbox, GameSource.Manual, GameSource.Ea, GameSource.Ubisoft };
        for (int g = 0; g < games; g++)
            map["g" + g] = new Game { Id = "g" + g, Name = "Game " + g, Source = sources[g % sources.Length] };
        if (before) sessions.Add(YS("g0", new DateTime(year - 1, 3, 1, 20, 0, 0), 3));
        var day = new DateTime(year, 1, 1);
        for (int i = 0; day.Year == year; day = day.AddDays(1), i++)
            if (i % 3 != 2) sessions.Add(YS("g" + (i * 7 % games) % Math.Max(1, games - i % 2), day.AddHours(19 + i % 4), 0.5 + i % 5 * 0.4));
        if (before) sessions.Add(YS("g0", new DateTime(year, 12, 30, 20, 0, 0), 2));
        return (sessions, map);
    }

    static void YearStoryTests()
    {
        var (sessions, games) = YearSample(2025, 12, before: true);
        var now = new DateTime(2026, 2, 1);
        var facts = YearFacts.Build(sessions, 2025, _ => true, id => games[id].Source, now);
        string Name(string id) => games.TryGetValue(id, out var g) ? g.Name : Strings.YirRemovedGame;
        var cards = YearStory.Plan(facts, YearThemes.Calm, Name);
        Check(cards.Select(c => c.Kind).SequenceEqual([CardKind.Intro, CardKind.Time, CardKind.Top, CardKind.Ranks, CardKind.When, CardKind.Records, CardKind.Launchers, CardKind.Summary])
              || cards.Select(c => c.Kind).SequenceEqual([CardKind.Intro, CardKind.Time, CardKind.Top, CardKind.Ranks, CardKind.When, CardKind.Records, CardKind.Fresh, CardKind.Launchers, CardKind.Summary]),
            "year story: the cards in order");
        Check(cards.All(c => c.Narration.Length > 10 && c.Title.Length > 0 && c.Blocks.Length > 0), "year story: every card has a title, blocks and a sentence for Narrator");
        Check(cards.First(c => c.Kind == CardKind.Top).Blocks.Any(b => b.Kind == BlockKind.Play && b.GameId == facts.Top[0].GameId), "year story: Most played has Play for that game");
        Check(cards.First(c => c.Kind == CardKind.Ranks).Title == "Your top 5", "year story: top 5 with five games or more");
        Check(cards.First(c => c.Kind == CardKind.Time).Blocks.First(b => b.Kind == BlockKind.Number).Value == (int)facts.Total.TotalHours,
            "year story: the time card counts the year's hours");

        // Cards with nothing to say are left out.
        var tiny = new List<Session> { YS("a", new DateTime(2025, 3, 1, 20, 0, 0), 3), YS("b", new DateTime(2025, 3, 3, 20, 0, 0), 3) };
        var tinyFacts = YearFacts.Build(tiny, 2025, _ => true, _ => GameSource.Steam, now);
        var tinyCards = YearStory.Plan(tinyFacts, YearThemes.Calm, id => id);
        Check(!tinyCards.Any(c => c.Kind is CardKind.Ranks or CardKind.Fresh or CardKind.Launchers) && tinyCards[^1].Kind == CardKind.Summary,
            "year story: no top list with two games, nothing new in the first year, no launcher card with one launcher");
        var tinyRecords = tinyCards.FirstOrDefault(c => c.Kind == CardKind.Records);
        Check(tinyRecords is not null && !tinyRecords.Blocks.Any(b => b.Kind == BlockKind.Dots), "year story: no streak without two days in a row");
        var three = YearFacts.Build([.. tiny, YS("c", new DateTime(2025, 4, 1, 20, 0, 0), 1)], 2025, _ => true, _ => GameSource.Steam, now);
        Check(YearStory.Plan(three, YearThemes.Calm, id => id).First(c => c.Kind == CardKind.Ranks).Title == "Your top 3", "year story: top 3 with three or four games");
        var removed = YearStory.Plan(tinyFacts, YearThemes.Calm, id => Strings.YirRemovedGame);
        Check(removed.First(c => c.Kind == CardKind.Top).Narration.Contains(Strings.YirRemovedGame), "year story: a game Binder no longer knows has a name");
    }

    static void YearThemeTests()
    {
        // Every theme, past ones included, builds every card from a full year, a first year and a tiny one, and
        // each block's motion ends within its budget, at the most items.
        var now = new DateTime(2026, 12, 20);
        var full = YearSample(2025, 40, before: true);
        var firstYear = YearSample(2025, 3, before: false);
        var samples = new[]
        {
            YearFacts.Build(full.Sessions, 2025, _ => true, id => full.Games[id].Source, now),
            YearFacts.Build(firstYear.Sessions, 2025, _ => true, id => firstYear.Games[id].Source, now),
            YearFacts.Build([YS("a", new DateTime(2026, 2, 2, 20, 0, 0), 6)], 2026, _ => true, _ => null, now),
        };
        var late = new List<string>();
        foreach (var theme in YearThemes.All.Concat([YearThemes.For(2024), YearThemes.For(2025), YearThemes.For(2026)]))
            foreach (var facts in samples)
                foreach (var card in YearStory.Plan(facts, theme, id => id))
                    foreach (var block in card.Blocks)
                        if (YearMotion.End(block, theme) > YearMotion.Budget(block.Kind))
                            late.Add($"{theme.Name} {card.Kind} {block.Kind} {YearMotion.End(block, theme)} ms");
        Check(late.Count == 0, $"year themes: every block's motion ends within its budget ({string.Join(", ", late.Distinct())})");
        Check(YearMotion.CardOutMs + YearMotion.CardInMs <= 500, "year themes: changing cards takes under half a second");
        Check(YearThemes.For(2026) == YearThemes.CoverTint && YearThemes.For(2025) == YearThemes.Calm && YearThemes.For(2099) == YearThemes.Calm,
            "year themes: each year keeps its theme, years without one get Calm");

        // Text on every card's background reads at 4.5:1 or more, for dark and light accents and cover colors.
        uint[] colors = [0x0078D4, 0x4CC2FF, 0xFFB900, 0xE81123, 0x00CC6A, 0x8764B8, 0x000000, 0xFFFFFF, 0x7A7574, 0xFFF100, 0x00B7C3];
        var poor = new List<string>();
        foreach (var ground in Enum.GetValues<Ground>())
            foreach (var accent in colors)
                foreach (var tint in colors)
                    foreach (bool dark in new[] { true, false })
                        for (int step = 0; step < 9; step++)
                        {
                            uint bg = YearColors.Background(ground, accent, tint, dark, step);
                            double text = YearColors.Contrast(YearColors.Text(dark), bg), secondary = YearColors.Contrast(YearColors.SecondaryText(bg, dark), bg);
                            if (text < 4.5 || secondary < 4.5) poor.Add($"{ground} {accent:X6}/{tint:X6} {(dark ? "dark" : "light")} {text:0.0}/{secondary:0.0}");
                        }
        Check(poor.Count == 0, $"year themes: text contrast 4.5:1 on every background ({string.Join(", ", poor.Take(5))})");
    }

    sealed class YearReviewSpy(YearData data) : IYearReviewActions
    {
        public readonly List<string> Calls = [];
        public YearData ReadData() => data;
        public YearTheme ThemeFor(int year) => YearThemes.For(year);
        public void Play(Game game) => Calls.Add("play " + game.Id);
        public void GoBack() => Calls.Add("back");
    }

    static void YearReviewViewModelTests()
    {
        var a = YearSample(2025, 8, before: true);
        var sessions = a.Sessions.Concat(YearSample(2026, 8, before: false).Sessions).ToList();
        var games = new Dictionary<string, Game>(a.Games);
        games["g0"] = new Game { Id = "g0", Name = "Kept settings", Source = GameSource.Steam }; // removed, settings kept
        var spy = new YearReviewSpy(new YearData(sessions, games, new HashSet<string> { "g0" }));
        var vm = new YearReviewViewModel(spy);
        var notified = new List<string?>();
        vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        vm.Show(null, new DateTime(2026, 12, 20));
        Check(vm.Year == 2026 && vm.YearChoices.SequenceEqual(["2026 so far", "2025"]) && (string?)vm.SelectedYearChoice == "2026 so far" &&
              vm.Index == 0 && vm.Card?.Kind == CardKind.Intro && notified.Contains(""), "year review: opens on the newest year, its first card");
        Check(vm.Theme == YearThemes.CoverTint && !vm.BackCommand.CanExecute(null) && vm.NextLabel == "Next" && vm.PositionText.StartsWith("1 of "),
            "year review: the year's theme, no Back on the first card");
        notified.Clear();
        vm.NextCommand.Execute(null);
        Check(vm.Index == 1 && vm.Forward && vm.BackCommand.CanExecute(null) && notified.Contains(nameof(YearReviewViewModel.Card)) && !notified.Contains(""),
            "year review: Next moves forward and reports only the card");
        vm.BackCommand.Execute(null);
        Check(vm.Index == 0 && !vm.Forward, "year review: Back moves back");
        while (!vm.IsLast) vm.Next();
        Check(vm.NextLabel == "Done" && vm.Card?.Kind == CardKind.Summary && vm.OtherYears.SequenceEqual([2025]), "year review: the last card says Done and lists the other years");
        vm.NextCommand.Execute(null);
        Check(spy.Calls is ["back"], "year review: Done goes back to the shelf");

        vm.SelectedYearChoice = "2025";
        Check(vm.Year == 2025 && vm.Index == 0 && vm.Theme == YearThemes.Calm && vm.Facts?.Year == 2025, "year review: the year box loads that year at its first card");
        vm.SelectedYearChoice = "1999";
        Check(vm.Year == 2025, "year review: an unknown year changes nothing");
        Check(vm.NameOf("g0") == "Kept settings" && !vm.CanPlay("g0") && vm.CanPlay("g1") && vm.NameOf("nobody") == Strings.YirRemovedGame,
            "year review: removed games keep their name but can't be played; unknown ones are 'A removed game'");
        vm.PickYear(2026);
        while (vm.Card?.Kind != CardKind.Top) vm.Next();
        vm.PlayCommand.Execute(null);
        Check(spy.Calls.Count == 1 || spy.Calls[^1].StartsWith("play "), "year review: Play on Most played plays that game (unless removed)");
        vm.RestartCommand.Execute(null);
        Check(vm.Index == 0, "year review: Watch again starts from the first card");
        vm.Release();
        Check(vm.Card is null && vm.Facts is null && vm.Cards.Count == 0, "year review: closing the page drops the year");

        var empty = new YearReviewViewModel(new YearReviewSpy(YearData.Empty));
        empty.Show(null, new DateTime(2026, 6, 1));
        Check(!empty.HasYears && empty.Card is null && empty.PositionText == "", "year review: no year with enough play shows nothing");
    }

    static void YearOfferTests()
    {
        var (sessions, games) = YearSample(2026, 6, before: false);
        var data = new YearData(sessions, games, new HashSet<string>());
        Check(YearReviewViewModel.Offer(data, new DateTime(2026, 11, 30), 0) is null, "year offer: not before December");
        var offer = YearReviewViewModel.Offer(data, new DateTime(2026, 12, 31), 0);
        Check(offer is { Year: 2026, GameIds.Length: 3 } && offer.Title == "Your 2026 in games" && offer.Detail.Contains("games"),
            "year offer: in December, the year with its top three covers");
        Check(YearReviewViewModel.Offer(data, new DateTime(2026, 12, 31), 2026) is null, "year offer: once a year");

        var spy = new ShelfActionsSpy { YearReview = true };
        var shelf = new ShelfViewModel(spy);
        Check(shelf.HasYearReview, "year offer: the … menu asks whether a year can be shown");
        var shelfGames = games.Values.ToList();
        shelf.SetData(shelfGames, sessions, new Settings());
        shelf.SetYearOffer(offer);
        Check(shelf.YearOfferVisible && shelf.SpotlightVisible && !shelf.ContinueVisible && !shelf.ShowWeek && !shelf.SwitchVisible && shelf.EnterGame is null,
            "year offer: it takes the top slot; Enter plays nothing");
        Check(shelf.FindGame(offer!.GameIds[0]) is not null && shelf.YearOfferTitle == offer.Title, "year offer: its covers and title");
        shelf.OpenYearOfferCommand.Execute(null);
        Check(spy.Calls[^1] == "year review 2026" && !shelf.YearOfferVisible && shelf.ContinueVisible, "year offer: Open opens that year, the slot goes back");
        shelf.SetYearOffer(offer);
        shelf.DismissYearOfferCommand.Execute(null);
        Check(!shelf.YearOfferVisible && spy.Calls.Count == 1, "year offer: Dismiss only hides it");
        shelf.SetYearOffer(offer);
        shelf.SetCrashShown(true);
        Check(!shelf.YearOfferVisible && shelf.CrashVisible, "year offer: the crash card comes first");
    }

    /// <summary>Opening the page is one pass: 20 years of daily sessions take milliseconds, not seconds.</summary>
    static void YearReviewSpeedTest()
    {
        var sessions = new List<Session>();
        var day = new DateTime(2006, 1, 1);
        for (int i = 0; day.Year < 2026; day = day.AddDays(1), i++)
            for (int s = 0; s < 3; s++) sessions.Add(YS("g" + (i * 3 + s) % 300, day.AddHours(10 + s * 4), 1.5));
        var games = Enumerable.Range(0, 300).ToDictionary(i => "g" + i, i => new Game { Id = "g" + i, Name = "Game " + i, Source = GameSource.Steam });
        var data = new YearData(sessions, games, new HashSet<string>());
        var vm = new YearReviewViewModel(new YearReviewSpy(data));
        vm.Show(null, new DateTime(2026, 6, 1)); // warm up
        var clock = System.Diagnostics.Stopwatch.StartNew();
        vm.Show(null, new DateTime(2026, 6, 1));
        clock.Stop();
        Console.WriteLine($"     year review: {sessions.Count:N0} sessions, years list + facts + cards in {clock.Elapsed.TotalMilliseconds:0} ms");
        Check(vm.Year == 2025 && clock.Elapsed < TimeSpan.FromMilliseconds(500), "year review: opening with 20 years of sessions is quick");
    }
}
