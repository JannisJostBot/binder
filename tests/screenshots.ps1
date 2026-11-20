# Screenshots for the website (Windows PowerShell 5.1, CI only: .github/workflows/site-screenshots.yml). Runs the
# published widget with a made-up library (the website's games, art from tests/Screenshots/art), in light and dark,
# and saves each view's window as a PNG with transparent rounded corners, plus the whole screen for reference.
#
# Like tests/smoke.ps1 it writes to the current user's %APPDATA%\Binder, sets HKCU\Software\Valve\Steam\SteamPath to
# a fake Steam install, changes the theme, the screen resolution and the display scale: run it on a CI machine only,
# never on your own PC.
param(
    [Parameter(Mandatory = $true)] [string] $Exe,
    [string] $Out = 'artifacts/screenshots'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing

$Exe = (Resolve-Path $Exe).Path
$artDir = (Resolve-Path (Join-Path $PSScriptRoot 'Screenshots/art')).Path
New-Item -ItemType Directory -Force $Out | Out-Null
$Out = (Resolve-Path $Out).Path
$dataDir = Join-Path $env:APPDATA 'Binder'
$log = @()
function Note([string] $text) { Write-Host $text; $script:log += $text }

$UIA = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]

# Win32 helpers (C# 5: Windows PowerShell compiles with the old compiler).
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class Shots {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DEVMODE {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettingsW(string device, int mode, ref DEVMODE dm);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettingsW(ref DEVMODE dm, int flags);

    // Every mode the display offers, as "width x height", the largest first.
    public static string Modes() {
        var seen = new System.Collections.Generic.List<string>();
        DEVMODE dm = new DEVMODE(); dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
        for (int i = 0; EnumDisplaySettingsW(null, i, ref dm); i++) {
            string m = dm.dmPelsWidth + "x" + dm.dmPelsHeight;
            if (!seen.Contains(m)) seen.Add(m);
        }
        return string.Join(", ", seen.ToArray());
    }
    // Switches to the largest mode no bigger than maxW x maxH; returns the result of ChangeDisplaySettings and the mode.
    public static string SetLargest(int maxW, int maxH) {
        DEVMODE best = new DEVMODE(); bool found = false;
        DEVMODE dm = new DEVMODE(); dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
        for (int i = 0; EnumDisplaySettingsW(null, i, ref dm); i++) {
            if (dm.dmPelsWidth > maxW || dm.dmPelsHeight > maxH || dm.dmBitsPerPel < 32) continue;
            if (!found || (long)dm.dmPelsWidth * dm.dmPelsHeight > (long)best.dmPelsWidth * best.dmPelsHeight) { best = dm; found = true; }
        }
        if (!found) return "no mode";
        best.dmFields = 0x80000 | 0x100000; // DM_PELSWIDTH | DM_PELSHEIGHT
        int r = ChangeDisplaySettingsW(ref best, 1); // CDS_UPDATEREGISTRY
        return r + " (" + best.dmPelsWidth + "x" + best.dmPelsHeight + ")";
    }

    [DllImport("user32.dll")] static extern bool SystemParametersInfoW(uint action, int param, ref int pv, uint flags);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] static extern bool SystemParametersInfoSet(uint action, int param, IntPtr pv, uint flags);
    // The display scale as Settings changes it (SPI_GET/SETLOGICALDPIOVERRIDE, undocumented): a step relative to the
    // recommended scale, applied without signing out.
    public static int ScaleStep() { int v = 0; SystemParametersInfoW(0x9E, 0, ref v, 0); return v; }
    public static bool SetScaleStep(int relative) { return SystemParametersInfoSet(0x9F, relative, IntPtr.Zero, 1); }

    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessageTimeoutW(IntPtr h, uint msg, IntPtr w, string l, uint flags, uint timeout, out IntPtr result);
    // Tells running apps that the light/dark setting changed, as Windows Settings does.
    public static void BroadcastTheme() {
        IntPtr r; SendMessageTimeoutW((IntPtr)0xFFFF, 0x1A, IntPtr.Zero, "ImmersiveColorSet", 2, 5000, out r);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    // The desktop's icons (the SysListView32 under SHELLDLL_DefView, in Progman or a WorkerW).
    public static IntPtr DesktopIcons() {
        IntPtr view = FindWindowExW(FindWindowExW(IntPtr.Zero, IntPtr.Zero, "Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
        for (IntPtr w = IntPtr.Zero; view == IntPtr.Zero; ) {
            w = FindWindowExW(IntPtr.Zero, w, "WorkerW", null);
            if (w == IntPtr.Zero) break;
            view = FindWindowExW(w, IntPtr.Zero, "SHELLDLL_DefView", null);
        }
        return view == IntPtr.Zero ? IntPtr.Zero : FindWindowExW(view, IntPtr.Zero, "SysListView32", null);
    }

    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    // The process's largest visible window: the panel, not a tooltip or popup.
    public static IntPtr VisibleWindowOf(int pid) {
        IntPtr found = IntPtr.Zero; long best = 0;
        EnumWindows(delegate (IntPtr h, IntPtr l) {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p == (uint)pid && IsWindowVisible(h)) {
                RECT r = Frame(h);
                long area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
                if (area > best) { best = area; found = h; }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
    // The window as drawn, without the invisible resize border.
    public static RECT Frame(IntPtr h) { RECT r; DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(RECT))); return r; }

    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    // A small mouse move in the screen's top left: the last input is the mouse, so no keyboard focus rings show.
    public static void NudgeMouse() {
        SetCursorPos(40, 40);
        mouse_event(0x0001, 2, 2, 0, UIntPtr.Zero);
    }
}
'@

function Wait-Until([scriptblock] $condition, [int] $seconds, [string] $what) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $result = & $condition
        if ($result) { return $result }
        Start-Sleep -Milliseconds 300
    }
    throw "Timed out after $seconds s waiting for: $what"
}

function Find-Named($root, [string] $name, [System.Windows.Automation.ControlType[]] $types) {
    $byName = New-Object System.Windows.Automation.PropertyCondition($UIA::NameProperty, $name)
    $typeConds = [System.Windows.Automation.Condition[]] @($types | ForEach-Object { New-Object System.Windows.Automation.PropertyCondition($UIA::ControlTypeProperty, $_) })
    $byType = if ($typeConds.Count -eq 1) { $typeConds[0] } else { [System.Windows.Automation.OrCondition]::new($typeConds) }
    $cond = [System.Windows.Automation.AndCondition]::new([System.Windows.Automation.Condition[]] @($byName, $byType))
    Wait-Until { $root.FindFirst($Scope::Descendants, $cond) } 10 "'$name'"
}

$CT = [System.Windows.Automation.ControlType]
function Invoke-Named($root, [string] $name) {
    $e = Find-Named $root $name @($CT::Button, $CT::ListItem, $CT::Hyperlink)
    $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

# An item of the shelf's "More actions" menu (a popup, so looked for from the root).
function Invoke-Menu($root, [string] $item) {
    Invoke-Named $root 'More actions'
    $e = Find-Named $UIA::RootElement $item @($CT::MenuItem)
    $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

# --- Screen: as large as the display offers (up to 4K), then the display scale that keeps ~1000 px of height. ---
Note "Display modes: $([Shots]::Modes())"
Note "Resolution change: $([Shots]::SetLargest(3840, 2160))"
Start-Sleep -Seconds 2
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$steps = @(100, 125, 150, 175, 200, 225, 250, 300)
$target = 0
for ($i = 0; $i -lt $steps.Count; $i++) { if ($screen.Height * 100 / $steps[$i] -ge 1000) { $target = $i } }
# Assumes the machine starts at 100% (true at 1024x768); the step is relative to the recommended scale.
$current = [Shots]::ScaleStep()
if ($target -gt 0) {
    Note "Display scale: step $current now, asking for $($steps[$target])% ($($target + $current) from the recommended): $([Shots]::SetScaleStep($target + $current))"
    Start-Sleep -Seconds 3
}
Note "Screen: $($screen.Width)x$($screen.Height)"

# --- A made-up library: the website's games. Ten from a fake Steam install (art in Steam's own cache folder, as on
# a real PC), one from Epic and one from GOG (their covers set as the game's own picture). ---
$steam = Join-Path ([IO.Path]::GetTempPath()) 'DemoSteam'
$steamapps = Join-Path $steam 'steamapps'
$cache = Join-Path $steam 'appcache\librarycache'
New-Item -ItemType Directory -Force (Join-Path $steamapps 'common'), $cache | Out-Null
Set-Content -Encoding UTF8 (Join-Path $steamapps 'libraryfolders.vdf') @"
"libraryfolders"
{
    "0"
    {
        "path"		"$($steam.Replace('\', '\\'))"
    }
}
"@
$now = Get-Date
$unix = [DateTimeOffset]::new($now).ToUnixTimeSeconds()
# Days since last played (0 = never), for Free up space's order.
$steamGames = @(
    @{ AppId = '3001001'; Art = 'starfall-drift'; Name = 'Starfall Drift'; Size = 48GB; Played = 1 },
    @{ AppId = '3001002'; Art = 'ember-keep';     Name = 'Ember Keep';     Size = 62GB; Played = 3 },
    @{ AppId = '3001003'; Art = 'tidebreak';      Name = 'Tidebreak';      Size = 18GB; Played = 40 },
    @{ AppId = '3001004'; Art = 'neon-circuit';   Name = 'Neon Circuit';   Size = 35GB; Played = 2 },
    @{ AppId = '3001005'; Art = 'frostline';      Name = 'Frostline';      Size = 27GB; Played = 9 },
    @{ AppId = '3001006'; Art = 'hollow-pines';   Name = 'Hollow Pines';   Size = 12GB; Played = 21 },
    @{ AppId = '3001007'; Art = 'iron-courier';   Name = 'Iron Courier';   Size = 41GB; Played = 0 },
    @{ AppId = '3001008'; Art = 'glowmoth';       Name = 'Glowmoth';       Size = 6GB;  Played = 5 },
    @{ AppId = '3001009'; Art = 'dune-runners';   Name = 'Dune Runners';   Size = 72GB; Played = 230 },
    @{ AppId = '3001010'; Art = 'skyward-isles';  Name = 'Skyward Isles';  Size = 9GB;  Played = 14 }
)
foreach ($g in $steamGames) {
    New-Item -ItemType Directory -Force (Join-Path $steamapps "common\$($g.Name)") | Out-Null
    $lastPlayed = if ($g.Played -gt 0) { $unix - $g.Played * 86400 } else { 0 }
    Set-Content -Encoding UTF8 (Join-Path $steamapps "appmanifest_$($g.AppId).acf") @"
"AppState"
{
    "appid"		"$($g.AppId)"
    "name"		"$($g.Name)"
    "StateFlags"		"4"
    "installdir"		"$($g.Name)"
    "LastPlayed"		"$lastPlayed"
    "SizeOnDisk"		"$([long]$g.Size)"
}
"@
    $art = Join-Path $cache $g.AppId
    New-Item -ItemType Directory -Force $art | Out-Null
    Copy-Item (Join-Path $artDir "$($g.Art)-cover.jpg") (Join-Path $art 'library_600x900.jpg')
    Copy-Item (Join-Path $artDir "$($g.Art)-hero.jpg") (Join-Path $art 'library_hero.jpg')
    Copy-Item (Join-Path $artDir "$($g.Art)-logo.png") (Join-Path $art 'logo.png')
}
New-Item -Force 'HKCU:\Software\Valve\Steam' | Out-Null
Set-ItemProperty 'HKCU:\Software\Valve\Steam' SteamPath ($steam.Replace('\', '/'))

$epicManifests = Join-Path $env:ProgramData 'Epic\EpicGamesLauncher\Data\Manifests'
New-Item -ItemType Directory -Force $epicManifests | Out-Null
$epicGame = Join-Path ([IO.Path]::GetTempPath()) 'DemoEpic\Rift Tactics'
New-Item -ItemType Directory -Force $epicGame | Out-Null
ConvertTo-Json -InputObject @{ DisplayName = 'Rift Tactics'; InstallLocation = $epicGame; AppName = 'RiftTactics';
    CatalogNamespace = 'demons'; CatalogItemId = 'demoitem'; InstallSize = 23GB; AppCategories = @('public', 'games') } |
    Set-Content -Encoding UTF8 (Join-Path $epicManifests 'DEMO1.item')

$gogGame = Join-Path ([IO.Path]::GetTempPath()) 'DemoGog\Little Lantern'
New-Item -ItemType Directory -Force $gogGame | Out-Null
Set-Content (Join-Path $gogGame 'LittleLantern.exe') ''
$gogKey = 'HKLM:\SOFTWARE\WOW6432Node\GOG.com\Games\1300000001'
try {
    New-Item -Force $gogKey -ErrorAction Stop | Out-Null
    Set-ItemProperty $gogKey gameID '1300000001'
    Set-ItemProperty $gogKey gameName 'Little Lantern'
    Set-ItemProperty $gogKey path $gogGame
    Set-ItemProperty $gogKey exe (Join-Path $gogGame 'LittleLantern.exe')
} catch { Note "Couldn't write the GOG registry key: $_" }

function Write-Library {
    New-Item -ItemType Directory -Force $dataDir | Out-Null
    $games = @(
        @{ Id = 'steam:3001001'; Source = 'Steam'; Name = 'Starfall Drift'; StoreId = '3001001'; Favorite = $true },
        @{ Id = 'steam:3001008'; Source = 'Steam'; Name = 'Glowmoth'; StoreId = '3001008'; Favorite = $true },
        @{ Id = 'epic:RiftTactics'; Source = 'Epic'; Name = 'Rift Tactics'; StoreId = 'RiftTactics'; InstallDir = $epicGame;
           CustomCover = (Join-Path $artDir 'rift-tactics-cover.jpg') },
        @{ Id = 'gog:1300000001'; Source = 'Gog'; Name = 'Little Lantern'; StoreId = '1300000001'; InstallDir = $gogGame;
           CustomCover = (Join-Path $artDir 'little-lantern-cover.jpg'); Favorite = $true }
    )
    foreach ($g in $steamGames) {
        if (-not ($games | Where-Object { $_.Id -eq "steam:$($g.AppId)" })) {
            $games += @{ Id = "steam:$($g.AppId)"; Source = 'Steam'; Name = $g.Name; StoreId = $g.AppId }
        }
    }
    foreach ($g in $games) { if ($g.Source -eq 'Steam') { $g.InstallDir = Join-Path $steamapps "common\$($g.Name)" } }
    ConvertTo-Json -InputObject $games -Depth 3 | Set-Content -Encoding UTF8 (Join-Path $dataDir 'library.json')

    # Eight weeks of evenings, the same every run; the last one ended 20 minutes ago (Starfall Drift, 1 h 50 min).
    $played = @('steam:3001001', 'steam:3001002', 'steam:3001004', 'steam:3001008', 'steam:3001005', 'gog:1300000001',
        'steam:3001010', 'steam:3001006', 'epic:RiftTactics', 'steam:3001003')
    $weights = @(9, 6, 5, 4, 3, 3, 2, 2, 2, 1)
    $pool = @(); for ($i = 0; $i -lt $played.Count; $i++) { $pool += @($played[$i]) * $weights[$i] }
    $rand = New-Object System.Random 7
    $lines = @()
    for ($day = 56; $day -ge 1; $day--) {
        if ($rand.Next(100) -lt 35) { continue }
        $start = $now.Date.AddDays(-$day).AddHours(18 + $rand.Next(4)).AddMinutes($rand.Next(60))
        $length = 25 + $rand.Next(150)
        $lines += [ordered]@{ GameId = $pool[$rand.Next($pool.Count)]; Start = $start.ToString('o'); End = $start.AddMinutes($length).ToString('o') }
    }
    $end = $now.AddMinutes(-20)
    $lines += [ordered]@{ GameId = 'steam:3001001'; Start = $end.AddMinutes(-110).ToString('o'); End = $end.ToString('o') }
    Get-ChildItem $dataDir -Filter 'sessions*' | Remove-Item -Force
    # One file per year, by the session's end (SessionLog).
    $lines | Group-Object { ([DateTimeOffset]::Parse($_.End)).Year } | ForEach-Object {
        $_.Group | ForEach-Object { ConvertTo-Json -InputObject $_ -Compress } |
            Set-Content -Encoding UTF8 (Join-Path $dataDir "sessions-$($_.Name).jsonl")
    }
    # The second-to-last session counts as already greeted, so "Welcome back" shows for the last one.
    $script:greetedUpTo = $now.AddDays(-1).ToString('o')
}

function Write-Settings([hashtable] $extra) {
    $s = @{ SetupDone = $true; DisplayMode = 'Flyout'; GameModeEnabled = $true; Updates = 'Off'; HideDelaySeconds = 10 }
    if ($extra) { foreach ($k in $extra.Keys) { $s[$k] = $extra[$k] } }
    ConvertTo-Json -InputObject $s -Depth 3 | Set-Content -Encoding UTF8 (Join-Path $dataDir 'settings.json')
}

function Set-Theme([bool] $light) {
    $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
    New-Item -Force $key -ErrorAction SilentlyContinue | Out-Null
    $v = [int]$light
    Set-ItemProperty $key AppsUseLightTheme $v -Type DWord
    Set-ItemProperty $key SystemUsesLightTheme $v -Type DWord
    [Shots]::BroadcastTheme()
    Start-Sleep -Seconds 2
}

function Stop-Widget {
    Get-Process Binder -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800
}

# Starts the widget (it opens the flyout), returns its UI Automation window.
function Start-Widget {
    Stop-Widget
    [Shots]::NudgeMouse()
    $p = Start-Process $Exe -PassThru
    $script:widget = $p
    $cond = New-Object System.Windows.Automation.PropertyCondition($UIA::ProcessIdProperty, $p.Id)
    $w = Wait-Until { $UIA::RootElement.FindFirst($Scope::Children, $cond) } 20 'widget window'
    Find-Named $w 'More actions' @($CT::Button) | Out-Null
    Start-Sleep -Seconds 4   # covers decoded, the rescan done
    $w
}

# The widget's window, cropped to its frame, its corners made transparent (Windows 11 draws them rounded).
function Shot([string] $name) {
    Start-Sleep -Milliseconds 700
    $hwnd = [Shots]::VisibleWindowOf($script:widget.Id)
    if ($hwnd -eq [IntPtr]::Zero) { throw "No visible window for '$name'" }
    $r = [Shots]::Frame($hwnd)
    $scale = [Shots]::GetDpiForWindow($hwnd) / 96.0
    $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
    $raw = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($raw)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $raw.Size)
    $g.Dispose()

    $png = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($png)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $d = [Math]::Round(8 * $scale) * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($w - $d, 0, $d, $d, 270, 90)
    $path.AddArc($w - $d, $h - $d, $d, $d, 0, 90); $path.AddArc(0, $h - $d, $d, $d, 90, 90); $path.CloseFigure()
    $brush = New-Object System.Drawing.TextureBrush $raw
    $g.FillPath($brush, $path)
    $g.Dispose(); $brush.Dispose()
    $file = Join-Path $Out "$($script:theme)-$name.png"
    $png.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $png.Dispose(); $raw.Dispose()

    # The whole screen too, for reference (and a picture of Binder by the clock).
    $b = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $full = New-Object System.Drawing.Bitmap $b.Width, $b.Height
    $g = [System.Drawing.Graphics]::FromImage($full)
    $g.CopyFromScreen($b.Left, $b.Top, 0, 0, $full.Size)
    $g.Dispose()
    $full.Save((Join-Path $Out "screen\$($script:theme)-$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $full.Dispose()
    Note "$($script:theme)-${name}: ${w}x${h} at $([int]($scale * 100))%"
}

# Runs one view; a failure is noted and the others still run.
function View([string] $name, [scriptblock] $steps) {
    try { & $steps; Shot $name }
    catch {
        Note "::warning::$($script:theme)-${name} failed: $_"
        try { Shot "$name-failed" } catch { }
    }
}

New-Item -ItemType Directory -Force (Join-Path $Out 'screen') | Out-Null
# A clear desktop: no icons, no other windows.
(New-Object -ComObject Shell.Application).MinimizeAll()
$icons = [Shots]::DesktopIcons()
if ($icons -ne [IntPtr]::Zero) { [Shots]::ShowWindow($icons, 0) | Out-Null } else { Note 'Desktop icons not found' }
Start-Sleep -Seconds 1

foreach ($theme in @('light', 'dark')) {
    $script:theme = $theme
    Set-Theme ($theme -eq 'light')
    Write-Library
    Write-Settings @{ WelcomeBackSessionStart = $script:greetedUpTo }

    View 'welcome-back' { $script:window = Start-Widget }
    View 'shelf' { $script:window = Start-Widget }
    View 'detail' {
        $script:window = Start-Widget
        Invoke-Named $window 'Details for Starfall Drift'
        Find-Named $window 'Back to all games' @($CT::Button) | Out-Null
        Start-Sleep -Seconds 2   # banner and logo
    }
    View 'settings' {
        $script:window = Start-Widget
        Invoke-Menu $window 'Settings'
        Find-Named $window 'Refresh library' @($CT::Button) | Out-Null
    }
    View 'game-mode' {
        $script:window = Start-Widget
        Invoke-Menu $window ('Game mode settings' + [char]0x2026)
        Start-Sleep -Seconds 2   # the page reads Windows' settings when it opens
    }
    View 'free-up-space' {
        $script:window = Start-Widget
        Invoke-Menu $window ('Free up space' + [char]0x2026)
        $box = Find-Named $window 'Space the new game needs, in gigabytes' @($CT::Edit)
        # 60 GB more than drive C: has free, so the plan has something to suggest.
        $free = [math]::Floor((Get-PSDrive C).Free / 1GB)
        $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue([string]($free + 60))
        Start-Sleep -Seconds 1
        # The demo games are on C:; CI machines also have a roomy D:, which Binder would suggest instead.
        $radio = New-Object System.Windows.Automation.PropertyCondition($UIA::ControlTypeProperty, $CT::RadioButton)
        $c = Wait-Until { $window.FindAll($Scope::Descendants, $radio) | Where-Object { $_.Current.Name -like '*C:*' } | Select-Object -First 1 } 10 'drive C: choice'
        $c.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        [Shots]::NudgeMouse()
        Start-Sleep -Seconds 2
    }
    View 'list' {
        Write-Settings @{ GridDensity = 'List' }
        $script:window = Start-Widget
    }
    View 'this-week' {
        Write-Settings @{ SpotlightShowsWeek = $true }
        $script:window = Start-Widget
    }
    Write-Settings $null
}
Stop-Widget
if ($icons -ne [IntPtr]::Zero) { [Shots]::ShowWindow($icons, 5) | Out-Null }
$log | Set-Content (Join-Path $Out 'log.txt')
