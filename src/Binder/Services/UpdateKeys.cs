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
        // YubiKey 1 (main), slot 9c, CN=Binder release key 1, certificate E5910D503B9E2D84D7232191517A143EF68AD21A
        // (added 2026-11-15 from the certificate exported in Yubico Authenticator).
        "MHYwEAYHKoZIzj0CAQYFK4EEACIDYgAEHtcHPsCNnuwJBBfQM+68/vF+ZaDMeEAOQb0wRIOODl3wv8mp7Nce767Mjy0s7/U04l94zRoMEJYGUJVXEwerPp/VzqFByWnPO7yAr1Kk2YU4rl6bFIXd4gHAnr2QpsHT",
        // YubiKey 2 (backup), slot 9c: not set up yet.
    ];
}
