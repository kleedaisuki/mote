# Calibrate WGC SystemRelativeTime against QPC on a self-painted synthetic HWND.
# The helper copies only its own window's center pixel; persisted output is numeric.
param(
    [switch] $SkipBuild,
    [string] $ExistingRawPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'WGC clock calibration requires Windows.' }

# A redirected .cache would violate the repository-local artifact contract.
function Assert-NoReparseAncestors {
    param([string] $Path)
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -ErrorAction Stop
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Reparse-point artifact path forbidden: $cursor"
            }
        }
        $parent = [IO.Path]::GetDirectoryName($cursor)
        if (-not $parent -or $parent -ceq $cursor) { break }
        $cursor = $parent
    }
}

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$cacheRoot = [IO.Path]::GetFullPath((Join-Path $root '.cache'))
$tool = Join-Path $root '.cache/benchmarks/native-paint-latency/wgc/WgcClockProbe.exe'
$source = Join-Path $PSScriptRoot 'WgcClockProbe.cpp'
$outDir = Join-Path $root ('.cache/benchmarks/native-paint-latency/wgc/clock-' +
    [guid]::NewGuid().ToString('N'))
Assert-NoReparseAncestors $tool
Assert-NoReparseAncestors $outDir
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$rawPath = Join-Path $outDir 'clock-marks.csv'
$summaryPath = Join-Path $outDir 'summary.json'

if ($ExistingRawPath) {
    $existing = [IO.Path]::GetFullPath($ExistingRawPath)
    Assert-NoReparseAncestors $existing
    if (-not $existing.StartsWith($cacheRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $existing -PathType Leaf)) {
        throw 'Existing clock marks must be a repository .cache file.'
    }
    $body = [IO.File]::ReadAllText($existing)
} else {
    if (-not $SkipBuild -and (-not (Test-Path -LiteralPath $tool -PathType Leaf) -or
        (Get-Item -LiteralPath $source).LastWriteTimeUtc -gt
        (Get-Item -LiteralPath $tool).LastWriteTimeUtc)) {
        & (Join-Path $PSScriptRoot 'Build-WgcObserver.ps1') | Out-Null
    }
    if (-not (Test-Path -LiteralPath $tool -PathType Leaf)) { throw 'WGC clock helper absent.' }
    # Drain both pipes asynchronously so a diagnostic exception cannot deadlock the watchdog.
    $start = [Diagnostics.ProcessStartInfo]::new($tool)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $helper = [Diagnostics.Process]::Start($start)
    if (-not $helper) { throw 'Could not start synthetic WGC clock helper.' }
    try {
        $stdout = $helper.StandardOutput.ReadToEndAsync()
        $stderr = $helper.StandardError.ReadToEndAsync()
        if (-not $helper.WaitForExit(10000)) {
            $helper.Kill()
            [void]$helper.WaitForExit(5000)
            throw 'Synthetic WGC clock helper exceeded 10 seconds.'
        }
        $body = $stdout.GetAwaiter().GetResult()
        $errors = $stderr.GetAwaiter().GetResult()
        if ($helper.ExitCode -ne 0) {
            throw "Synthetic WGC clock helper exit $($helper.ExitCode)`: $errors"
        }
    }
    finally { $helper.Dispose() }
}
[IO.File]::WriteAllText($rawPath, $body)

# Pair only an exact synthetic color frame after each synchronous WM_PAINT.
$rows = @($body -split '\r?\n' | Where-Object { $_ -ne '' })
$meta = $rows[0].Split(',')
if ($meta.Count -ne 6 -or $meta[0] -ne 'META') { throw 'Clock helper META absent.' }
$frequency = [double]::Parse($meta[1], [Globalization.CultureInfo]::InvariantCulture)
$frequencyDecimal = [decimal]::Parse($meta[1], [Globalization.CultureInfo]::InvariantCulture)
$toggles = @()
$frames = @()
foreach ($row in $rows | Select-Object -Skip 1) {
    $fields = $row.Split(',')
    if ($fields[0] -eq 'TOGGLE' -and $fields.Count -eq 5) {
        $toggles += [pscustomobject]@{
            color = [int]$fields[2]; before = [long]$fields[3]
            after_paint = [long]$fields[4]
        }
    } elseif ($fields[0] -eq 'FRAME' -and $fields.Count -eq 6) {
        $frames += [pscustomobject]@{
            color = [int]$fields[2]; system_100ns = [long]$fields[3]
            arrival = [long]$fields[4]; processed = [long]$fields[5]
        }
    } else { throw "Unexpected clock helper record: $row" }
}
if ($toggles.Count -ne 6 -or $frames.Count -lt 1) {
    throw 'Clock helper omitted synthetic color transitions or all capture frames.'
}
$paired = @()
for ($i = 0; $i -lt $toggles.Count; $i++) {
    $toggle = $toggles[$i]
    $limit = if ($i + 1 -lt $toggles.Count) { $toggles[$i + 1].before } `
        else { [long]::MaxValue }
    $frame = @($frames | Where-Object {
        $_.color -eq $toggle.color -and $_.arrival -ge $toggle.after_paint -and
        $_.arrival -lt $limit
    } | Select-Object -First 1)
    if (-not $frame.Count) {
        $paired += [ordered]@{ toggle = $i; matched = $false }
        continue
    }
    $mark = $frame[0]
    $arrival100ns = [decimal]$mark.arrival * 10000000 / $frequencyDecimal
    $paint100ns = [decimal]$toggle.after_paint * 10000000 / $frequencyDecimal
    $paired += [ordered]@{
        toggle = $i; matched = $true
        metadata_minus_arrival_ms = [Math]::Round(
            [double](([decimal]$mark.system_100ns - $arrival100ns) / 10000), 4)
        metadata_minus_paint_ms = [Math]::Round(
            [double](([decimal]$mark.system_100ns - $paint100ns) / 10000), 4)
        callback_minus_paint_ms = [Math]::Round(1000 *
            ($mark.arrival - $toggle.after_paint) / $frequency, 4)
        observer_processing_ms = [Math]::Round(1000 *
            ($mark.processed - $mark.arrival) / $frequency, 4)
    }
}
$future = @($paired | Where-Object {
    $_.matched -and $_.metadata_minus_arrival_ms -gt 0.001
}).Count
$toolSha = if (Test-Path -LiteralPath $tool -PathType Leaf) {
    (Get-FileHash -LiteralPath $tool -Algorithm SHA256).Hash
} else { $null }
$summary = [ordered]@{
    schema_version = 1
    raw_provenance = if ($ExistingRawPath) { 'existing_numeric_csv' } else { 'new_helper_run' }
    run_tool_sha256 = if ($ExistingRawPath) { $null } else { $toolSha }
    current_parser_host_tool_sha256 = $toolSha
    os = [Runtime.InteropServices.RuntimeInformation]::OSDescription
    qpc_frequency = [long]$meta[1]; warp = [bool][int]$meta[2]
    sample_vector_overflow = [int]$meta[3]; capture_errors = [int]$meta[4]
    total_frames = [int]$meta[5]; matched_toggles = @($paired | Where-Object { $_.matched }).Count
    future_metadata_count = $future; paired = $paired
    interpretation = 'WGC metadata is not a validated input-to-present or photon timestamp.'
}
$summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $summaryPath
Write-Output "wgc-clock-calibration $summaryPath"
