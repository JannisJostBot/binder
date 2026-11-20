// DEBUG BUILDS ONLY (see App.Dev.cs): left out of Release builds.
using Binder.Library;
using Binder.Models;

namespace Binder;

/// <summary>What the debug panel shows inside the panel, through the panel's own code.</summary>
public sealed partial class MainWindow
{
    /// <summary>The crash card for a made-up record (marked seen, so dismissing it saves nothing).</summary>
    internal void DevCrash(CrashRecord record) => ShowCrash(record);

    /// <summary>"Welcome back" for a made-up session; not stored, unlike the real one.</summary>
    internal void DevWelcome(WelcomeMoment moment) => Shelf.ViewModel!.SetWelcome(moment);

    /// <summary>The toast after giving a game a number; nothing is saved. With <paramref name="other"/>, it took that game's.</summary>
    internal void DevGameKeyToast(string name, int number, string? other) =>
        ShowToast(Strings.Format(Strings.GameKeyGiven, name, number),
            other is not null ? Strings.Format(Strings.GameKeyTaken, other) : Views.Format.GameKeyLabel(App.Settings.Hotkey, number),
            Strings.Ok);

    // Year in review (spec §28): the debug panel's made-up years, theme and "it's December".
    partial void DevYearReviewData(ref ViewModels.YearData data)
    {
        if (App.Current.DevMockYears) data = Dev.YearReviewMock.Data(data, YearReviewNow(), App.Current.DevMockRemovedTop);
    }

    partial void DevYearTheme(ref YearTheme theme)
    {
        if (App.Current.DevYearTheme is { } chosen) theme = chosen;
    }

    partial void DevYearReviewNow(ref DateTime now)
    {
        if (App.Current.DevMockYears) now = new DateTime(now.Year, 12, 20, 21, 30, 0);
    }

    /// <summary>The shelf's December offer, whatever the settings say; nothing is saved. False when no year can be offered.</summary>
    internal bool DevYearOffer()
    {
        if (ViewModels.YearReviewViewModel.Offer(YearReviewData(), YearReviewNow(), offered: 0) is not { } offer) return false;
        ShowShelf();
        Shelf.ViewModel!.SetYearOffer(offer);
        return true;
    }

    internal void DevCloseToast()
    {
        CloseToast();
        Shelf.HideCrash();
        Shelf.ViewModel?.SetWelcome(null);
        Shelf.ViewModel?.SetYearOffer(null);
    }
}
