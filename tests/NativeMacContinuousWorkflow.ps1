# Verify the retained --continuous product through external AX routing and keyboard input.
# Only bounded source metadata and exact synthetic disk bytes are read; never AXValue.
param(
    [Parameter(Mandatory)][string] $ExecutablePath,
    [Parameter(Mandatory)][string] $ReportPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsMacOS) { throw 'The ordinary Continuous GUI workflow requires macOS.' }

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$tempRoot = [IO.Path]::GetFullPath((Join-Path $root '.temp/mac-continuous-workflow'))
$scratch = Join-Path $tempRoot ([guid]::NewGuid().ToString('N'))
$reportRoot = [IO.Path]::GetFullPath((Join-Path $root '.cache/ci-inventory'))
$report = [IO.Path]::GetFullPath($ReportPath)
if (-not $report.StartsWith($reportRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::Ordinal)) { throw 'Report escaped repository .cache/ci-inventory.' }
New-Item -ItemType Directory -Force -Path $scratch, (Split-Path $report) | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$fixture = Join-Path $scratch 'continuous-note.txt'
$source = "alpha`nbeta`n"
[IO.File]::WriteAllText($fixture, $source, $utf8)
$expected = "X$source"
$expectedHex = [Convert]::ToHexString($utf8.GetBytes($expected))
$exe = [IO.Path]::GetFullPath($ExecutablePath)
$client = Join-Path $scratch 'canvas-ax-gate'
$editor = $null
$stage = 'compile-ax-client'
$result = [ordered]@{
    status = 'failed'; stage = $stage; route = 'ordinary-product-Continuous'
    method = 'external-AX-source-proxy-and-System-Events-keyboard'
    fixture_sha256_before = (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash
    saved_sha256 = $null; reopened_source_length = $null
    source_proxy_observations = @(); error = $null
    ax_scope = 'one exact-length AXTextArea labeled Mote editor in bounded process tree; other unlabeled AXTextAreas are not excluded'
    input_host_bound = 'not observable externally: NSTextView host is hidden from AX; separate published in-process Canvas probes cover bounded binding'
    limits = 'No full AXValue, real CJK IME, VoiceOver speech, or physical-paint timing.'
}

function Invoke-BoundedProcess {
    param([string] $File, [string[]] $Arguments, [int] $TimeoutMs, [string] $Name)
    $start = [Diagnostics.ProcessStartInfo]::new($File)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { [void]$start.ArgumentList.Add($argument) }
    $child = [Diagnostics.Process]::Start($start)
    try {
        if ($null -eq $child) { throw "$Name did not start." }
        $stdoutTask = $child.StandardOutput.ReadToEndAsync()
        $stderrTask = $child.StandardError.ReadToEndAsync()
        $timedOut = -not $child.WaitForExit($TimeoutMs)
        if ($timedOut) { $child.Kill($true); [void]$child.WaitForExit(10000) }
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        if ($stdout.Length -gt 8192) { $stdout = $stdout.Substring(0, 8192) + '<truncated>' }
        if ($stderr.Length -gt 8192) { $stderr = $stderr.Substring(0, 8192) + '<truncated>' }
        [IO.File]::WriteAllText((Join-Path $scratch "$Name.stdout.txt"), $stdout, $utf8)
        [IO.File]::WriteAllText((Join-Path $scratch "$Name.stderr.txt"), $stderr, $utf8)
        if ($timedOut) { throw "$Name exceeded its time bound." }
        if ($child.ExitCode -ne 0) { throw "$Name exited $($child.ExitCode)." }
        return $stdout.Trim()
    }
    finally { if ($null -ne $child) { $child.Dispose() } }
}

function Invoke-AppleScript {
    param([string] $Name, [string] $Body)
    $textPath = Join-Path $scratch "$Name.applescript"
    [IO.File]::WriteAllText($textPath, $Body, $utf8)
    [void](Invoke-BoundedProcess '/usr/bin/osacompile' @('-o',
        (Join-Path $scratch "$Name.scpt"), $textPath) 15000 "$Name-compile")
    [void](Invoke-BoundedProcess '/usr/bin/osascript' @($textPath) 15000 $Name)
}

function Start-Editor {
    $start = [Diagnostics.ProcessStartInfo]::new($exe)
    $start.WorkingDirectory = $root
    $start.UseShellExecute = $false
    $start.Environment['MOTE_HOME'] = Join-Path $scratch 'home'
    [void]$start.ArgumentList.Add('--continuous')
    [void]$start.ArgumentList.Add($fixture)
    return [Diagnostics.Process]::Start($start)
}

function Read-SourceProxy {
    param([int] $TargetProcessId, [int] $Length)
    $raw = Invoke-BoundedProcess $client @([string]$TargetProcessId, [string]$Length,
        'continuous-note.txt') 7000 'source-gate'
    return $raw | ConvertFrom-Json
}

function Wait-SourceProxy {
    param([int] $TargetProcessId, [int] $Length,
        [Nullable[int]] $SelectionStart, [Nullable[int]] $SelectionLength)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.ElapsedMilliseconds -lt 20000) {
        $editor.Refresh()
        if ($editor.HasExited) { throw "Ordinary Continuous editor exited $($editor.ExitCode)." }
        try {
            $ax = Read-SourceProxy $TargetProcessId $Length
            if ($ax.trusted -and $ax.applicationPid -eq $TargetProcessId -and
                $ax.focusedWindowPid -eq $TargetProcessId -and $ax.focusedWindowMatches -and
                $ax.sourceCandidates -eq 1 -and $ax.labeledSourceCandidates -eq 1 -and
                $ax.sourceLength -eq $Length -and $ax.proxyFocused -eq $true -and
                ($null -eq $SelectionStart -or $ax.selectionStart -eq $SelectionStart) -and
                ($null -eq $SelectionLength -or $ax.selectionLength -eq $SelectionLength)) {
                $result.source_proxy_observations += [ordered]@{
                    length = $Length; selection_start = $SelectionStart
                    selection_length = $SelectionLength; pid = $TargetProcessId
                }
                return
            }
        }
        catch { }
        Start-Sleep -Milliseconds 150
    }
    throw "Source-backed AX proxy did not reach expected length/selection at $stage."
}

