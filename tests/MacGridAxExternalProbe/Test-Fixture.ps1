# Portable checks verify the driver syntax, deterministic CSV and artifact boundaries only.
# Native Swift typecheck and AX execution remain macOS target gates.
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$driver = Join-Path $PSScriptRoot 'Run.ps1'
$tokens = $null; $errors = $null
$null = [Management.Automation.Language.Parser]::ParseFile($driver, [ref]$tokens, [ref]$errors)
if ($errors.Count -ne 0) { throw 'Driver PowerShell syntax invalid.' }
$expectedSha = '8BACC6F97A374853C5F04EE176384738D8BFF7F71B1C7E768D088310C98DD8C2'
foreach ($rid in @('osx-x64', 'osx-arm64')) {
    $relative = ".cache/ci-inventory/preflight/mac-grid-ax-$rid.json"
    & $driver -ExecutablePath unused -RuntimeIdentifier $rid -ReportPath $relative -ValidateFixtureOnly | Out-Null
    $report = Get-Content -LiteralPath (Join-Path $root $relative) -Raw | ConvertFrom-Json
    if ($report.status -cne 'fixture-preflight-passed' -or $report.fixture_sha256 -cne $expectedSha -or
        $report.fixture_utf16_units -ne 263760 -or -not $report.input_sha256_unchanged -or
        $null -ne $report.strict_one_file_inventory -or $report.swift_typecheck_passed -or
        $null -ne $report.editor_pid -or $null -ne $report.swift_report) { throw 'Portable preflight overclaimed native evidence or changed fixture.' }
    $fixture = [IO.File]::ReadAllText((Join-Path $root $report.fixture_relative_path))
    $records = $fixture.Split("`n")
    if ($records.Length -ne 1100 -or $records[0].Split(',').Length -ne 24 -or
        $records[0].Split(',')[1] -cne '' -or $records[1].Split(',').Length -ne 1 -or
        $records[1000].Split(',')[16] -cne 'r01001c17' -or $records[1099].Split(',')[23] -cne 'r01100c24') {
        throw 'CSV semantics oracle failed.'
    }
}
$refused = $false
try { & $driver -ExecutablePath unused -RuntimeIdentifier osx-x64 -ReportPath .temp/forbidden.json -ValidateFixtureOnly | Out-Null }
catch { $refused = $_.Exception.Message -ceq 'Synthetic artifacts must stay in repository .temp and .cache/ci-inventory.' }
if (-not $refused -or (Test-Path -LiteralPath (Join-Path $root '.temp/forbidden.json'))) { throw 'Outside report-root path was not refused.' }
Write-Output 'mac-grid-ax-portable-fixture-and-path-checks-passed'
