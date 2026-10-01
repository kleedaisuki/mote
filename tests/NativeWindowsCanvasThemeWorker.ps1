# GUI-only worker (same account, not an OS privilege boundary): never writes registry/config or launches/closes the editor.
# The independent owner bounds this entire process, including blocking UIA/PrintWindow.
param(
    [Parameter(Mandatory)][ValidateSet('win-x64','win-arm64')][string] $RuntimeIdentifier,
    [Parameter(Mandatory)][int] $TargetProcessId,
    [Parameter(Mandatory)][string] $RunId,
    [Parameter(Mandatory)][ValidateSet('dark-before','light','dark-after')][string] $Phase
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or $env:GITHUB_ACTIONS -cne 'true' -or
    $env:RUNNER_OS -cne 'Windows' -or $env:RUNNER_ENVIRONMENT -cne 'github-hosted') {
    throw 'Canvas theme worker requires a disposable hosted Windows runner.'
}
if ($RunId -cnotmatch '^[0-9a-f]{32}$' -or $TargetProcessId -le 0) { throw 'Invalid worker identity.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$scratch = Join-Path $root ".temp/native-canvas-theme/$RunId"
$output = Join-Path $root ".cache/native-canvas-theme/$RuntimeIdentifier/$RunId"
$reportPath = Join-Path $output "$Phase.json"
$exe = Join-Path $root "src/Mote.Native/bin/Release/net10.0/$RuntimeIdentifier/publish/mote.exe"
$process = [Diagnostics.Process]::GetProcessById($TargetProcessId)
if ([IO.Path]::GetFullPath($process.MainModule.FileName) -cne [IO.Path]::GetFullPath($exe)) {
    throw 'Worker target is not the exact repository published executable.'
}
Add-Type -AssemblyName System.Drawing.Common
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

/// <summary>Bounded native measurements for the published Windows editor.</summary>
public static class MoteCanvasThemeProbeNative {
    /// <summary>Win32 rectangle in physical screen or client coordinates.</summary>
    [StructLayout(LayoutKind.Sequential)] public struct Rect {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }
    /// <summary>Finds one top-level editor window owned by the launched process.</summary>
    public static IntPtr FindEditor(uint processId) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((window, data) => {
            GetWindowThreadProcessId(window, out uint owner);
            if (owner != processId) return true;
            var name = new StringBuilder(128);
            GetClassName(window, name, name.Capacity);
            if (name.ToString() != "MoteNativeEditorWindow") return true;
            found = window;
            return false;
        }, IntPtr.Zero);
        return found;
    }
    /// <summary>Reads one visible control label without exposing document text.</summary>
    public static string Label(IntPtr window) {
        var text = new StringBuilder(256);
        GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }
    /// <summary>Reads a bounded class name, never a window caption or document value.</summary>
    public static string ClassName(IntPtr window) {
        var name = new StringBuilder(128);
        GetClassName(window, name, name.Capacity);
        return name.ToString();
    }
    /// <summary>Verifies the exact target process before any observation or message.</summary>
    public static void AssertOwner(IntPtr window, uint processId) {
        GetWindowThreadProcessId(window, out uint owner);
        if (window == IntPtr.Zero || owner != processId)
            throw new InvalidOperationException("Target HWND is absent or has a foreign owner.");
    }
    /// <summary>Gets only a direct child of an already owner-verified HWND.</summary>
    public static IntPtr Child(IntPtr parent, string name) => FindWindowEx(parent, IntPtr.Zero, name, null);
    /// <summary>Notifies only the launched target; never broadcasts to other applications.</summary>
    public static bool NotifyAppearance(IntPtr window) {
        var name = Marshal.StringToHGlobalUni("ImmersiveColorSet");
        try { return SendMessageTimeout(window, 0x001A, IntPtr.Zero, name, 3, 1000, out _) != IntPtr.Zero; }
        finally { Marshal.FreeHGlobal(name); }
    }
    /// <summary>Detects the direct Canvas host without traversing unrelated windows.</summary>
    public static bool HasCanvasHost(IntPtr window) =>
        FindWindowEx(window, IntPtr.Zero, "MoteInteractiveCanvas", null) != IntPtr.Zero;
    /// <summary>Reads bounded child-control text through OS-marshaled WM_GETTEXT.</summary>
    public static string EditorText(IntPtr editor) {
        var text = new StringBuilder(256);
        if (SendMessageText(editor, 0x000D, (UIntPtr)text.Capacity, text, 3, 1000, out _) == IntPtr.Zero)
            throw new TimeoutException("Owned bounded WM_GETTEXT failed.");
        return text.ToString();
    }
    /// <summary>Counts visible first-row glyph pixels near a known plain-text foreground.</summary>
    public static int ForegroundPixelCount(IntPtr editor, uint expected) {
        if (!GetClientRect(editor, out Rect rect) || rect.Width < 150 || rect.Height < 50)
            throw new InvalidOperationException("Editor client rectangle is too small for a text sample.");
        var dc = GetDC(editor);
        if (dc == IntPtr.Zero) throw new InvalidOperationException("Editor DC unavailable.");
        try {
            int count = 0;
            for (int y = 4; y < Math.Min(42, rect.Height); y++)
            for (int x = 8; x < Math.Min(150, rect.Width); x++) {
                uint pixel = GetPixel(dc, x, y);
                if (Near(pixel, expected, 24)) count++;
            }
            return count;
        }
        finally { ReleaseDC(editor, dc); }
    }
    private static bool Near(uint actual, uint expected, int tolerance) {
        for (int shift = 0; shift <= 16; shift += 8)
            if (Math.Abs((int)((actual >> shift) & 255) -
                (int)((expected >> shift) & 255)) > tolerance) return false;
        return true;
    }
    /// <summary>Samples a blank editor client pixel after platform repaint.</summary>
    public static uint Background(IntPtr editor) {
        if (!GetClientRect(editor, out Rect rect) || rect.Width < 100 || rect.Height < 100)
            throw new InvalidOperationException("Editor client rectangle is too small for a background sample.");
        var dc = GetDC(editor);
        if (dc == IntPtr.Zero) throw new InvalidOperationException("Editor DC unavailable.");
        try {
            // Sample source body, above the bottom input ribbon; never certify ribbon color as Canvas.
            uint color = GetPixel(dc, rect.Width - 16, 64);
            if (color == 0xFFFFFFFFu) throw new InvalidOperationException("Editor pixel unavailable.");
            return color;
        }
        finally { ReleaseDC(editor, dc); }
    }
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr data);
    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageText(IntPtr window, uint message, UIntPtr capacity, StringBuilder text, uint flags, uint timeoutMs, out IntPtr result);
    [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr parent, int id);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
    /// <summary>Bounds a target-only close notification; never waits indefinitely.</summary>
    public static IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam) {
        if (SendMessageTimeout(window, message, wParam, lParam, 3, 1000, out var result) == IntPtr.Zero)
            throw new TimeoutException("Owned message failed.");
        return result;
    }
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr dc, int x, int y);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeoutMs, out IntPtr result);
}
'@

