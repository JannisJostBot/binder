using System.Security.Cryptography;
using System.Text;
using Binder.Library;

namespace Binder.Tests;

static partial class Tests
{
    /// <summary>
    /// Updates from GitHub (spec §25): the manifest's signature (good, changed byte, wrong key, backup key, urgent
    /// needs both keys), its strict rules, the downgrade and issued checks, the addresses and the download's size and
    /// hash check.
    /// </summary>
    static void UpdateTests(string root)
    {
        using var main = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        using var backup = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        string[] keys = [Pub(main), Pub(backup)];

        // The compiled-in keys: this test project builds as an official release (Binder.Tests.csproj), so these are
        // build/OfficialRelease.props's, the owner's YubiKeys: each one a P-384 public key, all different, nothing else.
        var compiled = Services.UpdateKeys.All;
        bool allP384 = compiled.All(k =>
        {
            try
            {
                using var e = ECDsa.Create();
                e.ImportSubjectPublicKeyInfo(Convert.FromBase64String(k), out var read);
                return e.KeySize == 384 && read == Convert.FromBase64String(k).Length;
            }
            catch (Exception ex) when (ex is FormatException or CryptographicException) { return false; }
        });
        Check(compiled.Length is >= 1 and <= 2 && allP384 && compiled.Distinct().Count() == compiled.Length,
            "update keys: one or two different ECDSA P-384 public keys compiled in");
        Check(Services.UpdateKeys.Enabled && UpdateManifest.Repository == "JannisJostBot/binder",
            "update keys: an official build checks the official repository");
        Check(!Services.UpdateKeys.IsEnabled("", []) && !Services.UpdateKeys.IsEnabled("someone/fork", []) &&
              !Services.UpdateKeys.IsEnabled("", [Pub(main)]) && Services.UpdateKeys.IsEnabled("someone/fork", [Pub(main)]),
            "update keys: a build checks only with a repository and a key");

        var commit = new string('a', 40);
        var sha = new string('0', 64);
        string Json(string version = "0.2.0", string issued = "2026-11-20T18:04:00Z", string urgent = "false",
            string notes = """{ "en": ["Fixes a crash."], "de": ["Behebt einen Absturz."], "fr": ["Corrige un plantage."] }""",
            string extra = "", string size = "31457280", string product = "Binder", string channel = "stable",
            string format = "1", string commitValue = "", string shaValue = "") =>
            $$"""
            {
              "format": {{format}},
              "product": "{{product}}",
              "channel": "{{channel}}",
              "version": "{{version}}",
              "issued": "{{issued}}",
              "commit": "{{(commitValue == "" ? commit : commitValue)}}",
              "setup": { "size": {{size}}, "sha256": "{{(shaValue == "" ? sha : shaValue)}}" },
              "urgent": {{urgent}},{{extra}}
              "notes": {{notes}}
            }
            """;
        byte[] B(string s) => Encoding.UTF8.GetBytes(s);
        byte[] Sig(byte[] manifest, params ECDsa[] by) =>
            B(string.Join("\n", by.Select(k => Convert.ToBase64String(Sign(k, manifest)))) + "\n");

        // Signatures.
        var good = B(Json());
        var release = UpdateManifest.Verify(good, Sig(good, main), keys);
        Check(release is { Urgent: false } && release.Manifest.Version == new Version(0, 2, 0) &&
              release.Manifest.SetupSize == 31457280 && release.Manifest.Commit == commit &&
              release.Manifest.Issued == new DateTimeOffset(2026, 11, 20, 18, 4, 0, TimeSpan.Zero),
            "update: a manifest signed with the main key verifies and parses");
        Check(UpdateManifest.Verify(good, Sig(good, backup), keys) is not null, "update: the backup key verifies too");
        Check(UpdateManifest.Verify(good, Sig(good, stranger), keys) is null, "update: another key's signature is refused");
        Check(UpdateManifest.Verify(good, Sig(good, main), []) is null, "update: no compiled-in keys, nothing verifies");
        var changed = (byte[])good.Clone();
        changed[good.Length / 2] ^= 0x01;
        Check(UpdateManifest.Verify(changed, Sig(good, main), keys) is null, "update: one changed byte fails the signature");
        var noPrefix = Convert.ToBase64String(main.SignData(good, HashAlgorithmName.SHA384, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        Check(UpdateManifest.Verify(good, B(noPrefix), keys) is null, "update: a signature without the fixed prefix is refused");
        var windowsLines = B(Convert.ToBase64String(Sign(main, good)) + "\r\n");
        Check(UpdateManifest.Verify(good, windowsLines, keys) is not null, "update: a signature file with CRLF reads");
        Throws(() => UpdateManifest.Verify(good, B("not base64!"), keys), "update: a malformed signature line is refused");
        Throws(() => UpdateManifest.Verify(good, B(""), keys), "update: an empty signature file is refused");
        Throws(() => UpdateManifest.Verify(good, B(Convert.ToBase64String(new byte[64])), keys), "update: a signature of the wrong length is refused");
        Check(UpdateManifest.Verify(good, new byte[UpdateManifest.MaxSignatureBytes + 1], keys) is null &&
              UpdateManifest.Verify(new byte[UpdateManifest.MaxManifestBytes + 1], Sig(good, main), keys) is null,
            "update: oversized files are refused before anything is read");
        using (var p256 = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            Throws<ArgumentException>(() => UpdateManifest.CountSigners(good, Sig(good, main), [Pub(p256)]), "update: only P-384 keys are accepted");
        // A signed but malformed manifest: the signature is checked first, then the strict parse refuses.
        var signedJunk = B("{\"format\": 2}");
        Throws(() => UpdateManifest.Verify(signedJunk, Sig(signedJunk, main), keys), "update: a signed manifest still has to pass the rules");

        // Urgent: only with both keys.
        var urgent = B(Json(urgent: "true"));
        Check(UpdateManifest.Verify(urgent, Sig(urgent, main, backup), keys) is { Urgent: true }, "update: urgent with both keys");
        Check(UpdateManifest.Verify(urgent, Sig(urgent, main), keys) is { Urgent: false }, "update: urgent with one key is a normal update");
        Check(UpdateManifest.Verify(urgent, Sig(urgent, main, main), keys) is { Urgent: false }, "update: the same key twice counts once");
        Check(UpdateManifest.Verify(urgent, Sig(urgent, main, stranger), keys) is { Urgent: false }, "update: a stranger's second signature doesn't count");

        // The rules.
        void Rejected(string json, string what) => Throws(() => UpdateManifest.Parse(B(json)), "update: rejected, " + what);
        Rejected(Json(format: "2"), "format 2");
        Rejected(Json(product: "Other"), "another product");
        Rejected(Json(channel: "beta"), "another channel");
        foreach (var v in new[] { "0.2", "0.2.0.1", "v0.2.0", "0.02.0", "-1.0.0", "0.2.x", "0.2. 0", "123456.0.0", "" })
            Rejected(Json(version: v), $"version \"{v}\"");
        foreach (var t in new[] { "2026-11-20", "2026-11-20T18:04:00+01:00", "2026-11-20 18:04:00Z", "yesterday" })
            Rejected(Json(issued: t), $"issued \"{t}\"");
        Rejected(Json(commitValue: new string('a', 39)), "a short commit");
        Rejected(Json(commitValue: new string('A', 40)), "an upper-case commit");
        Rejected(Json(shaValue: new string('g', 64)), "a hash with non-hex digits");
        Rejected(Json(size: "1000"), "a setup under 1 MB");
        Rejected(Json(size: "300000000"), "a setup over 200 MB");
        Rejected(Json(size: "31457280.5"), "a fractional size");
        Rejected(Json(urgent: "\"yes\""), "urgent as text");
        Rejected(Json(notes: """{ "de": ["Nur Deutsch."] }"""), "notes without English");
        Rejected(Json(notes: """{ "en": ["1", "2", "3", "4", "5", "6", "7"] }"""), "seven note lines");
        Rejected(Json(notes: $$"""{ "en": ["{{new string('x', 201)}}"] }"""), "a note line over 200 characters");
        Rejected(Json(notes: """{ "en": ["Two\nlines"] }"""), "a line break in a note");
        Rejected(Json(notes: """{ "en": ["evil \u202E txt.exe"] }"""), "a direction override in a note");
        Rejected(Json(notes: """{ "en": [""] }"""), "an empty note line");
        Rejected(Json(notes: """{ "en": [1] }"""), "a note that isn't text");
        Rejected(Json(extra: "\n  \"helperVersion\": 0,"), "helper version 0");
        Rejected(Json(extra: "\n  \"version\": \"9.9.9\","), "a repeated field");
        Rejected(Json().Replace("\"notes\"", "// comment\n  \"notes\""), "a comment");
        Rejected("[1]", "an array");
        Rejected("{" + new string('[', 10) + new string(']', 10) + "}", "deep nesting");
        Check(UpdateManifest.Parse(B(Json(extra: "\n  \"helperVersion\": 3,\n  \"later\": { \"x\": 1 },"))) is { HelperVersion: 3 },
            "update: helper version read, unknown fields ignored");
        var notes = UpdateManifest.Parse(B(Json(notes: """{ "en": ["Faster start."], "it": ["Ignorato."] }""")));
        Check(notes.NotesFor("de") is ["Faster start."] && notes.NotesFor("en") is ["Faster start."] && !notes.Notes.ContainsKey("it"),
            "update: notes fall back to English, unknown languages ignored");
        Check(UpdateManifest.Parse(B(Json())).NotesFor("fr") is ["Corrige un plantage."], "update: notes in the UI's language");

        // Downgrades, replays and the clock.
        var now = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
        var normal = new VerifiedRelease(UpdateManifest.Parse(good), false);
        var installed = new Version(0, 1, 4, 0);
        Check(UpdateManifest.Judge(normal, installed, null, now, null) == UpdateVerdict.Newer, "update: a newer version is offered");
        Check(UpdateManifest.Judge(normal, new Version(0, 2, 0, 0), null, now, null) == UpdateVerdict.NotNewer &&
              UpdateManifest.Judge(normal, new Version(0, 3, 0), null, now, null) == UpdateVerdict.NotNewer,
            "update: never the installed version or an older one");
        Check(UpdateManifest.Judge(normal, installed, normal.Manifest.Issued.AddSeconds(1), now, null) == UpdateVerdict.Stale,
            "update: a manifest issued before the last accepted one is refused");
        Check(UpdateManifest.Judge(normal, installed, normal.Manifest.Issued, now, null) == UpdateVerdict.Newer,
            "update: the same manifest read back again still counts");
        Check(UpdateManifest.Judge(normal, installed, null, normal.Manifest.Issued.AddDays(-2), null) == UpdateVerdict.FromFuture &&
              UpdateManifest.Judge(normal, installed, null, normal.Manifest.Issued.AddHours(-23), null) == UpdateVerdict.Newer,
            "update: issued more than a day in the future is refused");
        Check(UpdateManifest.Judge(normal, installed, null, now, new Version(0, 2, 0)) == UpdateVerdict.Skipped &&
              UpdateManifest.Judge(normal with { Urgent = true }, installed, null, now, new Version(0, 2, 0)) == UpdateVerdict.Newer &&
              UpdateManifest.Judge(normal, installed, null, now, new Version(0, 1, 9)) == UpdateVerdict.Newer,
            "update: a skipped version is held back unless urgent");

        // Addresses.
        Check(UpdateManifest.SetupUri(new Version(0, 2, 0)).AbsoluteUri ==
              "https://github.com/JannisJostBot/binder/releases/download/v0.2.0/BinderSetup-0.2.0.exe",
            "update: the setup's address is built from the version");
        Check(UpdateManifest.IsAllowedAddress(UpdateManifest.ManifestUri) &&
              UpdateManifest.IsAllowedAddress(new Uri("https://release-assets.githubusercontent.com/x?sig=1")) &&
              UpdateManifest.IsAllowedAddress(new Uri("https://objects.githubusercontent.com/x")),
            "update: GitHub's release hosts are allowed");
        foreach (var bad in new[] { "http://github.com/x", "https://github.com:8443/x", "https://evil.com/x",
                     "https://github.com.evil.com/x", "https://user@github.com/x", "file:///C:/x" })
            Check(!UpdateManifest.IsAllowedAddress(new Uri(bad)), $"update: address refused {bad}");

        // The download's check, against a local file.
        var file = Path.Combine(root, "setup.bin");
        var payload = RandomNumberGenerator.GetBytes(3 * 1024 * 1024 + 17);
        File.WriteAllBytes(file, payload);
        var hash = Convert.ToHexStringLower(SHA256.HashData(payload));
        bool Feed(SetupDownloadCheck c)
        {
            using var s = File.OpenRead(file);
            var buffer = new byte[81920];
            int n;
            while ((n = s.Read(buffer)) > 0)
                if (!c.Append(buffer.AsSpan(0, n))) return false;
            return true;
        }
        using (var c = new SetupDownloadCheck(payload.Length, hash))
            Check(Feed(c) && c.Matches(), "update: the download matches its size and hash");
        using (var c = new SetupDownloadCheck(payload.Length - 1, hash))
            Check(!Feed(c), "update: the download stops once it's longer than the manifest says");
        using (var c = new SetupDownloadCheck(payload.Length + 1, hash))
            Check(Feed(c) && !c.Matches(), "update: a short download doesn't match");
        using (var c = new SetupDownloadCheck(payload.Length, new string('0', 64)))
            Check(Feed(c) && !c.Matches(), "update: a wrong hash doesn't match");
        File.Delete(file);
    }

    static string Pub(ECDsa key) => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    /// <summary>What scripts/sign-release.ps1 does: the fixed prefix, then the manifest's bytes.</summary>
    static byte[] Sign(ECDsa key, byte[] manifest) =>
        key.SignData([.. UpdateManifest.SignaturePrefix, .. manifest], HashAlgorithmName.SHA384, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    static void Throws(Action a, string what) => Throws<FormatException>(a, what);

    static void Throws<T>(Action a, string what) where T : Exception
    {
        try { a(); Check(false, what); }
        catch (T) { Check(true, what); }
        catch (Exception e) { Check(false, $"{what} (threw {e.GetType().Name})"); }
    }
}
