# One-time move of your data from Game Shelf (the app's old name) to Binder.
#
# Copies %APPDATA%\GameShelf to %APPDATA%\Binder and the cover cache %LOCALAPPDATA%\GameShelf\covers to
# %LOCALAPPDATA%\Binder\covers, then points the paths stored in the JSON files (imported shortcuts, custom covers,
# manually added games' shortcut copies) at the new folders. It only copies: the old folders are left as they are,
# so you can check Binder first and remove Game Shelf afterwards.
#
# Before: quit Game Shelf and Binder (tray icon › Quit). If Binder already made %APPDATA%\Binder, that folder is
# renamed to Binder.before-migration-<time>, not deleted.
#
#   powershell -ExecutionPolicy Bypass -File scripts\migrate-from-gameshelf.ps1

$ErrorActionPreference = 'Stop'

$oldData  = Join-Path $env:APPDATA 'GameShelf'
$newData  = Join-Path $env:APPDATA 'Binder'
$oldCache = Join-Path $env:LOCALAPPDATA 'GameShelf'
$newCache = Join-Path $env:LOCALAPPDATA 'Binder'

function Stop-With($message) { Write-Host $message -ForegroundColor Red; exit 1 }

if (-not (Test-Path -LiteralPath $oldData -PathType Container)) { Stop-With "Nothing to migrate: $oldData doesn't exist." }

$running = Get-Process -Name 'GameShelf', 'GameShelf.Watcher', 'Binder', 'Binder.Watcher' -ErrorAction SilentlyContinue
if ($running) { Stop-With ("Quit these first (tray icon › Quit, or wait for the game to end): " + (($running | ForEach-Object { $_.ProcessName + '.exe' } | Sort-Object -Unique) -join ', ')) }

if (Test-Path -LiteralPath (Join-Path $oldCache 'gamemode-journal.json')) {
    Stop-With "Game Shelf's Game mode is still applied. Start Game Shelf once so it puts things back, quit it, then run this again."
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
foreach ($dir in $newData, (Join-Path $newCache 'covers')) {
    if (Test-Path -LiteralPath $dir) {
        $aside = "$dir.before-migration-$stamp"
        Move-Item -LiteralPath $dir -Destination $aside
        Write-Host "Moved the existing $dir aside to $aside"
    }
}

# Copy, then check that every file arrived with the same size.
function Copy-Folder($from, $to) {
    New-Item -ItemType Directory -Force -Path (Split-Path $to -Parent) | Out-Null
    Copy-Item -LiteralPath $from -Destination $to -Recurse   # $to doesn't exist yet, so it becomes the copy
    $missing = Get-ChildItem -LiteralPath $from -Recurse -File -Force | Where-Object {
        $copy = Join-Path $to $_.FullName.Substring($from.Length).TrimStart('\')
        -not (Test-Path -LiteralPath $copy) -or (Get-Item -LiteralPath $copy -Force).Length -ne $_.Length
    }
    if ($missing) { Stop-With ("Copy incomplete, nothing was deleted. Missing or different:`n" + (($missing | ForEach-Object FullName) -join "`n")) }
    $count = @(Get-ChildItem -LiteralPath $to -Recurse -File -Force).Count
    Write-Host "Copied $from -> $to ($count files)"
}

Copy-Folder $oldData $newData
if (Test-Path -LiteralPath (Join-Path $oldCache 'covers') -PathType Container) {
    Copy-Folder (Join-Path $oldCache 'covers') (Join-Path $newCache 'covers')
}

# Paths inside the data files. JSON writes a backslash as \\, so match that form; anchoring on
# "\Roaming\GameShelf\" and "\Local\GameShelf\" (not the full profile path) also works when the user name is written
# with \u escapes.
$roamingLeaf = Split-Path $env:APPDATA -Leaf
$localLeaf   = Split-Path $env:LOCALAPPDATA -Leaf
$pairs = @(
    @("\\$roamingLeaf\\GameShelf\\", "\\$roamingLeaf\\Binder\\"),
    @("\\$localLeaf\\GameShelf\\",   "\\$localLeaf\\Binder\\")
)
$utf8 = New-Object System.Text.UTF8Encoding $false
$changed = 0
Get-ChildItem -LiteralPath $newData -File -Force | Where-Object { $_.Name -match '\.(json|jsonl|bak)$' } | ForEach-Object {
    $text = [IO.File]::ReadAllText($_.FullName)
    $new = $text
    foreach ($p in $pairs) { $new = [regex]::Replace($new, [regex]::Escape($p[0]), $p[1].Replace('$', '$$'), 'IgnoreCase') }
    if ($new -ne $text) {
        [IO.File]::WriteAllText($_.FullName, $new, $utf8)
        $changed++
        Write-Host "Updated paths in $($_.Name)"
    }
}

$left = Get-ChildItem -LiteralPath $newData -File -Force | Where-Object { $_.Name -match '\.(json|jsonl)$' } |
    Select-String -Pattern '\\GameShelf\\' -SimpleMatch -List
if ($left) {
    Write-Host "These files still mention a GameShelf folder; check them before deleting the old one:" -ForegroundColor Yellow
    $left | ForEach-Object { Write-Host "  $($_.Path):$($_.LineNumber)" }
}

Write-Host ""
Write-Host "Done ($changed files with updated paths). The old folders are untouched:" -ForegroundColor Green
Write-Host "  $oldData"
Write-Host "  $oldCache"
Write-Host "Start Binder and check your games, playtime, favorites and imported shortcuts before removing Game Shelf."
