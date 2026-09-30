<#
.SYNOPSIS
Opt-in ordinary Windows Save evidence collector; never opens a user fixture.
.DESCRIPTION
Requires an independently pinned current-source AOT publish manifest. One positive
no-Delete-share control precedes up to four ordinary children. All evidence is
retained, including incomplete traces. See docs/windows-save-diagnostic-driver.md.
#>
[CmdletBinding()]
param(
    [string] $PublishManifest,
    [ValidateRange(1, 4)][int] $OrdinaryRuns = 4,
    [switch] $SelfTest
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

# Validate every existing ancestor inside the repository before creating files.
function Assert-OwnedPath([string] $Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Path escaped repository.' }
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $cursor = $full
    while ($cursor.Length -ge $root.Length) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse ancestor rejected.' }
            $owner = (Get-Acl -LiteralPath $cursor).GetOwner([Security.Principal.SecurityIdentifier]).Value
            if ($owner -notin @($sid, 'S-1-5-18', 'S-1-5-32-544')) { throw 'Unexpected path owner.' }
        }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
    return $full
}

# Generate exact ASCII bytes; no external input is accepted as document content.
function New-FixtureBytes {
    $record = [byte[]]::new(1024)
    [Array]::Fill($record, [byte]97)
    $prefix = [Text.Encoding]::ASCII.GetBytes("STARTUP-MARKER # note`n")
    [Array]::Copy($prefix, $record, $prefix.Length)
    $record[1023] = 10
    $bytes = [byte[]]::new(1048576)
    for ($i = 0; $i -lt 1024; $i++) { [Array]::Copy($record, 0, $bytes, $i * 1024, 1024) }
    return ,$bytes
}

# Byte comparison is exact rather than relying only on digest equality.
function Get-ByteOutcome([string] $Path, [byte[]] $Original, [byte[]] $New) {
    try { $actual = [IO.File]::ReadAllBytes($Path) }
    catch {
        $errorObject = $_.Exception
        while ($null -ne $errorObject.InnerException) { $errorObject = $errorObject.InnerException }
        $missing = $errorObject -is [IO.FileNotFoundException] -or $errorObject -is [IO.DirectoryNotFoundException]
        return @{ state = $(if ($missing) { 'missing' } else { 'unknown' }); exists = $(if ($missing) { $false } else { $null });
            length = $null; original = $false; new = $false; error_type = $errorObject.GetType().FullName }
    }
    $oldMatch = [Convert]::ToBase64String($actual) -ceq [Convert]::ToBase64String($Original)
    $newMatch = [Convert]::ToBase64String($actual) -ceq [Convert]::ToBase64String($New)
    return @{ state = $(if ($oldMatch) { 'original' } elseif ($newMatch) { 'new' } else { 'unexpected' });
        exists = $true; length = $actual.Length; original = $oldMatch; new = $newMatch }
}

# A terminal session in the last nonempty record and normal exit are both required.
function Read-TraceEvidence([string] $TraceHome, [bool] $NormalExit) {
    $records = @()
    $valid = $true
    foreach ($file in @(Get-ChildItem -LiteralPath $TraceHome -Filter '*.jsonl' -Recurse -File | Sort-Object Name)) {
        foreach ($line in [IO.File]::ReadLines($file.FullName)) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            try {
                $record = $line | ConvertFrom-Json -AsHashtable
                if ($record -isnot [Collections.IDictionary] -or $record['operation'] -isnot [string]) { $valid = $false; continue }
                if ($record['operation'] -like 'save.failure.*' -and
                    ($record['attributes'] -isnot [Collections.IDictionary] -or $record['attributes']['hresult'] -isnot [long] -and $record['attributes']['hresult'] -isnot [int])) {
                    $valid = $false; continue
                }
                $records += $record
            }
            catch { $valid = $false }
        }
    }
    $terminal = $valid -and $records.Count -gt 0 -and $records[-1].operation -ceq 'mote.session' -and
        @($records | Where-Object { $_.operation -ceq 'mote.session' }).Count -eq 1 -and
        @($records | Where-Object { $_.operation -ceq 'telemetry.dropped' }).Count -eq 0
    $failures = @($records | Where-Object { $_.operation -like 'save.failure.*' } | ForEach-Object {
        @{ operation = $_.operation; hresult = $_.attributes.hresult }
    })
    return @{ capture = $(if ($NormalExit -and $terminal) { 'complete' } else { 'incomplete' });
        parsable = $valid; terminal_session = $terminal; records = $records.Count; failures = $failures }
}

# This allowlisted prefix is frozen with NativeEditorController's Save UI.
function Test-SaveFailureText([string] $Text) {
    return $Text.StartsWith('Save failed.', [StringComparison]::Ordinal)
}

# Match only the current warning for this fixture's deterministic recovery slot.
function Test-RetainedRecoveryText([string] $Text, [string] $Fixture) {
    $id = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes([IO.Path]::GetFileName($Fixture).ToUpperInvariant()))).ToLowerInvariant()
    $path = Join-Path ([IO.Path]::GetDirectoryName($Fixture)) ".mote-save-$id.recovery"
    $prefix = "The retained Save recovery will remain after closing this document:`n$path`nIt is attempted snapshot v"
    $suffix = ', not necessarily your latest edits. Inspect, copy, or remove it explicitly before saving this target again.'
    return $Text -cmatch ('^' + [regex]::Escape($prefix) + '[0-9]{1,10}' + [regex]::Escape($suffix) + '$')
}

