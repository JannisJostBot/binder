using System.Globalization;
using Binder.Library;
using Binder.Models;

namespace Binder.ViewModels;

/// <summary>
/// What Year in review reads: the sessions and every game Binder knows by id, the library's and the removed ones
/// (<paramref name="Removed"/>: no longer installed, so no Play).
/// </summary>
public sealed record YearData(IReadOnlyList<Session> Sessions, IReadOnlyDictionary<string, Game> Games, IReadOnlySet<string> Removed)
{
    public static readonly YearData Empty = new([], new Dictionary<string, Game>(), new HashSet<string>());
}

/// <summary>The shelf's one-time offer in December (spec §28, board 42): the year, its line and the top three games.</summary>
public sealed record YearOffer(int Year, string Title, string Detail, string[] GameIds);

/// <summary>What the Year in review page asks its owner to do. An interface, so none can be left unwired.</summary>
public interface IYearReviewActions
{
    /// <summary>The data, read when the page opens (the debug panel can hand in made-up years instead).</summary>
    YearData ReadData();
    /// <summary>The year's theme (<see cref="YearThemes.For"/>; the debug panel can try another).</summary>
    YearTheme ThemeFor(int year);
    void Play(Game game);
    void GoBack();
}

/// <summary>
/// Year in review (spec §28, boards 41–43): a short story of a year, one card at a time, paced by the user. Works
/// out the year's facts and cards when the page opens or another year is picked (<see cref="YearFacts"/>,
/// <see cref="YearStory"/>), never on a timer. The view draws <see cref="Card"/> and plays its motion.
/// </summary>
public sealed partial class YearReviewViewModel : ObservableObject
{
    private readonly IYearReviewActions _actions;
    private YearData _data = YearData.Empty;
    private DateTime _now;
    private int[] _years = [];
    private CardSpec[] _cards = [];
    private int _index;

    public YearReviewViewModel(IYearReviewActions actions)
    {
        _actions = actions;
        NextCommand = new(Next);
        BackCommand = new(Back, () => _index > 0);
        LeaveCommand = new(actions.GoBack);
        RestartCommand = new(() => Go(0, forward: true));
        PlayCommand = new(PlayTop);
    }

    public RelayCommand NextCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand LeaveCommand { get; }
    public RelayCommand RestartCommand { get; }
    public RelayCommand PlayCommand { get; }

    /// <summary>The years that can be shown, newest first.</summary>
    public static int[] Years(YearData data, DateTime now) => YearFacts.Years(data.Sessions, id => Counts(data, id), now);

    /// <summary>
    /// The shelf's offer, or null: in December, for this year "so far", when it has enough play and wasn't offered
    /// yet (<paramref name="offered"/> is the last year offered).
    /// </summary>
    public static YearOffer? Offer(YearData data, DateTime now, int offered)
    {
        if (now.Month < YearFacts.SoFarFromMonth || offered >= now.Year) return null;
        var years = Years(data, now);
        if (years.Length == 0 || years[0] != now.Year) return null;
        var facts = YearFacts.Build(data.Sessions, now.Year, id => Counts(data, id), id => Source(data, id), now);
        return new YearOffer(now.Year,
            Strings.Format(Strings.YirOfferTitle, now.Year.ToString(CultureInfo.CurrentCulture)),
            Strings.Format(Strings.Dot, YearStory.HoursText(facts.Total), Strings.Plural(facts.Games, Strings.GameCountOne, Strings.GameCount)),
            [.. facts.Top.Take(3).Select(t => t.GameId)]);
    }

    /// <summary>Opens the page on <paramref name="year"/> (or the newest), at its first card.</summary>
    public void Show(int? year, DateTime now)
    {
        _now = now;
        _data = _actions.ReadData();
        _years = Years(_data, now);
        YearChoices = [.. _years.Select(Label)];
        int pick = year is { } y && _years.Contains(y) ? y : _years.FirstOrDefault();
        Load(pick);
    }

    /// <summary>Drops the data and cards (the page closed).</summary>
    public void Release()
    {
        _data = YearData.Empty;
        _cards = [];
        _index = 0;
        Facts = null;
        OnAllPropertiesChanged();
    }

