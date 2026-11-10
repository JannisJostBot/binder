using GameShelf.Library;
using GameShelf.Models;
using GameShelf.Views;

namespace GameShelf.ViewModels;

/// <summary>
/// What the Free up space page was doing, kept in <c>App.State</c> (memory only) while it's open: opening a launcher
/// to uninstall a game takes the focus and closes the flyout, and reopening it continues here.
/// </summary>
public sealed class FreeSpaceState
{
    public long Need { get; set; }
    public string? Drive { get; set; }
    /// <summary>The drive was chosen (by the user, or opened from a game's details); otherwise it follows "Best choice".</summary>
    public bool DrivePicked { get; set; }
    /// <summary>Tiles the user clicked: game id → removed (true) or kept (false), over the plan's own choice.</summary>
    public Dictionary<string, bool> Overrides { get; } = [];
    /// <summary>The games being uninstalled one at a time, and which one is next (-1: not started).</summary>
    public List<string> Queue { get; } = [];
    public int Step { get; set; } = -1;
    /// <summary>Where Back goes: the game whose details opened the page, the shelf (its "…" menu), else Settings.</summary>
    public string? ReturnGameId { get; set; }
    public bool ReturnToShelf { get; set; }

    public bool Uninstalling => Step >= 0 && Step < Queue.Count;
}

/// <summary>What the Free up space page asks its owner to do, and what it reads from Windows.</summary>
public interface IFreeSpaceActions
{
    void GoBack();
    /// <summary>Read free space and the library again (after the launchers removed games), then show the page again.</summary>
    void CheckAgain();
    /// <summary>Fixed drives that are ready, read now (no watchers).</summary>
    IReadOnlyList<DriveSpace> ReadDrives();
    /// <summary>The ids of the games running right now (never suggested for removal).</summary>
    IReadOnlySet<string> RunningGameIds(IReadOnlyList<Game> games);
    /// <summary>What to do in the launcher that removes the game.</summary>
    string UninstallHint(Game game);
    /// <summary>Hands the game's removal to its own launcher (or Settings › Apps).</summary>
    void OpenUninstall(Game game);
}

/// <summary>One drive to pick, as its choice shows it.</summary>
public sealed record DriveRow(string Root, string Label, double UsedPercent, string Text, bool Best, string Name);

/// <summary>
/// Free up space (spec §12, board 17): the new game's size in, a plan per drive out, the chosen drive's games whose
/// removal can be added or left out, then each removal handed to the game's own launcher. Built when opened, static
/// while shown (no timers); <see cref="Release"/> drops the data when the page closes. The treemap is the view's.
/// </summary>
public sealed partial class FreeSpaceViewModel : ObservableObject
{
    private readonly IFreeSpaceActions _actions;
    private Dictionary<string, List<SpaceGame>> _byDrive = [];
    private List<DriveSpace> _drives = [];
    private List<DrivePlan> _plans = [];
    private int _best = -1;

    public FreeSpaceViewModel(IFreeSpaceActions actions)
    {
        _actions = actions;
        BackCommand = new(actions.GoBack);
        CheckAgainCommand = new(actions.CheckAgain);
        UninstallCommand = new(Uninstall);
    }

    public RelayCommand BackCommand { get; }
    public RelayCommand CheckAgainCommand { get; }
    public RelayCommand UninstallCommand { get; }

    public FreeSpaceState State { get; private set; } = new();

    /// <summary>
    /// Shows the page for <paramref name="state"/>, reading drive space, the games on each drive and which games are
    /// running right now. Called on open, on "Check again" and when the panel reopens mid-uninstall.
    /// </summary>
    public void Show(FreeSpaceState state, IReadOnlyList<Game> games, IReadOnlyList<Session> sessions)
    {
        State = state;
        var (lastPlayed, playtime) = ShelfOrder.Stats(sessions);
        var installed = games.Where(g => SpacePlanner.DriveOf(g) is not null).ToList();
        _byDrive = SpacePlanner.ByDrive(installed, playtime, lastPlayed, DateTimeOffset.Now, _actions.RunningGameIds(installed));
        _drives = [.. _actions.ReadDrives()];

        if (State.Drive is null || !_drives.Any(d => d.Root == State.Drive))
        {
            State.DrivePicked = false;
            // Until there's a size to plan for: the drive with the most game data.
            State.Drive = _drives.OrderByDescending(d => GamesOn(d.Root).Sum(g => g.Size)).Select(d => d.Root).FirstOrDefault();
        }
        Recalculate();
        OnAllPropertiesChanged();
    }

