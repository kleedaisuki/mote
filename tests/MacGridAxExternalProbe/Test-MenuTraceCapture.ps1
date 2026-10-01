# Verify the bounded whitelist independently of AppKit or editor output.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $PSScriptRoot 'MenuTraceCapture.cs')
$valid = 'mote-grid-menu-v1 phase=show-enter seq=1 requests=1 opens=0 closes=0 open=0 result=-1 configured=1 items=12 coordinate=1 shown=0 key=1 first=0 active=1 allowaction=1 allowshown=0'
$return = $valid.Replace('phase=show-enter', 'phase=native-return').Replace('result=-1', 'result=1')
$schedule = $return.Replace('native-return', 'schedule-return')
$popupReturn = $return.Replace('native-return', 'popup-return')
$popupBegin = $valid.Replace('show-enter', 'popup-begin')
$invalid = @(
    'private document output'
    $valid.Replace('allowaction=1', 'allowaction=2')
    $valid.Replace('allowshown=0', 'allowshown=2')
    $valid.Replace(' allowaction=1 allowshown=0', '')
    $valid.Replace('allowaction=1 allowshown=0', 'allowshown=0 allowaction=1')
    $valid.Replace('seq=1 ', 'seq=0 ')
    $valid.Replace('opens=0', 'opens=17')
    $valid.Replace('result=-1', 'result=1')
    $return.Replace('result=1', 'result=-1')
    $schedule.Replace('result=1', 'result=-1')
    $popupReturn.Replace('result=1', 'result=-1')
    $popupBegin.Replace('result=-1', 'result=1')
    ($valid + ' private-path')
    ($valid + "`r`r")
    ('prefix ' + $valid)
    (('x' * 100000) + $valid)
)
$inputText = ($invalid + @($valid, $return, $schedule, $popupReturn, $popupBegin)) -join "`r`n"
$reader = [IO.StringReader]::new($inputText)
$actual = [Mote.Testing.MacGridMenuTraceCapture]::ReadAsync($reader, $true).GetAwaiter().GetResult()
if ($actual.Length -ne 5 -or $actual[0] -cne $valid -or $actual[1] -cne $return -or $actual[2] -cne $schedule -or $actual[3] -cne $popupReturn -or $actual[4] -cne $popupBegin) { throw 'Whitelist admitted malformed or private output.' }
foreach ($actionAllowed in @(0, 1)) {
    foreach ($shownAllowed in @(0, 1)) {
        $row = $valid.Replace('allowaction=1', "allowaction=$actionAllowed").Replace('allowshown=0', "allowshown=$shownAllowed")
        $reader = [IO.StringReader]::new($row)
        $actual = [Mote.Testing.MacGridMenuTraceCapture]::ReadAsync($reader, $true).GetAwaiter().GetResult()
        if ($actual.Length -ne 1 -or $actual[0] -cne $row) { throw 'Selector allowed facts were interpreted instead of captured.' }
    }
}
$reader = [IO.StringReader]::new((1..30 | ForEach-Object { $valid }) -join "`n")
$actual = [Mote.Testing.MacGridMenuTraceCapture]::ReadAsync($reader, $true).GetAwaiter().GetResult()
if ($actual.Length -ne 16) { throw 'Capture exceeded sixteen-line contract.' }
$reader = [IO.StringReader]::new($inputText)
$actual = [Mote.Testing.MacGridMenuTraceCapture]::ReadAsync($reader, $false).GetAwaiter().GetResult()
if ($actual.Length -ne 0) { throw 'Discard-only stream retained output.' }
# Exercise concurrent real child pipes beyond pipe capacity; no raw output file.
$start = [Diagnostics.ProcessStartInfo]::new((Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })))
$start.UseShellExecute = $false
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
[void]$start.ArgumentList.Add('-NoProfile')
[void]$start.ArgumentList.Add('-Command')
[void]$start.ArgumentList.Add("[Console]::Out.WriteLine('x' * 1000000); [Console]::Error.WriteLine('e' * 1000000); [Console]::Out.WriteLine('$valid')")
$child = [Diagnostics.Process]::Start($start)
try {
    $stdout = [Mote.Testing.MacGridMenuTraceCapture]::ReadAsync($child.StandardOutput, $true)
    $stderr = [Mote.Testing.MacGridMenuTraceCapture]::ReadAsync($child.StandardError, $false)
    if (-not $child.WaitForExit(15000) -or $child.ExitCode -ne 0) { throw 'Concurrent pipe drain failed.' }
    $actual = $stdout.GetAwaiter().GetResult()
    if ($actual.Length -ne 1 -or $actual[0] -cne $valid -or $stderr.GetAwaiter().GetResult().Length -ne 0) { throw 'Real pipe whitelist failed.' }
} finally {
    if (-not $child.HasExited) { $child.Kill(); [void]$child.WaitForExit(5000) }
    $child.Dispose()
}
Write-Output 'mac-grid-menu-bounded-whitelist-passed'
