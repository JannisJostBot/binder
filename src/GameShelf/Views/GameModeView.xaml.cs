using System.ComponentModel;
using GameShelf.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace GameShelf.Views;

/// <summary>
/// Settings › Game mode (spec §11, board 5). The state and what each control does live in
/// <see cref="GameModeViewModel"/> (bound with x:Bind); this file draws the rows (services, apps), opens the
/// app menus and keeps the flyout open while a menu or the UAC prompt has the focus.
/// </summary>
public sealed partial class GameModeView : UserControl
{
    public GameModeViewModel? ViewModel { get; private set; }

    public GameModeView()
    {
        InitializeComponent();
        AutomationProperties.SetName(this, Strings.GameMode);
    }

    /// <summary>Connects the page to its owner once, before it's first shown.</summary>
    public void Initialize(IGameModeActions actions)
    {
        if (ViewModel is not null) throw new InvalidOperationException("The Game mode page is already initialized.");
        ViewModel = new GameModeViewModel(actions);
        ViewModel.PropertyChanged += OnViewModelChanged;
        Bindings.Update();
    }

    public void FocusFirst() => BackButton.Focus(FocusState.Programmatic);

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        var name = e.PropertyName;
        bool all = string.IsNullOrEmpty(name);
        if (all || name == nameof(GameModeViewModel.CloseApps)) FillApps();
        if (all || name == nameof(GameModeViewModel.Services)) FillServices();
        if (all || name == nameof(GameModeViewModel.SmoothApps)) FillNames(SmoothList, smooth: true, AddSmoothButton);
        if (all || name == nameof(GameModeViewModel.UploadApps)) FillNames(UploadList, smooth: false, AddUploadButton);
        if (all || name == nameof(GameModeViewModel.AllowWarning))
            AllowBar.Severity = ViewModel!.AllowWarning ? InfoBarSeverity.Warning : InfoBarSeverity.Informational;
        if (all || name == nameof(GameModeViewModel.LastOk)) ShowLastIcon();
    }

    private void ShowLastIcon()
    {
        var ok = ViewModel!.LastOk;
        LastIcon.Glyph = ok switch { true => "", false => "", null => "" }; // done, warning, info
        LastIcon.Foreground = Resource<Microsoft.UI.Xaml.Media.Brush>(ok switch
        {
            true => "SystemFillColorSuccessBrush",
            false => "SystemFillColorCautionBrush",
            null => "TextFillColorSecondaryBrush",
        });
    }

    private void FillApps()
    {
        AppList.Children.Clear();
        foreach (var app in ViewModel!.CloseApps)
        {
            if (AppList.Children.Count > 0) AppList.Children.Add(new Border { Style = Resource<Style>("RowDivider") });
            var row = new Grid { Style = Resource<Style>("SettingRow"), ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { Style = Resource<Style>("RowText") };
            text.Children.Add(new TextBlock { Text = app.Name, Style = Resource<Style>("RowTitle"), TextTrimming = TextTrimming.CharacterEllipsis });
            var detail = new TextBlock { Text = app.Detail, Style = Resource<Style>("RowDetail") };
            text.Children.Add(detail);
            ToolTipService.SetToolTip(text, app.Path);
            row.Children.Add(text);

            // Close it (reopened after), or Calm it: efficiency mode, so it keeps running but uses less.
            var choice = new ComboBox { VerticalAlignment = VerticalAlignment.Center, MinWidth = 96 };
            choice.Items.Add(Strings.GmClose);
            choice.Items.Add(Strings.GmCalm);
            choice.SelectedIndex = app.Calm ? 1 : 0;
            AutomationProperties.SetName(choice, Strings.Format(Strings.GmWhilePlaying, app.Name));
            ToolTipService.SetToolTip(choice, Strings.GmCloseCalmToolTip);
            choice.SelectionChanged += (_, _) => detail.Text = ViewModel!.SetCalm(app.Path, choice.SelectedIndex == 1).Detail;
            Grid.SetColumn(choice, 1);
            row.Children.Add(choice);
            var remove = new Button { Content = "", Style = Resource<Style>("IconButton"), VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(remove, Strings.Format(Strings.RemoveName, app.Name));
            ToolTipService.SetToolTip(remove, Strings.Remove);
            remove.Click += (_, _) =>
            {
                ViewModel!.RemoveCloseApp(app.Path);
                AddAppButton.Focus(FocusState.Programmatic);
            };
            Grid.SetColumn(remove, 2);
            row.Children.Add(remove);
            AppList.Children.Add(row);
        }
    }

    /// <summary>The services' rows, with their state now.</summary>
    private void FillServices()
    {
        var vm = ViewModel!;
        ServiceList.Children.Clear();
        foreach (var service in vm.Services)
        {
            if (ServiceList.Children.Count > 0) ServiceList.Children.Add(new Border { Style = Resource<Style>("RowDivider") });
            var row = new Grid { Style = Resource<Style>("SettingRow") };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { Style = Resource<Style>("RowText") };
            text.Children.Add(new TextBlock { Text = service.Name, Style = Resource<Style>("RowTitle") });
            text.Children.Add(new TextBlock { Text = service.Detail, Style = Resource<Style>("RowDetail"), TextWrapping = TextWrapping.Wrap });
            row.Children.Add(text);
            var toggle = new ToggleSwitch { Style = Resource<Style>("RowSwitch"), IsOn = service.IsOn, IsEnabled = vm.HelperAllowed };
            AutomationProperties.SetName(toggle, Strings.Format(Strings.GmPauseName, service.Name));
            toggle.Toggled += (_, _) => ViewModel!.SetItem(service.Id, toggle.IsOn);
            Grid.SetColumn(toggle, 1);
            row.Children.Add(toggle);
            ServiceList.Children.Add(row);
        }
    }

    /// <summary>A list of exe names with a remove button each (kept smooth, or slowed down).</summary>
    private void FillNames(StackPanel list, bool smooth, Button addButton)
    {
        var vm = ViewModel!;
        list.Children.Clear();
        foreach (var exe in smooth ? vm.SmoothApps : vm.UploadApps)
        {
            var row = new Grid { Padding = new Thickness(0, 2, 0, 2), ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock
            {
                Text = Path.GetFileNameWithoutExtension(exe),
                Style = Resource<Style>("RowTitle"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var remove = new Button
            {
                Content = "", // Cancel (×), as on the close-apps rows
                Style = Resource<Style>("IconButton"),
                VerticalAlignment = VerticalAlignment.Center,
                IsEnabled = vm.HelperAllowed,
            };
            AutomationProperties.SetName(remove, Strings.Format(Strings.RemoveName, Path.GetFileNameWithoutExtension(exe)));
            ToolTipService.SetToolTip(remove, Strings.Remove);
            remove.Click += (_, _) =>
            {
                ViewModel!.RemoveName(smooth, exe);
                addButton.Focus(FocusState.Programmatic);
            };
            Grid.SetColumn(remove, 1);
            row.Children.Add(remove);
            list.Children.Add(row);
        }
    }

    private void OnAddSmoothClick(object sender, RoutedEventArgs e) =>
        ShowAppMenu(AddSmoothButton, ViewModel!.AppsFor(smooth: true), app => ViewModel!.AddName(true, app.Path));

    private void OnAddUploadClick(object sender, RoutedEventArgs e) =>
        ShowAppMenu(AddUploadButton, ViewModel!.AppsFor(smooth: false), app => ViewModel!.AddName(false, app.Path));

    /// <summary>Lists the apps with a window right now (read on click) to pick one from.</summary>
    private void OnAddAppClick(object sender, RoutedEventArgs e) =>
        ShowAppMenu(AddAppButton, ViewModel!.AppsToClose(), app => ViewModel!.AddCloseApp(app.Path));

    private static void ShowAppMenu(Button button, IReadOnlyList<AppChoice> apps, Action<AppChoice> pick)
    {
        var menu = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
        foreach (var app in apps)
        {
            var item = new MenuFlyoutItem { Text = app.Name };
            ToolTipService.SetToolTip(item, app.Path);
            item.Click += (_, _) => pick(app);
            menu.Items.Add(item);
        }
        if (apps.Count == 0) menu.Items.Add(new MenuFlyoutItem { Text = Strings.GmNoOtherApps, IsEnabled = false });
        App.ModalDepth++; // click-away must not close the flyout under the menu
        menu.Closed += (_, _) => App.ModalDepth--;
        menu.ShowAt(button);
    }

    private async void OnAllowClick(object sender, RoutedEventArgs e)
    {
        App.ModalDepth++; // the UAC prompt takes the focus: click-away must not close the flyout
        try
        {
            await ViewModel!.AllowAsync();
        }
        finally
        {
            App.ModalDepth--;
        }
    }

    private async void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        App.ModalDepth++;
        try
        {
            await ViewModel!.RemoveAsync();
        }
        finally
        {
            App.ModalDepth--;
        }
    }

    private static T Resource<T>(string key) => (T)Application.Current.Resources[key];
}
