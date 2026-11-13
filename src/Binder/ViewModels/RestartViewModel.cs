namespace Binder.ViewModels;

/// <summary>What the restart pill asks its owner (the app) to do.</summary>
public interface IRestartActions
{
    /// <summary>The countdown ran out: play the game again, the ordinary way.</summary>
    void RestartNow();

    /// <summary>Cancel, Esc or the panel opened: no restart; the crash card shows instead.</summary>
    void RestartCancelled();
}

/// <summary>
/// The restart pill after a crash (spec §17): "[Game] crashed · Restarting in 5 s", a bar that drains, and Cancel.
/// The view ticks it once a second; it ends exactly once, by running out or being cancelled.
/// </summary>
public sealed partial class RestartViewModel : ObservableObject
{
    /// <summary>Long enough to react, short enough not to keep the user waiting.</summary>
    public const int Seconds = 5;

    private readonly IRestartActions _actions;

    public RestartViewModel(IRestartActions actions, string gameName, int seconds = Seconds)
    {
        _actions = actions;
        Total = Math.Max(1, seconds);
        Remaining = Total;
        Title = Strings.Format(Strings.RestartPillTitle, gameName);
        CancelCommand = new RelayCommand(Cancel, () => !Ended);
    }

    public string Title { get; }
    public int Total { get; }
    public int Remaining { get; private set; }

    /// <summary>"Restarting in 5 s".</summary>
    public string Detail => Strings.Format(Strings.RestartPillDetail, Remaining);

    /// <summary>The whole line for screen readers.</summary>
    public string Name => Strings.Format(Strings.Sentences, Title, Detail);

    /// <summary>The bar: 1 when it starts, 0 when it restarts.</summary>
    public double Progress => (double)Remaining / Total;

    /// <summary>Restarted or cancelled: nothing more happens.</summary>
    public bool Ended { get; private set; }

    public RelayCommand CancelCommand { get; }

    /// <summary>One second has passed.</summary>
    public void Tick()
    {
        if (Ended) return;
        Remaining--;
        OnPropertyChanged(nameof(Remaining));
        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Progress));
        if (Remaining > 0) return;
        End();
        _actions.RestartNow();
    }

    public void Cancel()
    {
        if (Ended) return;
        End();
        _actions.RestartCancelled();
    }

    private void End()
    {
        Ended = true;
        OnPropertyChanged(nameof(Ended));
        CancelCommand.NotifyCanExecuteChanged();
    }
}
