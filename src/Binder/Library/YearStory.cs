using System.Globalization;
using Binder.Models;

namespace Binder.Library;

// Year in review's engine (spec §28, board 43). YearFacts has the numbers; this file decides the story: which
// cards, which blocks on each, their text and when each comes in. A year's look is a YearTheme, data only, that
// chooses from what the engine has. Nothing here references WinUI, so the unit tests cover all of it.

public enum CardKind { Intro, Time, Top, Ranks, When, Records, Fresh, Launchers, Summary }

/// <summary>The parts every card is built from (Views/YearReviewCards draws each one).</summary>
public enum BlockKind
{
    /// <summary>The small capitals line at the top.</summary>
    Caption,
    /// <summary>A large line (a game's name, "Mostly on weekday evenings").</summary>
    Headline,
    /// <summary>A number that counts up, with its unit beside it.</summary>
    Number,
    /// <summary>A line of body text.</summary>
    Line,
    /// <summary>A small secondary line.</summary>
    Note,
    /// <summary>Month bars; the highlighted item in the accent.</summary>
    Bars,
    /// <summary>Ranked games with a cover and a bar.</summary>
    Ranks,
    /// <summary>The 7 × 24 hour grid (Value 0–4 per cell).</summary>
    Heat,
    /// <summary>The year's covers, starting stacked like the logo.</summary>
    Mosaic,
    /// <summary>A row of covers dealt in.</summary>
    Covers,
    /// <summary>Small stat tiles (Label is the value, Detail its name).</summary>
    Tiles,
    /// <summary>A share bar with a row per launcher.</summary>
    Share,
    /// <summary>A break between two dates (the comeback).</summary>
    Timeline,
    /// <summary>The game's banner art and logo.</summary>
    Hero,
    /// <summary>One dot per day of the streak.</summary>
    Dots,
    /// <summary>The Play button for <see cref="BlockSpec.GameId"/>.</summary>
    Play,
}

/// <summary>A block's variant: month bars as rows (the top three), a record tile, a list tile, a compact ranked list.</summary>
public enum BlockStyle { Default, Rows, Record, List, Compact }

/// <summary>Where a block sits: in reading order from the top, at the bottom, or behind the rest (full card).</summary>
public enum BlockPlace { Flow, Bottom, Behind }

/// <summary>How a block comes in. Each is one Composition recipe in the view.</summary>
public enum Cue { None, Rise, Fade, Up, Fan, Grow, Wipe, Pop, Drop }

/// <summary>One row of a list block: a game, a month, a launcher, a tile.</summary>
public sealed record BlockItem(string Label, double Value, string? Detail = null, string? GameId = null, bool Highlight = false,
    GameSource? Source = null);

/// <summary>
/// One block on a card. <see cref="At"/> is when it starts coming in (ms after the card), <see cref="Value"/> the
/// number of a Number block (and its <see cref="Text"/> the unit).
/// </summary>
public sealed record BlockSpec(BlockKind Kind, int At, string? Text = null, double Value = 0, string? Detail = null,
    BlockItem[]? Items = null, string? GameId = null, BlockPlace Place = BlockPlace.Flow, BlockStyle Style = BlockStyle.Default)
{
    public BlockItem[] List => Items ?? [];
}

/// <summary>
/// One card: its blocks in reading order, the sentence Narrator reads when it comes in, and the game whose cover
/// color tints it (a CoverTint theme).
/// </summary>
public sealed record CardSpec(CardKind Kind, string Title, string Narration, BlockSpec[] Blocks, string? TintGameId, bool Centered = false);

// ---- Themes ---------------------------------------------------------------------------------------------------

/// <summary>The card's background: the Windows accent, the top game's cover color, or a neutral surface; always darkened (or lightened) by the engine.</summary>
public enum Ground { Accent, CoverTint, Neutral }

public enum ShapeKind { None, Circles, Stripes, DotGrid }

/// <summary>Gentle: soft ease and short staggers. Lively: a snappier ease and longer staggers.</summary>
public enum MotionKind { Gentle, Lively }

/// <summary>The intro's covers: fanned out of a stack, or dealt in one by one.</summary>
public enum IntroKind { Fan, Deal }

/// <summary>Time played: the number on top with bars below, centered with the top three months, or bars behind it.</summary>
public enum TimeLayout { NumberTop, NumberCenter, BarsBehind }

