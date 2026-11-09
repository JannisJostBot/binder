# CI test for GameShelfSetup.exe (Windows PowerShell 5.1): install through the dialogs, update silently while the
# widget and a watcher are running, then uninstall through the dialogs with "delete my data" ticked, and check
# that nothing is left behind and imported shortcuts went back on the desktop.
#
# Installs into the current user's profile and deletes Game Shelf's data at the end: CI machines only.
param(
    [Parameter(Mandatory = $true)] [string] $Setup,
    [Parameter(Mandatory = $true)] [string] $Published   # publish/app, to compare the installed files with
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing

$Setup = (Resolve-Path $Setup).Path
$Published = (Resolve-Path $Published).Path
$art = Join-Path (Get-Location) 'artifacts'
New-Item -ItemType Directory -Force $art | Out-Null
$metrics = @()
$UIA = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]

$installDir = Join-Path $env:LOCALAPPDATA 'Programs\GameShelf'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\GameShelf'
$startMenuLink = Join-Path ([Environment]::GetFolderPath('Programs')) 'Game Shelf.lnk'
$dataDir = Join-Path $env:APPDATA 'GameShelf'
$cacheDir = Join-Path $env:LOCALAPPDATA 'GameShelf'
$desktop = [Environment]::GetFolderPath('Desktop')
$log = Join-Path ([IO.Path]::GetTempPath()) 'GameShelfSetup.log'