    public string[] YearChoices { get; private set; } = [];

    /// <summary>The year box's item ("2026 so far", "2025"); bound by item, as its list changes with it.</summary>
    public object? SelectedYearChoice
    {
        get => Year == 0 ? null : Label(Year);
        set
        {
            if (value is not string label) return;
            int i = Array.IndexOf(YearChoices, label);
            if (i < 0 || _years[i] == Year) return;
            Load(_years[i]);
        }
    }

    public int Year { get; private set; }
    public YearFacts? Facts { get; private set; }
    public YearTheme Theme { get; private set; } = YearThemes.Calm;
    public IReadOnlyList<CardSpec> Cards => _cards;
    public int Index => _index;
    public CardSpec? Card => _index < _cards.Length ? _cards[_index] : null;
    /// <summary>The last move went forward (the new card comes in from the right) or back.</summary>
    public bool Forward { get; private set; } = true;
    public bool IsLast => _index == _cards.Length - 1;
    public bool HasYears => _years.Length > 0;
    public string NextLabel => IsLast ? Strings.Done : Strings.YirNext;
    public string PositionText => Card is { } card
        ? Strings.Format(Strings.YirPosition, _index + 1, _cards.Length, card.Title)
        : "";
    /// <summary>The other years, for the summary card's chips.</summary>
    public int[] OtherYears => [.. _years.Where(y => y != Year)];

    public static string Label(int year, DateTime now) => year == now.Year
        ? Strings.Format(Strings.YirSoFar, year.ToString(CultureInfo.CurrentCulture))
        : year.ToString(CultureInfo.CurrentCulture);

    public string Label(int year) => Label(year, _now);

    public void Next()
    {
        if (IsLast) _actions.GoBack(); // Done
        else Go(_index + 1, forward: true);
    }

    public void Back()
    {
        if (_index > 0) Go(_index - 1, forward: false);
    }

    /// <summary>A chip on the summary card, or the year box.</summary>
    public void PickYear(int year)
    {
        if (_years.Contains(year)) Load(year);
    }

    public Game? GameFor(string? id) => id is not null && _data.Games.TryGetValue(id, out var game) ? game : null;

    /// <summary>A game's name; "A removed game" when Binder no longer knows it.</summary>
    public string NameOf(string id) => GameFor(id)?.Name ?? Strings.YirRemovedGame;

    /// <summary>Whether the game is still in the library (removed games show "Not installed" instead of Play).</summary>
    public bool CanPlay(string? id) => GameFor(id) is { } game && !_data.Removed.Contains(game.Id) && !game.Hidden;

    private void PlayTop()
    {
        if (Card?.Blocks.FirstOrDefault(b => b.Kind == BlockKind.Play)?.GameId is { } id && CanPlay(id) && GameFor(id) is { } game)
            _actions.Play(game);
    }

    private void Load(int year)
    {
        Year = year;
        Theme = _actions.ThemeFor(year);
        if (year == 0)
        {
            Facts = null;
            _cards = [];
        }
        else
        {
            Facts = YearFacts.Build(_data.Sessions, year, id => Counts(_data, id), id => Source(_data, id), _now);
            _cards = YearStory.Plan(Facts, Theme, NameOf);
        }
        _index = 0;
        Forward = true;
        OnAllPropertiesChanged();
        BackCommand.NotifyCanExecuteChanged();
    }

    private void Go(int index, bool forward)
    {
        if (index < 0 || index >= _cards.Length) return;
        _index = index;
        Forward = forward;
        OnPropertyChanged(nameof(Forward));
        OnPropertyChanged(nameof(Index));
        OnPropertyChanged(nameof(IsLast));
        OnPropertyChanged(nameof(NextLabel));
        OnPropertyChanged(nameof(PositionText));
        OnPropertyChanged(nameof(Card));
        BackCommand.NotifyCanExecuteChanged();
    }

    // Hidden games don't count; a game Binder doesn't know (removed without settings) does.
    private static bool Counts(YearData data, string id) => !data.Games.TryGetValue(id, out var game) || !game.Hidden;

    private static GameSource? Source(YearData data, string id) => data.Games.TryGetValue(id, out var game) ? game.Source : null;
}
