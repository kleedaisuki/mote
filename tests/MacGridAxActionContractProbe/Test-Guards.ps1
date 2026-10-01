# Portable assertions cover syntax, path refusal and the no-native-evidence preflight contract.
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$driver = Join-Path $PSScriptRoot 'Run.ps1'
$tokens = $null; $errors = $null
$null = [Management.Automation.Language.Parser]::ParseFile($driver, [ref]$tokens, [ref]$errors)
if ($errors.Count -ne 0) { throw 'PowerShell owner syntax invalid.' }
foreach ($rid in @('osx-x64', 'osx-arm64')) {
    $relative = ".cache/ci-inventory/preflight/mac-grid-action-contract-$rid.json"
    & $driver -RuntimeIdentifier $rid -ReportPath $relative -ValidateOnly | Out-Null
    $report = Get-Content -LiteralPath (Join-Path $root $relative) -Raw | ConvertFrom-Json
    if ($report.status -cne 'portable-path-preflight-passed' -or -not $report.source_unchanged -or
        $report.native_typecheck -or $report.compiled -or $report.owner_normal_exit -or $report.forced_cleanup -or
        $null -ne $report.server -or $null -ne $report.client -or
        $null -ne $report.owner_exit_code -or $null -ne $report.child_exit_code) {
        throw 'Portable path preflight invented native evidence.'
    }
}
foreach ($invalid in @('.temp/action-control-forbidden.json', '.cache/ci-inventory/../action-control-forbidden.json',
    '.cache/ci-inventory/action-control-forbidden.txt')) {
    $refused = $false
    try { & $driver -RuntimeIdentifier osx-x64 -ReportPath $invalid -ValidateOnly | Out-Null }
    catch { $refused = $_.Exception.Message -ceq 'Report must stay in repository .cache/ci-inventory with a JSON suffix.' }
    if (-not $refused -or (Test-Path -LiteralPath (Join-Path $root $invalid))) { throw 'Invalid report path was not refused before writes.' }
}
Write-Output 'mac-grid-action-contract-portable-guards-passed'
