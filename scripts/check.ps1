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
    - the website (site/) loads nothing from other servers; its scripts are its own .js files and contact only
      GitHub's API for this repository's releases
    - a change to what site/docs.html documents (Game mode and its helper, Start with Windows, updates, shortcut
      import, the installer) also changes site/docs.html, unless -DocsUnchanged or a commit on the branch has a
      'Docs-Unchanged: <why>' line (what the page says still holds)

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
    [switch]$AllowPackages,
    [switch]$DocsUnchanged
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
# The website stays plain and private: nothing loaded from other servers (links are fine), no embedded pages, and
# scripts only from its own .js files (no inline code). Those may contact one address: GitHub's API for this
# repository's releases (owner, 2026-11-16: the download section follows the latest release); the page's
# Content-Security-Policy says the same. This is the website only; the app's network rules are in CLAUDE.md.
Scan '^site/.*\.html?$' '<\s*(iframe|embed|object)\b' 'no embedded pages on the website'
Scan '^site/.*\.html?$' '<\s*script\b(?![^>]*\bsrc\s*=\s*"[a-z0-9-]+\.js")' 'website scripts only from its own .js files, no inline code'
Scan '^site/.*\.html?$' '\bon[a-z]+\s*=\s*["'']' 'no inline event handlers on the website'
Scan '^site/.*\.js$' '(https?:)?//(?!api\.github\.com/repos/|github\.com/|www\.virustotal\.com/gui/file/)[a-z0-9.-]+\.[a-z]' 'website scripts contact only api.github.com (links: github.com, VirusTotal reports)'
Scan '^site/.*\.js$' '\b(XMLHttpRequest|WebSocket|EventSource|sendBeacon|importScripts|eval)\s*\(|new\s+(XMLHttpRequest|WebSocket|EventSource|Function)\b|\.innerHTML\b|document\.write' 'website scripts: fetch only, no eval or innerHTML'
Scan '^site/.*\.html?$' '<\s*(img|link|source|video|audio|track|image|use)\b[^>]*\b(src|href|srcset)\s*=\s*["'']?\s*(https?:)?//' 'the website loads nothing from other servers'
Scan '^site/.*\.(html?|css)$' '(@import|url\(\s*["'']?\s*(https?:)?//)' 'the website loads nothing from other servers'

# The documentation page says exactly what these do on the user's PC (the unit tests compare its Game mode items
# with the code; this catches the rest). A change that leaves every sentence true passes with -DocsUnchanged, or
# in CI with a 'Docs-Unchanged: <why>' line in one of the branch's commit messages.
$documented = @(
    '^src/Binder\.Watcher/GameMode(Shared)?\.cs$', '^src/Binder\.Elevated/', '^src/Binder\.Setup/',
    '^src/Binder/Services/(GameModeService|WindowsGaming|Autostart|Updater)\.cs$',
    '^src/Binder/Library/(ShortcutImporter|UpdateManifest)\.cs$'
)
if (-not $DocsUnchanged) { $DocsUnchanged = [bool](git log --format=%B "$mergeBase..HEAD" | Select-String -Pattern '^Docs-Unchanged:\s*\S') }
if (-not $DocsUnchanged -and -not ($changed -contains 'site/docs.html')) {
    foreach ($f in $changed) {
        foreach ($p in $documented) {
            if ($f -match $p) { Problem "${f}: site/docs.html documents what this does; update it in the same change, or if every sentence there still holds, pass -DocsUnchanged or add a 'Docs-Unchanged: <why>' line to a commit message"; break }
        }
    }
}

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