function Send-Key {
    param([int] $TargetProcessId, [string] $Name, [string] $Command)
    $body = @"
tell application "System Events"
    set targetProcess to first process whose unix id is $TargetProcessId
    set frontmost of targetProcess to true
    $Command
end tell
"@
    Invoke-AppleScript $Name $body
}

try {
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Published Mach-O is absent.' }
    $swiftSource = Join-Path $root 'benchmarks/NativeCanvasGui/CanvasAxGate.swift'
    [void](Invoke-BoundedProcess '/usr/bin/xcrun' @('swiftc', '-O',
        $swiftSource, '-o', $client) 60000 'swift-client-build')

    $stage = 'open-original'
    $editor = Start-Editor
    if ($null -eq $editor) { throw 'Ordinary Continuous editor did not start.' }
    Send-Key $editor.Id 'activate-original' 'delay 0.1'
    Wait-SourceProxy $editor.Id $source.Length 0 0

    # Non-idempotent X is sent only after two reversible source-selection probes.
    $stage = 'keyboard-routing'
    Send-Key $editor.Id 'select-one' 'key code 124 using shift down'
    Wait-SourceProxy $editor.Id $source.Length 0 1
    Send-Key $editor.Id 'collapse-selection' 'key code 123'
    Wait-SourceProxy $editor.Id $source.Length 0 0

    $stage = 'type-X'
    Send-Key $editor.Id 'type-X' 'keystroke "X"'
    Wait-SourceProxy $editor.Id $expected.Length 1 0
    $stage = 'save'
    Send-Key $editor.Id 'save' 'keystroke "s" using command down'
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.ElapsedMilliseconds -lt 15000 -and
        [Convert]::ToHexString([IO.File]::ReadAllBytes($fixture)) -cne $expectedHex) {
        Start-Sleep -Milliseconds 50
    }
    if ([Convert]::ToHexString([IO.File]::ReadAllBytes($fixture)) -cne $expectedHex) {
        throw 'External Save did not persist exactly X plus original BOMless UTF-8 bytes.'
    }
    $result.saved_sha256 = (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash
    $stage = 'close-original'
    Send-Key $editor.Id 'close-original' 'keystroke "w" using command down'
    if (-not $editor.WaitForExit(10000)) { throw 'Original editor did not close.' }
    if ($editor.ExitCode -ne 0) { throw "Original editor exited $($editor.ExitCode)." }
    $editor.Dispose(); $editor = $null

    $stage = 'reopen'
    $editor = Start-Editor
    if ($null -eq $editor) { throw 'Reopened ordinary editor did not start.' }
    Send-Key $editor.Id 'activate-reopen' 'delay 0.1'
    Wait-SourceProxy $editor.Id $expected.Length $null $null
    $result.reopened_source_length = $expected.Length
    if ([Convert]::ToHexString([IO.File]::ReadAllBytes($fixture)) -cne $expectedHex) {
        throw 'Reopening the editor changed saved source bytes.'
    }
    Send-Key $editor.Id 'close-reopen' 'keystroke "w" using command down'
    if (-not $editor.WaitForExit(10000)) { throw 'Reopened editor did not close.' }
    if ($editor.ExitCode -ne 0) { throw "Reopened editor exited $($editor.ExitCode)." }
    $result.status = 'passed'
}
catch { $result.error = $_.Exception.Message }
finally {
    if ($null -ne $editor) {
        try { if (-not $editor.HasExited) { $editor.Kill($true); [void]$editor.WaitForExit(10000) } }
        finally { $editor.Dispose() }
    }
    $result.stage = $stage
    $result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $report -Encoding utf8NoBOM
}
if ($result.status -ne 'passed') { throw 'Ordinary macOS Continuous workflow failed; inspect repository report.' }
