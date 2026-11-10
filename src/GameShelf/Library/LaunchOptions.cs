using System.Text;
using GameShelf.Models;

namespace GameShelf.Library;

/// <summary>Game engines, in the order a scan prefers them. New values go at the end (stored as numbers).</summary>
public enum GameEngine
{
    Unknown, Unity, Unreal, Source, Source2, Godot,
    Frostbite, ReEngine, Anvil, Snowdrop, Dunia, CryEngine, IdTech, Glacier, Insomniac, Illusion,
    GameMaker, Love, RenPy, RpgMaker,
}

/// <summary>
/// One suggested launch option: a plain label and the real arguments. Options that share a
/// <see cref="Group"/> exclude each other (one graphics API at a time).
/// </summary>
public sealed record LaunchSuggestion(string Label, string Args, string? Group = null)
{
    public IReadOnlyList<string> Tokens { get; } = LaunchOptions.Split(Args);
}

/// <summary>Launch arguments: splitting and quoting them, engine suggestions and how they reach the game.</summary>
public static class LaunchOptions
{
    /// <summary>Longest argument string we pass on (Windows command lines top out at 32K; games need far less).</summary>
    public const int MaxLength = 1024;

    public static string EngineName(GameEngine engine) => engine switch
    {
        GameEngine.Unity => "Unity",
        GameEngine.Unreal => "Unreal Engine",
        GameEngine.Source => "Source",
        GameEngine.Source2 => "Source 2",
        GameEngine.Godot => "Godot",
        GameEngine.Frostbite => "Frostbite",
        GameEngine.ReEngine => "RE Engine",
        GameEngine.Anvil => "Anvil",
        GameEngine.Snowdrop => "Snowdrop",
        GameEngine.Dunia => "Dunia",
        GameEngine.CryEngine => "CryEngine",
        GameEngine.IdTech => "id Tech",
        GameEngine.Glacier => "Glacier",
        GameEngine.Insomniac => "Insomniac Engine",
        GameEngine.Illusion => "Illusion Engine",
        GameEngine.GameMaker => "GameMaker",
        GameEngine.Love => "LÖVE",
        GameEngine.RenPy => "Ren'Py",
        GameEngine.RpgMaker => "RPG Maker",
        _ => "",
    };

    public static IReadOnlyList<LaunchSuggestion> Suggestions(GameEngine engine) => engine switch
    {
        GameEngine.Unity =>
        [
            new(Strings.OptBorderless, "-popupwindow"),
            new(Strings.OptWindowed, "-screen-fullscreen 0"),
            new(Strings.OptDx11, "-force-d3d11", "api"),
            new(Strings.OptDx12, "-force-d3d12", "api"),
            new(Strings.OptVulkan, "-force-vulkan", "api"),
            new(Strings.OptSecondMonitor, "-monitor 2"),
        ],
        GameEngine.Unreal =>
        [
            new(Many(Strings.OptDx11), "-dx11", "api"),
            new(Many(Strings.OptDx12), "-dx12", "api"),
            new(Many(Strings.OptWindowed), "-windowed"),
            new(Many(Strings.OptSkipIntros), "-nomovie"),
        ],
        GameEngine.Source or GameEngine.Source2 =>
        [
            new(Strings.OptSkipIntro, "-novid"),
            new(Strings.OptWindowed, "-windowed"),
            new(Strings.OptBorderless, "-noborder"),
            new(Strings.OptVulkan, "-vulkan"),
        ],
        // id Tech 6–8 console variables (DOOM 2016, Eternal, The Dark Ages; the newer Wolfenstein games).
        GameEngine.IdTech =>
        [
            new(Many(Strings.OptSkipIntros), "+com_skipIntroVideo 1"),
            new(Many(Strings.OptNoKeyPress), "+com_skipKeyPressOnLoadScreens 1"),
        ],
        // HITMAN (2016), 2 and World of Assassination open a settings launcher first.
        GameEngine.Glacier =>
        [
            new(Strings.OptSkipLauncher, "-skip_launcher"),
        ],
        GameEngine.Godot =>
        [
            new(Strings.OptVulkan, "--rendering-driver vulkan", "api"),
            new(Strings.OptDx12, "--rendering-driver d3d12", "api"),
            new(Strings.OptFullscreen, "--fullscreen", "mode"),
            new(Strings.OptWindowed, "--windowed", "mode"),
        ],
        _ => [],
    };

    /// <summary>An option that isn't in every game of the engine: "Windowed (many games)".</summary>
    private static string Many(string label) => Strings.Format(Strings.OptManyGames, label);

