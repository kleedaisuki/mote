# Portable owner/worker contracts and restoration fault injection. No registry API,
# Win32 call, editor launch or GUI worker is executed by these tests.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
function Read-ProbeAst([string] $Name) {
    $tokens=$null; $errors=$null
    $result=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $Name),[ref]$tokens,[ref]$errors)
    if ($errors.Count) { throw ($errors | Out-String) }
    return $result
}
$owner=Read-ProbeAst 'NativeWindowsCanvasThemeWorkflow.ps1'
$gui=Read-ProbeAst 'NativeWindowsCanvasThemeWorker.ps1'
# PowerShell variable names are case-insensitive; $home collides with readonly $HOME.
function Assert-NoReservedHomeWrite($Ast) {
 foreach ($assignment in $Ast.FindAll({param($n) $n -is [Management.Automation.Language.AssignmentStatementAst]},$true)) {
  foreach ($variable in $assignment.Left.FindAll({param($n) $n -is [Management.Automation.Language.VariableExpressionAst]},$true)) {
   $name=($variable.VariablePath.UserPath -split ':')[-1]
   if ([string]::Equals($name,'HOME',[StringComparison]::OrdinalIgnoreCase)) {
    throw 'Readonly PowerShell HOME must not be used as a synthetic configuration variable.'
   }
  }
 }
}
foreach ($probeAst in @($owner,$gui)) { Assert-NoReservedHomeWrite $probeAst }
foreach ($sample in @('$HOME = 1','$home = 1','$script:Home = 1')) {
 $tokens=$null;$errors=$null
 $sampleAst=[Management.Automation.Language.Parser]::ParseInput($sample,[ref]$tokens,[ref]$errors)
 $rejected=$false
 try { Assert-NoReservedHomeWrite $sampleAst } catch { $rejected=$true }
 if (-not $rejected) { throw 'Readonly HOME assignment was not rejected case-insensitively.' }
}
$readonlyRejected=$false
try { & ([scriptblock]::Create('$home = "synthetic-only"')) } catch { $readonlyRejected=$_.Exception.Message.Contains('HOME') }
if (-not $readonlyRejected) { throw 'Original hosted readonly-HOME defect did not reproduce locally.' }
# Execute only the actual safe Join-Path assignment, without fixtures/registry/processes.
$setup=@($owner.FindAll({param($n) $n -is [Management.Automation.Language.AssignmentStatementAst] -and $n.Left.Extent.Text -ceq '$syntheticHome'},$true))
if ($setup.Count -ne 1) { throw 'Expected one synthetic-home setup assignment.' }
$scratch=Join-Path (Join-Path $PSScriptRoot '..') '.temp/native-canvas-theme/contract-only'
. ([scriptblock]::Create($setup[0].Extent.Text))
if ($syntheticHome -cne (Join-Path $scratch 'home')) { throw 'Fixed synthetic home setup failed.' }
foreach ($ast in @($owner,$gui)) {
    $embedded=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.StringConstantExpressionAst] -and $n.Value.StartsWith('using System;')},$true))
    if ($embedded.Count -ne 1) { throw 'Expected exactly one native declaration body per role.' }
    Add-Type -TypeDefinition $embedded[0].Value
}
$ownerText=$owner.Extent.Text; $guiText=$gui.Extent.Text
foreach ($needle in @('Restore-ThemeRegistry','Invoke-RestoredThemeSession',"RUNNER_ENVIRONMENT -cne 'github-hosted'",'TimeoutMs = 30000', '$Worker.Kill($true)', 'OrdinalIgnoreCase', 'Assert-NoReparseAncestors', 'registry_restored=$true')) {
    if (-not $ownerText.Contains($needle)) { throw "Owner contract absent: $needle" }
}
foreach ($needle in @("::GetDlgItem(`$canvas, 301)",'$id -cne ''mote.source.document''', "source_version_status='unverified-no-public-external-version-contract'", "draw_callback_status='not-observed'", "physical_presentation_status='not-tested'", 'uint color = GetPixel(dc, rect.Width - 16, 64);')) {
    if (-not $guiText.Contains($needle)) { throw "Worker contract absent: $needle" }
}
if ($guiText -match 'Registry\]|RegistryValue|SetValue\(|CreateSubKey|Kill\(|Start-Process|0xFFFF(?![0-9A-Fa-f])' -or
    $ownerText.Contains('--legacy-page') -or $ownerText.Contains('--canvas-experimental') -or
    $ownerText.Contains('PrintWindow(') -or $ownerText.Contains('UIAutomationClient')) { throw 'Role separation failed.' }
