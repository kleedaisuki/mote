# Collect one fresh C0/P0 comparison; original AX errors remain experimental evidence.
# No keyboard, clipboard, input-source, TCC, Save, or desktop-global mutation is used.
param(
    [Parameter(Mandatory)][string]$ExecutablePath,
    [Parameter(Mandatory)][ValidateSet('osx-x64', 'osx-arm64')][string]$RuntimeIdentifier,
    [string]$ReportPath,
    [switch]$ValidateOnly
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$utf8 = [Text.UTF8Encoding]::new($false, $true)

# Canonical containment also rejects existing symlink/reparse ancestors before writes.
function Assert-ContainedPath([string]$Path, [string]$Boundary) {
    $prefix = $Boundary.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $Path.StartsWith($prefix, [StringComparison]::Ordinal)) { throw 'Unsafe artifact or executable path.' }
    $cursor = $Path
    while ($cursor.Length -ge $Boundary.Length) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Unsafe artifact or executable path.' }
        }
        $cursor = Split-Path -Parent $cursor
    }
}

# Drain process streams asynchronously but never persist their arbitrary native contents.
function Start-OwnedProcess([string]$File, [string[]]$Arguments, [hashtable]$Environment) {
    $info = [Diagnostics.ProcessStartInfo]::new($File)
    $info.WorkingDirectory = $root
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in $Arguments) { [void]$info.ArgumentList.Add($argument) }
    foreach ($key in $Environment.Keys) { $info.Environment[$key] = $Environment[$key] }
    $process = [Diagnostics.Process]::Start($info)
    return @{ process = $process; stdout = $process.StandardOutput.BaseStream.CopyToAsync([IO.Stream]::Null); stderr = $process.StandardError.BaseStream.CopyToAsync([IO.Stream]::Null) }
}

# The same ragged 1100-record fixture is used by the unchanged external AX gate.
function New-GridFixture([string]$Path) {
    $builder = [Text.StringBuilder]::new()
    for ($row = 1; $row -le 1100; $row++) {
        $fields = [Collections.Generic.List[string]]::new()
        $width = if ($row -eq 2) { 1 } else { 24 }
        for ($column = 1; $column -le $width; $column++) {
            $value = if ($row -eq 1 -and $column -eq 2) { '' } else {
                [string]::Format([Globalization.CultureInfo]::InvariantCulture, 'r{0:D5}c{1:D2}', $row, $column)
            }
            $fields.Add($value)
        }
        [void]$builder.Append([string]::Join(',', $fields))
        if ($row -lt 1100) { [void]$builder.Append("`n") }
    }
    $source = $builder.ToString()
    $records = $source.Split("`n")
    if ($records.Length -ne 1100 -or $records[0].Split(',')[1] -cne '' -or
        $records[1].Split(',').Length -ne 1 -or $records[1000].Split(',')[16] -cne 'r01001c17' -or
        $records[1099].Split(',')[23] -cne 'r01100c24' -or $source.Length -gt 512KB) { throw 'Invalid synthetic fixture.' }
    [IO.File]::WriteAllText($Path, $source, $utf8)
    return $source.Length
}

