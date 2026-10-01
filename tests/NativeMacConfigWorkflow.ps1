# Verify config and telemetry placement in the published, lone macOS Native AOT executable.
# A real document is opened so tracing produces records; System Events sends Command-W
# to the exact child PID, allowing the app's normal telemetry shutdown to flush JSONL.
param(
    [Parameter(Mandatory)][string] $ExecutablePath,
    [Parameter(Mandatory)][ValidateSet('osx-x64', 'osx-arm64')][string] $RuntimeIdentifier,
    [string] $ReportPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsMacOS) { throw 'NativeMacConfigWorkflow requires macOS.' }

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$scratchRoot = [IO.Path]::GetFullPath((Join-Path $root '.temp/mac-config-runtime'))
$scratch = [IO.Path]::GetFullPath((Join-Path $scratchRoot ([guid]::NewGuid().ToString('N'))))
$inventoryRoot = [IO.Path]::GetFullPath((Join-Path $root '.cache/ci-inventory'))
if (-not $scratch.StartsWith($scratchRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal) -or
    -not $scratchRoot.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
    throw 'Native config scratch path escaped repository .temp.'
}
if (-not $ReportPath) { $ReportPath = Join-Path $inventoryRoot "$RuntimeIdentifier/mac-config-runtime.json" }
$report = [IO.Path]::GetFullPath($ReportPath)
if (-not $report.StartsWith($inventoryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
    throw 'Native config report path escaped repository .cache/ci-inventory.'
}
New-Item -ItemType Directory -Force -Path $scratch, (Split-Path $report) | Out-Null

$exe = [IO.Path]::GetFullPath($ExecutablePath)
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$id = [guid]::NewGuid().ToString('N')
$inputPath = Join-Path $scratch "private-path-$id.json"
$contentSentinel = "mote_private_content_$id"
[IO.File]::WriteAllText($inputPath, "{`"secret`":`"$contentSentinel`"}", $utf8)
$fileName = [IO.Path]::GetFileName($inputPath)
$stage = 'preflight'
$active = $null
$success = $false
$result = [ordered]@{
    status = 'not-run'
    stage = $stage
    runtime_identifier = $RuntimeIdentifier
    executable_sha256 = $null
    publish_items = @()
    cases = @()
    error = $null
    note = 'Hosted macOS result only; no Windows inference. Cache/data override destinations are not runtime-observable until they have writers.'
}

# Start one child with an explicit environment; no process-global setting leaks to later cases.
function Start-Native {
    param([string] $MoteHome, [string] $TraceMode, [string] $CaseName, [switch] $Smoke)
    $start = [Diagnostics.ProcessStartInfo]::new($exe)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.WorkingDirectory = $root
    $start.Environment['MOTE_HOME'] = $MoteHome
    $start.Environment['MOTE_TRACE'] = $TraceMode
    [void]$start.Environment.Remove('MOTE_TRACE_SUBDIR')
    [void]$start.ArgumentList.Add($(if ($Smoke) { '--smoke-gui' } else { $inputPath }))
    $child = [Diagnostics.Process]::Start($start)
    if (-not $child) { throw "Could not start native case $CaseName." }
    return $child
}

# Run a bounded AppleScript command from a repository-local source file.
function Invoke-CloseWindow {
    param([int] $TargetPid, [string] $CaseName)
    $script = Join-Path $scratch "$CaseName-close.applescript"
    $source = @"
tell application "System Events"
    set targetProcess to first process whose unix id is $TargetPid
    repeat with attempt from 1 to 80
        try
            if exists window 1 of targetProcess then
                if name of window 1 of targetProcess contains "$fileName" then
                    set frontmost of targetProcess to true
                    keystroke "w" using command down
                    return "close-sent"
                end if
            end if
        on error errMsg number errNum
            if errNum is -25211 or errNum is -1743 then error errMsg number errNum
        end try
        delay 0.1
    end repeat
    error "Native document window did not become available for Command-W"
end tell
"@
    [IO.File]::WriteAllText($script, $source, $utf8)
    $compiler = [Diagnostics.ProcessStartInfo]::new('/usr/bin/osacompile')
    $compiler.UseShellExecute = $false
    $compiler.RedirectStandardOutput = $true
    $compiler.RedirectStandardError = $true
    [void]$compiler.ArgumentList.Add('-o')
    [void]$compiler.ArgumentList.Add((Join-Path $scratch "$CaseName-close.scpt"))
    [void]$compiler.ArgumentList.Add($script)
    $compileProcess = [Diagnostics.Process]::Start($compiler)
    try {
        if (-not $compileProcess.WaitForExit(15000)) {
            $compileProcess.Kill()
            $compileProcess.WaitForExit()
            throw "AppleScript compile timed out in $CaseName."
        }
        $compileError = $compileProcess.StandardError.ReadToEnd().Trim()
        if ($compileProcess.ExitCode -ne 0) { throw "AppleScript compile failed in ${CaseName}: $compileError" }
    }
    finally { $compileProcess.Dispose() }

    $start = [Diagnostics.ProcessStartInfo]::new('/usr/bin/osascript')
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    [void]$start.ArgumentList.Add($script)
    $command = [Diagnostics.Process]::Start($start)
    try {
        if (-not $command.WaitForExit(15000)) {
            $command.Kill()
            $command.WaitForExit()
            throw "AppleScript close timed out in $CaseName."
        }
        $output = $command.StandardOutput.ReadToEnd().Trim()
        $errorText = $command.StandardError.ReadToEnd().Trim()
        if ($command.ExitCode -ne 0 -or $output -cne 'close-sent') {
            throw "AppleScript close failed in $CaseName (exit $($command.ExitCode), output '$output'): $errorText"
        }
    }
    finally { $command.Dispose() }
}

# Ensure the already-published lone binary neither gains a sidecar nor changes bytes.
function Assert-PublishUnchanged {
    $items = @([IO.Directory]::EnumerateFileSystemEntries([IO.Path]::GetDirectoryName($exe)) |
        ForEach-Object { [IO.Path]::GetFileName($_) } | Sort-Object)
    if ($items.Count -ne 1 -or $items[0] -cne 'mote') {
        throw "Publish directory is not a lone Mach-O: $($items -join ', ')"
    }
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($exe)))
    if ($hash -cne $result.executable_sha256) { throw 'Native executable changed during runtime-path audit.' }
}