# Export predicate outcomes, never modal text or a host-derived text digest.
function Get-ClosePredicates([bool] $OwnedMatch, [string] $Title, [string] $Text,
    [string] $Fixture, [int] $StaticCount, [int[]] $StaticTypes, [bool] $AtTextCap) {
    return @{ owned_match = $OwnedMatch; title_is_mote = $Title -ceq 'mote';
        text_length = $Text.Length; static_count = $StaticCount; static_types = @($StaticTypes);
        text_at_cap = $AtTextCap; save_failure_prefix = Test-SaveFailureText $Text;
        retained_warning_match = Test-RetainedRecoveryText $Text $Fixture;
        discard_match = $Text -ceq 'Discard unsaved changes?' }
}

# Exception wrappers are common in PowerShell; codes identify the actual failing API.
function Get-ExceptionCodes([Exception] $Exception) {
    $deepest = $Exception
    for ($depth = 0; $depth -lt 8 -and $null -ne $deepest.InnerException; $depth++) { $deepest = $deepest.InnerException }
    return @{ type = $deepest.GetType().FullName; hresult = $deepest.HResult }
}

# Certify a unique exact-ID Button, never a label or the first arbitrary control.
function Select-DialogButton($Snapshot, [int] $Id) {
    if ($Snapshot.Overflow) { return @{ handle = [IntPtr]::Zero; mode = 'overflow' } }
    $matches = @($Snapshot.Controls | Where-Object { $_.Id -eq $Id -and $_.ClassIsButton })
    if ($matches.Count -gt 1) { return @{ handle = [IntPtr]::Zero; mode = 'ambiguous' } }
    if ($matches.Count -eq 0) { return @{ handle = [IntPtr]::Zero; mode = 'missing' } }
    $button = $matches[0]
    if (-not $button.Owned -or -not $button.Descendant) { return @{ handle = [IntPtr]::Zero; mode = 'unsafe' } }
    if ($Snapshot.Direct -ne [IntPtr]::Zero -and $Snapshot.Direct -ne $button.Handle) { return @{ handle = [IntPtr]::Zero; mode = 'direct-invalid' } }
    if (-not $button.Visible -or -not $button.Enabled) { return @{ handle = [IntPtr]::Zero; mode = 'not-ready' } }
    return @{ handle = $button.Handle; mode = $(if ($Snapshot.Direct -eq $button.Handle) { 'direct' } else { 'descendant' }) }
}

# Revalidate the same modal's purpose on every bounded readiness attempt.
function Wait-CertifiedButton([IntPtr] $Dialog, [int] $Id,
    [ValidateSet('save-failure', 'retained-warning', 'discard')][string] $Purpose, [hashtable] $Row) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $attempts = 0
    $Row.button_lookup = @{ expected_id = $Id; attempts = 0; mode = 'not-observed'; ready = $false }
    while ($timer.ElapsedMilliseconds -lt 1000 -and $childClock.Elapsed.TotalSeconds -lt 38) {
        $attempts++
        $same = [MoteSaveDiagnosticWin32]::Find([uint32]$process.Id, '#32770', $window) -eq $Dialog
        if (-not $same) { $Row.close_reason = 'owner-mismatch'; throw 'Modal changed during button readiness.' }
        if ([MoteSaveDiagnosticWin32]::Text($Dialog) -cne 'mote') { $Row.close_reason = 'title-mismatch'; throw 'Modal title changed during button readiness.' }
        $body = [MoteSaveDiagnosticWin32]::InspectDialog($Dialog).Text
        $purposeMatch = switch ($Purpose) {
            'save-failure' { Test-SaveFailureText $body }
            'retained-warning' { Test-RetainedRecoveryText $body $fixture }
            'discard' { $body -ceq 'Discard unsaved changes?' }
        }
        if (-not $purposeMatch) { $Row.close_reason = 'close-purpose-mismatch'; throw 'Modal purpose changed during button readiness.' }
        $snapshot = [MoteSaveDiagnosticWin32]::InspectButtons($Dialog, [uint32]$process.Id, $Id)
        $selected = Select-DialogButton $snapshot $Id
        $Row.button_lookup = @{ expected_id = $Id; attempts = $attempts; mode = $selected.mode;
            child_ids = @($snapshot.Controls | ForEach-Object { $_.Id }); overflow = $snapshot.Overflow;
            direct_present = $snapshot.Direct -ne [IntPtr]::Zero; ready = $false;
            controls = @($snapshot.Controls | ForEach-Object {
                @{ id = $_.Id; is_button = $_.ClassIsButton; owned = $_.Owned; descendant = $_.Descendant;
                    direct_child = $_.DirectChild; visible = $_.Visible; enabled = $_.Enabled }
            }) }
        if ($selected.mode -in @('overflow', 'ambiguous', 'unsafe', 'direct-invalid')) {
            $Row.close_reason = 'button-uncertifiable'; throw 'Button identity could not be certified.'
        }
        # Give direct child construction a bounded grace before descendant fallback.
        $ready = $selected.handle -ne [IntPtr]::Zero -and
            ($selected.mode -eq 'direct' -or $attempts -gt 1 -and $timer.ElapsedMilliseconds -ge 250)
        if ($ready -and $timer.ElapsedMilliseconds -lt 1000 -and $childClock.Elapsed.TotalSeconds -lt 38) {
            if ($selected.mode -eq 'direct' -and $attempts -gt 1) { $Row.button_lookup.mode = 'direct-retry' }
            $Row.button_lookup.ready = $true
            $Row.button_lookup.readiness_ms = $timer.ElapsedMilliseconds
            $Row.close_predicates.button_found = $true
            return $selected.handle
        }
        Start-Sleep -Milliseconds 25
    }
    $Row.close_predicates.button_found = $false
    $Row.close_reason = 'button-missing'; throw 'No certifiable button within readiness budget.'
}

