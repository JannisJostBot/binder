# Writes the website's Releases page (site/releases.html): every published version, newest first, with its notes,
# its date and its setup to download.
#
#   gh api --paginate "repos/OWNER/REPO/releases?per_page=100" --jq '.[]' > releases.jsonl
#   ./scripts/site-releases.ps1 -Releases releases.jsonl [-Repository OWNER/REPO] [-Site site/releases.html]
#
# The release list comes from GitHub's API (one release object per line, as above); the notes from
# docs/releases/<version>.json, the same lines the app shows under "What's new". .github/workflows/pages.yml runs
# this when it publishes the site (also when a release is published), so the page lists each release as it comes out.
# The page marks the two replaceable parts as <!-- releases -->…<!-- /releases --> (the cards) and
# <!-- releases-nav -->…<!-- /releases-nav --> (the side navigation); everything outside them stays.
# Drafts (releases still waiting for the owner's signature) and anything that isn't a vX.Y.Z release with its
# BinderSetup-X.Y.Z.exe are left out. Stops (and changes nothing) when a notes file is malformed or nothing is left.
param(
    [Parameter(Mandatory)] [string] $Releases,
    [string] $Repository = 'JannisJostBot/binder',
    [string] $Notes = (Join-Path $PSScriptRoot '..\docs\releases'),
    [string] $Site = (Join-Path $PSScriptRoot '..\site\releases.html')
)
$ErrorActionPreference = 'Stop'

if ($Repository -notmatch '^[A-Za-z0-9-]{1,39}/[A-Za-z0-9._-]{1,100}$') { throw "Not a repository: $Repository" }
$inv = [cultureinfo]::InvariantCulture
function Html([string] $text) { [System.Net.WebUtility]::HtmlEncode($text) }

# The notes' English lines, checked as docs/releases/README.md describes them; an urgent release's line comes first.
function Read-Notes([string] $version) {
    $file = Join-Path $Notes "$version.json"
    if (-not (Test-Path -LiteralPath $file)) { return $null }
    $json = Get-Content -Raw -LiteralPath $file | ConvertFrom-Json
    $lines = @($json.en)
    $urgent = $null
    if ($json.urgent) { $urgent = [string]$json.urgent.en }
    if ($lines.Count -lt 1 -or $lines.Count -gt 6) { throw "${file}: 'en' needs 1 to 6 lines" }
    foreach ($line in @($lines) + @($urgent | Where-Object { $_ })) {
        if ($line -isnot [string] -or $line.Length -lt 1 -or $line.Length -gt 200 -or $line -match '[\r\n]') {
            throw "${file}: a line isn't plain text of 1 to 200 characters: '$line'"
        }
    }
    [pscustomobject]@{ Lines = $lines; Urgent = $urgent }
}

# "34 MB", as the home page's download panel says it.
function Size([long] $bytes) {
    if ($bytes -lt 1MB) { return '1 MB' }
    ([math]::Round($bytes / 1MB)).ToString('0', $inv) + ' MB'
}

$list = foreach ($line in Get-Content -LiteralPath $Releases) {
    if (-not $line.Trim()) { continue }
    $r = $line | ConvertFrom-Json
    if ($r.draft) { continue }
    if ([string]$r.tag_name -notmatch '^v(\d{1,5}\.\d{1,5}\.\d{1,5})$') { continue }
    $version = $Matches[1]
    $name = "BinderSetup-$version.exe"
    $asset = @($r.assets) | Where-Object { $_.name -eq $name } | Select-Object -First 1
    if (-not $asset) { continue }
    $digest = [string]$asset.digest
    $sha = if ($digest -match '^sha256:([0-9a-f]{64})$') { $Matches[1] } else { $null }
    # The VirusTotal result .github/workflows/virustotal.yml wrote into the release's description, read the way
    # site/release.js reads it; kept only when it names this setup and its hash is GitHub's hash of the file.
    $scan = $null
    if ([string]$r.body -match '(?s)<!-- virustotal -->(.*?)<!-- /virustotal -->') {
        $section = $Matches[1]
        $counts = [regex]::Match($section, '\b(\d{1,3}) of (\d{1,3}) engines flagged\b')
        $day = [regex]::Match($section, '\b(20\d\d-[01]\d-[0-3]\d)\b')
        $hash = [regex]::Match($section, 'SHA-256 `([0-9a-f]{64})`')
        if ($section.Contains('`' + $name + '`') -and $counts.Success -and $day.Success -and $hash.Success) {
            $flagged = [int]$counts.Groups[1].Value
            $engines = [int]$counts.Groups[2].Value
            $scannedSha = $hash.Groups[1].Value
            if ($engines -ge 1 -and $flagged -le $engines -and (-not $sha -or $sha -eq $scannedSha)) {
                $scan = [pscustomobject]@{ Flagged = $flagged; Engines = $engines; Date = $day.Groups[1].Value; Sha = $scannedSha }
            }
        }
    }
    # PowerShell 7 already reads the ISO time as a date; otherwise it's the text.
    $when = $r.published_at
    $published = if ($when -is [datetime]) { $when.ToUniversalTime() } else { ([datetimeoffset]::Parse([string]$when, $inv)).UtcDateTime }
    [pscustomobject]@{
        Version    = $version
        Sort       = [version]$version
        Tag        = "v$version"
        Pre        = [bool]$r.prerelease
        Published  = $published
        File       = $name
        Bytes      = [long]$asset.size
        Sha        = $sha
        Scan       = $scan
        Notes      = Read-Notes $version
    }
}
$list = @($list | Sort-Object Sort -Descending)
if (-not $list) { throw "${Releases}: no published release with its setup" }
$latest = $list | Where-Object { -not $_.Pre } | Select-Object -First 1

