using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Binder.Library;

/// <summary>What the app makes of a verified release compared with what's installed (spec §25).</summary>
public enum UpdateVerdict
{
    /// <summary>A newer version: offer it (or install it, per the user's choice).</summary>
    Newer,
    /// <summary>The same or an older version than the installed one: never installed (no downgrades).</summary>
    NotNewer,
    /// <summary>Issued before the last manifest we accepted: an old release served again.</summary>
    Stale,
    /// <summary>Issued more than a day in the future: a wrong clock or a crafted file.</summary>
    FromFuture,
    /// <summary>The version the user chose to skip (only for a normal update).</summary>
    Skipped,
}

/// <summary>A manifest whose signature checked out, and whether it counts as urgent (signed by two keys).</summary>
public sealed record VerifiedRelease(UpdateManifest Manifest, bool Urgent);

/// <summary>
/// A release's <c>release.json</c> (spec §25, "The signed manifest"): the signature is checked on the raw bytes
/// first (<see cref="Verify"/>), only then is the JSON read, strictly (<see cref="Parse"/>). Pure logic, unit-tested;
/// the network, files and the setup's start are <c>Services/Updater</c>'s.
/// </summary>
public sealed record UpdateManifest(
    Version Version,
    DateTimeOffset Issued,
    string Commit,
    long SetupSize,
    string SetupSha256,
    bool Urgent,
    int? HelperVersion,
    IReadOnlyDictionary<string, string[]> Notes)
{
    public const string Product = "Binder";
    public const string Channel = "stable";
    /// <summary>The GitHub repository the releases come from (owner/name), set at build time; empty: no updates.</summary>
    public const string Repository = Services.UpdateKeys.Repository;

    /// <summary>Caps on bytes actually read, before anything is parsed.</summary>
    public const int MaxManifestBytes = 64 * 1024;
    public const int MaxSignatureBytes = 1024;

    public const long MinSetupBytes = 1L << 20;          // 1 MB
    public const long MaxSetupBytes = 200L << 20;        // 200 MB
    public const int MaxNoteLines = 6;
    public const int MaxNoteLength = 200;
    /// <summary>One signature per YubiKey: at most two are meaningful; a few spare lines for key rotation.</summary>
    private const int MaxSignatureLines = 4;
    /// <summary>ECDSA P-384 in IEEE P1363 form: r and s, 48 bytes each.</summary>
    private const int SignatureBytes = 96;
    private static readonly TimeSpan FutureSlack = TimeSpan.FromDays(1);
    private static readonly string[] Languages = ["en", "de", "fr"];

    /// <summary>
    /// What the key really signs: this fixed text and a zero byte, then the manifest's exact bytes, so a signature
    /// made for anything else can't verify as a manifest.
    /// </summary>
    public static ReadOnlySpan<byte> SignaturePrefix => "Binder release manifest v1\0"u8;

    public static readonly Uri ManifestUri = new($"https://github.com/{Repository}/releases/latest/download/release.json");
    public static readonly Uri SignatureUri = new($"https://github.com/{Repository}/releases/latest/download/release.json.sig");

    /// <summary>The setup's address, built from the version alone (it's never read from the manifest).</summary>
    public static Uri SetupUri(Version version) =>
        new($"https://github.com/{Repository}/releases/download/v{Text(version)}/BinderSetup-{Text(version)}.exe");

    /// <summary>The setup's file name in the updates folder while it downloads, then after the hash check.</summary>
    public static string SetupFileName(Version version) => $"BinderSetup-{Text(version)}.exe";

    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com",
    };

    /// <summary>Where a request (or a redirect) may go: HTTPS on the default port to GitHub's release hosts only.</summary>
    public static bool IsAllowedAddress(Uri uri) =>
        uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
        AllowedHosts.Contains(uri.IdnHost);

    /// <summary>"0.2.0": three whole numbers.</summary>
    public static string Text(Version v) => $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";

    /// <summary>
    /// Checks <paramref name="signatureFile"/> (one base64 signature per line) against the compiled-in
    /// <paramref name="publicKeys"/> (SubjectPublicKeyInfo, base64, P-384 only), then parses. Null when no key
    /// verifies or either file is over its cap; <see cref="FormatException"/> when the signature file or (after a
    /// valid signature) the manifest is malformed. Urgent counts only with valid signatures from two different keys.
    /// </summary>
    public static VerifiedRelease? Verify(byte[] manifest, byte[] signatureFile, IReadOnlyList<string> publicKeys)
    {
        if (manifest.Length > MaxManifestBytes || signatureFile.Length > MaxSignatureBytes) return null;
        var signers = CountSigners(manifest, signatureFile, publicKeys);
        if (signers == 0) return null;
        var parsed = Parse(manifest);
        return new VerifiedRelease(parsed, parsed.Urgent && signers >= 2);
    }

    /// <summary>How many different keys of <paramref name="publicKeys"/> signed <paramref name="manifest"/>.</summary>
    public static int CountSigners(ReadOnlySpan<byte> manifest, ReadOnlySpan<byte> signatureFile, IReadOnlyList<string> publicKeys)
    {
        var signatures = ReadSignatures(signatureFile);
        var signed = new byte[SignaturePrefix.Length + manifest.Length];
        SignaturePrefix.CopyTo(signed);
        manifest.CopyTo(signed.AsSpan(SignaturePrefix.Length));

        var count = 0;
        foreach (var key in publicKeys)
        {
            using var ecdsa = ImportKey(key);
            foreach (var signature in signatures)
            {
                if (!ecdsa.VerifyData(signed, signature, HashAlgorithmName.SHA384, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                    continue;
                count++;
                break;
            }
        }
        return count;
    }

    private static List<byte[]> ReadSignatures(ReadOnlySpan<byte> file)
    {
        if (file.Length > MaxSignatureBytes) throw new FormatException("Signature file too large.");
        string text;
        try { text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(file); }
        catch (ArgumentException) { throw new FormatException("Signature file isn't text."); }

        var signatures = new List<byte[]>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            if (signatures.Count == MaxSignatureLines) throw new FormatException("Too many signatures.");
            var bytes = new byte[SignatureBytes];
            if (!Convert.TryFromBase64String(line, bytes, out var written) || written != SignatureBytes)
                throw new FormatException("A signature line isn't a 96-byte base64 signature.");
            signatures.Add(bytes);
        }
        if (signatures.Count == 0) throw new FormatException("No signature.");
        return signatures;
    }

    private static ECDsa ImportKey(string spkiBase64)
    {
        var ecdsa = ECDsa.Create();
        try
        {
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(spkiBase64), out _);
            if (ecdsa.KeySize != 384) throw new ArgumentException("Update keys must be ECDSA P-384.");
            return ecdsa;
        }
        catch
        {
            ecdsa.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Reads a manifest whose signature already checked out. Strict: every rule in spec §25 or
    /// <see cref="FormatException"/>; unknown fields are ignored, repeated ones rejected.
    /// </summary>
    public static UpdateManifest Parse(byte[] bytes)
    {
        if (bytes.Length > MaxManifestBytes) throw new FormatException("Manifest too large.");
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                MaxDepth = 4, CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false,
                AllowDuplicateProperties = false,
            });
        }
        catch (JsonException e) { throw new FormatException("Manifest isn't valid JSON.", e); }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new FormatException("Manifest isn't an object.");

            if (!Get(root, "format", JsonValueKind.Number).TryGetInt32(out var format) || format != 1)
                throw new FormatException("Unknown manifest format.");
            if (Get(root, "product", JsonValueKind.String).GetString() != Product) throw new FormatException("Wrong product.");
            if (Get(root, "channel", JsonValueKind.String).GetString() != Channel) throw new FormatException("Wrong channel.");

            var version = ParseVersion(Get(root, "version", JsonValueKind.String).GetString()!)
                ?? throw new FormatException("Version isn't three whole numbers.");

            if (!DateTimeOffset.TryParseExact(Get(root, "issued", JsonValueKind.String).GetString(), "yyyy-MM-dd'T'HH:mm:ss'Z'",
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var issued))
                throw new FormatException("Issued isn't a UTC time.");

            var commit = Get(root, "commit", JsonValueKind.String).GetString()!;
            if (!IsLowerHex(commit, 40)) throw new FormatException("Commit isn't 40 hex digits.");

            var setup = Get(root, "setup", JsonValueKind.Object);
            if (!Get(setup, "size", JsonValueKind.Number).TryGetInt64(out var size) || size < MinSetupBytes || size > MaxSetupBytes)
                throw new FormatException("Setup size out of range.");
            var sha = Get(setup, "sha256", JsonValueKind.String).GetString()!;
            if (!IsLowerHex(sha, 64)) throw new FormatException("Setup hash isn't 64 hex digits.");

            var urgentElement = Get(root, "urgent", null);
            if (urgentElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new FormatException("Urgent isn't true or false.");

            int? helper = null;
            if (root.TryGetProperty("helperVersion", out var h))
            {
                if (h.ValueKind != JsonValueKind.Number || !h.TryGetInt32(out var hv) || hv < 1 || hv > 1000)
                    throw new FormatException("Helper version isn't a small whole number.");
                helper = hv;
            }

            return new UpdateManifest(version, issued, commit, size, sha, urgentElement.ValueKind == JsonValueKind.True, helper,
                ParseNotes(Get(root, "notes", JsonValueKind.Object)));
        }
    }

    private static Dictionary<string, string[]> ParseNotes(JsonElement notes)
    {
        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var language in Languages)
        {
            if (!notes.TryGetProperty(language, out var lines))
            {
                if (language == "en") throw new FormatException("Notes need English.");
                continue;
            }
            if (lines.ValueKind != JsonValueKind.Array || lines.GetArrayLength() > MaxNoteLines)
                throw new FormatException("Notes are a list of at most six lines.");
            var list = new string[lines.GetArrayLength()];
            var i = 0;
            foreach (var line in lines.EnumerateArray())
            {
                if (line.ValueKind != JsonValueKind.String || line.GetString() is not { } text || !IsPlainLine(text))
                    throw new FormatException("A note line isn't plain text of 1 to 200 characters.");
                list[i++] = text;
            }
            result[language] = list;
        }
        return result;
    }

    /// <summary>One line of plain text: no control characters (line breaks included) and no direction overrides.</summary>
    private static bool IsPlainLine(string text)
    {
        if (text.Length is 0 or > MaxNoteLength || text.Trim().Length == 0) return false;
        foreach (var c in text)
            if (char.IsControl(c) || c is >= '‪' and <= '‮' or >= '⁦' and <= '⁩' or '‎' or '‏')
                return false;
        return true;
    }

    private static JsonElement Get(JsonElement obj, string name, JsonValueKind? kind)
    {
        if (!obj.TryGetProperty(name, out var value) || (kind is { } k && value.ValueKind != k))
            throw new FormatException($"Manifest field {name} missing or of the wrong kind.");
        return value;
    }

    /// <summary>"0.2.0": three whole numbers of at most five digits, no signs, spaces or leading zeros.</summary>
    public static Version? ParseVersion(string text)
    {
        var parts = text.Split('.');
        if (parts.Length != 3) return null;
        var numbers = new int[3];
        for (var i = 0; i < 3; i++)
        {
            var p = parts[i];
            if (p.Length is 0 or > 5 || (p.Length > 1 && p[0] == '0')) return null;
            foreach (var c in p) if (c is < '0' or > '9') return null;
            numbers[i] = int.Parse(p, CultureInfo.InvariantCulture);
        }
        return new Version(numbers[0], numbers[1], numbers[2]);
    }

    private static bool IsLowerHex(string s, int length)
    {
        if (s.Length != length) return false;
        foreach (var c in s) if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f'))) return false;
        return true;
    }

    /// <summary>
    /// Whether a verified release is for us: never at or below <paramref name="installed"/>, never issued before
    /// <paramref name="lastIssued"/> (the highest accepted so far) or over a day after <paramref name="now"/>.
    /// <paramref name="skipped"/> holds back only a normal update; an urgent one comes back at the next check.
    /// </summary>
    public static UpdateVerdict Judge(VerifiedRelease release, Version installed, DateTimeOffset? lastIssued,
        DateTimeOffset now, Version? skipped)
    {
        var m = release.Manifest;
        if (m.Issued > now + FutureSlack) return UpdateVerdict.FromFuture;
        if (lastIssued is { } last && m.Issued < last) return UpdateVerdict.Stale;
        if (m.Version <= Normalize(installed)) return UpdateVerdict.NotNewer;
        if (!release.Urgent && skipped is { } s && m.Version == Normalize(s)) return UpdateVerdict.Skipped;
        return UpdateVerdict.Newer;
    }

    /// <summary>The assembly's "0.1.4.0" compares as "0.1.4".</summary>
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    /// <summary>The notes in the UI's language (two letters), else English.</summary>
    public string[] NotesFor(string language) => Notes.TryGetValue(language, out var lines) ? lines : Notes["en"];
}

/// <summary>
/// Checks the setup while it downloads (spec §25, "Download"): stops as soon as more arrives than the manifest's
/// size, and at the end compares the length and SHA-256. Feed it every chunk as it's written to the part file.
/// </summary>
public sealed class SetupDownloadCheck(long expectedSize, string expectedSha256) : IDisposable
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private long _received;

    public long Received => _received;

    /// <summary>False once the download is longer than expected: stop and delete the part file.</summary>
    public bool Append(ReadOnlySpan<byte> chunk)
    {
        _received += chunk.Length;
        if (_received > expectedSize) return false;
        _hash.AppendData(chunk);
        return true;
    }

    /// <summary>True only for exactly the expected size and hash. Call once, at the end.</summary>
    public bool Matches()
    {
        if (_received != expectedSize) return false;
        var actual = Convert.ToHexStringLower(_hash.GetHashAndReset());
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(actual), Encoding.ASCII.GetBytes(expectedSha256));
    }

    public void Dispose() => _hash.Dispose();
}
