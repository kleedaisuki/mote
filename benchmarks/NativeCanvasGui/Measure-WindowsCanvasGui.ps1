# External, bounded Win32 canvas open/edit/save/vertical-scroll measurement.
# Host readiness and edit acceptance are observed; no physical paint is measured.
param(
    [Parameter(Mandatory)][string] $ExecutablePath,
    [ValidateSet('many', 'long')][string[]] $Cases = @('many', 'long'),
    [ValidateSet(1, 10, 100)][int] $ManyMiB = 100,
    [ValidateRange(1, 100)][int] $EditCount = 1,
    [switch] $Trace
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'The Windows canvas GUI probe requires Windows.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$scratchRoot = [IO.Path]::GetFullPath((Join-Path $root '.temp/benchmarks/native-canvas-gui'))
$scratch = [IO.Path]::GetFullPath((Join-Path $scratchRoot ([guid]::NewGuid().ToString('N'))))
$cacheRoot = [IO.Path]::GetFullPath((Join-Path $root '.cache/benchmarks'))
if (-not $scratch.StartsWith($scratchRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase) -or
    -not $cacheRoot.StartsWith([IO.Path]::GetFullPath((Join-Path $root '.cache')) +
    [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Canvas GUI probe paths escaped the repository.'
}
$exe = [IO.Path]::GetFullPath($ExecutablePath)
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Executable absent: $exe" }
New-Item -ItemType Directory -Force -Path $scratch, $cacheRoot | Out-Null
if (-not ('MoteCanvasFixture' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'CanvasFixture.cs')
}
if (-not ('MoteCanvasGuiProbe' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'Win32Probe.cs')
}

# Polling interval and timeout are part of the observer contract, not editor timing.
function Wait-Observed {
    param([scriptblock] $Condition, [string] $Failure, [int] $TimeoutMs = 60000)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.ElapsedMilliseconds -lt $TimeoutMs) {
        $script:child.Refresh()
        if ($script:child.HasExited) {
            throw "Canvas process exited during $script:phase ($($script:child.ExitCode))."
        }
        $script:sampledPeak = [Math]::Max($script:sampledPeak,
            [long]$script:child.PeakWorkingSet64)
        if (& $Condition) { return }
        Start-Sleep -Milliseconds 20
    }
    $state = if ($null -ne $script:lastObserved) {
        $script:lastObserved | ConvertTo-Json -Compress
    } else { '{}' }
    throw "$Failure Last observation: $state"
}

# Nearest-rank summaries retain their raw-sample count; p95 is not a CI SLA.
function Get-Rank {
    param([double[]] $Values, [double] $Fraction)
    if ($Values.Length -eq 0) { return $null }
    $sorted = [double[]]@($Values | Sort-Object)
    return $sorted[[Math]::Ceiling($Fraction * $sorted.Length) - 1]
}

function Invoke-Case {
    param([string] $Name)
    $fileName = if ($Name -eq 'many') { "many-$ManyMiB.txt" } else { 'long-50.txt' }
    $file = Join-Path $scratch $fileName
    if ($Name -eq 'many') { [MoteCanvasFixture]::WriteManyLines($file, $ManyMiB) }
    else { [MoteCanvasFixture]::WriteLongLine($file) }
    $originalLength = (Get-Item -LiteralPath $file).Length
    $originalHash = [MoteCanvasFixture]::Sha256($file)
    $start = [Diagnostics.ProcessStartInfo]::new($exe)
    $start.WorkingDirectory = $root
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment['MOTE_HOME'] = Join-Path $scratch 'mote-home'
    $start.Environment['MOTE_TRACE'] = if ($Trace) { '1' } else { '0' }
    [void]$start.ArgumentList.Add('--canvas-experimental')
    [void]$start.ArgumentList.Add($file)
    $script:phase = "launch-$Name"
    $script:sampledPeak = 0L
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $script:child = [Diagnostics.Process]::Start($start)
    if ($null -eq $script:child) { throw 'Could not launch native canvas.' }
    $script:main = [IntPtr]::Zero
    $script:canvas = [IntPtr]::Zero
    $script:input = [IntPtr]::Zero
    $script:lastObserved = $null
    $result = [ordered]@{
        schema_version = 1
        timestamp_utc = [DateTimeOffset]::UtcNow.ToString('O')
        rid = 'win-x64'
        os = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        runtime = [Environment]::Version.ToString()
        executable_sha256 = [MoteCanvasFixture]::Sha256($exe)
        mode = $Name
        edit_count = $EditCount
        trace_enabled = [bool]$Trace
        source_bytes = $originalLength
        observer = 'external-Win32-window-focus-control-and-disk;not-physical-paint'
        poll_interval_ms = 20
        status = 'unverified'
        stage = $script:phase
        open_to_bound_host_ms = $null
        open_to_host_ready_ms = $null
        edit_to_dirty_ms = $null
        native_input_ack_p50_ms = $null
        native_input_ack_p95_ms = $null
        edit_to_presentation_p50_ms = $null
        edit_to_presentation_p95_ms = $null
        edit_to_presentation_count = 0
        edit_to_presentation_samples_ms = @()
        trace_sample_complete = $null
        gc_observation = 'unavailable-without-Native-AOT-EventPipe-or-internal-GC-marks'
        edit_to_save_ms = $null
        vertical_scroll_ms = $null
        vertical_scroll_position = $null
        horizontal_scroll_status = 'unsupported-in-current-interactive-canvas'
        host_length_before = $null
        host_length_after = $null
        working_set_at_ready_bytes = $null
        working_set_after_edit_bytes = $null
        peak_working_set_bytes = $null
        error = ''
    }
    try {
        $script:phase = "host-bound-$Name"
        Wait-Observed {
            $script:main = [MoteCanvasGuiProbe]::EditorWindow([uint32]$script:child.Id)
            $script:lastObserved = [ordered]@{
                main = $script:main -ne [IntPtr]::Zero
                title = if ($script:main -ne [IntPtr]::Zero) {
                    [MoteCanvasGuiProbe]::WindowTitle($script:main)
                } else { '' }
                canvas = $false
                input = $false
                visible = $false
                input_length = -1
            }
            if ($script:main -eq [IntPtr]::Zero -or
                -not $script:lastObserved.title.Contains([IO.Path]::GetFileName($file))) {
                return $false
            }
            $script:canvas = [MoteCanvasGuiProbe]::FindWindowExW($script:main,
                [IntPtr]::Zero, 'MoteInteractiveCanvas', $null)
            if ($script:canvas -eq [IntPtr]::Zero) { return $false }
            $script:lastObserved.canvas = $true
            $script:input = [MoteCanvasGuiProbe]::GetDlgItem($script:canvas, 301)
            if ($script:input -eq [IntPtr]::Zero) { return $false }
            $script:lastObserved.input = $true
            $script:lastObserved.visible = [MoteCanvasGuiProbe]::IsWindowVisible($script:input)
            $script:lastObserved.input_length = [MoteCanvasGuiProbe]::InputLength($script:input)
            return $script:lastObserved.visible -and $script:lastObserved.input_length -gt 0
        } "The $Name canvas did not bind a visible native input host." 30000
        $result.open_to_bound_host_ms = $watch.Elapsed.TotalMilliseconds
        [void][MoteCanvasGuiProbe]::SetForegroundWindow($script:main)
        $script:phase = "host-focused-$Name"
        Wait-Observed {
            return [MoteCanvasGuiProbe]::FocusedChild($script:main) -eq $script:input
        } "The $Name canvas native input host did not take keyboard focus." 15000
        $result.open_to_host_ready_ms = $watch.Elapsed.TotalMilliseconds
        $result.host_length_before = [MoteCanvasGuiProbe]::InputLength($script:input)
        if ($result.host_length_before -gt 16384) {
            throw "The $Name input host mirrored $($result.host_length_before) characters."
        }
        $script:child.Refresh()
        $result.working_set_at_ready_bytes = if ($script:child.WorkingSet64 -gt 0) {
            [long]$script:child.WorkingSet64
        } else { $null }

        $script:phase = "edit-$Name"
        $ack = [Collections.Generic.List[double]]::new()
        for ($edit = 1; $edit -le $EditCount; $edit++) {
            [void][MoteCanvasGuiProbe]::SendMessageW($script:input, 0x00B1,
                [UIntPtr]::Zero, [IntPtr]::Zero) # EM_SETSEL at source start.
            $editAt = $watch.Elapsed.TotalMilliseconds
            [void][MoteCanvasGuiProbe]::SendMessageW($script:input, 0x0102,
                [UIntPtr][int][char]'X', [IntPtr]::Zero) # Real input-island WM_CHAR.
            $ack.Add($watch.Elapsed.TotalMilliseconds - $editAt)
            if ($edit -eq 1) {
                Wait-Observed {
                    return [MoteCanvasGuiProbe]::WindowTitle($script:main).Contains('•')
                } "The $Name canvas did not accept one native WM_CHAR edit." 15000
                $result.edit_to_dirty_ms = $watch.Elapsed.TotalMilliseconds - $editAt
            }
            # Foreground semantic publication is debounced by 80 ms. Spacing
            # edits avoids silently timing only the final coalesced update.
            if ($edit -lt $EditCount) { Start-Sleep -Milliseconds 300 }
        }
        $result.native_input_ack_p50_ms = Get-Rank $ack.ToArray() 0.50
        $result.native_input_ack_p95_ms = Get-Rank $ack.ToArray() 0.95
        if ($Trace) { Start-Sleep -Milliseconds 200 }
        $result.host_length_after = [MoteCanvasGuiProbe]::InputLength($script:input)
        if ($result.host_length_after -gt 16384) { throw 'Input host expanded beyond 16 Ki units.' }
        $script:child.Refresh()
        $result.working_set_after_edit_bytes = if ($script:child.WorkingSet64 -gt 0) {
            [long]$script:child.WorkingSet64
        } else { $null }

        $script:phase = "save-$Name"
        if (-not [MoteCanvasGuiProbe]::PostMessageW($script:main, 0x0111,
            [UIntPtr]203, [IntPtr]::Zero)) { throw 'Could not post native Save command.' }
        Wait-Observed {
            return (Get-Item -LiteralPath $file).Length -eq $originalLength + $EditCount
        } "The $Name Save did not reach the expected byte length." 30000
        $result.edit_to_save_ms = $watch.Elapsed.TotalMilliseconds - $editAt
        if (-not [MoteCanvasFixture]::HasPrefixedEdits($file, $originalLength,
            $originalHash, $EditCount)) {
            throw "The $Name saved file differs from $EditCount exact prefixed X edits."
        }

        $script:phase = "scroll-$Name"
        if ($Name -eq 'many') {
            $before = [MoteCanvasGuiProbe]::VerticalPosition($script:canvas)
            $scrollAt = $watch.Elapsed.TotalMilliseconds
            [void][MoteCanvasGuiProbe]::SendMessageW($script:canvas, 0x0115,
                [UIntPtr]7, [IntPtr]::Zero) # WM_VSCROLL / SB_BOTTOM; real window dispatch.
            Wait-Observed {
                return [MoteCanvasGuiProbe]::VerticalPosition($script:canvas) -gt $before
            } 'Vertical canvas scrollbar did not advance.' 15000
            $result.vertical_scroll_ms = $watch.Elapsed.TotalMilliseconds - $scrollAt
            $result.vertical_scroll_position = [MoteCanvasGuiProbe]::VerticalPosition($script:canvas)
        }
        else {
            # One logical line cannot evidence vertical movement. It still must
            # accept a real wheel message without growing its bounded host.
            [void][MoteCanvasGuiProbe]::SendMessageW($script:canvas, 0x020A,
                [UIntPtr]7864320, [IntPtr]::Zero) # WM_MOUSEWHEEL, +120.
            if ([MoteCanvasGuiProbe]::InputLength($script:input) -gt 16384) {
                throw 'Long-line wheel event expanded the native host.'
            }
        }
        $script:child.Refresh()
        $script:sampledPeak = [Math]::Max($script:sampledPeak,
            [long]$script:child.PeakWorkingSet64)
        $result.peak_working_set_bytes = if ($script:sampledPeak -gt 0) {
            $script:sampledPeak
        } else { $null }
        $script:phase = "close-$Name"
        if (-not [MoteCanvasGuiProbe]::PostMessageW($script:main, 0x0010,
            [UIntPtr]::Zero, [IntPtr]::Zero)) { throw 'Could not close native window.' }
        if (-not $script:child.WaitForExit(15000) -or $script:child.ExitCode -ne 0) {
            throw 'Native canvas did not exit cleanly.'
        }
        if ($Trace) {
            $traceRoot = Join-Path $scratch 'mote-home/traces'
            $durations = [Collections.Generic.List[double]]::new()
            if (Test-Path -LiteralPath $traceRoot) {
                $traceOutput = Join-Path $cacheRoot ('native-canvas-traces/' +
                    [IO.Path]::GetFileName($scratch) + '-' + $Name)
                New-Item -ItemType Directory -Force -Path $traceOutput | Out-Null
                foreach ($traceFile in Get-ChildItem -LiteralPath $traceRoot -Filter '*.jsonl' -File) {
                    Copy-Item -LiteralPath $traceFile.FullName -Destination $traceOutput
                    foreach ($line in Get-Content -LiteralPath $traceFile.FullName) {
                        $record = $line | ConvertFrom-Json
                        if ($record.operation -ceq 'document.edit_to_presentation') {
                            $durations.Add([double]$record.duration_us / 1000)
                        }
                    }
                }
            }
            $result.edit_to_presentation_count = $durations.Count
            $result.edit_to_presentation_samples_ms = @($durations.ToArray())
            $result.trace_sample_complete = $durations.Count -eq $EditCount
            $result.edit_to_presentation_p50_ms = Get-Rank $durations.ToArray() 0.50
            $result.edit_to_presentation_p95_ms = Get-Rank $durations.ToArray() 0.95
        }
        $result.stage = 'complete'
        $result.status = 'passed'
    }
    catch {
        $result.stage = $script:phase
        $result.error = $_.Exception.Message
        throw
    }
    finally {
        if ($script:child -and -not $script:child.HasExited) {
            $script:child.Kill()
            $script:child.WaitForExit()
        }
        $stdout = $script:child.StandardOutput.ReadToEnd()
        $stderr = $script:child.StandardError.ReadToEnd()
        [IO.File]::WriteAllText((Join-Path $scratch "$Name-stdout.txt"), $stdout)
        [IO.File]::WriteAllText((Join-Path $scratch "$Name-stderr.txt"), $stderr)
        $script:child.Dispose()
        $result | ConvertTo-Json -Depth 5 -Compress | Add-Content -LiteralPath (Join-Path $cacheRoot 'native-canvas-gui-win-x64.jsonl') -Encoding utf8
        Write-Host ($result | ConvertTo-Json -Depth 5 -Compress)
    }
}

$allPassed = $false
try {
    foreach ($name in $Cases) { Invoke-Case $name }
    $allPassed = $true
}
finally {
    if (-not $scratch.StartsWith($scratchRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing cleanup outside repository .temp.' }
    if ($allPassed) { Remove-Item -LiteralPath $scratch -Recurse -Force }
    else { Write-Warning "Failed canvas probe retained under $scratch" }
}