# Reject unknown report properties rather than admitting arbitrary native text.
function Assert-Keys($Object, [string[]]$Keys) {
    if ($null -eq $Object -or @($Object.PSObject.Properties.Name | Where-Object { $_ -cnotin $Keys }).Count -ne 0 -or
        @($Keys | Where-Object { $_ -cnotin $Object.PSObject.Properties.Name }).Count -ne 0) { throw 'report-schema-invalid' }
}
function Assert-Integer($Value, [long]$Minimum, [long]$Maximum, [bool]$Nullable = $false) {
    if ($null -eq $Value -and $Nullable) { return }
    if (($Value -isnot [int] -and $Value -isnot [long]) -or $Value -lt $Minimum -or $Value -gt $Maximum) { throw 'report-schema-invalid' }
}
function Assert-Boolean($Value, [bool]$Nullable = $false) {
    if ($null -eq $Value -and $Nullable) { return }
    if ($Value -isnot [bool]) { throw 'report-schema-invalid' }
}
function Assert-ClientReport($Value) {
    Assert-Keys $Value @('schema', 'status', 'trusted', 'ready', 'owned_target', 'discovery', 'actions', 'identity', 'client_calls', 'cleanup')
    if ($Value.schema -cne 'mote-grid-action-client-v1' -or $Value.status -cnotin @('reply-observed', 'unresolved')) { throw 'report-schema-invalid' }
    foreach ($key in @('trusted', 'ready', 'owned_target')) { Assert-Boolean $Value.$key }
    Assert-Keys $Value.discovery @('attempts', 'nodes', 'admissions')
    foreach ($key in @('attempts', 'nodes', 'admissions')) { Assert-Integer $Value.discovery.$key 0 12000 }
    Assert-Keys $Value.actions @('attempts', 'names_error', 'names_count', 'advertised', 'original_error', 'begin_recorded', 'end_recorded')
    Assert-Integer $Value.actions.attempts 0 1
    foreach ($key in @('names_error', 'original_error')) { Assert-Integer $Value.actions.$key -2147483648 2147483647 $true }
    Assert-Integer $Value.actions.names_count 0 16 $true
    Assert-Boolean $Value.actions.advertised $true
    foreach ($key in @('begin_recorded', 'end_recorded')) { Assert-Boolean $Value.actions.$key }
    if ($Value.actions.end_recorded -and (-not $Value.actions.begin_recorded -or $Value.actions.attempts -ne 1 -or $null -eq $Value.actions.original_error)) { throw 'report-schema-invalid' }
    if ($Value.actions.begin_recorded -and ($Value.actions.attempts -ne 1 -or $Value.actions.names_error -ne 0 -or $Value.actions.advertised -ne $true)) { throw 'report-schema-invalid' }
    if ($Value.status -ceq 'reply-observed') {
        if (-not $Value.actions.end_recorded) { throw 'report-schema-invalid' }
        Assert-Keys $Value.identity @('unique_current_table', 'retained_equal_current', 'parent_equal_discovered_group', 'window_equal_discovered_window',
            'top_level_equal_window', 'parent_cycle', 'parent_reaches_window', 'reciprocal_child_occurrences', 'audit_admissions', 'audit_exhausted')
        foreach ($key in @('unique_current_table', 'retained_equal_current', 'parent_equal_discovered_group', 'window_equal_discovered_window',
            'top_level_equal_window', 'parent_cycle', 'parent_reaches_window')) { Assert-Boolean $Value.identity.$key $true }
        Assert-Integer $Value.identity.reciprocal_child_occurrences 0 128 $true
        Assert-Integer $Value.identity.audit_admissions 0 128
        Assert-Boolean $Value.identity.audit_exhausted
    }
    elseif (@($Value.identity.PSObject.Properties).Count -ne 0) { throw 'report-schema-invalid' }
    if (@($Value.client_calls).Count -gt 12000) { throw 'report-schema-invalid' }
    $sequence = 0
    foreach ($call in $Value.client_calls) {
        Assert-Keys $call @('sequence', 'phase', 'operation', 'error')
        Assert-Integer $call.sequence 1 12000
        if ($call.sequence -le $sequence -or $call.phase -cnotin @('prelude', 'post-reply') -or
            $call.operation -cnotin @('pid', 'timeout', 'child-count', 'children', 'role', 'identifier', 'parent', 'window', 'top-level', 'parent-walk', 'action-names', 'show-menu')) { throw 'report-schema-invalid' }
        Assert-Integer $call.error -2147483648 2147483647
        $sequence = $call.sequence
    }
    $actionCalls = @($Value.client_calls | Where-Object { $_.operation -ceq 'show-menu' })
    $expectedActions = if ($Value.actions.end_recorded) { 1 } else { 0 }
    if ($actionCalls.Count -ne $expectedActions -or ($expectedActions -eq 1 -and $actionCalls[0].error -ne $Value.actions.original_error)) { throw 'report-schema-invalid' }
    Assert-Keys $Value.cleanup @('finish_after_reply', 'finish_after_audit')
    Assert-Boolean $Value.cleanup.finish_after_reply $true
    Assert-Boolean $Value.cleanup.finish_after_audit $true
    if ($Value.cleanup.finish_after_reply -and -not $Value.actions.end_recorded) { throw 'report-schema-invalid' }
}

