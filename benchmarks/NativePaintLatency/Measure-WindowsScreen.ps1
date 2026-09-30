# Measure external input acknowledgement and first changed composed-desktop capture.
# Sampling is confined geometrically to a generated mote fixture; five-point
# occlusion checks cannot prove every interior pixel is target-owned.
param(
    [Parameter(Mandatory)][string] $ExecutablePath,
    [ValidateSet('many-1', 'many-10', 'many-100', 'long-50')]
    [string[]] $Cases = @('many-1', 'many-10', 'many-100', 'long-50'),
    [ValidateRange(1, 30)][int] $Repetitions = 3,
    [switch] $AllowLocal,
    [switch] $LocalTopmost
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'The composed-desktop probe requires Windows.' }
$hosted = $env:GITHUB_ACTIONS -ceq 'true' -and $env:RUNNER_OS -ceq 'Windows' -and
    $env:RUNNER_ENVIRONMENT -ceq 'github-hosted'
if (-not $hosted -and -not $AllowLocal) {
    throw 'Run automatically only on GitHub-hosted Windows; use -AllowLocal for an explicit synthetic local run.'
}
if ($LocalTopmost -and (-not $AllowLocal -or $hosted)) {
    throw '-LocalTopmost is permitted only for an explicit local synthetic run.'
}

