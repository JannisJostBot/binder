using GameShelf.Library;
using GameShelf.Services;
using GameShelf.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace GameShelf.Views;

/// <summary>Launch options and anti-cheat (milestone 10): the rows <see cref="DetailViewModel"/> describes, drawn here.</summary>
public sealed partial class DetailView
{
    private readonly List<CheckBox> _suggestionBoxes = [];
    private bool _updatingArgs;

    // ---- Launch options ----

    /// <summary>One check box per suggested option for the game's engine.</summary>
    private void DrawSuggestions()
    {
        var vm = ViewModel!;
        SuggestionList.Children.Clear();
        _suggestionBoxes.Clear();
        foreach (var choice in vm.Suggestions)
        {
            var box = new CheckBox { IsChecked = choice.IsOn, IsEnabled = vm.LaunchSupported, Content = SuggestionContent(choice.Suggestion) };
            AutomationProperties.SetName(box, choice.Name);
            box.Click += OnSuggestionClick;
            _suggestionBoxes.Add(box);
            SuggestionList.Children.Add(box);
        }
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
            Foreground = Resource<Brush>("TextFillColorSecondaryBrush"),
        });
        return panel;
    }

    private void OnSuggestionClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm || sender is not CheckBox clicked) return;
        vm.SetSuggestion(_suggestionBoxes.IndexOf(clicked), clicked.IsChecked == true);
        // Another option of the same group may have been unticked.
        for (int i = 0; i < _suggestionBoxes.Count && i < vm.Suggestions.Length; i++)
            _suggestionBoxes[i].IsChecked = vm.Suggestions[i].IsOn;
    }

    /// <summary>The extra arguments, except while the user is typing in the field (don't rewrite it under the cursor).</summary>
    private void WriteExtraArgs()
    {
        if (ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), ExtraArgsBox)) return;
        _updatingArgs = true;
        try
        {
            ExtraArgsBox.Text = ViewModel!.ExtraArgs;
        }
        finally
        {
            _updatingArgs = false;
        }
    }

    private void OnExtraArgsChanged(object sender, TextChangedEventArgs e)
    {
        if (!_updatingArgs && ViewModel is not null) ViewModel.ExtraArgs = ExtraArgsBox.Text;
    }

    private void OnExtraArgsLostFocus(object sender, RoutedEventArgs e) => ViewModel?.CommitExtraArgs();

    /// <summary>"Start with": another exe, only from inside the game's install folder (checked by the view model).</summary>
    private void OnPickStartWith(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { Game: { InstallDir: not null } game } vm) return;
        var path = FilePicker.Open(this, Strings.Format(Strings.PickStartWithTitle, game.Name), Strings.Format(Strings.FilterPrograms, "*.exe") + "|*.exe", vm.InstallFolder);
        if (path is not null) vm.SetStartWith(path);
    }

    // ---- Anti-cheat ----

    private void DrawAntiCheat()
    {
        var vm = ViewModel!;
        if (!vm.HasAntiCheat)
        {
            _antiCheat.Set(false);
            return;
        }

        RequirementList.Children.Clear();
        ConflictList.Children.Clear();
        if (vm.Checking)
            RequirementList.Children.Add(new TextBlock { Text = Strings.CheckingPc, Style = Resource<Style>("RowDetail") });
        foreach (var requirement in vm.Requirements)
            RequirementList.Children.Add(RequirementRow(requirement));
        foreach (var (conflict, text) in vm.Conflicts)
            ConflictList.Children.Add(ConflictRow(conflict, text));
        foreach (var note in vm.DriverToolNotes)
            ConflictList.Children.Add(StatusRow("", "TextFillColorSecondaryBrush", note, null));
        ConflictList.Visibility = ConflictList.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var glyph = vm.AntiCheatOk ? "" : "";
        var brush = Resource<Brush>(vm.AntiCheatOk ? "SystemFillColorSuccessBrush" : "SystemFillColorCautionBrush");
        AntiCheatGlyph.Glyph = glyph;
        AntiCheatGlyph.Foreground = brush;
        AntiCheatStateGlyph.Glyph = glyph;
        AntiCheatStateGlyph.Foreground = brush;

        // Board 21: the card stays closed when all is well and opens by itself when something needs attention,
        // unless the user already opened or closed it for this game.
        if (!vm.AntiCheatTouched) _antiCheat.Set(vm.AntiCheatIssues > 0);
    }

    private Grid RequirementRow(RequirementInfo requirement)
    {
        var row = new Grid { ColumnSpacing = 10, MinHeight = 28 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock { Text = requirement.Name, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        var tag = StatusTag(requirement.Status, requirement.Met);
        Grid.SetColumn(tag, 1);
        row.Children.Add(tag);
        AutomationProperties.SetName(row, Strings.Format(Strings.LabelValue, requirement.Name, requirement.Status));
        if (requirement.HelpUrl is { } help)
        {
            var link = new HyperlinkButton { Content = Strings.HowToFix, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 2, 4, 2) };
            link.Click += (_, _) => ViewModel?.OpenLink(help);
            AutomationProperties.SetName(link, Strings.Format(Strings.HowToFixName, requirement.Name));
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

    private Grid ConflictRow(DetailConflict conflict, string text)
    {
        var row = StatusRow("", "SystemFillColorCautionBrush", text, null);
        var close = new Button { Content = Strings.Close, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        AutomationProperties.SetName(close, Strings.Format(Strings.CloseApp, conflict.Name));
        ToolTipService.SetToolTip(close, Strings.Format(Strings.CloseAppToolTip, conflict.Name));
        close.Click += (_, _) =>
        {
            ViewModel?.CloseConflict(conflict);
            close.IsEnabled = false;
            close.Content = Strings.AskedToClose;
        };
        Grid.SetColumn(close, 2);
        row.Children.Add(close);
        return row;
    }

    /// <summary>An icon, a sentence and optionally a "How to fix" link to Microsoft's help.</summary>
    private Grid StatusRow(string glyph, string brushKey, string text, string? helpUrl)
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
            var link = new HyperlinkButton { Content = Strings.HowToFix, VerticalAlignment = VerticalAlignment.Center };
            link.Click += (_, _) => ViewModel?.OpenLink(helpUrl);
            AutomationProperties.SetName(link, Strings.Format(Strings.HowToFixName, text));
            Grid.SetColumn(link, 2);
            row.Children.Add(link);
        }
        return row;
    }

    private static T Resource<T>(string key) => (T)Application.Current.Resources[key];

    private void OnAntiCheatLabelClick(object sender, RoutedEventArgs e)
    {
        _antiCheat.Set(true);
        if (ViewModel is not null) ViewModel.AntiCheatTouched = true;
        AntiCheatSection.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0 });
    }
}