# Inspect exactly the selected MOTE_HOME, parse every JSONL line, and reject content/path leakage.
function Assert-Home {
    param([string] $MoteHome, [string] $CaseName, [bool] $HasConfig, [string] $TraceSubdir)
    # An if expression unwraps zero or one PowerShell pipeline results. Keep
    # these as arrays even for an absent or single-file home under StrictMode.
    $files = @()
    if ([IO.Directory]::Exists($MoteHome)) {
        $files = @([IO.Directory]::EnumerateFiles($MoteHome, '*', [IO.SearchOption]::AllDirectories) |
            ForEach-Object { [IO.Path]::GetRelativePath($MoteHome, $_).Replace([IO.Path]::DirectorySeparatorChar, '/') } | Sort-Object)
    }
    $expectedCount = [int]$HasConfig + [int](-not [string]::IsNullOrEmpty($TraceSubdir))
    if ($files.Count -ne $expectedCount) {
        throw "$CaseName created unexpected MOTE_HOME files: $($files -join ', ')"
    }
    if ($HasConfig -and -not ($files -ccontains 'config.toml')) {
        throw "$CaseName lost or moved the user config file."
    }
    $directories = @()
    if ([IO.Directory]::Exists($MoteHome)) {
        $directories = @([IO.Directory]::EnumerateDirectories($MoteHome, '*', [IO.SearchOption]::AllDirectories) |
            ForEach-Object { [IO.Path]::GetRelativePath($MoteHome, $_).Replace([IO.Path]::DirectorySeparatorChar, '/') } | Sort-Object)
    }
    $expectedDirectories = @()
    if ($TraceSubdir) { $expectedDirectories = @($TraceSubdir) }
    if ($directories.Count -ne $expectedDirectories.Count -or
        ($directories.Count -eq 1 -and $directories[0] -cne $expectedDirectories[0])) {
        throw "$CaseName created unexpected MOTE_HOME directories: $($directories -join ', ')"
    }
    if ([string]::IsNullOrEmpty($TraceSubdir)) {
        return [ordered]@{ files = $files; trace_bytes = 0; trace_records = 0; trace_operations = @() }
    }
    $tracePrefix = "$TraceSubdir/mote-trace-"
    $traceRel = @($files | Where-Object { $_.StartsWith($tracePrefix, [StringComparison]::Ordinal) -and $_.EndsWith('.jsonl', [StringComparison]::Ordinal) })
    if ($traceRel.Count -ne 1) { throw "$CaseName did not place exactly one JSONL in '$TraceSubdir'." }
    $trace = Join-Path $MoteHome $traceRel[0]
    $raw = $utf8.GetString([IO.File]::ReadAllBytes($trace))
    if (-not $raw.EndsWith("`n", [StringComparison]::Ordinal)) { throw "$CaseName JSONL has an incomplete final record." }
    foreach ($private in @($contentSentinel, $fileName, $inputPath, $MoteHome)) {
        if ($raw.Contains($private, [StringComparison]::OrdinalIgnoreCase)) {
            throw "$CaseName JSONL leaked a document path, content, or home path."
        }
    }
    $records = @($raw.Split("`n", [StringSplitOptions]::RemoveEmptyEntries) |
        ForEach-Object { ConvertFrom-Json -InputObject $_ -AsHashtable })
    if ($records.Count -lt 1 -or @($records | Where-Object { $_.schema_version -ne 1 }).Count -gt 0) {
        throw "$CaseName JSONL is empty or has an unsupported schema."
    }
    $operations = @($records | ForEach-Object { [string]$_.operation })
    if (-not ($operations -ccontains 'document.open_to_editable')) {
        throw "$CaseName lacked a real document-open telemetry record."
    }
    return [ordered]@{
        files = $files
        trace_bytes = [IO.FileInfo]::new($trace).Length
        trace_records = $records.Count
        trace_operations = $operations
    }
}

