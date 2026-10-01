# Exercise the published Native AOT AppKit horizontal canvas probe with two
# one-line files. The probe drives the real NSView and bottom input ribbon;
# it does not simulate an external trackpad, physical pointer, IME, or client AX.
param(
    [Parameter(Mandatory)][string] $ExecutablePath,
    [Parameter(Mandatory)][ValidateSet('osx-x64', 'osx-arm64')][string] $RuntimeIdentifier
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsMacOS) { throw 'Horizontal AppKit probe requires macOS.' }

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$temp = Join-Path $root '.temp'
$cache = Join-Path $root '.cache'
$reportDir = Join-Path $cache "ci-inventory/$RuntimeIdentifier"
New-Item -ItemType Directory -Force $temp, $cache, $reportDir | Out-Null
$id = [guid]::NewGuid().ToString('N')
$short = Join-Path $temp "horizontal-short-$id.txt"
$long = Join-Path $temp "horizontal-long-$id.txt"
$output = Join-Path $cache "horizontal-$id"
$stdout = Join-Path $reportDir 'horizontal-stdout.txt'
$stderr = Join-Path $reportDir 'horizontal-stderr.txt'
$reportPath = Join-Path $reportDir 'horizontal.json'
$shortOffset = 3000
$remoteOffset = 40 * 1024 * 1024
$shortMarker = 'MOTE_HORIZ_3000'
$remoteMarker = 'MOTE_HORIZ_REMOTE'
$pngNames = @('short-before.png', 'short-after.png', 'remote-before.png', 'remote-after.png')
$shortHash = ''
$longHash = ''
$result = [ordered]@{
    status = 'unverified'
    rid = $RuntimeIdentifier
    marker = ''
    scope = 'published-Mach-O-in-process-AppKit-body-and-input-ribbon-not-external-input'
    short_utf16_units = 16 * 1024
    long_utf16_units = 50 * 1024 * 1024
    short_marker_offset = $shortOffset
    remote_marker_offset = $remoteOffset
    short_sha256_unchanged = $false
    long_sha256_unchanged = $false
    output_directory = $output
    metrics = [ordered]@{}
    png = [ordered]@{}
    error = ''
}

function Write-HorizontalFixture {
    param([string] $Path, [int] $Length, [int] $Offset, [string] $Marker)

    # Stream the 50 MiB fixture so its generation does not allocate a 50 MiB string.
    $buffer = [byte[]]::new(1024 * 1024)
    [Array]::Fill($buffer, [byte][char]'x')
    $file = [IO.File]::Open($Path, [IO.FileMode]::CreateNew,
        [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $remaining = $Length
        while ($remaining -gt 0) {
            $count = [Math]::Min($remaining, $buffer.Length)
            $file.Write($buffer, 0, $count)
            $remaining -= $count
        }
        [void]$file.Seek($Offset, [IO.SeekOrigin]::Begin)
        $markerBytes = [Text.Encoding]::ASCII.GetBytes($Marker)
        $file.Write($markerBytes, 0, $markerBytes.Length)
    }
    finally { $file.Dispose() }
}

function Read-HorizontalMetrics {
    param([string] $Path)

    $values = [ordered]@{}
    $slices = [Collections.Generic.List[int]]::new()
    foreach ($line in [IO.File]::ReadAllLines($Path)) {
        $separator = $line.IndexOf('=')
        if ($separator -lt 1) { throw "Malformed horizontal metric: $line" }
        $key = $line.Substring(0, $separator)
        $value = $line.Substring($separator + 1)
        if ($key -ceq 'max-slice') {
            $parsed = [int]::Parse($value, [Globalization.CultureInfo]::InvariantCulture)
            $slices.Add($parsed)
            continue
        }
        if ($values.Contains($key)) { throw "Duplicate horizontal metric: $key" }
        $values[$key] = $value
    }
    if ($slices.Count -lt 4) { throw 'Missing bounded-slice metrics for all horizontal stages.' }
    foreach ($length in $slices) {
        if ($length -lt 1 -or $length -gt 16 * 1024) {
            throw "Canvas source interval exceeded 16 KiB: $length"
        }
    }
    $values['max-slice'] = @($slices.ToArray())
    return $values
}

function Get-Number {
    param([Collections.IDictionary] $Metrics, [string] $Key)

    if (-not $Metrics.Contains($Key)) { throw "Missing horizontal metric: $Key" }
    return [double]::Parse([string]$Metrics[$Key], [Globalization.CultureInfo]::InvariantCulture)
}

function Assert-HorizontalPng {
    param([string] $Path, [string] $ExpectedHash)

    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 1024) { throw "Horizontal raster is missing or trivial: $Path" }
    if ([Convert]::ToHexString($bytes, 0, 8) -cne '89504E470D0A1A0A') {
        throw "Horizontal raster lacks PNG signature: $Path"
    }
    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if ($hash -cne $ExpectedHash) { throw "Horizontal raster SHA-256 differs from metrics: $Path" }
    return [ordered]@{ bytes = $bytes.Length; sha256 = $hash }
}

