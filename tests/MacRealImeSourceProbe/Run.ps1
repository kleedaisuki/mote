# Compile and run a read-only macOS input-source/TCC inventory in this repository.
# No input source is enabled or selected; no keyboard event or document text is read.
param([Parameter(Mandatory)][string] $ReportPath)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsMacOS) { throw 'macOS is required.' }

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$scratchRoot = [IO.Path]::GetFullPath((Join-Path $root '.temp/mac-real-ime-source'))
$reportRoot = [IO.Path]::GetFullPath((Join-Path $root '.cache/ci-inventory'))
$report = [IO.Path]::GetFullPath($ReportPath)
if (-not $report.StartsWith($reportRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::Ordinal)) {
    throw 'Report must remain under repository .cache/ci-inventory.'
}
$scratch = Join-Path $scratchRoot ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $scratch, (Split-Path $report) | Out-Null
$binary = Join-Path $scratch 'inventory'
$compiler = [Diagnostics.ProcessStartInfo]::new('/usr/bin/clang')
foreach ($arg in @('-fobjc-arc', '-Wall', '-Wextra', '-Werror',
    '-framework', 'Foundation', '-framework', 'Carbon',
    '-framework', 'ApplicationServices', '-framework', 'CoreGraphics',
    '-o', $binary, (Join-Path $PSScriptRoot 'Inventory.m'))) {
    [void]$compiler.ArgumentList.Add($arg)
}
$compiler.UseShellExecute = $false
$compiler.RedirectStandardOutput = $true
$compiler.RedirectStandardError = $true
$build = [Diagnostics.Process]::Start($compiler)
try {
    if (-not $build.WaitForExit(30000)) {
        $build.Kill()
        $build.WaitForExit()
        throw 'Inventory clang build timed out after 30 seconds.'
    }
    $stderr = $build.StandardError.ReadToEnd()
    if ($build.ExitCode -ne 0) { throw "Inventory clang failed: $stderr" }
} finally { $build.Dispose() }

$start = [Diagnostics.ProcessStartInfo]::new($binary)
$start.UseShellExecute = $false
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$probe = [Diagnostics.Process]::Start($start)
try {
    if (-not $probe.WaitForExit(10000)) {
        $probe.Kill()
        $probe.WaitForExit()
        throw 'Input-source inventory timed out after 10 seconds.'
    }
    $json = $probe.StandardOutput.ReadToEnd()
    $stderr = $probe.StandardError.ReadToEnd()
    if ($probe.ExitCode -ne 0) { throw "Input-source inventory failed: $stderr" }
} finally { $probe.Dispose() }

$data = $json | ConvertFrom-Json -ErrorAction Stop
if ($data.schema -cne 'mote.mac-real-ime-source-inventory.v1' -or
    $data.real_ime_tested -ne $false) {
    throw 'Inventory schema invalid or mislabeled as a real IME pass.'
}
[IO.File]::WriteAllText($report, $json, [Text.UTF8Encoding]::new($false))
Write-Host "Read-only macOS input-source inventory: $report"
Write-Host "Pinyin candidates: $(@($data.pinyin_sources).Count); real IME tested: false"
