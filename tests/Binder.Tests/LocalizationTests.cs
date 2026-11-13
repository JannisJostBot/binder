using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Binder.Views;

namespace Binder.Tests;

static partial class Tests
{
    /// <summary>
    /// The app's text tables (src/Binder/Strings): the same entries in every language, the same {0} placeholders,
    /// a translator's comment on every English entry that names its placeholders, one property per entry, and every
    /// Strings.X the app's code and XAML use exists. Reads the source files, so the views and XAML (not compiled
    /// here) are covered too.
    /// </summary>
    static void LocalizationTests()
    {
        var dir = FindStringsFolder();
        if (dir is null)
        {
            Check(false, "localization: src/Binder/Strings found");
            return;
        }
        Dictionary<string, string> Read(string file) => XDocument.Load(Path.Combine(dir, file)).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");
        var en = Read("Strings.resx");
        // Context for translators: every English entry says where it appears and what each {0} holds.
        var comments = XDocument.Load(Path.Combine(dir, "Strings.resx")).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => ((string?)d.Element("comment") ?? "").Trim());
        var uncommented = comments.Where(c => c.Value.Length == 0).Select(c => c.Key).ToList();
        Check(uncommented.Count == 0, $"localization: every English entry has a comment for translators ({string.Join(", ", uncommented)})");
        var unexplained = en.Where(e => Placeholders(e.Value).Any(n => !comments[e.Key].Contains("{" + n + "}")))
            .Select(e => e.Key).ToList();
        Check(unexplained.Count == 0, $"localization: every comment says what its {{0}} placeholders hold ({string.Join(", ", unexplained)})");
        var de = Read("Strings.de.resx");
        var fr = Read("Strings.fr.resx");
        Check(en.Count > 100, "localization: English table read");
        foreach (var (lang, table) in new[] { ("de", de), ("fr", fr) })
        {
            var missing = en.Keys.Except(table.Keys).ToList();
            var extra = table.Keys.Except(en.Keys).ToList();
            Check(missing.Count == 0 && extra.Count == 0, $"localization: {lang} has the same entries (missing: {string.Join(", ", missing)}; extra: {string.Join(", ", extra)})");
            var empty = table.Where(e => e.Value.Trim().Length == 0 && en[e.Key].Trim().Length > 0).Select(e => e.Key).ToList();
            Check(empty.Count == 0, $"localization: no empty {lang} entries ({string.Join(", ", empty)})");
            var wrong = en.Keys.Where(k => table.TryGetValue(k, out var t) && !Placeholders(en[k]).SetEquals(Placeholders(t))).ToList();
            Check(wrong.Count == 0, $"localization: {lang} placeholders match English ({string.Join(", ", wrong)})");
        }

        var properties = typeof(Strings).GetProperties(BindingFlags.Public | BindingFlags.Static).Select(p => p.Name).ToHashSet();
        Check(properties.SetEquals(en.Keys), $"localization: one Strings property per entry (no property: {string.Join(", ", en.Keys.Except(properties))}; " +
            $"no entry: {string.Join(", ", properties.Except(en.Keys))})");

        var methods = typeof(Strings).GetMethods(BindingFlags.Public | BindingFlags.Static).Select(m => m.Name).ToHashSet();
        var src = Path.GetFullPath(Path.Combine(dir, ".."));
        var unknown = new SortedSet<string>();
        foreach (var file in Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".cs") || f.EndsWith(".xaml")) && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                                 && !f.Contains(Path.DirectorySeparatorChar + "Strings" + Path.DirectorySeparatorChar)))
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"\bStrings\.([A-Z]\w*)"))
                if (!properties.Contains(m.Groups[1].Value) && !methods.Contains(m.Groups[1].Value))
                    unknown.Add($"{m.Groups[1].Value} ({Path.GetFileName(file)})");
        Check(unknown.Count == 0, $"localization: every Strings.X in the app exists ({string.Join(", ", unknown)})");

        // The tables load, and the display language picks them.
        var before = CultureInfo.CurrentUICulture;
        var beforeFormat = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Check(Strings.Play == "Spielen" && Strings.Plural(1, Strings.GameCountOne, Strings.GameCount) == "1 Spiel" &&
                  Strings.Plural(0, Strings.GameCountOne, Strings.GameCount) == "0 Spiele", "localization: German, with its plurals");
            Check(Strings.LowerFirst(Strings.Format(Strings.WeeksAgo, 3)) == "vor 3 Wochen", "localization: lower case only at the start");
            Check(Format.Duration(TimeSpan.FromMinutes(155)) == "2 Std. 35 Min.", "localization: German durations");
            CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Check(Strings.Play == "Jouer" && Strings.Plural(0, Strings.GameCountOne, Strings.GameCount) == "0 jeu" &&
                  Strings.Plural(2, Strings.GameCountOne, Strings.GameCount) == "2 jeux", "localization: French, 0 is singular");
            Check(Strings.List(["A", "B", "C"]) == "A, B et C", "localization: French lists");
            CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-ES");
            Check(Strings.Play == "Play", "localization: other languages get English");
        }
        finally
        {
            CultureInfo.CurrentUICulture = before;
            CultureInfo.CurrentCulture = beforeFormat;
        }
        Check(Strings.List(["A", "B"]) == "A and B" && Strings.List(["A"]) == "A" && Strings.List([]) == "", "localization: English lists");
    }

    static HashSet<string> Placeholders(string text) =>
        Regex.Matches(text, @"\{(\d+)[^}]*\}").Select(m => m.Groups[1].Value).ToHashSet();

    static string? FindStringsFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Binder", "Strings");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }
}
