<#
.SYNOPSIS
Summarizes raw isolated-process and per-edit JSON measurements without hiding individual samples.
.DESCRIPTION
Cold medians and observed extrema use five processes; no significance is inferred from only five pairs.
Warm p95 is nearest-rank, includes the first analyzed edit, and never excludes an outlier.
.EXAMPLE
pwsh -File benchmarks/JsonArrayPages/Summarize.ps1 -InputDirectory .cache/json-array-pages/pairs
#>
param([Parameter(Mandatory = $true)][string]$InputDirectory)
$ErrorActionPreference = 'Stop'

# Returns deterministic nearest-rank summaries, including every supplied observation.
function Stats($values) {
    $v = @($values | Sort-Object)
    if ($v.Count -eq 0) { throw 'No observations.' }
    $median = if ($v.Count % 2) { $v[[int][Math]::Floor($v.Count / 2)] } else { ($v[$v.Count / 2 - 1] + $v[$v.Count / 2]) / 2 }
    return [ordered]@{ count = $v.Count; median = $median; min = $v[0]; max = $v[-1]; p95 = $v[[Math]::Ceiling($v.Count * 0.95) - 1] }
}

$files = @(Get-ChildItem -LiteralPath $InputDirectory -Filter '*.json' | Where-Object { $_.Name -notin @('environment.json', 'summary.json') })
$cold = @(); $warm = @()
foreach ($file in $files) {
    $result = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    if (!$result.fixture) { continue }
    $name = "$($result.fixture.Kind)-$($result.fixture.Newline)-$($result.fixture.MiB)"
    if ($result.mode -eq 'cold') {
        if ($file.BaseName -notmatch '-(baseline|candidate)-(\d+)$') { throw "Bad pair filename: $file" }
        $cold += [pscustomobject]@{ name = $name; label = $Matches[1]; pair = [int]$Matches[2]; result = $result; measure = $result.measurements[0] }
        continue
    }
    foreach ($phase in @('local-edit', 'dispersed-edit')) {
        $edits = @($result.measurements | Where-Object { $_.phase -eq $phase })
        if ($edits.Count -eq 0) { continue }
        $warm += [ordered]@{
            corpus = $name; phase = $phase; timingMs = (Stats ($edits | ForEach-Object elapsedMs))
            allocationBytes = (Stats ($edits | ForEach-Object allocatedBytes))
            visitedUnits = (Stats ($edits | ForEach-Object { $_.state.LastVisitedUnits }))
            pageCount = (Stats ($edits | ForEach-Object { $_.state.ArrayPageCount }))
            certificateLayout = $result.certificateLayout; afterEditsLive = $result.afterEditsLive
            editsLiveDelta = $result.editsLiveDelta; finalPeakWorking = $result.finalPeakWorking
        }
    }
}
$pairs = @()
foreach ($group in ($cold | Group-Object name | Sort-Object Name)) {
    $b = @($group.Group | Where-Object label -eq baseline | Sort-Object pair)
    $c = @($group.Group | Where-Object label -eq candidate | Sort-Object pair)
    if ($b.Count -eq 0 -or $c.Count -eq 0) { continue }
    if ($b.Count -ne $c.Count) { continue }
    $ratios = @()
    for ($i = 0; $i -lt $b.Count; $i++) {
        if ($b[$i].pair -ne $c[$i].pair -or $b[$i].result.fixture.Sha256 -ne $c[$i].result.fixture.Sha256 -or
            $b[$i].result.formatsAssemblySha256 -ne $b[0].result.formatsAssemblySha256 -or
            $c[$i].result.formatsAssemblySha256 -ne $c[0].result.formatsAssemblySha256) { throw 'Pair identity mismatch.' }
        $ratios += $c[$i].measure.elapsedMs / $b[$i].measure.elapsedMs - 1
    }
    $pairs += [ordered]@{
        corpus = $group.Name; baselineMs = (Stats ($b | ForEach-Object { $_.measure.elapsedMs }))
        candidateMs = (Stats ($c | ForEach-Object { $_.measure.elapsedMs })); pairedChange = (Stats $ratios)
        baselineAllocationBytes = (Stats ($b | ForEach-Object { $_.measure.allocatedBytes }))
        candidateAllocationBytes = (Stats ($c | ForEach-Object { $_.measure.allocatedBytes }))
        baselinePeakWorking = (Stats ($b | ForEach-Object { $_.result.finalPeakWorking }))
        candidatePeakWorking = (Stats ($c | ForEach-Object { $_.result.finalPeakWorking }))
        certificateLayout = $c[0].result.certificateLayout
        baselineAssemblySha256 = $b[0].result.formatsAssemblySha256; candidateAssemblySha256 = $c[0].result.formatsAssemblySha256
        fixtureSha256 = $c[0].result.fixture.Sha256
    }
}
$summary = [ordered]@{ pairs = $pairs; warm = $warm }
$summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $InputDirectory 'summary.json') -Encoding utf8
$pairs | ForEach-Object { "$($_.corpus): $([Math]::Round($_.baselineMs.median, 3)) -> $([Math]::Round($_.candidateMs.median, 3)) ms; paired median $([Math]::Round($_.pairedChange.median * 100, 2))%" }
$warm | ForEach-Object { "$($_.corpus) $($_.phase): $($_.timingMs.count) edits p50=$([Math]::Round($_.timingMs.median, 3)) p95=$([Math]::Round($_.timingMs.p95, 3)) max=$([Math]::Round($_.timingMs.max, 3)) ms, maxVisits=$($_.visitedUnits.max)" }
