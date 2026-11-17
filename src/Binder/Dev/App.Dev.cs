// DEBUG BUILDS ONLY. Everything in src/Binder/Dev is left out of Release builds (Binder.csproj), so none of it ships;
// publish.ps1 stops if DevPanel.Marker turns up in the published files. Nothing here writes the user's data.
using Binder.Models;
using Binder.ViewModels;

namespace Binder;

/// <summary>The debug panel's actions: the real toasts and cards, shown with made-up data.</summary>
public sealed partial class App
{
    private Views.GameModeToast? _devRestartPill;

    partial void DevCommand(string[] args, ref bool handled)
    {
        if (!args.Contains("--dev")) return;
        handled = true;
        Dev.DevPanel.Show(this);
    }

    /// <summary>The first game in the library, for the cards that need a real one.</summary>
    internal Game? DevGame => State.Games.FirstOrDefault();

    /// <summary>"Game mode on", as the watcher reports it with "--applied".</summary>
    internal void DevApplied(Shared.RestoreResult result) => ShowGameModeToast(Views.GameModeToast.Kind.Applied, result);

    /// <summary>"Back to normal", as the watcher reports it with "--restored" (with its sound, if that's on).</summary>
    internal void DevRestored(Shared.RestoreResult result) => ShowRestored(result.Encode(), exitWhenClosed: false);

    /// <summary>The debug panel's "Quarter speed": the Game mode toasts melt four times slower while it's ticked.</summary>
    internal bool DevSlowToasts { get; set; }

    partial void DevToastSpeed(ref float slow)
    {
        if (DevSlowToasts) slow = 4;
    }

    /// <summary>The restart pill after a crash; its countdown and Cancel only close it, nothing is started.</summary>
    internal void DevRestartPill()
    {
        _devRestartPill?.Close();
        Views.GameModeToast? toast = null;
        var actions = new Dev.DevRestartActions(() => toast?.Close());
        toast = new Views.GameModeToast(new RestartViewModel(actions, DevGame?.Name ?? "Test game"));
        toast.Closed += (_, _) =>
        {
            if (ReferenceEquals(_devRestartPill, toast)) _devRestartPill = null;
        };
        _devRestartPill = toast;
        toast.ShowQuietly();
    }

    /// <summary>Opens the panel, then shows something in it.</summary>
    internal void DevInPanel(Action<MainWindow> show)
    {
        OpenPanel();
        if (_panel is { } panel) show(panel);
    }

    internal void DevCloseToasts()
    {
        _gameModeToast?.Close();
        _devRestartPill?.Close();
        _panel?.DevCloseToast();
    }
}
