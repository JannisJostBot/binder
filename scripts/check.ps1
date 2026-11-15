<#
.SYNOPSIS
  Checks a change before it's committed: the project's hard rules, then the build and the unit tests.

.DESCRIPTION
  Compares the working tree (committed, staged, unstaged and new files) with the point where this branch left
  -Base (default origin/main) and fails on anything the project rules forbid. See AGENTS.md.

  Always checked:
    - no new NuGet packages (<PackageReference>) unless -AllowPackages (the owner approved it)
    - no [DllImport] (use [LibraryImport]) and no [ComImport] (use [GeneratedComInterface]): Native AOT
    - no {Binding} in XAML (use x:Bind): Native AOT
    - the website (site/) has no scripts and loads nothing from other servers

  Also checked with -Strict or -Task (work by agents from docs/tasks):
    - no changes to protected files (the watcher, the launcher, security checks, native code, the installer,
      project files, CI and the rules themselves)
    - no new timers, sleeps, delays, file watchers or endless loops, and no new native imports
    - no existing Check(...) removed or changed in the unit tests
    - with -Task: only the files listed under "## Files" in that task card are changed

.EXAMPLE
  ./scripts/check.ps1 -Task docs/tasks/T001-format-playtime-tests.md
.EXAMPLE
  ./scripts/check.ps1 -SkipBuild
#>
param(
    [string]$Base = 'origin/main',
    [string]$Task,
    [switch]$Strict,
    [switch]$SkipBuild,
    [switch]$AllowPackages
)

$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)   # the repo root
$problems = New-Object System.Collections.Generic.List[string]
function Problem([string]$text) { $problems.Add($text) | Out-Null; Write-Host "FAIL  $text" -ForegroundColor Red }
if ($Task) { $Strict = $true }

# --- What changed ---------------------------------------------------------------------------------------
$mergeBase = (& git merge-base HEAD $Base 2>$null)
if ($LASTEXITCODE -ne 0 -or -not $mergeBase) {
    Write-Host "Can't find where this branch left '$Base'. Run 'git fetch origin main' first." -ForegroundColor Red
    exit 2
}
$mergeBase = $mergeBase.Trim()

$changed = @(& git diff --name-only $mergeBase) + @(& git ls-files --others --exclude-standard) |
    Where-Object { $_ } | Sort-Object -Unique
Write-Host "Changed files since $($mergeBase.Substring(0, 8)) ($Base):"
if ($changed.Count -eq 0) { Write-Host '  (none)' } else { $changed | ForEach-Object { Write-Host "  $_" } }

# Added and removed lines per file, from the diff (tracked files) or the whole file (new files).
$added = @{}; $removed = @{}
foreach ($f in $changed) { $added[$f] = New-Object System.Collections.Generic.List[string]; $removed[$f] = New-Object System.Collections.Generic.List[string] }
$current = $null; $inHeader = $false
foreach ($line in (& git diff -U0 --no-color $mergeBase)) {
    # File headers run from "diff --git" to the first "@@" hunk line; only there do ---/+++ name the file.
    if ($line.StartsWith('diff --git ')) { $inHeader = $true; $current = $null; continue }
    if ($inHeader) {
        if ($line.StartsWith('@@')) { $inHeader = $false }
        elseif ($line.StartsWith('+++ b/')) { $current = $line.Substring(6) }
        elseif ($line.StartsWith('--- a/') -and -not $current) { $current = $line.Substring(6) }
        continue
    }
    if ($line.StartsWith('@@')) { continue }
    if (-not $current -or -not $added.ContainsKey($current)) { continue }
    if ($line.StartsWith('+')) { $added[$current].Add($line.Substring(1)) }
    elseif ($line.StartsWith('-')) { $removed[$current].Add($line.Substring(1)) }
}
foreach ($f in (& git ls-files --others --exclude-standard)) {
    if ($f -and (Test-Path -LiteralPath $f -PathType Leaf)) { Get-Content -LiteralPath $f | ForEach-Object { $added[$f].Add($_) } }
}

function Scan([string]$fileRegex, [string]$lineRegex, [string]$message) {
    foreach ($f in $changed) {
        if ($f -notmatch $fileRegex) { continue }
        foreach ($l in $added[$f]) {
            if ($l -match $lineRegex) { Problem "${f}: $message`n        $($l.Trim())" }
        }
    }
}

# --- Rules for every change -----------------------------------------------------------------------------
if (-not $AllowPackages) {
    Scan '\.(csproj|props|targets)$' '<PackageReference' 'new package: needs the owner''s approval first (CLAUDE.md, Dependencies policy)'
}
Scan '\.cs$' '\[\s*DllImport' 'use [LibraryImport] instead of [DllImport] (Native AOT)'
Scan '\.cs$' '\[\s*ComImport' 'use [GeneratedComInterface] instead of [ComImport] (Native AOT)'
Scan '\.xaml$' '\{\s*Binding' 'use x:Bind instead of {Binding} (Native AOT)'
# The website stays plain and private: no scripts, nothing loaded from other servers (links are fine).
Scan '^site/.*\.html?$' '<\s*(script|iframe|embed|object)\b' 'no scripts or embedded pages on the website'
Scan '^site/.*\.html?$' '<\s*(img|link|source|video|audio|track|image|use)\b[^>]*\b(src|href|srcset)\s*=\s*["'']?\s*(https?:)?//' 'the website loads nothing from other servers'
Scan '^site/.*\.(html?|css)$' '(@import|url\(\s*["'']?\s*(https?:)?//)' 'the website loads nothing from other servers'

