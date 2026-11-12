namespace GameShelf.Library;

public enum AntiCheatKind { EasyAntiCheat, BattlEye, Vanguard, Javelin, DenuvoAntiCheat, GameGuard, Xigncode, Ace, HoYoProtect }

/// <summary>A PC setting an anti-cheat may need (checked without admin rights, never changed).</summary>
public enum PcRequirement { SecureBoot, Tpm20, NoTestSigning, NoKernelDebugging, VanguardLoaded }

/// <summary>
/// What Binder knows about anti-cheats: names, what each needs from the PC, and programs and drivers that
/// are known to conflict. A built-in table, updated with app releases; games can change their requirements.
/// </summary>
public static class AntiCheat
{
    public static string Name(AntiCheatKind kind) => kind switch
    {
        AntiCheatKind.EasyAntiCheat => "Easy Anti-Cheat",
        AntiCheatKind.BattlEye => "BattlEye",
        AntiCheatKind.Vanguard => "Riot Vanguard",
        AntiCheatKind.Javelin => "EA Javelin",
        AntiCheatKind.DenuvoAntiCheat => "Denuvo Anti-Cheat",
        AntiCheatKind.GameGuard => "nProtect GameGuard",
        AntiCheatKind.Xigncode => "XIGNCODE3",
        AntiCheatKind.Ace => "ACE",
        AntiCheatKind.HoYoProtect => "HoYoProtect",
        _ => kind.ToString(),
    };

    /// <summary>
    /// The baseline for each anti-cheat plus exceptions for specific games. Kernel anti-cheats refuse to run with
    /// test signing or kernel debugging on.
    /// </summary>
    public static IReadOnlyList<PcRequirement> Requirements(IReadOnlyList<AntiCheatKind> kinds, string gameName, bool windows11)
    {
        var set = new List<PcRequirement>();
        void Add(PcRequirement r) { if (!set.Contains(r)) set.Add(r); }
        foreach (var kind in kinds)
        {
            if (kind == AntiCheatKind.Vanguard)
            {
                if (windows11) { Add(PcRequirement.Tpm20); Add(PcRequirement.SecureBoot); }
                Add(PcRequirement.VanguardLoaded);
            }
            Add(PcRequirement.NoTestSigning);
            Add(PcRequirement.NoKernelDebugging);
        }
        // Games that need more than their anti-cheat's baseline.
        if (kinds.Count > 0 && GameExceptions.Any(g => gameName.Contains(g, StringComparison.OrdinalIgnoreCase)))
            Add(PcRequirement.SecureBoot);
        return set;
    }

    /// <summary>Games known to require Secure Boot on top of their anti-cheat's baseline.</summary>
    private static readonly string[] GameExceptions = ["Battlefield 6"];

    public static string RequirementName(PcRequirement r) => r switch
    {
        PcRequirement.SecureBoot => "Secure Boot",
        PcRequirement.Tpm20 => "TPM 2.0",
        PcRequirement.NoTestSigning => Strings.TestSigningOff,
        PcRequirement.NoKernelDebugging => Strings.KernelDebuggingOff,
        PcRequirement.VanguardLoaded => Strings.VanguardRunning,
        _ => r.ToString(),
    };

    /// <summary>Microsoft's help page for a setting that is off, or null.</summary>
    public static string? HelpUrl(PcRequirement r) => r switch
    {
        PcRequirement.SecureBoot => "https://support.microsoft.com/windows/windows-11-and-secure-boot-a8ff1202-c0d9-42f5-940f-843abef64fad",
        PcRequirement.Tpm20 => "https://support.microsoft.com/windows/enable-tpm-2-0-on-your-pc-1fd5a332-360d-4f46-a1e7-ae6b0c90645c",
        PcRequirement.NoTestSigning => "https://learn.microsoft.com/windows-hardware/drivers/install/the-testsigning-boot-configuration-option",
        PcRequirement.NoKernelDebugging => "https://learn.microsoft.com/windows-hardware/drivers/devtest/bcdedit--debug",
        _ => null,
    };

    /// <summary>Programs anti-cheats commonly refuse to run next to: process name (no .exe, lower case) → display name.</summary>
    public static readonly IReadOnlyDictionary<string, string> ConflictingPrograms = new Dictionary<string, string>
    {
        ["cheatengine-x86_64"] = "Cheat Engine",
        ["cheatengine-x86_64-sse4-avx2"] = "Cheat Engine",
        ["cheatengine-i386"] = "Cheat Engine",
        ["cheat engine"] = "Cheat Engine",
        ["x64dbg"] = "x64dbg",
        ["x32dbg"] = "x64dbg",
        ["ollydbg"] = "OllyDbg",
        ["ida"] = "IDA",
        ["ida64"] = "IDA",
        ["windbg"] = "WinDbg",
        ["dbgx.shell"] = "WinDbg",
        ["processhacker"] = "Process Hacker",
        ["systeminformer"] = "System Informer",
        ["wemod"] = "WeMod",
        ["artmoney"] = "ArtMoney",
        ["reclass.net"] = "ReClass.NET",
    };

    /// <summary>
    /// Loaded drivers some anti-cheats block (old hardware-monitor and tuning tools): service-name prefix
    /// (lower case) → the tool to update.
    /// </summary>
    public static readonly IReadOnlyList<(string Prefix, string Tool)> BlockedDrivers =
    [
        ("winring0", Strings.DriverWinRing0),
        ("rtcore64", Strings.Format(Strings.DriverOlderVersion, "MSI Afterburner")),
        ("gdrv", Strings.DriverGigabyte),
    ];
}
