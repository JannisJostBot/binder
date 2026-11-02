using GameShelf.Library;
using GameShelf.Models;
using GameShelf.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace GameShelf.Views;

/// <summary>Launch options and anti-cheat (milestone 10): both come from a look at the game's files when the view opens.</summary>
public sealed partial class DetailView
{
    /// <summary>What the last look at the game's folder and this PC found, for the game on show.</summary>
    private sealed record ScanInfo(
        string GameId,
        GameScanResult? Result,
        List<(PcRequirement Requirement, bool? Met)> Requirements,
        List<PcChecks.Conflict> Conflicts,
        List<string> DriverTools);

    private ScanInfo? _scan;
    private readonly List<(CheckBox Box, LaunchSuggestion Suggestion)> _suggestionBoxes = [];
    private bool _updatingArgs;

    /// <summary>
    /// Engine and anti-cheat from the install folder (cached on the game until it updates), then the PC checks and
    /// running programs. Off the UI thread; the result is dropped if another game is on show by then.
    /// </summary>
    private async Task ScanAsync(Game game)
    {
        ScanInfo info;
        try
        {
            info = await Task.Run(() =>
            {
                var result = GameScan.Get(game);
                IReadOnlyList<AntiCheatKind> kinds = result?.AntiCheats ?? game.AntiCheats ?? [];
                var requirements = AntiCheat.Requirements(kinds, game.Name, PcChecks.IsWindows11)
                    .Select(r => (r, PcChecks.Meets(r)))
                    .ToList();
                bool any = kinds.Count > 0;
                return new ScanInfo(game.Id, result, requirements,
                    any ? PcChecks.RunningConflicts() : [],
                    any ? PcChecks.BlockedDriverTools() : []);
            });
        }
        catch (Exception e)
        {
            ErrorLog.Write(e, "game scan");
            return;
        }
        if (_game is null || _game.Id != game.Id) return;
        _scan = info;
        if (info.Result is { } result && GameScan.Store(game, result)) GameEdited?.Invoke(game);
        UpdateLaunchOptions();
        UpdateAntiCheat();
    }

    // ---- Launch options ----

