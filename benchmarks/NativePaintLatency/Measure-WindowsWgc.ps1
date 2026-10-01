# Measure a source-verified first observed WGC compositor frame for ordinary mote.
# Synthetic target only; frame pixels remain in observer memory and are never saved.
param(
    [Parameter(Mandatory)][string] $ExecutablePath,
    [ValidateSet('many-1', 'many-100')][string[]] $Cases = @('many-1'),
    [ValidateRange(1, 10)][int] $Repetitions = 1,
    [switch] $AllowLocal
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'WGC requires Windows.' }
$hosted = $env:GITHUB_ACTIONS -ceq 'true' -and $env:RUNNER_OS -ceq 'Windows' -and
    $env:RUNNER_ENVIRONMENT -ceq 'github-hosted'
if (-not $hosted -and -not $AllowLocal) {
    throw 'Automatic capture is hosted-only; use -AllowLocal for a synthetic local probe.'
}

# Reject junction/symlink ancestors before creating or recursively removing data.
function Assert-NoReparseAncestors {
    param([string] $Path)
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -ErrorAction Stop
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Reparse-point ancestor forbidden: $cursor"
            }
        }
        $parent = [IO.Path]::GetDirectoryName($cursor)
        if (-not $parent -or $parent -ceq $cursor) { break }
        $cursor = $parent
    }
}

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$scratchRoot = [IO.Path]::GetFullPath((Join-Path $root '.temp/benchmarks/native-wgc-latency'))
$outputRoot = [IO.Path]::GetFullPath((Join-Path $root '.cache/benchmarks/native-wgc-latency'))
$runId = [guid]::NewGuid().ToString('N')
$scratch = [IO.Path]::GetFullPath((Join-Path $scratchRoot $runId))
$output = [IO.Path]::GetFullPath((Join-Path $outputRoot $runId))
$exe = [IO.Path]::GetFullPath($ExecutablePath)
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'AOT executable absent.' }
foreach ($path in @($scratchRoot, $outputRoot, $scratch, $output)) {
    Assert-NoReparseAncestors $path
}
New-Item -ItemType Directory -Force -Path $scratch, $output | Out-Null
$reportPath = Join-Path $output 'wgc-observations.jsonl'
$observerPath = Join-Path $root '.cache/benchmarks/native-paint-latency/wgc/WgcEditObserver.exe'
if (-not (Test-Path -LiteralPath $observerPath -PathType Leaf) -or
    (Get-Item -LiteralPath (Join-Path $PSScriptRoot 'WgcEditObserver.cpp')).LastWriteTimeUtc -gt
    (Get-Item -LiteralPath $observerPath).LastWriteTimeUtc) {
    & (Join-Path $PSScriptRoot 'Build-WgcObserver.ps1') | Out-Null
}
if (-not (Test-Path -LiteralPath $observerPath -PathType Leaf)) { throw 'WGC helper absent.' }
Add-Type -Path (Join-Path $PSScriptRoot '../NativeCanvasGui/CanvasFixture.cs')
Add-Type -Path (Join-Path $PSScriptRoot '../NativeCanvasGui/Win32Probe.cs')
Add-Type -Path (Join-Path $PSScriptRoot 'WindowsScreenObserver.cs')
. (Join-Path $PSScriptRoot 'WgcStateOracle.ps1')
$cpuName = try { (Get-CimInstance Win32_Processor -ErrorAction Stop |
    Select-Object -First 1 -ExpandProperty Name).Trim() } catch { $null }
$gpuName = try { (Get-CimInstance Win32_VideoController -ErrorAction Stop |
    Select-Object -First 1 -ExpandProperty Name).Trim() } catch { $null }
