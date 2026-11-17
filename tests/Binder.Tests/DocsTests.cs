using System.Reflection;
using System.Text.RegularExpressions;
using Binder.Shared;

namespace Binder.Tests;

/// <summary>
/// The website's documentation (site/docs.html) against the code, so the page can't drift from what Game mode
/// does. Each Game mode item is one card:
/// <c>&lt;section class="doc-item" id="gm-…" data-item="&lt;id&gt;" data-default="on|off" data-admin="yes|no"&gt;</c>
/// (attributes in that order), in the order of the Settings page. Changing an item, its default, whether it needs
/// the admin helper, a service, an app list, a limit or a percentage fails here until docs.html says the same.
/// </summary>
static partial class Tests
{
    static void DocsTests()
    {
        var path = FindRepoFile(Path.Combine("site", "docs.html"));
        if (path is null)
        {
            Check(false, "docs: site/docs.html found");
            return;
        }
        var html = File.ReadAllText(path);
        var cards = Regex.Matches(html,
                "<section class=\"doc-item\" id=\"gm-([a-z]+)\" data-item=\"([a-z]+)\" data-default=\"(on|off)\" data-admin=\"(yes|no)\">(.*?)</section>",
                RegexOptions.Singleline)
            .Select(m => (Anchor: m.Groups[1].Value, Id: m.Groups[2].Value, On: m.Groups[3].Value == "on",
                Admin: m.Groups[4].Value == "yes", Text: m.Groups[5].Value))
            .ToList();
        int attributes = Regex.Matches(html, "data-item=").Count;

        Check(attributes == cards.Count, "docs: every data-item is a well-formed Game mode card");
        Check(cards.Select(c => c.Id).SequenceEqual(GameModeInfo.Items),
            "docs: one card per Game mode item, in the Settings order (" + string.Join(",", GameModeInfo.Items) + ")");
        Check(cards.All(c => c.Anchor == c.Id), "docs: each card's anchor is gm-<item id>");

        foreach (var card in cards)
        {
            bool on = GameModeInfo.DefaultItems.Contains(card.Id);
            Check(card.On == on && card.Text.Contains(on ? "On by default" : "Off by default"),
                $"docs: {card.Id} says {(on ? "on" : "off")} by default, like GameModeInfo.DefaultItems");
            bool admin = GameModeInfo.IsElevatedItem(card.Id);
            Check(card.Admin == admin && card.Text.Contains(admin ? "Needs the admin helper" : "No admin rights needed"),
                $"docs: {card.Id} {(admin ? "needs" : "doesn't need")} the admin helper, like GameModeInfo.ElevatedItems");
            if (ElevatedPart.ServiceFor(card.Id) is { } service)
                Check(card.Text.Contains("<code>" + service.Service + "</code>"), $"docs: {card.Id} names the service {service.Service}");
        }

        string Card(string id) => cards.FirstOrDefault(c => c.Id == id).Text ?? "";

        // "only the four services below" in the admin helper part.
        string[] numbers = ["no", "one", "two", "three", "four", "five", "six", "seven", "eight"];
        Check(ElevatedPart.Services.Length < numbers.Length && html.Contains($"only the {numbers[ElevatedPart.Services.Length]} services"),
            "docs: the number of services Game mode may pause");
        Check(html.Contains($"any service other than the {numbers[Math.Min(ElevatedPart.Services.Length, numbers.Length - 1)]} above"),
            "docs: \"What Game mode never does\" counts the services the same");

        var limits = ElevatedPart.UploadLimits;
        var limitText = string.Join(", ", limits[..^1]) + " or " + limits[^1] + " Mbit/s";
        Check(Card(GameModeInfo.Uploads).Contains(limitText), "docs: the upload limits (" + limitText + ")");
        Check(Card(GameModeInfo.Uploads).Contains($"The default is {ElevatedPart.DefaultUploadLimit} Mbit/s"), "docs: the default upload limit");
        foreach (var exe in ElevatedPart.DefaultThrottledApps)
            Check(Card(GameModeInfo.Uploads).Contains("<code>" + exe + "</code>"), $"docs: {exe} is slowed down by default");

        foreach (var (name, _, _) in ElevatedPart.VoiceApps)
            Check(Card(GameModeInfo.Smooth).Contains(name), $"docs: {name} is kept smooth when installed");
        var dscp = Regex.Match(Strings.GmSmoothText, @"DSCP (\d+)").Groups[1].Value;
        Check(dscp.Length > 0 && Card(GameModeInfo.Smooth).Contains("DSCP " + dscp), "docs: the DSCP value is the one the Game mode page shows");

        Check(Card(GameModeInfo.Updates).Contains($"to {ElevatedPart.UpdatePercent}% of the bandwidth") &&
              Card(GameModeInfo.Updates).Contains($"a limit of {ElevatedPart.UpdatePercent}% or less"),
            $"docs: Windows Update is limited to {ElevatedPart.UpdatePercent}%");

        var gameMode = typeof(Binder.Watcher.GameMode);
        string? Const(string name) => gameMode.GetField(name, BindingFlags.NonPublic | BindingFlags.Static)?.GetRawConstantValue()?.ToString();
        Check(Card(GameModeInfo.GameBar).Contains($@"<code>HKEY_CURRENT_USER\{Const("GameDvrKey")}</code>") &&
              Card(GameModeInfo.GameBar).Contains($"<code>{Const("HistoricalCapture")}</code>"),
            "docs: the Game Bar recording value");
        Check(int.TryParse(Const("OneDriveWaitMs"), out int wait) && Card(GameModeInfo.OneDrive).Contains($"waits up to {wait / 1000} seconds"),
            "docs: how long OneDrive is given to close");
    }

    /// <summary>A file by its path from the repository root, found by walking up from the test's folder.</summary>
    static string? FindRepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
