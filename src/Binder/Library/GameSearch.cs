using System.Globalization;
using System.Text;
using Binder.Models;

namespace Binder.Library;

/// <summary>
/// The shelf's search (spec §22 a): which games match what was typed and which comes first, so Enter can play the
/// top match. Pure code over the in-memory list, run per keystroke; nothing is stored.
/// </summary>
public static class GameSearch
{
    /// <summary>How well a name matches, best first. <see cref="None"/>: not at all.</summary>
    public enum Rank { Prefix, WordStart, Initials, Anywhere, None }

    // Case and accents ignored, culture-aware like the Name sort ("e" finds "É", "ss" may find "ß").
    private const CompareOptions Options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreKanaType |
        CompareOptions.IgnoreWidth;

    /// <summary>
    /// 1. the name starts with the query ("bal" → Baldur's Gate 3); 2. a word in it does ("gate"); 3. the query is
    /// the words' first letters, numbers whole ("bg3", "rdr2", "l4d2"; a leading "The" may be left out); 4. it's
    /// anywhere in the name.
    /// </summary>
    public static Rank Match(string name, string query, CompareInfo? compare = null)
    {
        query = query.Trim();
        if (query.Length == 0) return Rank.Prefix;
        compare ??= CultureInfo.CurrentCulture.CompareInfo;
        if (compare.IsPrefix(name, query, Options)) return Rank.Prefix;
        for (int i = 1; i < name.Length; i++)
            if (IsWordStart(name, i) && compare.IsPrefix(name.AsSpan(i), query, Options)) return Rank.WordStart;
        if (query.Length >= 2 && !query.Contains(' '))
            foreach (var initials in Initials(name))
                if (compare.IsPrefix(initials, query, Options)) return Rank.Initials;
        return compare.IndexOf(name, query, Options) >= 0 ? Rank.Anywhere : Rank.None;
    }

    /// <summary>
    /// The games that match, best first; within a rank the most recently played first, then the order they came in
    /// (the shelf's sort, so the name for unplayed games under the default sort).
    /// </summary>
    public static List<Game> Search(IEnumerable<Game> games, string query, IReadOnlyDictionary<string, DateTimeOffset> lastPlayed)
    {
        var compare = CultureInfo.CurrentCulture.CompareInfo;
        var found = new List<(Game Game, Rank Rank, int Order)>();
        int order = 0;
        foreach (var game in games)
        {
            var rank = Match(game.Name, query, compare);
            if (rank != Rank.None) found.Add((game, rank, order));
            order++;
        }
        found.Sort((a, b) =>
        {
            int c = a.Rank.CompareTo(b.Rank);
            if (c != 0) return c;
            bool aPlayed = lastPlayed.TryGetValue(a.Game.Id, out var aWhen), bPlayed = lastPlayed.TryGetValue(b.Game.Id, out var bWhen);
            if (aPlayed != bPlayed) return aPlayed ? -1 : 1;
            if (aPlayed && (c = bWhen.CompareTo(aWhen)) != 0) return c;
            return a.Order.CompareTo(b.Order);
        });
        return found.ConvertAll(f => f.Game);
    }

    /// <summary>A word starts here: after a space or punctuation (not an apostrophe: "Baldur's" is one word), or where digits follow letters ("Left4Dead").</summary>
    private static bool IsWordStart(string name, int i)
    {
        char c = name[i], before = name[i - 1];
        if (!char.IsLetterOrDigit(c)) return false;
        if (IsApostrophe(before)) return false;
        return !char.IsLetterOrDigit(before) || (char.IsDigit(c) != char.IsDigit(before));
    }

    private static bool IsApostrophe(char c) => c is '\'' or '’';

    /// <summary>The name's initials, with and (when it starts with "The") without the first word: "Red Dead Redemption 2" → "RDR2".</summary>
    public static IEnumerable<string> Initials(string name)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        foreach (char c in name)
        {
            if (IsApostrophe(c)) continue;
            bool split = !char.IsLetterOrDigit(c) || (word.Length > 0 && char.IsDigit(c) != char.IsDigit(word[^1]));
            if (split && word.Length > 0)
            {
                words.Add(word.ToString());
                word.Clear();
            }
            if (char.IsLetterOrDigit(c)) word.Append(c);
        }
        if (word.Length > 0) words.Add(word.ToString());
        if (words.Count == 0) yield break;

        static string Join(IEnumerable<string> words)
        {
            var initials = new StringBuilder();
            foreach (var w in words) initials.Append(char.IsDigit(w[0]) ? w : w[..1]); // numbers whole: "76", "2077"
            return initials.ToString();
        }
        yield return Join(words);
        if (words.Count > 1 && words[0].Equals("The", StringComparison.OrdinalIgnoreCase)) yield return Join(words.Skip(1));
    }
}
