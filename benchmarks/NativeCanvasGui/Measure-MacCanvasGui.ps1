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

# Every AppleScript is persisted inside this checkout, compiled, and time-bounded.
function Invoke-AppleScript {
    param([string] $Text, [string] $Name)
    $source = Join-Path $scratch "$Name.applescript"
    $compiled = Join-Path $scratch "$Name.scpt"
    [IO.File]::WriteAllText($source, $Text, $utf8)
    $compile = [Diagnostics.ProcessStartInfo]::new('/usr/bin/osacompile')
    [void]$compile.ArgumentList.Add('-o')
    [void]$compile.ArgumentList.Add($compiled)
    [void]$compile.ArgumentList.Add($source)
    $compile.UseShellExecute = $false
    $compile.RedirectStandardError = $true
    $compiler = [Diagnostics.Process]::Start($compile)
    try {
        if (-not $compiler.WaitForExit(15000)) {
            $compiler.Kill(); $compiler.WaitForExit()
            throw "AppleScript $Name compilation timed out."
        }
        $errorText = $compiler.StandardError.ReadToEnd().Trim()
        if ($compiler.ExitCode -ne 0) { throw "AppleScript $Name syntax failed: $errorText" }
    }
    finally { $compiler.Dispose() }
    $start = [Diagnostics.ProcessStartInfo]::new('/usr/bin/osascript')
    [void]$start.ArgumentList.Add($source)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $command = [Diagnostics.Process]::Start($start)
    try {
        if (-not $command.WaitForExit(20000)) {
            $command.Kill(); $command.WaitForExit()
            throw "AppleScript $Name execution timed out."
        }
        $stdout = $command.StandardOutput.ReadToEnd().Trim()
        $stderr = $command.StandardError.ReadToEnd().Trim()
        [IO.File]::WriteAllText((Join-Path $scratch "$Name.stderr.txt"), $stderr, $utf8)
        if ($command.ExitCode -ne 0) { throw "AppleScript $Name failed: $stderr" }
        return $stdout
    }
    finally { $command.Dispose() }
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
    on error
        return "unavailable"
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
    param([string] $Name)
    $fileName = if ($Name -eq 'many') { 'many-100.txt' } else { 'long-50.txt' }
    $file = Join-Path $scratch $fileName
    if ($Name -eq 'many') { [MoteCanvasFixture]::WriteManyLines($file) }
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
        error = ''
    }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $child = [Diagnostics.Process]::Start($start)
    if ($null -eq $child) { throw 'Could not launch macOS canvas.' }
    try {
        $result.stage = 'AX-focus'
        $readyScript = @"
tell application "System Events"
    set targetProcess to first process whose unix id is $($child.Id)
    repeat with attempt from 1 to 100
        try
            set frontmost of targetProcess to true
            if exists window 1 of targetProcess then
                if name of window 1 of targetProcess contains "$fileName" then
                    set focusedElement to value of attribute "AXFocusedUIElement" of targetProcess
                    set roleName to value of attribute "AXRole" of focusedElement
                    if roleName contains "TextArea" then return roleName
                end if
            end if
        end try
        delay 0.1
    end repeat
    error "canvas window or focused AX text element unavailable"
end tell
"@
        $role = Invoke-AppleScript $readyScript "$Name-ready"
        if (-not $role.Contains('TextArea')) { throw "Unexpected AX focus role: $role" }
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
        if ((Invoke-AppleScript $editScript "$Name-edit") -cne 'dirty') {
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
            $result.vertical_scroll_status = if ($result.ax_visible_range_before -ne 'unavailable' -and
                $result.ax_visible_range_after -ne 'unavailable' -and
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
        throw
    }
    finally {
        if (-not $child.HasExited) { $child.Kill(); $child.WaitForExit() }
        [IO.File]::WriteAllText((Join-Path $scratch "$Name-stdout.txt"),
            $child.StandardOutput.ReadToEnd(), $utf8)
        [IO.File]::WriteAllText((Join-Path $scratch "$Name-stderr.txt"),
            $child.StandardError.ReadToEnd(), $utf8)
        $child.Dispose()
        $result | ConvertTo-Json -Depth 5 -Compress |
            Add-Content -LiteralPath (Join-Path $cacheRoot 'native-canvas-gui-osx-arm64.jsonl') -Encoding utf8
        Write-Host ($result | ConvertTo-Json -Depth 5 -Compress)
    }
}

$allPassed = $false
try {
    foreach ($name in 'many', 'long') { Invoke-Case $name }
    $allPassed = $true
}
finally {
    if (-not $scratch.StartsWith($scratchRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::Ordinal)) { throw 'Refusing cleanup outside repository .temp.' }
    if ($allPassed) { Remove-Item -LiteralPath $scratch -Recurse -Force }
    else { Write-Warning "Failed Mac canvas probe retained under $scratch" }
}