function Convert-ColorRef([uint32] $Color) {
    return ('#{0:X2}{1:X2}{2:X2}' -f ($Color -band 255), (($Color -shr 8) -band 255), (($Color -shr 16) -band 255))
}

function Convert-HexToColorRef([string] $Color) {
    $red = [Convert]::ToUInt32($Color.Substring(1, 2), 16)
    $green = [Convert]::ToUInt32($Color.Substring(3, 2), 16)
    $blue = [Convert]::ToUInt32($Color.Substring(5, 2), 16)
    return [uint32]($red -bor ($green -shl 8) -bor ($blue -shl 16))
}

function Capture-EditorWindow([IntPtr] $Window, [IntPtr] $Status,
    [string] $Path, [bool] $CheckCaption) {
    $rect = [MoteCanvasThemeProbeNative+Rect]::new()
    if (-not [MoteCanvasThemeProbeNative]::GetWindowRect($Window, [ref]$rect) -or
        $rect.Width -lt 100 -or $rect.Height -lt 100) {
        throw 'Editor window geometry is unavailable.'
    }
    $statusRect = [MoteCanvasThemeProbeNative+Rect]::new()
    if (-not [MoteCanvasThemeProbeNative]::GetWindowRect($Status, [ref]$statusRect) -or
        $statusRect.Width -lt 100 -or $statusRect.Height -lt 12) {
        throw 'Status HWND geometry is unavailable.'
    }
    # Only sample the target PrintWindow bitmap. Translate the rightmost blank
    # status surface from its physical HWND rectangle into bitmap coordinates.
    $statusX = $statusRect.Left - $rect.Left + $statusRect.Width - 16
    $statusY = $statusRect.Top - $rect.Top + [int][Math]::Floor($statusRect.Height / 2)
    if ($statusX -lt 0 -or $statusX -ge $rect.Width -or
        $statusY -lt 0 -or $statusY -ge $rect.Height) {
        throw 'Status sample escaped the editor bitmap.'
    }
    $captionX = [int][Math]::Floor($rect.Width / 2)
    $captionY = 15
    if ($CheckCaption -and ($captionX -lt 120 -or $captionY -ge $rect.Height)) {
        throw 'Window is too small for a blank title sample.'
    }
    $bitmap = [Drawing.Bitmap]::new($rect.Width, $rect.Height)
    try {
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $dc = $graphics.GetHdc()
            try {
                if (-not [MoteCanvasThemeProbeNative]::PrintWindow($Window, $dc, 2)) {
                    throw 'PrintWindow did not capture the editor.'
                }
            }
            finally { $graphics.ReleaseHdc($dc) }
        }
        finally { $graphics.Dispose() }
        $statusPixel = $bitmap.GetPixel($statusX, $statusY)
        $statusRgb = '#{0:X2}{1:X2}{2:X2}' -f $statusPixel.R, $statusPixel.G, $statusPixel.B
        $captionRgb = $null
        if ($CheckCaption) {
            $captionPixel = $bitmap.GetPixel($captionX, $captionY)
            $captionRgb = '#{0:X2}{1:X2}{2:X2}' -f
                $captionPixel.R, $captionPixel.G, $captionPixel.B
        }
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
        return [pscustomobject]@{
            StatusRgb = $statusRgb; StatusX = $statusX; StatusY = $statusY
            CaptionRgb = $captionRgb
            CaptionX = if ($CheckCaption) { $captionX } else { $null }
            CaptionY = if ($CheckCaption) { $captionY } else { $null }
        }
    }
    finally { $bitmap.Dispose() }
}