    /// <summary>Drops the data (the page closed).</summary>
    public void Release()
    {
        _byDrive = [];
        _plans = [];
        _drives = [];
        Drives = [];
        Picked = [];
    }

    private List<SpaceGame> GamesOn(string root) => _byDrive.TryGetValue(root, out var list) ? list : [];

    /// <summary>Back goes to the game, the shelf or Settings, depending on where the page was opened.</summary>
    public string BackName => State.ReturnGameId is not null ? Strings.BackToGame : State.ReturnToShelf ? Strings.BackToShelf : Strings.BackToSettings;
    public string BackToolTip => Strings.Format(Strings.WithEsc, BackName);

    // ---- The size typed --------------------------------------------------------------------------------------

    /// <summary>The size in whole GB as the field shows it ("" before one is typed).</summary>
    public string NeedText => State.Need > 0 ? (State.Need / SpacePlanner.GB).ToString() : "";

    /// <summary>The user typed a size (digits only, filtered by the view): plan again from scratch.</summary>
    public void SetNeed(string text)
    {
        long gb = long.TryParse(text, out var value) ? value : 0;
        State.Need = gb * SpacePlanner.GB;
        ResetChoice();
        Recalculate(); // cheap: a few dozen games per drive
        OnPropertiesChanged(DriveNames);
        OnPropertiesChanged(MapNames);
        OnPropertiesChanged(SummaryNames);
    }

    /// <summary>A new size or drive starts from the plan again: clicked tiles and an unfinished uninstall no longer apply.</summary>
    private void ResetChoice()
    {
        State.Overrides.Clear();
        State.Queue.Clear();
        State.Step = -1;
    }

    private void Recalculate()
    {
        _plans = [.. _drives.Select(d => SpacePlanner.PlanDrive(d, GamesOn(d.Root), State.Need))];
        _best = State.Need > 0 ? SpacePlanner.Best(_plans) : -1;
        if (!State.DrivePicked && _best >= 0) State.Drive = _plans[_best].Drive.Root;
        FillDrives();
        FillSummary();
    }

    private void OnPropertiesChanged(string[] names)
    {
        foreach (var name in names) OnPropertyChanged(name);
    }

    // ---- Drives ----------------------------------------------------------------------------------------------

    private static readonly string[] DriveNames = [nameof(Drives), nameof(SelectedDrive)];

    public DriveRow[] Drives { get; private set; } = [];
    public string? SelectedDrive => State.Drive;

    private void FillDrives() =>
        Drives = [.. _plans.Select((plan, i) =>
        {
            var drive = plan.Drive;
            bool best = i == _best;
            double used = drive.Total > 0 ? (drive.Total - drive.Free) * 100.0 / drive.Total : 0;
            var status = Strings.Format(Strings.DriveFreeOf, Gb(drive.Free), Gb(drive.Total));
            var outcome = Outcome(plan);
            return new DriveRow(drive.Root, drive.Label, used, outcome.Length > 0 ? Strings.Format(Strings.Dot, status, outcome) : status, best,
                Strings.Format(Strings.DriveName, drive.Root.TrimEnd('\\'), drive.Label, status) + (outcome.Length > 0 ? ", " + outcome : "") + (best ? ", " + Strings.BestChoice : ""));
        })];

    private static string Outcome(DrivePlan plan)
    {
        if (plan.Missing <= -plan.Drive.Free) return ""; // no size typed yet
        if (plan.FitsAlready) return Strings.FitsAlreadyLower;
        if (!plan.Enough) return plan.Picked.Count == 0 ? Strings.NotEnoughSpace : Strings.NotEnoughEvenAfter;
        return Strings.Plural(plan.Picked.Count, Strings.ToRemoveOne, Strings.ToRemove);
    }

    /// <summary>The user picked a drive: its plan from scratch.</summary>
    public void PickDrive(string root)
    {
        if (root == State.Drive || !_drives.Any(d => d.Root == root)) return;
        State.Drive = root;
        State.DrivePicked = true;
        ResetChoice();
        FillSummary();
        OnPropertyChanged(nameof(SelectedDrive));
        OnPropertiesChanged(MapNames);
        OnPropertiesChanged(SummaryNames);
    }

