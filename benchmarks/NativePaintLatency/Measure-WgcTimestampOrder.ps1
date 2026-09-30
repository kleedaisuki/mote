# Diagnose WGC/DWM clock ordering using only a self-painted synthetic HWND.
# Numeric frame/clock records remain in repository .cache; no screenshots persist.
param(
    [ValidateRange(1, 5)][int] $Repetitions = 3,
    [ValidateSet('normal', 'same-cpu', 'delay-readback-20ms')]
    [string[]] $Conditions = @('normal', 'same-cpu', 'delay-readback-20ms'),
    [switch] $SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'WGC timestamp ordering requires Windows.' }

# Reject redirected artifact roots before any write, without changing user paths.
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

# Drain both pipes while enforcing a finite watchdog on each visible synthetic test.
function Invoke-ClockProbe {
    param([string] $Tool, [string[]] $Arguments, [string] $RawPath)
    $start = [Diagnostics.ProcessStartInfo]::new($Tool)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $child = [Diagnostics.Process]::Start($start)
    if (-not $child) { throw 'Could not start synthetic WGC clock helper.' }
    try {
        $stdout = $child.StandardOutput.ReadToEndAsync()
        $stderr = $child.StandardError.ReadToEndAsync()
        $timedOut = -not $child.WaitForExit(10000)
        if ($timedOut) {
            $child.Kill()
            if (-not $child.WaitForExit(5000)) { throw 'Synthetic WGC helper could not be reaped.' }
        }
        $body = $stdout.GetAwaiter().GetResult()
        $errorBody = $stderr.GetAwaiter().GetResult()
        [IO.File]::WriteAllText($RawPath, $body)
        [IO.File]::WriteAllText($RawPath + '.stderr.txt', $errorBody)
        if ($timedOut) { throw 'Synthetic WGC helper exceeded 10 seconds.' }
        if ($child.ExitCode -ne 0) { throw "Clock helper exit $($child.ExitCode): $errorBody" }
        return $body
    } finally { $child.Dispose() }
}

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$tool = Join-Path $root '.cache/benchmarks/native-paint-latency/wgc/WgcClockProbe.exe'
$out = Join-Path $root ('.cache/benchmarks/native-paint-latency/wgc/timestamp-order-' +
    [guid]::NewGuid().ToString('N'))