function Shot([string] $name) {
    $b = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($b.Left, $b.Top, 0, 0, $bmp.Size)
    $bmp.Save((Join-Path $art "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host "screenshot: $name.png"
}
function Fail([string] $message) { try { Shot 'setup-failure' } catch { }; throw $message }
function Wait-Until([scriptblock] $condition, [int] $seconds, [string] $what) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $result = & $condition
        if ($result) { return $result }
        Start-Sleep -Milliseconds 300
    }
    Fail "Timed out after $seconds s waiting for: $what"
}
# The task dialog: a top-level window titled "Game Shelf Setup" whose text includes $instruction.
function Get-Dialog([string] $instruction) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($UIA::NameProperty, 'Game Shelf Setup')
    Wait-Until {
        foreach ($w in $UIA::RootElement.FindAll($Scope::Children, $cond)) {
            $text = New-Object System.Windows.Automation.PropertyCondition($UIA::NameProperty, $instruction)
            if ($w.FindFirst($Scope::Descendants, $text)) { return $w }
        }
        $script:lastWait = $instruction
    } 20 "setup dialog '$instruction'"
}
# For a failed run: every top-level window of the setup processes (the task dialog may be hidden or off-screen).
function Dump-SetupWindows {
    $ids = @(Get-Process GameShelfSetup, Uninstall -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
    $lines = @("setup processes: $($ids -join ', ')")
    $UIA::RootElement.FindAll($Scope::Children, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object {
        if ($ids -contains $_.Current.ProcessId) {
            $lines += '{0} | {1} | pid {2} | {3} | offscreen={4}' -f $_.Current.Name, $_.Current.ClassName, $_.Current.ProcessId, $_.Current.BoundingRectangle, $_.Current.IsOffscreen
        }
    }
    $lines | Set-Content (Join-Path $art 'setup-windows.txt')
}
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Keys {
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    public static void Press(byte vk) { keybd_event(vk, 0, 0, UIntPtr.Zero); keybd_event(vk, 0, 2, UIntPtr.Zero); }
}
// The install/update window is a classic Win32 dialog. Windows PowerShell's managed UIA client sees its controls only
// as plain panes (no Toggle/Invoke, not focusable), so they are driven with the messages a click would send:
// set the check state, then tell the dialog the control was clicked (WM_COMMAND BN_CLICKED), as the button itself does.
public static class Win32Button {
    [DllImport("user32.dll")] static extern IntPtr SendMessageW(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int GetDlgCtrlID(IntPtr hwnd);
    const uint WM_COMMAND = 0x0111, BM_GETCHECK = 0xF0, BM_SETCHECK = 0xF1;
    public static bool IsChecked(IntPtr button) { return SendMessageW(button, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero) == (IntPtr)1; }
    public static void Check(IntPtr box) {
        SendMessageW(box, BM_SETCHECK, (IntPtr)1, IntPtr.Zero);
        SendMessageW(GetParent(box), WM_COMMAND, (IntPtr)(GetDlgCtrlID(box) & 0xFFFF), box);
    }
    // Posted: the dialog's handler for this button runs the install, which shouldn't block the test.
    public static void Click(IntPtr button) {
        PostMessageW(GetParent(button), WM_COMMAND, (IntPtr)(GetDlgCtrlID(button) & 0xFFFF), button);
    }
}
'@
# Everything in a window, for a failed run: name, control type, class and whether it can be invoked.
function Dump([object] $root, [string] $file) {
    $root.FindAll($Scope::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object {
        '{0} | {1} | {2} | invoke={3} toggle={4}' -f $_.Current.Name, $_.Current.ControlType.ProgrammaticName, $_.Current.ClassName,
            $_.GetCurrentPropertyValue($UIA::IsInvokePatternAvailableProperty), $_.GetCurrentPropertyValue($UIA::IsTogglePatternAvailableProperty)
    } | Set-Content (Join-Path $art $file)
}
# Clicks the element named $name: through its Invoke pattern, or by focusing it and pressing Space.
function Click([object] $root, [string] $name) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($UIA::NameProperty, $name)
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline) {
        foreach ($el in $root.FindAll($Scope::Descendants, $cond)) {
            if ($el.GetCurrentPropertyValue($UIA::IsInvokePatternAvailableProperty)) {
                $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
                Write-Host "clicked: $name"
                return
            }
            if ($el.Current.IsKeyboardFocusable) {
                $el.SetFocus()
                [Keys]::Press(0x20)
                Write-Host "clicked (keyboard): $name"
                return
            }
        }
        Start-Sleep -Milliseconds 300
    }
    Dump $root 'setup-dialog-tree.txt'
    Fail "No clickable '$name' in the dialog (tree in setup-dialog-tree.txt)"
}
function Ours([string] $exe) {
    @(Get-Process ([IO.Path]::GetFileNameWithoutExtension($exe)) -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq (Join-Path $installDir $exe) -or $_.Path -eq ((Join-Path $installDir $exe) + '.old') })
}
function Same-File([string] $name) {
    (Get-FileHash (Join-Path $installDir $name)).Hash -eq (Get-FileHash (Join-Path $Published $name)).Hash
}

# Start clean: the smoke test ran the portable copy; make sure nothing of it is running.
Get-Process GameShelf, GameShelf.Watcher, FakeGame -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Remove-Item $installDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $uninstallKey -Recurse -ErrorAction SilentlyContinue
Remove-Item $log -ErrorAction SilentlyContinue
(New-Object -ComObject Shell.Application).MinimizeAll()

try {
    $metrics += "GameShelfSetup.exe: $([int]((Get-Item $Setup).Length / 1KB)) KB"

    # --- 1. Install through the dialogs; "Open Game Shelf now" is ticked, so it starts afterwards. ---
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $proc = Start-Process $Setup -PassThru
    $dialog = Get-Dialog 'Install Game Shelf'
    $metrics += "Setup dialog shown after $($sw.ElapsedMilliseconds) ms"
    Start-Sleep -Milliseconds 500
    Shot 'setup-1-install'
    # The disclaimer: Install stays disabled until "I've read and understood the disclaimer" is ticked.
    $named = { param($name) $dialog.FindFirst($Scope::Descendants, (New-Object System.Windows.Automation.PropertyCondition($UIA::NameProperty, $name))) }
    $installButton = Wait-Until { & $named 'Install' } 5 'Install button'
    if ($installButton.Current.IsEnabled) { Fail 'Install is enabled before the disclaimer is confirmed' }
    $accept = Wait-Until { & $named "I've read and understood the disclaimer" } 5 'disclaimer checkbox'
    $acceptHwnd = [IntPtr] $accept.Current.NativeWindowHandle
    if ($accept.Current.ClassName -ne 'Button' -or $acceptHwnd -eq [IntPtr]::Zero) { Fail "The disclaimer checkbox is a $($accept.Current.ClassName), not a Win32 button" }
    if ([Win32Button]::IsChecked($acceptHwnd)) { Fail 'The disclaimer checkbox is ticked from the start' }
    [Win32Button]::Check($acceptHwnd)
    Wait-Until { $installButton.Current.IsEnabled } 5 'Install enabled after confirming the disclaimer' | Out-Null
    $metrics += "Disclaimer: Install disabled until confirmed"
    $sw.Restart()
    [Win32Button]::Click([IntPtr] $installButton.Current.NativeWindowHandle)
    Write-Host 'clicked: Install'
    $done = Get-Dialog 'Game Shelf is installed'
    $metrics += "Install took $($sw.ElapsedMilliseconds) ms (to the done dialog)"
    Start-Sleep -Milliseconds 500
    Shot 'setup-2-installed'
    Click $done 'Close'
    if (-not $proc.WaitForExit(10000)) { Fail 'Setup kept running after Close' }
    if ($proc.ExitCode -ne 0) { Fail "Setup exit code $($proc.ExitCode)" }

    # Every file of the app folder, at the same place: the WinUI widget is a folder, not a single exe.
    foreach ($file in Get-ChildItem $Published -Recurse -File) {
        $name = $file.FullName.Substring($Published.TrimEnd([char]92).Length + 1)
        if (-not (Test-Path (Join-Path $installDir $name)) -or -not (Same-File $name)) { Fail "$name differs from the published one" }
    }
    if (-not (Test-Path (Join-Path $installDir 'installed-files.txt'))) { Fail 'installed-files.txt missing' }
    if (-not (Test-Path (Join-Path $installDir 'Uninstall.exe'))) { Fail 'Uninstall.exe missing' }
    if (-not (Test-Path $startMenuLink)) { Fail 'Start menu shortcut missing' }
    $entry = Get-ItemProperty $uninstallKey
    foreach ($v in 'DisplayName', 'DisplayVersion', 'Publisher', 'DisplayIcon', 'UninstallString', 'QuietUninstallString', 'EstimatedSize') {
        if (-not $entry.$v) { Fail "Settings -> Apps entry has no $v" }
    }
    if ($entry.UninstallString -ne ('"' + (Join-Path $installDir 'Uninstall.exe') + '" /uninstall')) { Fail "UninstallString is $($entry.UninstallString)" }
    $metrics += "Installed: files match the published build, Start menu shortcut, Settings -> Apps entry '$($entry.DisplayName) $($entry.DisplayVersion)' by $($entry.Publisher), $($entry.EstimatedSize) KB"

    $widget = Wait-Until { Ours 'GameShelf.exe' | Select-Object -First 1 } 15 'installed widget started from the done dialog'
    Start-Sleep -Seconds 3
    Shot 'setup-3-opened'
    $metrics += 'Opened after install: yes'

    # --- 2. Update while the widget and a watcher (a game being tracked) are running. ---
    $watcher = Start-Process (Join-Path $installDir 'GameShelf.Watcher.exe') -PassThru -ArgumentList '--game', 'manual:none', '--process', 'NoSuchGame', '--timeout', '300', '--return', '0'
    Start-Sleep -Seconds 1
    $sw.Restart()
    $update = Start-Process $Setup -ArgumentList '/S' -PassThru
    if (-not $update.WaitForExit(60000)) { Fail 'Silent update did not finish' }
    $metrics += "Silent update with the app open and a game running: exit code $($update.ExitCode), $($sw.ElapsedMilliseconds) ms"
    if ($update.ExitCode -ne 0) { Fail "Update exit code $($update.ExitCode)" }
    $widget.Refresh()
    if (-not $widget.HasExited) { Fail 'The old widget is still running after the update' }
    $restarted = Wait-Until { Ours 'GameShelf.exe' | Select-Object -First 1 } 15 'widget restarted after the update'
    $watcher.Refresh()
    if ($watcher.HasExited) { Fail 'The update stopped the running watcher' }
    if (-not (Same-File 'GameShelf.Watcher.exe')) { Fail 'The watcher file was not replaced' }
    if (-not ((Get-Content $log -Raw) -match 'GameShelf.Watcher.exe is in use; moving it aside')) { Fail 'The in-use watcher was not moved aside' }
    $metrics += "Update: old widget quit and came back (pid $($widget.Id) -> $($restarted.Id)), watcher kept running on its old file, new file in place"

    # An imported shortcut, to check that uninstalling puts it back on the desktop.
    $stored = Join-Path $dataDir 'Shortcuts\Setup Test.url'
    New-Item -ItemType Directory -Force (Split-Path $stored) | Out-Null
    "[InternetShortcut]`r`nURL=https://example.com/" | Set-Content -Encoding ASCII $stored
    $original = Join-Path $desktop 'Setup Test.url'
    ConvertTo-Json -InputObject @(@{ StoredPath = $stored; OriginalPath = $original; Copied = $false }) |
        Set-Content -Encoding UTF8 (Join-Path $dataDir 'imported-shortcuts.json')
    Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' 'Game Shelf' ('"' + (Join-Path $installDir 'GameShelf.exe') + '" --background')

    # --- 3. Uninstall through the dialogs, deleting the data too. ---
    $proc = Start-Process (Join-Path $installDir 'Uninstall.exe') -ArgumentList '/uninstall' -PassThru
    $dialog = Get-Dialog 'Uninstall Game Shelf?'
    $check = Wait-Until { $dialog.FindFirst($Scope::Descendants, (New-Object System.Windows.Automation.PropertyCondition($UIA::NameProperty, 'Also delete my game list, play history and settings'))) } 5 'delete-data checkbox'
    if ($check.GetCurrentPropertyValue($UIA::IsTogglePatternAvailableProperty)) {
        $check.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    } else {
        $check.SetFocus(); [Keys]::Press(0x20)
    }
    Start-Sleep -Milliseconds 500
    Shot 'setup-4-uninstall'
    Click $dialog 'Uninstall'
    # The game is still being tracked: confirm.
    $running = Get-Dialog 'A game is still running'
    Shot 'setup-5-game-running'
    Click $running 'Uninstall anyway'
    $removed = Get-Dialog 'Game Shelf was removed'
    Start-Sleep -Milliseconds 500
    Shot 'setup-6-removed'
    Click $removed 'Close'

    Wait-Until { -not (Test-Path $installDir) } 15 'install folder removed' | Out-Null
    if (Test-Path $uninstallKey) { Fail 'The Settings -> Apps entry is still there' }
    if (Test-Path $startMenuLink) { Fail 'The Start menu shortcut is still there' }
    if ((Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -ErrorAction SilentlyContinue).'Game Shelf') { Fail 'Start with Windows is still set' }
    if (-not (Test-Path $original)) { Fail 'The imported shortcut did not go back on the desktop' }
    if ((Test-Path $dataDir) -or (Test-Path $cacheDir)) { Fail 'Game Shelf data is still there after "delete my data"' }
    if ((Ours 'GameShelf.exe').Count -or (Ours 'GameShelf.Watcher.exe').Count) { Fail 'Game Shelf is still running' }
    Wait-Until { -not (Get-ChildItem ([IO.Path]::GetTempPath()) -Directory -Filter 'GameShelf-uninstall-*') } 15 'temporary uninstaller removed' | Out-Null
    Remove-Item $original -ErrorAction SilentlyContinue
    $metrics += 'Uninstall: files, Start menu, Settings -> Apps entry, Run key and data gone; imported shortcut back on the desktop; temp copy removed'
}
finally {
    try { Dump-SetupWindows } catch { }
    Copy-Item $log (Join-Path $art 'setup.log') -ErrorAction SilentlyContinue
    $metrics | ForEach-Object { Write-Host $_ }
    $metrics | Set-Content (Join-Path $art 'setup-metrics.txt')
    Get-Process GameShelf, GameShelf.Watcher, GameShelfSetup, Uninstall -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}
