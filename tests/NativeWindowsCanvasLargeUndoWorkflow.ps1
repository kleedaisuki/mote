# Verify an actual opt-in Win32 canvas and focused RichEdit island in a lone Native AOT executable.
# The clipboard is kept in memory and restored; no pasted payload is written to logs.
param(
    [Parameter(Mandatory)][string] $ExecutablePath,
    [ValidateSet(17, 50)][int[]] $SizesMiB = @(17, 50)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'The canvas large-Undo workflow requires Windows.' }

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$scratchRoot = [IO.Path]::GetFullPath((Join-Path $root '.temp/canvas-large-undo'))
$scratch = [IO.Path]::GetFullPath((Join-Path $scratchRoot ([guid]::NewGuid().ToString('N'))))
$reportRoot = [IO.Path]::GetFullPath((Join-Path $root '.cache/canvas-large-undo'))
if (-not $scratch.StartsWith($scratchRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase) -or
    -not $reportRoot.StartsWith([IO.Path]::GetFullPath((Join-Path $root '.cache')) +
    [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Canvas verification artifacts must stay inside repository .temp and .cache.'
}
New-Item -ItemType Directory -Force -Path $scratch, $reportRoot | Out-Null
$reportPath = Join-Path $reportRoot 'latest.json'
$runReportPath = Join-Path $reportRoot ("run-$($SizesMiB -join '-').json")

# These calls cross the real process/window boundary; no in-process controller or fake shell is used.
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
public static class MoteLargeCanvasWin32 {
    public delegate bool EnumWindowsProc(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string className, string windowName);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr window, StringBuilder text, int capacity);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr window, StringBuilder text, int capacity);
    [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr parent, int controlId);
    [DllImport("user32.dll")] public static extern int GetWindowTextLengthW(IntPtr window);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint="SendMessageW", CharSet=CharSet.Unicode)]
    public static extern IntPtr SendMessageText(IntPtr window, uint message, UIntPtr wParam, StringBuilder text);
    [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetFocus();
    [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);
    [StructLayout(LayoutKind.Sequential)] public struct GuiThreadInfo {
        public uint cbSize, flags; public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public int left, top, right, bottom;
    }
    public static IntPtr WindowForProcess(uint id, string wantedClass) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((window, data) => {
            uint pid; GetWindowThreadProcessId(window, out pid);
            if (pid != id) return true;
            var name = new StringBuilder(128); GetClassNameW(window, name, name.Capacity);
            if (name.ToString() == wantedClass) { found = window; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    public static string Text(IntPtr window) {
        var text = new StringBuilder(512); SendMessageText(window, 0x000D, (UIntPtr)text.Capacity, text);
        return text.ToString();
    }
    public static int TextLength(IntPtr window) => (int)SendMessageW(window, 0x000E, UIntPtr.Zero, IntPtr.Zero);
    public static IntPtr Focus(uint threadId) {
        var info = new GuiThreadInfo(); info.cbSize = (uint)Marshal.SizeOf<GuiThreadInfo>();
        return GetGUIThreadInfo(threadId, ref info) ? info.hwndFocus : IntPtr.Zero;
    }
    public static void AssertFile(string path, int payloadLength, bool includePaste) {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        long expected = (includePaste ? payloadLength : 0L) + 4;
        if (file.Length != expected) throw new InvalidDataException($"Saved byte length {file.Length}, expected {expected}.");
        var buffer = new byte[1024 * 1024];
        int remaining = includePaste ? payloadLength : 0;
        while (remaining > 0) {
            int count = Math.Min(buffer.Length, remaining);
            file.ReadExactly(buffer.AsSpan(0, count));
            for (int i = 0; i < count; i++)
                if (buffer[i] != (byte)'P')
                    throw new InvalidDataException($"Saved paste differs at byte {payloadLength - remaining + i}.");
            remaining -= count;
        }
        file.ReadExactly(buffer.AsSpan(0, 4));
        if (buffer[0] != 's' || buffer[1] != 'e' || buffer[2] != 'e' || buffer[3] != 'd')
            throw new InvalidDataException("Saved original suffix differs.");
    }
}
'@

Add-Type -AssemblyName System.Windows.Forms

function Wait-Until {
    param([scriptblock] $Condition, [string] $Failure, [int] $TimeoutMs = 30000)
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.ElapsedMilliseconds -lt $TimeoutMs) {
        if (& $Condition) { return [math]::Round($watch.Elapsed.TotalMilliseconds, 1) }
        Start-Sleep -Milliseconds 50
    }
    throw $Failure
}

function Assert-Bytes {
    param([string] $Path, [int] $PayloadLength, [bool] $IncludePaste)
    # Stream-check the whole byte sequence in compiled code, not a length-only oracle.
    [MoteLargeCanvasWin32]::AssertFile($Path, $PayloadLength, $IncludePaste)
}

function Send-Command {
    param([IntPtr] $Window, [int] $Id)
    if (-not [MoteLargeCanvasWin32]::PostMessageW($Window, 0x0111, [UIntPtr]$Id,
        [IntPtr]::Zero)) { throw "Could not post native menu command $Id." }
}

function Run-Case {
    param([int] $MiB)
    $count = $MiB * 1024 * 1024
    $file = Join-Path $scratch ("paste-$MiB.txt")
    [IO.File]::WriteAllBytes($file, [Text.Encoding]::ASCII.GetBytes('seed'))
    $start = [Diagnostics.ProcessStartInfo]::new($exe)
    $start.WorkingDirectory = $root
    [void]$start.ArgumentList.Add('--canvas-experimental')
    [void]$start.ArgumentList.Add($file)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    if ($null -eq $process) { throw 'Native process did not launch.' }
    $phase = 'launch'
    $caseSucceeded = $false
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $peakWorkingSet = 0L
    $peakPrivateBytes = 0L
    $script:peakWorkingSet = 0L
    $script:peakPrivateBytes = 0L
    try {
        $main = [IntPtr]::Zero
        [void](Wait-Until {
            $process.Refresh()
            if ($process.HasExited) { throw "Native process exited during $phase ($($process.ExitCode))." }
            $script:main = [MoteLargeCanvasWin32]::WindowForProcess([uint32]$process.Id,
                'MoteNativeEditorWindow')
            return $script:main -ne [IntPtr]::Zero -and
                [MoteLargeCanvasWin32]::Text($script:main).Contains("paste-$MiB.txt")
        } 'Canvas file did not appear in the native window title.')
        $main = $script:main
        $canvas = [MoteLargeCanvasWin32]::FindWindowExW($main, [IntPtr]::Zero,
            'MoteInteractiveCanvas', $null)
        if ($canvas -eq [IntPtr]::Zero) { throw 'No opt-in continuous canvas child.' }
        $islandInput = [MoteLargeCanvasWin32]::GetDlgItem($canvas, 301)
        if ($islandInput -eq [IntPtr]::Zero -or -not [MoteLargeCanvasWin32]::IsWindowVisible($islandInput)) {
            throw 'Bound RichEdit input island is absent or hidden.'
        }
        [void](Wait-Until {
            return [MoteLargeCanvasWin32]::Text($islandInput) -ceq 'seed'
        } 'Input island was not bound to the original source.')
        [void][MoteLargeCanvasWin32]::SetForegroundWindow($main)
        $pidValue = [uint32]0
        $thread = [MoteLargeCanvasWin32]::GetWindowThreadProcessId($main, [ref]$pidValue)
        [void](Wait-Until {
            return [MoteLargeCanvasWin32]::Focus($thread) -eq $islandInput
        } 'The actual RichEdit input island did not have keyboard focus.')
        # The first source location is unambiguous, independent of open-time caret policy.
        [void][MoteLargeCanvasWin32]::SendMessageW($islandInput, 0x00B1, [UIntPtr]::Zero,
            [IntPtr]::Zero) # EM_SETSEL(0, 0)
        Start-Sleep -Milliseconds 100

        if ($MiB -eq 17) {
            # Missing Unicode clipboard text must not apply any prefix before reporting failure.
            $phase = 'blocked clipboard'
            [Windows.Forms.Clipboard]::Clear()
            if (-not [MoteLargeCanvasWin32]::PostMessageW($islandInput, 0x0302,
                [UIntPtr]::Zero, [IntPtr]::Zero)) { throw 'Could not post failure-path WM_PASTE.' }
            $dialog = [IntPtr]::Zero
            [void](Wait-Until {
                $script:dialog = [MoteLargeCanvasWin32]::WindowForProcess(
                    [uint32]$process.Id, '#32770')
                return $script:dialog -ne [IntPtr]::Zero
            } 'Missing-text clipboard did not show the expected failure dialog.')
            $dialog = $script:dialog
            if (-not [MoteLargeCanvasWin32]::PostMessageW($dialog, 0x0010,
                [UIntPtr]::Zero, [IntPtr]::Zero)) {
                throw 'Could not dismiss expected clipboard failure dialog.'
            }
            [void](Wait-Until {
                return [MoteLargeCanvasWin32]::WindowForProcess(
                    [uint32]$process.Id, '#32770') -eq [IntPtr]::Zero
            } 'Failure dialog did not close.')
            if ([MoteLargeCanvasWin32]::Text($main).Contains('•') -or
                [MoteLargeCanvasWin32]::Text($islandInput) -cne 'seed') {
                throw 'Failed clipboard paste partially mutated the original document.'
            }
            Assert-Bytes $file 0 $false
        }

        $phase = 'paste'
        $payload = 'P' * $count
        [Windows.Forms.Clipboard]::SetText($payload, [Windows.Forms.TextDataFormat]::UnicodeText)
        $payload = $null
        $pasteAt = $watch.Elapsed.TotalMilliseconds
        # WM_PASTE is the real OS message intercepted by the focused RichEdit subclass.
        if (-not [MoteLargeCanvasWin32]::PostMessageW($islandInput, 0x0302,
            [UIntPtr]::Zero, [IntPtr]::Zero)) { throw 'Could not post WM_PASTE.' }
        [void](Wait-Until {
            $process.Refresh()
            $script:peakWorkingSet = [math]::Max($script:peakWorkingSet,
                $process.WorkingSet64)
            $script:peakPrivateBytes = [math]::Max($script:peakPrivateBytes,
                $process.PrivateMemorySize64)
            return [MoteLargeCanvasWin32]::Text($main).Contains('•')
        } 'Canvas did not mark the large paste modified.' 180000)
        $peakWorkingSet = $script:peakWorkingSet
        $peakPrivateBytes = $script:peakPrivateBytes
        $pasteMs = [math]::Round($watch.Elapsed.TotalMilliseconds - $pasteAt, 1)
        $hostLength = [MoteLargeCanvasWin32]::TextLength($islandInput)
        if ($hostLength -gt 16384) { throw "Native input island expanded to $hostLength characters." }
        $phase = 'save paste'
        Send-Command $main 203
        [void](Wait-Until {
            try { Assert-Bytes $file $count $true; return $true }
            catch { return $false }
        } 'Paste did not save as the exact UTF-8 source bytes.' 180000)
        $pasteSaveMs = [math]::Round($watch.Elapsed.TotalMilliseconds - $pasteAt, 1)

        $phase = 'undo'
        $undoAt = $watch.Elapsed.TotalMilliseconds
        Send-Command $main 206
        [void](Wait-Until {
            return [MoteLargeCanvasWin32]::Text($islandInput) -ceq 'seed' -and
                [MoteLargeCanvasWin32]::Text($main).Contains('•')
        } 'Undo did not restore the bounded original input projection.' 180000)
        Send-Command $main 203
        [void](Wait-Until {
            try { Assert-Bytes $file 0 $false; return $true }
            catch { return $false }
        } 'Undo did not restore exact original document bytes on Save.' 180000)
        $undoSaveMs = [math]::Round($watch.Elapsed.TotalMilliseconds - $undoAt, 1)

        $phase = 'redo'
        $redoAt = $watch.Elapsed.TotalMilliseconds
        Send-Command $main 207
        [void](Wait-Until {
            return [MoteLargeCanvasWin32]::TextLength($islandInput) -le 16384 -and
                [MoteLargeCanvasWin32]::Text($main).Contains('•')
        } 'Redo did not reapply the canvas edit.' 180000)
        Send-Command $main 203
        [void](Wait-Until {
            try { Assert-Bytes $file $count $true; return $true }
            catch { return $false }
        } 'Redo did not restore exact pasted bytes on Save.' 180000)
        $redoSaveMs = [math]::Round($watch.Elapsed.TotalMilliseconds - $redoAt, 1)
        $process.Refresh()
        $peakWorkingSet = [math]::Max($peakWorkingSet, $process.PeakWorkingSet64)
        $peakPrivateBytes = [math]::Max($peakPrivateBytes, $process.PrivateMemorySize64)
        $phase = 'close'
        if (-not [MoteLargeCanvasWin32]::PostMessageW($main, 0x0010,
            [UIntPtr]::Zero, [IntPtr]::Zero)) { throw 'Could not close native window.' }
        if (-not $process.WaitForExit(30000) -or $process.ExitCode -ne 0) {
            throw 'Native process did not exit cleanly.'
        }
        # A second published process performs the reopen; disk inspection alone is not a UI reopen.
        $phase = 'reopen'
        $reopen = [Diagnostics.Process]::Start($start)
        try {
            $reopenMain = [IntPtr]::Zero
            [void](Wait-Until {
                $script:reopenMain = [MoteLargeCanvasWin32]::WindowForProcess(
                    [uint32]$reopen.Id, 'MoteNativeEditorWindow')
                return $script:reopenMain -ne [IntPtr]::Zero -and
                    [MoteLargeCanvasWin32]::Text($script:reopenMain).Contains("paste-$MiB.txt")
            } 'Reopened native process did not show the saved file.' 60000)
            $reopenMain = $script:reopenMain
            $reopenCanvas = [MoteLargeCanvasWin32]::FindWindowExW($reopenMain,
                [IntPtr]::Zero, 'MoteInteractiveCanvas', $null)
            $reopenInput = [MoteLargeCanvasWin32]::GetDlgItem($reopenCanvas, 301)
            if ($reopenInput -eq [IntPtr]::Zero -or
                -not [MoteLargeCanvasWin32]::IsWindowVisible($reopenInput)) {
                throw 'Reopened canvas did not expose a bounded input island.'
            }
            [void](Wait-Until {
                return [MoteLargeCanvasWin32]::Text($reopenInput).StartsWith('P',
                    [StringComparison]::Ordinal)
            } 'Reopened canvas did not display the saved paste at the source start.' 60000)
            if ([MoteLargeCanvasWin32]::TextLength($reopenInput) -gt 16384) {
                throw 'Reopened input island mirrored more than its bounded source interval.'
            }
            Assert-Bytes $file $count $true
            if (-not [MoteLargeCanvasWin32]::PostMessageW($reopenMain, 0x0010,
                [UIntPtr]::Zero, [IntPtr]::Zero) -or -not $reopen.WaitForExit(30000) -or
                $reopen.ExitCode -ne 0) { throw 'Reopened native process did not close cleanly.' }
        }
        finally {
            if ($reopen -and -not $reopen.HasExited) { $reopen.Kill($true) }
            if ($reopen) { $reopen.Dispose() }
        }
        $caseSucceeded = $true
        return [ordered]@{
            size_mib = $MiB
            inserted_utf16_units = $count
            saved_utf8_bytes = $count + 4
            island_chars_after_paste = $hostLength
            paste_to_modified_ms = $pasteMs
            paste_to_exact_save_ms = $pasteSaveMs
            undo_to_exact_save_ms = $undoSaveMs
            redo_to_exact_save_ms = $redoSaveMs
            peak_working_set_bytes = $peakWorkingSet
            peak_private_bytes_approx = $peakPrivateBytes
            reopen = 'native-gui-and-exact-disk-bytes'
            empty_clipboard_failure = if ($MiB -eq 17) { 'no-partial-mutation' } else { 'not-repeated' }
        }
    }
    catch { throw "Canvas $MiB MiB case failed in phase '$phase': $($_.Exception.Message)" }
    finally {
        if (-not $process.HasExited) { $process.Kill($true) }
        if (-not $caseSucceeded) {
            [IO.File]::WriteAllText((Join-Path $scratch "paste-${MiB}-stdout.txt"),
                $process.StandardOutput.ReadToEnd())
            [IO.File]::WriteAllText((Join-Path $scratch "paste-${MiB}-stderr.txt"),
                $process.StandardError.ReadToEnd())
        }
        $process.Dispose()
    }
}

$exe = [IO.Path]::GetFullPath($ExecutablePath)
$inventory = @(Get-ChildItem -LiteralPath (Split-Path $exe) -File)
if (-not (Test-Path -LiteralPath $exe -PathType Leaf) -or
    $inventory.Count -ne 1 -or $inventory[0].FullName -cne $exe) {
    throw 'A published lone executable in its own output directory is required.'
}
$originalClipboard = $null
$clipboardCaptured = $false
$success = $false
$report = [ordered]@{
    status = 'failed'
    executable = $exe
    executable_sha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    cases = @()
    error = ''
    clipboard_restored = $false
    note = 'OS WM_PASTE to focused RichEdit; document length inferred from complete exact Save bytes.'
}
try {
    # IDataObject remains in memory; never serialize clipboard contents to project artifacts.
    $originalClipboard = [Windows.Forms.Clipboard]::GetDataObject()
    $clipboardCaptured = $true
    foreach ($size in $SizesMiB) {
        $report.cases += Run-Case $size
        Write-Host "Canvas $size MiB large Undo workflow passed."
    }
    $success = $true
    $report.status = 'passed'
}
catch {
    $report.error = $_.Exception.Message
}
finally {
    if ($clipboardCaptured) {
        try {
            if ($null -eq $originalClipboard) { [Windows.Forms.Clipboard]::Clear() }
            else { [Windows.Forms.Clipboard]::SetDataObject($originalClipboard, $true) }
            $report.clipboard_restored = $true
        }
        catch { $report.error += " Clipboard restoration failed: $($_.Exception.Message)" }
    }
    $reportJson = $report | ConvertTo-Json -Depth 6
    $reportJson | Set-Content -LiteralPath $reportPath -Encoding utf8NoBOM
    $reportJson | Set-Content -LiteralPath $runReportPath -Encoding utf8NoBOM
    Write-Host ($report | ConvertTo-Json -Depth 6 -Compress)
    if (-not $success) { Write-Warning "Failure artifacts retained under $scratch" }
}
if ($report.status -ne 'passed' -or -not $report.clipboard_restored) { throw $report.error }
