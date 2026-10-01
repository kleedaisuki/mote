# Exercise source-bound horizontal navigation in a published, lone Windows Native AOT editor.
# The fixture and evidence stay in repository .temp/.cache; clipboard contents are never logged.
param(
    [Parameter(Mandatory)][string] $ExecutablePath,
    [switch] $OrdinaryProduct
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'The Windows horizontal workflow requires Windows.' }

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$suffix = if ($OrdinaryProduct) { 'windows-continuous-horizontal' } else { 'windows-horizontal' }
$tempRoot = [IO.Path]::GetFullPath((Join-Path $root ".temp/$suffix"))
$cacheRoot = [IO.Path]::GetFullPath((Join-Path $root ".cache/$suffix"))
$run = [IO.Path]::GetFullPath((Join-Path $tempRoot ([guid]::NewGuid().ToString('N'))))
if (-not $run.StartsWith($tempRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase) -or
    -not $cacheRoot.StartsWith([IO.Path]::GetFullPath((Join-Path $root '.cache')) +
    [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Probe artifacts must remain inside repository .temp and .cache.'
}
New-Item -ItemType Directory -Force -Path $run, $cacheRoot | Out-Null
$exe = [IO.Path]::GetFullPath($ExecutablePath)
$inventory = @(Get-ChildItem -LiteralPath (Split-Path $exe) -File)
if (-not (Test-Path -LiteralPath $exe -PathType Leaf) -or $inventory.Count -ne 1 -or
    $inventory[0].FullName -cne $exe) { throw 'A lone published executable is required.' }

Add-Type -AssemblyName System.Drawing.Common
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
public static class MoteHorizontalWin32 {
    public delegate bool EnumWindowsProc(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr window, StringBuilder text, int capacity);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string className, string title);
    [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr parent, int id);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint="SendMessageW", CharSet=CharSet.Unicode)]
    public static extern IntPtr SendMessageText(IntPtr window, uint message, UIntPtr wParam, StringBuilder text);
    [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] public static extern bool IsChild(IntPtr parent, IntPtr possibleChild);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll", SetLastError=true)] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr window, ref Point point);
    [DllImport("user32.dll")] public static extern bool GetScrollInfo(IntPtr window, int bar, ref ScrollInfo info);
    [DllImport("user32.dll")] public static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] public static extern bool CloseClipboard();
    [DllImport("user32.dll")] public static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")] public static extern IntPtr GetOpenClipboardWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetClipboardOwner();
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetFocus();
    [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr window, uint message, UIntPtr wParam, ref CharRange range);
    [DllImport("kernel32.dll")] public static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")] public static extern bool GlobalUnlock(IntPtr handle);
    public static uint OwnerPid(IntPtr window) {
        uint pid; GetWindowThreadProcessId(window, out pid); return pid;
    }
    public static string ClassName(IntPtr window) {
        var name = new StringBuilder(128); GetClassNameW(window, name, name.Capacity);
        return name.ToString();
    }
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct CharRange { public int Min, Max; }
    [StructLayout(LayoutKind.Sequential)] public struct ScrollInfo {
        public uint Size, Mask; public int Minimum, Maximum; public uint Page; public int Position, TrackPosition;
    }
    public static IntPtr MainWindow(uint id) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((window, data) => {
            uint pid; GetWindowThreadProcessId(window, out pid);
            if (pid != id) return true;
            var name = new StringBuilder(128); GetClassNameW(window, name, name.Capacity);
            if (name.ToString() == "MoteNativeEditorWindow") { found = window; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    public static string Text(IntPtr window) {
        var text = new StringBuilder(32769);
        SendMessageText(window, 0x000D, (UIntPtr)text.Capacity, text);
        return text.ToString();
    }
    public static int TextLength(IntPtr window) => (int)SendMessageW(window, 0x000E, UIntPtr.Zero, IntPtr.Zero);
    public static string InputSelection(IntPtr input) {
        var range = new CharRange();
        SendMessageW(input, 0x0434, UIntPtr.Zero, ref range); // EM_EXGETSEL
        return $"{range.Min},{range.Max}";
    }
    public static string ClipboardStatus() {
        var foreground = GetForegroundWindow();
        uint pid; GetWindowThreadProcessId(foreground, out pid);
        return $"sequence={GetClipboardSequenceNumber()};open={GetOpenClipboardWindow()};owner={GetClipboardOwner()};foreground={foreground};foreground_pid={pid}";
    }
    public static bool TryClipboardUnicode(out string value) {
        value = "";
        if (!OpenClipboard(IntPtr.Zero)) return false;
        try {
            var handle = GetClipboardData(13); // CF_UNICODETEXT
            if (handle == IntPtr.Zero) return false;
            var pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero) return false;
            try { value = Marshal.PtrToStringUni(pointer) ?? ""; return value.Length > 0; }
            finally { GlobalUnlock(handle); }
        }
        finally { CloseClipboard(); }
    }
    public static int HPos(IntPtr window) {
        var info = new ScrollInfo { Size = (uint)Marshal.SizeOf<ScrollInfo>(), Mask = 0x4 };
        if (!GetScrollInfo(window, 0, ref info)) throw new InvalidOperationException("GetScrollInfo(horizontal) failed.");
        return info.Position;
    }
    public static int VPos(IntPtr window) {
        var info = new ScrollInfo { Size = (uint)Marshal.SizeOf<ScrollInfo>(), Mask = 0x4 };
        if (!GetScrollInfo(window, 1, ref info)) throw new InvalidOperationException("GetScrollInfo(vertical) failed.");
        return info.Position;
    }
    public static void CreateFixture(string path, int lineLength, string tail) {
        var block = new byte[1024 * 1024]; Array.Fill(block, (byte)'a');
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var remaining = lineLength;
        while (remaining > 0) { var count = Math.Min(block.Length, remaining); output.Write(block, 0, count); remaining -= count; }
        foreach (var item in new (int Offset, string Marker)[] {
            (4096, "MOTE_INITIAL_4K_Q7"),
            (25 * 1024 * 1024 + 113, "MOTE_MIDDLE_25M_R8"),
            (lineLength - tail.Length - 128, "MOTE_FAR_END_S9") }) {
            output.Position = item.Offset; output.Write(Encoding.ASCII.GetBytes(item.Marker));
        }
        output.Position = lineLength - tail.Length;
        output.Write(Encoding.ASCII.GetBytes(tail));
        output.Position = lineLength;
        for (var i = 0; i < 160; i++) output.Write(Encoding.ASCII.GetBytes($"\nrow-{i:D3}-vertical-probe"));
    }
    public static void AssertPatched(string baseline, string saved, long offset, int replaced, byte inserted) {
        using var source = new FileStream(baseline, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var actual = new FileStream(saved, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (actual.Length != source.Length - replaced + 1) throw new InvalidDataException("Saved length differs from one exact replacement.");
        var a = new byte[1024 * 1024]; var b = new byte[a.Length];
        void Equal(long count) {
            while (count > 0) {
                var n = (int)Math.Min(a.Length, count);
                source.ReadExactly(a.AsSpan(0, n)); actual.ReadExactly(b.AsSpan(0, n));
                if (!a.AsSpan(0, n).SequenceEqual(b.AsSpan(0, n))) throw new InvalidDataException("Saved source bytes differ outside selected interval.");
                count -= n;
            }
        }
        Equal(offset);
        if (actual.ReadByte() != inserted) throw new InvalidDataException("Replacement byte missing.");
        source.Position += replaced;
        Equal(source.Length - source.Position);
    }
}
'@

function Wait-Until {
    param([scriptblock] $Condition, [string] $Failure, [int] $TimeoutMs = 60000)
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.ElapsedMilliseconds -lt $TimeoutMs) {
        if (& $Condition) { return }
        Start-Sleep -Milliseconds 50
    }
    throw $Failure
}

function Capture-Canvas {
    param([IntPtr] $Canvas, [string] $Path)
    # Keep PMv2 limited to this capture. Cross-awareness synthetic mouse messages
    # must retain the editor's logical coordinate convention outside this scope.
    $previousDpiContext = [MoteHorizontalWin32]::SetThreadDpiAwarenessContext([IntPtr](-4))
    if ($previousDpiContext -eq [IntPtr]::Zero) {
        throw "Could not establish physical-pixel HWND coordinates (Win32 error $([Runtime.InteropServices.Marshal]::GetLastWin32Error()))."
    }
    try {
    $rect = [MoteHorizontalWin32+Rect]::new()
    $windowRect = [MoteHorizontalWin32+Rect]::new()
    $point = [MoteHorizontalWin32+Point]::new()
    if (-not [MoteHorizontalWin32]::GetClientRect($Canvas, [ref]$rect) -or
        -not [MoteHorizontalWin32]::GetWindowRect($Canvas, [ref]$windowRect) -or
        -not [MoteHorizontalWin32]::ClientToScreen($Canvas, [ref]$point)) {
        throw 'Could not determine canvas screen coordinates.'
    }
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -lt 300 -or $height -lt 120) { throw "Canvas too small for visual verification ($width x $height)." }
    $center = [MoteHorizontalWin32+Point]::new()
    $center.X = $windowRect.Left + [int]($width / 2)
    $center.Y = $windowRect.Top + [int]($height / 2)
    $topWindow = [MoteHorizontalWin32]::WindowFromPoint($center)
    if ($topWindow -ne $Canvas -and -not [MoteHorizontalWin32]::IsChild($Canvas, $topWindow)) {
        $foreground = [MoteHorizontalWin32]::GetForegroundWindow()
        $report.capture_obstruction = [ordered]@{
            expected_editor_pid = $process.Id
            hit_window = $topWindow.ToInt64()
            hit_owner_pid = [MoteHorizontalWin32]::OwnerPid($topWindow)
            hit_class = [MoteHorizontalWin32]::ClassName($topWindow)
            foreground_pid = [MoteHorizontalWin32]::OwnerPid($foreground)
        }
        # Only a nonzero, independently identified foreign foreground owner
        # establishes environmental occlusion. Same-PID/PID-0 geometry remains
        # a real failed Canvas observation, never a softened product verdict.
        if ($report.capture_obstruction.hit_owner_pid -gt 0 -and
            $report.capture_obstruction.hit_owner_pid -ne $process.Id -and
            $report.capture_obstruction.foreground_pid -eq
                $report.capture_obstruction.hit_owner_pid) {
            $report.status = 'inconclusive'
        }
        throw 'Canvas is obscured at its center; desktop pixels cannot prove canvas rendering.'
    }
    $bitmap = [Drawing.Bitmap]::new($width, $height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        # PMv2 Win32 geometry and CopyFromScreen now share physical pixels.
        $graphics.CopyFromScreen($windowRect.Left, $windowRect.Top, 0, 0, $bitmap.Size)
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
    return [ordered]@{
        width = $width; height = $height
        capture_system_dpi = [MoteHorizontalWin32]::GetDpiForSystem()
        screen_x = $windowRect.Left; screen_y = $windowRect.Top
        client_to_screen_x = $point.X; client_to_screen_y = $point.Y
        sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    }
    }
    finally { [void][MoteHorizontalWin32]::SetThreadDpiAwarenessContext($previousDpiContext) }
}

function Measure-PixelDifference {
    param([string] $Before, [string] $After)
    $a = [Drawing.Bitmap]::new($Before)
    $b = [Drawing.Bitmap]::new($After)
    try {
        if ($a.Width -ne $b.Width -or $a.Height -ne $b.Height) {
            throw 'Canvas dimensions changed during fixed-window pixel comparison.'
        }
        $different = 0
        $sampled = 0
        for ($y = 2; $y -lt $a.Height - 2; $y += 2) {
            for ($x = 2; $x -lt $a.Width - 2; $x += 4) {
                $sampled++
                if ($a.GetPixel($x, $y).ToArgb() -ne $b.GetPixel($x, $y).ToArgb()) {
                    $different++
                }
            }
        }
        return [ordered]@{ different_samples = $different; total_samples = $sampled }
    }
    finally { $a.Dispose(); $b.Dispose() }
}

function Send-Mouse {
    param([IntPtr] $Canvas, [uint32] $Message, [int] $X, [int] $Y, [uint32] $Buttons = 0)
    $position = [IntPtr]([int](($Y -band 0xFFFF) -shl 16 -bor ($X -band 0xFFFF)))
    [void][MoteHorizontalWin32]::SendMessageW($Canvas, $Message, [UIntPtr]$Buttons, $position)
}

$lineLength = 50 * 1024 * 1024
$tailLength = 8192
$random = [Random]::new(20260929)
$alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789'
$chars = [char[]]::new($tailLength)
for ($i = 0; $i -lt $chars.Length; $i++) { $chars[$i] = $alphabet[$random.Next($alphabet.Length)] }
$tail = [string]::new($chars)
$baseline = Join-Path $run 'baseline.txt'
$file = Join-Path $run 'horizontal.txt'
[MoteHorizontalWin32]::CreateFixture($baseline, $lineLength, $tail)
[IO.File]::Copy($baseline, $file)
$originalHash = (Get-FileHash -LiteralPath $baseline -Algorithm SHA256).Hash
$originalClipboard = $null
$clipboardCaptured = $false
$process = $null
$reopen = $null
$phase = 'launch'
$success = $false
$report = [ordered]@{
    status = 'failed'
    phase = $phase
    route = if ($OrdinaryProduct) { 'ordinary-product-Continuous' }
        else { 'canvas-diagnostic' }
    os = [Environment]::OSVersion.VersionString
    exe_sha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    file_utf16_line_units = $lineLength
    baseline_sha256 = $originalHash
    artifacts = $run
    error = ''
    clipboard_restored = $false
    capture_obstruction = $null
}
try {
    $originalClipboard = [Windows.Forms.Clipboard]::GetDataObject()
    $clipboardCaptured = $true
    $start = [Diagnostics.ProcessStartInfo]::new($exe)
    $start.WorkingDirectory = $root
    if ($OrdinaryProduct) {
        $start.Environment['MOTE_HOME'] = Join-Path $run 'home'
        # Preserve this historical Continuous workflow rather than relabeling
        # it as qualification of the new full-native release default.
        [void]$start.ArgumentList.Add('--continuous')
    }
    else {
        [void]$start.ArgumentList.Add('--canvas-experimental')
    }
    [void]$start.ArgumentList.Add($file)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    if ($null -eq $process) { throw 'Published executable did not start.' }
    $main = [IntPtr]::Zero
    Wait-Until {
        $process.Refresh()
        if ($process.HasExited) { throw "Editor exited early ($($process.ExitCode))." }
        $script:main = [MoteHorizontalWin32]::MainWindow([uint32]$process.Id)
        return $script:main -ne [IntPtr]::Zero -and
            [MoteHorizontalWin32]::Text($script:main).Contains('horizontal.txt')
    } 'Published editor did not open the 50 MiB line.' 180000
    $main = $script:main
    $canvas = [MoteHorizontalWin32]::FindWindowExW($main, [IntPtr]::Zero,
        'MoteInteractiveCanvas', $null)
    $islandInput = [MoteHorizontalWin32]::GetDlgItem($canvas, 301)
    if ($canvas -eq [IntPtr]::Zero -or $islandInput -eq [IntPtr]::Zero -or
        -not [MoteHorizontalWin32]::IsWindowVisible($islandInput)) { throw 'Opt-in canvas/input HWND absent.' }
    $report.dpi = [MoteHorizontalWin32]::GetDpiForWindow($canvas)
    $report.probe_system_dpi = [MoteHorizontalWin32]::GetDpiForSystem()
    $report.theme = 'application-selected (not independently introspected)'
    $report.windows_app_theme = try {
        (Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' `
            -Name AppsUseLightTheme -ErrorAction Stop).AppsUseLightTheme
    } catch { 'unavailable' }
    $report.canvas_hwnd = $canvas.ToString()
    $report.input_hwnd = $islandInput.ToString()
    Wait-Until { [MoteHorizontalWin32]::TextLength($islandInput) -gt 0 } 'Input projection never bound.'
    $report.initial_input_length = [MoteHorizontalWin32]::TextLength($islandInput)
    if ($report.initial_input_length -gt 16384) { throw 'Initial RichEdit projection exceeded 16 Ki UTF-16 units.' }
    # A desktop screen grab is meaningful only if the real editor is actually
    # unobscured. Force this transient test process above ordinary windows.
    if (-not [MoteHorizontalWin32]::SetWindowPos($main, [IntPtr](-1),
        0, 0, 0, 0, 0x43)) { throw 'Could not make the test editor topmost for pixel evidence.' }
    [void][MoteHorizontalWin32]::SetForegroundWindow($main)
    Start-Sleep -Milliseconds 100
    $phase = 'initial-pixels'
    $report.initial_hscroll = [MoteHorizontalWin32]::HPos($canvas)
    $report.initial_png = Capture-Canvas $canvas (Join-Path $run 'initial.png')
    $phase = 'horizontal-end'
    [void][MoteHorizontalWin32]::SendMessageW($canvas, 0x0114, [UIntPtr]7, [IntPtr]::Zero) # SB_RIGHT
    Wait-Until { [MoteHorizontalWin32]::HPos($canvas) -gt 900000 } `
        'SB_RIGHT did not move to the distant source-backed end.' 60000
    [void][MoteHorizontalWin32]::SendMessageW($canvas, 0x0114, [UIntPtr]2, [IntPtr]::Zero) # SB_PAGELEFT
    Start-Sleep -Milliseconds 300
    $report.far_hscroll = [MoteHorizontalWin32]::HPos($canvas)
    $phase = 'wheel-roundtrip'
    $report.vertical_before = [MoteHorizontalWin32]::VPos($canvas)
    [void][MoteHorizontalWin32]::SendMessageW($canvas, 0x020A,
        [UIntPtr]([uint64]((65536 - 120) * [long]65536)), [IntPtr]::Zero)
    $report.vertical_away = [MoteHorizontalWin32]::VPos($canvas)
    [void][MoteHorizontalWin32]::SendMessageW($canvas, 0x020A,
        [UIntPtr](120 -shl 16), [IntPtr]::Zero)
    Start-Sleep -Milliseconds 200
    $report.vertical_return = [MoteHorizontalWin32]::VPos($canvas)
    $report.wheel_return_hscroll = [MoteHorizontalWin32]::HPos($canvas)
    if ($report.vertical_away -le $report.vertical_before -or
        $report.vertical_return -ne $report.vertical_before) {
        throw 'Vertical wheel did not move away and return to the first line.'
    }
    $phase = 'source-selection-copy'
    # Canvas receives pointer messages directly, avoiding the overlaid RichEdit island.
    # Copied random tail text is an independent source-offset oracle, not a UI label.
    [Windows.Forms.Clipboard]::Clear()
    Send-Mouse $canvas 0x0201 64 12 1
    Send-Mouse $canvas 0x0202 64 12 0
    Start-Sleep -Milliseconds 200
    $report.far_png = Capture-Canvas $canvas (Join-Path $run 'far.png')
    $report.pixel_change = Measure-PixelDifference (Join-Path $run 'initial.png') `
        (Join-Path $run 'far.png')
    if ($report.pixel_change.different_samples -lt 100) {
        throw "Physical canvas changed at only $($report.pixel_change.different_samples) samples; a caret blink is not distant text evidence."
    }
    Send-Mouse $canvas 0x0201 64 12 1  # WM_LBUTTONDOWN
    Send-Mouse $canvas 0x0200 390 12 1 # WM_MOUSEMOVE
    Send-Mouse $canvas 0x0202 390 12 0 # WM_LBUTTONUP
    $report.clipboard_before_copy = [MoteHorizontalWin32]::ClipboardStatus()
    $report.native_selection_before_copy = [MoteHorizontalWin32]::InputSelection($islandInput)
    if (-not [MoteHorizontalWin32]::PostMessageW($main, 0x0111,
        [UIntPtr]212, [IntPtr]::Zero)) { throw 'Could not submit native Copy menu command.' }
    $selected = ''
    try {
        Wait-Until {
            $candidate = ''
            if (-not [MoteHorizontalWin32]::TryClipboardUnicode([ref]$candidate)) { return $false }
            $script:selected = $candidate
            return $true
        } 'Global canvas selection did not reach OS Unicode clipboard.' 5000
    }
    catch {
        $report.clipboard_timeout = [MoteHorizontalWin32]::ClipboardStatus()
        $report.native_selection_on_timeout = [MoteHorizontalWin32]::InputSelection($islandInput)
        throw
    }
    $report.clipboard_after_copy = [MoteHorizontalWin32]::ClipboardStatus()
    if ($selected.Length -lt 8 -or $selected.Length -gt 512) {
        throw "Canvas selection length $($selected.Length) does not prove a bounded far-source interval."
    }
    $tailOffset = $tail.IndexOf($selected, [StringComparison]::Ordinal)
    if ($tailOffset -lt 0 -or
        $tail.IndexOf($selected, $tailOffset + 1, [StringComparison]::Ordinal) -ge 0) {
        throw 'Copied text is not a unique interval of the distant deterministic tail.'
    }
    $globalOffset = $lineLength - $tailLength + $tailOffset
    $report.selected_source_offset = $globalOffset
    $report.selected_utf16_units = $selected.Length
    $report.input_length_after_selection = [MoteHorizontalWin32]::TextLength($islandInput)
    if ($report.input_length_after_selection -gt 16384) { throw 'RichEdit mirrored more than 16 Ki.' }
    $phase = 'edit-save'
    [void][MoteHorizontalWin32]::SendMessageW($islandInput, 0x0102, [UIntPtr][int][char]'X', [IntPtr]::Zero)
    Wait-Until { [MoteHorizontalWin32]::Text($main).Contains('•') } 'Canvas edit was not committed.'
    [void][MoteHorizontalWin32]::PostMessageW($main, 0x0111, [UIntPtr]203, [IntPtr]::Zero)
    Wait-Until {
        try { [MoteHorizontalWin32]::AssertPatched($baseline, $file,
                $globalOffset, $selected.Length, [byte][char]'X'); return $true }
        catch { return $false }
    } 'Save did not persist the exact distant selection replacement, preserving all other bytes.' 180000
    $report.saved_sha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    if ($report.saved_sha256 -ceq $originalHash) { throw 'Save remained byte-identical to baseline.' }
    $phase = 'reopen'
    [void][MoteHorizontalWin32]::PostMessageW($main, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)
    if (-not $process.WaitForExit(30000) -or $process.ExitCode -ne 0) { throw 'First editor did not close cleanly.' }
    $reopen = [Diagnostics.Process]::Start($start)
    Wait-Until {
        $reopen.Refresh()
        if ($reopen.HasExited) { throw "Reopened editor exited early ($($reopen.ExitCode))." }
        $script:reopenMain = [MoteHorizontalWin32]::MainWindow([uint32]$reopen.Id)
        return $script:reopenMain -ne [IntPtr]::Zero -and
            [MoteHorizontalWin32]::Text($script:reopenMain).Contains('horizontal.txt')
    } 'Second published process did not reopen saved file.' 180000
    $reopenCanvas = [MoteHorizontalWin32]::FindWindowExW($script:reopenMain,
        [IntPtr]::Zero, 'MoteInteractiveCanvas', $null)
    $reopenInput = [MoteHorizontalWin32]::GetDlgItem($reopenCanvas, 301)
    if ($reopenCanvas -eq [IntPtr]::Zero -or $reopenInput -eq [IntPtr]::Zero -or
        -not [MoteHorizontalWin32]::IsWindowVisible($reopenInput)) {
        throw 'Reopened published editor did not expose canvas and input island.'
    }
    if ([MoteHorizontalWin32]::TextLength($reopenInput) -gt 16384) {
        throw 'Reopened input island mirrored more than 16 Ki.'
    }
    [MoteHorizontalWin32]::AssertPatched($baseline, $file, $globalOffset,
        $selected.Length, [byte][char]'X')
    [void][MoteHorizontalWin32]::PostMessageW($script:reopenMain, 0x0010,
        [UIntPtr]::Zero, [IntPtr]::Zero)
    if (-not $reopen.WaitForExit(30000) -or $reopen.ExitCode -ne 0) {
        throw 'Reopened published editor did not close cleanly.'
    }
    $success = $true
    $report.status = 'passed'
    $report.scope = 'Physical screen capture differs; exact unique far-source selection/copy/edit/Save/reopen bytes; bounded RichEdit. No physical IME claim.'
}
catch { $report.error = $_.Exception.Message }
finally {
    $report.phase = $phase
    foreach ($p in @($process, $reopen)) {
        if ($null -ne $p) {
            if (-not $p.HasExited) { $p.Kill($true) }
            if (-not $success) {
                $prefix = if ($p -eq $process) { 'first' } else { 'reopen' }
                try {
                    [IO.File]::WriteAllText((Join-Path $run "$prefix-stdout.txt"),
                        $p.StandardOutput.ReadToEnd())
                    [IO.File]::WriteAllText((Join-Path $run "$prefix-stderr.txt"),
                        $p.StandardError.ReadToEnd())
                }
                catch { $report.error += " Could not collect $prefix process streams: $($_.Exception.Message)" }
            }
            $p.Dispose()
        }
    }
    if ($clipboardCaptured) {
        # Clipboard ownership can briefly remain with the editor or another
        # desktop process after Copy/close. Retry only the restoration, never
        # the non-idempotent source edit. The report cannot claim success until
        # the original clipboard object has been placed back on the OS clipboard.
        $report.clipboard_restore_before = [MoteHorizontalWin32]::ClipboardStatus()
        $restoreError = ''
        for ($attempt = 1; $attempt -le 50; $attempt++) {
            $report.clipboard_restore_attempts = $attempt
            try {
                if ($null -eq $originalClipboard) { [Windows.Forms.Clipboard]::Clear() }
                else { [Windows.Forms.Clipboard]::SetDataObject($originalClipboard, $true) }
                $report.clipboard_restored = $true
                break
            }
            catch {
                $restoreError = $_.Exception.Message
                Start-Sleep -Milliseconds 100
            }
        }
        $report.clipboard_restore_after = [MoteHorizontalWin32]::ClipboardStatus()
        if (-not $report.clipboard_restored) {
            $report.status = 'failed'
            $report.error += " Clipboard restoration failed after $($report.clipboard_restore_attempts) attempts: $restoreError; $($report.clipboard_restore_after)."
            $success = $false
        }
    }
    $json = $report | ConvertTo-Json -Depth 7
    $json | Set-Content -LiteralPath (Join-Path $cacheRoot 'latest.json') -Encoding utf8NoBOM
    $json | Set-Content -LiteralPath (Join-Path $run 'result.json') -Encoding utf8NoBOM
    Write-Host ($report | ConvertTo-Json -Depth 7 -Compress)
}
if (-not $success -or -not $report.clipboard_restored) { throw $report.error }
