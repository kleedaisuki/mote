# Independent HKCU/editor owner: UIA and PrintWindow run only in killable workers.
param(
 [Parameter(Mandatory)][string] $ExecutablePath,
 [Parameter(Mandatory)][ValidateSet('win-x64','win-arm64')][string] $RuntimeIdentifier
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if (-not $IsWindows -or $env:GITHUB_ACTIONS -cne 'true' -or $env:RUNNER_OS -cne 'Windows' -or $env:RUNNER_ENVIRONMENT -cne 'github-hosted') {
 throw 'Canvas theme owner may mutate HKCU only on a disposable hosted Windows runner.'
}
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$exe=[IO.Path]::GetFullPath($ExecutablePath)
$expectedExe=[IO.Path]::GetFullPath((Join-Path $root "src/Mote.Native/bin/Release/net10.0/$RuntimeIdentifier/publish/mote.exe"))
if (-not [string]::Equals($exe,$expectedExe,[StringComparison]::OrdinalIgnoreCase)) { throw 'Only exact repository/RID published executable permitted.' }
$runId=[guid]::NewGuid().ToString('N')
$scratch=Join-Path $root ".temp/native-canvas-theme/$runId"
$output=Join-Path $root ".cache/native-canvas-theme/$RuntimeIdentifier/$runId"
$reportPath=Join-Path $root ".cache/ci-inventory/$RuntimeIdentifier/native-canvas-theme.json"

# Reject junctions in every existing ancestor before synthetic writes/executable use.
function Assert-NoReparseAncestors([string] $Target) {
 $cursor=[IO.Path]::GetFullPath($Target)
 while ($cursor) {
  if ((Test-Path -LiteralPath $cursor) -and (((Get-Item -LiteralPath $cursor).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) { throw 'Canvas theme path has reparse-point ancestor.' }
  $parent=[IO.Path]::GetDirectoryName($cursor)
  if (-not $parent -or $parent -ceq $cursor) { break }
  $cursor=$parent
 }
}
# Restore raw kind/data/existence, independently fault-injection tested.
function Restore-ThemeRegistry($State) {
 if (-not $State.Touched) { return }
 if ($null -eq $State.Key) { $State.Key=$State.Parent.OpenSubKey('Personalize',$true) }
 if ($null -eq $State.Key) {
  if (-not $State.KeyExists) { return }
  throw 'Original Personalize key disappeared before restoration.'
 }
 if ($State.ValueExists) {
  $State.Key.SetValue('AppsUseLightTheme',$State.Value,$State.Kind)
  if ($State.Key.GetValueKind('AppsUseLightTheme') -ne $State.Kind -or -not [Collections.StructuralComparisons]::StructuralEqualityComparer.Equals($State.Key.GetValue('AppsUseLightTheme',$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames),$State.Value)) { throw 'Original registry kind/data restoration verification failed.' }
 } else {
  $State.Key.DeleteValue('AppsUseLightTheme',$false)
  if ($State.Key.GetValueNames() -contains 'AppsUseLightTheme') { throw 'New registry value remained.' }
 }
 $State.Key.Dispose();$State.Key=$null
 if (-not $State.KeyExists) {
  $check=$State.Parent.OpenSubKey('Personalize',$true)
  if ($null -ne $check) {
   try { if ($check.GetValueNames().Length -ne 0 -or $check.GetSubKeyNames().Length -ne 0) { throw 'Created key acquired unrelated data; refusing to delete it.' } } finally { $check.Dispose() }
   $State.Parent.DeleteSubKey('Personalize',$false)
  }
  $remaining=$State.Parent.OpenSubKey('Personalize')
  if ($null -ne $remaining) { $remaining.Dispose();throw 'Created registry key remained.' }
 }
}
# Restoration is outside every fallible cleanup/hash/report path.
function Invoke-RestoredThemeSession($State,[scriptblock] $Body,[scriptblock] $Cleanup) {
 try { & $Body } finally { try { & $Cleanup } finally { Restore-ThemeRegistry $State } }
}
# The owner never waits on UIA/PrintWindow: only this killable worker deadline.
function Wait-ThemeWorker($Worker, [int] $TimeoutMs = 30000) {
 if ($Worker.WaitForExit($TimeoutMs)) { return }
 $Worker.Kill($true)
 if (-not $Worker.WaitForExit(3000)) { throw 'GUI worker remained alive after forced cleanup.' }
 throw 'GUI worker deadline exceeded; restoration remains with owner.'
}
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;
/// <summary>Nonblocking enumeration and timeout-bounded owned-editor close only.</summary>
public static class MoteCanvasThemeOwnerNative {
 private delegate bool Callback(IntPtr window,IntPtr data);
 [DllImport("user32.dll")] private static extern bool EnumWindows(Callback callback,IntPtr data);
 [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr window,StringBuilder text,int capacity);
 [DllImport("user32.dll",EntryPoint="SendMessageTimeoutW")] private static extern IntPtr SendMessageTimeout(IntPtr window,uint msg,IntPtr w,IntPtr l,uint flags,uint timeout,out IntPtr result);
 /// <summary>Finds one exact process editor without reading foreign captions/documents.</summary>
 public static IntPtr Find(uint pid) {
  IntPtr found=IntPtr.Zero;
  EnumWindows((window,data)=>{ GetWindowThreadProcessId(window,out uint owner);if(owner!=pid)return true;
   var name=new StringBuilder(128);GetClassName(window,name,name.Capacity);
   if(name.ToString()!="MoteNativeEditorWindow")return true;found=window;return false;
  },IntPtr.Zero);return found;
 }
 /// <summary>Revalidates owner and bounds WM_CLOSE to one second.</summary>
 public static void Close(IntPtr window,uint pid) {
  GetWindowThreadProcessId(window,out uint owner);
  if(window==IntPtr.Zero || owner!=pid)throw new InvalidOperationException("Owned editor HWND absent.");
  if(SendMessageTimeout(window,0x0010,IntPtr.Zero,IntPtr.Zero,3,1000,out _)==IntPtr.Zero)throw new TimeoutException("Owned close deadline exceeded.");
 }
}
"@
foreach ($path in @($exe,$scratch,$output,$reportPath)) { Assert-NoReparseAncestors $path }
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Published executable absent.' }
New-Item -ItemType Directory -Force -Path $scratch,$output,(Split-Path $reportPath) | Out-Null
foreach ($path in @($scratch,$output,$reportPath)) { Assert-NoReparseAncestors $path }
$fixture=Join-Path $scratch 'theme.txt';$home=Join-Path $scratch 'home'
New-Item -ItemType Directory -Force -Path $home | Out-Null
[IO.File]::WriteAllText($fixture,"alpha`nbeta`n",[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $home 'config.toml'),"[appearance]`ntheme = 'system'`n",[Text.UTF8Encoding]::new($false))
$sourceHash=(Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash
$state=[pscustomobject]@{Parent=$null;Key=$null;KeyExists=$false;ValueExists=$false;Kind=$null;Value=$null;Touched=$false}
$report=[ordered]@{
 status='failed';stage='registry-snapshot';rid=$RuntimeIdentifier;error=$null
 executable_sha256=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
 source_mode='ordinary-continuous-canvas';cases=@();workers=@()
 registry_original_key_exists=$null;registry_original_value_exists=$null;registry_original_kind=$null
 registry_restored=$false;source_sha256_unchanged=$false;normal_exit=$false
 source_version_status='unverified-no-public-external-version-contract'
 draw_callback_status='not-observed';physical_presentation_status='not-tested'
 scope='bounded external Canvas source UIA/raster pilot; no immutable-engine-version, draw-callback, physical-display or IME acceptance'
}
$script:editor=$null;$script:worker=$null
try {
 Invoke-RestoredThemeSession $state {
  $state.Parent=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Themes',$true)
  if ($null -eq $state.Parent) { throw 'Themes parent absent; refusing to create it.' }
  $state.Key=$state.Parent.OpenSubKey('Personalize',$true);$state.KeyExists=$null -ne $state.Key
  if (-not $state.KeyExists) { $state.Touched=$true;$state.Key=$state.Parent.CreateSubKey('Personalize',$true) }
  if ($null -eq $state.Key) { throw 'Cannot open writable Personalize key.' }
  $state.ValueExists=$state.Key.GetValueNames() -contains 'AppsUseLightTheme'
  if ($state.ValueExists) {
   $state.Kind=$state.Key.GetValueKind('AppsUseLightTheme')
   if ($state.Kind -eq [Microsoft.Win32.RegistryValueKind]::Unknown) { throw 'Unknown original registry kind.' }
   $state.Value=$state.Key.GetValue('AppsUseLightTheme',$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
  }
  $report.registry_original_key_exists=$state.KeyExists;$report.registry_original_value_exists=$state.ValueExists
  $report.registry_original_kind=if ($state.ValueExists) { $state.Kind.ToString() } else { $null }
  $state.Touched=$true;$state.Key.SetValue('AppsUseLightTheme',0,[Microsoft.Win32.RegistryValueKind]::DWord)
  $start=[Diagnostics.ProcessStartInfo]::new($exe);$start.UseShellExecute=$false;$start.CreateNoWindow=$true
  $start.Environment['MOTE_HOME']=$home;$start.Environment['MOTE_TRACE']='0'
  [void]$start.ArgumentList.Add($fixture) # Ordinary route, no legacy/experimental switches.
  $script:editor=[Diagnostics.Process]::Start($start)
  if ($null -eq $script:editor) { throw 'Editor launch failed.' }
  foreach ($phase in @('dark-before','light','dark-after')) {
   $report.stage=$phase
   $state.Key.SetValue('AppsUseLightTheme',$(if ($phase -ceq 'light') { 1 } else { 0 }),[Microsoft.Win32.RegistryValueKind]::DWord)
   $script:worker=Start-Process -FilePath (Join-Path $PSHOME 'pwsh.exe') -WindowStyle Hidden -PassThru `
    -ArgumentList @('-NoProfile','-File',"`"$(Join-Path $PSScriptRoot 'NativeWindowsCanvasThemeWorker.ps1')`"",'-RuntimeIdentifier',$RuntimeIdentifier,'-TargetProcessId',$script:editor.Id,'-RunId',$runId,'-Phase',$phase) `
    -RedirectStandardOutput (Join-Path $output "$phase.stdout.txt") -RedirectStandardError (Join-Path $output "$phase.stderr.txt")
   try { Wait-ThemeWorker $script:worker }
   catch {
    $report.workers += @{phase=$phase;completed=$false;error=$_.Exception.Message}
    throw
   }
   $report.workers += @{phase=$phase;exit_code=$script:worker.ExitCode;completed=$true}
   if ($script:worker.ExitCode -ne 0) { throw "GUI worker failed in $phase (exit $($script:worker.ExitCode))." }
   $phaseReport=Get-Content -LiteralPath (Join-Path $output "$phase.json") -Raw | ConvertFrom-Json
   if ($phaseReport.status -cne 'passed' -or @($phaseReport.cases).Count -ne 1) { throw 'Worker report did not pass exact phase.' }
   $report.cases += $phaseReport.cases[0]
   $script:worker.Dispose();$script:worker=$null
  }
  if ($report.cases[0].png_sha256 -ceq $report.cases[1].png_sha256 -or $report.cases[1].png_sha256 -ceq $report.cases[2].png_sha256) { throw 'Dark and light rasters identical.' }
 } {
  try {
   if ($null -ne $script:worker) {
    try { if (-not $script:worker.HasExited) { $script:worker.Kill($true);[void]$script:worker.WaitForExit(3000) } }
    finally { $script:worker.Dispose();$script:worker=$null }
   }
  } finally {
   if ($null -ne $script:editor) {
    try {
     if (-not $script:editor.HasExited) {
      try { [MoteCanvasThemeOwnerNative]::Close([MoteCanvasThemeOwnerNative]::Find([uint32]$script:editor.Id),[uint32]$script:editor.Id) } catch { }
      if (-not $script:editor.WaitForExit(3000)) { $script:editor.Kill($true);[void]$script:editor.WaitForExit(3000) }
      else { $report.normal_exit=$script:editor.ExitCode -eq 0 }
     }
    } finally { $script:editor.Dispose();$script:editor=$null }
   }
  }
 }
 $report.registry_restored=$true
 $report.source_sha256_unchanged=(Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash -ceq $sourceHash
 if (-not $report.normal_exit -or -not $report.source_sha256_unchanged) { throw 'Normal exit or exact source hash failed.' }
 $report.status='passed'
} catch { $report.error=$report.stage+': '+$_.Exception.GetType().Name+': '+$_.Exception.Message }
finally {
 # Session restoration is already attempted before these fallible operations.
 try { Restore-ThemeRegistry $state;$report.registry_restored=$true }
 catch { $report.registry_restored=$false;$report.error='registry-restore: '+$_.Exception.Message }
 try { $report.source_sha256_unchanged=(Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash -ceq $sourceHash }
 catch { $report.source_sha256_unchanged=$false;$report.error='fixture-hash: '+$_.Exception.Message }
 try { if ($null -ne $state.Key) { $state.Key.Dispose() };if ($null -ne $state.Parent) { $state.Parent.Dispose() } }
 catch { $report.status='failed';$report.error='registry-handle-disposal: '+$_.Exception.Message }
 if (-not $report.registry_restored -or -not $report.normal_exit -or -not $report.source_sha256_unchanged -or $null -ne $report.error) { $report.status='failed' }
 $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding utf8NoBOM
 Write-Host ($report | ConvertTo-Json -Depth 8 -Compress)
}
if ($report.status -cne 'passed') { throw "Native Canvas theme workflow failed: $($report.error)" }
