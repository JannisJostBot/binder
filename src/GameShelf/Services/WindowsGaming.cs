using System.ComponentModel;
using System.Diagnostics;
using GameShelf.Shared;
using Microsoft.Win32;

namespace GameShelf.Services;

/// <summary>
/// Windows' own gaming settings for Settings › Game mode (proposals §20): read once when the page opens, never
/// changed by us (the user turns them on in Windows Settings, which the page opens).
/// </summary>
public static class WindowsGaming
{
    private const string GameBarKey = @"Software\Microsoft\GameBar";
    private const string GpuPreferencesKey = @"Software\Microsoft\DirectX\UserGpuPreferences";

    /// <summary>Windows' Game Mode. A missing value means on (Windows' default).</summary>
    public static bool GameModeOn()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(GameBarKey);
            return key?.GetValue("AutoGameModeEnabled") is not int value || value != 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return true;
        }
    }

    /// <summary>"Optimizations for windowed games" exists on Windows 11 only.</summary>
    public static bool HasWindowedOptimizations => PcChecks.IsWindows11;

    /// <summary>On when Windows' graphics settings hold SwapEffectUpgradeEnable=1.</summary>
    public static bool WindowedOptimizationsOn()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(GpuPreferencesKey);
            return key?.GetValue("DirectXUserGlobalSettings") is string settings && GameModeInfo.IsSwapEffectUpgradeOn(settings);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>For the row's "Now:" text only (the watcher checks the exact exe path).</summary>
    public static bool OneDriveRunning()
    {
        var found = Process.GetProcessesByName("OneDrive");
        foreach (var p in found) p.Dispose();
        return found.Length > 0;
    }

    public static bool DocumentsInOneDrive() => GameModeInfo.OneDriveHoldsDocuments();

    /// <summary>Opens a page of Windows Settings (fixed ms-settings: links only).</summary>
    public static void OpenSettings(string uri)
    {
        if (!uri.StartsWith("ms-settings:", StringComparison.Ordinal)) return;
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            ErrorLog.Write(e, "Opening " + uri);
        }
    }
}
