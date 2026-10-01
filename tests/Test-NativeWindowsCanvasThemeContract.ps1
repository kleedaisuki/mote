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
foreach ($ast in @($owner,$gui)) {
    $embedded=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.StringConstantExpressionAst] -and $n.Value.StartsWith('using System;')},$true))
    if ($embedded.Count -ne 1) { throw 'Expected exactly one native declaration body per role.' }
    Add-Type -TypeDefinition $embedded[0].Value
}
$ownerText=$owner.Extent.Text; $guiText=$gui.Extent.Text
foreach ($needle in @('Restore-ThemeRegistry','Invoke-RestoredThemeSession',"RUNNER_ENVIRONMENT -cne 'github-hosted'",'TimeoutMs = 30000', '$Worker.Kill($true)', 'OrdinalIgnoreCase', 'Assert-NoReparseAncestors', 'registry_restored=$true')) {
    if (-not $ownerText.Contains($needle)) { throw "Owner contract absent: $needle" }
}
foreach ($needle in @("::GetDlgItem(`$canvas, 301)","Current.AutomationId -cne 'mote.source.document'", "source_version_status='unverified-no-public-external-version-contract'", "draw_callback_status='not-observed'", "physical_presentation_status='not-tested'", 'uint color = GetPixel(dc, rect.Width - 16, 64);')) {
    if (-not $guiText.Contains($needle)) { throw "Worker contract absent: $needle" }
}
if ($guiText -match 'Registry\]|RegistryValue|SetValue\(|CreateSubKey|Kill\(|Start-Process|0xFFFF(?![0-9A-Fa-f])' -or
    $ownerText.Contains('--legacy-page') -or $ownerText.Contains('--canvas-experimental') -or
    $ownerText.Contains('PrintWindow(') -or $ownerText.Contains('UIAutomationClient')) { throw 'Role separation failed.' }
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
"PASS: owner/GUI AST and C# compile; role separation; old finally red reproduction; $count restoration/cleanup/fidelity contracts; 7 source-range cases. No GUI/registry API executed."
