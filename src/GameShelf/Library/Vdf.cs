using System.Text;

namespace GameShelf.Library;

/// <summary>A KeyValues object: values are either string or VdfObject. Keys are case-insensitive.</summary>
public sealed class VdfObject : Dictionary<string, object>
{
    public VdfObject() : base(StringComparer.OrdinalIgnoreCase) { }

    public string? Str(string key) => TryGetValue(key, out var v) ? v as string : null;
    public VdfObject? Obj(string key) => TryGetValue(key, out var v) ? v as VdfObject : null;
}

/// <summary>
/// Minimal parser for Valve's text KeyValues format (.vdf / .acf). The files come from Steam library folders,
/// which can sit on drives other users or devices can write to, so input is treated as untrusted: nesting
/// depth and file size are capped, and malformed input throws <see cref="FormatException"/>.
/// </summary>
public static class Vdf
{
    /// <summary>Real Steam files nest about 4 levels; this stops a crafted file from overflowing the stack.</summary>
    public const int MaxDepth = 32;

    /// <summary>Manifests are a few KB; anything far bigger isn't one.</summary>
    public const long MaxFileBytes = 4 * 1024 * 1024;

    public static VdfObject ParseFile(string path)
    {
        if (new FileInfo(path).Length > MaxFileBytes) throw new FormatException("VDF file too large.");
        return Parse(File.ReadAllText(path));
    }

    public static VdfObject Parse(string text)
    {
        int pos = 0;
        return ParseObject(text, ref pos, depth: 0);
    }

    private static VdfObject ParseObject(string s, ref int pos, int depth)
    {
        if (depth > MaxDepth) throw new FormatException("VDF nested too deeply.");
        bool topLevel = depth == 0;
        var obj = new VdfObject();
        while (true)
        {
            SkipWhitespaceAndComments(s, ref pos);
            if (pos >= s.Length)
            {
                if (topLevel) return obj;
                throw new FormatException("Unexpected end of VDF.");
            }
            if (s[pos] == '}')
            {
                pos++;
                return obj;
            }

            var key = ReadToken(s, ref pos);
            SkipWhitespaceAndComments(s, ref pos);
            if (pos >= s.Length) throw new FormatException("Missing value in VDF.");

            if (s[pos] == '{')
            {
                pos++;
                obj[key] = ParseObject(s, ref pos, depth + 1);
            }
            else
            {
                obj[key] = ReadToken(s, ref pos);
            }
        }
    }

    private static string ReadToken(string s, ref int pos)
    {
        if (s[pos] != '"')
        {
            // Unquoted token: runs until whitespace or a brace.
            int start = pos;
            while (pos < s.Length && !char.IsWhiteSpace(s[pos]) && s[pos] is not ('{' or '}' or '"')) pos++;
            return s[start..pos];
        }

        pos++;
        StringBuilder? sb = null;
        int runStart = pos;
        while (pos < s.Length && s[pos] != '"')
        {
            if (s[pos] == '\\' && pos + 1 < s.Length)
            {
                sb ??= new StringBuilder();
                sb.Append(s, runStart, pos - runStart);
                char c = s[pos + 1];
                sb.Append(c switch { 'n' => '\n', 't' => '\t', _ => c });
                pos += 2;
                runStart = pos;
            }
            else pos++;
        }
        string result = sb is null ? s[runStart..pos] : sb.Append(s, runStart, pos - runStart).ToString();
        pos++; // closing quote
        return result;
    }

    private static void SkipWhitespaceAndComments(string s, ref int pos)
    {
        while (pos < s.Length)
        {
            if (char.IsWhiteSpace(s[pos])) pos++;
            else if (s[pos] == '/' && pos + 1 < s.Length && s[pos + 1] == '/')
            {
                while (pos < s.Length && s[pos] != '\n') pos++;
            }
            else return;
        }
    }
}
