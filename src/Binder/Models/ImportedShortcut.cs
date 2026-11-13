namespace Binder.Models;

/// <summary>A desktop shortcut we moved (or copied) into our folder, so it can be put back.</summary>
public sealed class ImportedShortcut
{
    /// <summary>Where the file is now, inside %APPDATA%\Binder\Shortcuts.</summary>
    public string StoredPath { get; set; } = "";

    /// <summary>Where it was on the desktop.</summary>
    public string OriginalPath { get; set; } = "";

    /// <summary>True if moving wasn't allowed (shared desktop) and we only copied it; the original stayed.</summary>
    public bool Copied { get; set; }
}
