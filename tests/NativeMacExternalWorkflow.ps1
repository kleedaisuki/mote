# Diagnose a published macOS editor using external System Events keyboard commands.
# The hosted workflow treats any non-success report as a strict failure.
param(
    [Parameter(Mandatory)][string] $ExecutablePath,
    [Parameter(Mandatory)][string] $ReportPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsMacOS) { throw 'This probe requires macOS.' }

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$scratchRoot = [IO.Path]::GetFullPath((Join-Path $root '.temp/mac-external-workflow'))
$scratch = [IO.Path]::GetFullPath((Join-Path $scratchRoot ([guid]::NewGuid().ToString('N'))))
if (-not $scratch.StartsWith($scratchRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
    throw 'External macOS workflow scratch path escaped the repository.'
}
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
$report = [IO.Path]::GetFullPath($ReportPath)
$reportRoot = [IO.Path]::GetFullPath((Join-Path $root '.cache/ci-inventory'))
if (-not $report.StartsWith($reportRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
    throw 'External macOS workflow report must remain under repository .cache/ci-inventory.'
}
New-Item -ItemType Directory -Force -Path (Split-Path $report) | Out-Null

$utf8 = [Text.UTF8Encoding]::new($false, $true)
$source = "alpha`nbeta`n"
$file = Join-Path $scratch 'note.txt'
[IO.File]::WriteAllText($file, $source, $utf8)
$exe = [IO.Path]::GetFullPath($ExecutablePath)
$process = $null
$success = $false
$stage = 'startup'
$result = [ordered]@{
    status = 'unknown'
    stage = $stage
    source_chars = $source.Length
    saved_chars = $null
    reopened_chars = $null
    error = ''
    method = 'external-System-Events-keyboard-and-native-clipboard'
    cjk_ime_tested = $false
    foreground_observation = ''
    foreground_observation_error = ''
}

# Run bounded AppleScript commands against the exact child PID, avoiding stale mote windows.
function Invoke-AppleScript {
    param([string] $ScriptText, [string] $Name)
    $script = Join-Path $scratch "$Name.applescript"
    [IO.File]::WriteAllText($script, $ScriptText, $utf8)
    # PowerShell syntax validation cannot see AppleScript grammar. Compile every
    # generated command before execution so a broken script cannot send a key.
    $compileStart = [Diagnostics.ProcessStartInfo]::new('/usr/bin/osacompile')
    [void]$compileStart.ArgumentList.Add('-o')
    [void]$compileStart.ArgumentList.Add((Join-Path $scratch "$Name.scpt"))
    [void]$compileStart.ArgumentList.Add($script)
    $compileStart.UseShellExecute = $false
    $compileStart.RedirectStandardOutput = $true
    $compileStart.RedirectStandardError = $true
    $compiler = [Diagnostics.Process]::Start($compileStart)
    try {
        if (-not $compiler.WaitForExit(15000)) {
            $compiler.Kill()
            $compiler.WaitForExit()
            throw "AppleScript $Name compilation timed out."
        }
        $compileError = $compiler.StandardError.ReadToEnd().Trim()
        [IO.File]::WriteAllText((Join-Path $scratch "$Name.compile.stderr.txt"), $compileError, $utf8)
        if ($compiler.ExitCode -ne 0) { throw "AppleScript $Name compilation failed: $compileError" }
    }
    finally { $compiler.Dispose() }
    $start = [Diagnostics.ProcessStartInfo]::new('/usr/bin/osascript')
    [void]$start.ArgumentList.Add($script)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $command = [Diagnostics.Process]::Start($start)
    try {
        if (-not $command.WaitForExit(15000)) {
            $command.Kill()
            $command.WaitForExit()
            $timedOutLog = $command.StandardError.ReadToEnd().Trim()
            [IO.File]::WriteAllText((Join-Path $scratch "$Name.stderr.txt"), $timedOutLog, $utf8)
            throw "AppleScript $Name timed out; partial log: $timedOutLog"
        }
        $stdout = $command.StandardOutput.ReadToEnd().Trim()
        $stderr = $command.StandardError.ReadToEnd().Trim()
        [IO.File]::WriteAllText((Join-Path $scratch "$Name.stdout.txt"), $stdout, $utf8)
        [IO.File]::WriteAllText((Join-Path $scratch "$Name.stderr.txt"), $stderr, $utf8)
        if ($command.ExitCode -ne 0) { throw "AppleScript $Name exited $($command.ExitCode): $stderr" }
        return $stdout
    }
    finally { $command.Dispose() }
}

# A window title alone is insufficient: wait for the NSTextView to be the actual
# focused AX element before sending a non-idempotent keystroke. Only this known
# activation/readiness state is retried; a failed typing or Save is never replayed.
function Wait-EditorReady {
    param([int] $ProcessId, [string] $Name)
    $script = @"
tell application "System Events"
    set targetProcess to first process whose unix id is $ProcessId
    repeat with attempt from 1 to 80
        try
            set frontmost of targetProcess to true
            if exists window 1 of targetProcess then
                if name of window 1 of targetProcess contains "note.txt" then
                    set focusedElement to value of attribute "AXFocusedUIElement" of targetProcess
                    set focusRole to value of attribute "AXRole" of focusedElement
                    if focusRole contains "TextArea" then
                        log "ready:window-and-AXTextArea"
                        return "AXTextArea"
                    end if
                end if
            end if
        end try
        delay 0.1
    end repeat
    error "mote window or focused NSTextView did not become ready"
end tell
"@
    $ready = Invoke-AppleScript $script $Name
    if ($ready -cne 'AXTextArea') { throw "Unexpected native focus role: $ready" }
}

# Observe, but never gate or repair, the desktop-global foreground after the
# existing PID/focused-TextArea readiness contract has passed. This helps
# compare the default editor with the separate opt-in Canvas activation probe.
function Observe-Foreground {
    param([int] $ProcessId)
    $script = @"
tell application "System Events"
    set targetProcess to first process whose unix id is $ProcessId
    set targetFrontmost to frontmost of targetProcess
    set globalPid to -1
    try
        set globalPid to unix id of first process whose frontmost is true
    end try
    set focusRole to "unknown"
    try
        set focusedElement to value of attribute "AXFocusedUIElement" of targetProcess
        set focusRole to value of attribute "AXRole" of focusedElement
    end try
    return "target-frontmost=" & (targetFrontmost as text) & ";global-pid=" & (globalPid as text) & ";target-ax-role=" & focusRole
end tell
"@
    return Invoke-AppleScript $script 'foreground-readonly'
}

function Start-Editor {
    $start = [Diagnostics.ProcessStartInfo]::new($exe)
    $start.WorkingDirectory = $root
    $start.UseShellExecute = $false
    $start.Environment['MOTE_HOME'] = Join-Path $scratch 'home'
    [void]$start.ArgumentList.Add($file)
    return [Diagnostics.Process]::Start($start)
}

function Wait-FileChange {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.ElapsedMilliseconds -lt 10000) {
        $value = [IO.File]::ReadAllText($file, $utf8)
        if ($value.Length -eq $source.Length + 1 -and $value.Replace('X', '') -ceq $source) {
            return $value
        }
        Start-Sleep -Milliseconds 50
    }
    throw 'External Command-S did not persist exactly one typed X.'
}

try {
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Executable not found: $exe" }
    $process = Start-Editor
    $stage = 'focus-original'
    Wait-EditorReady $process.Id 'focus-original'
    try { $result.foreground_observation = Observe-Foreground $process.Id }
    catch { $result.foreground_observation_error = $_.Exception.Message }
    $stage = 'type-X'
    $typeScript = @"
tell application "System Events"
    set targetProcess to first process whose unix id is $($process.Id)
    set frontmost of targetProcess to true
    set focusedElement to value of attribute "AXFocusedUIElement" of targetProcess
    if (value of attribute "AXRole" of focusedElement) does not contain "TextArea" then error "editor lost native focus"
    keystroke "X"
    log "typed:sent-X"
    delay 0.2
    -- The dirty marker changes the window title, invalidating AX references
    -- previously resolved through the old title; reacquire from the PID.
    set focusedElement to value of attribute "AXFocusedUIElement" of targetProcess
    return value of attribute "AXValue" of focusedElement
end tell
"@
    $nativeText = Invoke-AppleScript $typeScript 'type-X'
    # osascript trims stdout, so this diagnostic check ignores the final newline;
    # disk bytes and reopened clipboard below remain exact and unnormalized.
    $normalizedNative = $nativeText.TrimEnd()
    $expectedBase = $source.TrimEnd()
    if ($normalizedNative.Length -ne $expectedBase.Length + 1 -or
        $normalizedNative.Replace('X', '') -cne $expectedBase) {
        throw "Native focused text did not gain exactly one X (length $($nativeText.Length))."
    }
    $stage = 'command-S'
    $saveScript = @"
tell application "System Events"
    set targetProcess to first process whose unix id is $($process.Id)
    set frontmost of targetProcess to true
    keystroke "s" using command down
    log "save:sent-command-S"
end tell
"@
    [void](Invoke-AppleScript $saveScript 'command-S')
    $stage = 'persisted-disk'
    $saved = Wait-FileChange
    $savedBytes = [IO.File]::ReadAllBytes($file)
    if ([Convert]::ToHexString($savedBytes) -cne [Convert]::ToHexString($utf8.GetBytes($saved))) {
        throw 'Saved bytes are not exact BOMless UTF-8 of the independently read text.'
    }
    $result.saved_chars = $saved.Length
    $stage = 'close-original'
    $closeScript = @"
tell application "System Events"
    set targetProcess to first process whose unix id is $($process.Id)
    set frontmost of targetProcess to true
    keystroke "w" using command down
end tell
"@
    [void](Invoke-AppleScript $closeScript 'close-original')
    if (-not $process.WaitForExit(5000)) { throw 'Original editor did not close after external Command-W.' }
    if ($process.ExitCode -ne 0) { throw "Original editor exited $($process.ExitCode)." }
    $process.Dispose()
    $process = Start-Editor
    $stage = 'focus-reopened'
    Wait-EditorReady $process.Id 'focus-reopened'
    $stage = 'reopen-and-copy'
    $copyStart = [Diagnostics.ProcessStartInfo]::new('/usr/bin/pbcopy')
    $copyStart.UseShellExecute = $false
    $copyStart.RedirectStandardInput = $true
    $copyProcess = [Diagnostics.Process]::Start($copyStart)
    $copyProcess.StandardInput.Write('mote-external-probe-sentinel')
    $copyProcess.StandardInput.Close()
    if (-not $copyProcess.WaitForExit(5000)) {
        $copyProcess.Kill()
        throw 'pbcopy did not accept the sentinel within five seconds.'
    }
    if ($copyProcess.ExitCode -ne 0) { throw "pbcopy exited $($copyProcess.ExitCode)." }
    $copyProcess.Dispose()
    $reopenScript = @"
tell application "System Events"
    set targetProcess to first process whose unix id is $($process.Id)
    set frontmost of targetProcess to true
    set focusedElement to value of attribute "AXFocusedUIElement" of targetProcess
    if (value of attribute "AXRole" of focusedElement) does not contain "TextArea" then error "reopened editor lost native focus"
    keystroke "a" using command down
    keystroke "c" using command down
end tell
"@
    [void](Invoke-AppleScript $reopenScript 'reopen-copy')
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $reopened = ''
    while ($timer.ElapsedMilliseconds -lt 10000) {
        $pasteStart = [Diagnostics.ProcessStartInfo]::new('/usr/bin/pbpaste')
        $pasteStart.UseShellExecute = $false
        $pasteStart.RedirectStandardOutput = $true
        $paste = [Diagnostics.Process]::Start($pasteStart)
        if (-not $paste.WaitForExit(5000)) {
            $paste.Kill()
            throw 'pbpaste did not return within five seconds.'
        }
        if ($paste.ExitCode -ne 0) { throw "pbpaste exited $($paste.ExitCode)." }
        $reopened = $paste.StandardOutput.ReadToEnd()
        $paste.Dispose()
        if ($reopened -ceq $saved) { break }
        Start-Sleep -Milliseconds 50
    }
    if ($reopened -cne $saved) { throw 'Reopened native Copy did not yield the exact saved source.' }
    $result.reopened_chars = $reopened.Length
    $stage = 'close-reopened'
    $closeReopened = @"
tell application "System Events"
    set targetProcess to first process whose unix id is $($process.Id)
    set frontmost of targetProcess to true
    keystroke "w" using command down
end tell
"@
    [void](Invoke-AppleScript $closeReopened 'close-reopened')
    if (-not $process.WaitForExit(5000)) { throw 'Reopened editor did not close after external Command-W.' }
    if ($process.ExitCode -ne 0) { throw "Reopened editor exited $($process.ExitCode)." }
    $success = $true
    $result.status = 'external-open-keyboard-edit-save-reopen-ok'
}
catch {
    $result.status = 'external-workflow-unverified'
    $result.error = $_.Exception.Message
    if ($process -and -not $process.HasExited) {
        try {
            $axState = @"
tell application "System Events"
    set targetProcess to first process whose unix id is $($process.Id)
    set focusedElement to value of attribute "AXFocusedUIElement" of targetProcess
    set focusRole to value of attribute "AXRole" of focusedElement
    return "window=" & name of window 1 of targetProcess & " ; focused-role=" & focusRole & " ; tree=" & (entire contents of window 1 of targetProcess as text)
end tell
"@
            $observed = Invoke-AppleScript $axState 'failure-ax-state'
            [IO.File]::WriteAllText((Join-Path $scratch 'failure-ax-tree.txt'), $observed, $utf8)
        }
        catch {
            [IO.File]::WriteAllText((Join-Path $scratch 'failure-ax-error.txt'),
                $_.Exception.Message, $utf8)
        }
        try {
            $capture = [Diagnostics.ProcessStartInfo]::new('/usr/sbin/screencapture')
            $capture.UseShellExecute = $false
            [void]$capture.ArgumentList.Add('-x')
            [void]$capture.ArgumentList.Add('-m')
            [void]$capture.ArgumentList.Add((Join-Path $scratch 'failure-screen.png'))
            $screenshot = [Diagnostics.Process]::Start($capture)
            if (-not $screenshot.WaitForExit(5000)) { $screenshot.Kill(); $screenshot.WaitForExit() }
            $screenshot.Dispose()
        }
        catch {
            [IO.File]::WriteAllText((Join-Path $scratch 'failure-screen-error.txt'),
                $_.Exception.Message, $utf8)
        }
    }
}
finally {
    $result.stage = $stage
    if ($process) {
        if (-not $process.HasExited) { try { $process.Kill() } catch { } }
        $process.Dispose()
    }
    $result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $report -Encoding utf8
    Get-Content -LiteralPath $report
    if ($success) { Remove-Item -LiteralPath $scratch -Recurse -Force }
    else { Write-Warning "External macOS workflow did not verify; retained synthetic fixture under $scratch." }
}

if (-not $success) {
    throw "External macOS open-edit-save-reopen workflow failed at ${stage}: $($result.error)"
}