function Assert-NearColor([string] $Observed, [string] $Expected, [string] $Label,
    [int] $Tolerance = 20) {
    for ($part = 1; $part -le 5; $part += 2) {
        $actual = [Convert]::ToInt32($Observed.Substring($part, 2), 16)
        $wanted = [Convert]::ToInt32($Expected.Substring($part, 2), 16)
        if ([Math]::Abs($actual - $wanted) -gt $Tolerance) {
            throw "$Label color $Observed differs from expected $Expected."
        }
    }
}

function Wait-Until([scriptblock] $Condition, [string] $Failure, [int] $TimeoutMs = 15000) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.ElapsedMilliseconds -lt $TimeoutMs) {
        if (& $Condition) { return }
        Start-Sleep -Milliseconds 50
    }
    throw $Failure
}

# Checks source-provider coordinates, not the bounded native host's local EM_GETSEL.
function Assert-SourceState($Pattern, $ExpectedRange, [string] $ExpectedText) {
    if ($Pattern.DocumentRange.GetText(256) -cne $ExpectedText) {
        throw 'Theme transition changed the exact source-provider fixture.'
    }
    $selected = @($Pattern.GetSelection())
    if ($selected.Count -ne 1 -or $selected[0].GetText(8) -cne 'lp' -or
        $selected[0].CompareEndpoints([System.Windows.Automation.Text.TextPatternRangeEndpoint]::Start,
            $ExpectedRange, [System.Windows.Automation.Text.TextPatternRangeEndpoint]::Start) -ne 0 -or
        $selected[0].CompareEndpoints([System.Windows.Automation.Text.TextPatternRangeEndpoint]::End,
            $ExpectedRange, [System.Windows.Automation.Text.TextPatternRangeEndpoint]::End) -ne 0) {
        throw 'Theme transition changed the global source selection [1,3).'
    }
}

