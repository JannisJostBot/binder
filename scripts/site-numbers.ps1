# Writes the measured numbers of a release into the website's "How much it uses" table (site/index.html).
#
#   ./scripts/site-numbers.ps1 -Numbers artifacts/site-numbers.json -Version 0.2.1 [-Site site/index.html] [-Date 2026-11-17]
#
# The numbers come from the UI smoke test (tests/smoke.ps1 writes site-numbers.json); .github/workflows/
# site-numbers.yml runs both for each published release and opens a pull request with the result.
# The page marks each replaceable part as <!-- n:key -->text<!-- /n -->; everything outside the markers stays.
# Stops (and changes nothing) when a number is missing or implausible, or when a measurement contradicts what the
# page says next to it ("Processor: None" while closed or while a game runs).
param(
    [Parameter(Mandatory)] [string] $Numbers,
    [Parameter(Mandatory)] [string] $Version,
    [string] $Site = (Join-Path $PSScriptRoot '..\site\index.html'),
    [string] $Date
)
$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d{1,5}\.\d{1,5}\.\d{1,5}$') { throw "Not a version: $Version" }
$when = if ($Date) { [datetime]::ParseExact($Date, 'yyyy-MM-dd', [cultureinfo]::InvariantCulture) } else { (Get-Date).ToUniversalTime() }
$n = Get-Content -Raw $Numbers | ConvertFrom-Json

function Number([string] $key, [double] $max) {
    $value = $n.$key
    if ($null -eq $value -or $value -isnot [ValueType] -or [double]$value -lt 0 -or [double]$value -gt $max) {
        throw "site-numbers.json: '$key' is missing or not a number from 0 to ${max}: '$value'"
    }
    [double]$value
}

function Html([string] $text) { [System.Net.WebUtility]::HtmlEncode($text) }

# "2.4", "12", "120" (MB): one decimal below 10, whole numbers below 100, tens above.
function Mb([double] $mb) {
    $inv = [cultureinfo]::InvariantCulture
    if ($mb -lt 10) { return ([math]::Round($mb, 1)).ToString('0.#', $inv) }
    if ($mb -lt 100) { return ([math]::Round($mb)).ToString('0', $inv) }
    return ([math]::Round($mb / 10) * 10).ToString('0', $inv)
}

$open = Number 'openMB' 4000
$closed = Number 'closedMB' 1000
$watcher = Number 'watcherMB' 100
$games = [int](Number 'games' 100000)
$openBig = Number 'openBigMB' 4000
$bigGames = [int](Number 'bigGames' 100000)
if ($bigGames -le $games) { throw "site-numbers.json: the large library ($bigGames games) isn't larger than the small one ($games)" }
$closedCpu = Number 'closedCpuMs' 100000
$watcherCpu = Number 'watcherCpuMs' 100000

# The page says "None" for the processor in these two rows; a run that measured otherwise doesn't go on the page.
if ($closedCpu -gt 100) { throw "CPU while closed was $closedCpu ms over 10 s; the page says None" }
if ($watcherCpu -gt 50) { throw "The helper used $watcherCpu ms of CPU over 5 s while the game ran; the page says None" }

$machine = @()
if ($n.windows) { $machine += [string]$n.windows }
if ($n.cores) { $machine += "$([int]$n.cores) cores" }
if ($n.ramGB) { $machine += "$([int]$n.ramGB) GB" }
$machineText = if ($machine) { " ($($machine -join ', '))" } else { '' }

$values = @{
    # The figures (the page writes "MB" after them) and the helper's "About … MB" in the steps.
    open        = Html (Mb $open)
    openNote    = Html "With $games games; $(Mb $openBig) MB with $bigGames"
    closed      = Html (Mb $closed)
    watcher     = Html (Mb $watcher)
    watcherStep = Html "$(Mb $watcher) MB"
    measured    = Html ("Binder $Version, measured on " + $when.ToString('d MMMM yyyy', [cultureinfo]::InvariantCulture) +
        " on a GitHub Actions test machine$machineText, with Steam covers. Memory as Task Manager shows it")
}

$html = [IO.File]::ReadAllText((Resolve-Path $Site))
$pattern = '<!-- n:(\w+) -->.*?<!-- /n -->'
$found = @{}
foreach ($m in [regex]::Matches($html, $pattern, 'Singleline')) {
    $key = $m.Groups[1].Value
    if (-not $values.ContainsKey($key)) { throw "${Site}: unknown marker <!-- n:$key -->" }
    $found[$key] = $true
}
foreach ($key in $values.Keys) {
    if (-not $found[$key]) { throw "${Site}: marker <!-- n:$key --> is missing" }
}
$result = [regex]::Replace($html, $pattern, {
    param($m) "<!-- n:$($m.Groups[1].Value) -->$($values[$m.Groups[1].Value])<!-- /n -->"
}, 'Singleline')

if ($result -ceq $html) { Write-Host 'The page already shows these numbers.'; exit 0 }
[IO.File]::WriteAllText((Resolve-Path $Site), $result, (New-Object System.Text.UTF8Encoding $false))
Write-Host "Updated ${Site}:"
foreach ($key in 'open', 'openNote', 'closed', 'watcher', 'watcherStep', 'measured') { Write-Host "  ${key}: $($values[$key])" }