Assert-NoReparseAncestors $tool
Assert-NoReparseAncestors $out
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'Build-WgcObserver.ps1') | Out-Null }
if (-not (Test-Path -LiteralPath $tool -PathType Leaf)) { throw 'WGC clock helper absent.' }
New-Item -ItemType Directory -Path $out | Out-Null
$conditions = $Conditions
$runs = @()
for ($repeat = 0; $repeat -lt $Repetitions; $repeat++) {
    for ($step = 0; $step -lt $conditions.Count; $step++) {
        $condition = $conditions[($repeat + $step) % $conditions.Count]
        $arguments = @('--timestamp-order')
        if ($condition -ne 'normal') { $arguments += "--$condition" }
        $rawName = "$repeat-$condition.csv"
        try { $body = Invoke-ClockProbe $tool $arguments (Join-Path $out $rawName) }
        catch {
            [ordered]@{ condition = $condition; repetition = $repeat; status = 'helper_failed'
                error = $_.Exception.Message } | ConvertTo-Json |
                Set-Content (Join-Path $out "$repeat-$condition-failure.json")
            throw
        }
        $rows = @($body -split '\r?\n' | Where-Object { $_ })
        $meta = $rows[0].Split(',')
        if ($meta.Count -ne 6 -or $meta[0] -ne 'META') { throw 'Clock META missing.' }
        $frequency = [decimal]::Parse($meta[1], [Globalization.CultureInfo]::InvariantCulture)
        $frames = @{}; $orders = @{}; $toggles = @()
        foreach ($row in $rows | Select-Object -Skip 1) {
            $f = $row.Split(',')
            switch ($f[0]) {
                'TOGGLE' { if ($f.Count -ne 5) { throw 'Bad TOGGLE.' }; $toggles += ,$f }
                'FRAME' { if ($f.Count -ne 6) { throw 'Bad FRAME.' }; $frames[[int]$f[1]] = $f }
                'ORDER' { if ($f.Count -ne 12) { throw 'Bad ORDER.' }; $orders[[int]$f[1]] = $f }
                default { throw "Unexpected clock record: $row" }
            }
        }
        if ($toggles.Count -ne 6 -or $orders.Count -ne $frames.Count) { throw 'Incomplete clock record.' }
        $paired = @()
        for ($i = 0; $i -lt $toggles.Count; $i++) {
            $t = $toggles[$i]
            $limit = if ($i + 1 -lt $toggles.Count) { [long]$toggles[$i + 1][3] } else { [long]::MaxValue }
            $matches = @($frames.Keys | Sort-Object | Where-Object {
                $f = $frames[$_]
                $f[2] -eq $t[2] -and [long]$f[4] -ge [long]$t[4] -and [long]$f[4] -lt $limit
            } | Select-Object -First 1)
            if (-not $matches.Count) { $paired += [ordered]@{ toggle = $i; matched = $false }; continue }
            $f = $frames[$matches[0]]; $o = $orders[$matches[0]]
            $system = [decimal]$f[3]
            $dwmAvailable = $o[6] -eq '00000000'
            # Decimal rational conversion preserves the raw QPC comparison; no fixed 10 MHz assumption.
            $paired += [ordered]@{
                toggle = $i; matched = $true; frame_index = $matches[0]
                metadata_minus_arrival_ms = [double](($system - [decimal]$f[4] * 10000000 / $frequency) / 10000)
                metadata_minus_readback_ms = [double](($system - [decimal]$f[5] * 10000000 / $frequency) / 10000)
                metadata_minus_property_after_ms = [double](($system - [decimal]$o[3] * 10000000 / $frequency) / 10000)
                metadata_bracket_ms = [double](([decimal]$o[3] - [decimal]$o[2]) * 1000 / $frequency)
                dwm_hresult = $o[6]
                dwm_available = $dwmAvailable
                metadata_minus_dwm_compose_ms = if ($dwmAvailable -and [decimal]$o[7] -gt 0) {
                    [double](($system - [decimal]$o[7] * 10000000 / $frequency) / 10000)
                } else { $null }
                metadata_minus_dwm_vblank_ms = if ($dwmAvailable -and [decimal]$o[8] -gt 0) {
                    [double](($system - [decimal]$o[8] * 10000000 / $frequency) / 10000)
                } else { $null }
                dwm_query_ms = [double](([decimal]$o[5] - [decimal]$o[4]) * 1000 / $frequency)
                dwm_refresh_period_ms = if ($dwmAvailable -and [decimal]$o[11] -gt 0) {
                    [double]([decimal]$o[11] * 1000 / $frequency)
                } else { $null }
            }
        }
        $runs += [ordered]@{
            repetition = $repeat; condition = $condition; raw_csv = $rawName
            qpc_frequency = [long]$meta[1]; warp = [bool][int]$meta[2]
            vector_overflow = [int]$meta[3]; capture_errors = [int]$meta[4]
            total_frames = [int]$meta[5]; paired = $paired
        }
    }
}
$summary = [ordered]@{
    schema_version = 1
    os = [Runtime.InteropServices.RuntimeInformation]::OSDescription
    logical_processors = [Environment]::ProcessorCount
    source_sha256 = (Get-FileHash (Join-Path $PSScriptRoot 'WgcClockProbe.cpp')).Hash
    tool_sha256 = (Get-FileHash $tool).Hash
    runs = $runs
    interpretation = 'Synthetic clock calibration only; not product input-to-present, first desktop frame, or photons.'
}
$summary | ConvertTo-Json -Depth 7 | Set-Content (Join-Path $out 'summary.json')
Write-Output "wgc-timestamp-order $(Join-Path $out 'summary.json')"