# --- Rules for agent work -------------------------------------------------------------------------------
if ($Strict) {
    $protected = @(
        '^src/Binder\.Watcher/', '^src/Binder\.Setup/', '^src/Binder\.Elevated/',
        '^src/Binder/(Program|MessageWindow)\.cs$', '^src/Binder/app\.manifest$',
        '^src/Binder/Services/(Launcher|Native\w*|DesktopLayer|Monitors|Footprint|ShellImage[\w.]*|ShellNotify|PcChecks|PcInfo|CrashLog|Autostart|CoverCache|Storage|RunningGames|GameLogs|GameModeService)\.cs$',
        '^src/Binder/Library/(\w+Source|SourceDefaults|InstallFolder|ShortcutFile|ShortcutImporter|Vdf|LibraryScanner)\.cs$',
        '^src/Binder/Views/PanelBackdrop\.cs$',
        '^src/Binder/Library/UpdateManifest\.cs$', '^src/Binder/Services/(UpdateKeys|Updater)\.cs$', '^docs/releases/',
        '\.(csproj|sln|props|targets)$', '^scripts/', '^\.github/',
        '^tests/(smoke|setup-test)\.ps1$', '^tests/FakeGame/',
        '^(CLAUDE|AGENTS)\.md$', '^docs/(proposals|installer)\.md$', '^docs/tasks/'
    )
    foreach ($f in $changed) {
        foreach ($p in $protected) {
            if ($f -match $p) { Problem "${f}: protected file, agents don't change it (AGENTS.md, 'Don't touch')"; break }
        }
    }
    Scan '\.cs$' '(DispatcherTimer|DispatcherQueueTimer|CreateTimer\s*\(|System\.Threading\.Timer|new\s+Timer\s*\(|PeriodicTimer|FileSystemWatcher|Thread\.Sleep|Task\.Delay|SpinWait|while\s*\(\s*true\s*\)|for\s*\(\s*;\s*;\s*\))' `
        'timers, waiting and endless loops need the owner''s review (no background work while idle)'
    Scan '\.cs$' '\[\s*(LibraryImport|GeneratedComInterface|UnmanagedCallersOnly)' 'native code is protected: leave it to a reviewed change'
    foreach ($f in $changed) {
        if ($f -notmatch '^tests/Binder\.Tests/') { continue }
        foreach ($l in $removed[$f]) {
            if ($l -match 'Check\s*\(') { Problem "${f}: an existing test check was removed or changed; only add new ones`n        $($l.Trim())" }
        }
    }
}

if ($Task) {
    if (-not (Test-Path -LiteralPath $Task -PathType Leaf)) { Write-Host "Task card not found: $Task" -ForegroundColor Red; exit 2 }
    $allowed = New-Object System.Collections.Generic.List[string]
    $inFiles = $false
    foreach ($l in (Get-Content -LiteralPath $Task)) {
        if ($l -match '^##\s') { $inFiles = ($l -match '^##\s+Files\s*$'); continue }
        if ($inFiles -and $l -match '^\s*-\s*`([^`]+)`') { $allowed.Add($Matches[1].Replace('\', '/')) }
    }
    if ($allowed.Count -eq 0) { Problem "${Task}: the card has no '## Files' list" }
    foreach ($f in $changed) {
        if (-not $allowed.Contains($f)) { Problem "${f}: not in the task card's '## Files' list" }
    }
}

# --- Build and unit tests -------------------------------------------------------------------------------
if (-not $SkipBuild) {
    # Windows PowerShell 5.1 turns a native tool's stderr into errors under 'Stop'; exit codes decide here.
    $ErrorActionPreference = 'Continue'
    $onWindows = [Environment]::OSVersion.Platform -eq 'Win32NT'
    if ($onWindows) {
        Write-Host "`nBuilding (dotnet build Binder.sln -c Release)..."
        & dotnet build Binder.sln -c Release -nologo -v quiet
        if ($LASTEXITCODE -ne 0) { Problem 'the build failed (see the errors above)' }
    } else {
        Write-Host "`nSkipping the app build: the WinUI app only builds on Windows." -ForegroundColor Yellow
    }
    Write-Host "`nUnit tests (dotnet run --project tests/Binder.Tests -c Release)..."
    $out = & dotnet run --project tests/Binder.Tests -c Release 2>&1
    $out | ForEach-Object { if ("$_" -like 'FAIL *') { Write-Host $_ -ForegroundColor Red } }
    $summary = ($out | Select-Object -Last 1)
    Write-Host "  $summary"
    if ($LASTEXITCODE -ne 0) { Problem "unit tests: $summary" }
}

Write-Host ''
if ($problems.Count -eq 0) { Write-Host 'CHECK PASSED' -ForegroundColor Green; exit 0 }
Write-Host "CHECK FAILED ($($problems.Count) problem(s))" -ForegroundColor Red
exit 1
