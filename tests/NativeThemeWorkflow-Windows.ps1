# Diagnose live Windows application appearance against one published native executable.
# This script is deliberately restricted to a disposable GitHub-hosted Windows runner:
# it modifies HKCU only inside one try/finally and restores value existence, kind, and data.
param(
    [Parameter(Mandatory)][string] $ExecutablePath,
    [Parameter(Mandatory)][ValidateSet('win-x64', 'win-arm64')][string] $RuntimeIdentifier
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or $env:GITHUB_ACTIONS -cne 'true' -or
    $env:RUNNER_OS -cne 'Windows' -or $env:RUNNER_ENVIRONMENT -cne 'github-hosted') {
    throw 'NativeThemeWorkflow-Windows may change HKCU only on a GitHub-hosted Windows runner.'
}

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$scratchRoot = [IO.Path]::GetFullPath((Join-Path $root '.temp/native-theme'))
$outputRoot = [IO.Path]::GetFullPath((Join-Path $root ".cache/native-theme/$RuntimeIdentifier"))
$inventoryRoot = [IO.Path]::GetFullPath((Join-Path $root ".cache/ci-inventory/$RuntimeIdentifier"))
$runId = [guid]::NewGuid().ToString('N')
$scratch = [IO.Path]::GetFullPath((Join-Path $scratchRoot $runId))
$output = [IO.Path]::GetFullPath((Join-Path $outputRoot $runId))
foreach ($pair in @(@($scratchRoot, $scratch), @($outputRoot, $output))) {
    if (-not $pair[1].StartsWith($pair[0] + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Theme probe path escaped repository-local .temp or .cache.'
    }
}
New-Item -ItemType Directory -Force -Path $scratch, $output, $inventoryRoot | Out-Null
$reportPath = Join-Path $inventoryRoot 'native-theme.json'
$exe = [IO.Path]::GetFullPath($ExecutablePath)
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Published executable is absent.' }

# Read only the HWND/control geometry and RichEdit character format; source text is
# synthetic and stays in .temp. Captured PNGs contain no user file or desktop image.
Add-Type -AssemblyName System.Drawing.Common
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

/// <summary>Bounded native measurements for the published Windows editor.</summary>
public static class MoteThemeProbeNative {
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
    /// <summary>Detects the direct Canvas host without traversing unrelated windows.</summary>
    public static bool HasCanvasHost(IntPtr window) =>
        FindWindowEx(window, IntPtr.Zero, "MoteInteractiveCanvas", null) != IntPtr.Zero;
    /// <summary>Reads bounded child-control text through OS-marshaled WM_GETTEXT.</summary>
    public static string EditorText(IntPtr editor) {
        var text = new StringBuilder(256);
        SendMessageText(editor, 0x000D, (UIntPtr)text.Capacity, text); // WM_GETTEXT is OS-marshaled.
        return text.ToString();
    }
    /// <summary>Reads the exact RichEdit selection independent of an OS focus change.</summary>
    public static (int Start, int End) Selection(IntPtr editor) {
        SendMessageSelection(editor, 0x00B0, out int start, out int end);
        return (start, end);
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
            uint color = GetPixel(dc, rect.Width - 16, rect.Height - 16);
            if (color == 0xFFFFFFFFu) throw new InvalidOperationException("Editor pixel unavailable.");
            return color;
        }
        finally { ReleaseDC(editor, dc); }
    }
    /// <summary>Broadcasts a bounded OS setting-change notification to all top-level HWNDs.</summary>
    public static bool BroadcastAppearance() {
        var name = Marshal.StringToHGlobalUni("ImmersiveColorSet");
        try {
            return SendMessageTimeout(new IntPtr(0xFFFF), 0x001A, IntPtr.Zero,
                name, 0x0002, 1000, out _) != IntPtr.Zero;
        }
        finally { Marshal.FreeHGlobal(name); }
    }
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr data);
    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);
    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageText(IntPtr window, uint message, UIntPtr capacity, StringBuilder text);
    [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr parent, int id);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern IntPtr SendMessageSelection(IntPtr window, uint message, out int start, out int end);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr dc, int x, int y);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeoutMs, out IntPtr result);
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
    $rect = [MoteThemeProbeNative+Rect]::new()
    if (-not [MoteThemeProbeNative]::GetWindowRect($Window, [ref]$rect) -or
        $rect.Width -lt 100 -or $rect.Height -lt 100) {
        throw 'Editor window geometry is unavailable.'
    }
    $statusRect = [MoteThemeProbeNative+Rect]::new()
    if (-not [MoteThemeProbeNative]::GetWindowRect($Status, [ref]$statusRect) -or
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
                if (-not [MoteThemeProbeNative]::PrintWindow($Window, $dc, 2)) {
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

$report = [ordered]@{
    status = 'failed'; stage = 'preflight'; rid = $RuntimeIdentifier
    executable_sha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    registry_original_key_exists = $null; registry_original_value_exists = $null
    registry_original_kind = $null; registry_restored = $false
    source_sha256_unchanged = $false; cases = @(); error = $null
    launch_observation = $null
    scope = 'published Win32 HWND; synthetic HKCU app preference; no IME or physical-present assertion'
    windows_build = [Environment]::OSVersion.Version.Build
}
$process = $null
$registryParent = $null
$registryKey = $null
$keyExisted = $false
$valueExisted = $false
$originalKind = $null
$originalValue = $null
$registryTouched = $false
$oldMoteHome = [Environment]::GetEnvironmentVariable('MOTE_HOME')
$fixture = $null
$sourceHash = $null
try {
    $report.stage = 'registry-snapshot'
    $registryParent = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey(
        'Software\Microsoft\Windows\CurrentVersion\Themes', $true)
    if ($null -eq $registryParent) { throw 'Themes registry parent is absent; refusing to create it.' }
    $registryKey = $registryParent.OpenSubKey('Personalize', $true)
    $keyExisted = $null -ne $registryKey
    if (-not $keyExisted) {
        # Mark before CreateSubKey: even a later fixture/launch failure must
        # remove this newly created key in the same finally block.
        $registryTouched = $true
        $registryKey = $registryParent.CreateSubKey('Personalize', $true)
    }
    if ($null -eq $registryKey) { throw 'Cannot open a writable Personalize registry key.' }
    $valueExisted = $registryKey.GetValueNames() -contains 'AppsUseLightTheme'
    if ($valueExisted) {
        $originalKind = $registryKey.GetValueKind('AppsUseLightTheme')
        if ($originalKind -eq [Microsoft.Win32.RegistryValueKind]::Unknown) {
            throw 'Unknown existing registry value kind; refusing to mutate it.'
        }
        $originalValue = $registryKey.GetValue('AppsUseLightTheme', $null,
            [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
    }
    $report.registry_original_key_exists = $keyExisted
    $report.registry_original_value_exists = $valueExisted
    $report.registry_original_kind = if ($valueExisted) { $originalKind.ToString() } else { $null }

    $report.stage = 'fixtures'
    $moteHome = Join-Path $scratch 'home'
    New-Item -ItemType Directory -Force -Path $moteHome | Out-Null
    [IO.File]::WriteAllText((Join-Path $moteHome 'config.toml'),
        "[appearance]`ntheme = 'system'`n", [Text.UTF8Encoding]::new($false))
    $fixture = Join-Path $scratch 'theme.txt'
    [IO.File]::WriteAllText($fixture, "alpha`nbeta`n", [Text.UTF8Encoding]::new($false))
    $sourceHash = (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash
    [Environment]::SetEnvironmentVariable('MOTE_HOME', $moteHome)

    $registryTouched = $true
    $registryKey.SetValue('AppsUseLightTheme', 0, [Microsoft.Win32.RegistryValueKind]::DWord)
    [void][MoteThemeProbeNative]::BroadcastAppearance()
    $report.stage = 'launch'
    $launchTimer = [Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath $exe -ArgumentList $fixture -PassThru `
        -RedirectStandardOutput (Join-Path $scratch 'stdout.txt') `
        -RedirectStandardError (Join-Path $scratch 'stderr.txt')
    $window = [IntPtr]::Zero
    Wait-Until {
        $process.Refresh()
        if ($process.HasExited) { throw 'Published native editor exited before its window opened.' }
        $script:window = [MoteThemeProbeNative]::FindEditor([uint32]$process.Id)
        return $script:window -ne [IntPtr]::Zero -and
            [MoteThemeProbeNative]::Label($script:window).Contains('theme.txt')
    } 'Published native editor did not expose its synthetic file window.'
    $titleReadyElapsedMs = $launchTimer.ElapsedMilliseconds
    $editor = [MoteThemeProbeNative]::GetDlgItem($window, 101)
    $status = [MoteThemeProbeNative]::GetDlgItem($window, 103)
    if ($editor -eq [IntPtr]::Zero -or $status -eq [IntPtr]::Zero) {
        throw 'Published editor or status HWND is absent.'
    }
    [void][MoteThemeProbeNative]::SendMessage($editor, 0x00B1,
        [IntPtr]1, [IntPtr]3) # EM_SETSEL selects two synthetic source characters.
    $sourceText = [MoteThemeProbeNative]::EditorText($editor)
    $selection = [MoteThemeProbeNative]::Selection($editor)
    $selectionReadbackElapsedMs = $launchTimer.ElapsedMilliseconds
    # Preserve the original single selection attempt and immediate readback. Only
    # report metadata after that read; no readiness retry or arbitrary text leaves
    # the process. Hidden ID 101 plus a Canvas sibling discriminates host mode.
    $editorVisible = [MoteThemeProbeNative]::IsWindowVisible($editor)
    $canvasPresent = [MoteThemeProbeNative]::HasCanvasHost($window)
    $report.launch_observation = [ordered]@{
        readiness_phase = 'title-ready-controls-found-single-selection-readback'
        title_ready_elapsed_ms = $titleReadyElapsedMs
        selection_readback_elapsed_ms = $selectionReadbackElapsedMs
        selection_start = $selection.Item1; selection_end = $selection.Item2
        expected_selection_start = 1; expected_selection_end = 3
        editor_child_id = 101; editor_child_class = [MoteThemeProbeNative]::ClassName($editor)
        editor_child_visible = $editorVisible; direct_canvas_host_present = $canvasPresent
        host_mode_observation = if ($canvasPresent -and -not $editorVisible) {
            'canvas-with-hidden-legacy-editor'
        } elseif ($editorVisible -and -not $canvasPresent) {
            'visible-legacy-editor'
        } else { 'unclassified' }
        text_read_capacity_utf16_units = 256; bounded_text_utf16_units = $sourceText.Length
        text_read_at_capacity = $sourceText.Length -eq 255
        exact_synthetic_lf = $sourceText -ceq "alpha`nbeta`n"
        exact_synthetic_crlf = $sourceText -ceq "alpha`r`nbeta`r`n"
        exact_synthetic_cr = $sourceText -ceq "alpha`rbeta`r"
    }
    if ($selection.Item1 -ne 1 -or $selection.Item2 -ne 3) { throw 'Synthetic RichEdit selection was not established.' }

    foreach ($case in @(
        @{ Name = 'dark-before'; Value = 0; Background = '#1F2023'; Foreground = '#D8DADF'; Panel = '#27292D' },
        @{ Name = 'light'; Value = 1; Background = '#FFFFFF'; Foreground = '#26282E'; Panel = '#F6F7F9' },
        @{ Name = 'dark-after'; Value = 0; Background = '#1F2023'; Foreground = '#D8DADF'; Panel = '#27292D' }
    )) {
        $report.stage = $case.Name
        $registryKey.SetValue('AppsUseLightTheme', [int]$case.Value,
            [Microsoft.Win32.RegistryValueKind]::DWord)
        $broadcast = [MoteThemeProbeNative]::BroadcastAppearance()
        if (-not $broadcast) { throw 'Bounded WM_SETTINGCHANGE broadcast was not delivered.' }
        Wait-Until {
            $script:background = Convert-ColorRef ([MoteThemeProbeNative]::Background($editor))
            $script:textPixelCount = [MoteThemeProbeNative]::ForegroundPixelCount($editor,
                (Convert-HexToColorRef $case.Foreground))
            $bgOk = $true
            try { Assert-NearColor $script:background $case.Background 'Editor background' }
            catch { $bgOk = $false }
            return $bgOk -and $script:textPixelCount -ge 5
        } "Native editor did not transition to $($case.Name) palette."
        $currentSelection = [MoteThemeProbeNative]::Selection($editor)
        if ($currentSelection.Item1 -ne $selection.Item1 -or
            $currentSelection.Item2 -ne $selection.Item2 -or
            [MoteThemeProbeNative]::EditorText($editor) -cne $sourceText) {
            throw 'A theme transition changed native text or the source selection.'
        }
        $statusText = [MoteThemeProbeNative]::EditorText($status)
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
            selection_start = $currentSelection.Item1; selection_end = $currentSelection.Item2
            text_utf16_units = $sourceText.Length; status_notice = $false
            png = [IO.Path]::GetFileName($png)
            png_sha256 = (Get-FileHash -LiteralPath $png -Algorithm SHA256).Hash
        }
    }
    if ($report.cases[0].png_sha256 -ceq $report.cases[1].png_sha256 -or
        $report.cases[1].png_sha256 -ceq $report.cases[2].png_sha256) {
        throw 'Dark and light window rasters are identical.'
    }
    $report.source_sha256_unchanged =
        (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash -ceq $sourceHash
    if (-not $report.source_sha256_unchanged) { throw 'Theme probe modified the synthetic input file.' }
    $report.status = 'passed'
}
catch {
    $report.error = "$($report.stage): $($_.Exception.GetType().Name): $($_.Exception.Message)"
}
finally {
    if ($null -ne $fixture -and $null -ne $sourceHash -and
        (Test-Path -LiteralPath $fixture -PathType Leaf)) {
        $report.source_sha256_unchanged =
            (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash -ceq $sourceHash
    }
    if ($null -ne $process) {
        try {
            if (-not $process.HasExited) {
                [void][MoteThemeProbeNative]::SendMessage($window, 0x0010,
                    [IntPtr]::Zero, [IntPtr]::Zero) # WM_CLOSE.
                if (-not $process.WaitForExit(3000)) { $process.Kill($true) }
            }
            $process.Dispose()
        }
        catch { if ($report.error -eq $null) { $report.error = 'Native process cleanup failed.' } }
    }
    [Environment]::SetEnvironmentVariable('MOTE_HOME', $oldMoteHome)
    try {
        if ($registryTouched -and $null -eq $registryKey -and $null -ne $registryParent) {
            $registryKey = $registryParent.OpenSubKey('Personalize', $true)
        }
        if ($registryTouched -and $null -ne $registryKey) {
            if ($valueExisted) {
                $registryKey.SetValue('AppsUseLightTheme', $originalValue, $originalKind)
                $restored = $registryKey.GetValueKind('AppsUseLightTheme') -eq $originalKind -and
                    [Collections.StructuralComparisons]::StructuralEqualityComparer.Equals(
                        $registryKey.GetValue('AppsUseLightTheme', $null,
                            [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames),
                        $originalValue)
                if (-not $restored) { throw 'Original registry value could not be verified after restoration.' }
            }
            else {
                $registryKey.DeleteValue('AppsUseLightTheme', $false)
                if ($registryKey.GetValueNames() -contains 'AppsUseLightTheme') {
                    throw 'New registry value remained after cleanup.'
                }
            }
            $registryKey.Dispose()
            $registryKey = $null
            if (-not $keyExisted) {
                $check = $registryParent.OpenSubKey('Personalize', $true)
                if ($null -ne $check) {
                    $canDelete = $check.GetValueNames().Length -eq 0 -and $check.GetSubKeyNames().Length -eq 0
                    $check.Dispose()
                    if (-not $canDelete) { throw 'Created Personalize key acquired other data; refusing to delete it.' }
                    $registryParent.DeleteSubKey('Personalize', $false)
                }
                $remaining = $registryParent.OpenSubKey('Personalize')
                if ($null -ne $remaining) {
                    $remaining.Dispose()
                    throw 'Created Personalize key remained after cleanup.'
                }
            }
            $report.registry_restored = $true
        }
        elseif ($registryTouched -and -not $keyExisted -and $null -ne $registryParent) {
            # CreateSubKey itself may have failed before returning a handle.
            $remaining = $registryParent.OpenSubKey('Personalize')
            $report.registry_restored = $null -eq $remaining
            if ($null -ne $remaining) { $remaining.Dispose() }
        }
        elseif (-not $registryTouched) { $report.registry_restored = $true }
    }
    catch {
        $report.registry_restored = $false
        $report.error = "registry-restore: $($_.Exception.GetType().Name): $($_.Exception.Message)"
    }
    if ($null -ne $registryKey) { $registryKey.Dispose() }
    if ($null -ne $registryParent) { $registryParent.Dispose() }
    if (-not $report.registry_restored) { $report.status = 'failed' }
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding utf8NoBOM
    Write-Host ($report | ConvertTo-Json -Depth 8 -Compress)
}
if ($report.status -ne 'passed') { throw "Native Windows theme workflow failed: $($report.error)" }