try {
    $exe = [IO.Path]::GetFullPath($ExecutablePath)
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Native executable absent: $exe" }
    Write-HorizontalFixture $short (16 * 1024) $shortOffset $shortMarker
    Write-HorizontalFixture $long (50 * 1024 * 1024) $remoteOffset $remoteMarker
    $shortHash = (Get-FileHash -LiteralPath $short -Algorithm SHA256).Hash
    $longHash = (Get-FileHash -LiteralPath $long -Algorithm SHA256).Hash
    New-Item -ItemType Directory -Path $output | Out-Null

    $start = [Diagnostics.ProcessStartInfo]::new($exe)
    $start.WorkingDirectory = $root
    foreach ($argument in @('--check-native-mac-horizontal', $short, $long, $output)) {
        [void]$start.ArgumentList.Add($argument)
    }
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    try {
        $outTask = $process.StandardOutput.ReadToEndAsync()
        $errTask = $process.StandardError.ReadToEndAsync()
        $timedOut = -not $process.WaitForExit(300000)
        if ($timedOut) {
            $process.Kill($true)
            $process.WaitForExit()
        }
        $out = $outTask.GetAwaiter().GetResult().Trim()
        $err = $errTask.GetAwaiter().GetResult().Trim()
        if ($out.Length -gt 32768) { $out = $out.Substring(0, 32768) + "`n<truncated>" }
        if ($err.Length -gt 32768) { $err = $err.Substring(0, 32768) + "`n<truncated>" }
        [IO.File]::WriteAllText($stdout, $out)
        [IO.File]::WriteAllText($stderr, $err)
        if ($timedOut) { throw 'Mac horizontal workflow timed out after five minutes.' }
        if ($process.ExitCode -ne 0) { throw "Mac horizontal probe exited $($process.ExitCode): $err" }
        if ($out -cne 'mote-native-mac-horizontal-ready') {
            throw "Unexpected Mac horizontal success marker: $out"
        }
        $result.marker = $out
    }
    finally { $process.Dispose() }

    $metricsPath = Join-Path $output 'metrics.txt'
    if (-not (Test-Path -LiteralPath $metricsPath -PathType Leaf)) {
        throw 'Mac horizontal metrics.txt is absent.'
    }
    $metrics = Read-HorizontalMetrics $metricsPath
    $result.metrics = $metrics
    if ((Get-Number $metrics 'short-hit') -ne $shortOffset -or
        (Get-Number $metrics 'remote-hit') -ne $remoteOffset) {
        throw 'Canvas hit test did not map to exact global UTF-16 marker offsets.'
    }
    if ((Get-Number $metrics 'remote-left') -ne $remoteOffset -or
        (Get-Number $metrics 'remote-after-left') -le $remoteOffset) {
        throw '50 MiB canvas did not pan its bounded global source window.'
    }
    $afterX = Get-Number $metrics 'short-after-x'
    $hostRibbonX = Get-Number $metrics 'host-ribbon-caret-x'
    $canvasWidth = Get-Number $metrics 'canvas-width'
    if ($afterX -lt 12 -or $afterX -ge $canvasWidth -or
        $hostRibbonX -lt 0 -or $hostRibbonX -ge $canvasWidth) {
        throw 'Source canvas or native ribbon caret escaped its own visible surface.'
    }
    if ((Get-Number $metrics 'short-before-x') -le $afterX) {
        throw 'Short-line caret did not move from offscreen to visible canvas position.'
    }
    if ($metrics['remote-editor-first-responder'] -cne 'True' -or
        (Get-Number $metrics 'minimum-body-height') -le 0 -or
        (Get-Number $metrics 'zero-body-visible-rows') -ne 0 -or
        (Get-Number $metrics 'restored-body-height') -le 0 -or
        $metrics['probe-succeeded'] -cne 'True') {
        throw 'Input ribbon focus or tiny-body source visibility contract failed.'
    }

    foreach ($name in $pngNames) {
        $key = "$name-sha256"
        if (-not $metrics.Contains($key)) { throw "Missing raster hash metric: $key" }
        $result.png[$name] = Assert-HorizontalPng (Join-Path $output $name) $metrics[$key]
    }
    if ($result.png['short-before.png'].sha256 -ceq $result.png['short-after.png'].sha256 -or
        $result.png['remote-before.png'].sha256 -ceq $result.png['remote-after.png'].sha256) {
        throw 'Horizontal canvas before/after rasters are identical.'
    }
    $result.short_sha256_unchanged =
        (Get-FileHash -LiteralPath $short -Algorithm SHA256).Hash -ceq $shortHash
    $result.long_sha256_unchanged =
        (Get-FileHash -LiteralPath $long -Algorithm SHA256).Hash -ceq $longHash
    if (-not $result.short_sha256_unchanged -or -not $result.long_sha256_unchanged) {
        throw 'A horizontal probe modified its input file.'
    }
    $result.status = 'mac-horizontal-workflow-ok'
}
catch {
    $result.error = $_.Exception.Message
}
finally {
    # Preserve mutation evidence even when the native stage or raster check fails.
    if ($shortHash -and (Test-Path -LiteralPath $short -PathType Leaf)) {
        $result.short_sha256_unchanged =
            (Get-FileHash -LiteralPath $short -Algorithm SHA256).Hash -ceq $shortHash
    }
    if ($longHash -and (Test-Path -LiteralPath $long -PathType Leaf)) {
        $result.long_sha256_unchanged =
            (Get-FileHash -LiteralPath $long -Algorithm SHA256).Hash -ceq $longHash
    }
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding utf8NoBOM
    Write-Host ($result | ConvertTo-Json -Depth 8 -Compress)
}
if ($result.status -ne 'mac-horizontal-workflow-ok') { throw $result.error }