$base = "https://github.com/$Repository/releases"
$cards = New-Object System.Text.StringBuilder
$nav = New-Object System.Text.StringBuilder
foreach ($r in $list) {
    $id = 'v' + ($r.Version -replace '\.', '-')
    $isLatest = $r -eq $latest
    $date = $r.Published.ToString('d MMMM yyyy', $inv)
    $iso = $r.Published.ToString('yyyy-MM-dd', $inv)
    $download = "$base/download/$($r.Tag)/$($r.File)"
    $tags = ''
    if ($isLatest) { $tags += '<span class="doc-tag doc-tag-on">Latest</span>' }
    if ($r.Pre) { $tags += '<span class="doc-tag">Pre-release</span>' }
    if ($r.Notes -and $r.Notes.Urgent) { $tags += '<span class="doc-tag doc-tag-admin">Security fix</span>' }

    [void]$nav.Append("          <li><a href=""#$id"">$(Html $r.Version)</a></li>`n")
    [void]$cards.Append("      <article class=""release"" id=""$id"" aria-labelledby=""$id-title"">`n")
    [void]$cards.Append("        <header class=""release-head"">`n")
    [void]$cards.Append("          <h2 class=""release-title"" id=""$id-title"">Binder $(Html $r.Version)</h2>`n")
    if ($tags) { [void]$cards.Append("          <p class=""release-tags"">$tags</p>`n") }
    [void]$cards.Append("          <p class=""release-date""><time datetime=""$iso"">$(Html $date)</time></p>`n")
    [void]$cards.Append("        </header>`n")
    if ($r.Notes) {
        [void]$cards.Append("        <ul class=""release-notes"">`n")
        if ($r.Notes.Urgent) { [void]$cards.Append("          <li>$(Html $r.Notes.Urgent)</li>`n") }
        foreach ($note in $r.Notes.Lines) { [void]$cards.Append("          <li>$(Html $note)</li>`n") }
        [void]$cards.Append("        </ul>`n")
    } else {
        [void]$cards.Append("        <p class=""release-notes-none"">What changed is on <a href=""$base/tag/$($r.Tag)"">its GitHub page</a>.</p>`n")
    }
    $button = if ($isLatest) { 'button-primary' } else { 'button-secondary' }
    [void]$cards.Append("        <div class=""release-download"">`n")
    # The data attributes are for the dialog shown once the download starts (site/download.js).
    $data = "data-version=""$($r.Version)"""
    if ($r.Scan) {
        $data += " data-flagged=""$($r.Scan.Flagged)"" data-engines=""$($r.Scan.Engines)"" data-scanned=""$($r.Scan.Date)"" data-sha256=""$($r.Scan.Sha)"""
    }
    [void]$cards.Append("          <a class=""button button-small $button"" href=""$download"" $data>Download $(Html $r.Version)</a>`n")
    [void]$cards.Append("          <p class=""release-file"">$(Html $r.File) · $(Size $r.Bytes) · <a href=""$base/tag/$($r.Tag)"">On GitHub</a></p>`n")
    if ($r.Sha) { [void]$cards.Append("          <p class=""release-sha"">SHA-256 <code>$($r.Sha)</code></p>`n") }
    if ($r.Scan) {
        [void]$cards.Append("          <p class=""release-scan"">VirusTotal: $($r.Scan.Flagged) of $($r.Scan.Engines) engines flagged it · <a href=""https://www.virustotal.com/gui/file/$($r.Scan.Sha)"">Full report</a></p>`n")
    } else {
        [void]$cards.Append("          <p class=""release-scan"">VirusTotal: not scanned</p>`n")
    }
    [void]$cards.Append("        </div>`n")
    [void]$cards.Append("      </article>`n")
}

$html = [IO.File]::ReadAllText((Resolve-Path $Site))
foreach ($part in @(@('releases', $cards.ToString()), @('releases-nav', $nav.ToString()))) {
    $pattern = "(<!-- $($part[0]) -->\r?\n).*?(\s*<!-- /$($part[0]) -->)"
    $found = [regex]::Matches($html, $pattern, 'Singleline').Count
    if ($found -ne 1) { throw "${Site}: expected one <!-- $($part[0]) --> … <!-- /$($part[0]) --> part, found $found" }
    $text = $part[1].TrimEnd("`n")
    $html = [regex]::Replace($html, $pattern, { param($m) $m.Groups[1].Value + $text + $m.Groups[2].Value }, 'Singleline')
}
[IO.File]::WriteAllText((Resolve-Path $Site), $html, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "$Site`: $($list.Count) releases, latest $($latest.Version)"
