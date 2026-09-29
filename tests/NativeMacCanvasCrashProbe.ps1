# Capture a bounded LLDB backtrace for the published macOS ARM canvas path.
# This is diagnostic only: a debugger denial is not a product failure, and
# a successful smoke does not prove the larger interactive editor workflow.
param(
    [Parameter(Mandatory)][string] $ExecutablePath,
    [Parameter(Mandatory)][ValidateSet('osx-arm64')][string] $RuntimeIdentifier
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsMacOS) { throw 'The native macOS crash probe requires macOS.' }

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$exe = [IO.Path]::GetFullPath($ExecutablePath)
$temp = [IO.Path]::GetFullPath((Join-Path $root '.temp'))
$reports = [IO.Path]::GetFullPath((Join-Path $root ".cache/ci-inventory/$RuntimeIdentifier"))
New-Item -ItemType Directory -Force $temp, $reports | Out-Null
$reportPath = Join-Path $reports 'mac-canvas-crash.json'
$report = [ordered]@{
    status = 'unverified'
    rid = $RuntimeIdentifier
    executable_sha256 = ''
    smoke = $null
    clipboard = $null
    limitation = 'LLDB process backtrace only; not an external IME, editor, or paint acceptance test.'
    error = ''
}

function Invoke-BoundedDebugger {
    param([string] $Name, [string[]] $TargetArguments)

    $start = [Diagnostics.ProcessStartInfo]::new('/usr/bin/lldb')
    foreach ($argument in @('--batch', '-o', 'run', '-o', 'bt all', '--', $exe) + $TargetArguments) {
        [void]$start.ArgumentList.Add($argument)
    }
    $start.WorkingDirectory = $root
    $start.Environment['MOTE_NATIVE_MAC_STAGE_TRACE'] = '1'
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $timedOut = -not $process.WaitForExit(120000)
        if ($timedOut) {
            $process.Kill($true)
            $process.WaitForExit()
        }
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        # LLDB may print full argv and source paths. Upload only bounded,
        # repository-redacted debugger output, never the fixture itself.
        $stdout = $stdout.Replace($root, '<repo>')
        $stderr = $stderr.Replace($root, '<repo>')
        if ($stdout.Length -gt 65536) { $stdout = $stdout.Substring(0, 65536) + "`n<truncated>" }
        if ($stderr.Length -gt 32768) { $stderr = $stderr.Substring(0, 32768) + "`n<truncated>" }
        [IO.File]::WriteAllText((Join-Path $reports "mac-canvas-crash-$Name-stdout.txt"), $stdout)
        [IO.File]::WriteAllText((Join-Path $reports "mac-canvas-crash-$Name-stderr.txt"), $stderr)
        $signal = [regex]::Match($stdout + "`n" + $stderr, 'signal\s+(SIG[A-Z0-9]+)', 'IgnoreCase')
        return [ordered]@{
            status = if ($timedOut) { 'debugger-timeout' }
                elseif ($signal.Success) { 'target-signal' }
                elseif (($stdout + $stderr) -match 'not allowed|operation not permitted|attach failed') { 'debugger-denied' }
                else { 'no-signal-observed' }
            debugger_exit_code = $process.ExitCode
            target_signal = if ($signal.Success) { $signal.Groups[1].Value } else { '' }
            backtrace_present = ($stdout + $stderr) -match 'frame #0|\* thread #'
            stdout_chars = $stdout.Length
            stderr_chars = $stderr.Length
        }
    }
    finally { $process.Dispose() }
}

try {
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Published executable absent.' }
    $report.executable_sha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    $report.smoke = Invoke-BoundedDebugger 'smoke' @('--canvas-experimental', '--smoke-gui')
    if ($report.smoke.status -ne 'target-signal') {
        # The smoke may close before the crashing text-bound path. Use the
        # smallest supported in-process editor transaction as the fallback.
        $id = [guid]::NewGuid().ToString('N')
        $input = Join-Path $temp "canvas-crash-$id-input.txt"
        $output = Join-Path $temp "canvas-crash-$id-output.txt"
        [IO.File]::WriteAllText($input, 'abc', [Text.UTF8Encoding]::new($false))
        $report.clipboard = Invoke-BoundedDebugger 'clipboard' @(
            '--check-native-mac-canvas-clipboard', $input, $output)
    }
    $report.status = 'diagnostic-captured'
}
catch {
    $report.status = 'diagnostic-unavailable'
    $report.error = $_.Exception.GetType().Name + ': ' + $_.Exception.Message.Replace($root, '<repo>')
}
finally {
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $reportPath -Encoding utf8NoBOM
    Write-Host ($report | ConvertTo-Json -Depth 5 -Compress)
}
if ($report.status -ne 'diagnostic-captured') { throw $report.error }
