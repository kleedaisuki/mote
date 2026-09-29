# Verify configuration and local telemetry placement in a published Windows Native AOT binary.
# A real document is opened for trace-producing cases; PID-scoped WM_CLOSE gives mote
# an ordinary shutdown, so buffered JSONL is checked only after it has been flushed.
param(
    [Parameter(Mandatory)][string] $ExecutablePath,
    [Parameter(Mandatory)][ValidateSet('win-x64', 'win-arm64')][string] $RuntimeIdentifier,
    [string] $ReportPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'NativeWindowsConfigWorkflow requires Windows.' }

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$scratchRoot = [IO.Path]::GetFullPath((Join-Path $root '.temp/windows-config-runtime'))
$scratch = [IO.Path]::GetFullPath((Join-Path $scratchRoot ([guid]::NewGuid().ToString('N'))))
$inventoryRoot = [IO.Path]::GetFullPath((Join-Path $root '.cache/ci-inventory'))
if (-not $scratchRoot.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    -not $scratch.StartsWith($scratchRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Windows config fixture path escaped repository .temp.'
}
if (-not $ReportPath) { $ReportPath = Join-Path $inventoryRoot "$RuntimeIdentifier/windows-config-runtime.json" }
$report = [IO.Path]::GetFullPath($ReportPath)
if (-not $report.StartsWith($inventoryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Windows config report escaped repository .cache/ci-inventory.'
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
$success = $false
$result = [ordered]@{
    status = 'not-run'
    stage = $stage
    runtime_identifier = $RuntimeIdentifier
    executable_sha256 = $null
    publish_items = @()
    cases = @()
    error = $null
    note = 'Native AOT Windows runtime-path check; cache/data overrides have no runtime writer yet.'
}

# Call only the Win32 message APIs needed to close the exact child PID. Add-Type
# compiles in memory; fixture and report files stay in repository .temp/.cache.
$windowControl = @'
using System;
using System.Runtime.InteropServices;

namespace Mote.NativeTests
{
    /// <summary>Requests normal closure of top-level windows owned by one process.</summary>
    public static class WindowClose
    {
        private delegate bool EnumWindowsProc(IntPtr window, IntPtr context);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr context);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
        private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        /// <summary>Posts WM_CLOSE to all top-level windows for the specified PID.</summary>
        public static int CloseProcessWindows(int processId)
        {
            int posted = 0;
            EnumWindows((window, context) =>
            {
                GetWindowThreadProcessId(window, out uint owner);
                if (owner == (uint)processId && PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero))
                    posted++;
                return true;
            }, IntPtr.Zero);
            return posted;
        }
    }
}
'@
if (-not ('Mote.NativeTests.WindowClose' -as [type])) {
    Add-Type -TypeDefinition $windowControl -Language CSharp | Out-Null
}

# Start one child with explicit settings; process-global environment is unchanged.
function Start-Native {
    param([string] $MoteHome, [string] $TraceMode, [switch] $Smoke)
    $start = [Diagnostics.ProcessStartInfo]::new($exe)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.WorkingDirectory = $root
    $start.Environment['MOTE_HOME'] = $MoteHome
    $start.Environment['MOTE_TRACE'] = $TraceMode
    [void]$start.Environment.Remove('MOTE_TRACE_SUBDIR')
    [void]$start.ArgumentList.Add($(if ($Smoke) { '--smoke-gui' } else { $inputPath }))
    $child = [Diagnostics.Process]::Start($start)
    if (-not $child) { throw 'Could not start native editor.' }
    return $child
}

# Assert the published directory stays a lone EXE and its bytes stay identical.
function Assert-PublishUnchanged {
    $items = @([IO.Directory]::EnumerateFileSystemEntries([IO.Path]::GetDirectoryName($exe)) |
        ForEach-Object { [IO.Path]::GetFileName($_) } | Sort-Object)
    if ($items.Count -ne 1 -or $items[0] -cne 'mote.exe') {
        throw "Publish directory is not a lone executable: $($items -join ', ')"
    }
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($exe)))
    if ($hash -cne $result.executable_sha256) { throw 'Native executable changed during config workflow.' }
}

