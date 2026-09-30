# Exercise a freshly published strict one-file mote with a separate exact-PID AX client.
# The helper is test-only. No global keys, pasteboard, input-source or TCC mutation.
param(
    [Parameter(Mandatory)][string] $ExecutablePath,
    [Parameter(Mandatory)][ValidateSet('osx-x64', 'osx-arm64')][string] $RuntimeIdentifier,
    [Parameter(Mandatory)][string] $ReportPath,
    [switch] $ValidateFixtureOnly
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$scratchRoot = Join-Path $root '.temp/mac-grid-ax-external'
$scratch = [IO.Path]::GetFullPath((Join-Path $scratchRoot ([guid]::NewGuid().ToString('N'))))
$report = [IO.Path]::GetFullPath((Join-Path $root $ReportPath))
$reportRoot = [IO.Path]::GetFullPath((Join-Path $root '.cache/ci-inventory'))
if (-not $scratch.StartsWith($scratchRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal) -or
    -not $report.StartsWith($reportRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
    throw 'Synthetic artifacts must stay in repository .temp and .cache/ci-inventory.'
}
New-Item -ItemType Directory -Force $scratch, (Split-Path $report) | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$editor = $null
$probe = $null
$fixture = Join-Path $scratch 'grid-fixture.csv'
$result = [ordered]@{
    schema = 1
    status = 'probe-error'
    method = 'separate-process-AXUIElement-exact-PID'
    rid = $RuntimeIdentifier
    os = [Runtime.InteropServices.RuntimeInformation]::OSDescription
    host_architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    editor_pid = $null
    binary_sha256 = $null
    binary_bytes = $null
    helper_source_sha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'Probe.swift') -Algorithm SHA256).Hash
    driver_source_sha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
    strict_one_file_inventory = $null
    fixture_sha256 = $null
    fixture_utf16_units = $null
    fixture_relative_path = [IO.Path]::GetRelativePath($root, $fixture)
    fixture_records = 1100
    fixture_max_columns = 24
    input_sha256_unchanged = $null
    swift_typecheck_passed = $false
    swift_exit_code = $null
    swift_report = $null
    editor_normal_exit = $null
    editor_cleanup_forced = $false
    cleanup_error = ''
    error = ''
    scope = 'opt-in bounded Grid external AX API; not VoiceOver/IME/geometry/performance/release acceptance'
}
try {
    # No guessed header. Record 1 has a Complete empty field; record 2 is ragged.
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
    [IO.File]::WriteAllText($fixture, $source, $utf8)
    $result.fixture_sha256 = (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash
    $result.fixture_utf16_units = $source.Length
    $records = $source.Split("`n")
    if ($records.Length -ne 1100 -or $records[0].Split(',')[1] -ne '' -or
        $records[1].Split(',').Length -ne 1 -or $records[1000].Split(',')[16] -cne 'r01001c17' -or
        $records[1099].Split(',')[23] -cne 'r01100c24' -or $source.Length -gt 512KB) {
        throw 'Synthetic CSV oracle is invalid.'
    }
    if ($ValidateFixtureOnly) {
        # Portable preflight is deliberately not a native AX or strict inventory verdict.
        $result.status = 'fixture-preflight-passed'
    }
    else {
        if (-not $IsMacOS) { throw 'External Grid AX requires macOS; use ValidateFixtureOnly for portable fixture checks.' }
        $expectedArchitecture = if ($RuntimeIdentifier -eq 'osx-arm64') { 'Arm64' } else { 'X64' }
        if ($result.host_architecture -cne $expectedArchitecture) { throw 'Host process architecture does not match requested RID.' }
        $exe = [IO.Path]::GetFullPath((Join-Path $root $ExecutablePath))
        if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Published native executable absent.' }
        # Accept only an unpacked publish directory containing that exact one payload.
        $payloads = @(Get-ChildItem -LiteralPath (Split-Path $exe) -Recurse -File)
        if ($payloads.Count -ne 1 -or -not [string]::Equals($payloads[0].FullName, $exe, [StringComparison]::Ordinal)) {
            throw 'Published directory is not strict one-file inventory.'
        }
        $result.strict_one_file_inventory = $true
        $result.binary_sha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
        $result.binary_bytes = (Get-Item -LiteralPath $exe).Length
        $swift = Join-Path $PSScriptRoot 'Probe.swift'
        $typeLog = & /usr/bin/xcrun swiftc -typecheck $swift 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Swift typecheck failed: $($typeLog -join [Environment]::NewLine)" }
        $result.swift_typecheck_passed = $true
        $client = Join-Path $scratch 'grid-ax-client'
        $compileLog = & /usr/bin/xcrun swiftc -O $swift -o $client 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Swift compile failed: $($compileLog -join [Environment]::NewLine)" }
        $start = [Diagnostics.ProcessStartInfo]::new($exe)
        $start.WorkingDirectory = $root
        $start.UseShellExecute = $false
        $start.Environment['MOTE_HOME'] = Join-Path $scratch 'home'
        $start.Environment['MOTE_NATIVE_GRID_ACCESSIBILITY'] = '1'
        $start.Environment['MOTE_TRACE'] = '0'
        $start.Environment['MOTE_NATIVE_MAC_STAGE_TRACE'] = '0'
        # Ordinary source route. Only Grid AX registration is opt-in.
        [void]$start.ArgumentList.Add($fixture)
        $editor = [Diagnostics.Process]::Start($start)
        $result.editor_pid = $editor.Id
        $clientStart = [Diagnostics.ProcessStartInfo]::new($client)
        $clientStart.UseShellExecute = $false
        $clientStart.RedirectStandardOutput = $true
        $clientStart.RedirectStandardError = $true
        [void]$clientStart.ArgumentList.Add([string]$editor.Id)
        [void]$clientStart.ArgumentList.Add($fixture)
        $probe = [Diagnostics.Process]::Start($clientStart)
        # Read asynchronously to avoid pipe-capacity deadlock before WaitForExit.
        $stdoutTask = $probe.StandardOutput.ReadToEndAsync()
        $stderrTask = $probe.StandardError.ReadToEndAsync()
        if (-not $probe.WaitForExit(75000)) { throw 'External AX client watchdog expired.' }
        $result.swift_exit_code = $probe.ExitCode
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        if ($stderr.Trim().Length -ne 0) { $result.error = $stderr.Trim() }
        $result.swift_report = $stdout | ConvertFrom-Json -Depth 12
        $result.status = [string]$result.swift_report.status
        if ($result.status -cnotin @('passed', 'failed', 'external-accessibility-unavailable', 'probe-error')) {
            throw 'Unknown external-client status.'
        }
        if ($result.swift_report.editorPID -ne $editor.Id -or $result.swift_report.clientPID -ne $probe.Id -or
            $result.swift_report.editorPID -eq $result.swift_report.clientPID) { throw 'External-client PID evidence mismatch.' }
        if ($result.status -ceq 'passed') {
            if ($probe.ExitCode -ne 0 -or -not $result.swift_report.closedByProbe -or
                -not $result.swift_report.trusted -or $result.swift_report.phase -cne 'complete' -or
                @($result.swift_report.checks | Where-Object { -not $_.passed }).Count -ne 0) {
                throw 'External pass lacks completed successful checks and normal-close attempt.'
            }
            $result.editor_normal_exit = $editor.WaitForExit(10000) -and $editor.ExitCode -eq 0
            if (-not $result.editor_normal_exit) { $result.status = 'failed'; $result.error = 'Editor failed normal exit after AXClose.' }
        }
        elseif ($result.status -ceq 'external-accessibility-unavailable' -and
            ($probe.ExitCode -ne 0 -or $result.swift_report.trusted)) { throw 'Permission-unavailable classification inconsistent.' }
    }
}
catch { $result.status = 'probe-error'; $result.error = $_.Exception.Message }
finally {
    foreach ($child in @($probe, $editor)) {
        if ($null -eq $child) { continue }
        try {
            if (-not $child.HasExited) {
                if ($child -eq $editor) { $result.editor_cleanup_forced = $true }
                $child.Kill()
                if (-not $child.WaitForExit(10000)) { throw 'Owned child did not exit after cleanup.' }
            }
        }
        catch {
            # Cleanup races/errors cannot prevent the report and fixture audit.
            $result.cleanup_error = $_.Exception.GetType().Name
            $result.status = 'probe-error'
        }
        finally { $child.Dispose() }
    }
    try {
        if ($result.fixture_sha256) {
            $result.input_sha256_unchanged = [string]::Equals((Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash, $result.fixture_sha256, [StringComparison]::Ordinal)
            if (-not $result.input_sha256_unchanged) { $result.status = 'failed'; $result.error = 'Synthetic file bytes changed.' }
        }
    } catch { $result.status = 'probe-error'; $result.error = 'Fixture integrity evidence unavailable.' }
    $result | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $report -Encoding utf8NoBOM
    Get-Content -LiteralPath $report
}
if ($result.status -ceq 'external-accessibility-unavailable') { Write-Warning 'AX trust unavailable; environment-blocked, not passed.' }
elseif ($result.status -cnotin @('passed', 'fixture-preflight-passed')) { throw "External Grid AX diagnostic failed. See $report." }