if (-not $IsWindows) { throw 'Windows PowerShell 7 required.' }
if ($SelfTest) {
    $dir = Assert-OwnedPath (Join-Path $root ('.temp/windows-save-diagnostic/selftest-' + [guid]::NewGuid().ToString('N')))
    New-Item -ItemType Directory -Path $dir | Out-Null
    $bytes = New-FixtureBytes
    $new = [byte[]]::new($bytes.Length + 1); $new[0] = 88; [Array]::Copy($bytes, 0, $new, 1, $bytes.Length)
    $target = Join-Path $dir 'fixture.md'
    if ((Get-ByteOutcome $target $bytes $new).state -ne 'missing') { throw 'Missing oracle failed.' }
    [IO.File]::WriteAllBytes($target, $bytes)
    if ((Get-ByteOutcome $target $bytes $new).state -ne 'original') { throw 'Original oracle failed.' }
    [IO.File]::WriteAllBytes($target, $new)
    if ((Get-ByteOutcome $target $bytes $new).state -ne 'new') { throw 'New oracle failed.' }
    [IO.File]::WriteAllBytes($target, [byte[]]@(0))
    if ((Get-ByteOutcome $target $bytes $new).state -ne 'unexpected') { throw 'Unexpected oracle failed.' }
    $locked = [IO.File]::Open($target, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
    try { if ((Get-ByteOutcome $target $bytes $new).state -ne 'unknown') { throw 'Unreadable target inferred missing.' } }
    finally { $locked.Dispose() }
    $trace = Join-Path $dir 'trace.jsonl'
    [IO.File]::WriteAllText($trace, '{"operation":"save.failure.replace","attributes":{"hresult":-2147024864}}' + "`n" + '{"operation":"mote.session"}')
    $evidence = Read-TraceEvidence $dir $true
    if ($evidence.capture -ne 'complete' -or $evidence.failures[0].hresult -ne -2147024864) { throw 'Control trace failed.' }
    if ((Read-TraceEvidence $dir $false).capture -ne 'incomplete') { throw 'Forced-exit oracle failed.' }
    [IO.File]::AppendAllText($trace, "`n{broken")
    if ((Read-TraceEvidence $dir $true).capture -ne 'incomplete') { throw 'Malformed trace accepted.' }
    [IO.File]::WriteAllText($trace, '{"operation":"open"}')
    if ((Read-TraceEvidence $dir $true).capture -ne 'incomplete') { throw 'Missing session accepted.' }
    [IO.File]::WriteAllText($trace, '{"operation":"save.failure.replace"}' + "`n" + '{"operation":"mote.session"}')
    if ((Read-TraceEvidence $dir $true).capture -ne 'incomplete') { throw 'Missing HResult accepted.' }
    [IO.File]::WriteAllText($trace, '{}' + "`n" + '{"operation":"mote.session"}')
    if ((Read-TraceEvidence $dir $true).capture -ne 'incomplete') { throw 'Missing operation accepted.' }
    [IO.File]::WriteAllText($trace, '{"operation":"telemetry.dropped","attributes":{"count":1}}' + "`n" + '{"operation":"mote.session"}')
    if ((Read-TraceEvidence $dir $true).capture -ne 'incomplete') { throw 'Dropped records accepted.' }
    if (-not (Test-SaveFailureText 'Save failed. The target matched the original snapshot when checked.')) { throw 'Current failure dialog rejected.' }
    if (Test-SaveFailureText 'Recovery export failed;') { throw 'Unrelated dialog accepted.' }
    $recoveryId = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes('FIXTURE.MD'))).ToLowerInvariant()
    $warning = "The retained Save recovery will remain after closing this document:`n$(Join-Path $dir ".mote-save-$recoveryId.recovery")`nIt is attempted snapshot v1, not necessarily your latest edits. Inspect, copy, or remove it explicitly before saving this target again."
    if (-not (Test-RetainedRecoveryText $warning $target)) { throw 'Owned retained-recovery warning rejected.' }
    if (Test-RetainedRecoveryText $warning (Join-Path $dir 'other.md')) { throw 'Foreign retained-recovery path accepted.' }
    $predicates = Get-ClosePredicates $true 'mote' 'Save failed. synthetic' $target 2 @(3, 0) $false
    if (-not $predicates.save_failure_prefix -or $predicates.static_types[0] -ne 3) { throw 'Close predicate evidence failed.' }
    $unrecognized = Get-ClosePredicates $false 'other' 'unrecognized synthetic text' $target 1 @(0) $true
    if ($unrecognized.owned_match -or $unrecognized.title_is_mote -or $unrecognized.save_failure_prefix) { throw 'Unknown dialog predicate accepted.' }
    if (($unrecognized | ConvertTo-Json -Depth 3).Contains('unrecognized synthetic text')) { throw 'Raw dialog text leaked into evidence.' }
    $wrapped = [Exception]::new('private outer text', [IO.IOException]::new('private inner text'))
    $codes = Get-ExceptionCodes $wrapped
    if ($codes.type -cne 'System.IO.IOException' -or ($codes | ConvertTo-Json).Contains('private')) { throw 'Exception code evidence failed.' }
    $control = @{ Id = 1; ClassIsButton = $true; Owned = $true; Descendant = $true; Visible = $true; Enabled = $true; Handle = [IntPtr]101 }
    $snapshot = @{ Direct = [IntPtr]101; Overflow = $false; Controls = @($control) }
    if ((Select-DialogButton $snapshot 1).mode -ne 'direct') { throw 'Direct button selection failed.' }
    $snapshot.Direct = [IntPtr]::Zero
    if ((Select-DialogButton $snapshot 1).mode -ne 'descendant') { throw 'Nested exact button selection failed.' }
    $snapshot.Controls = @($control, $control)
    if ((Select-DialogButton $snapshot 1).mode -ne 'ambiguous') { throw 'Duplicate button accepted.' }
    $snapshot.Controls = @($control); $control.Owned = $false
    if ((Select-DialogButton $snapshot 1).mode -ne 'unsafe') { throw 'Foreign button accepted.' }
    $control.Owned = $true; $control.Enabled = $false
    if ((Select-DialogButton $snapshot 1).mode -ne 'not-ready') { throw 'Disabled button accepted.' }
    $control.Enabled = $true; $control.ClassIsButton = $false
    if ((Select-DialogButton $snapshot 1).mode -ne 'missing') { throw 'Non-Button accepted.' }
    $control.ClassIsButton = $true; $snapshot.Overflow = $true
    if ((Select-DialogButton $snapshot 1).mode -ne 'overflow') { throw 'Overflow snapshot accepted.' }
    Write-Output 'PASS: exact byte oracles and complete/incomplete trace controls; no GUI launch.'
    return
}

