# Non-gating external AppKit/AX capability and exact edit/save probe on macOS ARM.
# AX focus is not proof of hidden NSTextView host length or physical display.
param([Parameter(Mandatory)][string] $ExecutablePath)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsMacOS -or [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne
    [Runtime.InteropServices.Architecture]::Arm64) {
    throw 'This first Mac canvas GUI probe requires a macOS arm64 host.'
}
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$scratchRoot = [IO.Path]::GetFullPath((Join-Path $root '.temp/benchmarks/native-canvas-gui'))
$scratch = [IO.Path]::GetFullPath((Join-Path $scratchRoot ([guid]::NewGuid().ToString('N'))))
$cacheRoot = [IO.Path]::GetFullPath((Join-Path $root '.cache/benchmarks'))
if (-not $scratch.StartsWith($scratchRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::Ordinal) -or
    -not $cacheRoot.StartsWith([IO.Path]::GetFullPath((Join-Path $root '.cache')) +
    [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
    throw 'Mac canvas GUI probe paths escaped the repository.'
}
$exe = [IO.Path]::GetFullPath($ExecutablePath)
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Executable absent: $exe" }
New-Item -ItemType Directory -Force -Path $scratch, $cacheRoot | Out-Null
if (-not ('MoteCanvasFixture' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'CanvasFixture.cs')
}
$utf8 = [Text.UTF8Encoding]::new($false, $true)

# Every AppleScript is persisted, compiled before execution, and time-bounded.
# A failed attempt retains separate compiler and interpreter streams in .temp.
function Compile-AppleScript {
    param([string] $Text, [string] $Name)
    $source = Join-Path $scratch "$Name.applescript"
    $compiled = Join-Path $scratch "$Name.scpt"
    [IO.File]::WriteAllText($source, $Text, $utf8)
    $compile = [Diagnostics.ProcessStartInfo]::new('/usr/bin/osacompile')
    [void]$compile.ArgumentList.Add('-o')
    [void]$compile.ArgumentList.Add($compiled)
    [void]$compile.ArgumentList.Add($source)
    $compile.UseShellExecute = $false
    $compile.RedirectStandardOutput = $true
    $compile.RedirectStandardError = $true
    $compiler = [Diagnostics.Process]::Start($compile)
    try {
        $timedOut = -not $compiler.WaitForExit(10000)
        if ($timedOut) {
            try { $compiler.Kill($true) }
            catch [InvalidOperationException] { }
            [void]$compiler.WaitForExit()
        }
        $stdout = $compiler.StandardOutput.ReadToEnd().Trim()
        $stderr = $compiler.StandardError.ReadToEnd().Trim()
        [IO.File]::WriteAllText((Join-Path $scratch "$Name.compile.stdout.txt"), $stdout, $utf8)
        [IO.File]::WriteAllText((Join-Path $scratch "$Name.compile.stderr.txt"), $stderr, $utf8)
        if ($timedOut) { throw "compile-timeout: $Name" }
        if ($compiler.ExitCode -ne 0) { throw "compile-error: $Name : $stderr" }
    }
    finally { $compiler.Dispose() }
    return $compiled
}

function Invoke-CompiledAppleScript {
    param([string] $CompiledPath, [string] $Name, [int] $TimeoutMs = 4000)
    $start = [Diagnostics.ProcessStartInfo]::new('/usr/bin/osascript')
    [void]$start.ArgumentList.Add($CompiledPath)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $command = [Diagnostics.Process]::Start($start)
    try {
        $timedOut = -not $command.WaitForExit($TimeoutMs)
        if ($timedOut) {
            try { $command.Kill($true) }
            catch [InvalidOperationException] { }
            [void]$command.WaitForExit()
        }
        $stdout = $command.StandardOutput.ReadToEnd().Trim()
        $stderr = $command.StandardError.ReadToEnd().Trim()
        [IO.File]::WriteAllText((Join-Path $scratch "$Name.stdout.txt"), $stdout, $utf8)
        [IO.File]::WriteAllText((Join-Path $scratch "$Name.stderr.txt"), $stderr, $utf8)
        if ($timedOut) { throw "execution-timeout: $Name : $stderr" }
        if ($command.ExitCode -ne 0) { throw "execution-error: $Name : $stderr" }
        return $stdout
    }
    finally { $command.Dispose() }
}

function Invoke-AppleScript {
    param([string] $Text, [string] $Name, [int] $TimeoutMs = 4000)
    $compiled = Compile-AppleScript $Text $Name
    return Invoke-CompiledAppleScript $compiled $Name $TimeoutMs
}

# Each readiness stage has its own time budget and last observable state.
function Wait-AxStage {
    param(
        [Diagnostics.Process] $Child,
        [Collections.IDictionary] $Result,
        [string] $Name,
        [string] $Text,
        [string] $Expected,
        [int] $BudgetMs
    )
    $Result.readiness_observation = ''
    $Result.readiness_attempts = 0
    $Result.last_automation_status = ''
    $Result.last_automation_error = ''
    $compiled = Compile-AppleScript $Text $Name
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $attempt = 0
    while ($timer.ElapsedMilliseconds -lt $BudgetMs) {
        $Child.Refresh()
        if ($Child.HasExited) { throw "child-exited: $Name exit=$($Child.ExitCode)" }
        $attempt++
        $remaining = $BudgetMs - [int]$timer.ElapsedMilliseconds
        $limit = [Math]::Min(4000, [Math]::Max(500, $remaining))
        $attemptName = '{0}.attempt-{1:D2}' -f $Name, $attempt
        try {
            $observed = Invoke-CompiledAppleScript $compiled $attemptName $limit
            $Result.readiness_observation = $observed
            $Result.last_automation_error = ''
            if ($observed -ceq $Expected -or
                ($Expected -ceq 'role=*TextArea*' -and $observed -like $Expected)) {
                $Result.readiness_attempts = $attempt
                $Result.last_automation_status = 'matched'
                return $observed
            }
            $Result.last_automation_status = 'observation-mismatch'
        }
        catch {
            $Result.last_automation_error = $_.Exception.Message
            $Result.last_automation_status = if ($_.Exception.Message -match '^([a-z-]+):') {
                $Matches[1]
            } else { 'execution-error' }
        }
        Start-Sleep -Milliseconds 150
    }
    $Result.readiness_attempts = $attempt
    throw "readiness-timeout: $Name after $BudgetMs ms; status=$($Result.last_automation_status); observation=$($Result.readiness_observation); last_error=$($Result.last_automation_error)"
}

# A custom canvas AX text element may expose the entire document. Never read AXValue.
function Get-VisibleRange {
    param([int] $ProcessId, [string] $Name)
    $script = @"
tell application "System Events"
    set targetProcess to first process whose unix id is $ProcessId
    set focusedElement to value of attribute "AXFocusedUIElement" of targetProcess
    try
        return (value of attribute "AXVisibleCharacterRange" of focusedElement) as string
    on error errorMessage number errorNumber
        return "unavailable-error-" & errorNumber
    end try
end tell
"@
    return Invoke-AppleScript $script $Name
}

function Wait-FileLength {
    param([string] $Path, [long] $Expected)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.ElapsedMilliseconds -lt 30000) {
        if ((Get-Item -LiteralPath $Path).Length -eq $Expected) { return }
        Start-Sleep -Milliseconds 25
    }
    throw 'Native Save did not reach the expected file length.'
}

function Invoke-Case {
    param([ValidateSet('control', 'many', 'long')][string] $Name)
    $fileName = switch ($Name) {
        'control' { 'control-1.txt' }
        'many' { 'many-100.txt' }
        'long' { 'long-50.txt' }
    }
    $file = Join-Path $scratch $fileName
    if ($Name -eq 'control') { [MoteCanvasFixture]::WriteManyLines($file, 1) }
    elseif ($Name -eq 'many') { [MoteCanvasFixture]::WriteManyLines($file) }
    else { [MoteCanvasFixture]::WriteLongLine($file) }
    $originalLength = (Get-Item -LiteralPath $file).Length
    $originalHash = [MoteCanvasFixture]::Sha256($file)
    $start = [Diagnostics.ProcessStartInfo]::new($exe)
    $start.WorkingDirectory = $root
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment['MOTE_HOME'] = Join-Path $scratch 'mote-home'
    $start.Environment['MOTE_TRACE'] = '0'
    [void]$start.ArgumentList.Add('--canvas-experimental')
    [void]$start.ArgumentList.Add($file)
    $result = [ordered]@{
        schema_version = 1
        timestamp_utc = [DateTimeOffset]::UtcNow.ToString('O')
        rid = 'osx-arm64'
        os = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        runtime = [Environment]::Version.ToString()
        executable_sha256 = [MoteCanvasFixture]::Sha256($exe)
        mode = $Name
        source_bytes = $originalLength
        observer = 'external-System-Events-AX-automation;not-first-paint-or-hidden-host-length'
        status = 'unverified'
        stage = 'launch'
        process_id = $null
        process_start_ms = $null
        readiness_step = 'not-started'
        readiness_observation = ''
        readiness_attempts = 0
        last_automation_status = ''
        last_automation_error = ''
        ax_process_ms = $null
        ax_window_ms = $null
        ax_frontmost_ms = $null
        ax_focused_role = $null
        focus_diagnostic_error = ''
        foreground_process_observation = ''
        foreground_process_error = ''
        open_to_ax_focus_ms = $null
        edit_to_dirty_automation_ms = $null
        edit_to_save_automation_ms = $null
        ax_visible_range_before = $null
        ax_visible_range_after = $null
        vertical_scroll_status = 'unverified'
        horizontal_scroll_status = 'unsupported-in-current-interactive-canvas'
        bounded_host_status = 'not-externally-observable-when-AX-editor-is-hidden'
        working_set_at_focus_bytes = $null
        working_set_after_save_bytes = $null
        observed_peak_working_set_bytes = $null
        error_kind = ''
        error = ''
    }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $child = $null
    try {
        try { $child = [Diagnostics.Process]::Start($start) }
        catch { throw "process-launch-error: $($_.Exception.Message)" }
        if ($null -eq $child) { throw 'process-launch-error: Could not launch macOS canvas.' }
        $result.process_id = $child.Id
        $result.process_start_ms = $watch.Elapsed.TotalMilliseconds
        $result.stage = 'AX-focus'
        $result.readiness_step = 'process'
        $processScript = @"
tell application "System Events"
    set targetProcess to first process whose unix id is $($child.Id)
    return "process-visible"
end tell
"@
        [void](Wait-AxStage $child $result "$Name-ax-process" $processScript 'process-visible' 12000)
        $result.ax_process_ms = $watch.Elapsed.TotalMilliseconds
        $result.readiness_step = 'window'
        $windowScript = @"
tell application "System Events"
    set targetProcess to first process whose unix id is $($child.Id)
    set windowCount to count of windows of targetProcess
    if windowCount is 0 then return "window-count=0"
    set windowTitle to name of window 1 of targetProcess
    if windowTitle contains "$fileName" then return "window-matched"
    return "window-count=" & windowCount & ";title-length=" & (length of windowTitle)
end tell
"@
        [void](Wait-AxStage $child $result "$Name-ax-window" $windowScript 'window-matched' 20000)
        $result.ax_window_ms = $watch.Elapsed.TotalMilliseconds
        $result.readiness_step = 'frontmost'
        $frontScript = @"
tell application "System Events"
    set targetProcess to first process whose unix id is $($child.Id)
    set frontmost of targetProcess to true
    if frontmost of targetProcess then return "frontmost"
    return "not-frontmost"
end tell
"@
        $focusScript = @"
tell application "System Events"
    set targetProcess to first process whose unix id is $($child.Id)
    set focusedElement to value of attribute "AXFocusedUIElement" of targetProcess
    set roleName to value of attribute "AXRole" of focusedElement
    return "role=" & roleName
end tell
"@
        try {
            [void](Wait-AxStage $child $result "$Name-ax-frontmost" $frontScript 'frontmost' 10000)
        }
        catch {
            $foregroundError = $_.Exception.Message
            # Diagnostic only: inspect AX role once even if focus activation is
            # unavailable. Never dispatch an edit or Save without foreground.
            try {
                $result.ax_focused_role = Invoke-AppleScript $focusScript `
                    "$Name-ax-focus-readonly" 4000
            }
            catch { $result.focus_diagnostic_error = $_.Exception.Message }
            $frontWhoScript = @"
tell application "System Events"
    set foregroundProcess to first process whose frontmost is true
    return "pid=" & (unix id of foregroundProcess) & ";name=" & (name of foregroundProcess)
end tell
"@
            try {
                $result.foreground_process_observation = Invoke-AppleScript `
                    $frontWhoScript "$Name-foreground-process-readonly" 4000
            }
            catch { $result.foreground_process_error = $_.Exception.Message }
            throw $foregroundError
        }
        $result.ax_frontmost_ms = $watch.Elapsed.TotalMilliseconds
        $result.readiness_step = 'focused-role'
        $role = Wait-AxStage $child $result "$Name-ax-focus" $focusScript 'role=*TextArea*' 16000
        $result.ax_focused_role = $role
        $result.readiness_step = 'complete'
        $result.open_to_ax_focus_ms = $watch.Elapsed.TotalMilliseconds
        $child.Refresh()
        $result.working_set_at_focus_bytes = if ($child.WorkingSet64 -gt 0) {
            [long]$child.WorkingSet64
        } else { $null }
        if ($Name -eq 'many') {
            $result.ax_visible_range_before = Get-VisibleRange $child.Id "$Name-range-before"
        }

        $result.stage = 'small-edit'
        $editAt = $watch.Elapsed.TotalMilliseconds
        $editScript = @"
tell application "System Events"
    set targetProcess to first process whose unix id is $($child.Id)
    set frontmost of targetProcess to true
    set focusedElement to value of attribute "AXFocusedUIElement" of targetProcess
    if (value of attribute "AXRole" of focusedElement) does not contain "TextArea" then error "canvas lost AX focus"
    keystroke "X"
    repeat with attempt from 1 to 100
        if name of window 1 of targetProcess contains " •" then return "dirty"
        delay 0.05
    end repeat
    error "native edit did not mark the document dirty"
end tell
"@
        if ((Invoke-AppleScript $editScript "$Name-edit" 10000) -cne 'dirty') {
            throw 'External text-input edit was not acknowledged.'
        }
        $result.edit_to_dirty_automation_ms = $watch.Elapsed.TotalMilliseconds - $editAt
        $result.stage = 'save'
        $saveScript = @"
tell application "System Events"
    set targetProcess to first process whose unix id is $($child.Id)
    set frontmost of targetProcess to true
    keystroke "s" using command down
    return "save-sent"
end tell
"@
        if ((Invoke-AppleScript $saveScript "$Name-save") -cne 'save-sent') {
            throw 'External Command-S was not dispatched.'
        }
        Wait-FileLength $file ($originalLength + 1)
        $result.edit_to_save_automation_ms = $watch.Elapsed.TotalMilliseconds - $editAt
        if (-not [MoteCanvasFixture]::HasOnePrefixedEdit($file, $originalLength,
            $originalHash)) { throw 'Saved bytes differ from exactly one prefixed X.' }
        $child.Refresh()
        $result.working_set_after_save_bytes = if ($child.WorkingSet64 -gt 0) {
            [long]$child.WorkingSet64
        } else { $null }

        if ($Name -eq 'many') {
            $result.stage = 'vertical-menu'
            $pageScript = @"
tell application "System Events"
    set targetProcess to first process whose unix id is $($child.Id)
    set frontmost of targetProcess to true
    click menu item "Next Page" of menu "View" of menu bar 1 of targetProcess
    return "page-sent"
end tell
"@
            if ((Invoke-AppleScript $pageScript "$Name-next-page") -cne 'page-sent') {
                throw 'Native Next Page menu command was not dispatched.'
            }
            $result.ax_visible_range_after = Get-VisibleRange $child.Id "$Name-range-after"
            $result.vertical_scroll_status = if ($result.ax_visible_range_before -notlike 'unavailable*' -and
                $result.ax_visible_range_after -notlike 'unavailable*' -and
                $result.ax_visible_range_after -cne $result.ax_visible_range_before) {
                'AX-visible-range-changed-after-native-menu;not-pixel-verified'
            } else { 'menu-dispatched-but-anchor-unverified' }
        }
        $child.Refresh()
        $peak = [long]$child.PeakWorkingSet64
        $result.observed_peak_working_set_bytes = if ($peak -gt 0) { $peak } else { $null }
        $result.stage = 'close'
        $closeScript = @"
tell application "System Events"
    set targetProcess to first process whose unix id is $($child.Id)
    set frontmost of targetProcess to true
    keystroke "w" using command down
    return "close-sent"
end tell
"@
        if ((Invoke-AppleScript $closeScript "$Name-close") -cne 'close-sent' -or
            -not $child.WaitForExit(15000) -or $child.ExitCode -ne 0) {
            throw 'Native canvas did not close cleanly.'
        }
        $result.status = 'external-open-edit-save-passed'
        $result.stage = 'complete'
    }
    catch {
        $result.error = $_.Exception.Message
        $result.error_kind = if ($result.error -match '^([a-z-]+):') {
            $Matches[1]
        } else { 'unexpected' }
        throw
    }
    finally {
        if ($null -ne $child) {
            if (-not $child.HasExited) {
                try { $child.Kill($true); [void]$child.WaitForExit() }
                catch [InvalidOperationException] { } # Child exited during cleanup.
            }
            [IO.File]::WriteAllText((Join-Path $scratch "$Name-stdout.txt"),
                $child.StandardOutput.ReadToEnd(), $utf8)
            [IO.File]::WriteAllText((Join-Path $scratch "$Name-stderr.txt"),
                $child.StandardError.ReadToEnd(), $utf8)
            $child.Dispose()
        }
        $result | ConvertTo-Json -Depth 5 -Compress |
            Add-Content -LiteralPath (Join-Path $cacheRoot 'native-canvas-gui-osx-arm64.jsonl') -Encoding utf8
        Write-Host ($result | ConvertTo-Json -Depth 5 -Compress)
    }
}

$allPassed = $false
try {
    # A small exact-byte control separates AX/TCC/input failure from large-file loading.
    foreach ($name in 'control', 'many', 'long') { Invoke-Case $name }
    $allPassed = $true
}
finally {
    if (-not $scratch.StartsWith($scratchRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::Ordinal)) { throw 'Refusing cleanup outside repository .temp.' }
    if ($allPassed) { Remove-Item -LiteralPath $scratch -Recurse -Force }
    else { Write-Warning "Failed Mac canvas probe retained under $scratch" }
}
