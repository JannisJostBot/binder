using System.ComponentModel;
using Binder.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Binder.Views;

/// <summary>
/// First-run setup, three steps (spec §6, board 10). State and steps are <see cref="SetupViewModel"/>'s; the view
/// draws the launcher ticks and hosts the shortcut picker.
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
    }

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
}