if (-not $PublishManifest) { throw 'Explicit publish manifest required; no executable fallback.' }
$manifestPath = Assert-OwnedPath $PublishManifest
if (-not $manifestPath.StartsWith((Join-Path $root '.cache') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Publish manifest must live in repo .cache.' }
$pin = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($pin.schema -cne 'mote.windows-save-publish.v1') { throw 'Use the dedicated current-publish producer.' }
$publishLog = Assert-OwnedPath (Join-Path $root $pin.publish_log)
if ((Get-FileHash -LiteralPath $publishLog).Hash.ToLowerInvariant() -cne $pin.publish_log_sha256) { throw 'Publish log mismatch.' }
foreach ($input in $pin.inputs.PSObject.Properties) {
    $inputPath = Assert-OwnedPath (Join-Path $root $input.Name)
    if ((Get-FileHash -LiteralPath $inputPath).Hash.ToLowerInvariant() -cne $input.Value) { throw 'Pinned source input changed.' }
}
$head = (git -C $root rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $pin.head -cne $head) { throw 'Publish HEAD does not match checkout.' }
$changes = @(git -C $root status --porcelain -- src global.json Directory.Build.props Directory.Build.targets NuGet.config)
if ($LASTEXITCODE -ne 0 -or $changes.Count -ne 0) { throw 'Source/build inputs are not clean.' }
$exe = Assert-OwnedPath (Join-Path $root $pin.executable)
if (-not $exe.StartsWith((Join-Path $root '.cache') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Publish must be repo-local cache.' }
$entries = @(Get-ChildItem -LiteralPath ([IO.Path]::GetDirectoryName($exe)) -Force)
if ($entries.Count -ne 1 -or $entries[0].Name -cne 'mote.exe' -or $entries[0].PSIsContainer) { throw 'Expected exactly one mote.exe publish file.' }
$sha = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
if ($pin.sha256 -cne $sha -or $pin.bytes -ne $entries[0].Length) { throw 'Pinned executable mismatch.' }
$sdk = (dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or $pin.sdk -cne $sdk -or $pin.runtime_pack -notmatch '^Microsoft\.NETCore\.App\.Runtime\.win-x64/[0-9.]+$') { throw 'SDK/runtime pack provenance missing or mismatched.' }
if ($pin.publish_command -notmatch 'PublishAot=true' -or $pin.publish_command -notmatch 'win-x64' -or $pin.publish_command -notmatch 'self-contained true') { throw 'AOT publish attestation required.' }
$published = [DateTimeOffset]::Parse($pin.published_utc)
if ($published -gt [DateTimeOffset]::UtcNow -or $published -lt [DateTimeOffset]::UtcNow.AddHours(-2)) { throw 'Publish must be fresh (within two hours).' }
if ([DateTimeOffset]$entries[0].LastWriteTimeUtc -lt $published.AddSeconds(-5)) { throw 'Executable predates attested publish.' }
foreach ($input in @(git -C $root ls-files -- src global.json Directory.Build.props Directory.Build.targets NuGet.config)) {
    if ((Get-Item -LiteralPath (Join-Path $root $input)).LastWriteTimeUtc -gt $published.UtcDateTime) { throw 'Build input is newer than publish.' }
}

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading;
/// <summary>Kill only the owned child if UI or evidence I/O exceeds its budget.</summary>
public sealed class MoteSaveDiagnosticWatchdog : IDisposable {
    readonly Timer timer;
    int forced;
    public bool Forced => Volatile.Read(ref forced) != 0;
    public MoteSaveDiagnosticWatchdog(Process child, int milliseconds) {
        timer = new Timer(_ => {
            try { if (!child.HasExited) { Interlocked.Exchange(ref forced,1); child.Kill(); } }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }, null, milliseconds, Timeout.Infinite);
    }
    public void Dispose() => timer.Dispose();
}
/// <summary>In-memory dialog text and content-free metadata; never serialize Text.</summary>
public sealed class MoteSaveDialogObservation {
    public string Text;
    public int StaticCount;
    public int[] StaticTypes;
    public int[] StaticLengths;
    public bool AtTextCap;
}
/// <summary>Exact control identity metadata. Handles stay in memory, not reports.</summary>
public sealed class MoteSaveButtonControl {
    public IntPtr Handle;
    public int Id;
    public bool ClassIsButton, Owned, Descendant, DirectChild, Visible, Enabled;
}
/// <summary>At most 32 descendants, with overflow explicitly refusing selection.</summary>
public sealed class MoteSaveButtonSnapshot {
    public IntPtr Direct;
    public MoteSaveButtonControl[] Controls;
    public bool Overflow;
}
/// <summary>Bounded messages and exact PID/owner-filtered windows only.</summary>
public static class MoteSaveDiagnosticWin32 {
    public delegate bool Callback(IntPtr h, IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumWindows(Callback callback, IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, Callback callback, IntPtr data);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsChild(IntPtr parent, IntPtr child);
    [DllImport("user32.dll")] static extern int GetDlgCtrlID(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint command);
    [DllImport("user32.dll", EntryPoint="GetWindowLongW")] static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder text, int n);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr h, IntPtr after, string cls, string title);
    [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, UIntPtr w, IntPtr l);
    [DllImport("user32.dll", EntryPoint="SendMessageTimeoutW")] static extern IntPtr SendTimeout(IntPtr h, uint msg, UIntPtr w, IntPtr l, uint flags, uint ms, out IntPtr result);
    [DllImport("user32.dll", EntryPoint="SendMessageTimeoutW", CharSet=CharSet.Unicode)] static extern IntPtr TextTimeout(IntPtr h, uint msg, UIntPtr w, StringBuilder l, uint flags, uint ms, out IntPtr result);
    /// <summary>Read bounded text without unbounded synchronous UI calls.</summary>
    public static string Text(IntPtr h, int capacity=512) {
        var text = new StringBuilder(capacity); IntPtr result;
        if (TextTimeout(h, 13, (UIntPtr)(uint)capacity, text, 3, 500, out result)==IntPtr.Zero) throw new TimeoutException("UI text timeout");
        return text.ToString();
    }
    /// <summary>Return one visible window with exact PID/class/owner.</summary>
    public static IntPtr Find(uint expectedPid, string cls, IntPtr owner) {
        IntPtr result=IntPtr.Zero;
        EnumWindows((h,d)=> { uint pid; GetWindowThreadProcessId(h,out pid);
            var name=new StringBuilder(64); GetClassName(h,name,64);
            if(pid!=expectedPid || !IsWindowVisible(h) || name.ToString()!=cls || (owner!=IntPtr.Zero && GetWindow(h,4)!=owner)) return true;
            result=h; return false; }, IntPtr.Zero);
        return result;
    }
    /// <summary>Inspect static dialog text, never arbitrary control data.</summary>
    public static MoteSaveDialogObservation InspectDialog(IntPtr h) {
        var text=new StringBuilder();
        var types=new System.Collections.Generic.List<int>(); bool capped=false;
        var lengths=new System.Collections.Generic.List<int>();
        EnumChildWindows(h,(child,d)=> { var cls=new StringBuilder(64); GetClassName(child,cls,64);
            if(cls.ToString()=="Static" && text.Length<2048) {
                types.Add(GetWindowLong(child,-16) & 31);
                var value=Text(child); lengths.Add(value.Length); capped |= value.Length>=511; text.Append(value);
            }
            return true; },IntPtr.Zero);
        return new MoteSaveDialogObservation { Text=text.ToString(), StaticCount=types.Count,
            StaticTypes=types.ToArray(), StaticLengths=lengths.ToArray(), AtTextCap=capped };
    }
    /// <summary>Enumerate exact IDs/classes/ancestry, without reading button labels.</summary>
    public static MoteSaveButtonSnapshot InspectButtons(IntPtr dialog, uint expectedPid, int id) {
        if (dialog==IntPtr.Zero) throw new ArgumentException("Null modal rejected");
        var controls=new System.Collections.Generic.List<MoteSaveButtonControl>(); bool overflow=false;
        EnumChildWindows(dialog,(child,d)=> {
            if(controls.Count>=32) { overflow=true; return false; }
            uint pid; GetWindowThreadProcessId(child,out pid);
            var cls=new StringBuilder(64); GetClassName(child,cls,64);
            controls.Add(new MoteSaveButtonControl { Handle=child, Id=GetDlgCtrlID(child),
                ClassIsButton=cls.ToString()=="Button", Owned=pid==expectedPid,
                Descendant=IsChild(dialog,child), DirectChild=GetParent(child)==dialog,
                Visible=IsWindowVisible(child), Enabled=IsWindowEnabled(child) });
            return true;
        },IntPtr.Zero);
        return new MoteSaveButtonSnapshot { Direct=GetDlgItem(dialog,id), Controls=controls.ToArray(), Overflow=overflow };
    }
    /// <summary>Acknowledge input synchronously with a half-second bound.</summary>
    public static long Send(IntPtr h, uint msg, long w, long l) {
        IntPtr result;
        if(SendTimeout(h,msg,(UIntPtr)(ulong)w,(IntPtr)l,3,500,out result)==IntPtr.Zero) throw new TimeoutException("UI message timeout");
        return result.ToInt64();
    }
}
'@

$runId = [guid]::NewGuid().ToString('N')
$scratch = Assert-OwnedPath (Join-Path $root ".temp/windows-save-diagnostic/$runId")
$artifact = Assert-OwnedPath (Join-Path $root ".cache/windows-save-diagnostic/$runId")
New-Item -ItemType Directory -Path $scratch, $artifact | Out-Null
$volume = Get-Volume -DriveLetter ([IO.Path]::GetPathRoot($root)[0])
$manifest = @{ run_id = $runId; head = $head; sha256 = $sha; bytes = $entries[0].Length; sdk = $sdk;
    runtime_pack = $pin.runtime_pack; published_utc = $pin.published_utc; publish_command = $pin.publish_command;
    os = [Environment]::OSVersion.Version.ToString(); runner_image = $env:ImageVersion;
    filesystem = $volume.FileSystem; fixture_bytes = 1048576; max_children = 1 + $OrdinaryRuns }
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $artifact 'manifest.json') -Encoding utf8
$batchClock = [Diagnostics.Stopwatch]::StartNew()
$original = New-FixtureBytes
$new = [byte[]]::new($original.Length + 1); $new[0] = 88; [Array]::Copy($original, 0, $new, 1, $original.Length)
$rows = @()

# Wait budget includes UI calls, teardown and evidence capture, not just Save.
function Wait-Diagnostic([scriptblock] $Condition, [int] $UntilSeconds) {
    while ($childClock.Elapsed.TotalSeconds -lt $UntilSeconds -and $batchClock.Elapsed.TotalSeconds -lt 285) {
        if (& $Condition) { return }
        if ($process.HasExited) { throw 'Child exited before expected UI acknowledgement.' }
        Start-Sleep -Milliseconds 25
    }
    throw 'Bounded child capability/outcome timeout.'
}

for ($ordinal = 0; $ordinal -le $OrdinaryRuns; $ordinal++) {
    if ($batchClock.Elapsed.TotalSeconds -ge 240) { throw 'Batch launch budget exhausted.' }
    if ((Get-FileHash -LiteralPath $exe).Hash.ToLowerInvariant() -cne $sha) { throw 'Executable changed between children.' }
    $dir = Join-Path $scratch "child-$ordinal"
    $TraceHome = Join-Path $dir 'home'
    New-Item -ItemType Directory -Path $TraceHome | Out-Null
    $fixture = Join-Path $dir 'fixture.md'
    [IO.File]::WriteAllBytes($fixture, $original)
    $row = @{ ordinal = $ordinal; case = $(if ($ordinal -eq 0) { 'positive-no-delete-share-control' } else { 'ordinary' });
        fixture_sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($original)).ToLowerInvariant();
        expected_new_sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($new)).ToLowerInvariant();
        pid = $null; start_utc = $null; save_posted = $false; outcome = 'infrastructure-failure';
        dirty = $null; modal_class = $null; responsive = $null; normal_exit = $false; exit_code = $null;
        save_failure_observed = $false;
        close_error_stage = $null; close_reason = $null; close_predicates = $null;
        error_type = $null; trace_capture = 'incomplete'; disk = $null; held_ack = $false }
    $process = $null; $holder = $null; $watchdog = $null; $window = [IntPtr]::Zero; $modal = [IntPtr]::Zero
    $childClock = [Diagnostics.Stopwatch]::StartNew()
    try {
    try {
        $start = [Diagnostics.ProcessStartInfo]::new($exe)
        $start.UseShellExecute = $false; $start.CreateNoWindow = $true
        $start.WorkingDirectory = $root
        $start.ArgumentList.Add($fixture)
        $start.Environment.Remove('MOTE_TRACE_SUBDIR') | Out-Null
        $start.Environment['MOTE_HOME'] = $TraceHome; $start.Environment['MOTE_TRACE'] = '1'
        $process = [Diagnostics.Process]::Start($start)
        $watchdog = [MoteSaveDiagnosticWatchdog]::new($process, [Math]::Max(1, [int](45000 - $childClock.Elapsed.TotalMilliseconds)))
        $row.pid = $process.Id; $row.start_utc = $process.StartTime.ToUniversalTime().ToString('O')
        Wait-Diagnostic { $script:window = [MoteSaveDiagnosticWin32]::Find([uint32]$process.Id, 'MoteNativeEditorWindow', [IntPtr]::Zero); $window -ne [IntPtr]::Zero } 12
        Wait-Diagnostic {
            $canvas = [MoteSaveDiagnosticWin32]::FindWindowEx($window, [IntPtr]::Zero, 'MoteInteractiveCanvas', $null)
            if ($canvas -eq [IntPtr]::Zero) { return $false }
            $script:editor = [MoteSaveDiagnosticWin32]::FindWindowEx($canvas, [IntPtr]::Zero, 'RICHEDIT50W', $null)
            $editor -ne [IntPtr]::Zero -and [MoteSaveDiagnosticWin32]::Text($editor, 128).StartsWith('STARTUP-MARKER # note')
        } 18
        $length = [MoteSaveDiagnosticWin32]::Send($editor, 14, 0, 0)
        if ($length -le 0 -or $length -gt 16384) { throw 'Invalid source island length.' }
        [void][MoteSaveDiagnosticWin32]::Send($editor, 0xB1, 1, 1)
        if (([MoteSaveDiagnosticWin32]::Send($editor, 0xB0, 0, 0) -band 0xffffffffL) -ne 0x10001) { throw 'Selection acknowledgement mismatch.' }
        [void][MoteSaveDiagnosticWin32]::Send($editor, 0xB1, 0, 0)
        [void][MoteSaveDiagnosticWin32]::Send($editor, 0x102, 88, 0)
        Wait-Diagnostic { [MoteSaveDiagnosticWin32]::Text($window).Contains('•') } 22
        # The observer/control holder is separate from mote; ordinary cases hold nothing.
        if ($ordinal -eq 0) {
            $holder = [IO.File]::Open($fixture, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
            $row.held_ack = $holder.CanRead
            if (-not $row.held_ack) { throw 'Control did not acknowledge held handle.' }
        }
        if (-not [MoteSaveDiagnosticWin32]::PostMessage($window, 0x111, [UIntPtr]203, [IntPtr]::Zero)) { throw 'Save post failed.' }
        $row.save_posted = $true
        Wait-Diagnostic {
            $script:modal = [MoteSaveDiagnosticWin32]::Find([uint32]$process.Id, '#32770', $window)
            $modal -ne [IntPtr]::Zero -or -not [MoteSaveDiagnosticWin32]::Text($window).Contains('•')
        } 30
        $row.save_failure_observed = $modal -ne [IntPtr]::Zero
        $row.dirty = [MoteSaveDiagnosticWin32]::Text($window).Contains('•')
        $row.modal_class = $(if ($modal -ne [IntPtr]::Zero) { '#32770' } else { $null })
        [void][MoteSaveDiagnosticWin32]::Send($window, 0, 0, 0); $row.responsive = $true
        $row.disk = Get-ByteOutcome $fixture $original $new
        $row.outcome = $(if ($row.disk.state -in @('missing', 'unexpected')) { 'high-severity-unexpected-disk' }
            elseif ($modal -ne [IntPtr]::Zero) { 'save-failure' } elseif ($row.disk.new) { 'save-exact' } else { 'save-outcome-unverified' })
    }
    catch { $row.error_type = $_.Exception.GetType().FullName }
    finally {
        if ($null -ne $holder) { $holder.Dispose(); $holder = $null }
        if ($null -ne $process -and -not $process.HasExited) {
            try {
                if ($modal -ne [IntPtr]::Zero) {
                    # Never dismiss unknown dialogs or use global keyboard input.
                    $row.close_error_stage = 'failure-owner-check'
                    $current = [MoteSaveDiagnosticWin32]::Find([uint32]$process.Id, '#32770', $window)
                    $row.close_predicates = @{ owned_match = $current -eq $modal }
                    if ($current -ne $modal) { $row.close_reason = 'owner-mismatch'; throw 'Unrecognized failure dialog.' }
                    $row.close_error_stage = 'failure-title-read'
                    $title = [MoteSaveDiagnosticWin32]::Text($modal)
                    $row.close_predicates.title_is_mote = $title -ceq 'mote'
                    if ($title -cne 'mote') { $row.close_reason = 'title-mismatch'; throw 'Unrecognized failure dialog.' }
                    $row.close_error_stage = 'failure-static-read'
                    $observed = [MoteSaveDiagnosticWin32]::InspectDialog($modal)
                    $row.close_predicates = Get-ClosePredicates ($current -eq $modal) $title $observed.Text $fixture $observed.StaticCount $observed.StaticTypes $observed.AtTextCap
                    $row.close_predicates.static_lengths = @($observed.StaticLengths)
                    $row.close_error_stage = 'failure-purpose-guard'
                    if (-not $row.close_predicates.save_failure_prefix) { $row.close_reason = 'save-purpose-mismatch'; throw 'Unrecognized failure dialog.' }
                    $row.close_error_stage = 'failure-ok-lookup'
                    $ok = Wait-CertifiedButton $modal 1 'save-failure' $row
                    $row.close_error_stage = 'failure-ok-post'
                    $row.close_predicates.post_ack = [MoteSaveDiagnosticWin32]::PostMessage($ok, 0xF5, [UIntPtr]::Zero, [IntPtr]::Zero)
                    if (-not $row.close_predicates.post_ack) { $row.close_reason = 'message-post-failed'; throw 'Failure OK post failed.' }
                    $row.close_error_stage = 'failure-dismiss-wait'
                    Wait-Diagnostic { [MoteSaveDiagnosticWin32]::Find([uint32]$process.Id, '#32770', $window) -eq [IntPtr]::Zero } 34
                }
                $row.close_error_stage = 'main-close-post'
                if ($window -eq [IntPtr]::Zero) { $row.close_reason = 'main-window-missing'; throw 'No owned main window for normal close.' }
                $row.main_close_posted = [MoteSaveDiagnosticWin32]::PostMessage($window, 0x10, [UIntPtr]::Zero, [IntPtr]::Zero)
                if (-not $row.main_close_posted) { $row.close_reason = 'message-post-failed'; throw 'Main close post failed.' }
                $warningAck = $false; $discardAck = $false
                while (-not $process.HasExited -and $childClock.Elapsed.TotalSeconds -lt 40) {
                    $discard = [MoteSaveDiagnosticWin32]::Find([uint32]$process.Id, '#32770', $window)
                    if ($discard -ne [IntPtr]::Zero) {
                        $row.close_error_stage = 'close-dialog-title-read'
                        $title = [MoteSaveDiagnosticWin32]::Text($discard)
                        $row.close_predicates = @{ owned_match = $true; title_is_mote = $title -ceq 'mote' }
                        if ($title -cne 'mote') { $row.close_reason = 'title-mismatch'; throw 'Unrecognized close dialog title.' }
                        $row.close_error_stage = 'close-dialog-static-read'
                        $observed = [MoteSaveDiagnosticWin32]::InspectDialog($discard)
                        $text = $observed.Text
                        $row.close_predicates = Get-ClosePredicates $true 'mote' $text $fixture $observed.StaticCount $observed.StaticTypes $observed.AtTextCap
                        $row.close_predicates.static_lengths = @($observed.StaticLengths)
                        $row.close_error_stage = 'close-dialog-purpose-guard'
                        if (Test-RetainedRecoveryText $text $fixture) {
                            if (-not $warningAck) {
                                $row.close_error_stage = 'recovery-warning-ok-lookup'
                                $ok = Wait-CertifiedButton $discard 1 'retained-warning' $row
                                $row.close_error_stage = 'recovery-warning-ok-post'
                                $row.close_predicates.post_ack = [MoteSaveDiagnosticWin32]::PostMessage($ok, 0xF5, [UIntPtr]::Zero, [IntPtr]::Zero)
                                if (-not $row.close_predicates.post_ack) { $row.close_reason = 'message-post-failed'; throw 'Recovery warning OK post failed.' }
                                $warningAck = $true
                            }
                        }
                        elseif ($text -ceq 'Discard unsaved changes?') {
                            if (-not $discardAck) {
                                $row.close_error_stage = 'discard-yes-lookup'
                                $yes = Wait-CertifiedButton $discard 6 'discard' $row
                                $row.close_error_stage = 'discard-yes-post'
                                $row.close_predicates.post_ack = [MoteSaveDiagnosticWin32]::PostMessage($yes, 0xF5, [UIntPtr]::Zero, [IntPtr]::Zero)
                                if (-not $row.close_predicates.post_ack) { $row.close_reason = 'message-post-failed'; throw 'Discard Yes post failed.' }
                                $discardAck = $true
                            }
                        }
                        else { $row.close_reason = 'close-purpose-mismatch'; throw 'Unrecognized close dialog.' }
                    }
                    Start-Sleep -Milliseconds 50
                }
                $row.normal_exit = $process.HasExited -and -not $watchdog.Forced
                if ($row.normal_exit) { $row.close_error_stage = $null }
                else { $row.close_error_stage = 'normal-exit-wait'; $row.close_reason = 'normal-exit-incomplete' }
            }
            catch {
                $row.close_error_type = $_.Exception.GetType().FullName
                $row.close_exception = Get-ExceptionCodes $_.Exception
                if ($null -eq $row.close_reason) { $row.close_reason = 'api-or-runtime-exception' }
            }
        }
        # On an indeterminate Save, do not open target bytes until the child is gone.
        $evidenceDir = Join-Path $artifact "child-$ordinal"
        # Never copy/open a target or staged file while an uncertain Save is live.
        # A pre-kill snapshot is traces only; fixtures remain retained in scratch.
        New-Item -ItemType Directory -Path $evidenceDir | Out-Null
        $row | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $evidenceDir 'pre-exit-row.json') -Encoding utf8
        try {
            if (Test-Path -LiteralPath (Join-Path $TraceHome 'traces')) {
                Copy-Item -LiteralPath (Join-Path $TraceHome 'traces') -Destination (Join-Path $evidenceDir 'traces') -Recurse
            }
        }
        catch { $row.artifact_copy_error_type = $_.Exception.GetType().FullName }
        if ($null -ne $process -and -not $process.HasExited) {
            $row.trace_capture = 'incomplete-forced-exit'
            $process.Kill(); [void]$process.WaitForExit(1000)
        }
        if ($null -ne $watchdog -and $watchdog.Forced) { $row.normal_exit = $false; $row.trace_capture = 'incomplete-forced-exit' }
        if ($null -ne $process -and $process.HasExited) { $row.exit_code = $process.ExitCode }
        if ($null -ne $process -and -not $process.HasExited) { throw 'Owned child did not terminate; fixture evidence cannot be opened safely.' }
        if ($null -eq $row.disk) { $row.disk = Get-ByteOutcome $fixture $original $new }
        # Recapture after termination; retain the pre-kill snapshot separately.
        try { Copy-Item -LiteralPath $dir -Destination (Join-Path $artifact "child-$ordinal-final") -Recurse }
        catch { $row.final_copy_error_type = $_.Exception.GetType().FullName }
        $trace = Read-TraceEvidence $TraceHome ($row.normal_exit -and $row.exit_code -eq 0)
        if ($row.trace_capture -ne 'incomplete-forced-exit') { $row.trace_capture = $trace.capture }
        $row.trace = $trace; $row.elapsed_seconds = $childClock.Elapsed.TotalSeconds
        $row | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $artifact "row-$ordinal.json") -Encoding utf8
        $rows += $row
    }
    }
    finally {
        # Evidence errors must never strand a child or its held control handle.
        if ($null -ne $holder) { $holder.Dispose() }
        if ($null -ne $process -and -not $process.HasExited) {
            $process.Kill(); [void]$process.WaitForExit(1000)
        }
        if ($null -ne $watchdog) { $watchdog.Dispose() }
        if ($null -ne $process) { $process.Dispose() }
    }
    if ($ordinal -eq 0) {
        $control = $row.outcome -eq 'save-failure' -and $row.disk.original -and $row.dirty -and $row.trace_capture -eq 'complete' -and
            @($row.trace.failures | Where-Object { $_.operation -ceq 'save.failure.replace' -and $_.hresult -eq -2147024864 }).Count -eq 1
        if (-not $control) { throw "Positive trace/safety control failed; inspect $artifact" }
        continue
    }
    if ($row.outcome -ne 'save-exact' -or $row.trace_capture -ne 'complete' -or $row.trace.failures.Count -gt 0) { break }
}
$summary = @{ control = 'passed'; ordinary_attempted = @($rows | Where-Object { $_.case -eq 'ordinary' }).Count;
    ordinary_save_failures = @($rows | Where-Object { $_.case -eq 'ordinary' -and $_.save_failure_observed }).Count;
    interpretation = 'bounded diagnostic, not reliability rate or latency benchmark'; artifacts = ".cache/windows-save-diagnostic/$runId" }
$summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifact 'summary.json') -Encoding utf8
$summary
