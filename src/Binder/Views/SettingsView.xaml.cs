using System.ComponentModel;
using Binder.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Binder.Views;

/// <summary>
/// App settings, shown in place of the grid like the detail view. The state and what each control does live in
/// <see cref="SettingsViewModel"/> (bound with x:Bind); this file only builds the parts that are pure view.
/// </summary>
public sealed partial class SettingsView : UserControl
{
    public SettingsViewModel? ViewModel { get; private set; }

    public SettingsView()
    {
        InitializeComponent();
        AutomationProperties.SetName(this, Strings.Settings);

        // The parts that open from their card (board 19). They start closed.
        _ = new Disclosure(ModeChevron, ModeCard, ModeBody, Strings.DisclosureModes);
        _ = new Disclosure(StayReadyChevron, StayReadyCard, StayReadyBody, Strings.DisclosureStayReady);
        _ = new Disclosure(GameKeysChevron, GameKeysCard, GameKeysBody, Strings.DisclosureGameKeys);
        _ = new Disclosure(SourcesChevron, SourcesCard, SourcesBody, Strings.DisclosureSources);
        _ = new Disclosure(ShortcutsChevron, ShortcutsCard, ShortcutsBody, Strings.DisclosureShortcuts);
        _ = new Disclosure(HiddenChevron, HiddenCard, HiddenBody, Strings.DisclosureHidden);
        _ = new Disclosure(SourcePathsChevron, SourcePathsCard, SourcePathsBody, Strings.DisclosureSourcePaths);
        _whatsNew = new Disclosure(AboutChevron, AboutCard, AboutBody, Strings.DisclosureWhatsNew);
    }

    private readonly Disclosure _whatsNew;

