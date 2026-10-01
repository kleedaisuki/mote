# Reproduce the premature target-read observer bug with a controlled owned-file guard.
# This loads the production observer function by AST; no GUI or application mutation occurs.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'The guarded-file observer contract requires Windows.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$scratch = [IO.Path]::GetFullPath((Join-Path $root ('.temp/release-save-observer/' + [guid]::NewGuid().ToString('N'))))
if (-not $scratch.StartsWith((Join-Path $root '.temp') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Observer fixtures escaped repository .temp.'
}
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Invoke-NativeWindowsReleaseProduct.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'Product observer script did not parse.' }
$function = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -ceq 'Read-ReleaseSavedSemantics' }, $true)
if ($null -eq $function) { throw 'Production Save-completion observer was not found.' }
. ([scriptblock]::Create($function.Extent.Text))
$evidence = $scratch
New-Item -ItemType Directory -Force -Path (Join-Path $evidence 'home/traces') | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$target = Join-Path $scratch 'edited.csv'
$trace = Join-Path $evidence 'home/traces/process.jsonl'
$expected = "id,name`r`n1,edited中`r`n"
[IO.File]::WriteAllText($target, $expected, $utf8)
$session = '0123456789abcdef0123456789abcdef'
function Save-Trace([object[]] $Rows) {
    $text = ($Rows | ForEach-Object { $_ | ConvertTo-Json -Compress -Depth 4 }) -join "`n"
    [IO.File]::WriteAllText($trace, $text + "`n", $utf8)
}
function New-Row([string] $Operation, [long] $Version) {
    return @{operation=$Operation; status='success'; session_id=$session; attributes=@{version=$Version}}
}
$guard = [IO.File]::Open($target, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
$oldReadRejected = $false
$targetReads = 0
try {
    try { [void][IO.File]::ReadAllText($target) }
    catch [IO.IOException] { $oldReadRejected = $true }
    if (-not $oldReadRejected) { throw 'The controlled target guard did not reproduce the original sharing violation.' }
    Save-Trace @((New-Row 'document.save' 7))
    $completion = Read-ReleaseSavedSemantics -CompletionOnly
    if ($completion) { $targetReads++; [void][IO.File]::ReadAllText($target) }
    if ($completion -or $targetReads) { throw 'Engine persistence alone incorrectly admitted a guarded target read.' }
    Save-Trace @((New-Row 'document.save' 7), (New-Row 'save.completed' 7), (New-Row 'command.save' 8))
    if (Read-ReleaseSavedSemantics -CompletionOnly) { throw 'Mismatched command completion version was accepted.' }
}
finally { $guard.Dispose() }
Save-Trace @((New-Row 'document.save' 7), (New-Row 'save.completed' 7), (New-Row 'command.save' 7))
$completion = Read-ReleaseSavedSemantics -CompletionOnly
if ($null -eq $completion -or $completion.version -ne 7 -or $completion.session_id -cne $session) {
    throw 'Real completed identities were not transported exactly.'
}
$targetReads++
if ([IO.File]::ReadAllText($target) -cne $expected -or $targetReads -ne 1) { throw 'The post-completion read was not exact and single.' }
$report = @{status='passed'; old_guarded_read_rejected=$oldReadRejected; reads_before_completion=0;
    reads_after_completion=$targetReads; derived_version=$completion.version; gui_tested=$false}
[IO.File]::WriteAllText((Join-Path $scratch 'observer-result.json'), ($report | ConvertTo-Json), $utf8)
$report | ConvertTo-Json -Compress