# Exercise one independent configuration with a fresh home and an orderly app exit.
function Invoke-Case {
    param([string] $CaseName, [string] $ConfigText, [string] $TraceMode,
        [string] $ExpectedTraceSubdir, [switch] $Smoke)
    $moteHome = Join-Path $scratch "$CaseName-home"
    $hasConfig = -not [string]::IsNullOrEmpty($ConfigText)
    if ($hasConfig) {
        New-Item -ItemType Directory -Force -Path $moteHome | Out-Null
        [IO.File]::WriteAllText((Join-Path $moteHome 'config.toml'), $ConfigText, $utf8)
    }
    $child = $null
    try {
        $child = Start-Native $moteHome $TraceMode $CaseName -Smoke:$Smoke
        if ($Smoke) {
            if (-not $child.WaitForExit(20000)) { throw "$CaseName smoke GUI timed out." }
            $stdout = $child.StandardOutput.ReadToEnd().Trim()
            $stderr = $child.StandardError.ReadToEnd().Trim()
            if ($stdout -cne 'mote-native-gui-ready' -or $child.ExitCode -ne 0) {
                throw "$CaseName smoke GUI failed (exit $($child.ExitCode), stdout '$stdout'): $stderr"
            }
        }
        else {
            if ($ExpectedTraceSubdir) {
                $deadline = [DateTime]::UtcNow.AddSeconds(12)
                do {
                    if ($child.HasExited) { throw "$CaseName editor exited before document trace could be produced." }
                    $traceFiles = @([IO.Directory]::Exists($moteHome) ?
                        [IO.Directory]::EnumerateFiles($moteHome, '*.jsonl', [IO.SearchOption]::AllDirectories) : @())
                    if ($traceFiles.Count -gt 0) { break }
                    Start-Sleep -Milliseconds 100
                } while ([DateTime]::UtcNow -lt $deadline)
                if ($traceFiles.Count -eq 0) { throw "$CaseName produced no document trace before close." }
            }
            else { Start-Sleep -Milliseconds 750 }
            Invoke-CloseWindow $child.Id $CaseName
            if (-not $child.WaitForExit(10000)) { throw "$CaseName did not exit after Command-W." }
            if ($child.ExitCode -ne 0) { throw "$CaseName editor exited $($child.ExitCode)." }
        }
        $homeResult = Assert-Home $moteHome $CaseName $hasConfig $ExpectedTraceSubdir
        Assert-PublishUnchanged
        return [ordered]@{
            name = $CaseName
            exit_code = $child.ExitCode
            home_created = [IO.Directory]::Exists($moteHome)
            home_files = $homeResult.files
            trace_bytes = $homeResult.trace_bytes
            trace_records = $homeResult.trace_records
            trace_operations = $homeResult.trace_operations
        }
    }
    finally {
        if ($child) {
            if (-not $child.HasExited) { $child.Kill(); $child.WaitForExit() }
            $child.Dispose()
        }
    }
}

try {
    if (-not [IO.File]::Exists($exe)) { throw "Published native executable not found: $exe" }
    if ([IO.Path]::GetFileName($exe) -cne 'mote') { throw 'Expected a lone macOS mote Mach-O, not a bundle or wrapper.' }
    $result.executable_sha256 = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($exe)))
    Assert-PublishUnchanged
    $result.publish_items = @('mote')

    $stage = 'default-off'
    $result.cases += Invoke-Case 'default-off' '' '0' '' -Smoke
    if ($result.cases[-1].home_created) { throw 'Default/off smoke created MOTE_HOME.' }

    $stage = 'disabled-config'
    $disabled = "[paths]`ntraces = `"trace-custom`"`n[telemetry]`nenabled = false`n"
    $result.cases += Invoke-Case 'disabled-config' $disabled '0' ''

    $stage = 'env-optin-default'
    $result.cases += Invoke-Case 'env-optin-default' '' '1' 'traces'

    $stage = 'config-optin-override'
    $enabled = "[paths]`ncache = `"cache-custom`"`ndata = `"data-custom`"`ntraces = `"trace-custom`"`n[telemetry]`nenabled = true`n"
    $result.cases += Invoke-Case 'config-optin-override' $enabled '0' 'trace-custom'

    $stage = 'malformed-config-fallback'
    $malformed = "[paths]`ntraces = `"trace-custom`"`n[telemetry]`nenabled = true`nenabled = false`n"
    $result.cases += Invoke-Case 'malformed-config-fallback' $malformed '1' 'traces'

    $result.status = 'passed'
    $success = $true
}
catch {
    $result.status = 'failed'
    $result.error = $_.Exception.Message
}
finally {
    $result.stage = $stage
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $report -Encoding utf8
    Get-Content -LiteralPath $report
    if ($success -and $scratch.StartsWith($scratchRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $scratch -Recurse -Force
    }
    elseif (-not $success) { Write-Warning "Retained failed macOS config fixtures at $scratch" }
}
if (-not $success) { throw "macOS native config workflow failed at ${stage}: $($result.error)" }