# Lean stage-1 server evidence explicitly excludes unavailable getter dispatch rows.
function Assert-ServerReport($Value) {
    Assert-Keys $Value @('schema', 'ready_published', 'finish_consumed', 'normal_shutdown', 'dispatcher_observation_available',
        'callback_entries', 'off_main_entries', 'entry_overflow', 'lifetime_overflow', 'requests', 'dispatches', 'opens', 'closes', 'detaches',
        'action_entries', 'lifetime', 'server_calls')
    if ($Value.schema -cne 'mote-grid-action-server-v1' -or $Value.dispatcher_observation_available -cne $false -or
        @($Value.server_calls).Count -ne 0) { throw 'report-schema-invalid' }
    foreach ($key in @('ready_published', 'finish_consumed', 'normal_shutdown', 'dispatcher_observation_available')) { Assert-Boolean $Value.$key }
    foreach ($key in @('callback_entries', 'off_main_entries', 'entry_overflow', 'lifetime_overflow', 'requests', 'dispatches', 'opens', 'closes', 'detaches')) {
        Assert-Integer $Value.$key 0 2147483647
    }
    if (@($Value.action_entries).Count -gt 16 -or @($Value.lifetime).Count -gt 16 -or
        $Value.callback_entries -ne (@($Value.action_entries).Count + $Value.entry_overflow)) { throw 'report-schema-invalid' }
    foreach ($entry in $Value.action_entries) {
        Assert-Keys $entry @('on_main_thread', 'owner_lookup', 'receiver_equal_current_root', 'attached', 'installing', 'frame_present',
            'ready_baseline_available', 'attachment_equal_baseline', 'epoch_equal_baseline', 'queue_result', 'method_return', 'caught_exception')
        Assert-Boolean $entry.on_main_thread $true
        Assert-Boolean $entry.caught_exception
        foreach ($key in @('owner_lookup', 'receiver_equal_current_root', 'attached', 'installing', 'frame_present',
            'ready_baseline_available', 'attachment_equal_baseline', 'epoch_equal_baseline', 'queue_result', 'method_return')) { Assert-Boolean $entry.$key $true }
    }
    foreach ($event in $Value.lifetime) {
        Assert-Keys $event @('phase', 'attachment_equal_baseline', 'epoch_equal_baseline', 'receiver_equal_current_root', 'attached')
        if ($event.phase -cnotin @('queue', 'dispatch', 'open', 'close', 'detach')) { throw 'report-schema-invalid' }
        foreach ($key in @('attachment_equal_baseline', 'epoch_equal_baseline', 'receiver_equal_current_root', 'attached')) { Assert-Boolean $event.$key }
    }
}

# Read only bounded JSON artifacts, never stdout or arbitrary exception strings.
function Read-BoundedReport([string]$Path) {
    Assert-ContainedPath $Path $scratch
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf) -or (Get-Item -LiteralPath $Path).Length -gt 4MB) { throw 'report-unavailable' }
    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -Depth 20
}

# Fixed-size session markers cannot contain arbitrary commands or payloads.
function Test-FixedMarker([string]$Path, [string]$Token) {
    Assert-ContainedPath $Path $scratch
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf) -or (Get-Item -LiteralPath $Path).Length -ne $utf8.GetByteCount($Token)) { return $false }
    return [IO.File]::ReadAllText($Path, $utf8) -ceq $Token
}

