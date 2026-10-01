<#
.SYNOPSIS
Runs isolated managed JSON page analysis processes over identical repository-local corpora.
.DESCRIPTION
Build baseline and candidate binaries first. Pairs alternates execution order, starts a new
process for each measurement, and never includes corpus generation or Document.Open in Analyze time.
Warm runs 200 rotating near-start/middle/end edits followed by 5,000 dispersed sequential edits.
The measurements are parser microbenchmarks, not native input-to-draw or cold filesystem claims.
.EXAMPLE
pwsh -File benchmarks/JsonArrayPages/Run.ps1 -Mode Pairs -BaselineCommit 91a77f8
#>
param(
    [ValidateSet('Baseline', 'Pairs', 'Warm')][string]$Mode = 'Pairs',
    [int]$Repeats = 5,
    [string]$BaselineCommit = '91a77f886058802be179185b057919025c315a37',
    [string]$RunName = 'pairs',
    [string]$CorpusFilter = '*'
)
$ErrorActionPreference = 'Stop'
if (!(Test-Path -LiteralPath 'mote.sln')) { throw 'Run from the repository root.' }
if ($Repeats -lt 5) { throw 'At least five isolated cold repetitions are required.' }
if ($RunName -notmatch '^[a-zA-Z0-9_-]+$') { throw 'RunName must be a simple directory name.' }
$out = Join-Path '.cache/json-array-pages' $RunName
New-Item -ItemType Directory -Force $out | Out-Null
$baseline = '.temp/JsonArrayPages/baseline-bin/JsonArrayPages.dll'
$candidate = '.temp/JsonArrayPages/candidate-bin/JsonArrayPages.dll'
$environment = [ordered]@{
    mode = $Mode; repeats = $Repeats; baselineCommit = $BaselineCommit
    candidateCommit = (& git rev-parse HEAD).Trim(); sdk = (& dotnet --version).Trim()
    cpu = (Get-CimInstance Win32_Processor | Select-Object Name, NumberOfCores, NumberOfLogicalProcessors)
    os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    benchmarkSourceSha256 = (Get-FileHash benchmarks/JsonArrayPages/Program.cs -Algorithm SHA256).Hash
    baselineParserSha256 = (Get-FileHash .temp/JsonArrayPages/baseline/src/Mote.Formats/JsonIncrementalSession.cs -Algorithm SHA256).Hash
    candidateParserSha256 = (Get-FileHash src/Mote.Formats/JsonIncrementalSession.cs -Algorithm SHA256).Hash
    candidateCertificateSha256 = (Get-FileHash src/Mote.Formats/JsonArrayCertificate.cs -Algorithm SHA256).Hash
    candidateSnapshotSha256 = (Get-FileHash src/Mote.Engine/TextSnapshot.cs -Algorithm SHA256).Hash
    startedUtc = [DateTime]::UtcNow.ToString('O')
}
$environment | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $out 'environment.json') -Encoding utf8
$fixtures = Get-ChildItem -LiteralPath '.temp/JsonArrayPages/corpora' -Filter '*.json' |
    Where-Object { $_.Name -notlike '*.metadata.json' -and $_.BaseName -like $CorpusFilter } | Sort-Object Name
foreach ($fixture in $fixtures) {
    $name = $fixture.BaseName
    if ($Mode -eq 'Warm') {
        if ($name -notlike '*-100') { continue }
        & dotnet $candidate warm $fixture.FullName (Join-Path $out "$name-warm.json")
        if ($LASTEXITCODE -ne 0) { throw "Warm benchmark failed: $name" }
        continue
    }
    for ($i = 0; $i -lt $Repeats; $i++) {
        $labels = if ($Mode -eq 'Baseline') { @('baseline') } elseif ($i % 2 -eq 0) { @('baseline', 'candidate') } else { @('candidate', 'baseline') }
        foreach ($label in $labels) {
            $binary = if ($label -eq 'baseline') { $baseline } else { $candidate }
            & dotnet $binary cold $fixture.FullName (Join-Path $out "$name-$label-$i.json")
            if ($LASTEXITCODE -ne 0) { throw "Cold benchmark failed: $name/$label/$i" }
        }
    }
}
