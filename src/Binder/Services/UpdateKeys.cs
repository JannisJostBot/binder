namespace Binder.Services;

/// <summary>
/// Where updates come from (spec §25, "The keys"): <c>Repository</c>, the GitHub repository whose releases are
/// checked (owner/name), and <c>All</c>, the public keys a release manifest must be signed with (ECDSA P-384,
/// SubjectPublicKeyInfo in base64). Both are compiled in from the build's <c>BinderRepository</c> and
/// <c>BinderUpdateKeys</c> (<c>build/Updates.targets</c> writes them to <c>UpdateConfig.g.cs</c>); the official
/// releases' values are in <c>build/OfficialRelease.props</c>, changed only by the owner (AGENTS.md "Don't touch").
/// Never read at run time from anything the user's side can write. A build without keys (a local build, a fork
/// that set none) never checks for updates, and with no keys no release would verify anyway.
/// </summary>
public static partial class UpdateKeys
{
    /// <summary>Whether this build checks for updates: it names a repository and at least one key.</summary>
    public static bool Enabled => IsEnabled(Repository, All);

    public static bool IsEnabled(string repository, string[] keys) => repository.Length > 0 && keys.Length > 0;

    /// <summary>Whether this build names a repository (Settings › About's GitHub link).</summary>
    public static bool HasRepository => Repository.Length > 0;
}
