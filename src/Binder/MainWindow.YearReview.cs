using Binder.Library;
using Binder.Models;
using Binder.ViewModels;

namespace Binder;

/// <summary>Year in review (spec §28): what its page asks the panel to do (<see cref="IYearReviewActions"/>) and the shelf's offer.</summary>
public sealed partial class MainWindow
{
    YearData IYearReviewActions.ReadData() => YearReviewData();
    void IYearReviewActions.Play(Game game) => Play(game);
    void IYearReviewActions.GoBack() => ShowShelf();

    YearTheme IYearReviewActions.ThemeFor(int year)
    {
        var theme = YearThemes.For(year);
        DevYearTheme(ref theme);
        return theme;
    }

    bool IShelfActions.HasYearReview() => YearReviewViewModel.Years(YearReviewData(), YearReviewNow()).Length > 0;
    void IShelfActions.OpenYearReview(int? year) => OpenYearReview(year);

    // Debug builds only (src/Binder/Dev): made-up years, another theme, a December date. No body in Release, where
    // the compiler removes the calls.
    partial void DevYearReviewData(ref YearData data);
    partial void DevYearTheme(ref YearTheme theme);
    partial void DevYearReviewNow(ref DateTime now);

    private DateTime YearReviewNow()
    {
        var now = DateTime.Now;
        DevYearReviewNow(ref now);
        return now;
    }

    /// <summary>The sessions and every game Binder knows by id: the library's, and removed ones with settings (no Play).</summary>
    private YearData YearReviewData()
    {
        var games = new Dictionary<string, Game>(_games.Count + App.State.RemovedGames.Count);
        var removed = new HashSet<string>();
        foreach (var game in App.State.RemovedGames)
            if (games.TryAdd(game.Id, game)) removed.Add(game.Id);
        foreach (var game in _games)
        {
            games[game.Id] = game; // back in the library: the library's entry
            removed.Remove(game.Id);
        }
        var data = new YearData(_sessions, games, removed);
        DevYearReviewData(ref data);
        return data;
    }

    /// <summary>Opens Year in review on <paramref name="year"/> (or the newest), from the "…" menu or the shelf's offer.</summary>
    public void OpenYearReview(int? year)
    {
        YearReviewPage.Slowdown = App.Current.MotionSlowdown;
        ShowPage(YearReviewPage);
        YearReviewPage.UpdateLayout();
        YearReviewPage.ViewModel!.Show(year, YearReviewNow());
        YearReviewPage.FocusFirst();
    }

    /// <summary>
    /// Each time the panel opens: in December, Year in review offers the year once in the shelf's top slot (after the
    /// crash card and Welcome back), stored as offered right away so the next opening shows the usual slot.
    /// </summary>
    private void PickYearOffer()
    {
        var settings = App.Settings;
        if (!settings.YearReviewOffer || Shelf.ViewModel!.ShowsWelcome || Shelf.ViewModel!.CrashShown) return;
        var now = YearReviewNow();
        if (now.Month < YearFacts.SoFarFromMonth || settings.YearReviewOffered >= now.Year) return; // the cheap checks first
        if (YearReviewViewModel.Offer(YearReviewData(), now, settings.YearReviewOffered) is not { } offer) return;
        settings.YearReviewOffered = offer.Year;
        SaveSettings();
        Shelf.ViewModel!.SetYearOffer(offer);
    }
}
