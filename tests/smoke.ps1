# CI smoke test (Windows PowerShell 5.1). Runs the real widget with a fake library, drives it through
# UI Automation like a user would, takes screenshots, measures idle CPU and memory, and checks the
# launch -> hide -> return loop end to end with a stand-in game exe.
#
# Writes to the current user's %APPDATA%\Binder, sets HKCU\Software\Valve\Steam\SteamPath to a fake
# Steam install and toggles the Run key, so run it on a CI machine only, never on your own PC.
param(
    [Parameter(Mandatory = $true)] [string] $Exe,
    [Parameter(Mandatory = $true)] [string] $FakeGame
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing

$Exe = (Resolve-Path $Exe).Path
$FakeGame = (Resolve-Path $FakeGame).Path
$art = Join-Path (Get-Location) 'artifacts'
New-Item -ItemType Directory -Force $art | Out-Null
$dataDir = Join-Path $env:APPDATA 'Binder'
New-Item -ItemType Directory -Force $dataDir | Out-Null
$metrics = @()
# The website's "How much it uses" numbers (scripts/site-numbers.ps1 writes them into site/index.html).
$siteNumbers = [ordered]@{}
$testStart = Get-Date
$watcherBuild = if ($env:WATCHER_BUILD) { $env:WATCHER_BUILD } else { 'JIT' }

$UIA = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]

# Win32 helpers: z-order, real mouse clicks and keystrokes (UI Automation can't show z-order).
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Win {
    [DllImport("user32.dll")] static extern IntPtr GetTopWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowW(string cls, string title);
    // Separate methods: PowerShell passes $null to a string parameter as "", which FindWindow treats as a real name.
    public static IntPtr FindByClass(string cls) { return FindWindowW(cls, null); }
    public static IntPtr FindByTitle(string title) { return FindWindowW(null, title); }
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool repaint);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, System.Text.StringBuilder s, int n);
    public static string ClassName(IntPtr h) { var sb = new System.Text.StringBuilder(64); GetClassNameW(h, sb, 64); return sb.ToString(); }
    // The first visible windows from the top of the z-order, for diagnostics.
    public static string TopVisible(int count, IntPtr mark) {
        var sb = new System.Text.StringBuilder();
        int i = 0, n = 0;
        for (IntPtr w = GetTopWindow(IntPtr.Zero); w != IntPtr.Zero && n < count; w = GetWindow(w, 2), i++) {
            if (!IsWindowVisible(w)) continue;
            sb.Append(n == 0 ? "" : ", ").Append(i).Append(':').Append(ClassName(w)).Append(w == mark ? " (widget)" : "");
            n++;
        }
        return sb.ToString();
    }
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    // Position from the top of the z-order (0 = frontmost), or -1.
    public static int ZIndex(IntPtr target) {
        int i = 0;
        for (IntPtr w = GetTopWindow(IntPtr.Zero); w != IntPtr.Zero; w = GetWindow(w, 2), i++)
            if (w == target) return i;
        return -1;
    }
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        mouse_event(0x02, 0, 0, 0, UIntPtr.Zero);
        mouse_event(0x04, 0, 0, 0, UIntPtr.Zero);
    }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    public static RECT Rect(IntPtr h) { RECT r; GetWindowRect(h, out r); return r; }
    [StructLayout(LayoutKind.Sequential)] struct APPBARDATA { public int cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public IntPtr lParam; }
    [DllImport("shell32.dll")] static extern IntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);
    // ABM_SETSTATE with ABS_AUTOHIDE (1) or ABS_ALWAYSONTOP (2), like the taskbar setting in Windows Settings.
    public static void SetTaskbarAutoHide(IntPtr tray, bool on) {
        APPBARDATA d = new APPBARDATA();
        d.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
        d.hWnd = tray;
        d.lParam = (IntPtr)(on ? 1 : 2);
        SHAppBarMessage(0xA, ref d);
    }
    public static void Keys(params byte[] vks) {
        foreach (var vk in vks) keybd_event(vk, 0, 0, UIntPtr.Zero);
        for (int i = vks.Length - 1; i >= 0; i--) keybd_event(vks[i], 0, 2, UIntPtr.Zero);
    }
    public static void WinShiftG() { Keys(0x5B, 0x10, 0x47); }
    public static void Esc() { Keys(0x1B); }
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    // A visible top-level window of the process (the flyout), or zero. The hidden message window doesn't count.
    public static IntPtr VisibleWindowOf(int pid) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) => {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p == (uint)pid && IsWindowVisible(h)) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    public static void WinD() {
        keybd_event(0x5B, 0, 0, UIntPtr.Zero);
        keybd_event(0x44, 0, 0, UIntPtr.Zero);
        keybd_event(0x44, 0, 2, UIntPtr.Zero);
        keybd_event(0x5B, 0, 2, UIntPtr.Zero);
    }
}
'@

function Shot([string] $name) {
    $b = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($b.Left, $b.Top, 0, 0, $bmp.Size)
    $bmp.Save((Join-Path $art "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host "screenshot: $name.png"
}

function Fail([string] $message) {
    try { Shot 'failure' } catch { }
    throw $message
}

function Wait-Until([scriptblock] $condition, [int] $seconds, [string] $what) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $result = & $condition
        if ($result) { return $result }
        Start-Sleep -Milliseconds 300
    }
    Fail "Timed out after $seconds s waiting for: $what"
}

function Get-WidgetWindow([int] $processId) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($UIA::ProcessIdProperty, $processId)
    Wait-Until { $UIA::RootElement.FindFirst($Scope::Children, $cond) } 15 "widget window (pid $processId)"
}