    /// <summary>
    /// Splits an argument string the way a game's C runtime will (the CommandLineToArgvW rules): spaces separate,
    /// double quotes group, backslashes only escape before a quote.
    /// </summary>
    public static List<string> Split(string? text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        var current = new StringBuilder();
        bool inQuotes = false, any = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\\')
            {
                int slashes = 0;
                while (i < text.Length && text[i] == '\\') { slashes++; i++; }
                if (i < text.Length && text[i] == '"')
                {
                    current.Append('\\', slashes / 2);
                    if (slashes % 2 == 1) current.Append('"');
                    else i--; // the quote toggles quoting below
                }
                else
                {
                    current.Append('\\', slashes);
                    i--;
                }
                any = true;
            }
            else if (c == '"')
            {
                // "" inside quotes is a literal quote (CRT rule)
                if (inQuotes && i + 1 < text.Length && text[i + 1] == '"') { current.Append('"'); i++; }
                else inQuotes = !inQuotes;
                any = true;
            }
            else if ((c == ' ' || c == '\t') && !inQuotes)
            {
                if (any) result.Add(current.ToString());
                current.Clear();
                any = false;
            }
            else
            {
                current.Append(c);
                any = true;
            }
        }
        if (any) result.Add(current.ToString());
        return result;
    }

    /// <summary>Quotes one argument by the Windows argv rules, only when it needs it.</summary>
    public static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"']) < 0) return arg;
        var sb = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in arg)
        {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') sb.Append('\\', slashes * 2 + 1).Append('"');
            else sb.Append('\\', slashes).Append(c);
            slashes = 0;
        }
        return sb.Append('\\', slashes * 2).Append('"').ToString();
    }

    public static string Join(IEnumerable<string> args) => string.Join(' ', args.Select(Quote));

    /// <summary>Where <paramref name="sequence"/> starts in <paramref name="tokens"/> (case-insensitive), or -1.</summary>
    public static int IndexOf(IReadOnlyList<string> tokens, IReadOnlyList<string> sequence)
    {
        if (sequence.Count == 0) return -1;
        for (int i = 0; i + sequence.Count <= tokens.Count; i++)
        {
            bool match = true;
            for (int j = 0; j < sequence.Count && match; j++)
                match = string.Equals(tokens[i + j], sequence[j], StringComparison.OrdinalIgnoreCase);
            if (match) return i;
        }
        return -1;
    }

    /// <summary>The arguments with the suggestion's tokens removed (all occurrences).</summary>
    public static List<string> Without(IReadOnlyList<string> tokens, LaunchSuggestion suggestion)
    {
        var list = tokens.ToList();
        int at;
        while ((at = IndexOf(list, suggestion.Tokens)) >= 0) list.RemoveRange(at, suggestion.Tokens.Count);
        return list;
    }

    /// <summary>How the game is launched with arguments, or why it can't take any.</summary>
    public enum Route { Exe, Steam, NotSupported }

    /// <summary>
    /// The exe a game runs when started directly: the "Start with" override if it is still a valid exe inside
    /// the install folder, else its own exe. Null for games that start through a store URI or a shortcut.
    /// </summary>
    public static string? DirectExe(Game game)
    {
        if (ValidOverride(game) is { } exe) return exe;
        if (!string.IsNullOrEmpty(game.LaunchUri)) return null;
        return game.ExePath is { } path && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    public static Route RouteFor(Game game)
    {
        if (DirectExe(game) is not null) return Route.Exe;
        if (game.Source == GameSource.Steam && IsSteamAppId(game.StoreId)) return Route.Steam;
        return Route.NotSupported;
    }

    /// <summary>The "Start with" exe, only if it is an existing .exe inside the game's install folder.</summary>
    public static string? ValidOverride(Game game)
    {
        if (string.IsNullOrEmpty(game.ExeOverride) || string.IsNullOrEmpty(game.InstallDir)) return null;
        return IsInside(game.ExeOverride, game.InstallDir) &&
               game.ExeOverride.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
               File.Exists(game.ExeOverride)
            ? game.ExeOverride
            : null;
    }

    /// <summary>True when <paramref name="path"/> is a full path strictly inside <paramref name="folder"/>.</summary>
    public static bool IsInside(string path, string folder)
    {
        try
        {
            if (!Path.IsPathFullyQualified(path) || !Path.IsPathFullyQualified(folder)) return false;
            var full = Path.GetFullPath(path);
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && full.Length > root.Length;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>
    /// Steam's "run with arguments" link: steam://run/{appid}//{args}/. The appid must be digits; the arguments
    /// are percent-encoded so nothing in them can change the link itself.
    /// </summary>
    public static string? SteamRunUri(string? appId, IReadOnlyList<string> args)
    {
        if (!IsSteamAppId(appId)) return null;
        return $"steam://run/{appId}//{Uri.EscapeDataString(Join(args))}/";
    }

    /// <summary>
    /// The Steam link a Steam game starts with: the "run with arguments" link when it has arguments, else
    /// steam://rungameid/{appid}. Built from the validated appid only, never from a stored link.
    /// </summary>
    public static string? SteamLink(string? appId, IReadOnlyList<string> args)
    {
        if (!IsSteamAppId(appId)) return null;
        return args.Count > 0 ? SteamRunUri(appId, args) : "steam://rungameid/" + appId;
    }

    private static bool IsSteamAppId(string? id) => !string.IsNullOrEmpty(id) && id.Length <= 10 && id.All(char.IsAsciiDigit);
}