# Inspect exactly MOTE_HOME and reject unexpected files, directories, and private text.
function Assert-Home {
    param([string] $MoteHome, [string] $CaseName, [bool] $HasConfig, [string] $TraceSubdir)
    # Do not assign an if expression: PowerShell unwraps empty/one-element output
    # to null/scalar under StrictMode, making .Count unreliable.
    $files = @()
    $directories = @()
    if ([IO.Directory]::Exists($MoteHome)) {
        $files = @([IO.Directory]::EnumerateFiles($MoteHome, '*', [IO.SearchOption]::AllDirectories) |
            ForEach-Object { [IO.Path]::GetRelativePath($MoteHome, $_).Replace([IO.Path]::DirectorySeparatorChar, '/') } | Sort-Object)
        $directories = @([IO.Directory]::EnumerateDirectories($MoteHome, '*', [IO.SearchOption]::AllDirectories) |
            ForEach-Object { [IO.Path]::GetRelativePath($MoteHome, $_).Replace([IO.Path]::DirectorySeparatorChar, '/') } | Sort-Object)
    }
    $expectedFileCount = [int]$HasConfig + [int](-not [string]::IsNullOrEmpty($TraceSubdir))
    if ($files.Count -ne $expectedFileCount -or ($HasConfig -and -not ($files -ccontains 'config.toml'))) {
        throw "$CaseName created unexpected MOTE_HOME files: $($files -join ', ')"
    }
    $expectedDirectories = @()
    if ($TraceSubdir) { $expectedDirectories = @($TraceSubdir) }
    if ($directories.Count -ne $expectedDirectories.Count -or
        ($directories.Count -eq 1 -and $directories[0] -cne $expectedDirectories[0])) {
        throw "$CaseName created unexpected MOTE_HOME directories: $($directories -join ', ')"
    }
    if (-not $TraceSubdir) {
        return [ordered]@{ files = $files; trace_bytes = 0; trace_records = 0; operations = @() }
    }
    $tracePrefix = "$TraceSubdir/mote-trace-"
    $traceRel = @($files | Where-Object { $_.StartsWith($tracePrefix, [StringComparison]::Ordinal) -and $_.EndsWith('.jsonl', [StringComparison]::Ordinal) })
    if ($traceRel.Count -ne 1) { throw "$CaseName did not place exactly one JSONL in '$TraceSubdir'." }
    $trace = Join-Path $MoteHome $traceRel[0]
    $raw = $utf8.GetString([IO.File]::ReadAllBytes($trace))
    if (-not $raw.EndsWith("`n", [StringComparison]::Ordinal)) { throw "$CaseName JSONL has an incomplete final record." }
    foreach ($private in @($contentSentinel, $fileName, $inputPath, $MoteHome)) {
        if ($raw.Contains($private, [StringComparison]::OrdinalIgnoreCase)) {
            throw "$CaseName JSONL leaked document content or a fixture path."
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
        operations = $operations
    }
}

# Run one isolated config setting and wait for a normal, PID-scoped GUI close.
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
        $child = Start-Native $moteHome $TraceMode -Smoke:$Smoke
        if ($Smoke) {
            if (-not $child.WaitForExit(20000)) { throw "$CaseName smoke GUI timed out." }
            $stdout = $child.StandardOutput.ReadToEnd().Trim()
            $stderr = $child.StandardError.ReadToEnd().Trim()
            if ($child.ExitCode -ne 0 -or $stdout -cne 'mote-native-gui-ready') {
                throw "$CaseName smoke failed (exit $($child.ExitCode), stdout '$stdout'): $stderr"
            }
        }
        else {
            if ($ExpectedTraceSubdir) {
                $deadline = [DateTime]::UtcNow.AddSeconds(12)
                do {
                    if ($child.HasExited) { throw "$CaseName editor exited before a document trace was produced." }
                    $traceFiles = @([IO.Directory]::Exists($moteHome) ?
                        [IO.Directory]::EnumerateFiles($moteHome, '*.jsonl', [IO.SearchOption]::AllDirectories) : @())
                    if ($traceFiles.Count -gt 0) { break }
                    Start-Sleep -Milliseconds 100
                } while ([DateTime]::UtcNow -lt $deadline)
                if ($traceFiles.Count -eq 0) { throw "$CaseName produced no document trace before close." }
            }
            else { Start-Sleep -Milliseconds 750 }
            $posted = 0
            $deadline = [DateTime]::UtcNow.AddSeconds(8)
            do {
                $posted = [Mote.NativeTests.WindowClose]::CloseProcessWindows($child.Id)
                if ($posted -gt 0) { break }
                if ($child.HasExited) { throw "$CaseName editor exited before WM_CLOSE." }
                Start-Sleep -Milliseconds 100
            } while ([DateTime]::UtcNow -lt $deadline)
            if ($posted -eq 0) { throw "$CaseName had no closable top-level window." }
            if (-not $child.WaitForExit(10000)) { throw "$CaseName did not exit after WM_CLOSE." }
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
            trace_operations = $homeResult.operations
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
    if (-not [IO.File]::Exists($exe)) { throw "Published executable not found: $exe" }
    if ([IO.Path]::GetFileName($exe) -cne 'mote.exe') { throw 'Expected a lone published mote.exe.' }
    $result.executable_sha256 = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($exe)))
    Assert-PublishUnchanged
    $result.publish_items = @('mote.exe')

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
    if ($success -and $scratch.StartsWith($scratchRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $scratch -Recurse -Force
    }
    elseif (-not $success) { Write-Warning "Retained failed Windows config fixtures at $scratch" }
}
if (-not $success) { throw "Windows native config workflow failed at ${stage}: $($result.error)" }