    /// <summary>
    /// Shows Settings › About (the "…" menu's update item, the pill's What's new): scrolled into view, What's new
    /// opened when there are notes and <paramref name="openNotes"/>, focus on the card's text for screen readers.
    /// </summary>
    public void ShowAbout(bool openNotes)
    {
        if (openNotes && ViewModel is { HasNotes: true }) _whatsNew.Set(true);
        UpdateLayout();
        AboutCard.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0.3 });
    }

    /// <summary>Connects the page to its owner once, before it's first shown.</summary>
    public void Initialize(ISettingsActions actions)
    {
        if (ViewModel is not null) throw new InvalidOperationException("The Settings page is already initialized.");
        ViewModel = new SettingsViewModel(actions);
        ViewModel.PropertyChanged += OnViewModelChanged;
        Bindings.Update();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(SettingsViewModel.HiddenGames)) FillHidden();
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(SettingsViewModel.GameKeyRows)) FillGameKeys();
        // New data draws the launchers' entries again; a change to one updates them in place, so focus stays put.
        if (string.IsNullOrEmpty(e.PropertyName)) FillSourcePaths();
        else if (e.PropertyName == nameof(SettingsViewModel.SourceGroups)) UpdateSourcePaths();
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(SettingsViewModel.HasNotes))
        {
            _whatsNew.Enabled = ViewModel?.HasNotes == true;
            if (!_whatsNew.Enabled && _whatsNew.IsOpen) _whatsNew.Set(false);
        }
    }

    public void FocusFirst() => BackButton.Focus(FocusState.Programmatic);

    /// <summary>One row per hidden game, with a Show button (rows with dividers, so drawn here, not by a template).</summary>
    private void FillHidden()
    {
        var hidden = ViewModel?.HiddenGames ?? [];
        HiddenList.Children.Clear();
        foreach (var game in hidden)
        {
            if (HiddenList.Children.Count > 0)
                HiddenList.Children.Add(new Border { Style = (Style)Application.Current.Resources["RowDivider"], Margin = new Thickness(0, 0, -16, 0) });
            var row = new Grid { Style = (Style)Application.Current.Resources["SettingRow"], MinHeight = 52 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { Style = (Style)Application.Current.Resources["RowText"] };
            text.Children.Add(new TextBlock { Text = game.Name, Style = (Style)Application.Current.Resources["RowTitle"], TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = game.SourceLabel, Style = (Style)Application.Current.Resources["RowDetail"] });
            row.Children.Add(text);
            var show = new Button { Content = Strings.Show, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(show, Strings.Format(Strings.ShowGame, game.Name));
            show.Click += (_, _) => ViewModel?.Unhide(game);
            Grid.SetColumn(show, 1);
            row.Children.Add(show);
            HiddenList.Children.Add(row);
        }
    }

    // ---- Game shortcuts (spec §26) -------------------------------------------------------------------------

    private int _focusGameKey = -1; // the number whose list was just changed: its new list gets focus back

    /// <summary>
    /// One row per number: its key, then for 0 the last game played (fixed), for 1–9 a list of the shelf's games.
    /// Drawn again when the numbers change (the page is filled again), keeping focus on the list that changed.
    /// </summary>
    private void FillGameKeys()
    {
        var rows = ViewModel?.GameKeyRows ?? [];
        var names = ViewModel?.GameKeyGameNames ?? [];
        GameKeysList.Children.Clear();
        ComboBox? focus = null;
        foreach (var row in rows)
        {
            if (GameKeysList.Children.Count > 0)
                GameKeysList.Children.Add(new Border { Style = (Style)Application.Current.Resources["RowDivider"], Margin = new Thickness(0, 0, -16, 0) });
            var grid = new Grid { Style = (Style)Application.Current.Resources["SettingRow"], MinHeight = 48, ColumnSpacing = 14 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            string number = row.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var key = new Border { Style = (Style)Application.Current.Resources["KeyCap"], MinWidth = 22 };
            key.Child = new TextBlock { Text = number, Style = (Style)Application.Current.Resources["KeyCapText"], HorizontalAlignment = HorizontalAlignment.Center };
            AutomationProperties.SetAccessibilityView(key, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            grid.Children.Add(key);
            string label = ViewModel!.GameKeyLabel(row.Number);
            if (row.Number == Library.GameKeys.LastPlayed)
            {
                var text = new StackPanel { Style = (Style)Application.Current.Resources["RowText"] };
                text.Children.Add(new TextBlock { Text = Strings.GameKeyLastPlayed, Style = (Style)Application.Current.Resources["RowTitle"] });
                text.Children.Add(new TextBlock
                {
                    Text = row.GameName ?? Strings.NoGamePlayedYet,
                    Style = (Style)Application.Current.Resources["RowDetail"],
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
                Grid.SetColumn(text, 1);
                grid.Children.Add(text);
                ToolTipService.SetToolTip(grid, label);
                AutomationProperties.SetName(text, Strings.Format(Strings.GameKeyRowName, label, row.GameName ?? Strings.NoGamePlayedYet));
            }
            else
            {
                var box = new ComboBox
                {
                    ItemsSource = names,
                    SelectedIndex = row.ChoiceIndex,
                    MinWidth = 200,
                    MaxWidth = 260,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                AutomationProperties.SetName(box, label);
                ToolTipService.SetToolTip(box, label);
                int n = row.Number;
                box.SelectionChanged += (_, _) =>
                {
                    if (box.SelectedIndex < 0 || box.SelectedIndex == row.ChoiceIndex) return;
                    _focusGameKey = n;
                    ViewModel?.PickGameKey(n, box.SelectedIndex);
                };
                Grid.SetColumn(box, 2);
                grid.Children.Add(box);
                if (n == _focusGameKey) focus = box;
            }
            GameKeysList.Children.Add(grid);
        }
        _focusGameKey = -1;
        focus?.Focus(FocusState.Programmatic);
    }

    // ---- Advanced: where games and pictures are found (spec §14) ----------------------------------------------

    private sealed record EntryControls(SourcePathRow Row, TextBox Box, Button Reset, TextBlock Error, TextBlock Notice);
    private sealed record GroupControls(SourcePathGroup Group, Button Check, TextBlock Result);

    private readonly List<EntryControls> _entries = [];
    private readonly List<GroupControls> _groups = [];
    private readonly List<TeachingTip> _tips = [];

    /// <summary>
    /// Closes the (i) tips. An open tip is a popup of its own: left open when the page changes or the panel hides,
    /// it stays on screen without its window (and is the likely cause of a crash seen after the window was gone).
    /// </summary>
    public void CloseTips()
    {
        SourcePathsHelpTip.IsOpen = false;
        foreach (var tip in _tips) tip.IsOpen = false;
    }

    /// <summary>One block per launcher: its name and Check, then a text box per entry with Reset and its error.</summary>
    private void FillSourcePaths()
    {
        CloseTips();
        _tips.Clear();
        SourcePathsList.Children.Clear();
        _entries.Clear();
        _groups.Clear();
        if (ViewModel is not { } vm) return;
        foreach (var group in vm.SourceGroups)
        {
            var block = new StackPanel { Spacing = 8 };
            var head = new Grid { ColumnSpacing = 12 };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new TextBlock { Text = group.Name, Style = Res<Style>("BodyStrongTextBlockStyle"), VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetHeadingLevel(name, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level3);
            head.Children.Add(name);
            var check = new Button { Content = Strings.CheckSource, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(check, Strings.Format(Strings.CheckSourceName, group.Name));
            check.Click += async (_, _) => await vm.CheckSourceAsync(group.Launcher);
            Grid.SetColumn(check, 1);
            head.Children.Add(check);
            block.Children.Add(head);
            var result = new TextBlock { Style = Res<Style>("RowDetail"), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.None };
            AutomationProperties.SetLiveSetting(result, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
            block.Children.Add(result);
            _groups.Add(new(group, check, result));

            foreach (var row in group.Rows)
            {
                var entry = new StackPanel { Spacing = 4 };
                entry.Children.Add(EntryLabel(row));
                var line = new Grid { ColumnSpacing = 8 };
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var box = new TextBox { PlaceholderText = row.Placeholder, IsSpellCheckEnabled = false };
                AutomationProperties.SetName(box, row.Label);
                ToolTipService.SetToolTip(box, row.Placeholder);
                box.KeyDown += (_, e) =>
                {
                    if (e.Key != Windows.System.VirtualKey.Enter) return;
                    e.Handled = true;
                    vm.SetSourcePath(row.Entry, box.Text);
                };
                box.LostFocus += (_, _) => { if (box.Text != row.Value || row.Error is not null) vm.SetSourcePath(row.Entry, box.Text); };
                line.Children.Add(box);
                var reset = new Button { Content = Strings.ResetEntry, VerticalAlignment = VerticalAlignment.Top };
                AutomationProperties.SetName(reset, Strings.Format(Strings.ResetEntryName, row.Label));
                reset.Click += (_, _) => vm.ResetSourcePath(row.Entry);
                Grid.SetColumn(reset, 1);
                line.Children.Add(reset);
                entry.Children.Add(line);
                var error = new TextBlock
                {
                    Style = Res<Style>("RowDetail"), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.None,
                    Foreground = Res<Microsoft.UI.Xaml.Media.Brush>("SystemFillColorCriticalBrush"),
                };
                AutomationProperties.SetLiveSetting(error, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Assertive);
                entry.Children.Add(error);
                var notice = new TextBlock { Style = Res<Style>("RowDetail"), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.None };
                entry.Children.Add(notice);
                block.Children.Add(entry);
                _entries.Add(new(row, box, reset, error, notice));
            }
            SourcePathsList.Children.Add(block);
        }
        UpdateSourcePaths();
    }

    /// <summary>Shows the view model's values, errors and Check results in the drawn controls.</summary>
    private void UpdateSourcePaths()
    {
        foreach (var (row, box, reset, error, notice) in _entries)
        {
            if (box.Text != row.Value)
            {
                box.Text = row.Value;
                box.SelectionStart = box.Text.Length;
            }
            reset.Visibility = row.IsChanged || row.Error is not null ? Visibility.Visible : Visibility.Collapsed;
            error.Text = row.Error ?? "";
            error.Visibility = row.Error is null ? Visibility.Collapsed : Visibility.Visible;
            notice.Text = row.Notice ?? "";
            notice.Visibility = row.Notice is null ? Visibility.Collapsed : Visibility.Visible;
        }
        foreach (var (group, check, result) in _groups)
        {
            check.IsEnabled = !group.Checking;
            result.Text = group.CheckResult ?? "";
            result.Visibility = group.CheckResult is null ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>
    /// The entry's label with an (i) that opens what the entry is, its format and an example: the same title-and-tip
    /// as Free up space's map.
    /// </summary>
    private StackPanel EntryLabel(SourcePathRow row)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        line.Children.Add(new TextBlock { Text = row.Label, VerticalAlignment = VerticalAlignment.Center });
        var about = Strings.Format(Strings.InfoAbout, row.Label);
        var info = new Button { Style = Res<Style>("IconButton"), Content = "" };
        AutomationProperties.SetName(info, about);
        ToolTipService.SetToolTip(info, about);
        var tip = new TeachingTip
        {
            Target = info, Title = row.Label, IsLightDismissEnabled = true, PreferredPlacement = TeachingTipPlacementMode.Bottom,
            Content = new TextBlock { Text = SettingsViewModel.EntryHelp(row.Entry), TextWrapping = TextWrapping.Wrap },
        };
        info.Click += (_, _) => tip.IsOpen = !tip.IsOpen;
        line.Children.Add(info);
        line.Children.Add(tip);
        _tips.Add(tip);
        return line;
    }

    private void OnSourcePathsHelpClick(object sender, RoutedEventArgs e) => SourcePathsHelpTip.IsOpen = !SourcePathsHelpTip.IsOpen;

    private void OnResetAllPathsClick(object sender, RoutedEventArgs e) => ViewModel?.ResetAllSourcePaths();

    private static T Res<T>(string key) => (T)Application.Current.Resources[key];
}
