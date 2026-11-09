namespace GameShelf.Views;

/// <summary>What the per-game menu, the shelf and the detail page can ask to do with a game.</summary>
public enum GameAction { Play, Details, OpenFolder, StorePage, VerifyFiles, Uninstall, ToggleFavorite, Hide, TestLaunch, FreeUpSpace }

/// <summary>One bar of a <c>BarChart</c> (here, without WinUI, so view models can build them): its axis label, its tooltip and its value.</summary>
public readonly record struct Bar(string Label, string ToolTip, double Value);
