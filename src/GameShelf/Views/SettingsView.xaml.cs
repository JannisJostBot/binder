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

        // The parts that open from their card (board 19). They start closed.
        _ = new Disclosure(ModeChevron, ModeCard, ModeBody, "display modes");
        _ = new Disclosure(StayReadyChevron, StayReadyCard, StayReadyBody, "stay ready options");
        _ = new Disclosure(SourcesChevron, SourcesCard, SourcesBody, "game sources");
        _ = new Disclosure(ShortcutsChevron, ShortcutsCard, ShortcutsBody, "desktop shortcut options");
        _ = new Disclosure(HiddenChevron, HiddenCard, HiddenBody, "hidden games");
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
            var show = new Button { Content = "Show", VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(show, "Show " + game.Name);
            show.Click += (_, _) => ViewModel?.Unhide(game);
            Grid.SetColumn(show, 1);
            row.Children.Add(show);
            HiddenList.Children.Add(row);
        }
    }
}
