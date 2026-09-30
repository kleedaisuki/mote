# Probe published macOS preview activation through external AX and real Quartz gestures.
# Each invocation owns one fixture/editor; CI invokes profiles and gestures separately.
param(
    [Parameter(Mandatory)][string] $ExecutablePath,
    [Parameter(Mandatory)][string] $ReportPath,
    [switch] $LegacyPage,
    [switch] $Pointer,
    [switch] $Space
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsMacOS) { throw 'Native macOS preview probe requires macOS.' }

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$scratchRoot = [IO.Path]::GetFullPath((Join-Path $root '.temp/mac-preview-navigation'))
$reportRoot = [IO.Path]::GetFullPath((Join-Path $root '.cache/ci-inventory'))
$scratch = [IO.Path]::GetFullPath((Join-Path $scratchRoot ([guid]::NewGuid().ToString('N'))))
$report = [IO.Path]::GetFullPath((Join-Path $root $ReportPath))
if (-not $scratch.StartsWith($scratchRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::Ordinal) -or
    -not $report.StartsWith($reportRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::Ordinal)) {
    throw 'Probe paths must remain in repository .temp and .cache/ci-inventory.'
}
New-Item -ItemType Directory -Force -Path $scratch, (Split-Path $report) | Out-Null
$source = "# First`n`n## Destination`n"
$target = $source.IndexOf('## Destination', [StringComparison]::Ordinal)
$fixture = Join-Path $scratch 'preview.md'
$utf8 = [Text.UTF8Encoding]::new($false, $true)
[IO.File]::WriteAllText($fixture, $source, $utf8)
$expectedHash = (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash
$exe = [IO.Path]::GetFullPath($ExecutablePath)
$client = Join-Path $scratch 'preview-ax-probe'
$editor = $null
$stage = 'compile'
$gesture = if ($Pointer) { 'pointer' } elseif ($Space) { 'space' } else { 'enter' }
$result = [ordered]@{
    status = 'failed'; stage = $stage
    profile = if ($LegacyPage) { 'legacy-page' } else { 'continuous' }
    gesture = $gesture
    source_length = $source.Length; target_source_offset = $target
    fixture_sha256_before = $expectedHash; fixture_sha256_after = $null
    preview_offset = $null; preview_range_length = $null; preview_bounds = $null
    source_caret_before = $null; source_caret_after = $null
    source_focused_after = $null; preview_read_only = $null
    save_undo_no_mutation = $false; observations = @(); error = $null
    scope = 'External AX/Quartz synthetic gesture; no real IME or VoiceOver speech.'
}

function Invoke-Bounded {
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
        if ($timedOut) { throw "$Name exceeded $TimeoutMs ms." }
        if ($child.ExitCode -ne 0) {
            # Swift may emit deprecation warnings before the actual compiler
            # error. Keep the diagnostic tail bounded without hiding it.
            $tail = $stderr.Substring([Math]::Max(0, $stderr.Length - 16 * 1024))
            throw "$Name exited $($child.ExitCode): $tail"
        }
        return $stdout.Trim()
    }
    finally { if ($null -ne $child) { $child.Dispose() } }
}

function Read-Probe {
    param([string] $Action)
    $raw = Invoke-Bounded $client @([string]$editor.Id, [string]$source.Length, $Action) 8000 $Action
    if ([string]::IsNullOrWhiteSpace($raw)) { throw "AX $Action returned no JSON." }
    return $raw | ConvertFrom-Json -Depth 8
}

function Wait-State {
    param([scriptblock] $Accept, [string] $What, [switch] $Activate)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $last = $null
    while ($timer.ElapsedMilliseconds -lt 18000) {
        $editor.Refresh()
        if ($editor.HasExited) { throw "Editor exited at $stage with $($editor.ExitCode)." }
        $last = Read-Probe 'observe'
        if ($last.status -eq 'ax-unavailable') {
            throw 'External AX permission unavailable; no product verdict.'
        }
        if (& $Accept $last) { return $last }
        if ($Activate -and $last.frontmostPid -ne $editor.Id) {
            $activated = Read-Probe 'activate'
            if ($activated.status -eq 'ax-unavailable') {
                throw 'External AX permission unavailable; no product verdict.'
            }
        }
        Start-Sleep -Milliseconds 120
    }
    $bounded = if ($null -eq $last) { '<none>' } else {
        "status=$($last.status) source=$($last.sourceCandidates)/$($last.sourceLength) " +
        "selection=$($last.sourceSelectionStart):$($last.sourceSelectionLength) " +
        "focus=$($last.sourceFocused) preview=$($last.previewCandidates)/$($last.previewOffset)"
    }
    throw "$What did not converge within 18 s ($bounded)."
}

