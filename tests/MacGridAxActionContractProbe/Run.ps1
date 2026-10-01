# Compiles and runs an independent synthetic AppKit control, never the mote executable.
# Example: ./tests/MacGridAxActionContractProbe/Run.ps1 -RuntimeIdentifier osx-arm64
param(
    [Parameter(Mandatory)][ValidateSet('osx-x64', 'osx-arm64')][string]$RuntimeIdentifier,
    [string]$ReportPath,
    [switch]$ValidateOnly
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = ".cache/ci-inventory/mac-grid-action-contract-$RuntimeIdentifier.json"
}
$report = [IO.Path]::GetFullPath((Join-Path $root $ReportPath))
$reportRoot = [IO.Path]::GetFullPath((Join-Path $root '.cache/ci-inventory')) + [IO.Path]::DirectorySeparatorChar
if (-not $report.StartsWith($reportRoot, [StringComparison]::Ordinal) -or
    -not $report.EndsWith('.json', [StringComparison]::Ordinal)) {
    throw 'Report must stay in repository .cache/ci-inventory with a JSON suffix.'
}
$scratch = [IO.Path]::GetFullPath((Join-Path $root ".cache/mac-grid-action-contract/$RuntimeIdentifier"))
$source = Join-Path $PSScriptRoot 'Contract.m'
$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$result = [ordered]@{
    schema = 'mote-mac-grid-action-contract-v1'; runtime_identifier = $RuntimeIdentifier
    status = 'not-run'; source_sha256 = $sourceHash; source_unchanged = $false
    native_typecheck = $false; compiled = $false; owner_exit_code = $null; child_exit_code = $null
    owner_normal_exit = $false; forced_cleanup = $false; server = $null; client = $null
    error = ''; artifact_relative_path = [IO.Path]::GetRelativePath($root, $scratch)
}
$process = $null
try {
    if ($ValidateOnly) { $result.status = 'portable-path-preflight-passed' }
    else {
        if (-not $IsMacOS) { throw 'Native AppKit control requires macOS.' }
        $expected = if ($RuntimeIdentifier -ceq 'osx-x64') { 'X64' } else { 'Arm64' }
        if ([Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString() -cne $expected) {
            throw 'Runtime identifier does not match owner process architecture.'
        }
        [void][IO.Directory]::CreateDirectory($scratch)
        $native = Join-Path $scratch 'ax-action-control'
        $serverPath = Join-Path $scratch 'server.json'
        $clientPath = Join-Path $scratch 'client.json'
        # Remove only fixed stale reports under the verified synthetic scratch root.
        foreach ($old in @($serverPath, $clientPath)) {
            if (Test-Path -LiteralPath $old -PathType Leaf) { Remove-Item -LiteralPath $old }
        }
        $flags = @('-fobjc-arc', '-Wall', '-Wextra', '-Werror', '-Wno-deprecated-declarations')
        $typeOutput = & /usr/bin/xcrun clang @flags -fsyntax-only $source 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Native typecheck failed: $($typeOutput -join [Environment]::NewLine)" }
        $result.native_typecheck = $true
        $compileOutput = & /usr/bin/xcrun clang @flags -framework Cocoa -framework ApplicationServices $source -o $native 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Native compilation failed: $($compileOutput -join [Environment]::NewLine)" }
        $result.compiled = $true
        $start = [Diagnostics.ProcessStartInfo]::new($native)
        $start.WorkingDirectory = $root
        $start.UseShellExecute = $false
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        [void]$start.ArgumentList.Add('--server')
        [void]$start.ArgumentList.Add($scratch)
        $process = [Diagnostics.Process]::Start($start)
        $outputTask = $process.StandardOutput.ReadToEndAsync()
        $errorTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(45000)) { throw 'Synthetic control owner watchdog expired.' }
        $null = $outputTask.GetAwaiter().GetResult()
        $null = $errorTask.GetAwaiter().GetResult() # Native logs are not a desktop/source dump artifact.
        $result.owner_exit_code = $process.ExitCode
        if (-not (Test-Path -LiteralPath $serverPath -PathType Leaf) -or
            -not (Test-Path -LiteralPath $clientPath -PathType Leaf)) { throw 'Synthetic reports absent after normal owner exit.' }
        $result.server = Get-Content -LiteralPath $serverPath -Raw | ConvertFrom-Json -Depth 12
        $result.client = Get-Content -LiteralPath $clientPath -Raw | ConvertFrom-Json -Depth 12
        $result.owner_normal_exit = $result.server.normalShutdown -eq $true
        $result.child_exit_code = $result.server.childExit
        if ($result.client.status -ceq 'external-accessibility-unavailable' -and
            -not $result.client.trusted -and $process.ExitCode -eq 0 -and
            $result.owner_normal_exit -and $result.child_exit_code -eq 0) {
            $result.status = 'external-accessibility-unavailable'
        }
        elseif ($result.client.status -ceq 'control-completed' -and $result.client.trusted -and
            $process.ExitCode -eq 0 -and $result.server.childExit -eq 0 -and $result.server.normalShutdown -and
            @($result.server.metadata).Count -eq 7 -and @($result.server.effects).Count -eq 7 -and
            @($result.client.observations).Count -eq 7 -and
            @($result.client.observations | Where-Object { -not $_.oneMenuOpenedAndClosed -or -not $_.advertised }).Count -eq 0) {
            # Completion is evidence collection, NOT successful AX action replies or product acceptance.
            $result.status = 'control-completed'
        }
        else { $result.status = 'control-failed' }
    }
}
catch { $result.status = 'probe-error'; $result.error = $_.Exception.Message }
finally {
    if ($null -ne $process) {
        try {
            if (-not $process.HasExited) { $process.Kill($true); $null = $process.WaitForExit(5000); $result.forced_cleanup = $true }
        }
        finally { $process.Dispose() }
    }
    $result.source_unchanged = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ceq $sourceHash
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $report))
    [IO.File]::WriteAllText($report, ($result | ConvertTo-Json -Depth 14), [Text.UTF8Encoding]::new($false))
}
Write-Output ($result | ConvertTo-Json -Depth 14)
if ($result.status -cin @('control-completed', 'external-accessibility-unavailable', 'portable-path-preflight-passed') -and
    $result.source_unchanged -and -not $result.forced_cleanup) { exit 0 }
exit 1
