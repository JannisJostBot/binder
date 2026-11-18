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

    internal void DevCloseToast()
    {
        CloseToast();
        Shelf.HideCrash();
        Shelf.ViewModel?.SetWelcome(null);
    }
}
