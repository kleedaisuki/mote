# Exercise a real read-only RichEdit preview keyboard activation and source caret.
# The event crosses the native shell/controller boundary; no test-only product hook.
param(
    [Parameter(Mandatory)][string] $ExecutablePath,
    [string] $ReportPath = '.cache/ci-inventory/native-preview-navigation.json',
    [switch] $LegacyPage,
    [switch] $Pointer
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'This probe requires Windows.' }

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$scratchRoot = [IO.Path]::GetFullPath((Join-Path $root '.temp/preview-navigation'))
$scratch = [IO.Path]::GetFullPath((Join-Path $scratchRoot ([guid]::NewGuid().ToString('N'))))
$report = [IO.Path]::GetFullPath((Join-Path $root $ReportPath))
$reportRoot = [IO.Path]::GetFullPath((Join-Path $root '.cache/ci-inventory'))
if (-not $scratch.StartsWith($scratchRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase) -or
    -not $report.StartsWith($reportRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Probe paths must remain inside repository .temp and .cache/ci-inventory.'
}
New-Item -ItemType Directory -Force -Path $scratch, (Split-Path $report) | Out-Null

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class MotePreviewProbe {
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X; public int Y; }
    [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr parent, int id);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after,
        string className, string windowName);
    [DllImport("user32.dll", EntryPoint="SendMessageTimeoutW", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern IntPtr SendText(IntPtr hwnd, uint msg, UIntPtr w,
        StringBuilder buffer, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll", EntryPoint="SendMessageTimeoutW", SetLastError=true)]
    private static extern IntPtr SendValue(IntPtr hwnd, uint msg, UIntPtr w,
        IntPtr l, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint msg, UIntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr hwnd, ref Point point);
    public static void SetCaret(IntPtr hwnd, int offset) {
        IntPtr result;
        // EM_SETSEL is a system-marshaled scalar message. EM_EXSETSEL is WM_USER+55:
        // passing a pointer to it across processes would dereference foreign memory.
        if (SendValue(hwnd, 0x00B1, (UIntPtr)offset, (IntPtr)offset, 3, 3000,
            out result) == IntPtr.Zero)
            throw new TimeoutException("EM_SETSEL preview caret failed.");
    }
    public static int Caret(IntPtr hwnd) {
        IntPtr result;
        if (SendValue(hwnd, 0x00B0, UIntPtr.Zero, IntPtr.Zero, 3, 3000,
            out result) == IntPtr.Zero)
            throw new TimeoutException("EM_GETSEL source caret failed.");
        var packed = result.ToInt64();
        var start = (int)(packed & 0xFFFF);
        var end = (int)((packed >> 16) & 0xFFFF);
        if (start != end) throw new InvalidOperationException("Source selection was not collapsed.");
        return start;
    }
    public static string Text(IntPtr hwnd) {
        var buffer = new StringBuilder(32768); IntPtr result;
        if (SendText(hwnd, 0x000D, (UIntPtr)buffer.Capacity, buffer, 3, 3000, out result) == IntPtr.Zero)
            throw new TimeoutException("WM_GETTEXT preview failed.");
        return buffer.ToString();
    }
    public static void Enter(IntPtr hwnd) {
        if (!PostMessage(hwnd, 0x0100, (UIntPtr)13, IntPtr.Zero))
            throw new InvalidOperationException("WM_KEYDOWN preview activation could not be queued.");
    }
    public static void Click(IntPtr hwnd, int screenX, int screenY) {
        var point = new Point { X = screenX, Y = screenY };
        if (!ScreenToClient(hwnd, ref point))
            throw new InvalidOperationException("Preview hit-test coordinate conversion failed.");
        var packed = (IntPtr)((point.X & 0xFFFF) | ((point.Y & 0xFFFF) << 16));
        if (!PostMessage(hwnd, 0x0201, (UIntPtr)1, packed) ||
            !PostMessage(hwnd, 0x0202, UIntPtr.Zero, packed))
            throw new InvalidOperationException("Preview pointer messages could not be queued.");
    }
}
'@

function Wait-Until {
    param([scriptblock] $Condition, [string] $Failure)
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.ElapsedMilliseconds -lt 15000) {
        if (& $Condition) { return }
        Start-Sleep -Milliseconds 25
    }
    $previewText = if ($script:preview -ne [IntPtr]::Zero) {
        [MotePreviewProbe]::Text($script:preview)
    } else { '<none>' }
    throw "$Failure preview='$previewText' window='$([MotePreviewProbe]::Text($script:window))'"
}

$oldHome = $env:MOTE_HOME
$process = $null
try {
    $exe = [IO.Path]::GetFullPath($ExecutablePath)
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Missing executable: $exe" }
    $source = "# First`n`n## Destination`n"
    $path = Join-Path $scratch 'preview.md'
    [IO.File]::WriteAllText($path, $source, [Text.UTF8Encoding]::new($false))
    $env:MOTE_HOME = Join-Path $scratch '.mote'
    $args = if ($LegacyPage) { @('--legacy-page', ('"' + $path + '"')) }
        else { @(('"' + $path + '"')) }
    $process = Start-Process -FilePath $exe -ArgumentList $args -PassThru `
        -RedirectStandardOutput (Join-Path $scratch 'stdout.txt') `
        -RedirectStandardError (Join-Path $scratch 'stderr.txt')
    $window = [IntPtr]::Zero
    $editor = [IntPtr]::Zero
    $preview = [IntPtr]::Zero
    Wait-Until {
        $process.Refresh()
        if ($process.HasExited) { throw "Editor exited early: $($process.ExitCode)" }
        $script:window = $process.MainWindowHandle
        if ($script:window -eq [IntPtr]::Zero) { return $false }
        if ($LegacyPage) {
            $script:editor = [MotePreviewProbe]::GetDlgItem($script:window, 101)
        }
        else {
            $canvas = [MotePreviewProbe]::FindWindowEx($script:window,
                [IntPtr]::Zero, 'MoteInteractiveCanvas', $null)
            $script:editor = if ($canvas -eq [IntPtr]::Zero) { [IntPtr]::Zero }
                else { [MotePreviewProbe]::FindWindowEx($canvas,
                    [IntPtr]::Zero, 'RICHEDIT50W', $null) }
        }
        $script:preview = [MotePreviewProbe]::GetDlgItem($script:window, 102)
        return $script:editor -ne [IntPtr]::Zero -and $script:preview -ne [IntPtr]::Zero
    } 'Native source/preview controls did not appear.'
    Wait-Until { [MotePreviewProbe]::Text($preview).Contains('Destination') } `
        'Semantic preview did not publish Destination.'
    $rendered = [MotePreviewProbe]::Text($preview)
    $displayOffset = $rendered.IndexOf('Destination', [StringComparison]::Ordinal)
    $nativeOffset = $displayOffset - [regex]::Matches($rendered.Substring(0, $displayOffset), "`r`n").Count
    $before = [MotePreviewProbe]::Caret($editor)
    if (-not [MotePreviewProbe]::IsWindow($preview)) {
        throw "Preview HWND vanished before activation: preview=$preview editor=$editor window=$window"
    }
    if ($Pointer) {
        Add-Type -AssemblyName UIAutomationClient
        Add-Type -AssemblyName UIAutomationTypes
        $element = [System.Windows.Automation.AutomationElement]::FromHandle($preview)
        $pattern = [System.Windows.Automation.TextPattern]$element.GetCurrentPattern(
            [System.Windows.Automation.TextPattern]::Pattern)
        $hit = $pattern.DocumentRange.FindText('Destination', $false, $false)
        if ($null -eq $hit) { throw 'UIA could not locate preview hit-test range.' }
        $rectangles = $hit.GetBoundingRectangles()
        if ($rectangles.Length -eq 0) { throw 'UIA did not expose preview glyph bounds.' }
        $rect = $rectangles[0]
        [MotePreviewProbe]::Click($preview,
            [int][Math]::Round($rect.X + [Math]::Min(8, $rect.Width / 2)),
            [int][Math]::Round($rect.Y + $rect.Height / 2))
    }
    else {
        [MotePreviewProbe]::SetCaret($preview, $nativeOffset)
        [MotePreviewProbe]::Enter($preview)
    }
    $target = $source.IndexOf('## Destination', [StringComparison]::Ordinal)
    Wait-Until {
        if ($LegacyPage) { return [MotePreviewProbe]::Caret($editor) -eq $target }
        return [MotePreviewProbe]::Text($editor).Contains('Destination')
    } "Preview activation did not reveal source item $target."
    $after = [MotePreviewProbe]::Caret($editor)
    $islandText = [MotePreviewProbe]::Text($editor)
    if ($LegacyPage -and $after -ne $target) { throw 'Legacy source caret differs from mapped target.' }
    if (-not $LegacyPage -and -not $islandText.Contains('Destination')) {
        throw 'Continuous source input island did not rebase to the destination.'
    }
    if ([IO.File]::ReadAllText($path) -cne $source) {
        throw 'Preview navigation unexpectedly modified the source file.'
    }
    if (-not [MotePreviewProbe]::PostMessage($window, 0x0111, [UIntPtr]203,
        [IntPtr]::Zero)) { throw 'Native Save command could not be queued.' }
    Start-Sleep -Milliseconds 150
    if (-not [MotePreviewProbe]::PostMessage($window, 0x0111, [UIntPtr]206,
        [IntPtr]::Zero)) { throw 'Native Undo command could not be queued.' }
    Start-Sleep -Milliseconds 150
    if ([IO.File]::ReadAllText($path) -cne $source -or
        [MotePreviewProbe]::Text($window).Contains('•')) {
        throw 'Save/Undo after preview activation changed source or marked it dirty.'
    }
    $result = [pscustomobject]@{
        passed = $true
        profile = if ($LegacyPage) { 'legacy-page' } else { 'continuous' }
        gesture = if ($Pointer) { 'pointer' } else { 'keyboard' }
        preview_offset = $displayOffset
        source_caret_before = $before
        source_caret_after = $after
        input_island_text = $islandText
        source_length = $source.Length
    }
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $report -Encoding utf8
    $result | ConvertTo-Json -Compress
}
finally {
    if ($process -and -not $process.HasExited) {
        [void][MotePreviewProbe]::PostMessage($process.MainWindowHandle, 0x0010,
            [UIntPtr]::Zero, [IntPtr]::Zero)
        if (-not $process.WaitForExit(5000)) { Stop-Process -Id $process.Id -Force }
    }
    $env:MOTE_HOME = $oldHome
}
