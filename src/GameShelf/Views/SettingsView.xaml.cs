using System.ComponentModel;
using GameShelf.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace GameShelf.Views;

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
        _ = new Disclosure(SourcesChevron, SourcesCard, SourcesBody, Strings.DisclosureSources);
        _ = new Disclosure(ShortcutsChevron, ShortcutsCard, ShortcutsBody, Strings.DisclosureShortcuts);
        _ = new Disclosure(HiddenChevron, HiddenCard, HiddenBody, Strings.DisclosureHidden);
        _ = new Disclosure(SourcePathsChevron, SourcePathsCard, SourcePathsBody, Strings.DisclosureSourcePaths);
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
        // New data draws the launchers' entries again; a change to one updates them in place, so focus stays put.
        if (string.IsNullOrEmpty(e.PropertyName)) FillSourcePaths();
        else if (e.PropertyName == nameof(SettingsViewModel.SourceGroups)) UpdateSourcePaths();
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

    // ---- Advanced: where games and pictures are found (spec §14) ----------------------------------------------

    private sealed record EntryControls(SourcePathRow Row, TextBox Box, Button Reset, TextBlock Error, TextBlock Notice);
    private sealed record GroupControls(SourcePathGroup Group, Button Check, TextBlock Result);

    private readonly List<EntryControls> _entries = [];
    private readonly List<GroupControls> _groups = [];

    /// <summary>One block per launcher: its name and Check, then a text box per entry with Reset and its error.</summary>
    private void FillSourcePaths()
    {
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
            var name = new TextBlock { Text = group.Name, Style = Res<Style>("RowTitle"), VerticalAlignment = VerticalAlignment.Center };
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

    /// <summary>The entry's label with an (i) that opens what the entry is, its format and an example.</summary>
    private static StackPanel EntryLabel(SourcePathRow row)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        line.Children.Add(new TextBlock
        {
            Text = row.Label, Style = Res<Style>("RowDetail"), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.None,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var about = Strings.Format(Strings.InfoAbout, row.Label);
        var info = new Button { Style = Res<Style>("IconButton"), Content = "", Width = 28, Height = 28, FontSize = 13 };
        AutomationProperties.SetName(info, about);
        ToolTipService.SetToolTip(info, about);
        var text = new TextBlock { Text = SettingsViewModel.EntryHelp(row.Entry), TextWrapping = TextWrapping.Wrap, MaxWidth = 320 };
        info.Flyout = new Flyout { Content = text };
        line.Children.Add(info);
        return line;
    }

    private void OnSourcePathsHelpClick(object sender, RoutedEventArgs e) => SourcePathsHelpTip.IsOpen = !SourcePathsHelpTip.IsOpen;

    private void OnResetAllPathsClick(object sender, RoutedEventArgs e) => ViewModel?.ResetAllSourcePaths();

    private static T Res<T>(string key) => (T)Application.Current.Resources[key];
}