    // ---- The chosen drive's games (the view draws them as a treemap) -----------------------------------------

    private static readonly string[] MapNames = [nameof(CurrentPlan), nameof(UnknownText), nameof(HasUnknown), nameof(MapEmptyText)];

    public DrivePlan? CurrentPlan => _plans.FirstOrDefault(p => p.Drive.Root == State.Drive);

    /// <summary>The plan's picks with the user's tile clicks applied, in the plan's order then the added ones.</summary>
    public List<SpaceGame> Chosen(DrivePlan plan)
    {
        var chosen = plan.Picked.Where(g => !State.Overrides.TryGetValue(g.Game.Id, out var on) || on).ToList();
        foreach (var g in plan.Games)
            if (State.Overrides.TryGetValue(g.Game.Id, out var on) && on && !chosen.Contains(g)) chosen.Add(g);
        return chosen;
    }

    /// <summary>A tile was clicked: the game is added to the removals, or left out.</summary>
    public void ToggleGame(string id, bool removedNow)
    {
        State.Overrides[id] = !removedNow;
        State.Queue.Clear();
        State.Step = -1;
        FillSummary();
        OnPropertyChanged(nameof(CurrentPlan));
        OnPropertiesChanged(SummaryNames);
    }

    public string MapEmptyText => CurrentPlan is { Games.Count: > 0 }
        ? Strings.MapNoSizes
        : Strings.MapNoGames;

    public string UnknownText
    {
        get
        {
            var unknown = CurrentPlan?.Games.Where(g => !g.SizeKnown).Select(g => g.Game.Name).ToList() ?? [];
            return unknown.Count == 0 ? "" :
                Strings.Format(Strings.UnknownSizes, unknown.Count > 6
                    ? Strings.Format(Strings.ListAnd, string.Join(Strings.ListSeparator, unknown.Take(6)), Strings.Format(Strings.MoreCount, unknown.Count - 6))
                    : Strings.List(unknown));
        }
    }

    public bool HasUnknown => UnknownText.Length > 0;

    // ---- Summary and uninstall -------------------------------------------------------------------------------

    private static readonly string[] SummaryNames =
    [
        nameof(ShowSummary), nameof(Headline), nameof(Status), nameof(StatusOk), nameof(Picked), nameof(Uninstalling),
        nameof(StepTitle), nameof(StepMessage), nameof(UninstallText), nameof(CanUninstall),
    ];

    public bool ShowSummary { get; private set; }
    public string Headline { get; private set; } = "";
    public string Status { get; private set; } = "";
    public bool StatusOk { get; private set; }
    /// <summary>The games to remove, as listed under the plan.</summary>
    public SpaceGame[] Picked { get; private set; } = [];
    public bool Uninstalling => ShowSummary && State.Uninstalling;
    public string StepTitle { get; private set; } = "";
    public string StepMessage { get; private set; } = "";
    public string UninstallText { get; private set; } = "";
    public bool CanUninstall { get; private set; }

    private void FillSummary()
    {
        if (CurrentPlan is not { } plan || State.Need <= 0)
        {
            ShowSummary = false;
            Picked = [];
            return;
        }
        ShowSummary = true;

        var chosen = plan.FitsAlready ? [] : Chosen(plan);
        long freed = chosen.Sum(g => g.Size);
        if (plan.FitsAlready)
        {
            Headline = Strings.FitsAlready;
            Status = Strings.Format(Strings.FreeAfterwards, Gb(plan.Drive.Free - State.Need));
            StatusOk = true;
        }
        else if (chosen.Count == 0 && plan.Picked.Count == 0)
        {
            Headline = Strings.Format(Strings.Missing, Gb(plan.Missing));
            Status = Strings.NothingToRemove;
            StatusOk = false;
        }
        else
        {
            Headline = Strings.Format(Strings.FreesOfMissing, Gb(freed), Gb(plan.Missing));
            StatusOk = freed >= plan.Missing;
            Status = StatusOk ? Strings.Format(Strings.FreeAfterwards, Gb(plan.Drive.Free + freed - State.Need)) : Strings.Format(Strings.StillMissing, Gb(plan.Missing - freed));
        }
        Picked = [.. chosen];

        if (State.Uninstalling)
        {
            int n = State.Queue.Count;
            var next = chosen.FirstOrDefault(g => g.Game.Id == State.Queue[State.Step]);
            StepTitle = Strings.Format(Strings.UninstallStep, State.Step + 1, n, next?.Game.Name);
            StepMessage = next is null ? "" : _actions.UninstallHint(next.Game);
            UninstallText = State.Step + 1 < n ? Strings.NextGame : Strings.Done;
            CanUninstall = true;
        }
        else
        {
            UninstallText = chosen.Count == 0 ? Strings.NothingToUninstall : Strings.Plural(chosen.Count, Strings.UninstallCountOne, Strings.UninstallCount);
            CanUninstall = chosen.Count > 0;
        }
    }

