// DEBUG BUILDS ONLY (see App.Dev.cs): left out of Release builds. English only; users never see it.
using Binder.Library;
using Binder.Models;
using Binder.Shared;
using Binder.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Binder.Dev;

/// <summary>
/// The debug panel ("Binder.exe --dev" from a Debug build): buttons that show each toast, card and message through
/// the app's real code, with made-up data, so they can be checked without playing a game. Built in code (no XAML),
/// one window at a time.
/// </summary>
internal static class DevPanel
{
    /// <summary>Searched for by publish.ps1 in everything it publishes: finding it means debug code got into a release.</summary>
    internal const string Marker = "BINDER-DEBUG-PANEL-NOT-FOR-RELEASE";

    private static Window? _window;

    public static void Show(App app)
    {
        if (_window is not null)
        {
            _window.Activate();
            return;
        }

        var list = new StackPanel { Spacing = 6, Padding = new Thickness(16) };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.7 };

        void Group(string name) => list.Children.Add(new TextBlock
        {
            Text = name,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            Margin = new Thickness(0, 12, 0, 2),
        });

        void Add(string label, Action action)
        {
            var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
            button.Click += (_, _) =>
            {
                try
                {
                    action();
                    status.Text = label;
                }
                catch (Exception e)
                {
                    status.Text = label + ": " + e.GetType().Name + ": " + e.Message;
                }
            };
            list.Children.Add(button);
        }

        void Crash(string label, Func<Game, CrashRecord> make) => Add(label, () =>
        {
            if (app.DevGame is not { } game)
            {
                status.Text = "The library is empty: add a game first.";
                return;
            }
            var record = make(game);
            record.Seen = true; // dismissing it then saves nothing
            app.DevInPanel(panel => panel.DevCrash(record));
        });

        Group("Game mode toast");
        Add("On · 5 changes", () => app.DevApplied(new RestoreResult(5, 5, [])));
        Add("On · 1 change", () => app.DevApplied(new RestoreResult(1, 1, [])));
        Add("On · already set up", () => app.DevApplied(new RestoreResult(0, 0, [])));
        Add("On · 2 of 3, Print Spooler failed", () => app.DevApplied(new RestoreResult(2, 3, ["Print Spooler"])));
        Add("Back to normal · 5 changes", () => app.DevRestored(new RestoreResult(5, 5, [])));
        Add("Back to normal · 4 of 5 (card)", () => app.DevRestored(new RestoreResult(4, 5, ["Print Spooler"])));
        Add("Back to normal · 3 of 5, two failed (card)", () => app.DevRestored(new RestoreResult(3, 5, ["Print Spooler", "Spotify"])));
        Add("Restart pill after a crash", app.DevRestartPill);

        // Test branch (ToastMelt): the same toasts with the melt; the buttons above stay today's toast.
        Group("Melt (test branch)");
        var slow = new CheckBox { Content = "Quarter speed" };
        list.Children.Add(slow);
        void Melt(string label, Views.MeltMode mode, bool applied, int changes) => Add(label, () =>
            app.DevMelt(mode, slow.IsChecked == true, () =>
            {
                if (applied) app.DevApplied(new RestoreResult(changes, changes, []));
                else app.DevRestored(new RestoreResult(changes, changes, []));
            }));
        Melt("On · 5 changes · Win2D", Views.MeltMode.Win2D, applied: true, 5);
        Melt("On · 5 changes · outline", Views.MeltMode.Outline, applied: true, 5);
        Melt("On · 2 changes · Win2D", Views.MeltMode.Win2D, applied: true, 2);
        Melt("Back to normal · 5 changes · Win2D", Views.MeltMode.Win2D, applied: false, 5);
        Melt("Back to normal · 5 changes · outline", Views.MeltMode.Outline, applied: false, 5);
        Melt("Back to normal · 2 changes · Win2D", Views.MeltMode.Win2D, applied: false, 2);

        Group("Crash card (opens the panel)");
        Crash("Graphics driver (NVIDIA), driver reset", g => Record(g, GameEnding.Crashed, CrashCulprit.NvidiaDriver, 0xC0000005, "nvwgf2umx.dll", driverReset: true));
        Crash("Overlay or mod", g => Record(g, GameEnding.Crashed, CrashCulprit.Overlay, 0xC0000005, "dxgi.dll"));
        Crash("Visual C++ runtime", g => Record(g, GameEnding.Crashed, CrashCulprit.CppRuntime, 0xC0000409, "VCRUNTIME140.dll"));
        Crash("Didn't start, runtime missing", g => Record(g, GameEnding.StartFailed, CrashCulprit.MissingRuntime, 0xC0000135, "VCRUNTIME140.dll", length: TimeSpan.FromSeconds(2)));
        Crash("Froze, Windows closed it", g => Record(g, GameEnding.ClosedUnexpectedly, CrashCulprit.None, null, null, hang: true));
        Crash("Unknown error", g => Record(g, GameEnding.Crashed, CrashCulprit.Unknown, 0xC0000409, "Game.exe"));
        Crash("Error code only", g => Record(g, GameEnding.ClosedUnexpectedly, CrashCulprit.None, 1, null, exitCode: 1));

        Group("In the panel");
        Add("Welcome back, with a milestone", () =>
        {
            if (app.DevGame is not { } game) return;
            var start = DateTimeOffset.Now - TimeSpan.FromMinutes(72);
            app.DevInPanel(panel => panel.DevWelcome(new WelcomeMoment(game.Id, start, TimeSpan.FromMinutes(72), TimeSpan.FromHours(4.5), 10)));
        });
        Add("Couldn't detect the game", () =>
        {
            if (app.DevGame is { } game) app.DevInPanel(panel => panel.ShowNotDetected(game.Id));
        });
        Add("Updated to 9.9.9 (pill)", () => app.DevInPanel(panel => panel.ShowUpdated(Strings.Format(Strings.Updated, "9.9.9"), hasNotes: true)));
        Add("Something went wrong (error toast)", () => app.DevInPanel(panel => panel.ShowError()));

        Group("Tools");
        Add("Open the panel", app.OpenPanel);
        Add("Close all toasts and cards", app.DevCloseToasts);

        list.Children.Add(status);
        var root = new ScrollViewer { Content = list };
        AutomationProperties.SetHelpText(root, Marker);

        var window = new Window { Title = "Binder · debug panel", Content = root };
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(420, 900));
        window.Closed += (_, _) => _window = null;
        _window = window;
        window.Activate();
    }

    private static CrashRecord Record(Game game, GameEnding ending, CrashCulprit culprit, uint? error, string? module,
        TimeSpan? length = null, bool driverReset = false, bool hang = false, uint exitCode = 0xC0000005)
    {
        var end = DateTimeOffset.Now - TimeSpan.FromMinutes(1);
        return new CrashRecord
        {
            GameId = game.Id,
            Start = end - (length ?? TimeSpan.FromMinutes(72)),
            End = end,
            Ending = ending,
            Culprit = culprit,
            ExitCode = hang ? 1u : exitCode,
            ErrorCode = error,
            Module = module,
            Exe = "Game.exe",
            Hang = hang,
            DriverReset = driverReset,
        };
    }
}

/// <summary>The restart pill's actions in the debug panel: whatever happens, it only closes.</summary>
internal sealed class DevRestartActions(Action close) : IRestartActions
{
    public void RestartNow() => close();

    public void RestartCancelled() => close();
}
