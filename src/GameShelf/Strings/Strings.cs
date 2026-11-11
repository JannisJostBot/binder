using System.Globalization;
using System.Resources;

namespace GameShelf;

/// <summary>
/// The app's text. English is in <c>Strings.resx</c>, translations next to it (<c>Strings.de.resx</c>,
/// <c>Strings.fr.resx</c>); Windows' display language picks one, English fills any gap. Code reads the properties in
/// <c>Strings.Entries.cs</c>, XAML binds them with <c>{x:Bind s:Strings.Name}</c>. Text with values has <c>{0}</c>,
/// <c>{1}</c> in it for <see cref="Format"/>; counts use <see cref="Plural"/>. No reflection: ResourceManager reads
/// the embedded tables (and the satellite assemblies, which Native AOT compiles in).
/// </summary>
public sealed partial class Strings
{
    // Named after this class: the build names Strings.resx after the class in Strings.cs next to it.
    private static readonly ResourceManager Table = new(typeof(Strings));

    /// <summary>The entry in the current display language, or its name if it's missing (a bug the tests catch).</summary>
    public static string Get(string name) => Table.GetString(name, CultureInfo.CurrentUICulture) ?? name;

    /// <summary>An entry with values filled in, numbers and dates in the user's format.</summary>
    public static string Format(string text, params object?[] args) => string.Format(CultureInfo.CurrentCulture, text, args);

    /// <summary>
    /// "1 game" or "{0} games": <paramref name="one"/> or <paramref name="other"/> by the display language's plural
    /// rule, with the count as {0}. English and German use "one" for 1 only, French for 0 and 1.
    /// </summary>
    public static string Plural(long count, string one, string other) => Format(IsOne(count) ? one : other, count);

    private static bool IsOne(long count) =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr" ? count is 0 or 1 : count == 1;

    /// <summary>Text from the start of a sentence used inside one: "Yesterday" → "yesterday", "Vor 3 Tagen" → "vor 3 Tagen".</summary>
    public static string LowerFirst(string text) =>
        text.Length == 0 ? text : char.ToLower(text[0], CultureInfo.CurrentCulture) + text[1..];

    /// <summary>A list of names: "A", "A and B", "A, B and C", in the display language.</summary>
    public static string List(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "",
        1 => names[0],
        _ => Format(ListAnd, string.Join(ListSeparator, names.Take(names.Count - 1)), names[^1]),
    };
}
