using GameShelf.Models;
using GameShelf.Services;
using Microsoft.UI.Xaml;

namespace GameShelf;

/// <summary>The in-panel toast, errors, "couldn't detect the game" and the crash card.</summary>
public sealed partial class MainWindow
{
    private void OnToastButton(object sender, RoutedEventArgs e)
    {
        // "OK" dismisses a notice; "Undo" also runs its action.
        var action = _toastAction;
        CloseToast();
        action?.Invoke();
    }

    private void CloseToast()
    {
        _undoTimer?.Stop();
        _undoTimer = null;
        _toastAction = null;
        Toast.Visibility = Visibility.Collapsed;
    }

    private void ShowToast(string title, string detail, string button, Action? action = null)
    {
        _undoTimer?.Stop();
        _undoTimer = null;
        _toastAction = action;
        if (action is not null)
        {
            _undoTimer = _dispatcher.CreateTimer();
            _undoTimer.Interval = UndoTimeout;
            _undoTimer.IsRepeating = false;
            _undoTimer.Tick += (_, _) => CloseToast();
            _undoTimer.Start();
        }
        ToastBar.Title = title;
        ToastBar.Message = detail;
        ToastButton.Content = button;
        Toast.Visibility = Visibility.Visible;
    }

    /// <summary>A message with OK, from outside the panel (e.g. a restart after a crash that couldn't start).</summary>
    public void ShowNotice(string title, string detail) => ShowToast(title, detail, Strings.Ok);

    /// <summary>After an unexpected error was caught and logged.</summary>
    public void ShowError() =>
        ShowToast(Strings.SomethingWentWrong, Strings.Format(Strings.ErrorDetail, ErrorLog.FilePath), Strings.Ok);

    public void ShowNotDetected(string gameId)
    {
        var name = _games.FirstOrDefault(g => g.Id == gameId)?.Name ?? Strings.TheGame;
        ShowToast(Strings.NotDetectedTitle, Strings.Format(Strings.NotDetectedDetail, name), Strings.Ok);
    }

    /// <summary>The crash check finished: refresh the session icons, and show the card of an unseen problem.</summary>
    public void CrashesChecked(CrashRecord? unseen)
    {
        RefreshViews();
        if (unseen is not null && !(Shelf.ShownCrash is { } shown && shown.GameId == unseen.GameId && shown.Start == unseen.Start))
            ShowCrash(unseen);
    }

    private void ShowCrash(CrashRecord record)
    {
        if (FindGame(record.GameId) is not { } game) return;
        Shelf.ShowCrash(game, record, _sessions, App.State.Crashes);
    }

    /// <summary>Dismissed or played again: the card goes and won't come back.</summary>
    private void CrashSeen()
    {
        if (Shelf.ShownCrash is { } record) CrashStore.MarkSeen(App.State.Crashes, record);
        Shelf.HideCrash();
    }
}
