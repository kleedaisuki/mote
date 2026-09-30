# Measure the ordinary Native AOT mote <file> route using an external Win32 client.
# This is a source-bound/UI-message probe, NOT a first-paint or physical-key test.
param(
    [Parameter(Mandatory)][string] $ExecutablePath,
    [ValidateSet(1, 100)][int] $SizeMiB = 1,
    [ValidateRange(1, 20)][int] $Runs = 3,
    [switch] $CheckInventoryOnly,
    [switch] $ReadinessOnly,
    [switch] $DiagnosticTrace,
    [switch] $KeepFailureArtifacts,
    [string] $FixturePath,
    [string] $FixtureSha256
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'The Win32 ordinary-startup probe requires Windows.' }

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$scratchRoot = [IO.Path]::GetFullPath((Join-Path $root '.temp/native-startup'))
$resultRoot = [IO.Path]::GetFullPath((Join-Path $root '.cache/native-startup'))
$scratch = [IO.Path]::GetFullPath((Join-Path $scratchRoot ([guid]::NewGuid().ToString('N'))))
if (-not $scratch.StartsWith($scratchRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) { throw 'Scratch escaped the repository.' }

# A lexical prefix does not protect a recursive cleanup through a junction.
function Assert-NoReparseAncestors {
    param([string] $Path)
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Reparse-point ancestor forbidden: $cursor"
            }
        }
        $parent = [IO.Path]::GetDirectoryName($cursor)
        if (-not $parent -or $parent -ceq $cursor) { break }
        $cursor = $parent
    }
}

