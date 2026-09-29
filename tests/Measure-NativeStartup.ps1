# Measure process-level native GUI smoke latency on a hosted runner, without a hard threshold.
param(
    [Parameter(Mandatory)][string] $ExecutablePath,
    [Parameter(Mandatory)][string] $RuntimeIdentifier,
    [ValidateRange(3, 100)][int] $WarmRuns = 10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Keep every probe output under the checkout, including redirected GUI-process streams.
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$scratchRoot = [IO.Path]::GetFullPath((Join-Path $root '.temp/benchmarks'))
$scratch = [IO.Path]::GetFullPath((Join-Path $scratchRoot ("native-startup-$RuntimeIdentifier-" + [guid]::NewGuid().ToString('N'))))
if (-not $scratch.StartsWith($scratchRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Native-startup scratch path escaped the repository.'
}
New-Item -ItemType Directory -Force -Path $scratch | Out-Null

# Nearest-rank percentiles avoid implying resolution finer than the small sample supports.
function Get-NearestRank {
    param([double[]] $SortedValues, [double] $Fraction)
    $index = [Math]::Max(0, [Math]::Ceiling($Fraction * $SortedValues.Length) - 1)
    return $SortedValues[$index]
}

# Wait explicitly: on Windows, a GUI-subsystem executable may outlive a bare PowerShell & call.
function Invoke-NativeSmoke {
    param([string] $Exe, [string] $Directory, [int] $Index)
    $stdout = Join-Path $Directory ("stdout-$Index.txt")
    $stderr = Join-Path $Directory ("stderr-$Index.txt")
    $start = @{
        FilePath = $Exe
        ArgumentList = '--smoke-gui'
        PassThru = $true
        Wait = $true
        RedirectStandardOutput = $stdout
        RedirectStandardError = $stderr
    }
    if ($IsWindows) { $start.WindowStyle = 'Hidden' }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process @start
    $watch.Stop()
    $output = (Get-Content -LiteralPath $stdout -Raw).Trim()
    if ($process.ExitCode -ne 0 -or $output -ne 'mote-native-gui-ready') {
        throw "Native GUI smoke failed at sample $Index (exit $($process.ExitCode), output '$output'): $(Get-Content -LiteralPath $stderr -Raw)"
    }
    return $watch.Elapsed.TotalMilliseconds
}

try {
    $exe = [IO.Path]::GetFullPath($ExecutablePath)
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Native executable not found: $exe" }
    $cold = Invoke-NativeSmoke $exe $scratch 0
    $samples = [double[]]@()
    for ($i = 1; $i -le $WarmRuns; $i++) {
        $samples += Invoke-NativeSmoke $exe $scratch $i
    }
    [Array]::Sort($samples)
    $result = [ordered]@{
        timestamp_utc = [DateTimeOffset]::UtcNow.ToString('O')
        runtime_identifier = $RuntimeIdentifier
        os = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        process_architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        metric = 'process-start-to-smoke-gui-exit, not first paint'
        cold_ms = $cold
        warm_runs = $WarmRuns
        warm_p50_ms = Get-NearestRank $samples 0.50
        warm_p95_ms = Get-NearestRank $samples 0.95
        warm_samples_ms = $samples
    }
    $json = $result | ConvertTo-Json -Depth 5 -Compress
    $outputDirectory = Join-Path $root '.cache/benchmarks'
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
    Add-Content -LiteralPath (Join-Path $outputDirectory 'native-startup.jsonl') -Value $json -Encoding utf8
    Write-Output $json
}
finally {
    if (-not $scratch.StartsWith($scratchRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing recursive cleanup outside repository .temp/benchmarks.'
    }
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
}