# Buttons, and the grid's covers, which WinUI exposes as invokable list items.
function Find-Button($root, [string] $name) {
    $byName = New-Object System.Windows.Automation.PropertyCondition($UIA::NameProperty, $name)
    $isButton = [System.Windows.Automation.OrCondition]::new([System.Windows.Automation.Condition[]] @(
        (New-Object System.Windows.Automation.PropertyCondition($UIA::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)),
        (New-Object System.Windows.Automation.PropertyCondition($UIA::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)),
        (New-Object System.Windows.Automation.PropertyCondition($UIA::ControlTypeProperty, [System.Windows.Automation.ControlType]::Hyperlink))))   # HyperlinkButton, e.g. "Hide from shelf"
    $cond = [System.Windows.Automation.AndCondition]::new([System.Windows.Automation.Condition[]] @($byName, $isButton))
    Wait-Until { $root.FindFirst($Scope::Descendants, $cond) } 10 "button '$name'"
}

function Find-Element($root, [string] $name, $controlType) {
    $byName = New-Object System.Windows.Automation.PropertyCondition($UIA::NameProperty, $name)
    $byType = New-Object System.Windows.Automation.PropertyCondition($UIA::ControlTypeProperty, $controlType)
    $cond = [System.Windows.Automation.AndCondition]::new([System.Windows.Automation.Condition[]] @($byName, $byType))
    Wait-Until { $root.FindFirst($Scope::Descendants, $cond) } 10 "$($controlType.ProgrammaticName) '$name'"
}

function Get-Game([string] $id) {
    (Get-Content (Join-Path $dataDir 'library.json') -Raw | ConvertFrom-Json) | Where-Object { $_.Id -eq $id }
}

function Find-ById($root, [string] $id) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($UIA::AutomationIdProperty, $id)
    Wait-Until { $root.FindFirst($Scope::Descendants, $cond) } 10 "element '$id'"
}

# Cover size is a combo box: open it and pick the item.
function Select-CoverSize($root, [string] $size) {
    $box = Find-Element $root 'Cover size' ([System.Windows.Automation.ControlType]::ComboBox)
    $box.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    $item = Wait-Until { $UIA::RootElement.FindFirst($Scope::Descendants, [System.Windows.Automation.AndCondition]::new([System.Windows.Automation.Condition[]] @(
        (New-Object System.Windows.Automation.PropertyCondition($UIA::NameProperty, $size)),
        (New-Object System.Windows.Automation.PropertyCondition($UIA::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem))))) } 5 "cover size item '$size'"
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    try { $box.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse() } catch { }
}

function Invoke-Button($root, [string] $name) {
    $button = Find-Button $root $name
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Write-Host "clicked: $name"
}

# Settings is the last item of the shelf's "..." menu (More actions); the menu opens in a popup, so look from the root.
function Open-Settings($root) {
    Invoke-Button $root 'More actions'
    $item = Wait-Until { $UIA::RootElement.FindFirst($Scope::Descendants, [System.Windows.Automation.AndCondition]::new([System.Windows.Automation.Condition[]] @(
        (New-Object System.Windows.Automation.PropertyCondition($UIA::NameProperty, 'Settings')),
        (New-Object System.Windows.Automation.PropertyCondition($UIA::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem))))) } 5 "menu item 'Settings'"
    $item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Write-Host 'clicked: Settings (More actions menu)'
}

# Asks the running widget for its memory split (managed heap vs the rest) and adds it to the metrics.
function Add-MemoryReport([string] $label) {
    $report = Join-Path $env:LOCALAPPDATA 'Binder\memory.txt'
    Remove-Item $report -ErrorAction SilentlyContinue
    Start-Process $Exe -ArgumentList '--memory-report' -Wait
    if (Test-Path $report) { $script:metrics += "Memory report ($label): " + ((Get-Content $report | Select-Object -Skip 1) -join '; ') }
}

# Task Manager's "Memory" column (Details: "Memory (private working set)"), in MB; the number users compare with.
function Get-PrivateWorkingSetMB($process) {
    $p = Get-CimInstance Win32_PerfFormattedData_PerfProc_Process -Filter "IDProcess=$($process.Id)" -ErrorAction SilentlyContinue
    if ($p) { [math]::Round($p.WorkingSetPrivate / 1MB, 1) }
}

function Assert-Running($process, [string] $when) {
    $process.Refresh()
    if ($process.HasExited) { Fail "The widget exited unexpectedly ($when), exit code $($process.ExitCode)" }
}

