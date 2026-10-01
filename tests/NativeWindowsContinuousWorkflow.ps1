# Exercise the retained --continuous product through a bounded RichEdit island,
# real Win32 messages, exact source bytes, and a GUI reopen; not the release default.
param(
    [Parameter(Mandatory)][string] $ExecutablePath,
    [Parameter(Mandatory)][string] $ReportPath,
    [ValidateSet(0, 100)][int] $ManyMiB = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'This workflow requires Windows.' }

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$scratchRoot = [IO.Path]::GetFullPath((Join-Path $root '.temp/gui-continuous-workflow'))
$scratch = [IO.Path]::GetFullPath((Join-Path $scratchRoot ([guid]::NewGuid().ToString('N'))))
if (-not $scratch.StartsWith($scratchRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'GUI workflow scratch path escaped the repository.'
}
$reportRoot = [IO.Path]::GetFullPath((Join-Path $root '.cache/ci-inventory'))
$report = [IO.Path]::GetFullPath($ReportPath)
if (-not $report.StartsWith($reportRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Continuous report escaped repository .cache/ci-inventory.'
}

# Reject junctions/symlinks in every existing ancestor before fixture creation
# and again before deletion: lexical workspace prefixes alone are insufficient.
function Assert-NoReparseAncestors {
    param([string] $TargetPath)
    $cursor = [IO.Path]::GetFullPath($TargetPath)
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -ErrorAction Stop
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'A reparse-point ancestor is not permitted for synthetic GUI fixtures.'
            }
        }
        $parent = [IO.Path]::GetDirectoryName($cursor)
        if (-not $parent -or $parent -ceq $cursor) { break }
        $cursor = $parent
    }
}
Assert-NoReparseAncestors $scratch
Assert-NoReparseAncestors $report
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path $report) | Out-Null
Assert-NoReparseAncestors $scratch
Assert-NoReparseAncestors $report

# WM_CHAR and WM_COMMAND target the real RichEdit/main-window adapters, not controller internals.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
public static class MoteContinuousGuiProbe {
    public delegate bool EnumWindowsProc(IntPtr window, IntPtr data);
    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string windowName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message,
        UIntPtr wParam, IntPtr lParam, uint flags, uint timeoutMs, out IntPtr result);
    public static IntPtr SendBounded(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam) {
        if (SendMessageTimeout(window, message, wParam, lParam, 3, 3000,
            out var result) == IntPtr.Zero)
            throw new TimeoutException("Win32 editor message failed or exceeded 3 seconds.");
        return result;
    }
    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr window, StringBuilder text, int capacity);
    public static string[] WindowsForProcess(uint expectedId) {
        var windows = new List<string>();
        EnumWindows((window, data) => {
            uint processId;
            GetWindowThreadProcessId(window, out processId);
            if (processId == expectedId) {
                var name = new StringBuilder(256);
                GetClassName(window, name, name.Capacity);
                windows.Add(name.ToString() + ":" + window.ToString());
            }
            return true;
        }, IntPtr.Zero);
        return windows.ToArray();
    }
}
'@

# Polls a condition without consuming unbounded runner time on an inaccessible GUI session.
function Wait-Until {
    param([scriptblock] $Condition, [string] $Failure, [int] $TimeoutMs = 15000)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.ElapsedMilliseconds -lt $TimeoutMs) {
        if (& $Condition) { return }
        Start-Sleep -Milliseconds 25
    }
    throw $Failure
}