# Check every existing path component, not only the lexical child path. A
# junction in .temp, .cache, or an ancestor would otherwise redirect a later
# recursive cleanup outside the intended workspace.
function Assert-NoReparseAncestors {
    param([string] $Path)
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -ErrorAction Stop
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "A reparse-point ancestor is not permitted for benchmark paths: $cursor"
            }
        }
        $parent = [IO.Path]::GetDirectoryName($cursor)
        if (-not $parent -or $parent -ceq $cursor) { break }
        $cursor = $parent
    }
}

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$scratchRoot = [IO.Path]::GetFullPath((Join-Path $root '.temp/benchmarks/native-paint-latency'))
$outputRoot = [IO.Path]::GetFullPath((Join-Path $root '.cache/benchmarks/native-paint-latency'))
$runId = [guid]::NewGuid().ToString('N')
$scratch = [IO.Path]::GetFullPath((Join-Path $scratchRoot $runId))
$output = [IO.Path]::GetFullPath((Join-Path $outputRoot $runId))
foreach ($pair in @(@($scratchRoot, $scratch), @($outputRoot, $output))) {
    if (-not $pair[1].StartsWith($pair[0] + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Benchmark paths escaped repository-local .temp or .cache.'
    }
}
$exe = [IO.Path]::GetFullPath($ExecutablePath)
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Published mote executable is absent.' }
Assert-NoReparseAncestors $scratchRoot
Assert-NoReparseAncestors $outputRoot
New-Item -ItemType Directory -Force -Path $scratch, $output | Out-Null
Assert-NoReparseAncestors $scratch
Assert-NoReparseAncestors $output
$reportPath = Join-Path $output 'screen-observations.jsonl'
$cpuName = try { (Get-CimInstance Win32_Processor -ErrorAction Stop |
    Select-Object -First 1 -ExpandProperty Name).Trim() } catch { $null }
$physicalMemoryBytes = try { [long](Get-CimInstance Win32_ComputerSystem -ErrorAction Stop).TotalPhysicalMemory } `
    catch { $null }
Add-Type -Path (Join-Path $PSScriptRoot '../NativeCanvasGui/CanvasFixture.cs')
Add-Type -Path (Join-Path $PSScriptRoot '../NativeCanvasGui/Win32Probe.cs')
Add-Type -Path (Join-Path $PSScriptRoot 'WindowsScreenObserver.cs')

function Wait-Until {
    param([scriptblock] $Condition, [string] $Failure, [int] $TimeoutMs = 30000)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.ElapsedMilliseconds -lt $TimeoutMs) {
        $script:child.Refresh()
        if ($script:child.HasExited) { throw "mote exited during $script:stage ($($script:child.ExitCode))." }
        if (& $Condition) { return }
        Start-Sleep -Milliseconds 20
    }
    throw $Failure
}

function Write-Fixture {
    param([string] $Case, [string] $Path)
    if ($Case -eq 'long-50') { [MoteCanvasFixture]::WriteLongLine($Path) }
    else { [MoteCanvasFixture]::WriteManyLines($Path, [int]$Case.Split('-')[1]) }
    # A unique first-row prefix makes one leading X change observable beyond
    # the independently blinking caret. The rest remains the exact size fixture.
    $header = [Text.Encoding]::ASCII.GetBytes('mote paint sentinel 0123456789')
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Write,
        [IO.FileShare]::None)
    try { $stream.Write($header, 0, $header.Length) }
    finally { $stream.Dispose() }
}

function Remove-GeneratedCase {
    param([string] $CaseDirectory)
    $target = [IO.Path]::GetFullPath($CaseDirectory)
    if (-not $target.StartsWith($scratch + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetDirectoryName($target) -cne $scratch) {
        throw 'Refusing recursive removal outside the generated case parent.'
    }
    if (-not (Test-Path -LiteralPath $target)) { return }
    Assert-NoReparseAncestors $target
    $resolvedTempItem = Resolve-Path -LiteralPath (Join-Path $root '.temp')
    $resolvedTemp = [IO.Path]::GetFullPath($resolvedTempItem.Path)
    $resolvedTarget = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $target).Path)
    if (-not $resolvedTarget.StartsWith($resolvedTemp + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Resolved cleanup target escaped the actual workspace .temp directory.'
    }
    $linkedChild = Get-ChildItem -LiteralPath $target -Force -Recurse -ErrorAction Stop |
        Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 } |
        Select-Object -First 1
    if ($linkedChild) {
        throw 'Generated case contains a reparse-point descendant; refusing recursive removal.'
    }
    Remove-Item -LiteralPath $target -Recurse -Force
}

function Invoke-Case {
    param([string] $Case, [int] $Repetition)
    $name = "$Case-$Repetition"
    $caseDir = [IO.Path]::GetFullPath((Join-Path $scratch $name))
    if (-not $caseDir.StartsWith($scratch + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetDirectoryName($caseDir) -cne $scratch) {
        throw 'Generated case directory escaped its repository-local scratch parent.'
    }
    Assert-NoReparseAncestors $caseDir
    try {
    New-Item -ItemType Directory -Force -Path $caseDir | Out-Null
    Assert-NoReparseAncestors $caseDir
    $file = Join-Path $caseDir 'synthetic.txt'
    Write-Fixture $Case $file
    $bytes = (Get-Item -LiteralPath $file).Length
    $sourceHash = [MoteCanvasFixture]::Sha256($file)
    $start = [Diagnostics.ProcessStartInfo]::new($exe)
    $start.WorkingDirectory = $root
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment['MOTE_HOME'] = Join-Path $caseDir 'mote-home'
    $start.Environment['MOTE_TRACE'] = '0'
    [void]$start.ArgumentList.Add('--canvas-experimental')
    [void]$start.ArgumentList.Add($file)

    $script:stage = 'launch'
    $script:child = [Diagnostics.Process]::Start($start)
    if ($null -eq $script:child) { throw 'Could not start mote.' }
    }
    catch { Remove-GeneratedCase $caseDir; throw }
    $result = [ordered]@{
        schema_version = 1; run_id = $runId; status = 'failed'; stage = $script:stage
        case = $Case; repetition = $Repetition; source_bytes = $bytes
        original_sha256 = $sourceHash; synthetic_source_removed = $false
        executable_sha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
        executable_last_write_utc = (Get-Item -LiteralPath $exe).LastWriteTimeUtc.ToString('O')
        os = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        cpu_name = $cpuName; logical_processors = [Environment]::ProcessorCount
        physical_memory_bytes = $physicalMemoryBytes
        display_dpi = $null; canvas_client_width_px = $null; canvas_client_height_px = $null
        primary_display_width_px = $null; primary_display_height_px = $null
        roi_client_x_px = 42; roi_client_y_px = 4; roi_width_px = 256; roi_height_px = 32
        process_id = $script:child.Id
        foreground_at_focus = $null; foreground_before_edit = $null
        endpoint = 'first visual-change screen-DC capture with later exact-source Undo/Redo screen-state oracle; not compositor-present time'
        negative_control_changed = $null; negative_control_captures = $null
        unsettled_control_attempts = 0; unsettled_control_pixels = @()
        input_ack_ms = $null; first_changed_capture_ms = $null
        first_changed_pixels = $null; capture_count = $null
        baseline_ink_pixels = $null
        first_raw_changed_capture_ms = $null; first_raw_changed_pixels = $null
        settled_changed_pixels = $null
        max_capture_gap_ms = $null; median_capture_cost_ms = $null
        median_owner_check_ms = $null; median_bitblt_ms = $null
        median_readback_ms = $null; first_changed_capture_cost_ms = $null
        first_changed_bitblt_ms = $null
        copy_area_samples_each = $null; copy_area_full_median_ms = $null
        copy_area_small_median_ms = $null
        copy_area_full_bitblt_median_ms = $null
        copy_area_small_bitblt_median_ms = $null
        source_unchanged_before_save = $false; disk_oracle_passed = $false
        undo_exact_oracle = $false; redo_exact_oracle = $false
        undo_screen_distinct = $false; redo_screen_match = $false
        undo_shape_changed_pixels = $null; redo_max_changed_pixels = $null
        source_specific_verified = $false
        failure_code = $null; error = $null
    }
    try {
        $script:main = [IntPtr]::Zero
        $script:canvas = [IntPtr]::Zero
        $script:input = [IntPtr]::Zero
        $script:stage = 'host-ready'
        Wait-Until {
            $script:main = [MoteCanvasGuiProbe]::EditorWindow([uint32]$script:child.Id)
            if ($script:main -eq [IntPtr]::Zero -or
                -not [MoteCanvasGuiProbe]::WindowTitle($script:main).Contains('synthetic.txt')) { return $false }
            $script:canvas = [MoteCanvasGuiProbe]::FindWindowExW($script:main, [IntPtr]::Zero,
                'MoteInteractiveCanvas', $null)
            if ($script:canvas -eq [IntPtr]::Zero) { return $false }
            $script:input = [MoteCanvasGuiProbe]::GetDlgItem($script:canvas, 301)
            return $script:input -ne [IntPtr]::Zero -and
                [MoteCanvasGuiProbe]::IsWindowVisible($script:input) -and
                [MoteWindowsScreenObserver]::InputLengthBounded($script:input) -gt 0
        } 'The bounded input island was not ready.'
        if ($LocalTopmost) {
            [MoteWindowsScreenObserver]::MakeSyntheticTopmost($script:main)
        }
        [void][MoteCanvasGuiProbe]::SetForegroundWindow($script:main)
        $script:stage = 'focus'
        Wait-Until { [MoteCanvasGuiProbe]::FocusedChild($script:main) -eq $script:input } `
            'The native input island did not receive focus.' 15000
        # Windows may refuse focus theft from a noninteractive local Codex
        # launcher. A hosted result is invalid without exact foreground HWND;
        # an explicit local proof-of-method records the weaker visible state.
        [void][MoteCanvasGuiProbe]::SetForegroundWindow($script:main)
        Start-Sleep -Milliseconds 100
        $result.foreground_at_focus = [MoteWindowsScreenObserver]::IsForeground($script:main)
        if ($hosted -and -not $result.foreground_at_focus) {
            throw 'The synthetic mote window is not the hosted foreground HWND.'
        }
        $display = [MoteWindowsScreenObserver]::Describe($script:canvas)
        $result.display_dpi = $display.Dpi
        $result.canvas_client_width_px = $display.CanvasWidth
        $result.canvas_client_height_px = $display.CanvasHeight
        $result.primary_display_width_px = $display.PrimaryWidth
        $result.primary_display_height_px = $display.PrimaryHeight
        [MoteWindowsScreenObserver]::SelectStartBounded($script:input)
        Start-Sleep -Milliseconds 350 # Initial source paint must settle before the control.

        $script:stage = 'negative-control'
        # Native semantic publication can still repaint after the host first
        # appears. Treat those as unsettled warm-up attempts, not edit frames.
        $control = $null
        for ($attempt = 1; $attempt -le 5; $attempt++) {
            $control = [MoteWindowsScreenObserver]::Observe(
                $script:canvas, $script:input, $false, 300)
            if (-not $control.RawChanged) { break }
            $result.unsettled_control_attempts++
            $result.unsettled_control_pixels += $control.FirstRawChangedPixels
            Start-Sleep -Milliseconds 200
        }
        $result.negative_control_changed = $control.RawChanged
        $result.negative_control_captures = $control.Captures
        if ($control.RawChanged) { throw 'WM_NULL changed first-row desktop pixels; baseline is confounded.' }

        $script:stage = 'edit-and-screen'
        $result.foreground_before_edit = [MoteWindowsScreenObserver]::IsForeground($script:main)
        if ($hosted -and -not $result.foreground_before_edit) {
            throw 'The synthetic mote window lost hosted foreground focus before the edit.'
        }
        $edit = [MoteWindowsScreenObserver]::Observe($script:canvas, $script:input, $true, 2500)
        $result.input_ack_ms = $edit.InputAckMs
        $result.first_changed_capture_ms = $edit.FirstChangedCaptureMs
        $result.first_changed_pixels = $edit.ChangedPixels
        $result.first_raw_changed_capture_ms = $edit.FirstRawChangedCaptureMs
        $result.first_raw_changed_pixels = $edit.FirstRawChangedPixels
        $result.settled_changed_pixels = $edit.SettledChangedPixels
        $result.capture_count = $edit.Captures
        $result.baseline_ink_pixels = $edit.BaselineInkPixels
        $result.max_capture_gap_ms = $edit.MaxCaptureGapMs
        $result.median_capture_cost_ms = $edit.MedianCaptureCostMs
        $result.median_owner_check_ms = $edit.MedianOwnerCheckMs
        $result.median_bitblt_ms = $edit.MedianBitBltMs
        $result.median_readback_ms = $edit.MedianReadbackMs
        $result.first_changed_capture_cost_ms = $edit.FirstChangedCaptureCostMs
        $result.first_changed_bitblt_ms = $edit.FirstChangedBitBltMs
        if (-not $edit.Changed) {
            throw 'No stable visible first-row screen change after one WM_CHAR.'
        }
        Wait-Until { [MoteCanvasGuiProbe]::WindowTitle($script:main).Contains('•') } `
            'The native edit did not mark the synthetic file dirty.' 10000
        $result.source_unchanged_before_save =
            (Get-Item -LiteralPath $file).Length -eq $bytes -and
            [MoteCanvasFixture]::Sha256($file) -ceq $sourceHash
        if (-not $result.source_unchanged_before_save) {
            throw 'Source file changed before explicit Save.'
        }

        $script:stage = 'save-oracle'
        if (-not [MoteCanvasGuiProbe]::PostMessageW($script:main, 0x0111,
            [UIntPtr]203, [IntPtr]::Zero)) { throw 'Could not post native Save.' }
        Wait-Until { (Get-Item -LiteralPath $file).Length -eq $bytes + 1 } `
            'Saved fixture did not grow by one byte.' 30000
        $result.disk_oracle_passed = [MoteCanvasFixture]::HasOnePrefixedEdit($file,
            $bytes, $sourceHash)
        if (-not $result.disk_oracle_passed) { throw 'Saved bytes differ from one prefixed X.' }

        # Reverse the exact source state *after* the timing window. A transient
        # blank/foreign repaint cannot pass both source-hash and screen-state
        # A→B→A→B checks, even if it persisted for the first 100 ms.
        $script:stage = 'undo-screen-oracle'
        if (-not [MoteCanvasGuiProbe]::PostMessageW($script:main, 0x0111,
            [UIntPtr]206, [IntPtr]::Zero)) { throw 'Could not post native Undo.' }
        Wait-Until {
            [MoteWindowsScreenObserver]::InputPrefixBounded($script:input).StartsWith(
                'mote paint sentinel', [StringComparison]::Ordinal)
        } 'Undo did not restore the synthetic native-host prefix.' 15000
        if (-not [MoteCanvasGuiProbe]::PostMessageW($script:main, 0x0111,
            [UIntPtr]203, [IntPtr]::Zero)) { throw 'Could not Save the undone source.' }
        Wait-Until { (Get-Item -LiteralPath $file).Length -eq $bytes } `
            'Undo Save did not restore original byte length.' 30000
        $result.undo_exact_oracle = [MoteCanvasFixture]::Sha256($file) -ceq $sourceHash
        if (-not $result.undo_exact_oracle) { throw 'Undo Save differs from the full original SHA-256.' }
        Start-Sleep -Milliseconds 150
        $undoScreen = [MoteWindowsScreenObserver]::ContrastUndoState($script:canvas, $edit)
        $result.undo_shape_changed_pixels = $undoScreen.ShapeDifferentPixels
        $result.undo_screen_distinct = $undoScreen.StableVisible -and
            $undoScreen.ShapeDifferentPixels -ge 100
        if (-not $result.undo_screen_distinct) {
            throw 'Exact-source Undo did not produce a stable, distinct first-row glyph shape.'
        }

        $script:stage = 'redo-screen-oracle'
        if (-not [MoteCanvasGuiProbe]::PostMessageW($script:main, 0x0111,
            [UIntPtr]207, [IntPtr]::Zero)) { throw 'Could not post native Redo.' }
        Wait-Until {
            [MoteWindowsScreenObserver]::InputPrefixBounded($script:input).StartsWith(
                'Xmote paint sentinel', [StringComparison]::Ordinal)
        } 'Redo did not restore the synthetic native-host prefix.' 15000
        if (-not [MoteCanvasGuiProbe]::PostMessageW($script:main, 0x0111,
            [UIntPtr]203, [IntPtr]::Zero)) { throw 'Could not Save the redone source.' }
        Wait-Until { (Get-Item -LiteralPath $file).Length -eq $bytes + 1 } `
            'Redo Save did not restore edited byte length.' 30000
        $result.redo_exact_oracle = [MoteCanvasFixture]::HasOnePrefixedEdit($file,
            $bytes, $sourceHash)
        if (-not $result.redo_exact_oracle) { throw 'Redo Save differs from one-X full suffix SHA-256.' }
        Start-Sleep -Milliseconds 150
        $redoScreen = [MoteWindowsScreenObserver]::MatchState($script:canvas, $edit, $true)
        $result.redo_screen_match = $redoScreen.Matches
        $result.redo_max_changed_pixels = $redoScreen.MaxDifferentPixels
        if (-not $result.redo_screen_match) { throw 'Redo screen does not match the timed candidate image.' }
        $result.source_specific_verified = $true

        # Profile screen-copy area only after the timed edit and its exact
        # source-state oracle. Eighty extra captures must not warm the primary
        # first-edit path or make it incomparable with earlier observations.
        $script:stage = 'observer-area-control'
        $area = [MoteWindowsScreenObserver]::ProfileCopyArea($script:canvas, 20)
        $result.copy_area_samples_each = $area.SamplesPerGeometry
        $result.copy_area_full_median_ms = $area.FullMedianMs
        $result.copy_area_small_median_ms = $area.SmallMedianMs
        $result.copy_area_full_bitblt_median_ms = $area.FullBitBltMedianMs
        $result.copy_area_small_bitblt_median_ms = $area.SmallBitBltMedianMs
        $script:stage = 'close'
        if (-not [MoteCanvasGuiProbe]::PostMessageW($script:main, 0x0010,
            [UIntPtr]::Zero, [IntPtr]::Zero)) { throw 'Could not close mote.' }
        if (-not $script:child.WaitForExit(15000) -or $script:child.ExitCode -ne 0) {
            throw 'mote did not exit cleanly.'
        }
        $result.status = if ($result.foreground_at_focus -and $result.foreground_before_edit) {
            'passed-foreground'
        } else { 'passed-visible-background' }
        $result.stage = 'complete'
    }
    catch {
        $result.stage = $script:stage
        $result.error = $_.Exception.Message
        $cause = $_.Exception
        while ($null -ne $cause) {
            if ($cause -is [TimeoutException]) {
                $result.failure_code = 'cross-process-dispatch-timeout-or-exit'
                break
            }
            $cause = $cause.InnerException
        }
        throw
    }
    finally {
        $cleanupError = $null
        try {
            if (-not $script:child.HasExited) {
                $script:child.Kill()
                if (-not $script:child.WaitForExit(5000)) {
                    throw 'Exact launched child could not be reaped within five seconds.'
                }
            }
            Remove-GeneratedCase $caseDir
            $result.synthetic_source_removed = $true
        }
        catch {
            $cleanupError = $_.Exception.Message
            $result.status = 'failed'
            $result.stage = 'cleanup'
            $result.error = if ($result.error) { "$($result.error); cleanup: $cleanupError" } `
                else { "cleanup: $cleanupError" }
        }
        $result | ConvertTo-Json -Compress -Depth 8 | Add-Content -LiteralPath $reportPath
        $script:child.Dispose()
        if ($cleanupError) { throw $cleanupError }
    }
}

foreach ($case in $Cases) {
    for ($i = 1; $i -le $Repetitions; $i++) { Invoke-Case $case $i }
}
Write-Output "native-screen-observations-ok $reportPath"