Assert-NoReparseAncestors $scratch
Assert-NoReparseAncestors $resultRoot
# External fixtures are an opt-in readiness-only adapter, never an edit/Save
# route. Require owned repo-local ASCII bytes and an independent pinned digest.
$externalFixture = -not [string]::IsNullOrWhiteSpace($FixturePath)
$fixtureExtension = '.md'
$sourcePrefix = 'STARTUP-MARKER # note'
$externalPath = $null
$externalSha = $null
if ($externalFixture -or -not [string]::IsNullOrWhiteSpace($FixtureSha256)) {
    if (-not $externalFixture -or $FixtureSha256 -cnotmatch '^[a-fA-F0-9]{64}$' -or -not $ReadinessOnly) {
        throw 'External fixture requires FixturePath, pinned FixtureSha256, and ReadinessOnly.'
    }
    if ($DiagnosticTrace) {
        throw 'External readiness pilot does not collect child traces; DiagnosticTrace is unsupported.'
    }
    $externalPath = [IO.Path]::GetFullPath($FixturePath)
    $ownedTemp = [IO.Path]::GetFullPath((Join-Path $root '.temp'))
    if (-not $externalPath.StartsWith($ownedTemp + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) { throw 'External fixture must remain below repository .temp.' }
    Assert-NoReparseAncestors $externalPath
    if (-not (Test-Path -LiteralPath $externalPath -PathType Leaf)) { throw 'External fixture is absent.' }
    $fixtureExtension = [IO.Path]::GetExtension($externalPath).ToLowerInvariant()
    if ($fixtureExtension -notin @('.json', '.csv', '.md')) { throw 'External fixture format is unsupported.' }
    if ([IO.FileInfo]::new($externalPath).Length -ne [long]$SizeMiB * 1048576) {
        throw 'External fixture must have the exact requested MiB size.'
    }
    $externalSha = (Get-FileHash -LiteralPath $externalPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($externalSha -cne $FixtureSha256.ToLowerInvariant()) { throw 'External fixture SHA-256 differs from pinned digest.' }
    $input = [IO.File]::OpenRead($externalPath)
    try {
        $prefixBytes = [byte[]]::new(20)
        $count = $input.Read($prefixBytes, 0, $prefixBytes.Length)
        if ($count -ne 20 -or @($prefixBytes | Where-Object { $_ -lt 32 -or $_ -gt 126 }).Count -ne 0) {
            throw 'External fixture needs 20 printable ASCII bytes before its first line break for the source oracle.'
        }
        $sourcePrefix = [Text.Encoding]::ASCII.GetString($prefixBytes)
    }
    finally { $input.Dispose() }
}
$exe = [IO.Path]::GetFullPath($ExecutablePath)
Assert-NoReparseAncestors $exe
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Executable absent: $exe" }
if ((Split-Path $exe -Leaf) -cne 'mote.exe') { throw 'Expected the one-file Windows mote.exe.' }
$exeDir = Split-Path $exe
$entries = @(Get-ChildItem -LiteralPath $exeDir -Force)
if ($entries.Count -ne 1 -or $entries[0].PSIsContainer -or
    $entries[0].Name -cne 'mote.exe' -or
    ($entries[0].Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw 'Publish directory must contain exactly one real file named mote.exe; hidden/system sidecars and subdirectories are forbidden.'
}
$sha = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
$exeBytes = [IO.FileInfo]::new($exe).Length
if ($CheckInventoryOnly) {
    [pscustomobject]@{ executable_sha256 = $sha; executable_bytes = $exeBytes;
        inventory_entries = $entries.Count }
    return
}
$gitHead = (git -C $root rev-parse HEAD).Trim()
New-Item -ItemType Directory -Force -Path $scratch, $resultRoot | Out-Null

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class MoteOrdinaryStartupWin32 {
    public delegate bool EnumWindowsProc(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr data);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr window, StringBuilder text, int capacity);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string windowName);
    [DllImport("user32.dll", EntryPoint="SendMessageTimeoutW", SetLastError=true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, UIntPtr wParam,
        IntPtr lParam, uint flags, uint timeoutMs, out IntPtr result);
    [DllImport("user32.dll", EntryPoint="SendMessageTimeoutW", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern IntPtr SendTextTimeout(IntPtr window, uint message, UIntPtr wParam,
        StringBuilder lParam, uint flags, uint timeoutMs, out IntPtr result);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);
    public static IntPtr MainWindow(uint expectedPid, bool requireEditorClass = false) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((window, data) => {
            uint pid;
            GetWindowThreadProcessId(window, out pid);
            if (pid == expectedPid && IsWindowVisible(window)) {
                if (requireEditorClass) {
                    var name = new StringBuilder(64);
                    GetClassName(window, name, name.Capacity);
                    if (name.ToString() != "MoteNativeEditorWindow") return true;
                }
                found = window; return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    public static uint ForegroundPid() {
        uint pid;
        GetWindowThreadProcessId(GetForegroundWindow(), out pid);
        return pid;
    }
    public static string Title(IntPtr window) {
        var text = new StringBuilder(256);
        GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }
    public static string WindowSummary(uint expectedPid) {
        var result = new StringBuilder();
        EnumWindows((window, data) => {
            uint pid;
            GetWindowThreadProcessId(window, out pid);
            if (pid != expectedPid || result.Length > 1024) return true;
            var cls = new StringBuilder(64);
            GetClassName(window, cls, cls.Capacity);
            result.Append(cls).Append(':').Append(Title(window))
                .Append(':').Append(IsWindowVisible(window)).Append(';');
            if (cls.ToString() == "#32770") {
                EnumChildWindows(window, (child, childData) => {
                    if (result.Length > 1024) return false;
                    var childClass = new StringBuilder(64);
                    GetClassName(child, childClass, childClass.Capacity);
                    if (childClass.ToString() == "Static") {
                        var childText = Title(child);
                        result.Append("Static:").Append(childText.Length > 160
                            ? childText.Substring(0, 160) : childText).Append(';');
                    }
                    return true;
                }, IntPtr.Zero);
            }
            return true;
        }, IntPtr.Zero);
        return result.ToString();
    }
    public static long Send(IntPtr window, uint message, long wParam, long lParam) {
        IntPtr result;
        if (SendMessageTimeout(window, message, (UIntPtr)(ulong)wParam, (IntPtr)lParam,
            3, 3000, out result) == IntPtr.Zero)
            throw new TimeoutException("Bounded Win32 message failed or exceeded 3 seconds.");
        return result.ToInt64();
    }
    public static string ReadText(IntPtr window, int capacity) {
        var text = new StringBuilder(capacity);
        IntPtr result;
        if (SendTextTimeout(window, 0x000D, (UIntPtr)(uint)capacity, text,
            3, 3000, out result) == IntPtr.Zero)
            throw new TimeoutException("Bounded WM_GETTEXT failed or exceeded 3 seconds.");
        return text.ToString();
    }
}
'@

# Exact 1024-byte Markdown-ish records make fixture size deterministic without
# allocating a 100 MiB PowerShell string. Fixture generation is outside timing.
function Write-Fixture {
    param([string] $Path, [int] $MiB)
    $head = [Text.Encoding]::ASCII.GetBytes("STARTUP-MARKER # note`n")
    $record = [byte[]]::new(1024)
    [Array]::Fill($record, [byte][char]'a')
    [Array]::Copy($head, $record, $head.Length)
    $record[1023] = [byte][char]"`n"
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    try { for ($i = 0; $i -lt $MiB * 1024; $i++) { $stream.Write($record) } }
    finally { $stream.Dispose() }
}

# Stream the prospective X+original SHA before editing, avoiding a second huge file.
function Get-ExpectedSha256 {
    param([string] $Path)
    $hash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
    $input = [IO.File]::OpenRead($Path)
    try {
        $hash.AppendData([byte[]]@([byte][char]'X'))
        $buffer = [byte[]]::new(1048576)
        while (($count = $input.Read($buffer)) -gt 0) { $hash.AppendData($buffer, 0, $count) }
        return [Convert]::ToHexString($hash.GetHashAndReset()).ToLowerInvariant()
    }
    finally { $input.Dispose(); $hash.Dispose() }
}

function Wait-For {
    param([scriptblock] $Condition, [string] $Failure, [int] $TimeoutMs)
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.ElapsedMilliseconds -lt $TimeoutMs) {
        if (& $Condition) { return }
        Start-Sleep -Milliseconds 5
    }
    throw $Failure
}

$resultPath = Join-Path $resultRoot 'ordinary-windows.jsonl'
$moteHome = Join-Path $scratch 'home'
$expectedSha = $null
$timeout = if ($SizeMiB -eq 100) { 120000 } else { 30000 }
$samples = [Collections.Generic.List[object]]::new()
$allPassed = $true

try {
    for ($ordinal = 0; $ordinal -lt $Runs; $ordinal++) {
        # Fresh filenames avoid test-induced external replacement of a previously
        # opened path. All files have byte-for-byte identical synthetic contents.
        $fixture = Join-Path $scratch "ordinary-$SizeMiB-$ordinal$fixtureExtension"
        if ($externalFixture) {
            Copy-Item -LiteralPath $externalPath -Destination $fixture
            if ((Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash.ToLowerInvariant() -cne $externalSha) {
                throw 'Copied external fixture differs from pinned digest.'
            }
        } else {
            Write-Fixture $fixture $SizeMiB
            if ($null -eq $expectedSha) { $expectedSha = Get-ExpectedSha256 $fixture }
        }
        $originalLength = [IO.FileInfo]::new($fixture).Length
        $start = [Diagnostics.ProcessStartInfo]::new($exe)
        $start.UseShellExecute = $false
        $start.WorkingDirectory = $root
        $start.Environment['MOTE_HOME'] = $moteHome
        $start.Environment['MOTE_TRACE'] = if ($DiagnosticTrace) { '1' } else { '0' }
        [void]$start.ArgumentList.Add($fixture)
        $sample = [ordered]@{
            schema_version = 1; utc = [DateTimeOffset]::UtcNow.ToString('O')
            os = [Runtime.InteropServices.RuntimeInformation]::OSDescription
            cpu_count = [Environment]::ProcessorCount
            git_head = $gitHead
            executable_sha256 = $sha; executable_bytes = $exeBytes
            size_mib = $SizeMiB; ordinal = $ordinal
            fixture_kind = if ($externalFixture) { 'external-pinned-readiness-only' } else { 'generated-markdown' }
            fixture_format = $fixtureExtension.TrimStart('.')
            original_fixture_sha256 = $externalSha
            copied_fixture_sha256_after_probe = $null
            input_fixture_sha256_after_probe = $null
            configuration_ms = $null; child_open_to_editable_ms = $null
            child_open_to_draw_submission_ms = $null
            child_endpoint_status = 'not-collected by external driver; no child clock subtraction'
            measurement_mode = if ($ReadinessOnly) { 'source-readiness-and-selection' }
                else { 'source-readiness-edit-and-exact-save' }
            diagnostic_trace = [bool]$DiagnosticTrace
            cache_class = if ($ordinal -eq 0) {
                'first editor invocation in batch; just-written unique file; OS cache not evicted'
            } else {
                'fresh editor process; just-written unique file; same MOTE_HOME; OS cache not evicted'
            }
            process_create_return_ms = $null
            child_process_id = $null
            window_visible_ms = $null; title_file_ms = $null
            source_bound_ms = $null; selection_ack_ms = $null
            edit_dirty_ack_ms = $null; save_exact_ms = $null
            foreground_pid_at_source = $null; focus_status = 'not-observed'
            source_island_chars = $null; source_prefix = $null
            canvas_seen = $false; editor_seen = $false; source_probe_chars = $null
            cpu_at_source_ms = $null; cpu_after_save_ms = $null
            working_set_at_source_bytes = $null; working_set_after_save_bytes = $null
            peak_working_set_bytes = $null; peak_virtual_bytes = $null
            saved_sha256 = $null; status = 'failed'; error = $null
            failure_window_title = $null; failure_file_bytes = $null
            failure_child_exited = $null; failure_foreground_pid = $null
            failure_process_windows = $null
            failure_main_window_ping = $null
            metric_boundary = 'external process launch to visible HWND, bound source, native message ack, exact Save; not first paint or physical key'
        }
        # All expensive metadata and fixture preparation are complete before
        # the launch clock starts. Process.Start is the first timed operation.
        $clock = [Diagnostics.Stopwatch]::StartNew()
        $process = $null
        $window = [IntPtr]::Zero
        try {
            $process = [Diagnostics.Process]::Start($start)
            $sample.process_create_return_ms = $clock.Elapsed.TotalMilliseconds
            if ($null -eq $process) { throw 'Process.Start returned null.' }
            $sample.child_process_id = $process.Id
            Wait-For {
                if ($process.HasExited) { throw "Editor exited early: $($process.ExitCode)" }
                $script:window = [MoteOrdinaryStartupWin32]::MainWindow([uint32]$process.Id, $externalFixture)
                return $script:window -ne [IntPtr]::Zero
            } 'Visible ordinary main HWND not observed.' $timeout
            $sample.window_visible_ms = $clock.Elapsed.TotalMilliseconds
            Wait-For {
                [MoteOrdinaryStartupWin32]::Title($window).Contains((Split-Path $fixture -Leaf))
            } 'Window title never named the opened file.' $timeout
            $sample.title_file_ms = $clock.Elapsed.TotalMilliseconds
            $canvas = [IntPtr]::Zero
            $editor = [IntPtr]::Zero
            Wait-For {
                $script:canvas = [MoteOrdinaryStartupWin32]::FindWindowEx($window,
                    [IntPtr]::Zero, 'MoteInteractiveCanvas', $null)
                if ($script:canvas -eq [IntPtr]::Zero) { return $false }
                $sample.canvas_seen = $true
                $script:editor = [MoteOrdinaryStartupWin32]::FindWindowEx($canvas,
                    [IntPtr]::Zero, 'RICHEDIT50W', $null)
                if ($script:editor -eq [IntPtr]::Zero) { return $false }
                $sample.editor_seen = $true
                $text = [MoteOrdinaryStartupWin32]::ReadText($editor, 128)
                $sample.source_probe_chars = $text.Length
                return $text.StartsWith($sourcePrefix, [StringComparison]::Ordinal)
            } 'Expected source marker was not bound to the native input island.' $timeout
            $sample.source_bound_ms = $clock.Elapsed.TotalMilliseconds
            # External bytes are compared in memory, never copied to the report.
            $sample.source_prefix = if ($externalFixture) { 'pinned-20-byte-ASCII-prefix-verified' } else { $sourcePrefix }
            $sample.source_island_chars = [MoteOrdinaryStartupWin32]::Send($editor, 0x000E, 0, 0)
            if ($sample.source_island_chars -gt 16384 -or $sample.source_island_chars -le 0) {
                throw "Unbounded or empty source island: $($sample.source_island_chars)"
            }
            $sample.foreground_pid_at_source = [MoteOrdinaryStartupWin32]::ForegroundPid()
            $sample.focus_status = if ($sample.foreground_pid_at_source -eq $process.Id) {
                'target-foreground; native first-responder not asserted'
            } else { 'foreign-foreground; exclude user-responsiveness inference' }
            $process.Refresh()
            $sample.cpu_at_source_ms = $process.TotalProcessorTime.TotalMilliseconds
            $sample.working_set_at_source_bytes = [long]$process.WorkingSet64
            [void][MoteOrdinaryStartupWin32]::Send($editor, 0x00B1, 1, 1) # EM_SETSEL
            $selection = [MoteOrdinaryStartupWin32]::Send($editor, 0x00B0, 0, 0) # EM_GETSEL
            if (($selection -band 0xffffffffL) -ne 0x00010001L) {
                throw "Native source selection acknowledgement differed: $selection"
            }
            [void][MoteOrdinaryStartupWin32]::Send($editor, 0x00B1, 0, 0)
            $sample.selection_ack_ms = $clock.Elapsed.TotalMilliseconds
            if (-not $ReadinessOnly) {
                [void][MoteOrdinaryStartupWin32]::Send($editor, 0x0102, [int][char]'X', 0) # WM_CHAR
                Wait-For { [MoteOrdinaryStartupWin32]::Title($window).Contains('•') } `
                    'Native edit did not mark the document dirty.' $timeout
                $sample.edit_dirty_ack_ms = $clock.Elapsed.TotalMilliseconds
                if (-not [MoteOrdinaryStartupWin32]::PostMessage($window, 0x0111,
                    [UIntPtr]203, [IntPtr]::Zero)) { throw 'Native Save menu command was not posted.' }
                Wait-For {
                    -not [MoteOrdinaryStartupWin32]::Title($window).Contains('•') -and
                        [IO.FileInfo]::new($fixture).Length -eq $originalLength + 1
                } 'Native Save did not complete.' $timeout
                $sample.save_exact_ms = $clock.Elapsed.TotalMilliseconds
                $sample.saved_sha256 = (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash.ToLowerInvariant()
                if ($sample.saved_sha256 -cne $expectedSha) {
                    throw 'Saved source SHA-256 does not match exact X+fixture.'
                }
            }
            $process.Refresh()
            if (-not $ReadinessOnly) {
                $sample.cpu_after_save_ms = $process.TotalProcessorTime.TotalMilliseconds
                $sample.working_set_after_save_bytes = [long]$process.WorkingSet64
            }
            $sample.peak_working_set_bytes = if ($process.PeakWorkingSet64 -gt 0) {
                [long]$process.PeakWorkingSet64 } else { $null }
            $sample.peak_virtual_bytes = if ($process.PeakVirtualMemorySize64 -gt 0) {
                [long]$process.PeakVirtualMemorySize64 } else { $null }
            if ($externalFixture) {
                $sample.copied_fixture_sha256_after_probe = (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash.ToLowerInvariant()
                $sample.input_fixture_sha256_after_probe = (Get-FileHash -LiteralPath $externalPath -Algorithm SHA256).Hash.ToLowerInvariant()
                if ($sample.copied_fixture_sha256_after_probe -cne $externalSha -or
                    $sample.input_fixture_sha256_after_probe -cne $externalSha) {
                    throw 'Readiness-only probe changed fixture bytes.'
                }
            }
            $sample.status = 'passed'
        }
        catch {
            $sample.error = $_.Exception.Message
            if ($null -ne $process) {
                $sample.failure_child_exited = $process.HasExited
                $sample.failure_foreground_pid = [MoteOrdinaryStartupWin32]::ForegroundPid()
                $sample.failure_process_windows = [MoteOrdinaryStartupWin32]::WindowSummary([uint32]$process.Id)
            }
            if ($window -ne [IntPtr]::Zero) {
                $sample.failure_window_title = [MoteOrdinaryStartupWin32]::Title($window)
                try {
                    [void][MoteOrdinaryStartupWin32]::Send($window, 0, 0, 0) # WM_NULL
                    $sample.failure_main_window_ping = 'responsive-within-3s'
                }
                catch { $sample.failure_main_window_ping = 'no-response-within-3s' }
            }
            if (Test-Path -LiteralPath $fixture) {
                $sample.failure_file_bytes = [IO.FileInfo]::new($fixture).Length
            }
        }
        finally {
            if ($null -ne $process) {
                try {
                    if (-not $process.HasExited) { $process.Kill($true); [void]$process.WaitForExit(10000) }
                } finally { $process.Dispose() }
            }
        }
        $samples.Add($sample)
        $json = $sample | ConvertTo-Json -Depth 5 -Compress
        Add-Content -LiteralPath $resultPath -Value $json -Encoding utf8
        Write-Output $json
        if ($sample.status -ne 'passed') {
            $allPassed = $false
            throw "Startup sample $ordinal failed: $($sample.error)"
        }
    }
}
finally {
    Assert-NoReparseAncestors $scratch
    if (-not ($KeepFailureArtifacts -and -not $allPassed) -and (Test-Path -LiteralPath $scratch)) {
        Remove-Item -LiteralPath $scratch -Recurse -Force
    }
}