$physicalMemory = try { [long](Get-CimInstance Win32_ComputerSystem -ErrorAction Stop).TotalPhysicalMemory } `
    catch { $null }

# Bounded polling keeps all target failures recoverable rather than hanging CI.
function Wait-Until {
    param([scriptblock] $Condition, [string] $Failure, [int] $TimeoutMs = 15000)
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.ElapsedMilliseconds -lt $TimeoutMs) {
        $script:child.Refresh()
        if ($script:child.HasExited) { throw "mote exited during $($script:stage)." }
        if (& $Condition) { return }
        Start-Sleep -Milliseconds 20
    }
    throw $Failure
}

# Only exact generated case children under the resolved workspace .temp may be removed.
function Remove-GeneratedCase {
    param([string] $CaseDirectory)
    $target = [IO.Path]::GetFullPath($CaseDirectory)
    if ([IO.Path]::GetDirectoryName($target) -cne $scratch -or
        -not $target.StartsWith($scratch + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing removal outside generated case parent.'
    }
    if (-not (Test-Path -LiteralPath $target)) { return }
    Assert-NoReparseAncestors $target
    $resolvedTemp = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath (Join-Path $root '.temp')).Path)
    $resolvedTarget = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $target).Path)
    if (-not $resolvedTarget.StartsWith($resolvedTemp + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Resolved cleanup target escaped repository .temp.'
    }
    $linkedChild = Get-ChildItem -LiteralPath $target -Force -Recurse -ErrorAction Stop |
        Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 } |
        Select-Object -First 1
    if ($linkedChild) { throw 'Generated case has reparse-point descendant.' }
    Remove-Item -LiteralPath $target -Recurse -Force
}

# Parse only fixed numeric/signature records; never accept raw pixels or source bytes.
function Read-ObserverRecord {
    param([string[]] $Lines)
    $meta = $null
    $edit = $null
    $frames = @()
    $states = @()
    foreach ($line in $Lines) {
        $fields = $line.Split(',')
        if ($fields[0] -eq 'META' -and $fields.Count -eq 9) {
            $meta = [pscustomobject]@{
                mode = $fields[1]; frequency = [long]$fields[2]
                roi_x = [int]$fields[3]; roi_y = [int]$fields[4]
                warp = [bool][int]$fields[5]; overflow = [int]$fields[6]
                errors = [int]$fields[7]; required_flags = [int]$fields[8]
            }
        } elseif ($fields[0] -eq 'FRAME' -and $fields.Count -eq 9) {
            $frames += [pscustomobject]@{
                phase = $fields[1]; system_100ns = [long]$fields[2]
                arrival_qpc = [long]$fields[3]; readback_ticks = [long]$fields[4]
                signature = $fields[5]; ink = [int]$fields[6]
                changed_pixels = [int]$fields[7]; target_flags = [int]$fields[8]
            }
        } elseif ($fields[0] -eq 'STATE' -and $fields.Count -eq 4) {
            $states += [pscustomobject]@{
                phase = $fields[1]; target_flags = [int]$fields[2]
                qpc = [long]$fields[3]
            }
        } elseif ($fields[0] -eq 'EDIT' -and $fields.Count -eq 6) {
            $edit = [pscustomobject]@{
                dispatch_qpc = [long]$fields[1]; ack_qpc = [long]$fields[2]
                control_frames = [int]$fields[3]; overflow = [int]$fields[4]
                errors = [int]$fields[5]
            }
        } else { throw "Unexpected WGC helper output record: $line" }
    }
    if (-not $meta -or $frames.Count -lt 1) { throw 'Incomplete WGC helper output.' }
    return [pscustomobject]@{ meta = $meta; frames = $frames; states = $states; edit = $edit }
}

# Run the exact-window native helper; a nonzero exit is inconclusive, not a fallback.
function Invoke-Observer {
    param([string] $Mode, [IntPtr] $Main, [IntPtr] $Canvas, [IntPtr] $InputHwnd)
    $args = @($Mode, $Main.ToInt64().ToString(), $Canvas.ToInt64().ToString())
    if ($Mode -eq 'edit') { $args += $InputHwnd.ToInt64().ToString() }
    $start = [Diagnostics.ProcessStartInfo]::new($observerPath)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($arg in $args) { [void]$start.ArgumentList.Add($arg) }
    $helper = [Diagnostics.Process]::Start($start)
    if (-not $helper) { throw 'WGC helper failed to start.' }
    try {
        $stdout = $helper.StandardOutput.ReadToEndAsync()
        $stderr = $helper.StandardError.ReadToEndAsync()
        if (-not $helper.WaitForExit(20000)) {
            $helper.Kill()
            [void]$helper.WaitForExit(5000)
            throw 'WGC helper exceeded its 20-second process deadline.'
        }
        $output = $stdout.GetAwaiter().GetResult()
        $errors = $stderr.GetAwaiter().GetResult()
        if ($helper.ExitCode -ne 0) {
            throw "WGC helper exit $($helper.ExitCode)`: $errors"
        }
        $lines = @($output -split '\r?\n' | Where-Object { $_ -ne '' })
    }
    finally { $helper.Dispose() }
    if (-not $lines) {
        throw 'WGC helper returned no metadata records.'
    }
    return Read-ObserverRecord $lines
}