# A separate, bounded UIA client checks the ordinary product's single source
# Document in Raw, Control, and Content; no cross-process AXValue is requested.
function Read-SourceTree {
    param([IntPtr] $Window, [IntPtr] $Canvas, [int] $ExpectedPid)
    $start = [Diagnostics.ProcessStartInfo]::new('pwsh')
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @('-NoProfile', '-File',
        (Join-Path $PSScriptRoot 'NativeWindowsContinuousAxTree.ps1'),
        '-WindowHandle', [string]$Window.ToInt64(),
        '-CanvasHandle', [string]$Canvas.ToInt64(),
        '-ExpectedPid', [string]$ExpectedPid)) {
        [void]$start.ArgumentList.Add($argument)
    }
    $child = [Diagnostics.Process]::Start($start)
    try {
        if ($null -eq $child) { throw 'Bounded Windows UIA client did not start.' }
        $outTask = $child.StandardOutput.ReadToEndAsync()
        $errTask = $child.StandardError.ReadToEndAsync()
        $timedOut = -not $child.WaitForExit(20000)
        if ($timedOut) { $child.Kill($true); [void]$child.WaitForExit(10000) }
        $stdout = $outTask.GetAwaiter().GetResult()
        $stderr = $errTask.GetAwaiter().GetResult()
        if ($stdout.Length -gt 8192) { $stdout = $stdout.Substring(0, 8192) + '<truncated>' }
        if ($stderr.Length -gt 4096) { $stderr = $stderr.Substring(0, 4096) + '<truncated>' }
        [IO.File]::WriteAllText((Join-Path $scratch 'uia-tree-stdout.txt'), $stdout)
        [IO.File]::WriteAllText((Join-Path $scratch 'uia-tree-stderr.txt'), $stderr)
        if ($timedOut) { throw 'Windows source UIA tree query exceeded 20 seconds.' }
        if ($child.ExitCode -ne 0) { throw "Windows source UIA tree query failed: $stderr" }
        return $stdout | ConvertFrom-Json
    }
    finally { if ($null -ne $child) { $child.Dispose() } }
}

# Isolate the published editor from the runner account's MOTE_HOME and retain
# only bounded process diagnostics, never source fixture bytes.
$script:childStreams = @{}
function Start-Editor {
    param([string] $Path)
    $start = [Diagnostics.ProcessStartInfo]::new($exe)
    $start.WorkingDirectory = $root
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment['MOTE_HOME'] = Join-Path $scratch 'home'
    [void]$start.ArgumentList.Add('--continuous')
    [void]$start.ArgumentList.Add($Path)
    $child = [Diagnostics.Process]::Start($start)
    if ($null -eq $child) { throw 'Ordinary Continuous editor did not start.' }
    $script:childStreams[[string]$child.Id] = @{
        output = $child.StandardOutput.ReadToEndAsync()
        error = $child.StandardError.ReadToEndAsync()
    }
    return $child
}

function Save-ChildStreams {
    param([Diagnostics.Process] $Child, [string] $Prefix)
    $tasks = $script:childStreams[[string]$Child.Id]
    if ($null -eq $tasks) { return }
    $stdout = $tasks.output.GetAwaiter().GetResult()
    $stderr = $tasks.error.GetAwaiter().GetResult()
    if ($stdout.Length -gt 4096) { $stdout = $stdout.Substring(0, 4096) + '<truncated>' }
    if ($stderr.Length -gt 4096) { $stderr = $stderr.Substring(0, 4096) + '<truncated>' }
    [IO.File]::WriteAllText((Join-Path $scratch "$Prefix-stdout.txt"), $stdout)
    [IO.File]::WriteAllText((Join-Path $scratch "$Prefix-stderr.txt"), $stderr)
    [void]$script:childStreams.Remove([string]$Child.Id)
}

