using GameShelf.Library;
using GameShelf.Models;
using GameShelf.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI.ViewManagement;

namespace GameShelf;

/// <summary>Playing a game: conflicts, the launch toast with its countdown, and coming back after the game.</summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Play, after asking to close conflicting programs when the game's anti-cheat reminder is on and one is
    /// running (checked only now, on Play).
    /// </summary>
    private void Play(Game game)
    {
        if (game.AntiCheatReminder && game.AntiCheats is { Count: > 0 } && PcChecks.RunningConflicts() is { Count: > 0 } conflicts)
            _ = ConfirmConflictsAsync(game, conflicts);
        else
            Launch(game);
    }

    private async Task ConfirmConflictsAsync(Game game, List<PcChecks.Conflict> conflicts)
    {
        try
        {
            var names = Strings.List([.. conflicts.Select(c => c.Name)]);
            var antiCheat = Strings.List([.. game.AntiCheats!.Select(AntiCheat.Name)]);
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
                Title = Strings.Format(Strings.ConflictTitle, names),
                Content = Strings.Format(conflicts.Count == 1 ? Strings.ConflictTextOne : Strings.ConflictText, game.Name, antiCheat, names),
                PrimaryButtonText = conflicts.Count == 1 ? Strings.ConflictCloseOne : Strings.ConflictClose,
                SecondaryButtonText = Strings.PlayAnyway,
                CloseButtonText = Strings.Cancel,
                DefaultButton = ContentDialogButton.Primary,
            };
            App.ModalDepth++; // click-away must not close the flyout under the dialog
            ContentDialogResult result;
            try
            {
                result = await dialog.ShowAsync();
            }
            finally
            {
                App.ModalDepth--;
            }
            if (result == ContentDialogResult.None) return;
            if (result == ContentDialogResult.Primary)
                foreach (var conflict in conflicts) PcChecks.AskToClose(conflict);
            Launch(game);
        }
        catch (Exception e)
        {
            ErrorLog.Write(e, "anti-cheat reminder");
            Launch(game);
        }
    }

    /// <summary>Starts the game with its launch options and keeps the panel open (no watcher, no session).</summary>
    private void TestLaunch(Game game)
    {
        App.Current.CancelCountdown();
        HideCountdown();
        if (Launcher.StartGame(game) is { } launchError)
            ShowToast(Strings.Format(Strings.CouldntStart, game.Name), launchError, Strings.Ok);
        else
            ShowToast(Strings.Format(Strings.TestLaunchTitle, game.Name), Strings.Format(Strings.TestLaunchDetail, Launcher.Describe(game)), Strings.Ok);
    }

    private void Launch(Game game)
    {
        App.Current.CancelCountdown();
        HideCountdown();

        if (Launcher.StartGame(game) is { } launchError)
        {
            ShowToast(Strings.Format(Strings.CouldntStart, game.Name), launchError, Strings.Ok);
            return;
        }
        if (!Launcher.CanWatch(game))
        {
            // Nothing would tell us when it closes (e.g. a web link shortcut), so hiding would be for good.
            ShowToast(Strings.Format(Strings.StartedGame, game.Name), Strings.StayingOpenNoWatch, Strings.Ok);
            return;
        }
        if (Launcher.StartWatcher(game, App.Settings.ComeBackOnClose) is { } watcherError)
        {
            // Without a watcher nothing would bring the widget back, so stay open.
            ShowToast(Strings.Format(Strings.StartedGame, game.Name), Strings.Format(Strings.StayingOpenNoWatcher, watcherError), Strings.Ok);
            return;
        }

        int delay = game.HideDelaySeconds ?? App.Settings.HideDelaySeconds;
        if (delay <= 0)
        {
            App.Current.Quit(); // exit completely: during the game only the watcher remains
            return;
        }

        // The countdown runs in App, so it keeps going if the flyout closes (the game takes the foreground).
        App.Current.StartCountdown(delay, Strings.Format(Strings.StartingGame, game.Name));
        ShowCountdown();
    }

    /// <summary>The launch countdown card, also when the flyout reopens while it's still counting.</summary>
    private void ShowCountdown()
    {
        int remaining = App.Current.CountdownRemaining, total = Math.Max(1, App.Current.CountdownTotal);
        CountdownTitle.Text = App.Current.CountdownTitle;
        UpdateCountdown(remaining);
        Countdown.Visibility = Visibility.Visible;

        // The bar drains in one smooth, linear move to the end (compositor-run). With animations off it steps
        // once a second instead (UpdateCountdown).
        _countdownBar?.Stop();
        _countdownBar = null;
        CountdownScale.ScaleX = (double)remaining / total;
        if (!new UISettings().AnimationsEnabled) return;
        var drain = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = (double)remaining / total,
            To = 0,
            Duration = TimeSpan.FromSeconds(remaining),
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(drain, CountdownScale);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(drain, "ScaleX");
        _countdownBar = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        _countdownBar.Children.Add(drain);
        _countdownBar.Begin();
    }

    private Microsoft.UI.Xaml.Media.Animation.Storyboard? _countdownBar;

    private void OnCountdownTick(int remaining)
    {
        if (Countdown.Visibility == Visibility.Visible) UpdateCountdown(remaining);
    }

    private void UpdateCountdown(int remaining)
    {
        CountdownText.Text = Strings.Format(Strings.HidingIn, remaining);
        if (_countdownBar is null) CountdownScale.ScaleX = (double)remaining / Math.Max(1, App.Current.CountdownTotal);
    }

    private void HideCountdown()
    {
        _countdownBar?.Stop();
        _countdownBar = null;
        Countdown.Visibility = Visibility.Collapsed;
    }

    private void OnKeepOpenClick(object sender, RoutedEventArgs e)
    {
        App.Current.CancelCountdown();
        HideCountdown();
        Shelf.FocusSearch();
    }

    /// <summary>A game ended while we were still open (e.g. after "Keep open"): show the new session.</summary>
    public void Returned()
    {
        if (App.Current.CountdownRunning)
        {
            // The game ended before the countdown did; don't hide now.
            App.Current.CancelCountdown();
            HideCountdown();
        }
        RefreshViews();
        // On screen (Keep open, or the desktop panel): welcome back now. Hidden: when it next opens (Reopen).
        if (!IsClosing) PickWelcome();
    }
}
