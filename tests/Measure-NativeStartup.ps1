# Measure process-start-to-GUI-smoke-exit, not first editable frame or physical paint.
param(
    [Parameter(Mandatory)][string] $ExecutablePath,
    [Parameter(Mandatory)][string] $RuntimeIdentifier,
    [ValidateRange(3, 100)][int] $WarmRuns = 10,
    [switch] $CompareTelemetry
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# All child output, trace files, and temporary state remain inside this checkout.
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$scratchRoot = [IO.Path]::GetFullPath((Join-Path $root '.temp/benchmarks'))
$scratch = [IO.Path]::GetFullPath((Join-Path $scratchRoot ("native-startup-$RuntimeIdentifier-" + [guid]::NewGuid().ToString('N'))))
if (-not $scratch.StartsWith($scratchRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Native-startup scratch path escaped the repository.'
}
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
$moteHome = Join-Path $scratch 'mote-home'
New-Item -ItemType Directory -Force -Path $moteHome | Out-Null

# Return the conventional nearest-rank statistic; three samples cannot justify a tail SLA.
function Get-NearestRank {
    param([double[]] $Values, [double] $Fraction)
    $sorted = [double[]]@($Values | Sort-Object)
    if ($sorted.Length -eq 0) { return $null }
    $index = [Math]::Max(0, [Math]::Ceiling($Fraction * $sorted.Length) - 1)
    return $sorted[$index]
}

# Start and explicitly poll a GUI-subsystem process, retaining a sampled RSS lower bound.
function Invoke-NativeSmoke {
    param([string] $Exe, [string] $Directory, [string] $TraceMode, [int] $Index,
        [string] $Phase, [string] $MoteHome)

    $prefix = "$TraceMode-$Index"
    $stdout = Join-Path $Directory "$prefix-stdout.txt"
    $stderr = Join-Path $Directory "$prefix-stderr.txt"
    $start = @{
        FilePath = $Exe
        ArgumentList = '--smoke-gui'
        WorkingDirectory = $root
        PassThru = $true
        RedirectStandardOutput = $stdout
        RedirectStandardError = $stderr
        Environment = @{
            MOTE_HOME = $MoteHome
            MOTE_TRACE = if ($TraceMode -eq 'on') { '1' } else { '0' }
        }
    }
    if ($IsWindows) { $start.WindowStyle = 'Hidden' }

    $watch = [Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process @start
    $peak = [long]0
    $memorySamples = 0
    while (-not $process.HasExited) {
        if ($watch.Elapsed.TotalSeconds -gt 30) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            throw "Native GUI smoke exceeded 30 seconds at sample $prefix."
        }
        try {
            $process.Refresh()
            $peak = [Math]::Max($peak, [long]$process.PeakWorkingSet64)
            $memorySamples++
        }
        catch { }
        Start-Sleep -Milliseconds 2
    }
    $process.WaitForExit()
    $watch.Stop()

    $output = (Get-Content -LiteralPath $stdout -Raw).Trim()
    if ($process.ExitCode -ne 0 -or $output -ne 'mote-native-gui-ready') {
        throw "Native GUI smoke failed at sample $prefix (exit $($process.ExitCode), output '$output'): $(Get-Content -LiteralPath $stderr -Raw)"
    }
    $traces = Join-Path $MoteHome 'traces'
    $traceBytes = [long]0
    if (Test-Path -LiteralPath $traces) {
        foreach ($file in Get-ChildItem -LiteralPath $traces -File -Recurse) { $traceBytes += $file.Length }
    }
    return [ordered]@{
        timestamp_utc = [DateTimeOffset]::UtcNow.ToString('O')
        runtime_identifier = $RuntimeIdentifier
        phase = $Phase
        telemetry = $TraceMode
        ordinal = $Index
        process_start_to_smoke_exit_ms = $watch.Elapsed.TotalMilliseconds
        # macOS can report zero throughout a short smoke run; zero is unavailable,
        # not evidence that the GUI process consumed no resident memory.
        observed_peak_working_set_bytes = if ($peak -gt 0) { $peak } else { $null }
        memory_sample_count = $memorySamples
        cumulative_trace_bytes = $traceBytes
    }
}

try {
    $exe = [IO.Path]::GetFullPath($ExecutablePath)
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Native executable not found: $exe" }
    $outputDirectory = Join-Path $root '.cache/benchmarks'
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
    $samplePath = Join-Path $outputDirectory 'native-startup-samples.jsonl'
    $samples = [Collections.Generic.List[object]]::new()

    $first = Invoke-NativeSmoke $exe $scratch 'off' 0 'first-run' $moteHome
    $samples.Add($first)
    Add-Content -LiteralPath $samplePath -Value ($first | ConvertTo-Json -Depth 5 -Compress) -Encoding utf8

    for ($i = 1; $i -le $WarmRuns; $i++) {
        # Alternate order so a warming cache or changing runner load does not always favor tracing.
        $modes = if (-not $CompareTelemetry) { @('off') }
            elseif ($i % 2 -eq 1) { @('off', 'on') } else { @('on', 'off') }
        foreach ($mode in $modes) {
            $sample = Invoke-NativeSmoke $exe $scratch $mode $i 'warm' $moteHome
            $samples.Add($sample)
            Add-Content -LiteralPath $samplePath -Value ($sample | ConvertTo-Json -Depth 5 -Compress) -Encoding utf8
        }
    }

    $off = [double[]]@($samples | Where-Object { $_.phase -eq 'warm' -and $_.telemetry -eq 'off' } |
        ForEach-Object { $_.process_start_to_smoke_exit_ms })
    $on = [double[]]@($samples | Where-Object { $_.phase -eq 'warm' -and $_.telemetry -eq 'on' } |
        ForEach-Object { $_.process_start_to_smoke_exit_ms })
    $offPeak = [double[]]@($samples | Where-Object { $_.phase -eq 'warm' -and $_.telemetry -eq 'off' -and
        $null -ne $_.observed_peak_working_set_bytes } | ForEach-Object { $_.observed_peak_working_set_bytes })
    $onPeak = [double[]]@($samples | Where-Object { $_.phase -eq 'warm' -and $_.telemetry -eq 'on' -and
        $null -ne $_.observed_peak_working_set_bytes } | ForEach-Object { $_.observed_peak_working_set_bytes })
    $deltas = [double[]]@()
    if ($CompareTelemetry) {
        for ($i = 1; $i -le $WarmRuns; $i++) {
            $offSample = $samples | Where-Object { $_.ordinal -eq $i -and $_.telemetry -eq 'off' } | Select-Object -First 1
            $onSample = $samples | Where-Object { $_.ordinal -eq $i -and $_.telemetry -eq 'on' } | Select-Object -First 1
            $deltas += $onSample.process_start_to_smoke_exit_ms - $offSample.process_start_to_smoke_exit_ms
        }
    }
    $result = [ordered]@{
        schema_version = 2
        timestamp_utc = [DateTimeOffset]::UtcNow.ToString('O')
        runtime_identifier = $RuntimeIdentifier
        os = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        process_architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        runner_name = $env:RUNNER_NAME
        executable_sha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
        metric = 'process-start-to-smoke-gui-exit, not first editable frame or physical paint'
        cold_definition = 'first run after publish on this runner; filesystem cache was not evicted'
        cold_ms = $first.process_start_to_smoke_exit_ms
        cold_observed_peak_working_set_bytes = $first.observed_peak_working_set_bytes
        warm_runs = $WarmRuns
        warm_p50_ms = Get-NearestRank $off 0.50
        warm_p95_ms = Get-NearestRank $off 0.95
        warm_samples_ms = $off
        warm_observed_peak_working_set_p50_bytes = Get-NearestRank $offPeak 0.50
        telemetry_comparison = [bool]$CompareTelemetry
        telemetry_on_warm_p50_ms = Get-NearestRank $on 0.50
        telemetry_on_warm_p95_ms = Get-NearestRank $on 0.95
        telemetry_on_warm_samples_ms = $on
        telemetry_on_observed_peak_working_set_p50_bytes = Get-NearestRank $onPeak 0.50
        telemetry_on_minus_off_paired_samples_ms = $deltas
        telemetry_on_minus_off_paired_p50_ms = Get-NearestRank $deltas 0.50
        telemetry_trace_bytes_after_run = ($samples | Select-Object -Last 1).cumulative_trace_bytes
    }
    $json = $result | ConvertTo-Json -Depth 6 -Compress
    Add-Content -LiteralPath (Join-Path $outputDirectory 'native-startup.jsonl') -Value $json -Encoding utf8
    Write-Output $json
}
finally {
    if (-not $scratch.StartsWith($scratchRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing recursive cleanup outside repository .temp/benchmarks.'
    }
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
}
