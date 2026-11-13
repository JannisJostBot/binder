using Binder.Library;
using Microsoft.Win32;

namespace Binder.Tests;

static partial class Tests
{
    /// <summary>
    /// Milestone 17 (spec §14): Settings › Advanced values are untrusted. Each kind is checked; invalid and unknown
    /// ones fall back to the defaults.
    /// </summary>
    static void SourceDefaultsTests(string dir)
    {
        SourceValueError V(SourceEntry entry, string value) => SourceDefaults.Validate(entry, value, out _);
        string N(SourceEntry entry, string value) { SourceDefaults.Validate(entry, value, out var n); return n; }

        // Empty: the default.
        Check(V(SourceEntry.SteamFolder, "  ") == SourceValueError.None && N(SourceEntry.SteamFolder, " ") == "", "sources: empty means the default");

        // Folders: full, local, existing.
        var folder = Path.Combine(dir, "source-folder");
        Directory.CreateDirectory(folder);
        Check(V(SourceEntry.SteamFolder, folder) == SourceValueError.None && N(SourceEntry.SteamFolder, folder + "\\") == folder,
            "sources: an existing full folder is accepted, without its trailing backslash");
        Check(V(SourceEntry.EpicManifests, "Manifests") == SourceValueError.Folder &&
              V(SourceEntry.EpicManifests, Path.Combine(dir, "missing-folder")) == SourceValueError.Folder &&
              V(SourceEntry.EpicManifests, @"\\server\share\Manifests") == SourceValueError.Folder &&
              V(SourceEntry.EpicManifests, "//server/share/x") == SourceValueError.Folder &&
              V(SourceEntry.SteamArtFolder, "C:relative") == SourceValueError.Folder,
            "sources: relative, missing and network folders are refused");

        // Registry keys: HKLM\SOFTWARE or HKCU\Software only.
        Check(N(SourceEntry.GogKey, @"HKEY_LOCAL_MACHINE\SOFTWARE\GOG.com\Games") == @"HKLM\SOFTWARE\GOG.com\Games" &&
              N(SourceEntry.UbisoftKey, @"hkcu\Software\Ubisoft\Installs") == @"HKCU\Software\Ubisoft\Installs",
            "sources: registry keys under SOFTWARE are accepted with their hive's short name");
        Check(V(SourceEntry.GogKey, @"HKLM\SYSTEM\CurrentControlSet") == SourceValueError.RegistryKey &&
              V(SourceEntry.GogKey, @"HKU\.DEFAULT\Software\x") == SourceValueError.RegistryKey &&
              V(SourceEntry.GogKey, @"HKLM\SOFTWARE") == SourceValueError.RegistryKey &&
              V(SourceEntry.GogKey, @"HKLM\SOFTWARE\..\SYSTEM") == SourceValueError.RegistryKey &&
              V(SourceEntry.GogKey, @"HKLM\SOFTWARE\\GOG") == SourceValueError.RegistryKey &&
              V(SourceEntry.RockstarKey, @"SOFTWARE\Rockstar") == SourceValueError.RegistryKey,
            "sources: other hives, other roots, '..' and empty parts are refused");
        Check(SourceDefaults.TryParseKey(SourceDefaults.DefaultGogKey, out var hive, out var sub) && hive == RegistryHive.LocalMachine &&
              sub == @"SOFTWARE\WOW6432Node\GOG.com\Games", "sources: the default keys parse");

        // File names: one image name.
        Check(V(SourceEntry.SteamBannerFile, "library_hero_2x.jpg") == SourceValueError.None &&
              V(SourceEntry.SteamLogoFile, "logo.PNG") == SourceValueError.None,
            "sources: plain .jpg / .png names are accepted");
        Check(V(SourceEntry.SteamBannerFile, @"..\hero.jpg") == SourceValueError.FileName &&
              V(SourceEntry.SteamBannerFile, "sub/hero.jpg") == SourceValueError.FileName &&
              V(SourceEntry.SteamBannerFile, "C:hero.jpg") == SourceValueError.FileName &&
              V(SourceEntry.SteamBannerFile, "hero.exe") == SourceValueError.FileName &&
              V(SourceEntry.SteamLogoFile, ".png") == SourceValueError.FileName &&
              V(SourceEntry.SteamLogoFile, "a, b.png") == SourceValueError.FileName,
            "sources: folders, other types and lists are refused for one file name");
        Check(N(SourceEntry.SteamCoverFiles, "a.jpg ,b.jpeg") == "a.jpg, b.jpeg" &&
              V(SourceEntry.SteamCoverFiles, "a.jpg, ../b.jpg") == SourceValueError.FileNames &&
              V(SourceEntry.SteamCoverFiles, "a.jpg, b.jpg, c.jpg, d.jpg, e.jpg, f.jpg") == SourceValueError.FileNames &&
              V(SourceEntry.SteamCoverFiles, " , ") == SourceValueError.FileNames,
            "sources: cover names are a short list of image names");

        // The download address.
        Check(V(SourceEntry.SteamDownload, SourceDefaults.DefaultDownload) == SourceValueError.None &&
              V(SourceEntry.SteamDownload, "https://cdn.example.com/steam/{appid}/{file}?v=1") == SourceValueError.None &&
              V(SourceEntry.SteamDownload, "https://cdn.example.com/apps/{appid}/header.jpg") == SourceValueError.None,
            "sources: https addresses with {appid} are accepted");
        Check(V(SourceEntry.SteamDownload, "http://cdn.example.com/{appid}/{file}") == SourceValueError.Address &&
              V(SourceEntry.SteamDownload, "https://cdn.example.com/{file}") == SourceValueError.Address &&
              V(SourceEntry.SteamDownload, "https://{appid}.example.com/{file}") == SourceValueError.Address &&
              V(SourceEntry.SteamDownload, "https://user:pw@cdn.example.com/{appid}") == SourceValueError.Address &&
              V(SourceEntry.SteamDownload, "https://cdn.example.com/{appid}/{name}") == SourceValueError.Address &&
              V(SourceEntry.SteamDownload, "file:///C:/{appid}") == SourceValueError.Address &&
              V(SourceEntry.SteamDownload, "https://cdn.example.com/{appid} x") == SourceValueError.Address &&
              V(SourceEntry.SteamDownload, "https://127.0.0.1/{appid}/{file}") == SourceValueError.Address,
            "sources: http, no {appid}, placeholders in the host, user info, unknown placeholders and IP hosts are refused");
        Check(SourceDefaults.DownloadHost("https://cdn.example.com/steam/{appid}") == "cdn.example.com" &&
              SourceDefaults.DefaultHost == "shared.steamstatic.com", "sources: the download host is read for the notice");

        // In use: valid values replace the defaults, the rest is ignored.
        SourceDefaults.Use(new Dictionary<string, string>
        {
            ["SteamCoverFiles"] = "one.jpg, two.png",
            ["SteamBannerFile"] = "../evil.jpg",
            ["SteamDownload"] = "https://cdn.example.com/{appid}/{file}",
            ["EpicManifests"] = folder,
            ["NotAnEntry"] = "x",
            ["3"] = "y.jpg",
        });
        Check(SourceDefaults.SteamCoverFiles().SequenceEqual(["one.jpg", "two.png"]) && SourceDefaults.SteamBannerFile() == "library_hero.jpg" &&
              SourceDefaults.SteamDownloadUrl("570", "one.jpg") == "https://cdn.example.com/570/one.jpg" &&
              SourceDefaults.EpicManifests() == folder && SourceDefaults.SteamLogoFile() == "logo.png",
            "sources: valid values are used, invalid and unknown ones keep the defaults");
        SourceDefaults.Use(null);
        Check(SourceDefaults.SteamCoverFiles().SequenceEqual(SourceDefaults.DefaultCoverFiles) &&
              SourceDefaults.SteamDownloadUrl("570", "logo.png") == "https://shared.steamstatic.com/store_item_assets/steam/apps/570/logo.png",
            "sources: back to the defaults");
    }
}