function Assert-Bytes {
    param([string] $Phase)
    $actual = (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash
    if ($actual -cne $expectedHash) { throw "$Phase changed exact source bytes." }
    return $actual
}

try {
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Published Mach-O absent.' }
    $swift = Join-Path $PSScriptRoot 'MacPreviewNavigationProbe/Probe.swift'
    [void](Invoke-Bounded '/usr/bin/xcrun' @('swiftc', '-typecheck', $swift) 60000 'swift-typecheck')
    [void](Invoke-Bounded '/usr/bin/xcrun' @('swiftc', '-O', $swift, '-o', $client) 90000 'swift-build')

    $stage = 'open'
    $start = [Diagnostics.ProcessStartInfo]::new($exe)
    $start.UseShellExecute = $false
    $start.WorkingDirectory = $root
    $start.Environment['MOTE_HOME'] = Join-Path $scratch 'home'
    if ($LegacyPage) { [void]$start.ArgumentList.Add('--legacy-page') }
    [void]$start.ArgumentList.Add($fixture)
    $editor = [Diagnostics.Process]::Start($start)
    if ($null -eq $editor) { throw 'Editor did not start.' }
    $ready = Wait-State {
        param($state)
        $state.status -eq 'observed' -and $state.frontmostPid -eq $editor.Id -and
            $state.sourceSelectionStart -eq 0 -and $state.sourceSelectionLength -eq 0 -and
            $state.windowDirty -eq $false
    } 'Source/preview initial AX state' -Activate
    $result.preview_offset = $ready.previewOffset
    $result.preview_range_length = $ready.previewRangeLength
    $result.preview_bounds = [ordered]@{
        x = $ready.boundsX; y = $ready.boundsY
        width = $ready.boundsWidth; height = $ready.boundsHeight
    }
    $result.preview_read_only = $ready.previewEditable -eq $false
    $result.source_caret_before = $ready.sourceSelectionStart
    $result.observations += [ordered]@{ stage = 'ready'; status = $ready.status
        source_selection = $ready.sourceSelectionStart; source_focused = $ready.sourceFocused }

    if (-not $Pointer) {
        $stage = 'set-preview-caret'
        $selected = Read-Probe 'select'
        if ($selected.status -ne 'observed' -or $selected.actionError -or
            $selected.previewSelectionStart -ne $ready.previewOffset -or
            $selected.previewSelectionLength -ne 0 -or $selected.previewFocused -ne $true -or
            $selected.sourceSelectionStart -ne 0 -or $selected.sourceSelectionLength -ne 0 -or
            $selected.sourceFocused -ne $false) {
            throw 'AX setup did not focus the exact preview caret without navigating source.'
        }
    }

    $stage = 'activate'
    $action = if ($Pointer) { 'click' } else { $gesture }
    $acted = Read-Probe $action
    if ($acted.actionError) { throw "Preview $action failed: $($acted.actionError)" }
    $after = Wait-State {
        param($state)
        $state.status -eq 'observed' -and $state.frontmostPid -eq $editor.Id -and
            $state.sourceSelectionStart -eq $target -and
            $state.sourceSelectionLength -eq 0 -and $state.sourceFocused -eq $true -and
            $state.windowDirty -eq $false
    } 'Preview-to-source navigation'
    $result.source_caret_after = $after.sourceSelectionStart
    $result.source_focused_after = $after.sourceFocused
    $result.observations += [ordered]@{ stage = 'activated'; status = $after.status
        source_selection = $after.sourceSelectionStart; source_focused = $after.sourceFocused }
    [void](Assert-Bytes 'Preview navigation')

    $stage = 'save-undo'
    foreach ($command in @('save', 'undo')) {
        $commandResult = Read-Probe $command
        if ($commandResult.actionError) { throw "$command failed: $($commandResult.actionError)" }
        Start-Sleep -Milliseconds 200
        [void](Assert-Bytes $command)
    }
    $final = Read-Probe 'observe'
    if ($final.status -ne 'observed' -or $final.sourceSelectionStart -ne $target -or
        $final.sourceSelectionLength -ne 0 -or $final.sourceFocused -ne $true -or
        $final.windowDirty -ne $false) {
        throw 'Save/Undo changed source selection/focus, marked the window dirty, or made AX state unavailable.'
    }
    $result.save_undo_no_mutation = $true
    $result.fixture_sha256_after = Assert-Bytes 'Final observation'
    $result.status = 'passed'
}
catch { $result.error = $_.Exception.Message }
finally {
    if ($null -ne $editor) {
        try {
            if (-not $editor.HasExited) {
                $editor.Kill($true)
                [void]$editor.WaitForExit(10000)
            }
        }
        finally { $editor.Dispose() }
    }
    $result.stage = $stage
    $result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $report -Encoding utf8NoBOM
}
if ($result.status -ne 'passed') {
    throw 'macOS preview navigation diagnostic failed; inspect repository JSON report.'
}
