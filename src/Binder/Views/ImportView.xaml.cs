using Binder.Library;
using Binder.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Binder.Views;

/// <summary>One shortcut in the picker.</summary>
public sealed class ImportRow(ShortcutFile shortcut, string detail, bool selected)
{
    public ShortcutFile Shortcut { get; } = shortcut;
    public string Name => Shortcut.Name;
    public string Detail { get; } = detail;
    public bool Selected { get; set; } = selected;
}

/// <summary>Picker for desktop shortcuts to import. Nothing is moved until Import is pressed.</summary>
public sealed partial class ImportView : UserControl
{
    public event Action? Cancelled;
    public event Action<IReadOnlyList<ShortcutFile>>? ImportRequested;
    /// <summary>The ticked set changed (for the setup's "Move N" button).</summary>
    public event Action? SelectionChanged;

    private List<ImportRow> _rows = [];
    private bool _embedded;

    public ImportView()
    {
        InitializeComponent();
        AutomationProperties.SetName(this, Strings.ImportShortcuts);
    }

    /// <summary>Esc: back to settings (the window asks). In setup, Esc is the window's (closes the flyout).</summary>
    public bool CancelByEscape()
    {
        if (_embedded) return false;
        Cancelled?.Invoke();
        return true;
    }

    /// <summary>Inside first-run setup (step 2): only the list; setup shows its own title and buttons.</summary>
    public void UseEmbedded()
    {
        _embedded = true;
        HeaderRow.Visibility = Visibility.Collapsed;
        IntroText.Visibility = Visibility.Collapsed;
        ActionsRow.Visibility = Visibility.Collapsed;
        Root.Padding = new Thickness(0);
        Root.RowSpacing = 0;
        ListCard.Margin = new Thickness(0);
    }

    /// <summary>The rows showing these shortcuts, in list order (setup's merge animation).</summary>
    public IReadOnlyList<FrameworkElement> RowsFor(IReadOnlyList<ShortcutFile> shortcuts)
    {
        var wanted = new HashSet<ShortcutFile>(shortcuts, ReferenceEqualityComparer.Instance);
        var found = new List<FrameworkElement>();
        for (int i = 0; i < _rows.Count && i < RowsList.Children.Count; i++)
            if (wanted.Contains(_rows[i].Shortcut) && RowsList.Children[i] is FrameworkElement row) found.Add(row);
        return found;
    }

    /// <summary>The card the rows scroll in: what's outside it isn't in view.</summary>
    public FrameworkElement ListArea => ListCard;

    public IReadOnlyList<ShortcutFile> Selected => _rows.Where(r => r.Selected).Select(r => r.Shortcut).ToList();

    public void Show(IReadOnlyList<ShortcutFile> shortcuts, IReadOnlyList<Game> library)
    {
        _rows = shortcuts
            .Select(s => new ImportRow(s, Describe(s, library), ShortcutImporter.LooksLikeGame(s, library)))
            .OrderByDescending(r => r.Selected)
            .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        RowsList.Children.Clear();
        foreach (var row in _rows)
        {
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = row.Name, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = row.Detail, Style = (Style)Application.Current.Resources["RowDetail"] });
            var box = new CheckBox { Content = text, IsChecked = row.Selected, Margin = new Thickness(0, 4, 0, 4) };
            AutomationProperties.SetName(box, row.Name);
            // Checked/Unchecked rather than Click: UI Automation (screen readers, tests) toggles without a click.
            box.Checked += (_, _) => Toggle(row, true);
            box.Unchecked += (_, _) => Toggle(row, false);
            RowsList.Children.Add(box);
        }
        EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateButton();
    }

    public void FocusFirst() => BackButton.Focus(FocusState.Programmatic);

    private void Toggle(ImportRow row, bool selected)
    {
        row.Selected = selected;
        UpdateButton();
    }

    private static string Describe(ShortcutFile s, IReadOnlyList<Game> library)
    {
        if (ShortcutImporter.ExistingFor(s, library) is { } existing)
            return Strings.Format(Strings.Dot, existing.SourceLabel, Strings.AlreadyOnShelf);
        if (s.SteamAppId is not null) return Strings.Format(Strings.LinkFrom, "Steam");
        if (s.EpicAppName is not null) return Strings.Format(Strings.LinkFrom, "Epic Games");
        if (s.TargetPath is { } target) return target;
        return s.Url is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri) ? Strings.Format(Strings.Dot, Strings.Link, $"{uri.Scheme}://{uri.Host}") : Strings.Link;
    }

    private void UpdateButton()
    {
        int count = _rows.Count(r => r.Selected);
        ImportButton.Content = Strings.Plural(count, Strings.ImportCountOne, Strings.ImportCount);
        ImportButton.IsEnabled = count > 0;
        SelectionChanged?.Invoke();
    }

    private void OnImportClick(object sender, RoutedEventArgs e) =>
        ImportRequested?.Invoke(_rows.Where(r => r.Selected).Select(r => r.Shortcut).ToList());

    private void OnCancelClick(object sender, RoutedEventArgs e) => Cancelled?.Invoke();
}