$process = $null
$window = [IntPtr]::Zero
$path = $null
$success = $false
$stage = 'launch'
$result = [ordered]@{
    status = 'failed'; stage = $stage; route = 'ordinary-product-Continuous'
    many_mib = $ManyMiB
    method = 'Win32 child HWND/RichEdit island WM_CHAR and exact disk Save/reopen'
    source_chars = $null; saved_chars = $null
    initial_island_chars = $null; reopened_island_chars = $null
    source_document_tree = $null
    vertical_scroll_before = $null; vertical_scroll_after = $null
    large_fixture_removed = $false
    source_sha256 = $null; error = $null
    limitation = 'No physical IME, screen-reader speech, or compositor-present proof.'
}
try {
    $exe = [IO.Path]::GetFullPath($ExecutablePath)
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Executable not found: $exe" }
    $path = Join-Path $scratch 'note.md'
    Assert-NoReparseAncestors $path
    $utf8 = [Text.UTF8Encoding]::new($false, $true)
    if ($ManyMiB -eq 100) {
        # The 100 MiB corpus and suffix oracle stream through bounded buffers;
        # do not create a 100 MiB PowerShell string or upload the fixture.
        Add-Type -Path (Join-Path $root 'benchmarks/NativeCanvasGui/CanvasFixture.cs')
        Add-Type -Path (Join-Path $root 'benchmarks/NativeCanvasGui/Win32Probe.cs')
        [MoteCanvasFixture]::WriteManyLines($path, 100)
        $result.source_chars = 100L * 1024 * 1024
        $originalHash = [MoteCanvasFixture]::Sha256($path)
        $expected = $null
    }
    else {
        $source = "alpha`n" + ('q' * 81920) + "`nREMOTE-MARKER`n"
        $result.source_chars = $source.Length
        $expected = "X$source"
        [IO.File]::WriteAllText($path, $source, $utf8)
    }
    $readyTimeout = if ($ManyMiB -eq 100) { 60000 } else { 15000 }
    $process = Start-Editor $path
    Wait-Until {
        $process.Refresh()
        $script:window = $process.MainWindowHandle
        if ($script:window -eq [IntPtr]::Zero) { return $false }
        $title = [Text.StringBuilder]::new(256)
        [void][MoteContinuousGuiProbe]::GetWindowText($script:window, $title, $title.Capacity)
        return $title.ToString().Contains('note.md')
    } "Native editor did not show the opened file (exited=$($process.HasExited), window=$window, main=$($process.MainWindowHandle), title=$($process.MainWindowTitle), process_windows=$([string]::Join(',', [MoteContinuousGuiProbe]::WindowsForProcess([uint32]$process.Id))))." $readyTimeout

    $stage = 'bound-island-and-edit'
    $canvas = [MoteContinuousGuiProbe]::FindWindowEx($window,
        [IntPtr]::Zero, 'MoteInteractiveCanvas', $null)
    if ($canvas -eq [IntPtr]::Zero) { throw 'Ordinary route did not create the source canvas.' }
    $editor = [MoteContinuousGuiProbe]::FindWindowEx($canvas,
        [IntPtr]::Zero, 'RICHEDIT50W', $null)
    if ($editor -eq [IntPtr]::Zero) { throw 'Continuous canvas input island not found.' }
    $tree = Read-SourceTree $window $canvas $process.Id
    $result.source_document_tree = $tree
    foreach ($view in @('raw_documents', 'control_documents', 'content_documents')) {
        $documents = @($tree.$view)
        $sources = @($documents | Where-Object automation_id -CEQ 'mote.source.document')
        $previews = @($documents | Where-Object automation_id -CEQ '102')
        if ($documents.Count -ne 2 -or $sources.Count -ne 1 -or
            $previews.Count -ne 1 -or -not $sources[0].has_text_pattern -or
            -not $sources[0].has_value_pattern -or
            $sources[0].value_is_read_only -ne $false -or
            -not $previews[0].has_text_pattern -or
            -not $previews[0].has_value_pattern -or
            $previews[0].accessible_name -cne 'Mote preview' -or
            $previews[0].text_is_read_only -ne $true -or
            $previews[0].value_is_read_only -ne $true) {
            throw "Ordinary UIA $view is not exactly one editable source and one read-only preview."
        }
    }
    if (@($tree.canvas_raw_documents).Count -ne 1 -or
        @($tree.canvas_raw_documents)[0].automation_id -cne 'mote.source.document') {
        throw 'Canvas subtree does not expose exactly one source Document.'
    }
    $focusProven = $tree.focus_status -ceq 'source-focused'
    $initialHostLength = 0L
    Wait-Until {
        $script:initialHostLength = [MoteContinuousGuiProbe]::SendBounded(
            $editor, 0x000E, [UIntPtr]::Zero, [IntPtr]::Zero).ToInt64() # WM_GETTEXTLENGTH
        return $script:initialHostLength -gt 0
    } 'Ordinary Continuous input island did not bind any source text.' $readyTimeout
    if ($initialHostLength -gt 16384) {
        throw "Ordinary route did not bind a bounded input island ($initialHostLength)."
    }
    $result.initial_island_chars = $initialHostLength
    [void][MoteContinuousGuiProbe]::SendBounded($editor, 0x00B1,
        [UIntPtr]::Zero, [IntPtr]::Zero) # EM_SETSEL(0, 0)
    [void][MoteContinuousGuiProbe]::SendBounded($editor, 0x0102, [UIntPtr][int][char]'X', [IntPtr]::Zero) # WM_CHAR
    Wait-Until {
        $title = [Text.StringBuilder]::new(256)
        [void][MoteContinuousGuiProbe]::GetWindowText($window, $title, $title.Capacity)
        return $title.ToString().Contains('•')
    } 'Native edit did not mark the document modified.'

    $stage = 'save-exact-source'
    if (-not [MoteContinuousGuiProbe]::PostMessage($window, 0x0111, [UIntPtr]203, [IntPtr]::Zero)) {
        throw 'Could not send the native Save menu command.'
    }
    Wait-Until {
        try {
            if ($ManyMiB -eq 100) {
                return [MoteCanvasFixture]::HasOnePrefixedEdit(
                    $path, [long]$result.source_chars, $originalHash)
            }
            return [IO.File]::ReadAllText($path, $utf8) -ceq $expected
        }
        catch [IO.IOException] { return $false }
    } 'Continuous Save did not persist exact X-prefixed source.' $readyTimeout
    if ($ManyMiB -eq 100) {
        $result.saved_chars = [long]$result.source_chars + 1
        $result.vertical_scroll_before = [MoteCanvasGuiProbe]::VerticalPosition($canvas)
        if (-not [MoteContinuousGuiProbe]::PostMessage(
            $canvas, 0x0115, [UIntPtr]7, [IntPtr]::Zero)) {
            throw 'Could not dispatch Canvas SB_BOTTOM navigation.'
        }
        Wait-Until {
            $script:scrollPosition = [MoteCanvasGuiProbe]::VerticalPosition($canvas)
            return $script:scrollPosition -gt $result.vertical_scroll_before
        } 'Canvas vertical scrollbar did not move after SB_BOTTOM.' 20000
        $result.vertical_scroll_after = $script:scrollPosition
    }
    else {
        $saved = [IO.File]::ReadAllText($path, $utf8)
        $result.saved_chars = $saved.Length
        if ([Convert]::ToHexString([IO.File]::ReadAllBytes($path)) -cne
            [Convert]::ToHexString($utf8.GetBytes($expected))) {
            throw 'Saved Continuous source bytes differ from the BOMless UTF-8 oracle.'
        }
    }
    if (-not [MoteContinuousGuiProbe]::PostMessage($window, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)) {
        throw 'Could not close the native window.'
    }
    Wait-Until { $process.Refresh(); return $process.HasExited } 'Native process did not exit after close.' $readyTimeout
    if ($process.ExitCode -ne 0) { throw "Native editor exited with code $($process.ExitCode)." }
    Save-ChildStreams $process 'original'

    # Reopen through the published GUI, rather than treating a disk read as a UI reopen.
    $stage = 'reopen-gui'
    $process = Start-Editor $path
    Wait-Until {
        $process.Refresh()
        $script:window = $process.MainWindowHandle
        if ($script:window -eq [IntPtr]::Zero) { return $false }
        $title = [Text.StringBuilder]::new(256)
        [void][MoteContinuousGuiProbe]::GetWindowText($script:window, $title, $title.Capacity)
        return $title.ToString().Contains('note.md')
    } 'Reopened native editor did not show the saved file.' $readyTimeout
    $canvas = [MoteContinuousGuiProbe]::FindWindowEx($window,
        [IntPtr]::Zero, 'MoteInteractiveCanvas', $null)
    if ($canvas -eq [IntPtr]::Zero) { throw 'Reopened source canvas was absent.' }
    $editor = [MoteContinuousGuiProbe]::FindWindowEx($canvas,
        [IntPtr]::Zero, 'RICHEDIT50W', $null)
    if ($editor -eq [IntPtr]::Zero) { throw 'Reopened Continuous input island not found.' }
    $reopenedHostLength = [MoteContinuousGuiProbe]::SendBounded(
        $editor, 0x000E, [UIntPtr]::Zero, [IntPtr]::Zero).ToInt64()
    if ($reopenedHostLength -lt 1 -or $reopenedHostLength -gt 16384) {
        throw "Reopened Continuous input island exceeded 16 Ki ($reopenedHostLength)."
    }
    $result.reopened_island_chars = $reopenedHostLength
    if ($ManyMiB -eq 100) {
        if (-not [MoteCanvasFixture]::HasOnePrefixedEdit(
            $path, [long]$result.source_chars, $originalHash)) {
            throw 'Reopened 100 MiB source failed streamed byte oracle.'
        }
    }
    elseif ([IO.File]::ReadAllText($path, $utf8) -cne $expected) {
        throw 'Reopening Continuous editor changed saved source bytes.'
    }
    if (-not [MoteContinuousGuiProbe]::PostMessage($window, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)) {
        throw 'Could not close the reopened native window.'
    }
    Wait-Until { $process.Refresh(); return $process.HasExited } 'Reopened native process did not exit.' $readyTimeout
    if ($process.ExitCode -ne 0) { throw "Reopened native editor exited with code $($process.ExitCode)." }
    Save-ChildStreams $process 'reopen'
    $success = $focusProven
    $result.source_sha256 = if ($ManyMiB -eq 100) {
        [MoteCanvasFixture]::Sha256($path)
    } else { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
    $result.status = if ($focusProven) { 'passed' } else { 'inconclusive' }
    if (-not $focusProven) {
        $result.error = "Source focus was not proven under a stable mote foreground: $($tree.focus_status)."
    }
    $stage = 'done'
    $result.stage = $stage
}
catch {
    $result.error = $_.Exception.Message
    throw
}
finally {
    if ($process -and -not $process.HasExited) {
        try { Stop-Process -Id $process.Id -Force }
        catch {
            $result.status = 'failed'
            $result.error = 'Exact child process cleanup failed.'
            $success = $false
        }
    }
    if ($process) { Save-ChildStreams $process 'last' }
    if (-not $scratch.StartsWith($scratchRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing recursive cleanup outside repository .temp/gui-continuous-workflow.'
    }
    # A successful byte oracle or foreground-only inconclusive result needs no
    # retained 100 MiB fixture; keep small logs and the .cache JSON report.
    if ($ManyMiB -eq 100 -and $null -ne $path -and
        $result.status -in @('passed', 'inconclusive') -and
        (Test-Path -LiteralPath $path -PathType Leaf)) {
        try {
            $fixtureItem = Get-Item -LiteralPath $path
            if (-not [string]::Equals($fixtureItem.DirectoryName, $scratch,
                [StringComparison]::OrdinalIgnoreCase) -or
                ($fixtureItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw 'Synthetic fixture path or type changed unexpectedly.'
            }
            Assert-NoReparseAncestors $fixtureItem.FullName
            Remove-Item -LiteralPath $fixtureItem.FullName -Force
            $result.large_fixture_removed = $true
        }
        catch {
            $result.status = 'failed'
            $result.error = 'Bounded synthetic fixture cleanup failed.'
            $success = $false
        }
    }
    if (-not $success) { Write-Warning "Retained failed GUI probe under $scratch" }
    $result.stage = $stage
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $report -Encoding utf8NoBOM
    Write-Output ($result | ConvertTo-Json -Depth 8 -Compress)
}
if (-not $success) { throw 'Ordinary Windows Continuous workflow failed; inspect .cache report.' }