$report = [ordered]@{
    status='failed'; stage=$Phase; cases=@(); launch_observation=$null; error=$null
    source_version_status='unverified-no-public-external-version-contract'
    draw_callback_status='not-observed'; physical_presentation_status='not-tested'
}
$launchTimer = [Diagnostics.Stopwatch]::StartNew()
try {
    $window = [IntPtr]::Zero
    Wait-Until {
        $process.Refresh()
        if ($process.HasExited) { throw 'Published native editor exited before its window opened.' }
        $script:window = [MoteCanvasThemeProbeNative]::FindEditor([uint32]$TargetProcessId)
        return $script:window -ne [IntPtr]::Zero -and
            [MoteCanvasThemeProbeNative]::Label($script:window).Contains('theme.txt')
    } 'Published native editor did not expose its synthetic file window.'
    $titleReadyElapsedMs = $launchTimer.ElapsedMilliseconds
    [MoteCanvasThemeProbeNative]::AssertOwner($window, [uint32]$TargetProcessId)
    $canvas = [MoteCanvasThemeProbeNative]::Child($window, 'MoteInteractiveCanvas')
    [MoteCanvasThemeProbeNative]::AssertOwner($canvas, [uint32]$TargetProcessId)
    $editor = [MoteCanvasThemeProbeNative]::GetDlgItem($canvas, 301)
    $status = [MoteCanvasThemeProbeNative]::GetDlgItem($window, 103)
    [MoteCanvasThemeProbeNative]::AssertOwner($editor, [uint32]$TargetProcessId)
    [MoteCanvasThemeProbeNative]::AssertOwner($status, [uint32]$TargetProcessId)
    Wait-Until {
        $script:sourceElement = [System.Windows.Automation.AutomationElement]::FromHandle($canvas)
        if ($script:sourceElement.Current.ProcessId -ne $TargetProcessId -or
            $script:sourceElement.Current.AutomationId -cne 'mote.source.document' -or
            $script:sourceElement.Current.ControlType -ne [System.Windows.Automation.ControlType]::Document) { return $false }
        $script:textPattern = $script:sourceElement.GetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern)
        return $script:textPattern.DocumentRange.GetText(256) -ceq "alpha`nbeta`n" -and
            [MoteCanvasThemeProbeNative]::IsWindowVisible($editor) -and
            [MoteCanvasThemeProbeNative]::IsWindowVisible($canvas)
    } 'Ordinary Canvas did not expose target-owned source Document and visible input island.'
    $sourceText = $textPattern.DocumentRange.GetText(256)
    $sourceRange = $textPattern.DocumentRange.Clone()
    $startEndpoint = [System.Windows.Automation.Text.TextPatternRangeEndpoint]::Start
    $endEndpoint = [System.Windows.Automation.Text.TextPatternRangeEndpoint]::End
    $character = [System.Windows.Automation.Text.TextUnit]::Character
    $sourceRange.MoveEndpointByRange($endEndpoint, $sourceRange, $startEndpoint)
    if ($sourceRange.MoveEndpointByUnit($endEndpoint, $character, 3) -ne 3 -or
        $sourceRange.MoveEndpointByUnit($startEndpoint, $character, 1) -ne 1) {
        throw 'Synthetic source range endpoints did not move by exact ASCII characters.'
    }
    if ($Phase -ceq 'dark-before') { $sourceRange.Select() } # Exactly one attempt across all workers.
    Assert-SourceState $textPattern $sourceRange $sourceText
    $report.launch_observation = [ordered]@{
        readiness_phase = 'source-document-ready-exact-UIA-selection'
        title_ready_elapsed_ms = $titleReadyElapsedMs
        source_automation_id = $sourceElement.Current.AutomationId
        source_control_type = $sourceElement.Current.ControlType.ProgrammaticName
        input_child_id = 301
        input_child_class = [MoteCanvasThemeProbeNative]::ClassName($editor)
        input_child_visible = $true
        direct_canvas_host_present = $true
        selection_start = 1; selection_end = 3
        exact_synthetic_lf = $true
    }

    $case = switch ($Phase) {
        'dark-before' { @{ Name='dark-before'; Background='#1F2023'; Foreground='#D8DADF'; Panel='#27292D' } }
        'light' { @{ Name='light'; Background='#FFFFFF'; Foreground='#26282E'; Panel='#F6F7F9' } }
        'dark-after' { @{ Name='dark-after'; Background='#1F2023'; Foreground='#D8DADF'; Panel='#27292D' } }
    }
        [MoteCanvasThemeProbeNative]::AssertOwner($window, [uint32]$TargetProcessId)
        [MoteCanvasThemeProbeNative]::AssertOwner($canvas, [uint32]$TargetProcessId)
        $broadcast = [MoteCanvasThemeProbeNative]::NotifyAppearance($window)
        if (-not $broadcast) { throw 'Target-owned WM_SETTINGCHANGE was not delivered.' }
        Wait-Until {
            $script:background = Convert-ColorRef ([MoteCanvasThemeProbeNative]::Background($canvas))
            $script:textPixelCount = [MoteCanvasThemeProbeNative]::ForegroundPixelCount($canvas,
                (Convert-HexToColorRef $case.Foreground))
            $bgOk = $true
            try { Assert-NearColor $script:background $case.Background 'Editor background' }
            catch { $bgOk = $false }
            return $bgOk -and $script:textPixelCount -ge 5
        } "Native editor did not transition to $($case.Name) palette."
        Assert-SourceState $textPattern $sourceRange $sourceText
        $statusText = [MoteCanvasThemeProbeNative]::EditorText($status)
        if ($statusText.Contains('Theme update unavailable')) {
            throw 'The published adapter reported an unavailable palette.'
        }
        $png = Join-Path $output "$($case.Name).png"
        $checkCaption = [Environment]::OSVersion.Version.Build -ge 22000
        $surface = Capture-EditorWindow $window $status $png $checkCaption
        Assert-NearColor $surface.StatusRgb $case.Panel 'Status chrome background' 1
        if ($checkCaption) {
            Assert-NearColor $surface.CaptionRgb $case.Panel 'Win11 title background' 1
        }
        $report.cases += [ordered]@{
            name = $case.Name; background = $background
            expected_text_foreground = $case.Foreground; text_foreground_pixel_count = $textPixelCount
            status_background = $surface.StatusRgb
            status_sample_x = $surface.StatusX; status_sample_y = $surface.StatusY
            caption_background = $surface.CaptionRgb
            caption_sample_x = $surface.CaptionX; caption_sample_y = $surface.CaptionY
            caption_status = if ($checkCaption) { 'verified-win11-pixel' } else { 'unsupported-os-build' }
            selection_start = 1; selection_end = 3
            source_version_status = $report.source_version_status
            text_utf16_units = $sourceText.Length; status_notice = $false
            png = [IO.Path]::GetFileName($png)
            png_sha256 = (Get-FileHash -LiteralPath $png -Algorithm SHA256).Hash
        }
    
    $report.status = 'passed'
}
catch { $report.error = $_.Exception.GetType().Name + ': ' + $_.Exception.Message }
finally {
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding utf8NoBOM
    $process.Dispose()
}
if ($report.status -cne 'passed') { throw "Canvas GUI worker failed: $($report.error)" }
