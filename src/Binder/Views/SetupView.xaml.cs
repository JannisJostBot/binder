using System.ComponentModel;
using Binder.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Binder.Views;

/// <summary>
/// First-run setup, four steps (spec §6, board 10). State and steps are <see cref="SetupViewModel"/>'s; the view
/// draws the launcher ticks and step 4's list of Game mode changes, and hosts the shortcut picker.
/// </summary>
public sealed partial class SetupView : UserControl
{
    public SetupViewModel? ViewModel { get; private set; }

    public SetupView()
    {
        InitializeComponent();
        AutomationProperties.SetName(this, Strings.SetupName);
        Picker.UseEmbedded();
        Picker.SelectionChanged += () => ViewModel?.SetSelection(Picker.Selected);
        _helper = new Disclosure(HelperChevron, HelperCard, HelperBody, Strings.DisclosureHelper);
    }

    private readonly Disclosure _helper;

    /// <summary>
    /// After step 2's Move: the rows of the picked shortcuts fly into Binder's logo (<see cref="LogoMerge"/>) while
    /// they're moved, then <paramref name="then"/> goes on to step 3. The page takes no clicks meanwhile; with animations off it goes on at once.
    /// </summary>
    public void PlayMerge(IReadOnlyList<Library.ShortcutFile> moved, Func<Task> then)
    {
        var rows = Picker.RowsFor(moved);
        IsHitTestVisible = false;
        LogoMerge.Play(MergeLayer, Step2, Picker.ListArea, rows, async () =>
        {
            await then();
            IsHitTestVisible = true;
            LogoMerge.Clear(MergeLayer, Step2, rows);
        });
    }

    /// <summary>Connects the page to its owner once, before it's first shown.</summary>
    public void Initialize(ISetupActions actions)
    {
        if (ViewModel is not null) throw new InvalidOperationException("The setup page is already initialized.");
        ViewModel = new SetupViewModel(actions);
        ViewModel.PropertyChanged += OnViewModelChanged;
        Bindings.Update();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        var vm = ViewModel!;
        if (e.PropertyName is null or "" or nameof(SetupViewModel.Launchers)) ShowLaunchers(vm.Launchers);
        if (e.PropertyName is null or "")
        {
            ShowGameModeItems(vm.GameModeItems);
            _helper.Set(false); // a new run starts closed
        }
        // Only when they arrive (step 2), so a new run doesn't flash an empty picker first.
        if (e.PropertyName == nameof(SetupViewModel.Shortcuts))
        {
            Picker.Show(vm.Shortcuts, vm.Library);
            vm.SetSelection(Picker.Selected);
        }
    }

    private void ShowLaunchers(SetupLauncherRow[] launchers)
    {
        LauncherList.Children.Clear();
        foreach (var row in launchers)
        {
            var line = new Grid();
            var box = new CheckBox { Content = row.Name, IsChecked = row.Enabled, Margin = new Thickness(0, 4, 0, 4) };
            box.Checked += (_, _) => row.Enabled = true;
            box.Unchecked += (_, _) => row.Enabled = false;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(box, row.Count);
            line.Children.Add(box);
            line.Children.Add(new TextBlock
            {
                Text = row.Count,
                Style = (Style)Application.Current.Resources["RowDetail"],
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0),
            });
            LauncherList.Children.Add(line);
        }
    }

    /// <summary>Step 4's list: one line per change, the ones that need the admin helper with a shield.</summary>
    private void ShowGameModeItems(SetupGameModeItem[] items)
    {
        GameModeList.Children.Clear();
        foreach (var item in items)
        {
            var line = new Grid { ColumnSpacing = 10 };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (item.NeedsAdmin)
            {
                var shield = new FontIcon { Glyph = "\uEA18", FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
                ToolTipService.SetToolTip(shield, Strings.SetupGmAdminMark);
                AutomationProperties.SetAccessibilityView(shield, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
                line.Children.Add(shield);
            }
            var text = new TextBlock { Text = item.Text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            if (item.NeedsAdmin) AutomationProperties.SetHelpText(text, Strings.SetupGmAdminMark);
            Grid.SetColumn(text, 1);
            line.Children.Add(text);
            GameModeList.Children.Add(line);
        }
    }
}
