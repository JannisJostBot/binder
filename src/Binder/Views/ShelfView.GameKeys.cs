using Binder.Library;
using Binder.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;

namespace Binder.Views;

/// <summary>
/// Spec §26, board 37: dragging a cover (or a row) raises the numbers 0–9 over the bottom of the grid; dropping it on
/// one gives the game that number for its game shortcut. A click still plays: a drag starts only past Windows' drag
/// distance. The right-click menu's "Shortcut" does the same from the keyboard, a controller or a screen reader.
/// </summary>
public sealed partial class ShelfView
{
    /// <summary>Our own format, so a cover dropped on another app gives it nothing.</summary>
    private const string DragFormat = "Binder.Game";

    private Game? _dragged;
    private Run? _slotHintMain, _slotHintDetail;
    private readonly List<(int Number, Border Slot, Game? Holder)> _slots = [];

    private void OnDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.Count != 1 || e.Items[0] is not Game game || ViewModel is null)
        {
            e.Cancel = true;
            return;
        }
        _dragged = game;
        e.Data.SetData(DragFormat, game.Id);
        e.Data.RequestedOperation = DataPackageOperation.Link;
        ShowKeySlots(game);
    }

    private void OnDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        _dragged = null;
        KeySlotHost.Visibility = Visibility.Collapsed;
        KeySlotHost.Child = null; // released until the next drag
        _slots.Clear();
        _slotHintMain = _slotHintDetail = null;
    }

    /// <summary>Builds the row of numbers: each with a small mark of the game on it, a dashed "+" when free.</summary>
    private void ShowKeySlots(Game dragged)
    {
        var resources = Application.Current.Resources;
        var onKeys = ViewModel!.GamesOnKeys();
        _slots.Clear();
        var hint = new TextBlock { Style = (Style)resources["CaptionTextBlockStyle"], TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
        _slotHintMain = new Run { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        _slotHintDetail = new Run { Foreground = (Brush)resources["TextFillColorSecondaryBrush"] };
        hint.Inlines.Add(_slotHintMain);
        hint.Inlines.Add(_slotHintDetail);
        var row = new Grid { ColumnSpacing = 6 };
        for (int number = 0; number <= 9; number++)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var slot = KeySlot(number, onKeys[number], dragged, resources);
            Grid.SetColumn(slot, number);
            row.Children.Add(slot);
            _slots.Add((number, slot, onKeys[number]));
        }
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(hint);
        panel.Children.Add(row);
        bool highContrast = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
        var card = new Border
        {
            Child = panel,
            Padding = new Thickness(10, 10, 10, 12),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(highContrast ? 2 : 1),
            Background = (Brush)resources["ToastSurfaceBrush"],
            BorderBrush = (Brush)resources["ToastStrokeBrush"],
        };
        AutomationProperties.SetName(card, Strings.GameKeys);
        KeySlotHost.Child = card;
        KeySlotHost.Visibility = Visibility.Visible;
        ShowSlotHint(null);
        if (new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
        {
            var shift = new TranslateTransform();
            card.RenderTransform = shift;
            var rise = new DoubleAnimation
            {
                From = 12, To = 0, Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            Storyboard.SetTarget(rise, shift);
            Storyboard.SetTargetProperty(rise, "Y");
            var fade = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(120) };
            Storyboard.SetTarget(fade, card);
            Storyboard.SetTargetProperty(fade, "Opacity");
            var show = new Storyboard();
            show.Children.Add(rise);
            show.Children.Add(fade);
            show.Begin();
        }
    }

    private Border KeySlot(int number, Game? holder, Game dragged, ResourceDictionary resources)
    {
        bool locked = number == GameKeys.LastPlayed;
        var key = new Border { Style = (Style)resources["KeyCap"], MinWidth = 20, HorizontalAlignment = HorizontalAlignment.Center };
        key.Child = new TextBlock
        {
            Text = number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Style = (Style)resources["KeyCapText"],
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var mark = new Grid { Width = 30, Height = 30, HorizontalAlignment = HorizontalAlignment.Center };
        if (holder is not null)
        {
            mark.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(FallbackArt.Parse(holder.CoverTint ?? CoverColors.MonogramTone(holder.Name))),
            });
            mark.Children.Add(new TextBlock
            {
                Text = CoverColors.Initials(holder.Name),
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        else
        {
            mark.Children.Add(new Rectangle
            {
                RadiusX = 4, RadiusY = 4, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 2 },
                Stroke = (Brush)resources["ControlStrongStrokeColorDefaultBrush"],
            });
            mark.Children.Add(new FontIcon { Glyph = "", FontSize = 12, Foreground = (Brush)resources["TextFillColorTertiaryBrush"] });
        }
        var content = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(key);
        content.Children.Add(mark);
        var slot = new Border
        {
            Child = content,
            Height = 70,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Background = (Brush)resources["ControlFillColorDefaultBrush"],
            BorderBrush = (Brush)resources["ControlStrokeColorDefaultBrush"],
            AllowDrop = !locked,
            // The last game played can't be given away; the dragged game's own number dims (dropping elsewhere moves it).
            Opacity = locked ? 0.6 : holder?.Id == dragged.Id ? 0.45 : 1,
        };
        string label = ViewModel!.GameKeyLabel(number);
        AutomationProperties.SetName(slot, Strings.Format(Strings.GameKeyRowName, label,
            locked ? Strings.GameKeyLastPlayed : holder?.Name ?? Strings.GameKeyFree));
        if (locked) ToolTipService.SetToolTip(slot, Strings.GameKeyZeroToolTip);
        else
        {
            slot.DragOver += (_, e) => OnSlotDragOver(slot, number, holder, e);
            slot.DragLeave += (_, _) => SetSlotOver(slot, false);
            slot.Drop += (_, e) => OnSlotDrop(number, e);
        }
        return slot;
    }

    private void OnSlotDragOver(Border slot, int number, Game? holder, DragEventArgs e)
    {
        e.Handled = true; // not the window's "drop an .exe to add it"
        if (_dragged is not { } game || !e.DataView.Contains(DragFormat))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }
        e.AcceptedOperation = DataPackageOperation.Link;
        e.DragUIOverride.Caption = Strings.Format(Strings.GameKeyPut, game.Name, number);
        e.DragUIOverride.IsGlyphVisible = false;
        SetSlotOver(slot, true);
        ShowSlotHint((number, holder));
    }

    private void OnSlotDrop(int number, DragEventArgs e)
    {
        e.Handled = true;
        if (_dragged is { } game && e.DataView.Contains(DragFormat)) ViewModel?.SetGameKey(game, number);
    }

    /// <summary>The slot under the pointer gets the accent edge; the others go back to normal.</summary>
    private void SetSlotOver(Border slot, bool over)
    {
        var resources = Application.Current.Resources;
        foreach (var (_, other, _) in _slots)
        {
            bool on = over && ReferenceEquals(other, slot);
            other.BorderThickness = new Thickness(on ? 2 : 1);
            other.BorderBrush = (Brush)resources[on ? "AccentFillColorDefaultBrush" : "ControlStrokeColorDefaultBrush"];
        }
        if (!over) ShowSlotHint(null);
    }

    /// <summary>The line above the numbers: what dropping does here.</summary>
    private void ShowSlotHint((int Number, Game? Holder)? over)
    {
        if (_slotHintMain is null || _slotHintDetail is null || _dragged is not { } game) return;
        if (over is not { } target)
        {
            _slotHintMain.Text = Strings.Format(Strings.GameKeyDropHint, game.Name);
            _slotHintDetail.Text = " · " + Strings.GameKeyZeroHint;
            return;
        }
        _slotHintMain.Text = Strings.Format(Strings.GameKeyPut, game.Name, target.Number);
        _slotHintDetail.Text = " · " + (target.Holder is null ? ViewModel!.GameKeyLabel(target.Number)
            : target.Holder.Id == game.Id ? Strings.GameKeyAlreadyThere
            : Strings.Format(Strings.GameKeyReplaces, target.Holder.Name));
    }
}
