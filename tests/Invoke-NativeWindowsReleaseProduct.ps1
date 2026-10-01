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
if ($original.Length -ge 65535 -or $expected.Length -ge 65535) {
    throw 'This ordinary-task observer requires fewer than 65,535 UTF-16 units; this is not an editor capacity limit.'
}
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
    // LVITEMW uses the platform's natural pointer alignment (x64/ARM64 are both
    // 64-bit here). Text points into memory allocated in the owned child.
    [StructLayout(LayoutKind.Sequential)] private struct Item {
        public uint Mask; public int Row, Column; public uint State, StateMask;
        public IntPtr Text; public int TextCapacity, Image; public IntPtr Parameter;
        public int Indent, Group, ColumnCount; public IntPtr Columns, ColumnFormats; public int GroupIndex;
    }
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr data);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint id);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr window, StringBuilder text, int size);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr window, StringBuilder text, int size);
    [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr window, int id);
    [DllImport("user32.dll", EntryPoint="SendMessageW")] public static extern IntPtr Send(IntPtr window, uint message, IntPtr first, IntPtr second);
    [DllImport("user32.dll", EntryPoint="SendMessageW", CharSet=CharSet.Unicode)] public static extern IntPtr SendText(IntPtr window, uint message, IntPtr first, string text);
    [DllImport("user32.dll", EntryPoint="SendMessageW", CharSet=CharSet.Unicode)] public static extern IntPtr ReadText(IntPtr window, uint message, IntPtr first, StringBuilder text);
    [DllImport("user32.dll", EntryPoint="PostMessageW")] public static extern bool Post(IntPtr window, uint message, IntPtr first, IntPtr second);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, UIntPtr size, uint allocation, uint protect);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, UIntPtr size, uint free);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] bytes, UIntPtr count, out UIntPtr written);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] bytes, UIntPtr count, out UIntPtr read);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
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
    /// <summary>Finds a descendant of this owned window, never a desktop-global table.</summary>
    public static IntPtr FindChild(IntPtr parent, uint pid, string name) {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(parent, (window, data) => {
            GetWindowThreadProcessId(window, out uint owner);
            var buffer = new StringBuilder(256);
            GetClassName(window, buffer, buffer.Capacity);
            if (owner == pid && buffer.ToString() == name) { found = window; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    /// <summary>Reads one actual owner-data cell through a bounded, correctly marshaled owned-process LVITEMW.</summary>
    public static string CellLabel(IntPtr table, uint pid, int row, int column) {
        GetWindowThreadProcessId(table, out uint owner);
        if (owner != pid || pid == 0 || IntPtr.Size != 8 || row < 0 || row >= 256 || column < 0 || column > 64)
            throw new InvalidOperationException("Native Grid observation ownership or bounds failed.");
        IntPtr process = OpenProcess(0x38, false, pid); // VM_OPERATION | VM_READ | VM_WRITE; no input/code execution.
        if (process == IntPtr.Zero) throw new InvalidOperationException("Owned Grid process observation failed.");
        IntPtr remote = IntPtr.Zero, local = IntPtr.Zero;
        try {
            int size = Marshal.SizeOf<Item>();
            remote = VirtualAllocEx(process, IntPtr.Zero, (UIntPtr)(size + 512), 0x3000, 4);
            if (remote == IntPtr.Zero) throw new InvalidOperationException("Owned Grid observation allocation failed.");
            var item = new Item { Mask=1, Row=row, Column=column, Text=IntPtr.Add(remote,size), TextCapacity=256 };
            local = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(item, local, false);
            var payload = new byte[size + 512];
            Marshal.Copy(local, payload, 0, size);
            if (!WriteProcessMemory(process,remote,payload,(UIntPtr)payload.Length,out UIntPtr written) || written.ToUInt64() != (ulong)payload.Length)
                throw new InvalidOperationException("Owned Grid observation write failed.");
            if (Send(table,0x104B,IntPtr.Zero,remote) == IntPtr.Zero) // LVM_GETITEMW invokes the real owner-data callback.
                throw new InvalidOperationException("Native Grid cell observation was refused.");
            var result = new byte[512];
            if (!ReadProcessMemory(process,item.Text,result,(UIntPtr)result.Length,out UIntPtr read) || read.ToUInt64() != (ulong)result.Length)
                throw new InvalidOperationException("Owned Grid observation read failed.");
            string text = Encoding.Unicode.GetString(result);
            int end = text.IndexOf('\0');
            if (end < 0) throw new InvalidOperationException("Native Grid observation exceeded its bounded text buffer.");
            return text.Substring(0,end);
        }
        finally {
            if (local != IntPtr.Zero) Marshal.FreeHGlobal(local);
            if (remote != IntPtr.Zero) VirtualFreeEx(process,remote,UIntPtr.Zero,0x8000);
            CloseHandle(process);
        }
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

# Wait for retained success evidence; incomplete live writer suffixes are not complete records.
function Read-ReleaseSavedSemantics {
    $traceRoot = Join-Path $evidence 'home/traces'
    if (-not [IO.Directory]::Exists($traceRoot)) { return $null }
    $rows = @()
    foreach ($file in [IO.Directory]::EnumerateFiles($traceRoot, '*.jsonl')) {
        $stream = [IO.FileStream]::new($file, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8)
        try { $lines = $reader.ReadToEnd().Split("`n") }
        finally { $reader.Dispose() }
        for ($index = 0; $index -lt $lines.Length - 1; $index++) {
            if ($lines[$index].Length) { $rows += ($lines[$index] | ConvertFrom-Json -AsHashtable) }
        }
    }
    $save = @($rows | Where-Object { $_.operation -ceq 'document.save' -and $_.status -ceq 'success' })
    if ($save.Count -ne 1 -or -not $save[0].attributes.ContainsKey('version')) { return $null }
    $version = $save[0].attributes.version
    foreach ($operation in @('analysis.parse','analysis.published','native.source.style_publish')) {
        if (-not @($rows | Where-Object { $_.operation -ceq $operation -and $_.status -ceq 'success' -and
            $_.attributes.ContainsKey('version') -and $_.attributes.version -eq $version }).Count) { return $null }
    }
    return @{ version=$version; session_id=$save[0].session_id }
}
function Read-ReleaseSelection([IntPtr] $Control) {
    # EM_EXGETSEL is >= WM_USER and cannot marshal a client-local CHARRANGE
    # pointer to another process. EM_GETSEL with null pointers returns the exact
    # packed range for our bounded tasks; refuse overflow rather than truncate.
    $packed = [MoteReleaseWin32]::Send($Control, 0x00B0, [IntPtr]::Zero, [IntPtr]::Zero).ToInt64()
    if ($packed -eq -1 -or $packed -eq 4294967295) { throw 'Native selection exceeded the bounded observer range.' }
    $range = [MoteReleaseWin32+Range]::new()
    $range.Start = [int]($packed -band 0xFFFF)
    $range.End = [int](($packed -shr 16) -band 0xFFFF)
    $script:observedSelection = @{ start=$range.Start; end=$range.End }
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
    $script:promptEdit = [IntPtr]::Zero
    Wait-ReleaseCondition {
        $script:prompt = [MoteReleaseWin32]::Find([uint32]$script:process.Id, 'MoteNativeTextPrompt')
        if ($script:prompt -eq [IntPtr]::Zero) { return $false }
        $script:promptEdit = [MoteReleaseWin32]::GetDlgItem($script:prompt, 301)
        # The top-level HWND becomes enumerable during WM_CREATE, before its
        # input/buttons exist. Observe ready controls before sending one answer.
        return $script:promptEdit -ne [IntPtr]::Zero -and
            [MoteReleaseWin32]::GetDlgItem($script:prompt, 302) -ne [IntPtr]::Zero
    } 'Native prompt controls did not become ready.'
    $edit = $script:promptEdit
    $script:promptControlPresent = $edit -ne [IntPtr]::Zero
    $script:promptTextSet = [MoteReleaseWin32]::SendText($edit, 0x000C, [IntPtr]::Zero, $Value) -ne [IntPtr]::Zero
    $promptValue = [Text.StringBuilder]::new(4096)
    [void][MoteReleaseWin32]::ReadText($edit, 0x000D, [IntPtr]$promptValue.Capacity, $promptValue)
    $script:promptTextMatches = $promptValue.ToString() -ceq $Value
    if (-not $script:promptControlPresent -or -not $script:promptTextSet -or -not $script:promptTextMatches) {
        throw 'Native prompt did not acknowledge exact fixture input.'
    }
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
    # Both edit and fresh reopen use the delivered ordinary launch, not an
    # explicitly selected candidate that could hide an incorrect default.
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
$observedSelection = $null
$expectedSelection = $null
$findObserved = $null
$promptControlPresent = $false
$promptTextSet = $false
$promptTextMatches = $false
$report = [ordered]@{ status = 'failed'; stage = $stage; profile = 'native-source'; launch_route = 'bare-default'; screenshot = $false;
    external_native_messages = $true; physical_keyboard = $false; real_ime = $false; screen_reader = $false }
try {
    $normalized = $original.Replace("`r`n", "`n").Replace("`r", "`n")
    $normalizedExpected = $expected.Replace("`r`n", "`n").Replace("`r", "`n")
    Start-ReleaseEditor 'edit'
    Wait-ReleaseCondition { (Read-ReleaseText $editor) -ceq $normalized } 'Exact native source did not open.'
    $stage = 'find'
    $offset = $normalized.IndexOf($marker, [StringComparison]::Ordinal)
    $expectedSelection = @{ start=$offset; end=$offset + $marker.Length }
    Invoke-ReleasePrompt 213 $marker
    Wait-ReleaseCondition {
        $selection = Read-ReleaseSelection $editor
        $matched = $selection.Start -eq $offset -and $selection.End -eq $offset + $marker.Length
        if ($matched) { $script:findObserved = @{ start=$selection.Start; end=$selection.End } }
        return $matched
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
    $stage = 'final-semantic-view'
    $kind = switch ([IO.Path]::GetExtension($inputFile).ToLowerInvariant()) {
        '.md' { 'Markdown' }; '.toml' { 'Toml' }; '.json' { 'Json' }; '.yaml' { 'Yaml' }; '.csv' { 'Csv' }; default { 'PlainText' }
    }
    $expectedCsvRows = @()
    if ($kind -ceq 'Csv') {
        $reader = [Microsoft.VisualBasic.FileIO.TextFieldParser]::new([IO.StringReader]::new($expected))
        try {
            $reader.SetDelimiters(','); $reader.HasFieldsEnclosedInQuotes = $true; $reader.TrimWhiteSpace = $false
            while (-not $reader.EndOfData) { $expectedCsvRows += ,$reader.ReadFields() }
        }
        finally { $reader.Dispose() }
        if ($expectedCsvRows.Count -gt 256 -or @($expectedCsvRows | Where-Object { $_.Length -gt 64 }).Count -or
            @($expectedCsvRows | ForEach-Object { $_ } | Where-Object { $_.Length -ge 256 }).Count) {
            throw 'CSV task exceeds this bounded native-label observer; this is not an editor capacity limit.'
        }
    }
    $script:semanticView = $null
    Wait-ReleaseCondition {
        $traceWitness = Read-ReleaseSavedSemantics
        if ($null -eq $traceWitness) { return $false }
        $statusControl = [MoteReleaseWin32]::GetDlgItem($window, 103)
        $status = [Text.StringBuilder]::new(4096)
        [void][MoteReleaseWin32]::GetWindowText($statusControl, $status, $status.Capacity)
        $text = $status.ToString()
        $policyName = if ($kind -ceq 'PlainText') { 'Plain text' } else { $kind }
        if ($text -notmatch ([regex]::Escape($policyName) + '.*(Complete|semantic analysis).*v' + $traceWitness.version + '\b')) { return $false }
        $gridCells = 0
        if ($kind -ceq 'Csv') {
            $table = [MoteReleaseWin32]::FindChild($window, [uint32]$process.Id, 'SysListView32')
            if ($table -eq [IntPtr]::Zero) { return $false }
            $count = [MoteReleaseWin32]::Send($table, 0x1004, [IntPtr]::Zero, [IntPtr]::Zero).ToInt64()
            if ($count -ne $expectedCsvRows.Count) { return $false }
            for ($row = 0; $row -lt $expectedCsvRows.Count; $row++) {
                for ($column = 0; $column -lt $expectedCsvRows[$row].Length; $column++) {
                    if ([MoteReleaseWin32]::CellLabel($table, [uint32]$process.Id, $row, $column + 1) -cne $expectedCsvRows[$row][$column]) { return $false }
                    $gridCells++
                }
            }
        }
        elseif ($kind -cne 'PlainText') {
            $preview = [MoteReleaseWin32]::GetDlgItem($window, 102)
            if (-not (Read-ReleaseText $preview).Contains($replacement, [StringComparison]::Ordinal)) { return $false }
        }
        $script:semanticView = @{ schema_version=1; document_kind=$kind; version=$traceWitness.version;
            trace_session_id=$traceWitness.session_id; source_units=$expected.Length; analysis_status_current=$true;
            parse_publish_style_witness=$true; grid_cells=$gridCells; grid_labels_exact=($kind -ceq 'Csv');
            endpoint='native status/preview or actual owner-data callbacks plus version-linked trace; not physical pixels' }
        return $true
    } 'The saved document did not reach a current semantic native view.'
    [IO.File]::WriteAllText((Join-Path $evidence 'native-product-semantics.json'), ($script:semanticView | ConvertTo-Json), $utf8)
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
    $report['find_expected'] = $expectedSelection
    $report['find_observed'] = $findObserved
    $report['last_observed_selection'] = $observedSelection
    $report['prompt_control_present'] = $promptControlPresent
    $report['prompt_text_set'] = $promptTextSet
    $report['prompt_text_matches'] = $promptTextMatches
    [IO.File]::WriteAllText((Join-Path $evidence 'windows-product.json'), ([pscustomobject]$report | ConvertTo-Json), $utf8)
    if ($process -and -not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
    if ($process) { $process.Dispose() }
}