if ([string]::IsNullOrWhiteSpace($ReportPath)) { $ReportPath = ".cache/ci-inventory/mac-grid-showmenu-pair-$RuntimeIdentifier.json" }
$report = [IO.Path]::GetFullPath((Join-Path $root $ReportPath))
$exe = [IO.Path]::GetFullPath((Join-Path $root $ExecutablePath))
Assert-ContainedPath $report (Join-Path $root '.cache/ci-inventory')
Assert-ContainedPath $exe $root
if (-not $report.EndsWith('.json', [StringComparison]::Ordinal)) { throw 'Unsafe artifact or executable path.' }
$scratch = Join-Path $root (".cache/mac-grid-showmenu-discriminator/{0}/{1}" -f [guid]::NewGuid().ToString('N'), $RuntimeIdentifier)
Assert-ContainedPath $scratch (Join-Path $root '.cache')
$result = [ordered]@{
    schema = 'mote-grid-showmenu-pair-owner-v1'; rid = $RuntimeIdentifier; status = 'not-run'
    host_architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    driver_sha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
    source_sha256 = [ordered]@{}; source_unchanged = $false; native_typecheck = $false; compiled = $false
    client_sha256 = $null; control_sha256 = $null; binary_sha256 = $null; strict_one_file_inventory = $null
    fixture_sha256 = $null; fixture_utf16_units = $null; fixture_records = 1100; fixture_max_columns = 24
    artifact_relative_path = [IO.Path]::GetRelativePath($root, $scratch); sessions = @(); error_code = ''
    scope = 'lean shared-client first comparison; not product AX, VoiceOver, IME, or release acceptance'
}
try {
    foreach ($name in @('Client.m', 'Control.m')) {
        $result.source_sha256[$name] = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $name) -Algorithm SHA256).Hash
    }
    [void][IO.Directory]::CreateDirectory($scratch)
    $fixture = Join-Path $scratch 'grid-fixture.csv'
    $fixtureUnits = New-GridFixture $fixture
    $fixtureHash = (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash
    $result.fixture_sha256 = $fixtureHash; $result.fixture_utf16_units = $fixtureUnits
    if ($ValidateOnly) { $result.status = 'portable-preflight-passed' }
    else {
        if (-not $IsMacOS) { throw 'platform-unavailable' }
        $expected = if ($RuntimeIdentifier -ceq 'osx-x64') { 'X64' } else { 'Arm64' }
        if ($result.host_architecture -cne $expected) { throw 'architecture-mismatch' }
        $payloads = @(Get-ChildItem -LiteralPath (Split-Path -Parent $exe) -Recurse -File)
        if ($payloads.Count -ne 1 -or $payloads[0].FullName -cne $exe) { throw 'strict-inventory-failed' }
        $result.strict_one_file_inventory = $true
        $result.binary_sha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
        $flags = @('-fobjc-arc', '-Wall', '-Wextra', '-Werror', '-Wno-deprecated-declarations')
        foreach ($name in @('Client', 'Control')) {
            $source = Join-Path $PSScriptRoot "$name.m"
            $compilerOutput = & /usr/bin/xcrun clang @flags -fsyntax-only $source 2>&1
            if ($LASTEXITCODE -ne 0) {
                # Compiler-only diagnostics concern committed test code, before any target launches.
                $text = ($compilerOutput | ForEach-Object { [string]$_ }) -join "`n"
                [IO.File]::WriteAllText((Join-Path $scratch 'compiler.log'), $text.Substring(0, [Math]::Min(16000, $text.Length)), $utf8)
                throw 'native-typecheck-failed'
            }
            $compilerOutput = & /usr/bin/xcrun clang @flags -framework Cocoa -framework ApplicationServices $source -o (Join-Path $scratch $name) 2>&1
            if ($LASTEXITCODE -ne 0) {
                $text = ($compilerOutput | ForEach-Object { [string]$_ }) -join "`n"
                [IO.File]::WriteAllText((Join-Path $scratch 'compiler.log'), $text.Substring(0, [Math]::Min(16000, $text.Length)), $utf8)
                throw 'native-compile-failed'
            }
        }
        $result.native_typecheck = $true; $result.compiled = $true
        $result.client_sha256 = (Get-FileHash -LiteralPath (Join-Path $scratch 'Client')).Hash
        $result.control_sha256 = (Get-FileHash -LiteralPath (Join-Path $scratch 'Control')).Hash
        foreach ($sessionName in @('C0', 'P0')) {
            $session = Join-Path $scratch $sessionName
            [void][IO.Directory]::CreateDirectory($session)
            $facts = [ordered]@{
                session = $sessionName; status = 'probe-error'; owner_exit_code = $null; client_exit_code = $null
                owner_normal_exit = $false; forced_cleanup = $false; ready_observed = $false; ready_polls = 0; finish_after_reply = $false
                fixture_sha256 = $fixtureHash; fixture_utf16_units = $fixtureUnits; fixture_unchanged = $false
                client = $null; server = $null; error_code = ''
            }
            $owner = $null; $client = $null
            $watch = [Diagnostics.Stopwatch]::StartNew()
            try {
                $environment = @{
                    MOTE_HOME = (Join-Path $session 'home'); MOTE_TRACE = '0'; MOTE_NATIVE_MAC_STAGE_TRACE = '0'
                    MOTE_NATIVE_GRID_ACCESSIBILITY = '1'; MOTE_NATIVE_GRID_SHOWMENU_DISCRIMINATOR = '1'
                    MOTE_NATIVE_GRID_SHOWMENU_SESSION = $session; MOTE_NATIVE_GRID_MENU_DIAGNOSTIC = '0'
                }
                if ($sessionName -ceq 'C0') { $owner = Start-OwnedProcess (Join-Path $scratch 'Control') @($session) $environment }
                else { $owner = Start-OwnedProcess $exe @($fixture) $environment }
                $ready = Join-Path $session 'ready'
                $polls = 0
                while (-not (Test-Path -LiteralPath $ready -PathType Leaf) -and $polls -lt 20) {
                    if ($owner.process.HasExited -or $watch.Elapsed.TotalSeconds -ge 12) { throw 'ready-unavailable' }
                    $polls++
                    Start-Sleep -Milliseconds 150
                }
                $facts.ready_polls = $polls
                if (-not (Test-Path -LiteralPath $ready -PathType Leaf)) { throw 'ready-unavailable' }
                if (-not (Test-FixedMarker $ready "mote-grid-pair-ready-v1`n")) { throw 'ready-contract-invalid' }
                $facts.ready_observed = $true
                $client = Start-OwnedProcess (Join-Path $scratch 'Client') @([string]$owner.process.Id, $session) @{}
                $remaining = [Math]::Max(0, [Math]::Min(55000, 65000 - [int]$watch.ElapsedMilliseconds))
                if (-not $client.process.WaitForExit($remaining)) { throw 'client-watchdog' }
                $facts.client_exit_code = $client.process.ExitCode
                $null = $client.stdout.GetAwaiter().GetResult(); $null = $client.stderr.GetAwaiter().GetResult()
                $finish = Join-Path $session 'finish'
                if (-not (Test-FixedMarker $finish "mote-grid-pair-finish-v1`n")) { throw 'finish-contract-invalid' }
                $remaining = [Math]::Max(0, [Math]::Min(10000, 75000 - [int]$watch.ElapsedMilliseconds))
                if (-not $owner.process.WaitForExit($remaining)) { throw 'owner-watchdog' }
                $facts.owner_exit_code = $owner.process.ExitCode
                $facts.owner_normal_exit = $owner.process.ExitCode -eq 0
                $null = $owner.stdout.GetAwaiter().GetResult(); $null = $owner.stderr.GetAwaiter().GetResult()
                $clientReport = Read-BoundedReport (Join-Path $session 'client.json')
                Assert-ClientReport $clientReport
                $serverReport = Read-BoundedReport (Join-Path $session 'server.json')
                Assert-ServerReport $serverReport
                $facts.client = $clientReport; $facts.server = $serverReport
                $facts.finish_after_reply = $clientReport.cleanup.finish_after_reply
                $facts.status = 'unresolved'
                if ($facts.owner_normal_exit -and $facts.client_exit_code -eq 0 -and $serverReport.ready_published -and
                    $serverReport.finish_consumed -and $serverReport.normal_shutdown -and $serverReport.entry_overflow -eq 0 -and
                    $serverReport.lifetime_overflow -eq 0 -and $clientReport.status -ceq 'reply-observed' -and $clientReport.trusted -and
                    $clientReport.ready -and $clientReport.owned_target -and $clientReport.actions.attempts -eq 1 -and
                    $clientReport.actions.begin_recorded -and $clientReport.actions.end_recorded -and
                    $clientReport.cleanup.finish_after_reply -and $clientReport.cleanup.finish_after_audit) {
                    $facts.status = if ($clientReport.actions.original_error -eq 0) { 'completed-success-observed' } else { 'completed-failure-observed' }
                }
            }
            catch {
                $code = $_.Exception.Message
                $facts.error_code = if ($code -cin @('ready-unavailable', 'ready-contract-invalid', 'client-watchdog', 'finish-contract-invalid',
                    'owner-watchdog', 'report-schema-invalid', 'report-unavailable', 'Unsafe artifact or executable path.')) { $code } else { 'session-incomplete' }
            }
            finally {
                foreach ($owned in @($client, $owner)) {
                    if ($null -eq $owned) { continue }
                    if (-not $owned.process.HasExited) { $owned.process.Kill($true); $null = $owned.process.WaitForExit(5000); $facts.forced_cleanup = $true }
                    $owned.process.Dispose()
                }
                $facts.fixture_unchanged = (Get-FileHash -LiteralPath $fixture).Hash -ceq $fixtureHash
                if ($facts.forced_cleanup -or -not $facts.fixture_unchanged) { $facts.status = 'unresolved' }
                $result.sessions += $facts
            }
        }
        $statuses = @($result.sessions | ForEach-Object { $_.status })
        $result.status = 'unresolved'
        if ($statuses.Count -eq 2 -and @($statuses | Where-Object { $_ -cnotin @('completed-success-observed', 'completed-failure-observed') }).Count -eq 0) {
            $result.status = if ('completed-failure-observed' -cin $statuses) { 'completed-failure-observed' } else { 'completed-success-observed' }
        }
    }
}
catch {
    $result.status = 'probe-error'
    $code = $_.Exception.Message
    $result.error_code = if ($code -cin @('platform-unavailable', 'architecture-mismatch', 'strict-inventory-failed',
        'native-typecheck-failed', 'native-compile-failed', 'Invalid synthetic fixture.')) { $code } else { 'preflight-or-native-failure' }
}
finally {
    $result.source_unchanged = $true
    foreach ($name in $result.source_sha256.Keys) {
        if ((Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $name)).Hash -cne $result.source_sha256[$name]) { $result.source_unchanged = $false }
    }
    if (-not $result.source_unchanged) { $result.status = 'unresolved' }
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $report))
    [IO.File]::WriteAllText($report, ($result | ConvertTo-Json -Depth 24), $utf8)
}
Write-Output $result.status
if ($result.status -cnotin @('portable-preflight-passed', 'completed-success-observed', 'completed-failure-observed')) { exit 1 }