/// <summary>A year's look: data only, chosen from what the engine has. Never text, images or anything read at run time.</summary>
public sealed record YearTheme(string Name, Ground Ground, ShapeKind Shapes, MotionKind Motion, IntroKind Intro,
    int DisplayWeight, double NumberScale, TimeLayout TimeLayout);

/// <summary>Which theme each year has. A year keeps its theme for good, so a past year always looks as it did.</summary>
public static class YearThemes
{
    public static readonly YearTheme Calm = new("Calm", Ground.Accent, ShapeKind.Circles, MotionKind.Gentle, IntroKind.Fan,
        DisplayWeight: 600, NumberScale: 1, TimeLayout.NumberTop);

    public static readonly YearTheme CoverTint = new("Cover tint", Ground.CoverTint, ShapeKind.Stripes, MotionKind.Lively, IntroKind.Deal,
        DisplayWeight: 700, NumberScale: 1.1, TimeLayout.NumberCenter);

    /// <summary>Not given to a year yet: an example of the same blocks arranged differently (board 43), in the debug panel.</summary>
    public static readonly YearTheme Grid = new("Grid", Ground.Neutral, ShapeKind.DotGrid, MotionKind.Gentle, IntroKind.Fan,
        DisplayWeight: 300, NumberScale: 1, TimeLayout.BarsBehind);

    public static readonly YearTheme[] All = [Calm, CoverTint, Grid];

    public static YearTheme For(int year) => year switch
    {
        2026 => CoverTint,
        _ => Calm, // every year without its own, earlier ones included
    };
}

/// <summary>Colors from the theme's ground, worked out in plain numbers (0xRRGGBB) so the tests can check contrast.</summary>
public static class YearColors
{
    public const uint DarkText = 0xF3F3F5, LightText = 0x1A1A1F;
    private const uint DarkBase = 0x16161A, LightBase = 0xFFFFFF, Neutral = 0x2A2A2F;

    /// <summary>
    /// A card's background: the ground's color mixed far toward the dark (or light) base, a little lighter or darker
    /// per card (<paramref name="step"/> 0–2) so neighbouring cards differ.
    /// </summary>
    public static uint Background(Ground ground, uint accent, uint? tint, bool dark, int step)
    {
        uint source = ground switch
        {
            Ground.CoverTint => tint ?? accent,
            Ground.Neutral => Neutral,
            _ => accent,
        };
        double keep = (dark ? 0.24 : 0.16) + 0.04 * (step % 3);
        if (ground == Ground.Neutral) keep = dark ? 0.9 + 0.03 * (step % 3) : 0.06 + 0.02 * (step % 3);
        return Mix(source, dark ? DarkBase : LightBase, keep);
    }

    public static uint Text(bool dark) => dark ? DarkText : LightText;

    /// <summary>A secondary text color: the text toward the background, still 4.5:1 or more.</summary>
    public static uint SecondaryText(uint background, bool dark)
    {
        var text = Text(dark);
        for (double keep = 0.72; keep < 1; keep += 0.04)
        {
            var mixed = Mix(text, background, keep);
            if (Contrast(mixed, background) >= 4.5) return mixed;
        }
        return text;
    }

    /// <summary>Shapes and empty bars: the background a little toward the text.</summary>
    public static uint Shape(uint background, bool dark) => Mix(Text(dark), background, dark ? 0.08 : 0.07);

    /// <summary>Ordinary bars: the background further toward the text.</summary>
    public static uint Muted(uint background, bool dark) => Mix(Text(dark), background, dark ? 0.22 : 0.2);

    /// <summary>Mixes <paramref name="a"/> into <paramref name="b"/>: <paramref name="keep"/> of a.</summary>
    public static uint Mix(uint a, uint b, double keep)
    {
        uint Channel(int shift) => (uint)Math.Round(((a >> shift) & 0xFF) * keep + ((b >> shift) & 0xFF) * (1 - keep));
        return Channel(16) << 16 | Channel(8) << 8 | Channel(0);
    }

