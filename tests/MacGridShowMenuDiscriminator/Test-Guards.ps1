# Portable fixture, containment, schema, and no-invented-native-evidence assertions.
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$driver = Join-Path $PSScriptRoot 'Run.ps1'
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($driver, [ref]$tokens, [ref]$errors)
if ($errors.Count -ne 0) { throw 'Owner script syntax invalid.' }
# Load validation functions without executing the native driver or creating native evidence.
foreach ($name in @('Assert-Keys', 'Assert-Integer', 'Assert-Boolean', 'Assert-ClientReport', 'Assert-ServerReport')) {
    $function = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq $name }, $true)
    Invoke-Expression $function.Extent.Text
}
$checks = 0
foreach ($rid in @('osx-x64', 'osx-arm64')) {
    $relative = ".cache/ci-inventory/preflight/mac-grid-showmenu-pair-$rid.json"
    & $driver -ExecutablePath 'src/Mote.Native/bin/Release/mote' -RuntimeIdentifier $rid -ReportPath $relative -ValidateOnly | Out-Null
    $value = Get-Content -LiteralPath (Join-Path $root $relative) -Raw | ConvertFrom-Json
    if ($value.status -cne 'portable-preflight-passed' -or -not $value.source_unchanged -or $value.compiled -or $value.native_typecheck -or
        $null -ne $value.binary_sha256 -or $null -ne $value.client_sha256 -or $null -ne $value.control_sha256 -or
        $null -ne $value.strict_one_file_inventory -or @($value.sessions).Count -ne 0) { throw 'Portable preflight invented native evidence.' }
    $checks++
}
foreach ($invalid in @('.temp/pair-forbidden.json', '.cache/ci-inventory/../pair-forbidden.json', '.cache/ci-inventory/pair-forbidden.txt')) {
    $refused = $false
    try { & $driver -ExecutablePath 'src/mote' -RuntimeIdentifier osx-x64 -ReportPath $invalid -ValidateOnly | Out-Null }
    catch { $refused = $_.Exception.Message -ceq 'Unsafe artifact or executable path.' }
    if (-not $refused -or (Test-Path -LiteralPath (Join-Path $root $invalid))) { throw 'Unsafe report path admitted.' }
    $checks++
}
$refused = $false
try { & $driver -ExecutablePath '../outside-mote' -RuntimeIdentifier osx-x64 -ValidateOnly | Out-Null }
catch { $refused = $_.Exception.Message -ceq 'Unsafe artifact or executable path.' }
if (-not $refused) { throw 'Unsafe executable path admitted.' }
$checks++
$baseline = @'
{"schema":"mote-grid-action-client-v1","status":"reply-observed","trusted":true,"ready":true,"owned_target":true,
"discovery":{"attempts":1,"nodes":3,"admissions":3},"actions":{"attempts":1,"names_error":0,"names_count":1,"advertised":true,"original_error":-25205,"begin_recorded":true,"end_recorded":true},
"identity":{"unique_current_table":true,"retained_equal_current":true,"parent_equal_discovered_group":true,"window_equal_discovered_window":true,"top_level_equal_window":true,"parent_cycle":false,"parent_reaches_window":true,"reciprocal_child_occurrences":1,"audit_admissions":8,"audit_exhausted":false},
"client_calls":[{"sequence":1,"phase":"prelude","operation":"pid","error":0},{"sequence":2,"phase":"prelude","operation":"show-menu","error":-25205},{"sequence":3,"phase":"post-reply","operation":"parent","error":0}],"cleanup":{"finish_after_reply":true,"finish_after_audit":true}}
'@
Assert-ClientReport ($baseline | ConvertFrom-Json)
$checks++
foreach ($mutation in @(
    { param($v) $v | Add-Member noteproperty foreign_text 'forbidden' },
    { param($v) $v.actions.attempts = 2 },
    { param($v) $v.actions.original_error = $null },
    { param($v) $v.client_calls[1].operation = 'keyboard' },
    { param($v) $v.client_calls[1].sequence = 1 },
    { param($v) $v.client_calls = @(1..12001 | ForEach-Object { $v.client_calls[0] }) },
    { param($v) $v.identity.audit_admissions = 129 },
    { param($v) $v.actions.begin_recorded = $false },
    { param($v) $v.trusted = 'true' },
    { param($v) $v.discovery.nodes = 1.5 }
)) {
    $value = $baseline | ConvertFrom-Json
    & $mutation $value
    $refused = $false
    try { Assert-ClientReport $value } catch { $refused = $_.Exception.Message -ceq 'report-schema-invalid' }
    if (-not $refused) { throw 'Invalid client schema admitted.' }
    $checks++
}
$serverBaseline = @'
{"schema":"mote-grid-action-server-v1","ready_published":true,"finish_consumed":true,"normal_shutdown":true,"dispatcher_observation_available":false,
"callback_entries":1,"off_main_entries":0,"entry_overflow":0,"lifetime_overflow":0,"requests":1,"dispatches":1,"opens":1,"closes":1,"detaches":1,
"action_entries":[{"on_main_thread":true,"owner_lookup":true,"receiver_equal_current_root":true,"attached":true,"installing":false,"frame_present":true,"ready_baseline_available":true,"attachment_equal_baseline":true,"epoch_equal_baseline":true,"queue_result":true,"method_return":true,"caught_exception":false}],
"lifetime":[{"phase":"queue","attachment_equal_baseline":true,"epoch_equal_baseline":true,"receiver_equal_current_root":true,"attached":true}],"server_calls":[]}
'@
Assert-ServerReport ($serverBaseline | ConvertFrom-Json)
$checks++
foreach ($mutation in @(
    { param($v) $v | Add-Member noteproperty source_content 'forbidden' },
    { param($v) $v.callback_entries = 2 },
    { param($v) $v.dispatcher_observation_available = $true },
    { param($v) $v.server_calls = @('forbidden') },
    { param($v) $v.action_entries = @(1..17 | ForEach-Object { $v.action_entries[0] }) },
    { param($v) $v.lifetime = @(1..17 | ForEach-Object { $v.lifetime[0] }) },
    { param($v) $v.lifetime[0].phase = 'save' },
    { param($v) $v.action_entries[0].owner_lookup = 'true' },
    { param($v) $v.entry_overflow = -1 }
)) {
    $value = $serverBaseline | ConvertFrom-Json
    & $mutation $value
    $refused = $false
    try { Assert-ServerReport $value } catch { $refused = $_.Exception.Message -ceq 'report-schema-invalid' }
    if (-not $refused) { throw 'Invalid server schema admitted.' }
    $checks++
}
Write-Output "mac-grid-showmenu-pair-portable-guards-passed checks=$checks"
