# Builds what ships, into publish/ (see docs/installer.md):
#   app\                   the widget (WinUI 3, Native AOT, self-contained: no runtime to install) and the watcher
#                          (Native AOT, ~1 MB, ~3 MB of memory) and Game mode's helper (GameShelf.Elevated.exe),
#                          as they're laid out in the install folder
#   GameShelfSetup.exe     Native AOT installer/updater with app\ inside
#
#   ./publish.ps1
#   ./publish.ps1 -CertificateThumbprint <sha1>   # also Authenticode-signs the exes (cert in your store)
#
# Native AOT needs the "Desktop development with C++" workload (MSVC linker) from Visual Studio
# or the Build Tools.
param(
    [string] $Output = (Join-Path $PSScriptRoot 'publish'),
    [string] $CertificateThumbprint,
    [string] $TimestampUrl = 'http://timestamp.digicert.com'
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$runtime = 'win-x64'

function Sign([string[]] $files) {
    if (-not $CertificateThumbprint) { return }
    $signtool = (Get-Command signtool.exe -ErrorAction SilentlyContinue).Source
    if (-not $signtool) {
        $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $signtool) { throw 'signtool.exe not found: install the Windows SDK, or leave out -CertificateThumbprint.' }
    & $signtool sign /sha1 $CertificateThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $files
    if ($LASTEXITCODE -ne 0) { throw "signtool failed ($LASTEXITCODE)" }
}

$app = Join-Path $Output 'app'
Remove-Item $app -Recurse -Force -ErrorAction SilentlyContinue

# Intermediate output goes to obj/publish-* so it can't mix with the normal bin/ build.
dotnet publish src/GameShelf -c Release -r $runtime -p:PublishAot=true -p:DebugType=none `
    -p:OutputPath="$PSScriptRoot/obj/publish-widget/" -o $app
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet publish src/GameShelf.Watcher -c Release -r $runtime -p:PublishAot=true `
    -p:DebugType=none -p:OutputPath="$PSScriptRoot/obj/publish-watcher/" -o $app
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Game mode's admin part: "Allow..." copies it to Program Files (one UAC prompt); it never runs from here otherwise.
dotnet publish src/GameShelf.Elevated -c Release -r $runtime -p:PublishAot=true `
    -p:DebugType=none -p:OutputPath="$PSScriptRoot/obj/publish-elevated/" -o $app
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Metadata for compilers and debug symbols: not needed to run.
Get-ChildItem $app -Recurse -File -Include *.winmd, *.pdb | Remove-Item

$widget = Join-Path $app 'GameShelf.exe'
$watcher = Join-Path $app 'GameShelf.Watcher.exe'
$elevated = Join-Path $app 'GameShelf.Elevated.exe'
Sign @($widget, $watcher, $elevated)

# The setup's payload: app.zip with the app folder, plus payload.txt with every file's SHA-256 and path, which
# setup checks after writing each file to disk.
$payload = Join-Path $PSScriptRoot 'obj/setup-payload'
Remove-Item $payload -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $payload | Out-Null
$root = (Resolve-Path $app).Path.TrimEnd('\') + '\'
$lines = foreach ($file in Get-ChildItem $app -Recurse -File | Sort-Object FullName) {
    '{0} {1} {2}' -f (Get-FileHash $file.FullName -Algorithm SHA256).Hash, $file.Length, $file.FullName.Substring($root.Length)
}
[IO.File]::WriteAllLines((Join-Path $payload 'payload.txt'), [string[]] $lines)   # no BOM
# Entry names with "/", as zip expects (Windows PowerShell's ZipFile.CreateFromDirectory writes "\").
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::Open((Join-Path $payload 'app.zip'), [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem $app -Recurse -File | Sort-Object FullName) {
        $name = $file.FullName.Substring($root.Length).Replace('\', '/')
        [void] [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $name, [IO.Compression.CompressionLevel]::Optimal)
    }
}
finally {
    $zip.Dispose()
}

dotnet publish src/GameShelf.Setup -c Release -r $runtime -p:PublishAot=true `
    -p:PayloadDir="$payload/" -p:DebugType=none -p:OutputPath="$PSScriptRoot/obj/publish-setup/" -o "$PSScriptRoot/obj/setup-out"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$setup = Join-Path $Output 'GameShelfSetup.exe'
Copy-Item (Join-Path $PSScriptRoot 'obj/setup-out/GameShelfSetup.exe') $setup -Force
Sign @($setup)

# Single files of earlier versions of this script, so publish/ only holds what this build made.
Remove-Item (Join-Path $Output 'GameShelf.exe'), (Join-Path $Output 'GameShelf.Watcher.exe') -ErrorAction SilentlyContinue

Write-Host ''
$appFiles = Get-ChildItem $app -Recurse -File
Write-Host ('{0,-28} {1,8:N0} KB in {2} files' -f 'app\', (($appFiles | Measure-Object Length -Sum).Sum / 1KB), $appFiles.Count)
Write-Host ('{0,-28} {1,8:N0} KB' -f 'GameShelfSetup.exe', ((Get-Item $setup).Length / 1KB))
Write-Host ("GameShelfSetup.exe SHA-256: {0}" -f (Get-FileHash $setup -Algorithm SHA256).Hash)
Write-Host "Published to $Output"
