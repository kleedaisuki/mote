# Validate content-free launch observations without starting mote or touching HKCU.
# Compiles native declarations but exercises only a synthetic metadata stub.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$path = Join-Path $PSScriptRoot 'NativeThemeWorkflow-Windows.ps1'
$text = [IO.File]::ReadAllText($path)
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw ($errors | Out-String) }
$embedded = $ast.FindAll({param($node) $node -is [Management.Automation.Language.StringConstantExpressionAst] -and $node.Value.StartsWith('using System;')}, $true)
if ($embedded.Count -ne 1) { throw 'Expected one native helper body.' }
Add-Type -TypeDefinition $embedded[0].Value
if ([regex]::Matches($text, 'SendMessage\(\$editor, 0x00B1,').Count -ne 1) { throw 'Selection attempts changed.' }
if ($text.IndexOf('$selection = [MoteThemeProbeNative]::Selection($editor)') -gt $text.IndexOf('$editorVisible =')) { throw 'Observation perturbs selection readback.' }
if ($text -notmatch "RUNNER_ENVIRONMENT -cne 'github-hosted'") { throw 'Hosted-runner guard missing.' }
$observation = $ast.FindAll({param($node) $node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -eq '$report.launch_observation' -and $node.Right.Extent.Text.Contains('readiness_phase')}, $true)
if ($observation.Count -ne 1) { throw 'Expected one content-free observation.' }
Add-Type -TypeDefinition @"
using System;
public static class ThemeObservationStub {
    public static string ClassName(IntPtr window) => "RICHEDIT50W";
}
"@
$body = $observation[0].Extent.Text.Replace('[MoteThemeProbeNative]', '[ThemeObservationStub]')
$report = @{launch_observation = $null}
$editor = [IntPtr]1
$titleReadyElapsedMs = 10; $selectionReadbackElapsedMs = 12
$selection = [ValueTuple[int,int]]::new(0,0)
foreach ($case in @(
    @{Visible=$true; Canvas=$false; Text="alpha`r`nbeta`r`n"; Mode='visible-legacy-editor'; Match='exact_synthetic_crlf'},
    @{Visible=$false; Canvas=$true; Text=''; Mode='canvas-with-hidden-legacy-editor'; Match=$null},
    @{Visible=$true; Canvas=$true; Text=('x' * 255); Mode='unclassified'; Match=$null}
)) {
    $editorVisible = $case.Visible; $canvasPresent = $case.Canvas; $sourceText = $case.Text
    & ([scriptblock]::Create($body))
    $result = $report.launch_observation
    if ($result.host_mode_observation -cne $case.Mode -or $result.selection_start -ne 0 -or $result.selection_end -ne 0) { throw 'Wrong selection/mode observation.' }
    if ($null -ne $case.Match -and -not $result[$case.Match]) { throw 'Synthetic sentinel mismatch.' }
    if ($result.bounded_text_utf16_units -ne $case.Text.Length -or $result.text_read_at_capacity -ne ($case.Text.Length -eq 255)) { throw 'Wrong bounded length.' }
    if ($case.Text.Length -gt 0 -and ($result | ConvertTo-Json -Compress).Contains($case.Text)) { throw 'Source content escaped metadata report.' }
}
'PASS: PowerShell AST; embedded C# compile; single-attempt ordering/runner guard; three metadata contracts; no source values serialized.'
