# Builds what ships, into publish/ (see docs/installer.md):
#   app\                   the widget (WinUI 3, Native AOT, self-contained: no runtime to install) and the watcher
#                          (Native AOT, ~1 MB, ~3 MB of memory) and Game mode's helper (GameShelf.Elevated.exe),
#                          as they're laid out in the install folder
#   GameShelfSetup.exe     Native AOT installer/updater with app\ inside
#
#   ./scripts/publish.ps1
#   ./scripts/publish.ps1 -CertificateThumbprint <sha1>   # also Authenticode-signs the exes (cert in your store)
#
# Native AOT needs the "Desktop development with C++" workload (MSVC linker) from Visual Studio
# or the Build Tools.
param(
    [string] $Output = (Join-Path (Split-Path $PSScriptRoot -Parent) 'publish'),
    [string] $CertificateThumbprint,
    [string] $TimestampUrl = 'http://timestamp.digicert.com'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent   # the repo root
Set-Location $repo

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
    -p:OutputPath="$repo/obj/publish-widget/" -o $app
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet publish src/GameShelf.Watcher -c Release -r $runtime -p:PublishAot=true `
    -p:DebugType=none -p:OutputPath="$repo/obj/publish-watcher/" -o $app
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Game mode's admin part: "Allow..." copies it to Program Files (one UAC prompt); it never runs from here otherwise.
dotnet publish src/GameShelf.Elevated -c Release -r $runtime -p:PublishAot=true `
    -p:DebugType=none -p:OutputPath="$repo/obj/publish-elevated/" -o $app
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Metadata for compilers and debug symbols: not needed to run.
Get-ChildItem $app -Recurse -File -Include *.winmd, *.pdb | Remove-Item

# The app's translations (de\, fr\ GameShelf.resources.dll): Native AOT compiles them into GameShelf.exe.
foreach ($satellite in Get-ChildItem $app -Recurse -File -Filter 'GameShelf.resources.dll') {
    $folder = $satellite.Directory
    Remove-Item $satellite.FullName
    if (-not (Get-ChildItem $folder.FullName -Force)) { Remove-Item $folder.FullName }
}

# Licenses: Binder's own (GPL 3.0 with its section 7 permission) and those of everything it bundles, taken from
# the exact package versions this build used, into app\licenses (THIRD-PARTY-NOTICES.txt lists them). A build
# without them must not ship, so a missing file stops it.
$licenses = Join-Path $app 'licenses'
New-Item -ItemType Directory -Force $licenses | Out-Null
function Add-License([string] $source, [string] $name) {
    if (-not (Test-Path -LiteralPath $source)) { throw "License file missing: $source" }
    Copy-Item -LiteralPath $source (Join-Path $licenses $name)
}
Add-License (Join-Path $repo 'LICENSE') 'GameShelf-LICENSE.txt'
Add-License (Join-Path $repo 'LICENSE-EXCEPTION.txt') 'GameShelf-LICENSE-EXCEPTION.txt'
Add-License (Join-Path $repo 'THIRD-PARTY-NOTICES.txt') 'THIRD-PARTY-NOTICES.txt'

$assets = Get-Content (Join-Path $repo 'src/GameShelf/obj/project.assets.json') -Raw | ConvertFrom-Json
$packages = @($assets.packageFolders.PSObject.Properties.Name)[0]
# The .NET runtime pack (compiled into every exe by Native AOT).
$runtimeVersion = $assets.project.frameworks.PSObject.Properties.Value |
    ForEach-Object { $_.downloadDependencies } | Where-Object { $_.name -eq "Microsoft.NETCore.App.Runtime.$runtime" } |
    ForEach-Object { $_.version.Trim('[', ']').Split(',')[0].Trim() } | Select-Object -First 1
if (-not $runtimeVersion) { throw 'The .NET runtime pack version was not found in project.assets.json' }
$runtimeDir = Join-Path $packages "microsoft.netcore.app.runtime.$runtime/$runtimeVersion"
Add-License (Join-Path $runtimeDir 'LICENSE.TXT') 'dotnet-LICENSE.txt'
Add-License (Join-Path $runtimeDir 'THIRD-PARTY-NOTICES.TXT') 'dotnet-THIRD-PARTY-NOTICES.txt'
# The Windows App SDK packages: one copy of their license (the same text in each), and each one's notices.
$sdkLicenses = @{}
foreach ($library in $assets.libraries.PSObject.Properties | Where-Object { $_.Name -like 'Microsoft.WindowsAppSDK.*/*' }) {
    $dir = Join-Path $packages $library.Value.path
    $short = ($library.Name -split '/')[0] -replace '^Microsoft\.', ''
    $license = Join-Path $dir 'license.txt'
    if (Test-Path -LiteralPath $license) {
        $hash = (Get-FileHash -LiteralPath $license -Algorithm SHA256).Hash
        if (-not $sdkLicenses.ContainsKey($hash)) {
            Add-License $license $(if ($sdkLicenses.Count -eq 0) { 'WindowsAppSDK-license.txt' } else { "$short-license.txt" })
            $sdkLicenses[$hash] = $true
        }
    }
    $notice = Join-Path $dir 'NOTICE.txt'
    if (Test-Path -LiteralPath $notice) { Add-License $notice "$short-NOTICE.txt" }
}
if ($sdkLicenses.Count -eq 0) { throw 'The Windows App SDK license was not found in its packages' }
# WebView2, only when its DLLs came along into the app folder.
if (Get-ChildItem $app -Recurse -File -Filter 'Microsoft.Web.WebView2*.dll') {
    $webView = $assets.libraries.PSObject.Properties | Where-Object { $_.Name -like 'Microsoft.Web.WebView2/*' } | Select-Object -First 1
    if (-not $webView) { throw 'WebView2 DLLs are in the app, but its package was not found' }
    $dir = Join-Path $packages $webView.Value.path
    Add-License (Join-Path $dir 'LICENSE.txt') 'WebView2-LICENSE.txt'
    Add-License (Join-Path $dir 'NOTICE.txt') 'WebView2-NOTICE.txt'
}

$widget = Join-Path $app 'GameShelf.exe'
$watcher = Join-Path $app 'GameShelf.Watcher.exe'
$elevated = Join-Path $app 'GameShelf.Elevated.exe'
Sign @($widget, $watcher, $elevated)

# The setup's payload: app.zip with the app folder, plus payload.txt with every file's SHA-256 and path, which
# setup checks after writing each file to disk.
$payload = Join-Path $repo 'obj/setup-payload'
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
    -p:PayloadDir="$payload/" -p:DebugType=none -p:OutputPath="$repo/obj/publish-setup/" -o "$repo/obj/setup-out"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$setup = Join-Path $Output 'GameShelfSetup.exe'
Copy-Item (Join-Path $repo 'obj/setup-out/GameShelfSetup.exe') $setup -Force
Sign @($setup)

# Single files of earlier versions of this script, so publish/ only holds what this build made.
Remove-Item (Join-Path $Output 'GameShelf.exe'), (Join-Path $Output 'GameShelf.Watcher.exe') -ErrorAction SilentlyContinue

Write-Host ''
$appFiles = Get-ChildItem $app -Recurse -File
Write-Host ('{0,-28} {1,8:N0} KB in {2} files' -f 'app\', (($appFiles | Measure-Object Length -Sum).Sum / 1KB), $appFiles.Count)
Write-Host ('{0,-28} {1,8:N0} KB' -f 'GameShelfSetup.exe', ((Get-Item $setup).Length / 1KB))
Write-Host ("GameShelfSetup.exe SHA-256: {0}" -f (Get-FileHash $setup -Algorithm SHA256).Hash)
Write-Host "Published to $Output"
