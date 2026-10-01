# Qualify one real native-source product task against the published Windows binary.
# Native messages exercise RichEdit; they are not physical keyboard or real IME evidence.
param(
    [Parameter(Mandatory)][string] $ExecutablePath,
    [Parameter(Mandatory)][string] $InputPath,
    [Parameter(Mandatory)][string] $OutputPath,
    [Parameter(Mandatory)][string] $EvidenceDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'The Windows release product workflow requires Windows.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$inputFile = [IO.Path]::GetFullPath($InputPath)
$outputFile = [IO.Path]::GetFullPath($OutputPath)
foreach ($path in @($evidence, $outputFile)) {
    if (-not ($path.StartsWith((Join-Path $root '.cache') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        $path.StartsWith((Join-Path $root '.temp') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase))) {
        throw 'Release workflow artifacts must remain under repository .cache or .temp.'
    }
}
if ($inputFile -eq $outputFile -or (Test-Path -LiteralPath $outputFile)) { throw 'Output must be a distinct new file.' }
New-Item -ItemType Directory -Force -Path $evidence, (Split-Path $outputFile) | Out-Null
$exe = [IO.Path]::GetFullPath($ExecutablePath)
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$original = [IO.File]::ReadAllText($inputFile)
$marker = 'mote-release-original'
$replacement = 'mote-release-edited中'
if ($original.IndexOf($marker, [StringComparison]::Ordinal) -lt 0 -or
    $original.IndexOf($marker, [StringComparison]::Ordinal) -ne $original.LastIndexOf($marker, [StringComparison]::Ordinal)) {
    throw 'The task requires exactly one release marker.'
}
$expected = $original.Replace($marker, $replacement, [StringComparison]::Ordinal)
$beforeHash = (Get-FileHash -LiteralPath $inputFile -Algorithm SHA256).Hash
Copy-Item -LiteralPath $inputFile -Destination $outputFile

if (-not ('MoteReleaseWin32' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
/// <summary>PID-scoped native control messages, without clipboard or global input mutation.</summary>
public static class MoteReleaseWin32 {
    public delegate bool EnumProc(IntPtr window, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] public struct Range { public int Start, End; }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr data);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint id);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr window, StringBuilder text, int size);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr window, StringBuilder text, int size);
    [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr window, int id);
    [DllImport("user32.dll", EntryPoint="SendMessageW")] public static extern IntPtr Send(IntPtr window, uint message, IntPtr first, IntPtr second);
    [DllImport("user32.dll", EntryPoint="SendMessageW", CharSet=CharSet.Unicode)] public static extern IntPtr SendText(IntPtr window, uint message, IntPtr first, string text);
    [DllImport("user32.dll", EntryPoint="SendMessageW", CharSet=CharSet.Unicode)] public static extern IntPtr ReadText(IntPtr window, uint message, IntPtr first, StringBuilder text);
    [DllImport("user32.dll", EntryPoint="SendMessageW")] public static extern IntPtr ReadRange(IntPtr window, uint message, IntPtr first, ref Range range);
    [DllImport("user32.dll", EntryPoint="PostMessageW")] public static extern bool Post(IntPtr window, uint message, IntPtr first, IntPtr second);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    /// <summary>Finds only the child's top-level window of the requested registered class.</summary>
    public static IntPtr Find(uint pid, string name) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((window, data) => {
            GetWindowThreadProcessId(window, out uint owner);
            var buffer = new StringBuilder(256);
            GetClassName(window, buffer, buffer.Capacity);
            if (owner == pid && buffer.ToString() == name) { found = window; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@
}

# Process deadlines protect runner resources; they are not experience latency targets.
function Wait-ReleaseCondition([scriptblock] $Condition, [string] $Failure) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.ElapsedMilliseconds -lt 15000) {
        if (& $Condition) { return }
        if ($script:process -and $script:process.HasExited) { throw "Child exited before $Failure" }
        Start-Sleep -Milliseconds 20
    }
    throw $Failure
}
function Read-ReleaseText([IntPtr] $Control) {
    $text = [Text.StringBuilder]::new(1048576)
    [void][MoteReleaseWin32]::ReadText($Control, 0x000D, [IntPtr]$text.Capacity, $text)
    return $text.ToString().Replace("`r`n", "`n").Replace("`r", "`n")
}
function Read-ReleaseSelection([IntPtr] $Control) {
    $range = [MoteReleaseWin32+Range]::new()
    [void][MoteReleaseWin32]::ReadRange($Control, 0x0434, [IntPtr]::Zero, [ref]$range)
    return $range
}
function Invoke-ReleaseMenu([int] $Id) {
    if (-not [MoteReleaseWin32]::Post($script:window, 0x0111, [IntPtr]$Id, [IntPtr]::Zero)) {
        throw 'Native command dispatch failed.'
    }
}
function Invoke-ReleasePrompt([int] $Menu, [string] $Value) {
    Invoke-ReleaseMenu $Menu
    $script:prompt = [IntPtr]::Zero
    Wait-ReleaseCondition {
        $script:prompt = [MoteReleaseWin32]::Find([uint32]$script:process.Id, 'MoteNativeTextPrompt')
        return $script:prompt -ne [IntPtr]::Zero
    } 'Native prompt did not open.'
    $edit = [MoteReleaseWin32]::GetDlgItem($script:prompt, 301)
    [void][MoteReleaseWin32]::SendText($edit, 0x000C, [IntPtr]::Zero, $Value)
    if (-not [MoteReleaseWin32]::Post($script:prompt, 0x0111, [IntPtr]302, [IntPtr]::Zero)) { throw 'Prompt acceptance failed.' }
}
function Start-ReleaseEditor([string] $Label) {
    $start = [Diagnostics.ProcessStartInfo]::new($exe)
    $start.WorkingDirectory = $root
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment['MOTE_HOME'] = Join-Path $evidence 'home'
    $start.Environment['MOTE_TRACE'] = '1'
    [void]$start.ArgumentList.Add('--native-source')
    [void]$start.ArgumentList.Add($outputFile)
    $script:process = [Diagnostics.Process]::Start($start)
    $script:window = [IntPtr]::Zero
    Wait-ReleaseCondition {
        $script:window = [MoteReleaseWin32]::Find([uint32]$script:process.Id, 'MoteNativeEditorWindow')
        if ($script:window -eq [IntPtr]::Zero) { return $false }
        $title = [Text.StringBuilder]::new(512)
        [void][MoteReleaseWin32]::GetWindowText($script:window, $title, $title.Capacity)
        return $title.ToString().Contains([IO.Path]::GetFileName($outputFile), [StringComparison]::Ordinal)
    } 'The actual source product did not open the file.'
    $script:editor = [MoteReleaseWin32]::GetDlgItem($script:window, 101)
    if ($script:editor -eq [IntPtr]::Zero) { throw 'Actual source RichEdit was not found.' }
}
function Close-ReleaseEditor([string] $Label) {
    if (-not [MoteReleaseWin32]::Post($script:window, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)) { throw 'Normal child close failed.' }
    if (-not $script:process.WaitForExit(15000)) { throw 'The actual product did not close normally.' }
    [IO.File]::WriteAllText((Join-Path $evidence "$Label.stdout.txt"), $script:process.StandardOutput.ReadToEnd(), $utf8)
    [IO.File]::WriteAllText((Join-Path $evidence "$Label.stderr.txt"), $script:process.StandardError.ReadToEnd(), $utf8)
    if ($script:process.ExitCode -ne 0) { throw 'The actual product exited unsuccessfully.' }
    $script:process.Dispose()
    $script:process = $null
}

$process = $null
$stage = 'open'
$capture = $false
$report = [ordered]@{ status = 'failed'; stage = $stage; profile = 'native-source'; screenshot = $false;
    external_native_messages = $true; physical_keyboard = $false; real_ime = $false; screen_reader = $false }
try {
    $normalized = $original.Replace("`r`n", "`n").Replace("`r", "`n")
    $normalizedExpected = $expected.Replace("`r`n", "`n").Replace("`r", "`n")
    Start-ReleaseEditor 'edit'
    Wait-ReleaseCondition { (Read-ReleaseText $editor) -ceq $normalized } 'Exact native source did not open.'
    $stage = 'find'
    $offset = $normalized.IndexOf($marker, [StringComparison]::Ordinal)
    Invoke-ReleasePrompt 213 $marker
    Wait-ReleaseCondition {
        $selection = Read-ReleaseSelection $editor
        return $selection.Start -eq $offset -and $selection.End -eq $offset + $marker.Length
    } 'Whole-document Find did not select the exact marker.'
    $stage = 'replace'
    [void][MoteReleaseWin32]::SendText($editor, 0x00C2, [IntPtr]::Zero, $replacement) # EM_REPLACESEL: no OS clipboard.
    Wait-ReleaseCondition { (Read-ReleaseText $editor) -ceq $normalizedExpected } 'Selected Unicode replacement was not exact.'
    $stage = 'undo'
    Invoke-ReleaseMenu 206
    Wait-ReleaseCondition { (Read-ReleaseText $editor) -ceq $normalized } 'Engine Undo did not restore original text.'
    $stage = 'redo'
    Invoke-ReleaseMenu 207
    Wait-ReleaseCondition { (Read-ReleaseText $editor) -ceq $normalizedExpected } 'Engine Redo did not restore replacement.'
    $stage = 'native-undo'
    [void][MoteReleaseWin32]::Send($editor, 0x00C7, [IntPtr]::Zero, [IntPtr]::Zero) # WM_UNDO routes to canonical history.
    Wait-ReleaseCondition { (Read-ReleaseText $editor) -ceq $normalized } 'Native Undo diverged from canonical history.'
    Invoke-ReleaseMenu 207
    Wait-ReleaseCondition { (Read-ReleaseText $editor) -ceq $normalizedExpected } 'Redo after native Undo failed.'
    $stage = 'dirty-close-cancel'
    if (-not [MoteReleaseWin32]::Post($window, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)) { throw 'Dirty-close dispatch failed.' }
    $script:discardPrompt = [IntPtr]::Zero
    Wait-ReleaseCondition {
        $script:discardPrompt = [MoteReleaseWin32]::Find([uint32]$process.Id, '#32770')
        return $script:discardPrompt -ne [IntPtr]::Zero
    } 'Dirty close did not ask for discard consent.'
    if (-not [MoteReleaseWin32]::Post($script:discardPrompt, 0x0111, [IntPtr]2, [IntPtr]::Zero)) { throw 'Discard cancellation failed.' }
    Wait-ReleaseCondition {
        return [MoteReleaseWin32]::Find([uint32]$process.Id, '#32770') -eq [IntPtr]::Zero -and
            (Read-ReleaseText $editor) -ceq $normalizedExpected
    } 'Cancel did not preserve the actual edited source.'
    $stage = 'goto'
    $lastLine = $normalizedExpected.LastIndexOf("`n", [StringComparison]::Ordinal) + 1
    $lineNumber = @($normalizedExpected.ToCharArray() | Where-Object { $_ -eq "`n" }).Count + 1
    Invoke-ReleasePrompt 215 ([string]$lineNumber)
    Wait-ReleaseCondition {
        $selection = Read-ReleaseSelection $editor
        return $selection.Start -eq $lastLine -and $selection.End -eq $lastLine
    } 'Whole-document Go to Line did not reach the last line.'
    $stage = 'save'
    Invoke-ReleaseMenu 203
    Wait-ReleaseCondition { [IO.File]::ReadAllText($outputFile) -ceq $expected } 'Canonical Save did not persist exact replacement.'
    Wait-ReleaseCondition {
        $title = [Text.StringBuilder]::new(512)
        [void][MoteReleaseWin32]::GetWindowText($window, $title, $title.Capacity)
        return -not $title.ToString().EndsWith(' *', [StringComparison]::Ordinal)
    } 'Save completion did not clear modified chrome.'
    $stage = 'capture'
    Add-Type -AssemblyName System.Drawing.Common
    $rect = [MoteReleaseWin32+Rect]::new()
    if ([MoteReleaseWin32]::GetWindowRect($window, [ref]$rect)) {
        $bitmap = [Drawing.Bitmap]::new($rect.Right - $rect.Left, $rect.Bottom - $rect.Top)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $dc = $graphics.GetHdc()
            try { $capture = [MoteReleaseWin32]::PrintWindow($window, $dc, 2) }
            finally { $graphics.ReleaseHdc($dc) }
            if ($capture) { $bitmap.Save((Join-Path $evidence 'native-product.png'), [Drawing.Imaging.ImageFormat]::Png) }
        }
        finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
    $stage = 'close'
    Close-ReleaseEditor 'edit'
    $stage = 'fresh-gui-reopen'
    Start-ReleaseEditor 'reopen'
    Wait-ReleaseCondition { (Read-ReleaseText $editor) -ceq $normalizedExpected } 'A fresh product GUI did not reopen exact saved text.'
    Close-ReleaseEditor 'reopen'
    if ((Get-FileHash -LiteralPath $inputFile -Algorithm SHA256).Hash -cne $beforeHash) { throw 'Original input changed.' }
    $report.status = 'passed'
    $report.screenshot = $capture
    $report.stage = 'complete'
    [pscustomobject]$report | ConvertTo-Json -Compress
}
finally {
    if ($report.status -ne 'passed') { $report.stage = $stage }
    [IO.File]::WriteAllText((Join-Path $evidence 'windows-product.json'), ([pscustomobject]$report | ConvertTo-Json), $utf8)
    if ($process -and -not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
    if ($process) { $process.Dispose() }
}
