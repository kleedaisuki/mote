# Opt-in, reversible Apple Pinyin activation on a disposable GitHub-hosted Mac.
# This does not open mote, send keys, capture a screen, or establish real IME input.
param(
    [Parameter(Mandatory)][ValidateSet('osx-x64', 'osx-arm64')][string] $RuntimeIdentifier,
    [Parameter(Mandatory)][string] $ReportPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsMacOS -or $env:GITHUB_ACTIONS -cne 'true' -or
    $env:RUNNER_ENVIRONMENT -cne 'github-hosted' -or $env:RUNNER_OS -cne 'macOS') {
    throw 'Refusing input-source mutation outside a GitHub-hosted macOS Actions job.'
}
$expectedArch = if ($RuntimeIdentifier -eq 'osx-x64') { 'X64' } else { 'ARM64' }
if ($env:RUNNER_ARCH -cne $expectedArch) { throw 'RID does not match hosted runner architecture.' }

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$workspace = [IO.Path]::GetFullPath($env:GITHUB_WORKSPACE)
if (-not [string]::Equals($workspace, $root, [StringComparison]::Ordinal)) {
    throw 'Probe root is not the checked-out GitHub workspace.'
}
$reportRoot = [IO.Path]::GetFullPath((Join-Path $root '.cache/ci-inventory'))
$report = [IO.Path]::GetFullPath($ReportPath)
if (-not $report.StartsWith($reportRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::Ordinal)) {
    throw 'Activation report must remain under repository .cache/ci-inventory.'
}
$scratch = Join-Path $root ('.temp/mac-real-ime-activation/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $scratch, (Split-Path $report) | Out-Null
$binary = Join-Path $scratch 'activate'
$compiler = [Diagnostics.ProcessStartInfo]::new('/usr/bin/clang')
foreach ($arg in @('-fobjc-arc', '-Wall', '-Wextra', '-Werror',
    '-framework', 'Foundation', '-framework', 'Carbon',
    '-o', $binary, (Join-Path $PSScriptRoot 'Activate.m'))) {
    [void]$compiler.ArgumentList.Add($arg)
}
$compiler.UseShellExecute = $false
$compiler.RedirectStandardOutput = $true
$compiler.RedirectStandardError = $true
$build = [Diagnostics.Process]::Start($compiler)
try {
    if (-not $build.WaitForExit(30000)) {
        $build.Kill(); $build.WaitForExit()
        throw 'Activation clang build exceeded 30 seconds.'
    }
    $stderr = $build.StandardError.ReadToEnd()
    if ($build.ExitCode -ne 0) { throw "Activation clang failed: $stderr" }
} finally { $build.Dispose() }

$start = [Diagnostics.ProcessStartInfo]::new($binary)
$start.UseShellExecute = $false
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$probe = [Diagnostics.Process]::Start($start)
try {
    if (-not $probe.WaitForExit(20000)) {
        $probe.Kill(); $probe.WaitForExit()
        throw 'Activation probe exceeded 20 seconds; disposable runner must be discarded.'
    }
    $json = $probe.StandardOutput.ReadToEnd()
    $stderr = $probe.StandardError.ReadToEnd()
    $exitCode = $probe.ExitCode
} finally { $probe.Dispose() }

if ([string]::IsNullOrWhiteSpace($json)) { throw "Activation did not emit a JSON report: $stderr" }
$data = $json | ConvertFrom-Json -ErrorAction Stop
if ($data.schema -cne 'mote.mac-real-ime-activation.v1' -or $data.real_ime_tested -ne $false) {
    throw 'Activation report schema invalid or mislabeled as real IME input.'
}
[IO.File]::WriteAllText($report, $json, [Text.UTF8Encoding]::new($false))
Write-Host "Disposable hosted-Mac activation report: $report"
Write-Host "Activation passed: $($data.activation_passed); restoration passed: $($data.restoration_passed); real IME tested: false"
if ($exitCode -ne 0 -or -not $data.restoration_passed) {
    throw "Pinyin activation or restoration failed; inspect scoped report. Helper exit=$exitCode. $stderr"
}