if (-not $guiText.Contains('[int] $TimeoutMs = 15000') -or
    $guiText.IndexOf('return Read-CanvasReadinessAttempt') -gt $guiText.IndexOf('$sourceRange.Select()') -or
    $guiText.IndexOf('return Read-CanvasReadinessAttempt') -gt $guiText.IndexOf('::NotifyAppearance($window)')) {
 throw 'Readiness deadline or pre-input/pre-theme ordering changed.'
}
if ([regex]::Matches($guiText,'\$sourceRange\.Select\(\)').Count -ne 1 -or
    -not $guiText.Contains("if (`$Phase -ceq 'dark-before')")) { throw 'Exactly one source selection per session required.' }
# Duck-typed registry stand-ins contain no operating-system calls.
Add-Type -TypeDefinition @"
using System;
using Microsoft.Win32;
public class CanvasThemeFakeKey {
    public bool Exists=true;
    public bool HasValue=true;
    public object Value=123;
    public RegistryValueKind Kind=RegistryValueKind.DWord;
    public bool OtherData;
    public bool CorruptRestore;
    public int SetCount;
    public string[] GetValueNames() => HasValue ? (OtherData ? new[]{"AppsUseLightTheme","other"}:new[]{"AppsUseLightTheme"}) : (OtherData ? new[]{"other"}:Array.Empty<string>());
    public string[] GetSubKeyNames() => Array.Empty<string>();
    public RegistryValueKind GetValueKind(string name) => Kind;
    public object GetValue(string name,object fallback,RegistryValueOptions options) => Value;
    public void SetValue(string name,object value,RegistryValueKind kind) { SetCount++; HasValue=true; Value=CorruptRestore ? (object)"wrong" : value; Kind=kind; }
    public void DeleteValue(string name,bool error) { HasValue=false; }
    public void Dispose() { }
}
public class CanvasThemeFakeParent {
    public CanvasThemeFakeKey Key;
    public CanvasThemeFakeKey OpenSubKey(string name,bool writable=false) => Key.Exists ? Key : null;
    public void DeleteSubKey(string name,bool error) { Key.Exists=false; }
}
namespace System.Windows.Automation.Text { public enum TextPatternRangeEndpoint { Start, End } }
public class CanvasThemeSyntheticRange {
    public string Text; public int Start; public int End;
    public string GetText(int max) => Text;
    public int CompareEndpoints(System.Windows.Automation.Text.TextPatternRangeEndpoint endpoint,
        CanvasThemeSyntheticRange other,System.Windows.Automation.Text.TextPatternRangeEndpoint otherEndpoint) =>
        (endpoint==System.Windows.Automation.Text.TextPatternRangeEndpoint.Start?Start:End)-
        (otherEndpoint==System.Windows.Automation.Text.TextPatternRangeEndpoint.Start?other.Start:other.End);
}
public class CanvasThemeSyntheticPattern {
    public CanvasThemeSyntheticRange DocumentRange;
    public CanvasThemeSyntheticRange[] Selected;
    public CanvasThemeSyntheticRange[] GetSelection() => Selected;
}
"@
foreach ($name in @('Restore-ThemeRegistry','Invoke-RestoredThemeSession','Wait-ThemeWorker')) {
    $f=@($owner.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -ceq $name},$true))
    if ($f.Count -ne 1) { throw "Missing owner function $name" }
    . ([scriptblock]::Create($f[0].Extent.Text))
}
function New-FakeState([bool] $KeyExists=$true,[bool] $ValueExists=$true,[object] $Value=123,[Microsoft.Win32.RegistryValueKind] $Kind='DWord') {
    $key=[CanvasThemeFakeKey]::new(); $key.Value=999
    $parent=[CanvasThemeFakeParent]::new(); $parent.Key=$key
    return [pscustomobject]@{Key=$key; Parent=$parent;KeyExists=$KeyExists;ValueExists=$ValueExists;Value=$Value;Kind=$Kind;Touched=$true}
}
# Red demonstration: the old linear finally loses restore if hashing/cleanup throws.
$state=New-FakeState; $failed=$false
try { try { throw 'body fault' } finally { throw 'cleanup/hash fault'; Restore-ThemeRegistry $state } } catch { $failed=$true }
if (-not $failed -or $state.Parent.Key.SetCount -ne 0 -or $state.Parent.Key.Value -ne 999) { throw 'Old defect reproduction failed.' }
# Green: nested-finally owner still restores for body, cleanup, and combined faults.
$count=0
foreach ($bodyFails in @($false,$true)) {
    foreach ($cleanupFails in @($false,$true)) {
        $state=New-FakeState; $threw=$false
        try { Invoke-RestoredThemeSession $state { if ($bodyFails) { throw 'worker deadline/body fault' } } { if ($cleanupFails) { throw 'cleanup fault' } } }
        catch { $threw=$true }
        if ($threw -ne ($bodyFails -or $cleanupFails) -or $state.Parent.Key.Value -ne 123 -or $state.Parent.Key.Kind -ne 'DWord') { throw 'Nested-finally restoration failed.' }
        $count++
    }
}
foreach ($case in @(
    @{Kind='ExpandString';Value='%TEMP%/literal'}, @{Kind='Binary';Value=[byte[]]@(0,255,3)},
    @{Kind='MultiString';Value=[string[]]@('alpha','beta')}, @{Kind='QWord';Value=9223372036854770000L}
)) {
    $state=New-FakeState -Value $case.Value -Kind $case.Kind
    Restore-ThemeRegistry $state
    if ($state.Parent.Key.Kind -ne $case.Kind -or -not [Collections.StructuralComparisons]::StructuralEqualityComparer.Equals($state.Parent.Key.Value,$case.Value)) { throw 'Raw registry kind/data fidelity failed.' }
    Restore-ThemeRegistry $state # Retry must preserve original bytes/data.
    $count++
}
$state=New-FakeState -ValueExists $false; Restore-ThemeRegistry $state
if ($state.Parent.Key.HasValue -or -not $state.Parent.Key.Exists) { throw 'Existing-key absent-value restore failed.' }; $count++
$state=New-FakeState -KeyExists $false -ValueExists $false; Restore-ThemeRegistry $state; Restore-ThemeRegistry $state
if ($state.Parent.Key.Exists) { throw 'New empty key removal/retry failed.' }; $count++
$state=New-FakeState -KeyExists $false -ValueExists $false; $state.Key.OtherData=$true; $threw=$false
try { Restore-ThemeRegistry $state } catch { $threw=$true }
if (-not $threw -or -not $state.Parent.Key.Exists) { throw 'Unrelated new-key data must prevent deletion.' }; $count++
$state=New-FakeState; $state.Key.CorruptRestore=$true; $threw=$false
try { Restore-ThemeRegistry $state } catch { $threw=$true }
if (-not $threw) { throw 'Corrupted restore must fail verification.' }; $count++
# Real subprocess watchdog test: child only sleeps, no GUI/HKCU/file inspection.
$start=[Diagnostics.ProcessStartInfo]::new((Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })))
$start.UseShellExecute=$false;$start.CreateNoWindow=$true
foreach ($arg in @('-NoProfile','-Command','Start-Sleep -Seconds 300')) { [void]$start.ArgumentList.Add($arg) }
$child=[Diagnostics.Process]::Start($start)
$state=New-FakeState;$threw=$false
try {
 try { Invoke-RestoredThemeSession $state { Wait-ThemeWorker $child -TimeoutMs 150 } { } }
 catch { $threw=$true }
 if (-not $threw -or -not $child.HasExited -or $state.Parent.Key.Value -ne 123) { throw 'Real worker timeout did not kill child and restore fake registry.' }
} finally { if (-not $child.HasExited) { $child.Kill($true);[void]$child.WaitForExit(3000) };$child.Dispose() }
$count++
$f=@($gui.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -ceq 'Assert-SourceState'},$true))
. ([scriptblock]::Create($f[0].Extent.Text))
$expected=[CanvasThemeSyntheticRange]::new(); $expected.Start=1; $expected.End=3; $expected.Text='lp'
foreach ($case in @(
    @{Text="alpha`nbeta`n";Start=1;End=3;Selected='lp';Count=1;Accept=$true},
    @{Text="alpha`nbeta`n";Start=0;End=2;Selected='lp';Count=1;Accept=$false},
    @{Text="alpha`nbeta`n";Start=1;End=4;Selected='lp';Count=1;Accept=$false},
    @{Text="alpha`nbeta`n";Start=1;End=3;Selected='ph';Count=1;Accept=$false},
    @{Text="changed`nbeta`n";Start=1;End=3;Selected='lp';Count=1;Accept=$false},
    @{Text="alpha`nbeta`n";Start=1;End=3;Selected='lp';Count=0;Accept=$false},
    @{Text="alpha`nbeta`n";Start=1;End=3;Selected='lp';Count=2;Accept=$false}
)) {
    $doc=[CanvasThemeSyntheticRange]::new();$doc.Text=$case.Text
    $r=[CanvasThemeSyntheticRange]::new();$r.Text=$case.Selected;$r.Start=$case.Start;$r.End=$case.End
    $pattern=[CanvasThemeSyntheticPattern]::new();$pattern.DocumentRange=$doc
    $pattern.Selected=[CanvasThemeSyntheticRange[]]@(for ($i=0;$i -lt $case.Count;$i++) { $r })
    $accepted=$true
    try { Assert-SourceState $pattern $expected "alpha`nbeta`n" } catch { $accepted=$false }
    if ($accepted -ne $case.Accept) { throw 'Source range case misclassified.' }
}
# Source-readiness metadata must reveal failed conjuncts, never source values/IDs/messages.
foreach ($name in @('New-CanvasReadinessObservation','Resolve-DirectCanvasSource','Read-CanvasReadinessAttempt')) {
 $f=@($gui.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -ceq $name},$true))
 if ($f.Count -ne 1) { throw 'Missing readiness metadata helper.' }
 . ([scriptblock]::Create($f[0].Extent.Text))
}
$documentType=[pscustomobject]@{ProgrammaticName='ControlType.Document'}
$editType=[pscustomobject]@{ProgrammaticName='ControlType.Edit'}
$readinessCases=0
foreach ($case in @(
 @{Pid=77;Id='mote.source.document';Type=$documentType;Text="alpha`nbeta`n";Canvas=$true;Input=$true;Pattern=$true;Accept=$true;Class='expected-source';ReadText=$true},
 @{Pid=78;Id='PRIVATE_FOREIGN_ID';Type=$documentType;Text='PRIVATE_FOREIGN_TEXT';Canvas=$true;Input=$true;Pattern=$true;Accept=$false;Class='not-read';ReadText=$false},
 @{Pid=77;Id='PRIVATE_UNKNOWN_ID';Type=$documentType;Text='PRIVATE_UNKNOWN_TEXT';Canvas=$true;Input=$true;Pattern=$true;Accept=$false;Class='other';ReadText=$false},
 @{Pid=77;Id='';Type=$documentType;Text='PRIVATE_EMPTYID_TEXT';Canvas=$true;Input=$true;Pattern=$true;Accept=$false;Class='empty';ReadText=$false},
 @{Pid=77;Id='mote.source.document';Type=$editType;Text='PRIVATE_EDIT_TEXT';Canvas=$true;Input=$true;Pattern=$true;Accept=$false;Class='expected-source';ReadText=$false},
 @{Pid=77;Id='mote.source.document';Type=$documentType;Text="alpha`r`nbeta`r`n";Canvas=$true;Input=$true;Pattern=$true;Accept=$false;Class='expected-source';ReadText=$true},
 @{Pid=77;Id='mote.source.document';Type=$documentType;Text="alpha`rbeta`r";Canvas=$true;Input=$true;Pattern=$true;Accept=$false;Class='expected-source';ReadText=$true},
 @{Pid=77;Id='mote.source.document';Type=$documentType;Text='PRIVATE_WRONG_TEXT';Canvas=$true;Input=$true;Pattern=$true;Accept=$false;Class='expected-source';ReadText=$true},
 @{Pid=77;Id='mote.source.document';Type=$documentType;Text="alpha`nbeta`n";Canvas=$false;Input=$true;Pattern=$true;Accept=$false;Class='expected-source';ReadText=$true},
 @{Pid=77;Id='mote.source.document';Type=$documentType;Text="alpha`nbeta`n";Canvas=$true;Input=$false;Pattern=$true;Accept=$false;Class='expected-source';ReadText=$true},
 @{Pid=77;Id='mote.source.document';Type=$documentType;Text='PRIVATE_PATTERN_TEXT';Canvas=$true;Input=$true;Pattern=$false;Accept=$false;Class='expected-source';ReadText=$false}
)) {
 $range=[CanvasThemeSyntheticRange]::new();$range.Text=$case.Text
 $pattern=[CanvasThemeSyntheticPattern]::new();$pattern.DocumentRange=$range
 $element=[pscustomobject]@{NextSibling=$null;Current=[pscustomobject]@{ProcessId=$case.Pid;AutomationId=$case.Id;ControlType=$case.Type};SyntheticPattern=$pattern;PatternAvailable=$case.Pattern;PatternCalls=0}
 $element | Add-Member ScriptMethod GetCurrentPattern {
  param($id)
  $this.PatternCalls++
  if (-not $this.PatternAvailable) { throw [InvalidOperationException]::new('PRIVATE_EXCEPTION_SOURCE') }
  return $this.SyntheticPattern
 }
 $canvasRoot=[pscustomobject]@{Current=[pscustomobject]@{ProcessId=77;AutomationId='';ControlType=[pscustomobject]@{ProgrammaticName='ControlType.Pane'}};FirstChild=$element}
 $o=New-CanvasReadinessObservation 3 125
 $accepted=$false;$threw=$false
 try { $accepted=Read-CanvasReadinessAttempt $o { $canvasRoot } { @{Canvas=$case.Canvas;Input=$case.Input} } 77 $documentType 'portable-pattern' {param($rootElement) $rootElement.FirstChild} {param($childElement) $childElement.NextSibling} }
 catch { $threw=$true }
 if ($accepted -ne $case.Accept -or $o.automation_id_class -cne $(if ($case.Pid -eq 77 -and $case.Id -ceq 'mote.source.document' -and $case.Type -eq $documentType) { 'expected-source' } else { 'not-read' }) -or $o.attempt_count -ne 3 -or $o.elapsed_ms -ne 125) { throw 'Readiness acceptance/classification changed.' }
 if ($o.root_control_type -cne 'ControlType.Pane' -or $o.root_automation_id_class -cne 'empty' -or $o.source_candidate_count -ne $(if ($case.Pid -eq 77 -and $case.Id -ceq 'mote.source.document' -and $case.Type -eq $documentType) { 1 } else { 0 })) { throw 'Root/source metadata mixed or wrong candidate count.' }
 if (($null -ne $o.bounded_text_utf16_units) -ne $case.ReadText) { throw 'Readiness text gate changed.' }
 if ($case.ReadText -and ($o.bounded_text_utf16_units -ne $case.Text.Length -or $o.exact_synthetic_lf -ne ($case.Text -ceq "alpha`nbeta`n") -or $o.exact_synthetic_crlf -ne ($case.Text -ceq "alpha`r`nbeta`r`n") -or $o.exact_synthetic_cr -ne ($case.Text -ceq "alpha`rbeta`r"))) { throw 'Readiness text metadata wrong.' }
 if ($threw -ne (-not $case.Pattern) -or ($threw -and ($o.error_stage -cne 'text-pattern' -or $null -eq $o.error_hresult -or $o.text_pattern_available -ne $false))) { throw 'Readiness API error provenance wrong.' }
 if ($o.canvas_initial_owner_verified -ne $true -or $o.input_initial_owner_verified -ne $true -or $o.Contains('canvas_owned_by_target') -or $o.canvas_visible -ne $case.Canvas -or $o.input_visible -ne $case.Input) { throw 'Visibility facts missing.' }
 $json=$o | ConvertTo-Json -Compress
 foreach ($secret in @('PRIVATE_',"alpha`nbeta`n","alpha`r`nbeta`r`n",'PRIVATE_EXCEPTION_SOURCE')) {
  if ($json.Contains($secret) -or $json.Contains(($secret | ConvertTo-Json -Compress).Trim('"'))) { throw 'Source/foreign identity/exception data leaked.' }
 }
 if (-not $case.ReadText -and $case.Pattern -and $element.PatternCalls -ne 0) { throw 'Pattern read before accepted identity.' }
 $readinessCases++
}
# Direct-child topology contract: missing/duplicate/foreign/deep/root fallback are rejected.
$topologyCases=0
foreach ($shape in @('none','duplicate','foreign','foreign-root','grandchild','root-document','overflow')) {
 $rootCurrent=[pscustomobject]@{ProcessId=77;AutomationId='';ControlType=[pscustomobject]@{ProgrammaticName='ControlType.Pane'}}
 $root=[pscustomobject]@{Current=$rootCurrent;FirstChild=$null}
 $first=[pscustomobject]@{Current=[pscustomobject]@{ProcessId=77;AutomationId='mote.source.document';ControlType=$documentType};NextSibling=$null}
 switch ($shape) {
  'duplicate' { $root.FirstChild=$first;$first.NextSibling=[pscustomobject]@{Current=$first.Current;NextSibling=$null} }
  'foreign' {
   $foreignCurrent=[pscustomobject]@{ProcessId=78}
   $foreignCurrent | Add-Member ScriptProperty AutomationId { throw 'PRIVATE_FOREIGN_ID_MUST_NOT_BE_READ' }
   $foreignCurrent | Add-Member ScriptProperty ControlType { throw 'PRIVATE_FOREIGN_TYPE_MUST_NOT_BE_READ' }
   $first.Current=$foreignCurrent;$root.FirstChild=$first
  }
  'foreign-root' {
   $root.Current=[pscustomobject]@{ProcessId=78}
   $root.Current | Add-Member ScriptProperty AutomationId { throw 'PRIVATE_FOREIGN_ROOT_ID' }
   $root.Current | Add-Member ScriptProperty ControlType { throw 'PRIVATE_FOREIGN_ROOT_TYPE' }
   $root.FirstChild=$first
  }
  'grandchild' { $root.FirstChild=[pscustomobject]@{Current=$rootCurrent;NextSibling=$null;FirstChild=$first} }
  'root-document' { $root.Current=$first.Current }
  'overflow' {
   $previous=$null
   for ($i=0;$i -lt 33;$i++) {
    $node=[pscustomobject]@{Current=$rootCurrent;NextSibling=$null}
    if ($null -eq $previous) { $root.FirstChild=$node } else { $previous.NextSibling=$node }
    $previous=$node
   }
  }
 }
 $o=New-CanvasReadinessObservation 1 0;$accepted=$false;$threw=$false
 try { $accepted=Read-CanvasReadinessAttempt $o { $root } { @{Canvas=$true;Input=$true} } 77 $documentType 'portable-pattern' {param($r) $r.FirstChild} {param($c) $c.NextSibling} }
 catch { $threw=$true }
 if ($accepted -or ($threw -ne ($shape -ceq 'overflow'))) { throw "Direct-child shape misclassified: $shape" }
 if ($shape -ceq 'duplicate' -and $o.source_candidate_count -ne 2) { throw 'Duplicate candidates not counted.' }
 if ($shape -ceq 'foreign' -and (-not $o.foreign_child_seen -or $o.source_candidate_count -ne 0 -or $null -ne $o.error_hresult)) { throw 'Foreign identity was read or accepted.' }
 if ($shape -ceq 'foreign-root' -and ($o.root_provider_process_matches_target -ne $false -or $o.direct_children_visited -ne 0 -or $null -ne $o.error_hresult)) { throw 'Foreign root identity/subtree was inspected.' }
 if ($shape -ceq 'overflow' -and (-not $o.child_budget_exceeded -or $o.direct_children_visited -ne 32 -or $null -eq $o.error_hresult)) { throw 'Direct child budget was not enforced.' }
 $json=$o | ConvertTo-Json -Compress
 if ($json.Contains('PRIVATE_')) { throw 'Foreign child content leaked.' }
 $topologyCases++
}
# Every attempt starts fresh: unknown facts must not survive the previous provider.
$fresh=New-CanvasReadinessObservation 4 175
if ($null -ne $fresh.exact_synthetic_lf -or $fresh.automation_id_class -cne 'not-read' -or $null -ne $fresh.text_pattern_available) { throw 'Attempt metadata retained stale facts.' }
"PASS: owner/GUI AST and C# compile; role separation and readonly-HOME red/green setup; old finally red reproduction; $count restoration/cleanup/fidelity contracts; 7 source-range cases; $readinessCases content-free readiness cases; $topologyCases direct-child topology cases. No GUI/registry API executed."
