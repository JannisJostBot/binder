<#
.SYNOPSIS
  Signs a Binder release on the owner's PC with a YubiKey and publishes it (docs/proposals.md §25).

.DESCRIPTION
  Two jobs:

  -ExportPublicKey
    Prints the public key of a YubiKey's signing certificate (PIV slot 9c, ECC P-384, found by Windows in
    CurrentUser\My) as base64 SubjectPublicKeyInfo, for src/Binder/Services/UpdateKeys.cs.

  -Version 0.2.0 [-Urgent] [-DryRun]
    For the draft release v<Version> that .github/workflows/release.yml made:
      1. checks the tag: on origin, on main, the same commit as here, <Version> in Directory.Build.props;
      2. checks the draft holds only BinderSetup-<Version>.exe, downloads it and runs `gh attestation verify`
         (this repository, release.yml, the tag's commit, GitHub-hosted runners);
      3. writes release.json itself from what it checked plus docs/releases/<Version>.json at the tagged commit;
      4. shows it and asks to confirm;
      5. signs it (the YubiKey asks for its PIN and a touch), with -Urgent with both YubiKeys in turn; with both
         keys' certificates in Windows, it asks which one signs (or pass -Thumbprint);
      6. verifies the signature file with the keys in UpdateKeys.cs at the tagged commit, as the app will;
      7. uploads release.json and release.json.sig to the draft and publishes it as the latest release.
    -DryRun stops after uploading: the draft stays a draft, nobody gets it. Run again without -DryRun to publish it
    (the dry run's release.json and .sig are replaced), or delete it.

  The signature: ECDSA P-384 / SHA-384, IEEE P1363 (96 bytes), base64, one line per key, over the fixed text
  "Binder release manifest v1", a zero byte, then release.json's exact bytes.

  Needs PowerShell 7, git, and the GitHub CLI (gh) signed in as the owner. Never put a key in a file or a secret.

.EXAMPLE
  ./scripts/sign-release.ps1 -ExportPublicKey
.EXAMPLE
  ./scripts/sign-release.ps1 -Version 0.2.0
.EXAMPLE
  ./scripts/sign-release.ps1 -Version 0.2.1 -Urgent
#>
#Requires -Version 7.2
[CmdletBinding(DefaultParameterSetName = 'Sign')]
param(
    [Parameter(ParameterSetName = 'Export', Mandatory)] [switch]$ExportPublicKey,
    [Parameter(ParameterSetName = 'Sign', Mandatory)] [ValidatePattern('^(0|[1-9]\d{0,4})\.(0|[1-9]\d{0,4})\.(0|[1-9]\d{0,4})$')] [string]$Version,
    [Parameter(ParameterSetName = 'Sign')] [switch]$Urgent,
    [Parameter(ParameterSetName = 'Sign')] [switch]$DryRun,
    # Which certificate signs, skipping the question; without it and with more than one P-384 smart card
    # certificate present, the script lists them and asks.
    [string]$Thumbprint,
    # -Urgent: the second key's certificate, skipping the second question.
    [Parameter(ParameterSetName = 'Sign')] [string]$SecondThumbprint
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Set-Location (Split-Path $PSScriptRoot -Parent)   # the repo root
# git's output (the notes with their accents) is UTF-8; read it as such, whatever the console's code page.
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$Repo = 'JannisJostBot/binder'
$Prefix = [System.Text.Encoding]::ASCII.GetBytes("Binder release manifest v1`0")
$Utf8 = [System.Text.UTF8Encoding]::new($false)
$SHA384 = [System.Security.Cryptography.HashAlgorithmName]::SHA384
$P1363 = [System.Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation

function Fail([string]$text) { Write-Host "FAIL  $text" -ForegroundColor Red; exit 1 }
function Run([string]$exe, [string[]]$arguments) {
    $out = & $exe @arguments
    if ($LASTEXITCODE -ne 0) { Fail "$exe $($arguments -join ' ') failed ($LASTEXITCODE)" }
    return $out
}

# --- The YubiKey's certificate ---------------------------------------------------------------------------
# Windows keeps a smart card's certificate after the card is taken out, so both YubiKeys are usually listed: with
# more than one, the owner picks from a numbered list (or passes -Thumbprint / -SecondThumbprint). $exclude leaves
# out the key that already signed (-Urgent's second signature).
function Get-SigningCertificate([string]$thumb, [string]$which, [string]$exclude) {
    $all = @(Get-ChildItem Cert:\CurrentUser\My | Where-Object {
        $_.HasPrivateKey -and $_.PublicKey.Oid.Value -eq '1.2.840.10045.2.1' -and
        $_.PublicKey.GetECDsaPublicKey().KeySize -eq 384 -and $_.Thumbprint -ne $exclude
    } | Sort-Object Subject)
    if ($thumb) { $all = @($all | Where-Object Thumbprint -eq $thumb.ToUpperInvariant()) }
    if ($all.Count -eq 0) { Fail "No ECC P-384 certificate with a private key in CurrentUser\My$(if ($thumb) { " with thumbprint $thumb" }). Is $which plugged in?" }
    if ($all.Count -eq 1) { return $all[0] }
    Write-Host "Which key signs ($which)?"
    for ($i = 0; $i -lt $all.Count; $i++) { Write-Host "  [$($i + 1)] $($all[$i].Subject)  $($all[$i].Thumbprint)" }
    $answer = Read-Host "Number (1-$($all.Count))"
    $n = 0
    if (-not [int]::TryParse($answer, [ref]$n) -or $n -lt 1 -or $n -gt $all.Count) { Fail "Not one of the numbers: $answer" }
    return $all[$n - 1]
}

function Get-PublicKeyText($cert) { [Convert]::ToBase64String($cert.PublicKey.ExportSubjectPublicKeyInfo()) }

if ($ExportPublicKey) {
    $cert = Get-SigningCertificate $Thumbprint 'the YubiKey'
    Write-Host "Certificate: $($cert.Subject) ($($cert.Thumbprint))"
    Write-Host 'For src/Binder/Services/UpdateKeys.cs:'
    Write-Output "        `"$(Get-PublicKeyText $cert)`","
    exit 0
}

# --- 1. The tag ------------------------------------------------------------------------------------------
$tag = "v$Version"
Run git @('fetch', '--quiet', 'origin', 'main', "+refs/tags/${tag}:refs/tags/${tag}") | Out-Null
$commit = (Run git @('rev-parse', "$tag^{commit}")).Trim()
if ($commit -notmatch '^[0-9a-f]{40}$') { Fail "Can't read the tag's commit." }
$remote = @(Run git @('ls-remote', '--tags', 'origin', "refs/tags/$tag", "refs/tags/$tag^{}"))
$remoteCommit = ($remote | Where-Object { $_ -match "refs/tags/$([regex]::Escape($tag))\^\{\}$" } | Select-Object -First 1) ??
                ($remote | Select-Object -First 1)
if (-not $remoteCommit -or $remoteCommit.Split("`t")[0] -ne $commit) { Fail "$tag on origin isn't the commit $commit." }
& git merge-base --is-ancestor $commit origin/main
if ($LASTEXITCODE -ne 0) { Fail "$tag ($commit) isn't on main." }
$props = (Run git @('show', "${commit}:Directory.Build.props")) -join "`n"
if ($props -notmatch "<Version>$([regex]::Escape($Version))</Version>") { Fail "Directory.Build.props at $tag doesn't say $Version." }

# The keys the app will verify with, from the tagged commit (not from the working tree).
$keysFile = (Run git @('show', "${commit}:src/Binder/Services/UpdateKeys.cs")) -join "`n"
$keys = @([regex]::Matches(($keysFile -replace '//[^\n]*', ''), '"([A-Za-z0-9+/]{100,}={0,2})"') | ForEach-Object { $_.Groups[1].Value })
if ($keys.Count -eq 0) { Fail 'UpdateKeys.cs at the tag has no keys: run -ExportPublicKey and add them first.' }

$shared = (Run git @('show', "${commit}:src/Binder.Watcher/GameModeShared.cs")) -join "`n"
if ($shared -notmatch 'HelperVersion\s*=\s*(\d+)\s*;') { Fail "Can't read HelperVersion at $tag." }
$helperVersion = [int]$Matches[1]

# --- The notes -------------------------------------------------------------------------------------------
$notesText = (Run git @('show', "${commit}:docs/releases/$Version.json")) -join "`n"
$notesFile = $notesText | ConvertFrom-Json -AsHashtable
function Test-Line([string]$line) {
    $line.Length -ge 1 -and $line.Length -le 200 -and $line.Trim().Length -gt 0 -and
        $line -notmatch '[\p{Cc}‎‏‪-‮⁦-⁩]'
}
$notes = [ordered]@{}
foreach ($lang in 'en', 'de', 'fr') {
    $lines = @($notesFile[$lang])
    if ($lines.Count -eq 0) { Fail "docs/releases/$Version.json has no '$lang' notes." }
    if ($Urgent) {
        $urgentLines = $notesFile['urgent']
        $first = if ($urgentLines -is [hashtable]) { $urgentLines[$lang] } else { $null }
        if (-not $first) { Fail "-Urgent: docs/releases/$Version.json needs `"urgent`": { `"$lang`": `"<what the security fix is>`" }." }
        $lines = @($first) + $lines
    }
    if ($lines.Count -gt 6) { Fail "'$lang' notes have more than 6 lines$(if ($Urgent) { ' (with the urgent line)' })." }
    foreach ($l in $lines) { if ($l -isnot [string] -or -not (Test-Line $l)) { Fail "A '$lang' note line isn't plain text of 1 to 200 characters: $l" } }
    $notes[$lang] = [string[]]$lines
}

# --- 2. The draft and its setup --------------------------------------------------------------------------
$release = (Run gh @('release', 'view', $tag, '--repo', $Repo, '--json', 'isDraft,assets')) | ConvertFrom-Json
if (-not $release.isDraft) { Fail "$tag isn't a draft: a published release is never changed." }
$setupName = "BinderSetup-$Version.exe"
$assetNames = @($release.assets | ForEach-Object name)
# The setup, plus at most the release.json and its signature an earlier dry run left: those are replaced below.
$fromDryRun = @($assetNames | Where-Object { $_ -in 'release.json', 'release.json.sig' })
if ($setupName -notin $assetNames -or ($assetNames.Count - $fromDryRun.Count) -ne 1) {
    Fail "The draft should hold only $setupName (and an earlier dry run's release.json and .sig), it holds: $($assetNames -join ', ')"
}
if ($fromDryRun.Count -gt 0) { Write-Host "The draft has $($fromDryRun -join ' and ') from an earlier dry run: they're replaced." }

$work = Join-Path ([IO.Path]::GetTempPath()) "binder-release-$Version-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $work | Out-Null
Run gh @('release', 'download', $tag, '--repo', $Repo, '--pattern', $setupName, '--dir', $work) | Out-Null
$setup = Join-Path $work $setupName
Run gh @('attestation', 'verify', $setup, '--repo', $Repo,
    '--signer-workflow', "$Repo/.github/workflows/release.yml",
    '--source-ref', "refs/tags/$tag", '--source-digest', $commit, '--deny-self-hosted-runners') | Out-Host

$size = (Get-Item -LiteralPath $setup).Length
if ($size -lt 1MB -or $size -gt 200MB) { Fail "The setup's size ($size bytes) is outside 1 to 200 MB." }
$sha256 = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()

# --- 3. release.json -------------------------------------------------------------------------------------
$manifest = [ordered]@{
    format        = 1
    product       = 'Binder'
    channel       = 'stable'
    version       = $Version
    issued        = [DateTime]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", [Globalization.CultureInfo]::InvariantCulture)
    commit        = $commit
    setup         = [ordered]@{ size = $size; sha256 = $sha256 }
    urgent        = [bool]$Urgent
    helperVersion = $helperVersion
    notes         = $notes
}
$manifestBytes = $Utf8.GetBytes(($manifest | ConvertTo-Json -Depth 5) + "`n")
if ($manifestBytes.Length -gt 64KB) { Fail 'release.json is over 64 KB.' }
$manifestPath = Join-Path $work 'release.json'
[IO.File]::WriteAllBytes($manifestPath, $manifestBytes)

# --- 4. Confirm ------------------------------------------------------------------------------------------
Write-Host ''
Write-Host "Version   $Version$(if ($Urgent) { '   URGENT (both YubiKeys)' })"
Write-Host "Commit    $commit"
Write-Host "Setup     $size bytes"
Write-Host "SHA-256   $sha256"
Write-Host "Helper    $helperVersion"
Write-Host 'Notes (en):'
$notes['en'] | ForEach-Object { Write-Host "  - $_" }
Write-Host ''
if ((Read-Host "Type $Version to sign$(if ($DryRun) { ' (dry run, not published)' })") -ne $Version) { Fail 'Not confirmed.' }

# --- 5. Sign ---------------------------------------------------------------------------------------------
$payload = [byte[]]($Prefix + $manifestBytes)
function Sign-With($cert) {
    if ((Get-PublicKeyText $cert) -notin $keys) { Fail "The certificate $($cert.Thumbprint) isn't one of the keys in UpdateKeys.cs at $tag." }
    $key = [System.Security.Cryptography.X509Certificates.ECDsaCertificateExtensions]::GetECDsaPrivateKey($cert)
    if (-not $key) { Fail "Can't open the private key of $($cert.Thumbprint)." }
    try {
        Write-Host 'Enter the PIN and touch the YubiKey when it blinks.'
        return [Convert]::ToBase64String($key.SignData($payload, $SHA384, $P1363))
    } finally { $key.Dispose() }
}
$first = Get-SigningCertificate $Thumbprint $(if ($Urgent) { 'the first of two' } else { 'normally YubiKey 1' })
$signatures = @(Sign-With $first)
if ($Urgent) {
    Read-Host 'Now take out that YubiKey, plug in the other one and press Enter' | Out-Null
    $second = Get-SigningCertificate $SecondThumbprint 'the second key' $first.Thumbprint
    $signatures += Sign-With $second
}
$sigBytes = [System.Text.Encoding]::ASCII.GetBytes(($signatures -join "`n") + "`n")

# --- 6. Verify as the app does ---------------------------------------------------------------------------
$signers = 0
foreach ($k in $keys) {
    $ecdsa = [System.Security.Cryptography.ECDsa]::Create()
    try {
        $read = 0
        $ecdsa.ImportSubjectPublicKeyInfo([Convert]::FromBase64String($k), [ref]$read)
        foreach ($s in $signatures) {
            if ($ecdsa.VerifyData($payload, [Convert]::FromBase64String($s), $SHA384, $P1363)) { $signers++; break }
        }
    } finally { $ecdsa.Dispose() }
}
$needed = if ($Urgent) { 2 } else { 1 }
if ($signers -lt $needed) { Fail "Only $signers key(s) verify the signature, $needed needed." }
$sigPath = Join-Path $work 'release.json.sig'
[IO.File]::WriteAllBytes($sigPath, $sigBytes)
Write-Host "Signed and verified ($signers key(s))." -ForegroundColor Green

# --- 7. Upload and publish -------------------------------------------------------------------------------
Run gh @('release', 'upload', $tag, $manifestPath, $sigPath, '--repo', $Repo, '--clobber') | Out-Host
if ($DryRun) {
    Write-Host "Dry run: $tag stays a draft. Files in $work. To publish it, run again without -DryRun (it replaces these two files); otherwise delete the draft: gh release delete $tag --repo $Repo" -ForegroundColor Yellow
    exit 0
}
Run gh @('release', 'edit', $tag, '--repo', $Repo, '--draft=false', '--latest') | Out-Host
Remove-Item -LiteralPath $work -Recurse -Force
Write-Host "Published $tag." -ForegroundColor Green
