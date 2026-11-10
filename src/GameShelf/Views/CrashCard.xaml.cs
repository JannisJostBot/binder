using GameShelf.Library;
using GameShelf.Models;
using GameShelf.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace GameShelf.Views;

/// <summary>
/// The crash card (milestone 11, board 13): what happened in plain words, the details Windows recorded, and what
/// to do: play again, open the game's log, or copy a bug report or AI prompt (shown below once copied). Built from
/// a stored <see cref="CrashRecord"/>; reads the PC facts only when a text is copied.
/// </summary>
public sealed partial class CrashCard : UserControl
{
    private const int RecentSessions = 10;

    private Game? _game;
    private CrashRecord? _record;
    private string? _logPath;
    private int _problems, _recent;

    public event Action<Game>? PlayRequested;
    public event Action<CrashRecord>? Dismissed;

    public CrashCard()
    {
        InitializeComponent();
        RedistLink.NavigateUri = new Uri(CrashAnalysis.RedistUrl);
    }

    public CrashRecord? Record => _record;

    /// <param name="sessions">All sessions; the game's last 10 up to this one give "how often".</param>
    public void Show(Game game, CrashRecord record, IReadOnlyList<Session> sessions, IReadOnlyList<CrashRecord> records)
    {
        _game = game;
        _record = record;

        TitleText.Text = CrashAnalysis.Title(game.Name, record);
        ExplanationText.Text = CrashAnalysis.Explanation(record);
        AutomationProperties.SetName(this, TitleText.Text);

        DetailsGrid.Children.Clear();
        DetailsGrid.RowDefinitions.Clear();
        AddDetail(Strings.CrashWhatHappened, CrashAnalysis.WhatHappened(record));
        if (CrashAnalysis.Where(record) is { } where) AddDetail(Strings.CrashWhere, where);
        if (CrashAnalysis.AlsoAtThatTime(record) is { } also) AddDetail(Strings.CrashAlso, also);
        AddDetail(Strings.CrashWhen, Format.SessionStart(record.End));

        var recent = sessions.Where(s => s.GameId == game.Id && s.Start <= record.Start)
            .OrderByDescending(s => s.Start).Take(RecentSessions).ToList();
        _recent = Math.Max(1, recent.Count);
        _problems = Math.Max(1, recent.Count(s => CrashStore.For(records, s) is { IsProblem: true }));

        _logPath = GameLogs.Find(game);
        LogButton.Visibility = _logPath is null ? Visibility.Collapsed : Visibility.Visible;
        RedistLink.Visibility = CrashAnalysis.OffersRedist(record) ? Visibility.Visible : Visibility.Collapsed;
        PromptButton.Visibility = CrashAnalysis.OffersAiPrompt(record) ? Visibility.Visible : Visibility.Collapsed;
        PlayAgainButton.Visibility = record.Ending == GameEnding.StartFailed && CrashAnalysis.OffersRedist(record)
            ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(PlayAgainButton, Strings.Format(Strings.PlayAgainName, game.Name));
        CopiedPanel.Visibility = Visibility.Collapsed;
    }

    private void AddDetail(string label, string value)
    {
        int row = DetailsGrid.RowDefinitions.Count;
        DetailsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var name = new TextBlock
        {
            Text = label,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        };
        var text = new TextBlock
        {
            Text = value,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };
        AutomationProperties.SetName(text, Strings.Format(Strings.LabelValue, label, value));
        Grid.SetRow(name, row);
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 1);
        DetailsGrid.Children.Add(name);
        DetailsGrid.Children.Add(text);
    }

    private void OnDismissClick(object sender, RoutedEventArgs e)
    {
        if (_record is not null) Dismissed?.Invoke(_record);
    }

    private void OnPlayAgainClick(object sender, RoutedEventArgs e)
    {
        if (_game is not null) PlayRequested?.Invoke(_game);
    }

    private void OnLogClick(object sender, RoutedEventArgs e)
    {
        if (_logPath is not null) GameLogs.Open(_logPath);
    }

    private void OnReportClick(object sender, RoutedEventArgs e)
    {
        if (_game is null || _record is null) return;
        var store = LauncherIcons.HasBadge(_game.Source) ? LauncherBadge.Label(_game.Source) : null;
        Copy(CrashAnalysis.BugReport(_game.Name, store, _record, PcInfo.Read(), _problems, _recent),
            Strings.CopiedReport);
    }

    private void OnPromptClick(object sender, RoutedEventArgs e)
    {
        if (_game is null || _record is null) return;
        Copy(CrashAnalysis.AiPrompt(_game.Name, _record, PcInfo.Read(), _problems, _recent),
            Strings.CopiedPrompt);
    }

    private void Copy(string text, string note)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
            Clipboard.Flush(); // stays on the clipboard after the flyout closes
            CopiedNote.Text = note;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            // Another app holds the clipboard: show the text so it can still be selected and copied by hand.
            CopiedNote.Text = Strings.ClipboardFailed;
        }
        CopiedText.Text = text;
        CopiedPanel.Visibility = Visibility.Visible;
    }
}