# --- Fake Steam install, found through the registry like the real one. Real appids, so the CDN art
# downloads. One malicious manifest (absolute installdir) must be rejected by the scan. ---
$steam = Join-Path ([IO.Path]::GetTempPath()) 'FakeSteam'
$steamapps = Join-Path $steam 'steamapps'
New-Item -ItemType Directory -Force (Join-Path $steamapps 'common') | Out-Null
$vdfPath = $steam.Replace('\', '\\')
Set-Content -Encoding UTF8 (Join-Path $steamapps 'libraryfolders.vdf') @"
"libraryfolders"
{
    "0"
    {
        "path"		"$vdfPath"
    }
}
"@
$steamGames = @(
    @{ AppId = '570'; Name = 'Dota 2'; Dir = 'dota 2 beta'; Size = 42GB },
    @{ AppId = '730'; Name = 'Counter-Strike 2'; Dir = 'Counter-Strike Global Offensive'; Size = 33GB },
    @{ AppId = '440'; Name = 'Team Fortress 2'; Dir = 'Team Fortress 2'; Size = 26GB },
    @{ AppId = '230410'; Name = 'Warframe'; Dir = 'Warframe'; Size = 50GB },
    @{ AppId = '1172470'; Name = 'Apex Legends'; Dir = 'Apex Legends'; Size = 75GB },
    @{ AppId = '1085660'; Name = 'Destiny 2'; Dir = 'Destiny 2'; Size = 105GB },
    @{ AppId = '999999'; Name = 'Evil Manifest'; Dir = 'C:\\Windows'; Size = 1GB }
)
foreach ($g in $steamGames) {
    if ($g.AppId -ne '999999') { New-Item -ItemType Directory -Force (Join-Path $steamapps "common\$($g.Dir)") | Out-Null }
    Set-Content -Encoding UTF8 (Join-Path $steamapps "appmanifest_$($g.AppId).acf") @"
"AppState"
{
    "appid"		"$($g.AppId)"
    "name"		"$($g.Name)"
    "StateFlags"		"4"
    "installdir"		"$($g.Dir)"
    "SizeOnDisk"		"$([long]$g.Size)"
}
"@
}
New-Item -Force 'HKCU:\Software\Valve\Steam' | Out-Null
Set-ItemProperty 'HKCU:\Software\Valve\Steam' SteamPath ($steam.Replace('\', '/'))

# --- Fake Epic install: a manifest in ProgramData like the launcher writes, plus one pointing at a drive root. ---
$epicManifests = Join-Path $env:ProgramData 'Epic\EpicGamesLauncher\Data\Manifests'
New-Item -ItemType Directory -Force $epicManifests | Out-Null
$epicGame = Join-Path ([IO.Path]::GetTempPath()) 'FakeEpic\Fake Epic Game'
New-Item -ItemType Directory -Force $epicGame | Out-Null
ConvertTo-Json -InputObject @{ DisplayName = 'Fake Epic Game'; InstallLocation = $epicGame; AppName = 'FakeEpicApp';
    CatalogNamespace = 'fakens'; CatalogItemId = 'fakeitem'; InstallSize = 3GB; AppCategories = @('public', 'games') } |
    Set-Content -Encoding UTF8 (Join-Path $epicManifests 'FAKE1.item')
ConvertTo-Json -InputObject @{ DisplayName = 'Evil Epic'; InstallLocation = 'C:\'; AppName = 'EvilApp';
    CatalogNamespace = 'evilns'; CatalogItemId = 'evilitem' } |
    Set-Content -Encoding UTF8 (Join-Path $epicManifests 'EVIL1.item')

# --- Fake GOG install in HKLM (needs admin, which CI runners have; skipped otherwise). ---
$gogGame = Join-Path ([IO.Path]::GetTempPath()) 'FakeGog\Fake GOG Game'
New-Item -ItemType Directory -Force $gogGame | Out-Null
Set-Content (Join-Path $gogGame 'game.exe') ''
$gogKey = 'HKLM:\SOFTWARE\WOW6432Node\GOG.com\Games\1207658924'
$gogReady = $true
try {
    New-Item -Force $gogKey -ErrorAction Stop | Out-Null
    Set-ItemProperty $gogKey gameID '1207658924'
    Set-ItemProperty $gogKey gameName 'Fake GOG Game'
    Set-ItemProperty $gogKey path $gogGame
    Set-ItemProperty $gogKey exe (Join-Path $gogGame 'game.exe')
} catch { $gogReady = $false; Write-Host "::warning::Couldn't write the GOG registry key, skipping GOG checks: $_" }

# --- Desktop shortcuts to import: a program in a known game folder and a Steam link (both pre-ticked),
# and a web link (not ticked, must stay). ---
$desktop = [Environment]::GetFolderPath('Desktop')
$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut((Join-Path $desktop 'Loop Shortcut.lnk'))
$lnk.TargetPath = $FakeGame
$lnk.Arguments = '20'
$lnk.Save()
Set-Content (Join-Path $desktop 'Dota 2.url') "[InternetShortcut]`r`nURL=steam://rungameid/570`r`n"
Set-Content (Join-Path $desktop 'Example.url') "[InternetShortcut]`r`nURL=https://example.com/`r`n"

# Library: only the manual stand-in game; the Steam games come from the scan.
$now = Get-Date
$games = @(
    @{ Id = 'manual:loop'; Source = 'Manual'; Name = 'Loop Test'; ExePath = $FakeGame;
       InstallDir = (Split-Path $FakeGame); HideDelaySeconds = 0 }
)
$sessions = @(
    @{ GameId = 'steam:570'; Start = $now.AddDays(-1).AddHours(-3).ToString('o'); End = $now.AddDays(-1).AddMinutes(-25).ToString('o') },
    @{ GameId = 'steam:570'; Start = $now.AddDays(-4).AddHours(-2).ToString('o'); End = $now.AddDays(-4).AddMinutes(-50).ToString('o') },
    @{ GameId = 'steam:730'; Start = $now.AddDays(-2).AddHours(-1).ToString('o'); End = $now.AddDays(-2).ToString('o') },
    # One from just now, so "This week" has something even on a Monday (the others are then all last week).
    @{ GameId = 'steam:570'; Start = $now.AddMinutes(-2).ToString('o'); End = $now.AddMinutes(-1).ToString('o') }
)
ConvertTo-Json -InputObject $games -Depth 3 | Set-Content -Encoding UTF8 (Join-Path $dataDir 'library.json')
# Written in the old single-file format, so the widget's move into sessions-<year>.jsonl runs too.
ConvertTo-Json -InputObject $sessions -Depth 3 | Set-Content -Encoding UTF8 (Join-Path $dataDir 'sessions.json')
# The first part tests the desktop-docked mode, with first-run setup already done; section 5 tests the flyout.
ConvertTo-Json -InputObject @{ HideDelaySeconds = 10; DisplayMode = 'Desktop'; SetupDone = $true } | Set-Content -Encoding UTF8 (Join-Path $dataDir 'settings.json')
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Remove-ItemProperty $runKey 'Binder' -ErrorAction SilentlyContinue

# Desktop-layer screenshots need a clear desktop: minimize the runner's console and anything else.
(New-Object -ComObject Shell.Application).MinimizeAll()
Start-Sleep -Seconds 1

try {
    # --- 1. Start and show the shelf. ---
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $widget = Start-Process $Exe -PassThru
    $window = Get-WidgetWindow $widget.Id
    $metrics += "Time to window: $($sw.ElapsedMilliseconds) ms (includes UI Automation lookup)"
    Start-Sleep -Seconds 6   # let covers download and decode
    Assert-Running $widget 'after start'
    Shot '1-shelf'

    Wait-Until { Get-Game 'steam:570' } 10 'Steam games found by the scan' | Out-Null
    if (Get-Game 'steam:999999') { Fail 'The scan accepted a manifest with an absolute installdir' }
    Wait-Until { Get-Game 'epic:FakeEpicApp' } 10 'Epic game found by the scan' | Out-Null
    if (Get-Game 'epic:EvilApp') { Fail 'The scan accepted an Epic manifest installed at a drive root' }
    $metrics += 'Epic scan: fake game found, drive-root manifest rejected'
    if ($gogReady) {
        if (-not (Get-Game 'gog:1207658924')) { Fail 'The GOG game in the registry was not found' }
        $metrics += 'GOG scan: fake game found'
    }
    $metrics += "Steam scan: $(@((Get-Content (Join-Path $dataDir 'library.json') -Raw | ConvertFrom-Json) | Where-Object { $_.Source -eq 'Steam' }).Count) games found, malicious manifest rejected"

    # --- 2. Idle cost: no timers or animations should run while it sits on the desktop. ---
    $widget.Refresh()
    $cpuBefore = $widget.TotalProcessorTime
    Start-Sleep -Seconds 10
    $widget.Refresh()
    $idleCpuMs = [int]($widget.TotalProcessorTime - $cpuBefore).TotalMilliseconds
    $metrics += "Idle CPU over 10 s: $idleCpuMs ms"
    $metrics += "Working set: $([int]($widget.WorkingSet64 / 1MB)) MB, private: $([int]($widget.PrivateMemorySize64 / 1MB)) MB"
    if ($idleCpuMs -gt 200) { Write-Host "::warning::Idle CPU was $idleCpuMs ms over 10 s (budget: ~0)" }

    # --- 3. Detail view and a per-game action. ---
    Invoke-Button $window 'Details for Dota 2'   # the cover itself would play
    Find-Button $window 'Back to all games' | Out-Null   # only exists on the detail view
    Start-Sleep -Seconds 2   # hero banner
    Assert-Running $widget 'opening the detail view'
    Shot '2-detail'

    Invoke-Button $window 'Add to favorites'
    Wait-Until { (Get-Content (Join-Path $dataDir 'library.json') -Raw) -match '"Favorite":\s*true' } 5 'favorite saved to library.json' | Out-Null
    Invoke-Button $window 'Back to all games'
    Find-Button $window 'More actions' | Out-Null
    Assert-Running $widget 'going back to the shelf'
    Shot '3-shelf-favorite'

    # --- 3a. Milestone 8: launcher badges, generated covers, cover size, sort. ---
    $imageType = New-Object System.Windows.Automation.PropertyCondition($UIA::ControlTypeProperty, [System.Windows.Automation.ControlType]::Image)
    $badges = @($window.FindAll($Scope::Descendants, $imageType) | ForEach-Object { $_.Current.Name } | Where-Object { $_ })
    $metrics += "Launcher badges on screen: $($badges.Count) ($((($badges | Group-Object | ForEach-Object { "$($_.Name) x$($_.Count)" }) -join ', ')))"
    if (-not ($badges -contains 'Steam')) { Fail 'No Steam launcher badge found on the covers' }
    if ($badges -contains 'Manual' -or $badges -contains 'Shortcut') { Fail 'A manually added game got a launcher badge' }
    # The file name carries a version suffix (manual_loop_icon2.png), so match any.
    $iconCache = Join-Path $env:LOCALAPPDATA 'Binder\covers\manual_loop_icon*.png'
    Wait-Until { Test-Path $iconCache } 10 'generated icon cover cached for the manual game' | Out-Null
    $metrics += 'Generated cover: icon of the manual game cached'

    Open-Settings $window
    Select-CoverSize $window 'List'
    Invoke-Button $window 'Back to all games'
    Start-Sleep -Milliseconds 800
    Shot '13-list-view'
    Open-Settings $window
    Select-CoverSize $window 'Small'
    Invoke-Button $window 'Back to all games'
    Start-Sleep -Milliseconds 800
    Shot '14-small-covers'
    Open-Settings $window
    Select-CoverSize $window 'Medium'
    Wait-Until { (Get-Content (Join-Path $dataDir 'settings.json') -Raw) -match '"GridDensity":\s*"Medium"' } 5 'cover size saved' | Out-Null
    Invoke-Button $window 'Back to all games'

    # The sort button's name says the current order ("Sort: Recently played"), so find it by its x:Name.
    $sortButton = Find-ById $window 'SortButton'
    $pattern = $null
    if ($sortButton.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref] $pattern)) { $pattern.Expand() }
    else { $sortButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
    Write-Host 'clicked: sort button'
    Start-Sleep -Milliseconds 500
    Shot '15-sort-menu'
    $byName = Wait-Until { $UIA::RootElement.FindFirst($Scope::Descendants, (New-Object System.Windows.Automation.PropertyCondition($UIA::NameProperty, ('Name (A' + [char]0x2013 + 'Z)')))) } 5 'sort menu item'
    # A radio menu item: depending on the WinUI version it offers Invoke, Toggle or SelectionItem.
    $pattern = $null
    if ($byName.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref] $pattern)) { $pattern.Invoke() }
    elseif ($byName.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref] $pattern)) { $pattern.Toggle() }
    elseif ($byName.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref] $pattern)) { $pattern.Select() }
    else { Fail "Sort menu item 'Name' offers no Invoke, Toggle or SelectionItem pattern ($($byName.GetSupportedPatterns() | ForEach-Object { $_.ProgrammaticName }))" }
    Wait-Until { (Get-Content (Join-Path $dataDir 'settings.json') -Raw) -match '"SortBy":\s*"Name"' } 5 'sort order saved' | Out-Null
    $metrics += 'Cover size (List, Small, Medium) and sort by name: applied and saved'

    # --- 3a. The top slot: switch from Continue playing to This week (a session this week exists); it's saved. ---
    $weekItem = Wait-Until { $window.FindFirst($Scope::Descendants, (New-Object System.Windows.Automation.PropertyCondition($UIA::NameProperty, 'This week'))) } 10 "top slot switch 'This week'"
    $pattern = $null
    if ($weekItem.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref] $pattern)) { $pattern.Select() }
    elseif ($weekItem.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref] $pattern)) { $pattern.Invoke() }
    else { Fail "Top slot switch offers no SelectionItem or Invoke pattern ($($weekItem.GetSupportedPatterns() | ForEach-Object { $_.ProgrammaticName }))" }
    Wait-Until { (Get-Content (Join-Path $dataDir 'settings.json') -Raw) -match '"SpotlightShowsWeek":\s*true' } 5 'top slot choice saved' | Out-Null
    Start-Sleep -Milliseconds 300
    Shot '16-top-slot-week'
    $metrics += 'Top slot: switched to This week and saved'

    # --- 3b. Hide a game, then bring it back from Settings. Start with Windows writes a quoted Run entry. ---
    Invoke-Button $window 'Details for Team Fortress 2'
    Invoke-Button $window 'Hide from shelf'
    Wait-Until { (Get-Game 'steam:440').Hidden -eq $true } 5 'Team Fortress 2 hidden in library.json' | Out-Null
    Open-Settings $window
    Find-Button $window 'Refresh library' | Out-Null
    Start-Sleep -Milliseconds 500
    Shot '6-settings'

    Invoke-Button $window 'Show hidden games'   # the Hidden games card opens (board 19)
    Invoke-Button $window 'Show Team Fortress 2'
    Wait-Until { (Get-Game 'steam:440').Hidden -ne $true } 5 'Team Fortress 2 shown again' | Out-Null
    $metrics += 'Hide and show again from Settings: yes'

    $autostart = Find-Element $window 'Start with Windows' ([System.Windows.Automation.ControlType]::Button)
    $autostart.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    $expected = '"' + $Exe + '" --background'
    Wait-Until { (Get-ItemProperty $runKey -ErrorAction SilentlyContinue).'Binder' -eq $expected } 5 "Run key set to $expected" | Out-Null
    $autostart.SetFocus()   # scrolls the bottom of the page into view
    Start-Sleep -Milliseconds 500
    Shot '7-settings-bottom'
    $autostart.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Wait-Until { -not (Get-ItemProperty $runKey -ErrorAction SilentlyContinue).'Binder' } 5 'Run key removed' | Out-Null
    $metrics += 'Start with Windows: quoted Run entry added and removed'

    Invoke-Button $window 'Back to all games'
    Find-Button $window 'More actions' | Out-Null

    # --- 3c. Desktop layer: the widget stays behind other windows, even when clicked, and shows on Win+D. ---
    $hwnd = [IntPtr]$window.Current.NativeWindowHandle
    $progman = [Win]::FindByClass('Progman')
    $metrics += "Z-order at start: widget $([Win]::ZIndex($hwnd)), desktop (Progman) $([Win]::ZIndex($progman))"
    if ([Win]::ZIndex($hwnd) -gt [Win]::ZIndex($progman)) { Write-Host '::warning::Widget is below the desktop window' }

    # Another app's window (a plain WinForms window in a separate process), overlapping the widget's lower left.
    $otherApp = Start-Process powershell -WindowStyle Hidden -PassThru -ArgumentList '-NoProfile', '-Command',
        'Add-Type -AssemblyName System.Windows.Forms; $f = New-Object Windows.Forms.Form; $f.Text = ''Other app''; [Windows.Forms.Application]::Run($f)'
    $otherHwnd = Wait-Until { $h = [Win]::FindByTitle('Other app'); if ($h -ne [IntPtr]::Zero) { $h } } 20 'other app window'
    [Win]::ShowWindow($otherHwnd, 1) | Out-Null   # SW_SHOWNORMAL: the hidden PowerShell host would hide it
    [Win]::MoveWindow($otherHwnd, 300, 300, 500, 400, $true) | Out-Null
    Start-Sleep -Seconds 1
    $behind = [Win]::ZIndex($hwnd) -gt [Win]::ZIndex($otherHwnd)
    Shot '8-behind-window'

    # A real mouse click on an empty, uncovered part of the widget (right of the greeting) activates it.
    $rect = $window.Current.BoundingRectangle
    [Win]::Click([int]($rect.Left + 250), [int]($rect.Top + 40))
    Start-Sleep -Seconds 1
    $stillBehind = [Win]::ZIndex($hwnd) -gt [Win]::ZIndex($otherHwnd)
    $metrics += "Desktop layer: behind a new window: $behind, still behind after clicking the widget: $stillBehind"
    if (-not ($behind -and $stillBehind)) { Fail "Desktop layer: the widget came in front of another window (behind: $behind, after click: $stillBehind)" }

    [Win]::WinD()
    Start-Sleep -Seconds 2
    $fg = [Win]::GetForegroundWindow()
    $metrics += "Win+D: widget visible: $([Win]::IsWindowVisible($hwnd)), minimized: $([Win]::IsIconic($hwnd)), z-order widget $([Win]::ZIndex($hwnd)) vs foreground $([Win]::ZIndex($fg)) ($([Win]::ClassName($fg)))"
    $metrics += "Win+D: top visible windows: $([Win]::TopVisible(8, $hwnd))"
    Shot '9-win-d'
    [Win]::WinD()   # restore
    Start-Sleep -Seconds 1
    Stop-Process -Id $otherApp.Id -Force -ErrorAction SilentlyContinue
    Assert-Running $widget 'desktop layer checks'

    # --- 3d. Import desktop shortcuts through the picker, then put them back. ---
    Open-Settings $window
    Invoke-Button $window 'Import desktop shortcuts'
    $importButton = Find-ById $window 'ImportButton'
    Start-Sleep -Milliseconds 500
    Shot '10-import-picker'
    if ($importButton.Current.Name -ne 'Import 2 shortcuts') { Fail "Picker pre-selected the wrong shortcuts: '$($importButton.Current.Name)'" }
    $importButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Wait-Until { -not (Test-Path (Join-Path $desktop 'Loop Shortcut.lnk')) -and -not (Test-Path (Join-Path $desktop 'Dota 2.url')) } 5 'imported shortcuts moved off the desktop' | Out-Null
    if (-not (Test-Path (Join-Path $desktop 'Example.url'))) { Fail 'An unticked shortcut was moved' }
    $shortcutGames = @((Get-Content (Join-Path $dataDir 'library.json') -Raw | ConvertFrom-Json) | Where-Object { $_.Source -eq 'Shortcut' })
    if ($shortcutGames.Count -ne 1 -or $shortcutGames[0].Name -ne 'Loop Shortcut') { Fail "Expected one shortcut game (the Steam link merges), got $($shortcutGames.Count)" }
    Find-Button $window 'Loop Shortcut' | Out-Null
    Start-Sleep -Milliseconds 500
    Shot '11-imported'
    $metrics += 'Shortcut import: 2 moved off the desktop, Steam link merged, web link left alone'

    Open-Settings $window
    Invoke-Button $window 'Show desktop shortcut options'
    Invoke-Button $window 'Restore shortcuts to desktop'
    Wait-Until { (Test-Path (Join-Path $desktop 'Loop Shortcut.lnk')) -and (Test-Path (Join-Path $desktop 'Dota 2.url')) } 5 'shortcuts restored to the desktop' | Out-Null
    if (@((Get-Content (Join-Path $dataDir 'library.json') -Raw | ConvertFrom-Json) | Where-Object { $_.Source -eq 'Shortcut' }).Count -ne 0) {
        Fail 'Restored shortcuts are still on the shelf'
    }
    $metrics += 'Shortcut restore: both back on the desktop, removed from the shelf'
    Invoke-Button $window 'Back to all games'
    Find-Button $window 'More actions' | Out-Null

    # --- 3e. Auto-hide taskbar: it reserves no space but slides over everything when it pops up, so the
    # panel must leave room for it. ---
    $tray = [Win]::FindByClass('Shell_TrayWnd')
    $trayRect = [Win]::Rect($tray)
    $trayHeight = $trayRect.Bottom - $trayRect.Top
    $screenBottom = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds.Bottom
    $beforeBottom = ([Win]::Rect($hwnd)).Bottom
    [Win]::SetTaskbarAutoHide($tray, $true)
    # The bug only shows once Windows gives the taskbar's strip back to the work area; wait for that.
    Wait-Until { [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea.Bottom -gt $screenBottom - $trayHeight } 10 'work area to grow when the taskbar auto-hides' | Out-Null
    Start-Sleep -Seconds 2   # time for the widget to handle the setting change
    $autoHideBottom = ([Win]::Rect($hwnd)).Bottom
    Shot '12-autohide-taskbar'
    if ($autoHideBottom -gt $screenBottom - $trayHeight) {
        [Win]::SetTaskbarAutoHide($tray, $false)
        Fail "With an auto-hide taskbar the widget reaches into its strip (bottom $autoHideBottom, taskbar starts at $($screenBottom - $trayHeight))"
    }
    [Win]::SetTaskbarAutoHide($tray, $false)
    Start-Sleep -Seconds 2
    $metrics += "Auto-hide taskbar: widget bottom $beforeBottom -> $autoHideBottom (screen $screenBottom, taskbar $trayHeight px), back to $(([Win]::Rect($hwnd)).Bottom) when turned off"

    # --- 4. Launch -> hide -> return, with the stand-in game (hide delay 0 for this game). ---
    # Clicking the cover plays it (the detail view's Play button is the same Launcher path).
    Invoke-Button $window 'Loop Test'
    Wait-Until { $widget.Refresh(); $widget.HasExited } 10 'widget to exit after launching the game' | Out-Null
    $metrics += 'Widget exited after launch: yes'

    $game = Wait-Until { Get-Process FakeGame -ErrorAction SilentlyContinue } 10 'stand-in game running'
    $watcher = Wait-Until { Get-Process Binder.Watcher -ErrorAction SilentlyContinue } 10 'watcher running'
    Start-Sleep -Seconds 4   # past detection: the watcher should now be blocked in a kernel wait
    $watcher.Refresh()
    $w0 = $watcher.TotalProcessorTime
    Start-Sleep -Seconds 5
    $watcher.Refresh()
    $watcherCpu = [int]($watcher.TotalProcessorTime - $w0).TotalMilliseconds
    $watcherPws = Get-PrivateWorkingSetMB $watcher
    $metrics += "Watcher while game runs: $([math]::Round($watcher.WorkingSet64 / 1MB, 1)) MB working set, $([math]::Round($watcher.PrivateMemorySize64 / 1MB, 1)) MB private, $watcherPws MB private working set, CPU over 5 s: $watcherCpu ms ($watcherBuild build)"
    if ($watcherBuild -eq 'Native AOT') { $siteNumbers.watcherMB = $watcherPws; $siteNumbers.watcherCpuMs = $watcherCpu }
    if ($watcherBuild -eq 'Native AOT' -and $watcher.PrivateMemorySize64 -gt 3MB) {
        # Private bytes: the watcher's own memory. Working set also counts shared Windows DLL pages.
        Write-Host "::warning::Watcher private memory is $([math]::Round($watcher.PrivateMemorySize64 / 1MB, 1)) MB (budget: ~3 MB)"
    }
    Shot '4-during-game'

    # The game exits after 15 s; the watcher waits a 3 s restart grace, logs the session and relaunches the widget.
    $returned = Wait-Until { Get-Process Binder -ErrorAction SilentlyContinue | Where-Object { $_.Id -ne $widget.Id } } 60 'widget to come back after the game closed'
    $returned = $returned | Select-Object -First 1
    $script:returnedProcess = $returned
    Wait-Until { (Get-ChildItem $dataDir -Filter 'sessions-*.jsonl' | Get-Content -Raw) -match 'manual:loop' } 5 'session written by the watcher' | Out-Null
    $metrics += 'Widget came back and the session was recorded: yes'
    Get-WidgetWindow $returned.Id | Out-Null
    Start-Sleep -Seconds 3
    Assert-Running $returned 'after coming back'
    Shot '5-returned'

    # --- 5. Milestone 9: first-run setup, then the flyout (the default mode) and the stay-ready footprint. ---
    Stop-Process -Id $returned.Id -Force
    $returned.WaitForExit()
    $script:returnedProcess = $null
    ConvertTo-Json -InputObject @{ HideDelaySeconds = 10 } | Set-Content -Encoding UTF8 (Join-Path $dataDir 'settings.json')
    $widget = Start-Process $Exe -PassThru
    $window = Get-WidgetWindow $widget.Id
    Find-Button $window 'Skip setup' | Out-Null
    Start-Sleep -Seconds 2
    Shot '16-setup-launchers'
    Invoke-Button $window 'Next'
    Find-Button $window 'Not now' | Out-Null
    Start-Sleep -Seconds 2   # the desktop scan for the picker
    Shot '17-setup-desktop'
    Invoke-Button $window 'Not now'
    Find-Button $window 'Done' | Out-Null
    Start-Sleep -Milliseconds 500
    Shot '18-setup-mode'
    Invoke-Button $window 'Done'
    Wait-Until { (Get-Content (Join-Path $dataDir 'settings.json') -Raw) -match '"SetupDone":\s*true' } 5 'setup saved' | Out-Null
    if ((Get-Content (Join-Path $dataDir 'settings.json') -Raw) -notmatch '"DisplayMode":\s*"Flyout"') { Fail 'Setup did not keep the flyout as the default mode' }
    Find-Button $window 'More actions' | Out-Null
    Start-Sleep -Milliseconds 800
    Shot '19-flyout-open'
    $metrics += 'First-run setup: three steps, saved, flyout kept as the default'

    # Shortcut closes it; the process stays, with the panel gone.
    $pid9 = $widget.Id
    [Win]::WinShiftG()
    Wait-Until { [Win]::VisibleWindowOf($pid9) -eq [IntPtr]::Zero } 5 'flyout to close on Win+Shift+G' | Out-Null
    Start-Sleep -Seconds 5   # compaction and trimming after close
    Assert-Running $widget 'closing the flyout'
    $widget.Refresh()
    Add-MemoryReport 'closed after use'
    $closedPws = Get-PrivateWorkingSetMB $widget
    $metrics += "Stay ready (closed after use): working set $([math]::Round($widget.WorkingSet64 / 1MB, 1)) MB, private $([math]::Round($widget.PrivateMemorySize64 / 1MB, 1)) MB, private working set $closedPws MB"
    $siteNumbers.closedMB = $closedPws
    $c0 = $widget.TotalProcessorTime
    Start-Sleep -Seconds 10
    $widget.Refresh()
    $closedCpu = [int]($widget.TotalProcessorTime - $c0).TotalMilliseconds
    $metrics += "Stay ready: CPU over 10 s while closed: $closedCpu ms"
    $siteNumbers.closedCpuMs = $closedCpu
    if ($closedCpu -gt 100) { Write-Host "::warning::CPU while closed was $closedCpu ms over 10 s (budget: ~0)" }

    # Open with the shortcut and time it, then Esc closes.
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    [Win]::WinShiftG()
    Wait-Until { [Win]::VisibleWindowOf($pid9) -ne [IntPtr]::Zero } 5 'flyout to open on Win+Shift+G' | Out-Null
    $openMs = $sw.ElapsedMilliseconds
    Start-Sleep -Milliseconds 600
    Shot '20-flyout-hotkey'
    $metrics += "Time to open (Win+Shift+G, warm process): $openMs ms to visible"
    [Win]::Esc()
    Wait-Until { [Win]::VisibleWindowOf($pid9) -eq [IntPtr]::Zero } 5 'flyout to close on Esc' | Out-Null
    $metrics += 'Esc closes the flyout: yes'

    # Click-away closes it.
    [Win]::WinShiftG()
    Wait-Until { [Win]::VisibleWindowOf($pid9) -ne [IntPtr]::Zero } 5 'flyout to open again' | Out-Null
    Start-Sleep -Milliseconds 800
    $screen = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    [Win]::Click([int]($screen.Left + $screen.Width / 4), [int]($screen.Top + $screen.Height / 2))
    Wait-Until { [Win]::VisibleWindowOf($pid9) -eq [IntPtr]::Zero } 5 'flyout to close when clicking the desktop' | Out-Null
    $metrics += 'Click-away closes the flyout: yes'

    # A second start (taskbar pin, Start menu) toggles the running one instead of starting another.
    Start-Sleep -Milliseconds 600   # past the reopen guard
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $second = Start-Process $Exe -PassThru
    Wait-Until { [Win]::VisibleWindowOf($pid9) -ne [IntPtr]::Zero } 10 'second start to open the flyout' | Out-Null
    $metrics += "Time to open (second start forwarding to the running one): $($sw.ElapsedMilliseconds) ms"
    if (-not $second.WaitForExit(5000)) { Fail 'The second instance kept running' }
    $second = Start-Process $Exe -PassThru
    Wait-Until { [Win]::VisibleWindowOf($pid9) -eq [IntPtr]::Zero } 10 'second start to close the flyout' | Out-Null
    $second.WaitForExit(5000) | Out-Null
    if (@(Get-Process Binder -ErrorAction SilentlyContinue).Count -ne 1) { Fail 'More than one Binder process is running' }
    $metrics += 'Second start toggles the running instance: yes'

    # Started with Windows (--background): no window, waits in the tray at the lowest footprint.
    Stop-Process -Id $pid9 -Force
    $widget.WaitForExit()
    $widget = Start-Process $Exe -ArgumentList '--background' -PassThru
    Start-Sleep -Seconds 6
    Assert-Running $widget 'starting with --background'
    if ([Win]::VisibleWindowOf($widget.Id) -ne [IntPtr]::Zero) { Fail '--background showed a window' }
    $widget.Refresh()
    Add-MemoryReport 'never opened'
    $metrics += "Stay ready (started with Windows, never opened): working set $([math]::Round($widget.WorkingSet64 / 1MB, 1)) MB, private $([math]::Round($widget.PrivateMemorySize64 / 1MB, 1)) MB"
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    [Win]::WinShiftG()
    Wait-Until { [Win]::VisibleWindowOf($widget.Id) -ne [IntPtr]::Zero } 10 'first open after --background' | Out-Null
    $metrics += "Time to open (first open after starting with Windows): $($sw.ElapsedMilliseconds) ms to visible"
    Start-Sleep -Seconds 4
    $widget.Refresh()
    Add-MemoryReport 'open'
    $openPws = Get-PrivateWorkingSetMB $widget
    $metrics += "Flyout open: working set $([math]::Round($widget.WorkingSet64 / 1MB, 1)) MB, private $([math]::Round($widget.PrivateMemorySize64 / 1MB, 1)) MB, private working set $openPws MB"
    $siteNumbers.openMB = $openPws
    $siteNumbers.games = @((Get-Content (Join-Path $dataDir 'library.json') -Raw | ConvertFrom-Json) | ForEach-Object { $_ }).Count
    Shot '21-flyout-after-background'
}
finally {
    # Diagnostics for a failed run: the app's own error log, the returned widget's state, and Windows' crash
    # events (.NET Runtime 1026 has the stack trace, Application Error 1000 the faulting module).
    Copy-Item (Join-Path $env:LOCALAPPDATA 'Binder\error.log') $art -ErrorAction SilentlyContinue
    if ($script:returnedProcess) {
        $script:returnedProcess.Refresh()
        $metrics += "Returned widget: exited $($script:returnedProcess.HasExited)$(if ($script:returnedProcess.HasExited) { ", exit code $($script:returnedProcess.ExitCode)" })"
    }
    try {
        Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $testStart } -ErrorAction Stop |
            Where-Object { $_.ProviderName -in '.NET Runtime', 'Application Error', 'Windows Error Reporting' } |
            Format-List TimeCreated, ProviderName, Id, Message | Out-File (Join-Path $art 'crash-events.txt') -Width 400
    } catch { }
    $metrics | ForEach-Object { Write-Host $_ }
    $metrics | Set-Content (Join-Path $art 'metrics.txt')
    # The test machine, for the website's "Measured on" line.
    try {
        $os = Get-CimInstance Win32_OperatingSystem
        $siteNumbers.windows = $os.Caption -replace '^Microsoft\s+', ''
        $siteNumbers.cores = (Get-CimInstance Win32_ComputerSystem).NumberOfLogicalProcessors
        $siteNumbers.ramGB = [int][math]::Round($os.TotalVisibleMemorySize / 1MB)
    } catch { }
    $siteNumbers | ConvertTo-Json | Set-Content (Join-Path $art 'site-numbers.json')
    Copy-Item (Join-Path $dataDir '*.json') $art -ErrorAction SilentlyContinue
    Get-Process Binder, Binder.Watcher, FakeGame -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Remove-ItemProperty $runKey 'Binder' -ErrorAction SilentlyContinue
    Remove-Item $gogKey -Recurse -ErrorAction SilentlyContinue
    try { [Win]::SetTaskbarAutoHide([Win]::FindByClass('Shell_TrayWnd'), $false) } catch { }
}
