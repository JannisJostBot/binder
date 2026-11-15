namespace Binder.Services;

/// <summary>
/// The public keys a release manifest must be signed with (spec §25, "The keys"): ECDSA P-384, SubjectPublicKeyInfo
/// in base64, as <c>scripts/sign-release.ps1 -ExportPublicKey</c> prints them. The main key (YubiKey 1) and the
/// backup key (YubiKey 2); either verifies a normal update, an urgent one needs both. Changed only by the owner, in
/// a reviewed change (AGENTS.md "Don't touch"). Empty until the keys are set up: then no release verifies and the
/// app never installs anything.
/// </summary>
public static class UpdateKeys
{
    public static readonly string[] All =
    [
        // YubiKey 1 (main), slot 9c, PIN and touch always, CN=Binder release key 1, certificate
        // 424ADAC3045F6870BA4F035E531A5BACE5123C89 (made 2026-11-15 with ykman; replaces that day's first key, which
        // didn't need a touch and never signed anything).
        "MHYwEAYHKoZIzj0CAQYFK4EEACIDYgAE/Oid10BJh+z4UQbDqElyf9kTCHnI2j1ftyjOeuZ6iPz5zJIttQHms3NSbmMZJq0ti3EOUjw3i5jDhDOz2zhpkgq3XDgbv+DaxjL9rEICDlFx7V6zVhSY89hwmzYpYUcQ",
        // YubiKey 2 (backup), slot 9c: not set up yet.
    ];
}