# Make exact-size generated source; no user document is ever opened.
function Write-Fixture {
    param([string] $Case, [string] $Path)
    [MoteCanvasFixture]::WriteManyLines($Path, [int]$Case.Split('-')[1])
    $prefix = [Text.Encoding]::ASCII.GetBytes('mote paint sentinel 0123456789')
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Write,
        [IO.FileShare]::None)
    try { $stream.Write($prefix, 0, $prefix.Length); $stream.Flush($true) }
    finally { $stream.Dispose() }
}

# One target process supplies the timed edit and later exact-source A/B oracles.
function Invoke-Case {
    param([string] $Case, [int] $Repetition)
    $caseDir = [IO.Path]::GetFullPath((Join-Path $scratch "$Case-$Repetition"))
    $script:child = $null
    try {
        Assert-NoReparseAncestors $caseDir
        New-Item -ItemType Directory -Path $caseDir | Out-Null
        $file = Join-Path $caseDir 'synthetic.txt'
        Write-Fixture $Case $file
        $bytes = (Get-Item -LiteralPath $file).Length
        $sourceHash = [MoteCanvasFixture]::Sha256($file)
        $script:stage = 'launch'
        $start = [Diagnostics.ProcessStartInfo]::new($exe)
        $start.WorkingDirectory = $root
        $start.UseShellExecute = $false
        $start.Environment['MOTE_HOME'] = Join-Path $caseDir 'mote-home'
        $start.Environment['MOTE_TRACE'] = '0'
        [void]$start.ArgumentList.Add($file) # Ordinary Continuous, no opt-in switch.
        $script:child = [Diagnostics.Process]::Start($start)
        if (-not $script:child) { throw 'mote failed to start.' }
    }
    catch {
        if ($script:child -and -not $script:child.HasExited) {
            $script:child.Kill()
            [void]$script:child.WaitForExit(5000)
        }
        Remove-GeneratedCase $caseDir
        throw
    }
    $result = [ordered]@{
        schema_version = 1; run_id = $runId; case = $Case; repetition = $Repetition
        status = 'inconclusive'; stage = $script:stage; error = $null
        exe_sha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
        observer_sha256 = (Get-FileHash -LiteralPath $observerPath -Algorithm SHA256).Hash
        source_bytes = $bytes; source_sha256 = $sourceHash
        os = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        cpu_name = $cpuName; gpu_name = $gpuName; physical_memory_bytes = $physicalMemory
        logical_processors = [Environment]::ProcessorCount
        process_id = $script:child.Id; foreground = $false
        canvas_client_width_px = $null; canvas_client_height_px = $null
        canvas_dpi = $null; primary_display_width_px = $null; primary_display_height_px = $null
        roi_width = 220; roi_height = 24; roi_x = $null; roi_y = $null
        undo_roi_x = $null; undo_roi_y = $null; redo_roi_x = $null; redo_roi_y = $null
        baseline_signature = $null; undo_signature = $null; redo_signature = $null
        warp = $null; qpc_frequency = $null; frame_count = $null
        wgc_pool_drops_unknown = $true; sample_vector_overflow = $null
        no_op_max_changed_pixels = $null; first_b_changed_pixels = $null
        baseline_ink_pixels = $null; undo_ink_pixels = $null
        redo_ink_pixels = $null; first_b_ink_pixels = $null
        target_flags_pre = $null; target_flags_post = $null
        target_flags_first_b = $null; target_required_flags = $null
        target_state_valid = $false; target_state_transition = $null
        sampled_target_frames = $null; sampled_invalid_target_frames = $null
        input_ack_ms = $null; first_observed_b_metadata_delta_ms = $null
        diagnostic_raw_metadata_minus_dispatch_ms = $null
        first_b_system_minus_arrival_ms = $null
        first_b_observer_processing_ms = $null; clock_order_anomaly = $null
        max_observer_processing_ms = $null; max_callback_arrival_gap_ms = $null
        capture_errors = $null
        baseline_matches_undo = $false; redo_matches_timed_b = $false
        source_unchanged_before_save = $false; save_b_exact = $false
        undo_a_exact = $false; redo_b_exact = $false
        first_observed_not_first_guaranteed = $true
        privacy = 'synthetic exact-HWND ROI; no captured pixels persisted'
        fixture_removed = $false
    }
    try {
        $script:main = [IntPtr]::Zero
        $script:canvas = [IntPtr]::Zero
        $script:input = [IntPtr]::Zero
        $script:stage = 'ready'
        Wait-Until {
            $script:main = [MoteCanvasGuiProbe]::EditorWindow([uint32]$script:child.Id)
            if ($script:main -eq [IntPtr]::Zero -or
                -not [MoteCanvasGuiProbe]::WindowTitle($script:main).Contains('synthetic.txt')) {
                return $false
            }
            $script:canvas = [MoteCanvasGuiProbe]::FindWindowExW($script:main,
                [IntPtr]::Zero, 'MoteInteractiveCanvas', $null)
            if ($script:canvas -eq [IntPtr]::Zero) { return $false }
            $script:input = [MoteCanvasGuiProbe]::GetDlgItem($script:canvas, 301)
            return $script:input -ne [IntPtr]::Zero -and
                [MoteCanvasGuiProbe]::IsWindowVisible($script:input) -and
                [MoteWindowsScreenObserver]::InputLengthBounded($script:input) -gt 0
        } 'The ordinary Continuous native input island was not ready.' 30000
        [void][MoteCanvasGuiProbe]::SetForegroundWindow($script:main)
        Wait-Until { [MoteCanvasGuiProbe]::FocusedChild($script:main) -eq $script:input } `
            'The exact native input island did not receive focus.' 15000
        $result.foreground = [MoteWindowsScreenObserver]::IsForeground($script:main)
        $display = [MoteWindowsScreenObserver]::Describe($script:canvas)
        $result.canvas_client_width_px = $display.CanvasWidth
        $result.canvas_client_height_px = $display.CanvasHeight
        $result.canvas_dpi = $display.Dpi
        $result.primary_display_width_px = $display.PrimaryWidth
        $result.primary_display_height_px = $display.PrimaryHeight
        if ($hosted -and -not $result.foreground) {
            throw 'Hosted synthetic mote window did not become foreground.'
        }
        $script:stage = 'wgc-edit'
        $timed = Invoke-Observer edit $script:main $script:canvas $script:input
        if (-not $timed.edit) { throw 'WGC helper omitted edit timestamp.' }
        $result.roi_x = $timed.meta.roi_x
        $result.roi_y = $timed.meta.roi_y
        $result.warp = $timed.meta.warp
        $result.qpc_frequency = $timed.meta.frequency
        $result.frame_count = $timed.frames.Count
        $result.sample_vector_overflow = $timed.edit.overflow
        $result.capture_errors = $timed.edit.errors
        $result.target_required_flags = $timed.meta.required_flags
        $pre = @($timed.states | Where-Object phase -eq 'pre')[-1]
        $post = @($timed.states | Where-Object phase -eq 'post')[-1]
        if (-not $pre -or -not $post) { throw 'WGC helper omitted target revalidation state.' }
        $result.target_flags_pre = $pre.target_flags
        $result.target_flags_post = $post.target_flags
        $observedFrames = @($timed.frames | Where-Object phase -eq 'timed')
        if ($observedFrames.Count) {
            $processing = @($observedFrames | ForEach-Object {
                1000 * $_.readback_ticks / [double]$timed.meta.frequency
            })
            $result.max_observer_processing_ms = [Math]::Round(
                ($processing | Measure-Object -Maximum).Maximum, 4)
            $maxGap = 0.0
            for ($i = 1; $i -lt $observedFrames.Count; $i++) {
                $gap = 1000 * ($observedFrames[$i].arrival_qpc -
                    $observedFrames[$i - 1].arrival_qpc) / [double]$timed.meta.frequency
                if ($gap -gt $maxGap) { $maxGap = $gap }
            }
            $result.max_callback_arrival_gap_ms = [Math]::Round($maxGap, 4)
        }
        $control = @($timed.frames | Where-Object phase -eq 'control')
        $result.no_op_max_changed_pixels = if ($control.Count) {
            ($control | Measure-Object -Property changed_pixels -Maximum).Maximum
        } else { 0 }
        if ($result.no_op_max_changed_pixels -gt 64) {
            throw 'WM_NULL changed the captured source-row ROI materially.'
        }
        $result.source_unchanged_before_save =
            (Get-Item -LiteralPath $file).Length -eq $bytes -and
            [MoteCanvasFixture]::Sha256($file) -ceq $sourceHash
        if (-not $result.source_unchanged_before_save) {
            throw 'Source changed before explicit Save.'
        }
        $script:stage = 'save-b'
        if (-not [MoteCanvasGuiProbe]::PostMessageW($script:main, 0x0111,
            [UIntPtr]203, [IntPtr]::Zero)) { throw 'Could not post Save.' }
        Wait-Until { (Get-Item -LiteralPath $file).Length -eq $bytes + 1 } `
            'Saved fixture did not grow by one byte.' 30000
        $result.save_b_exact = [MoteCanvasFixture]::HasOnePrefixedEdit(
            $file, $bytes, $sourceHash)
        if (-not $result.save_b_exact) { throw 'Save B full-source oracle failed.' }
        $script:stage = 'undo-a'
        if (-not [MoteCanvasGuiProbe]::PostMessageW($script:main, 0x0111,
            [UIntPtr]206, [IntPtr]::Zero)) { throw 'Could not post Undo.' }
        Wait-Until { [MoteWindowsScreenObserver]::InputPrefixBounded($script:input).StartsWith(
            'mote paint sentinel', [StringComparison]::Ordinal) } 'Undo prefix absent.'
        if (-not [MoteCanvasGuiProbe]::PostMessageW($script:main, 0x0111,
            [UIntPtr]203, [IntPtr]::Zero)) { throw 'Could not Save Undo.' }
        Wait-Until { (Get-Item -LiteralPath $file).Length -eq $bytes } `
            'Undo Save size wrong.' 30000
        $result.undo_a_exact = [MoteCanvasFixture]::Sha256($file) -ceq $sourceHash
        if (-not $result.undo_a_exact) { throw 'Undo A full-source oracle failed.' }
        Start-Sleep -Milliseconds 150
        $undo = Invoke-Observer snapshot $script:main $script:canvas $script:input
        $initialA = @($timed.frames | Where-Object phase -eq 'baseline')[-1]
        $undoA = @($undo.frames | Where-Object phase -eq 'baseline')[-1]
        $result.baseline_ink_pixels = $initialA.ink
        $result.undo_ink_pixels = $undoA.ink
        if ($initialA.ink -lt 100 -or $undoA.ink -lt 100) {
            throw 'Initial/Undo A WGC glyph ROI is blank or low contrast.'
        }
        $result.undo_roi_x = $undo.meta.roi_x
        $result.undo_roi_y = $undo.meta.roi_y
        $result.baseline_signature = $initialA.signature
        $result.undo_signature = $undoA.signature
        if ($result.roi_x -ne $result.undo_roi_x -or
            $result.roi_y -ne $result.undo_roi_y) {
            throw 'WGC ROI moved between timed edit and Undo reference.'
        }
        $result.baseline_matches_undo = $initialA.signature -ceq $undoA.signature
        if (-not $result.baseline_matches_undo) { throw 'Initial/Undo A WGC signatures differ.' }
        $script:stage = 'redo-b'
        if (-not [MoteCanvasGuiProbe]::PostMessageW($script:main, 0x0111,
            [UIntPtr]207, [IntPtr]::Zero)) { throw 'Could not post Redo.' }
        Wait-Until { [MoteWindowsScreenObserver]::InputPrefixBounded($script:input).StartsWith(
            'Xmote paint sentinel', [StringComparison]::Ordinal) } 'Redo prefix absent.'
        if (-not [MoteCanvasGuiProbe]::PostMessageW($script:main, 0x0111,
            [UIntPtr]203, [IntPtr]::Zero)) { throw 'Could not Save Redo.' }
        Wait-Until { (Get-Item -LiteralPath $file).Length -eq $bytes + 1 } `
            'Redo Save size wrong.' 30000
        $result.redo_b_exact = [MoteCanvasFixture]::HasOnePrefixedEdit(
            $file, $bytes, $sourceHash)
        if (-not $result.redo_b_exact) { throw 'Redo B full-source oracle failed.' }
        Start-Sleep -Milliseconds 150
        $redo = Invoke-Observer snapshot $script:main $script:canvas $script:input
        $redoB = @($redo.frames | Where-Object phase -eq 'baseline')[-1]
        $result.redo_ink_pixels = $redoB.ink
        if ($redoB.ink -lt 100) { throw 'Redo B WGC glyph ROI is blank or low contrast.' }
        $result.redo_roi_x = $redo.meta.roi_x
        $result.redo_roi_y = $redo.meta.roi_y
        $result.redo_signature = $redoB.signature
        if ($result.roi_x -ne $result.redo_roi_x -or
            $result.roi_y -ne $result.redo_roi_y) {
            throw 'WGC ROI moved between timed edit and Redo reference.'
        }
        if ($redoB.signature -ceq $undoA.signature) {
            throw 'A/B WGC signatures are identical despite exact source change.'
        }
        $candidates = @($timed.frames | Where-Object {
            $_.phase -eq 'timed' -and $_.signature -ceq $redoB.signature -and
            $_.changed_pixels -ge 128
        })
        $result.redo_matches_timed_b = $candidates.Count -gt 0
        if (-not $result.redo_matches_timed_b) {
            throw 'No timed WGC frame matches the later exact Redo B glyph state.'
        }
        $firstB = $candidates[0]
        $result.first_b_changed_pixels = $firstB.changed_pixels
        $result.first_b_ink_pixels = $firstB.ink
        if ($firstB.ink -lt 100) { throw 'First B WGC glyph ROI is blank or low contrast.' }
        $result.target_flags_first_b = $firstB.target_flags
        $required = $timed.meta.required_flags
        # Baseline, quiet control and every timed callback participate.
        # Sparse callbacks cannot prove state between sampled frames.
        $state = Test-SampledTargetState $pre.target_flags $post.target_flags `
            $required @($timed.frames)
        $result.sampled_target_frames = $state.sampled_frames
        $result.sampled_invalid_target_frames = $state.invalid_frames
        $result.target_state_transition = $state.transition
        $result.target_state_valid = $state.valid
        $frequency = [double]$timed.meta.frequency
        $result.input_ack_ms = [Math]::Round(1000 *
            ($timed.edit.ack_qpc - $timed.edit.dispatch_qpc) / $frequency, 4)
        $dispatch100ns = [decimal]$timed.edit.dispatch_qpc * 10000000 / $timed.meta.frequency
        $arrival100ns = [decimal]$firstB.arrival_qpc * 10000000 / $timed.meta.frequency
        # A compositor-render timestamp materially after callback arrival
        # violates the assumed QPC ordering. Allow only 1 microsecond for
        # cross-thread/QPC conversion rounding, not a whole display millisecond.
        $result.clock_order_anomaly = [decimal]$firstB.system_100ns - $arrival100ns -gt 10
        $deltaMs = ([decimal]$firstB.system_100ns - $dispatch100ns) / 10000
        $result.first_b_system_minus_arrival_ms = [Math]::Round(
            [double](([decimal]$firstB.system_100ns - $arrival100ns) / 10000), 4)
        $result.first_b_observer_processing_ms = [Math]::Round(1000 *
            $firstB.readback_ticks / $frequency, 4)
        if ($deltaMs -lt 0 -or $deltaMs -gt 10000) {
            throw 'WGC timestamp cannot be aligned to dispatch QPC.'
        }
        $result.diagnostic_raw_metadata_minus_dispatch_ms =
            [Math]::Round([double]$deltaMs, 4)
        if ($result.target_state_valid -and -not $result.warp -and
            -not $result.clock_order_anomaly -and
            -not $result.sample_vector_overflow -and -not $result.capture_errors) {
            $result.first_observed_b_metadata_delta_ms =
                $result.diagnostic_raw_metadata_minus_dispatch_ms
        }
        $result.status = if ($result.target_state_valid -and -not $result.warp -and
            -not $result.clock_order_anomaly -and
            -not $result.sample_vector_overflow -and -not $result.capture_errors) {
            'source_verified_first_observed_wgc_frame'
        } else { 'source_verified_but_timing_inconclusive' }
        $result.stage = 'complete'
    }
    catch {
        $result.stage = $script:stage
        $result.error = $_.Exception.Message
    }
    finally {
        $cleanupError = $null
        try {
            if (-not $script:child.HasExited) {
                $script:child.Kill()
                if (-not $script:child.WaitForExit(5000)) {
                    throw 'Exact launched child could not be reaped.'
                }
            }
            Remove-GeneratedCase $caseDir
            $result.fixture_removed = $true
        } catch {
            $cleanupError = $_.Exception.Message
            $result.status = 'cleanup_failed'
            $result.error = if ($result.error) { "$($result.error); cleanup: $cleanupError" } `
                else { "cleanup: $cleanupError" }
        }
        $result | ConvertTo-Json -Compress -Depth 6 | Add-Content -LiteralPath $reportPath
        $script:child.Dispose()
        if ($cleanupError) { throw $cleanupError }
    }
}

foreach ($case in $Cases) {
    for ($i = 1; $i -le $Repetitions; $i++) { Invoke-Case $case $i }
}
Write-Output "native-wgc-observations $reportPath"