    private void UpdateLaunchOptions()
    {
        if (_game is not { } game) return;
        var route = LaunchOptions.RouteFor(game);
        bool supported = route != LaunchOptions.Route.NotSupported;

        bool canPick = game.InstallDir is not null && GameActions.InstallFolder(game) is not null;
        var startWith = LaunchOptions.ValidOverride(game);
        StartWithButton.IsEnabled = canPick;
        StartWithText.Text = startWith is not null ? Path.GetFileName(startWith)
            : canPick ? "Automatic" : "Automatic (there's no install folder to choose from)";
        ToolTipService.SetToolTip(StartWithText, startWith);
        AutoStartWithItem.IsEnabled = game.ExeOverride is not null;

        var engine = _scan?.Result?.Engine ?? game.Engine ?? GameEngine.Unknown;
        bool scanning = _scan is null && game.Engine is null && game.InstallDir is not null;
        bool known = engine != GameEngine.Unknown;
        EngineTagText.Text = known ? LaunchOptions.EngineName(engine) : scanning ? "Checking…" : "Not recognized";
        EngineTagText.Foreground = Resource<Brush>(known ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");
        EngineGlyph.Visibility = known ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(EngineTag, "Engine: " + EngineTagText.Text);
        EngineText.Text = LaunchOptions.Suggestions(engine).Count > 0 ? "Suggested options" : "Add the arguments this game understands below.";
        _updatingArgs = true;
        try
        {
            var tokens = LaunchOptions.Split(game.LaunchArgs);
            SuggestionList.Children.Clear();
            _suggestionBoxes.Clear();
            foreach (var suggestion in LaunchOptions.Suggestions(engine))
            {
                bool on = LaunchOptions.IndexOf(tokens, suggestion.Tokens) >= 0;
                if (on) tokens = LaunchOptions.Without(tokens, suggestion);
                var box = new CheckBox { IsChecked = on, IsEnabled = supported, Content = SuggestionContent(suggestion) };
                AutomationProperties.SetName(box, $"{suggestion.Label} ({suggestion.Args})");
                box.Click += OnSuggestionClick;
                _suggestionBoxes.Add((box, suggestion));
                SuggestionList.Children.Add(box);
            }
            SuggestionList.Visibility = _suggestionBoxes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            // Don't rewrite the field under the user's cursor.
            if (!ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), ExtraArgsBox))
                ExtraArgsBox.Text = LaunchOptions.Join(tokens);
            ExtraArgsBox.IsEnabled = supported;
        }
        finally
        {
            _updatingArgs = false;
        }
        UpdateStartsAs();
    }

    private static StackPanel SuggestionContent(LaunchSuggestion suggestion)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = suggestion.Label });
        panel.Children.Add(new TextBlock
        {
            Text = suggestion.Args,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });
        return panel;
    }

    private void UpdateStartsAs()
    {
        if (_game is not { } game) return;
        var route = LaunchOptions.RouteFor(game);
        StartsAsText.Text = "Starts as: " + Launcher.Describe(game);
        string? note = route switch
        {
            LaunchOptions.Route.Steam when !string.IsNullOrWhiteSpace(game.LaunchArgs) =>
                "Steam may ask you to confirm these arguments the first time.",
            LaunchOptions.Route.NotSupported when game.Source == GameSource.Epic =>
                "Epic games take arguments only from the Epic Games Launcher: Library, the game's … menu, Manage, Launch options.",
            LaunchOptions.Route.NotSupported when game.Source == GameSource.Shortcut =>
                "This shortcut can't pass arguments. Choose the game's program under Start with to use them.",
            LaunchOptions.Route.NotSupported =>
                $"{game.SourceLabel} games start through their launcher, which doesn't take arguments from other apps. Choose the game's program under Start with to use them.",
            _ => null,
        };
        LaunchNoteText.Text = note ?? "";
        LaunchNoteText.Visibility = note is null ? Visibility.Collapsed : Visibility.Visible;
        ResetLaunchButton.IsEnabled = game.LaunchArgs is not null || game.ExeOverride is not null;
        TestLaunchButton.IsEnabled = route != LaunchOptions.Route.NotSupported;
    }

    /// <summary>The arguments as the checkboxes and the field show them: ticked suggestions first, then the extras.</summary>
    private void ComposeArgs()
    {
        if (_game is not { } game) return;
        var tokens = new List<string>();
        foreach (var (box, suggestion) in _suggestionBoxes)
            if (box.IsChecked == true) tokens.AddRange(suggestion.Tokens);
        tokens.AddRange(LaunchOptions.Split(ExtraArgsBox.Text));
        var joined = LaunchOptions.Join(tokens);
        game.LaunchArgs = joined.Length == 0 ? null : joined.Length > LaunchOptions.MaxLength ? joined[..LaunchOptions.MaxLength] : joined;
    }

    private void OnSuggestionClick(object sender, RoutedEventArgs e)
    {
        if (_game is not { } game || sender is not CheckBox clicked) return;
        var group = _suggestionBoxes.FirstOrDefault(s => s.Box == clicked).Suggestion?.Group;
        if (clicked.IsChecked == true && group is not null)
            foreach (var (box, suggestion) in _suggestionBoxes)
                if (box != clicked && suggestion.Group == group) box.IsChecked = false;
        ComposeArgs();
        UpdateStartsAs();
        GameEdited?.Invoke(game);
    }

    private void OnExtraArgsChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingArgs) return;
        ComposeArgs();
        UpdateStartsAs(); // saved when the field loses focus, not per keystroke
    }

    private void OnExtraArgsLostFocus(object sender, RoutedEventArgs e)
    {
        if (_game is { } game) GameEdited?.Invoke(game);
    }

    /// <summary>"Start with": another exe, only from inside the game's install folder.</summary>
    private void OnPickStartWith(object sender, RoutedEventArgs e)
    {
        if (_game is not { } game || game.InstallDir is null) return;
        var path = FilePicker.Open(this, $"Choose the program to start {game.Name} with", "Programs (*.exe)|*.exe",
            GameActions.InstallFolder(game));
        if (path is null) return;
        if (!LaunchOptions.IsInside(path, game.InstallDir) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            LaunchNoteText.Text = "Choose a program inside the game's own folder.";
            LaunchNoteText.Visibility = Visibility.Visible;
            return;
        }
        game.ExeOverride = Path.GetFullPath(path);
        SaveLaunchOptions(game);
    }

    private void OnAutoStartWith(object sender, RoutedEventArgs e)
    {
        if (_game is not { } game) return;
        game.ExeOverride = null;
        SaveLaunchOptions(game);
    }

    private void OnTestLaunchClick(object sender, RoutedEventArgs e)
    {
        if (_game is not { } game) return;
        ComposeArgs();
        GameEdited?.Invoke(game);
        Raise(GameAction.TestLaunch);
    }

    private void OnResetLaunchClick(object sender, RoutedEventArgs e)
    {
        if (_game is not { } game) return;
        game.LaunchArgs = null;
        game.ExeOverride = null;
        _updatingArgs = true;
        ExtraArgsBox.Text = "";
        _updatingArgs = false;
        SaveLaunchOptions(game);
    }

    private void SaveLaunchOptions(Game game)
    {
        GameEdited?.Invoke(game);
        UpdateLaunchOptions();
    }

    // ---- Anti-cheat ----

    private void UpdateAntiCheat()
    {
        if (_game is not { } game) return;
        IReadOnlyList<AntiCheatKind> kinds = _scan?.Result?.AntiCheats ?? game.AntiCheats ?? [];
        if (kinds.Count == 0)
        {
            AntiCheatLabel.Visibility = Visibility.Collapsed;
            AntiCheatSection.Visibility = Visibility.Collapsed;
            return;
        }

        var names = string.Join(", ", kinds.Select(AntiCheat.Name));
        AntiCheatNames.Text = names;
        RemindCheck.IsChecked = game.AntiCheatReminder;
        RequirementList.Children.Clear();
        ConflictList.Children.Clear();

        int issues = 0;
        if (_scan is null)
        {
            RequirementList.Children.Add(new TextBlock { Text = "Checking this PC…", Style = Resource<Style>("RowDetail") });
        }
        else
        {
            foreach (var (requirement, met) in _scan.Requirements)
            {
                if (met == false) issues++;
                RequirementList.Children.Add(RequirementRow(requirement, met));
            }
            foreach (var conflict in _scan.Conflicts)
            {
                issues++;
                ConflictList.Children.Add(ConflictRow(conflict, names));
            }
            foreach (var tool in _scan.DriverTools)
                ConflictList.Children.Add(StatusRow("", "TextFillColorSecondaryBrush",
                    $"A driver from {tool} is loaded. Some anti-cheats block it; updating the tool usually helps.", null));
        }
        ConflictList.Visibility = ConflictList.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        AntiCheatGlyph.Glyph = issues == 0 ? "" : "";
        AntiCheatGlyph.Foreground = Resource<Brush>(issues == 0 ? "SystemFillColorSuccessBrush" : "SystemFillColorCautionBrush");
        AntiCheatLabelText.Text = issues == 0 ? names : $"{names} · {issues} {(issues == 1 ? "issue" : "issues")}";
        AutomationProperties.SetName(AntiCheatLabel, AntiCheatLabelText.Text + ". Show details");
        AntiCheatLabel.Visibility = Visibility.Visible;
        AntiCheatSection.Visibility = Visibility.Visible;
    }

    private static Grid RequirementRow(PcRequirement requirement, bool? met)
    {
        // The tag states the setting's state; its color says whether that's what the anti-cheat needs.
        string status = (requirement, met) switch
        {
            (_, null) => "Couldn't check",
            (PcRequirement.NoTestSigning or PcRequirement.NoKernelDebugging, true) => "Off",
            (PcRequirement.NoTestSigning or PcRequirement.NoKernelDebugging, false) => "On",
            (PcRequirement.Tpm20, true) => "Found",
            (PcRequirement.Tpm20, false) => "Not found",
            (PcRequirement.VanguardLoaded, true) => "Running",
            (PcRequirement.VanguardLoaded, false) => "Restart needed",
            (_, true) => "On",
            (_, false) => "Off",
        };        string name = requirement switch
        {
            // The tag says whether it's on, so the row names the setting itself.
            PcRequirement.NoTestSigning => "Test signing",
            PcRequirement.NoKernelDebugging => "Kernel debugging",
            PcRequirement.VanguardLoaded => "Vanguard driver",
            _ => AntiCheat.RequirementName(requirement),
        };

        var row = new Grid { ColumnSpacing = 10, MinHeight = 28 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        var tag = StatusTag(status, met);
        Grid.SetColumn(tag, 1);
        row.Children.Add(tag);
        AutomationProperties.SetName(row, $"{name}: {status}");
        if (met == false && AntiCheat.HelpUrl(requirement) is { } help)
        {
            var link = new HyperlinkButton { Content = "How to fix", VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 2, 4, 2) };
            link.Click += (_, _) => GameActions.ShellOpen(help);
            AutomationProperties.SetName(link, $"How to fix {name}");
            Grid.SetColumn(link, 2);
            row.Children.Add(link);
        }
        return row;
    }

    /// <summary>
    /// A small pill with an icon. Solid status colors with the text-on-accent color (dark on green or yellow in
    /// dark mode, white on deep green or amber in light mode) so it reads well over the acrylic; neutral and
    /// outlined when the state is unknown.
    /// </summary>
    private static Border StatusTag(string text, bool? good)
    {
        var (glyph, background) = good switch
        {
            true => ("", "SystemFillColorSuccessBrush"),
            false => ("", "SystemFillColorCautionBrush"),
            null => ("", "SystemFillColorNeutralBackgroundBrush"),
        };
        var foreground = Resource<Brush>(good is null ? "TextFillColorSecondaryBrush" : "TextOnAccentFillColorPrimaryBrush");
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 11, Foreground = foreground, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock
        {
            Text = text,
            Style = Resource<Style>("CaptionTextBlockStyle"),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return new Border
        {
            Child = content,
            Padding = new Thickness(8, 3, 9, 4),
            CornerRadius = new CornerRadius(10),
            Background = Resource<Brush>(background),
            BorderBrush = Resource<Brush>(good is null ? "CardStrokeColorDefaultBrush" : "ControlStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(good is null ? 1 : 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
    }
    private Grid ConflictRow(PcChecks.Conflict conflict, string antiCheat)
    {
        var row = StatusRow("", "SystemFillColorCautionBrush", $"{conflict.Name} is running. {antiCheat} may refuse to start next to it.", null);
        var close = new Button { Content = "Close", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        AutomationProperties.SetName(close, "Close " + conflict.Name);
        ToolTipService.SetToolTip(close, $"Ask {conflict.Name} to close, like clicking its X");
        close.Click += (_, _) =>
        {
            PcChecks.AskToClose(conflict);
            close.IsEnabled = false;
            close.Content = "Asked to close";
        };
        Grid.SetColumn(close, 2);
        row.Children.Add(close);
        return row;
    }

    /// <summary>An icon, a sentence and optionally a "How to fix" link to Microsoft's help.</summary>
    private static Grid StatusRow(string glyph, string brushKey, string text, string? helpUrl)
    {
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14, Foreground = Resource<Brush>(brushKey), VerticalAlignment = VerticalAlignment.Center });
        var label = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(label, 1);
        row.Children.Add(label);
        if (helpUrl is not null)
        {
            var link = new HyperlinkButton { Content = "How to fix", VerticalAlignment = VerticalAlignment.Center };
            link.Click += (_, _) => GameActions.ShellOpen(helpUrl);
            AutomationProperties.SetName(link, "How to fix: " + text);
            Grid.SetColumn(link, 2);
            row.Children.Add(link);
        }
        return row;
    }

    private static T Resource<T>(string key) => (T)Application.Current.Resources[key];

    private void OnAntiCheatLabelClick(object sender, RoutedEventArgs e) =>
        AntiCheatSection.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0 });

    private void OnRemindClick(object sender, RoutedEventArgs e)
    {
        if (_game is not { } game) return;
        game.AntiCheatReminder = RemindCheck.IsChecked == true;
        GameEdited?.Invoke(game);
    }
}
