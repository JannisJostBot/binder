namespace Binder.Services;

/// <summary>
/// Last-resort error log at %LOCALAPPDATA%\Binder\error.log, so unexpected errors can be looked at instead
/// of the widget just vanishing. Kept small: rotated to error.old.log past 1 MB.
/// </summary>
public static class ErrorLog
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Sync = new();

    public static string FilePath => Path.Combine(Storage.CacheDir, "error.log");

    public static void Write(Exception? error, string where)
    {
        if (error is null) return;
        Append($"[{where}] {error}");
    }

    /// <summary>Something that went wrong and was handled (a damaged file replaced by its backup, …), for a later look.</summary>
    public static void Note(string text) => Append("[note] " + text);

    private static void Append(string text)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Storage.CacheDir);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                    File.Move(FilePath, Path.Combine(Storage.CacheDir, "error.old.log"), overwrite: true);
                File.AppendAllText(FilePath, $"{DateTimeOffset.Now:O} {text}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Nowhere left to report it.
        }
    }
}