    /// <summary>WCAG contrast ratio of two colors.</summary>
    public static double Contrast(uint a, uint b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(uint rgb)
    {
        double C(int shift)
        {
            double c = ((rgb >> shift) & 0xFF) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * C(16) + 0.7152 * C(8) + 0.0722 * C(0);
    }
}

/// <summary>How long each cue runs and how far items are staggered, per theme; the view and the motion budget test read these.</summary>
public static class YearMotion
{
    /// <summary>Every block's motion ends by this (the brief's exception allows about a second)…</summary>
    public const int BudgetMs = 1000;
    /// <summary>…except the moment of a card: a number counting up, the intro's covers and the comeback's line.</summary>
    public const int FeatureBudgetMs = 1300;
    public const int CardInMs = 260, CardOutMs = 150, CountMs = 900;
    /// <summary>Most items a list block brings in one by one; the rest come with the last.</summary>
    public const int MaxStaggered = 12;

    public static int Duration(Cue cue) => cue switch
    {
        Cue.None => 0,
        Cue.Fade or Cue.Pop => 300,
        Cue.Fan => 650,
        Cue.Grow => 600,
        Cue.Wipe => 700,
        _ => 450, // Rise, Up, Drop
    };

    private static int Stagger(MotionKind motion, BlockKind kind) => kind switch
    {
        BlockKind.Heat => 0, // the grid sweeps as one wave (each cell's delay comes from its place, HeatDelay)
        BlockKind.Mosaic => 35,
        BlockKind.Bars => 45,
        BlockKind.Dots => 35,
        _ => motion == MotionKind.Lively ? 70 : 50,
    };

    /// <summary>How long a list may take from its first item to its last: long lists stagger less.</summary>
    private static int MaxSpread(BlockKind kind) => kind == BlockKind.Bars ? 200 : 350;

    /// <summary>The delay between two items of a block with <paramref name="items"/> items.</summary>
    public static int StaggerFor(BlockKind kind, MotionKind motion, int items)
    {
        int n = Math.Min(items, MaxStaggered);
        return n <= 1 ? 0 : Math.Min(Stagger(motion, kind), MaxSpread(kind) / (n - 1));
    }

    /// <summary>When item <paramref name="index"/> of a block starts (ms after the card).</summary>
    public static int ItemAt(BlockSpec block, YearTheme theme, int index) =>
        block.At + Math.Min(index, MaxStaggered - 1) * StaggerFor(block.Kind, theme.Motion, block.List.Length);

    /// <summary>The cue each kind of block comes in with, in a theme.</summary>
    public static Cue CueFor(BlockKind kind, YearTheme theme) => kind switch
    {
        BlockKind.Mosaic => theme.Intro == IntroKind.Fan ? Cue.Fan : Cue.Drop,
        BlockKind.Bars => Cue.Grow,
        BlockKind.Heat => Cue.Pop,
        BlockKind.Covers => Cue.Drop,
        BlockKind.Share => Cue.Wipe,
        BlockKind.Dots => Cue.Pop,
        BlockKind.Hero => Cue.Fade,
        BlockKind.Timeline => Cue.Rise,
        BlockKind.Note => Cue.Fade,
        _ => theme.Motion == MotionKind.Lively ? Cue.Up : Cue.Rise,
    };

    /// <summary>The cue of the rows that come with a block's own motion (a rank's row, a launcher's row).</summary>
    public static Cue RowCue(YearTheme theme) => theme.Motion == MotionKind.Lively ? Cue.Up : Cue.Rise;

    /// <summary>When a block's last part has come in (ms after the card), for the budget.</summary>
    public static int End(BlockSpec block, YearTheme theme)
    {
        int last = ItemAt(block, theme, Math.Max(0, block.List.Length - 1));
        return block.Kind switch
        {
            BlockKind.Heat => block.At + HeatDelay(6, 23) + Duration(Cue.Pop),
            BlockKind.Number => block.At + Math.Max(Duration(CueFor(block.Kind, theme)), CountMs),
            // A rank's bar grows with its row (not in the compact list).
            BlockKind.Ranks => last + (block.Style == BlockStyle.Compact ? Duration(RowCue(theme)) : Math.Max(Duration(RowCue(theme)), Duration(Cue.Grow))),
            // The bar is wiped in while the rows come in under it.
            BlockKind.Share => Math.Max(block.At + Duration(Cue.Wipe), last + Duration(RowCue(theme))),
            // The tile rises, its break draws, then the return dot pops.
            BlockKind.Timeline => block.At + TimelineLineAt + Duration(Cue.Grow) + Duration(Cue.Pop),
            _ => last + Duration(CueFor(block.Kind, theme)),
        };
    }

    /// <summary>The comeback's dashed line starts drawing this long after its tile.</summary>
    public const int TimelineLineAt = 0;

    /// <summary>The longest a block may take.</summary>
    public static int Budget(BlockKind kind) => kind is BlockKind.Number or BlockKind.Mosaic or BlockKind.Timeline ? FeatureBudgetMs : BudgetMs;

    /// <summary>The hour grid's wave: Monday midnight first, Sunday's last hour last.</summary>
    public static int HeatDelay(int day, int hour) => hour * 14 + day * 22;
}

/// <summary>Turns a year's facts into its cards (the skip rules live here, once).</summary>
public static class YearStory
{
    /// <summary>Most covers the intro and "New this year" show.</summary>
    public const int MaxCovers = 12, MaxNewCovers = 10;

    /// <summary>
    /// The cards for a year. <paramref name="nameOf"/> gives a game's name ("A removed game" when Binder no longer
    /// knows it).
    /// </summary>
    public static CardSpec[] Plan(YearFacts f, YearTheme theme, Func<string, string> nameOf)
    {
        var culture = CultureInfo.CurrentCulture;
        string topName = f.Top.Length > 0 ? nameOf(f.Top[0].GameId) : "";
        string? topId = f.Top.Length > 0 ? f.Top[0].GameId : null;
        var cards = new List<CardSpec>();
        string hours = HoursText(f.Total);
        string games = Strings.Plural(f.Games, Strings.GameCountOne, Strings.GameCount);
        string introLine = Strings.Format(Strings.Dot, games, hours);
        if (f.SoFar) introLine = Strings.Format(Strings.YirSoFarLine, introLine);

        // 1 · Intro
        string foot = f.Since is { } since ? Strings.Format(Strings.YirSince, Day(since)) : Strings.YirCounted;
        cards.Add(new CardSpec(CardKind.Intro, Strings.YirIntroTitle,
            Strings.Format(Strings.YirSayIntro, f.Year.ToString(culture), introLine),
            [
                new(BlockKind.Mosaic, 100, Items: [.. f.Top.Take(MaxCovers).Select(t => new BlockItem(nameOf(t.GameId), 0, GameId: t.GameId))]),
                new(BlockKind.Caption, 300, Strings.YirIntroTitle),
                new(BlockKind.Headline, 340, f.Year.ToString(culture), Style: BlockStyle.List,
                    Items: [.. f.Year.ToString(culture).Select(c => new BlockItem(c.ToString(), 0))]),
                new(BlockKind.Line, 520, introLine),
                new(BlockKind.Note, 560, foot, Place: BlockPlace.Bottom),
            ], topId, Centered: true));

        // 2 · Time played
        int busiest = f.BusiestMonth;
        var monthNames = culture.DateTimeFormat;
        var bars = f.Months.Select((m, i) => new BlockItem(monthNames.GetAbbreviatedMonthName(i + 1), m.TotalHours,
            Strings.Format(Strings.LabelValue, monthNames.GetMonthName(i + 1), HoursText(m)), Highlight: i == busiest)).ToArray();
        string timeLine = Strings.Format(Strings.YirTimeLine, Strings.Plural(f.Days, Strings.YirDayCountOne, Strings.YirDayCount),
            Strings.Plural(f.Sessions, Strings.YirSessionCountOne, Strings.YirSessionCount),
            Views.Format.Duration(f.Sessions > 0 ? f.Total / f.Sessions : TimeSpan.Zero));
        string busiestLine = Strings.Format(Strings.YirBusiest, monthNames.GetMonthName(busiest + 1), HoursText(f.Months[busiest]));
        var time = new List<BlockSpec>
        {
            new(BlockKind.Caption, 100, Strings.YirTimeTitle),
            new(BlockKind.Number, 140, Unit((int)f.Total.TotalHours), Value: (int)f.Total.TotalHours, Detail: hours),
            new(BlockKind.Line, 380, timeLine),
        };
        if (f.LastYear is { } last) time.Add(new(BlockKind.Note, 440, Strings.Format(Strings.LabelValue, (f.Year - 1).ToString(culture), HoursText(last))));
        switch (theme.TimeLayout)
        {
            case TimeLayout.NumberCenter:
                time.Add(new(BlockKind.Bars, 200, busiestLine, Place: BlockPlace.Bottom,
                    Items: [.. bars.Select((b, i) => (b, i)).OrderByDescending(x => x.b.Value).Take(3).Select((x, rank) => x.b with { Highlight = rank == 0 })],
                    Style: BlockStyle.Rows));
                break;
            case TimeLayout.BarsBehind:
                time.Add(new(BlockKind.Bars, 200, busiestLine, Items: bars, Place: BlockPlace.Behind));
                break;
            default:
                time.Add(new(BlockKind.Bars, 200, busiestLine, Items: bars, Place: BlockPlace.Bottom));
                break;
        }
        cards.Add(new CardSpec(CardKind.Time, Strings.YirTimeTitle,
            Strings.Format(Strings.YirSayTime, hours, timeLine, busiestLine), [.. time], topId,
            Centered: theme.TimeLayout == TimeLayout.NumberCenter));

        // 3 · Most played
        if (f.Top.Length > 0)
        {
            var top = f.Top[0];
            int share = (int)Math.Round(top.Time.TotalMinutes * 100 / Math.Max(1, f.Total.TotalMinutes));
            string shareLine = Strings.Format(Strings.Dot, Strings.Format(Strings.YirShare, share),
                Strings.Plural(top.Sessions, Strings.YirSessionCountOne, Strings.YirSessionCount));
            var blocks = new List<BlockSpec>
            {
                new(BlockKind.Hero, 0, topName, GameId: top.GameId),
                new(BlockKind.Caption, 60, Strings.YirTopTitle),
                new(BlockKind.Headline, 100, topName),
                new(BlockKind.Number, 360, Unit((int)top.Time.TotalHours), Value: (int)top.Time.TotalHours, Detail: HoursText(top.Time)),
                new(BlockKind.Line, 300, shareLine),
            };
            if (f.TopFirst is { } first && f.TopLast is { } lastDay)
                blocks.Add(new(BlockKind.Note, 360, Strings.Format(Strings.YirTopDates, Day(first), Day(lastDay))));
            blocks.Add(new(BlockKind.Play, 400, GameId: top.GameId, Place: BlockPlace.Bottom));
            cards.Add(new CardSpec(CardKind.Top, Strings.YirTopTitle,
                Strings.Format(Strings.YirSayTop, topName, HoursText(top.Time), shareLine), [.. blocks], top.GameId));
        }

        // 4 · Top 5 (top 3 with 3–4 games; left out with fewer)
        if (f.Top.Length >= 3)
        {
            int n = f.Top.Length >= 5 ? 5 : 3;
            double max = Math.Max(1, f.Top[0].Time.TotalMinutes);
            var ranks = f.Top.Take(n).Select((t, i) => new BlockItem(nameOf(t.GameId), t.Time.TotalMinutes / max,
                HoursText(t.Time), t.GameId, Highlight: i == 0)).ToArray();
            var blocks = new List<BlockSpec>
            {
                new(BlockKind.Caption, 100, Strings.Format(Strings.YirTopN, n)),
                new(BlockKind.Ranks, 120, Items: ranks),
            };
            int others = f.Top.Length - n;
            if (others > 0) blocks.Add(new(BlockKind.Note, 700, Strings.Plural(others, Strings.YirOthersOne, Strings.YirOthers), Place: BlockPlace.Bottom));
            cards.Add(new CardSpec(CardKind.Ranks, Strings.Format(Strings.YirTopN, n),
                Strings.Format(Strings.LabelValue, Strings.Format(Strings.YirTopN, n),
                    Strings.List([.. ranks.Select(r => Strings.Format(Strings.Dot, r.Label, r.Detail))])), [.. blocks], topId));
        }

        // 5 · When
        string when = f.When switch
        {
            WhenKind.WeekdayEvenings => Strings.YirWhenWeekdayEvenings,
            WhenKind.Weekends => Strings.YirWhenWeekends,
            WhenKind.LateNights => Strings.YirWhenLate,
            WhenKind.Afternoons => Strings.YirWhenAfternoons,
            WhenKind.Mornings => Strings.YirWhenMornings,
            _ => Strings.YirWhenEvenings,
        };
        var tiles = new BlockItem[]
        {
            new(Strings.Format(Strings.YirPercent, f.EveningPercent), f.EveningPercent, Strings.YirEveningShare),
            new(Strings.Format(Strings.YirPercent, f.WeekendPercent), f.WeekendPercent, Strings.YirWeekendShare),
            new(Strings.Format(Strings.YirPercent, f.LatePercent), f.LatePercent, Strings.YirLateShare),
        };
        cards.Add(new CardSpec(CardKind.When, Strings.YirWhenTitle,
            Strings.Format(Strings.Sentences, when, Strings.List([.. tiles.Select(t => Strings.Format(Strings.Dot, t.Label, t.Detail))])),
            [
                new(BlockKind.Caption, 100, Strings.YirWhenTitle),
                new(BlockKind.Headline, 160, when),
                new(BlockKind.Heat, 240, Items: [.. f.Hours.Select(v => new BlockItem("", v))]),
                new(BlockKind.Tiles, 400, Items: tiles, Place: BlockPlace.Bottom),
            ], topId));

        // 6 · Records
        var records = new List<BlockSpec> { new(BlockKind.Caption, 100, Strings.YirRecordsTitle) };
        var said = new List<string>();
        if (f.Longest is { } longest)
        {
            string detail = Strings.Format(Strings.Dot, nameOf(longest.GameId), Day(longest.Start));
            records.Add(new(BlockKind.Tiles, 180, Strings.YirLongest, Style: BlockStyle.Record,
                Items: [new(Views.Format.Duration(longest.Length), 0, detail, longest.GameId)]));
            said.Add(Strings.Format(Strings.LabelValue, Strings.YirLongest, Strings.Format(Strings.Dot, Views.Format.Duration(longest.Length), detail)));
        }
        if (f.Streak is { } streak)
        {
            string range = Strings.Format(Strings.YirRange, Day(streak.First), Day(streak.Last));
            records.Add(new(BlockKind.Caption, 300, Strings.YirStreak));
            records.Add(new(BlockKind.Number, 340, Strings.Plural(streak.Days, Strings.YirDayUnitOne, Strings.YirDayUnit), Value: streak.Days,
                Detail: Strings.Plural(streak.Days, Strings.YirDayCountOne, Strings.YirDayCount)));
            records.Add(new(BlockKind.Dots, 300, range, Value: streak.Days, Items: [.. Enumerable.Range(0, Math.Min(streak.Days, 31)).Select(_ => new BlockItem("", 1))]));
            said.Add(Strings.Format(Strings.LabelValue, Strings.YirStreak, Strings.Format(Strings.Dot, Strings.Plural(streak.Days, Strings.YirDayCountOne, Strings.YirDayCount), range)));
        }
        if (f.Milestones.Length > 0)
        {
            var items = f.Milestones.Select(m => new BlockItem(Strings.Format(Strings.WelcomeMilestone, m.Hours, nameOf(m.GameId)), m.Hours, GameId: m.GameId)).ToArray();
            records.Add(new(BlockKind.Tiles, 400, Strings.YirMilestones, Style: BlockStyle.List, Items: items, Place: BlockPlace.Bottom));
            said.Add(Strings.Format(Strings.LabelValue, Strings.YirMilestones, Strings.List([.. items.Select(i => i.Label)])));
        }
        if (said.Count > 0)
            cards.Add(new CardSpec(CardKind.Records, Strings.YirRecordsTitle, string.Join(" ", said.Select(Sentence)), [.. records], topId));

        // 7 · New this year (not in the first year with Binder) and the longest comeback
        if (f.NewIds.Length > 0 || f.Back is not null)
        {
            var fresh = new List<BlockSpec> { new(BlockKind.Caption, 100, Strings.YirNewTitle) };
            var freshSaid = new List<string>();
            if (f.NewIds.Length > 0)
            {
                string count = Strings.Plural(f.NewIds.Length, Strings.YirNewCountOne, Strings.YirNewCount);
                fresh.Add(new(BlockKind.Headline, 160, count));
                fresh.Add(new(BlockKind.Covers, 200, Items: [.. f.NewIds.Take(MaxNewCovers).Select(id => new BlockItem(nameOf(id), 0, GameId: id))]));
                freshSaid.Add(Strings.Format(Strings.LabelValue, count, Strings.List([.. f.NewIds.Take(MaxNewCovers).Select(nameOf)])));
            }
            if (f.Back is { } back)
            {
                string title = Strings.Format(Strings.YirBackAfter, Strings.Plural(back.Months, Strings.YirMonthCountOne, Strings.YirMonthCount));
                fresh.Add(new(BlockKind.Timeline, 400, title, GameId: back.GameId, Place: BlockPlace.Bottom,
                    Items: [new(Strings.Format(Strings.YirLastPlayed, Day(back.LastPlayed, withYear: true)), 0), new(Strings.Format(Strings.YirBackOn, Day(back.Back)), 0)]));
                freshSaid.Add(Strings.Format(Strings.LabelValue, title, nameOf(back.GameId)));
            }
            cards.Add(new CardSpec(CardKind.Fresh, Strings.YirNewTitle, string.Join(" ", freshSaid.Select(Sentence)), [.. fresh], topId));
        }

        // 8 · By launcher (left out with one)
        if (f.Launchers.Length > 1)
        {
            var items = f.Launchers.Select(l => new BlockItem(l.Source == GameSource.Manual ? Strings.SourceOther : Views.Format.LauncherName(l.Source),
                l.Percent, HoursText(l.Time), Source: l.Source)).ToArray();
            string line = Strings.Format(Strings.YirLaunchersLine, f.Launchers.Length);
            cards.Add(new CardSpec(CardKind.Launchers, Strings.YirLaunchersTitle,
                Strings.Format(Strings.LabelValue, line, Strings.List([.. items.Select(i => Strings.Format(Strings.Dot, i.Label, Strings.Format(Strings.YirPercent, i.Value)))])),
                [
                    new(BlockKind.Caption, 100, Strings.YirLaunchersTitle),
                    new(BlockKind.Headline, 160, line),
                    new(BlockKind.Share, 200, Items: items),
                ], topId));
        }

        // 9 · Summary
        var summary = new List<BlockSpec>
        {
            new(BlockKind.Hero, 60, f.Year.ToString(culture), Detail: introLine, GameId: topId),
            new(BlockKind.Tiles, 200, Items:
            [
                new(Views.Format.Playtime(f.Total), 0, Strings.YirTimeTitle),
                new(f.Games.ToString("N0", culture), 0, Strings.Games),
                new(topName, 0, Strings.YirTopTitle),
                new(f.Streak is { } s ? Strings.Plural(s.Days, Strings.YirDayCountOne, Strings.YirDayCount) : Views.Format.Duration(f.Longest?.Length ?? TimeSpan.Zero), 0,
                    f.Streak is not null ? Strings.YirStreak : Strings.YirLongest),
            ]),
        };
        if (f.Top.Length >= 3)
            summary.Add(new(BlockKind.Ranks, 260, Strings.Format(Strings.YirTopN, Math.Min(5, f.Top.Length)), Style: BlockStyle.Compact,
                Items: [.. f.Top.Take(5).Select((t, i) => new BlockItem(nameOf(t.GameId), 0, HoursText(t.Time), t.GameId))]));
        cards.Add(new CardSpec(CardKind.Summary, Strings.YirSummaryTitle,
            Strings.Format(Strings.YirSayIntro, f.Year.ToString(culture), introLine), [.. summary], topId));

        return [.. cards];
    }

    /// <summary>"312 hours" (whole hours; "45 min" under one).</summary>
    public static string HoursText(TimeSpan time) => time.TotalHours < 1
        ? Views.Format.Playtime(time)
        : Strings.Plural((int)time.TotalHours, Strings.YirHourCountOne, Strings.YirHourCount);

    /// <summary>The unit beside a counted number of hours.</summary>
    public static string Unit(int hours) => Strings.Plural(hours, Strings.YirHourUnitOne, Strings.YirHourUnit);

    /// <summary>A day as the user's format writes it without the year ("March 7"), or with it ("May 2025").</summary>
    public static string Day(DateTime day, bool withYear = false)
    {
        var format = CultureInfo.CurrentCulture.DateTimeFormat;
        return day.ToString(withYear ? format.YearMonthPattern : format.MonthDayPattern, CultureInfo.CurrentCulture);
    }

    private static string Sentence(string text) => text.EndsWith('.') ? text : text + ".";
}
