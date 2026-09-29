# Exercise a published Win32 editor through real OS window/control messages and disk I/O.
param([Parameter(Mandatory)][string] $ExecutablePath)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'This workflow requires Windows.' }

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$scratchRoot = [IO.Path]::GetFullPath((Join-Path $root '.temp/gui-workflow'))
$scratch = [IO.Path]::GetFullPath((Join-Path $scratchRoot ([guid]::NewGuid().ToString('N'))))
if (-not $scratch.StartsWith($scratchRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'GUI workflow scratch path escaped the repository.'
}
New-Item -ItemType Directory -Force -Path $scratch | Out-Null

# WM_CHAR and WM_COMMAND target the real RichEdit/main-window adapters, not controller internals.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
public static class MoteGuiProbe {
    public delegate bool EnumWindowsProc(IntPtr window, IntPtr data);
    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string className, string windowName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string windowName);
    [DllImport("user32.dll")]
    public static extern IntPtr GetDlgItem(IntPtr parent, int controlId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessageText(IntPtr window, uint message, UIntPtr wParam, StringBuilder text);
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

$process = $null
$window = [IntPtr]::Zero
$success = $false
try {
    $exe = [IO.Path]::GetFullPath($ExecutablePath)
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Executable not found: $exe" }
    $path = Join-Path $scratch 'note.md'
    $source = "alpha`nbeta`n"
    [IO.File]::WriteAllText($path, $source, [Text.UTF8Encoding]::new($false))
    $process = Start-Process -FilePath $exe -ArgumentList $path -PassThru `
        -RedirectStandardOutput (Join-Path $scratch 'stdout.txt') `
        -RedirectStandardError (Join-Path $scratch 'stderr.txt')
    Wait-Until {
        $process.Refresh()
        $script:window = $process.MainWindowHandle
        if ($script:window -eq [IntPtr]::Zero) { return $false }
        $title = [Text.StringBuilder]::new(256)
        [void][MoteGuiProbe]::GetWindowText($script:window, $title, $title.Capacity)
        return $title.ToString().Contains('note.md')
    } "Native editor did not show the opened file (exited=$($process.HasExited), window=$window, main=$($process.MainWindowHandle), title=$($process.MainWindowTitle), process_windows=$([string]::Join(',', [MoteGuiProbe]::WindowsForProcess([uint32]$process.Id))))."

    $editor = [MoteGuiProbe]::GetDlgItem($window, 101)
    if ($editor -eq [IntPtr]::Zero) { throw 'Native RichEdit control not found.' }
    [void][MoteGuiProbe]::SendMessage($editor, 0x0102, [UIntPtr][int][char]'X', [IntPtr]::Zero) # WM_CHAR
    Wait-Until {
        $title = [Text.StringBuilder]::new(256)
        [void][MoteGuiProbe]::GetWindowText($window, $title, $title.Capacity)
        return $title.ToString().Contains('•')
    } 'Native edit did not mark the document modified.'

    if (-not [MoteGuiProbe]::PostMessage($window, 0x0111, [UIntPtr]203, [IntPtr]::Zero)) {
        throw 'Could not send the native Save menu command.'
    }
    Wait-Until {
        try {
            $saved = [IO.File]::ReadAllText($path)
            return $saved.Length -eq $source.Length + 1 -and $saved.Replace('X', '') -eq $source
        }
        catch [IO.IOException] { return $false }
    } 'Native Save did not persist exactly one inserted character.'
    $saved = [IO.File]::ReadAllText($path)
    if (-not [MoteGuiProbe]::PostMessage($window, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)) {
        throw 'Could not close the native window.'
    }
    Wait-Until { $process.Refresh(); return $process.HasExited } 'Native process did not exit after close.'
    if ($process.ExitCode -ne 0) { throw "Native editor exited with code $($process.ExitCode)." }

    # Reopen through the published GUI, rather than treating a disk read as a UI reopen.
    $process = Start-Process -FilePath $exe -ArgumentList $path -PassThru `
        -RedirectStandardOutput (Join-Path $scratch 'reopen-stdout.txt') `
        -RedirectStandardError (Join-Path $scratch 'reopen-stderr.txt')
    Wait-Until {
        $process.Refresh()
        $script:window = $process.MainWindowHandle
        if ($script:window -eq [IntPtr]::Zero) { return $false }
        $title = [Text.StringBuilder]::new(256)
        [void][MoteGuiProbe]::GetWindowText($script:window, $title, $title.Capacity)
        return $title.ToString().Contains('note.md')
    } 'Reopened native editor did not show the saved file.'
    $editor = [MoteGuiProbe]::GetDlgItem($window, 101)
    if ($editor -eq [IntPtr]::Zero) { throw 'Reopened RichEdit control not found.' }
    $reopened = [Text.StringBuilder]::new($saved.Length * 2 + 32)
    [void][MoteGuiProbe]::SendMessageText($editor, 0x000D, [UIntPtr]$reopened.Capacity, $reopened) # WM_GETTEXT
    $reopenedText = $reopened.ToString().Replace("`r`n", "`n").Replace("`r", "`n")
    if ($reopenedText -ne $saved) { throw 'Reopened native control does not show the saved text.' }
    if (-not [MoteGuiProbe]::PostMessage($window, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)) {
        throw 'Could not close the reopened native window.'
    }
    Wait-Until { $process.Refresh(); return $process.HasExited } 'Reopened native process did not exit.'
    if ($process.ExitCode -ne 0) { throw "Reopened native editor exited with code $($process.ExitCode)." }
    $success = $true
    [pscustomobject]@{
        result = 'native-windows-open-edit-save-reopen-ok'
        source_chars = $source.Length
        saved_chars = $saved.Length
        reopened_chars = $reopenedText.Length
        exit_code = $process.ExitCode
    } | ConvertTo-Json -Compress
}
finally {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    if (-not $scratch.StartsWith($scratchRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing recursive cleanup outside repository .temp/gui-workflow.'
    }
    if ($success -and (Test-Path -LiteralPath $scratch)) { Remove-Item -LiteralPath $scratch -Recurse -Force }
    elseif (-not $success) { Write-Warning "Retained failed GUI probe under $scratch" }
}