    /// <summary>Starts handing the chosen games to their launchers one at a time, or goes on to the next one.</summary>
    private void Uninstall()
    {
        if (CurrentPlan is not { } plan) return;
        var chosen = Chosen(plan);
        if (!State.Uninstalling)
        {
            if (chosen.Count == 0) return;
            State.Queue.Clear();
            State.Queue.AddRange(chosen.Select(g => g.Game.Id));
            State.Step = 0;
        }
        else if (++State.Step >= State.Queue.Count)
        {
            // All handed over: see what the launchers removed.
            State.Queue.Clear();
            State.Step = -1;
            _actions.CheckAgain();
            return;
        }
        FillSummary();
        OnPropertiesChanged(SummaryNames);
        if (chosen.FirstOrDefault(g => g.Game.Id == State.Queue[State.Step]) is { } next) _actions.OpenUninstall(next.Game);
    }

    // ---- Text ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Why a game was picked: "3 h played · Yesterday", "Never played", or "Last played 8 months ago" when only the
    /// store knows it was played (Steam's LastPlayed, with no playtime of ours).
    /// </summary>
    public static string Why(SpaceGame g)
    {
        if (g.Playtime > TimeSpan.Zero)
            return g.LastPlayed is { } when ? Strings.Format(Strings.Dot, Strings.Format(Strings.Played, Format.Playtime(g.Playtime)), Ago(when))
                : Strings.Format(Strings.Played, Format.Playtime(g.Playtime));
        return g.LastPlayed is { } last ? Strings.Format(Strings.LastPlayedAgo, Strings.LowerFirst(Ago(last))) : Strings.NeverPlayed;
    }

    /// <summary>The short form on a tile: "3 h", "never played", "a year ago".</summary>
    public static string Played(SpaceGame g) =>
        g.Playtime > TimeSpan.Zero ? Format.Playtime(g.Playtime) : g.LastPlayed is { } last ? Strings.LowerFirst(Ago(last)) : Strings.LowerFirst(Strings.NeverPlayed);

    /// <summary>What a tile reads out: name, size, why, favorite and recent.</summary>
    public static string TileName(SpaceGame g) =>
        Strings.Format(Strings.SpaceTileName, g.Game.Name, Gb(g.Size), Strings.LowerFirst(Why(g)))
        + (g.Favorite ? ", " + Strings.FavoriteLower : "") + (g.Recent ? ", " + Strings.PlayedRecently : "");

    private static string Ago(DateTimeOffset when)
    {
        int days = (DateTime.Today - when.LocalDateTime.Date).Days;
        if (days < 7) return Format.RelativeDay(when);
        if (days < 30) return days < 14 ? Strings.LastWeek : Strings.Format(Strings.WeeksAgo, days / 7);
        int months = days / 30;
        if (months < 12) return months == 1 ? Strings.AMonthAgo : Strings.Format(Strings.MonthsAgo, months);
        int years = days / 365;
        return years <= 1 ? Strings.AYearAgo : Strings.Format(Strings.YearsAgo, years);
    }

    /// <summary>Whole GB from 10 up, one decimal below: "128", "4.2", "0".</summary>
    public static string Gb(long bytes)
    {
        double gb = bytes / (double)SpacePlanner.GB;
        return gb >= 10 || gb == 0 ? $"{Math.Round(gb):0}" : $"{gb:0.#}";
    }
}
