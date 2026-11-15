using System.Security.Cryptography;

namespace Binder.Setup;

/// <summary>A file that didn't check out after writing it (its hash isn't the payload's).</summary>
internal sealed class FileCheckException(string name) : Exception(name)
{
    public string Name { get; } = name;
}

/// <summary>
/// Replaces a set of files all or nothing, as far as the file system allows (spec §25, "To decide" 3, owner
/// 2026-11-15). First every new file is written as <c>name.new</c> next to its final place (same folder, same
/// volume) with write-through and hashed again (<see cref="Stage"/>); nothing installed is touched yet, so a failure
/// there (disk full, a bad file, power loss) leaves the old version whole, and <see cref="Discard"/> removes what was
/// written. Only then <see cref="Commit"/> renames them in: each old file goes to <c>name.old</c> first (allowed even
/// for a running exe), then the new one takes its place. If a rename fails, the files already swapped are put back
/// from their <c>.old</c> copies. Leftover .old/.new files are the installer's to clean up (<c>RemoveOldFiles</c>).
/// No dependencies beyond the base library: the unit tests compile this file.
/// </summary>
internal sealed class FileSwap(Action<string> log)
{
    private sealed record Staged(string Name, string Target, string Temp);

    private readonly List<Staged> _staged = [];

    public int Count => _staged.Count;

    /// <summary>
    /// Writes <paramref name="target"/>'s new content to <c>target.new</c> and checks it against
    /// <paramref name="sha256"/> (hex; null: not checked, e.g. the setup's own copy). Throws
    /// <see cref="FileCheckException"/> for a mismatch, IO exceptions as they come.
    /// </summary>
    public void Stage(string name, string target, Action<Stream> write, string? sha256)
    {
        var temp = target + ".new";
        File.Delete(temp);
        _staged.Add(new(name, target, temp)); // listed first, so Discard also removes a half-written one
        using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough))
            write(stream);
        if (sha256 is null) return;
        using var check = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read);
        var actual = Convert.ToHexString(SHA256.HashData(check));
        if (!actual.Equals(sha256, StringComparison.OrdinalIgnoreCase)) throw new FileCheckException(name);
    }

    /// <summary>Removes every staged <c>.new</c> file: nothing was swapped.</summary>
    public void Discard()
    {
        foreach (var s in _staged) TryDelete(s.Temp);
        _staged.Clear();
    }

    /// <summary>
    /// Swaps every staged file in. On a failure, the ones already swapped get their old file back (a new file that
    /// had none is removed), the rest of the staged files are deleted, and the exception goes on to the caller.
    /// </summary>
    public void Commit()
    {
        var swapped = new List<(Staged File, bool HadOld)>();
        try
        {
            foreach (var s in _staged)
            {
                bool hadOld = File.Exists(s.Target);
                if (hadOld) File.Move(s.Target, s.Target + ".old", overwrite: true);
                try
                {
                    File.Move(s.Temp, s.Target);
                }
                catch
                {
                    if (hadOld) TryMove(s.Target + ".old", s.Target); // this one's old file back at once
                    throw;
                }
                swapped.Add((s, hadOld));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log($"swap failed after {swapped.Count} of {_staged.Count} files, putting the old ones back: {ex.Message}");
            for (int i = swapped.Count - 1; i >= 0; i--)
            {
                var (s, hadOld) = swapped[i];
                if (hadOld) TryMove(s.Target + ".old", s.Target, overwrite: true);
                else TryDelete(s.Target);
            }
            Discard();
            throw;
        }
        _staged.Clear();
    }

    private void TryMove(string from, string to, bool overwrite = false)
    {
        try { File.Move(from, to, overwrite); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log($"couldn't put back {to}: {ex.Message}"); }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
