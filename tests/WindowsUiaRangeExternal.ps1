# Probe the ordinary Native AOT source Document without global desktop input.
param([string] $ExecutablePath = "$PSScriptRoot/../.cache/uia-range-aot/mote.exe", [ValidateRange(1, 120)][int] $TimeoutSeconds = 60)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Windows UI Automation requires Windows.' }
$root = [IO.Path]::GetFullPath("$PSScriptRoot/..")
$cache = Join-Path $root '.cache/windows-uia-range-external'
$scratch = Join-Path $root '.temp/windows-uia-range-external'
New-Item -ItemType Directory -Force $cache, $scratch | Out-Null
$exe = [IO.Path]::GetFullPath($ExecutablePath)
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Missing binary: $exe" }
$fixture = Join-Path $scratch 'range.txt'
[IO.File]::WriteAllText($fixture, "abcdef`nsecond line`n", [Text.UTF8Encoding]::new($false))
$report = Join-Path $cache 'report.json'
# Do not let a previous completed run masquerade as a blocked current client.
if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report }
$project = Join-Path $cache 'Client.csproj'
@'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><UseWPF>true</UseWPF><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>
'@ | Set-Content -LiteralPath $project
@'
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation;
using System.Windows.Automation.Text;
/// <summary>Target-PID-only external UIA contract probe.</summary>
internal static class Program
{
    [DllImport("user32.dll")] private static extern bool PostMessageW(nint window, uint message, nuint wparam, nint lparam);
    /// <summary>Persist observations before potentially blocking calls.</summary>
    private static void Write(string path, Dictionary<string, object?> report) => File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    /// <summary>Assert independently derived expected results.</summary>
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    [STAThread]
    private static int Main(string[] args)
    {
        var report = new Dictionary<string, object?> { ["schema"] = "mote-uia-range-external-v1", ["client_thread"] = Environment.CurrentManagedThreadId, ["client_apartment"] = Thread.CurrentThread.GetApartmentState().ToString(), ["passed"] = false };
        var pid = int.Parse(args[0]); var path = args[1];
        try
        {
            report["target_pid"] = pid; report["stage"] = "find-source"; Write(path, report);
            AutomationElement? source = null;
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed.TotalSeconds < 20 && source is null)
            {
                var windows = AutomationElement.RootElement.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, pid));
                foreach (AutomationElement window in windows)
                {
                    var matches = window.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "mote.source.document"));
                    Check(matches.Count <= 1, "Duplicate source Document in target window");
                    if (matches.Count == 1) { Check(source is null, "Duplicate source across target windows"); source = matches[0]; }
                }
                if (source is null) Thread.Sleep(100);
            }
            Check(source is not null, "Target source Document not found");
            Check(source!.Current.ControlType == ControlType.Document, "Source must expose Document control type");
            var text = (TextPattern)source!.GetCurrentPattern(TextPattern.Pattern);
            var original = text.DocumentRange; var before = original.GetText(-1);
            Check(before == "abcdef\nsecond line\n", "Full source differs from fixture");
            var range = original.Clone();
            range.MoveEndpointByRange(TextPatternRangeEndpoint.End, range, TextPatternRangeEndpoint.Start);
            Check(range.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, 3) == 3, "End move count");
            Check(range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, 1) == 1, "Start move count");
            report["start_offset"] = range.CompareEndpoints(TextPatternRangeEndpoint.Start, original, TextPatternRangeEndpoint.Start);
            report["end_offset"] = range.CompareEndpoints(TextPatternRangeEndpoint.End, original, TextPatternRangeEndpoint.Start);
            Check((int)report["start_offset"]! == 1 && (int)report["end_offset"]! == 3, "Exact endpoint distances must be 1 and 3");
            Check(range.GetText(-1) == "bc", "Range text must be bc");
            report["stage"] = "select"; Write(path, report);
            try { range.Select(); report["select_hresult"] = "0x00000000"; }
            catch (Exception error) { report["select_hresult"] = $"0x{error.HResult:X8}"; report["select_exception"] = error.GetType().FullName; throw; }
            Write(path, report);
            var selection = text.GetSelection();
            Check(selection.Length == 1 && selection[0].GetText(-1) == "bc", "Global selection text must be bc");
            Check(selection[0].CompareEndpoints(TextPatternRangeEndpoint.Start, original, TextPatternRangeEndpoint.Start) == 1 && selection[0].CompareEndpoints(TextPatternRangeEndpoint.End, original, TextPatternRangeEndpoint.Start) == 3, "Selection offsets must be 1/3");
            Check(original.GetText(-1) == before, "DocumentRange clone must remain independent and source unchanged");
            range.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, 1);
            Check(original.GetText(-1) == before && selection[0].GetText(-1) == "bc", "Range mutation must not mutate original or selection clone");
            report["stage"] = "normal-close"; Write(path, report);
            using var target = Process.GetProcessById(pid);
            target.Refresh(); Check(target.MainWindowHandle != 0, "Target window missing");
            Check(PostMessageW(target.MainWindowHandle, 0x0010, 0, 0), "Target-owned WM_CLOSE failed");
            Check(target.WaitForExit(10000) && target.ExitCode == 0, "Target must exit normally");
            report["target_exit"] = target.ExitCode; report["stage"] = "stale-range"; Write(path, report);
            try { range.GetText(-1); throw new InvalidOperationException("Closed target range remained readable"); }
            catch (ElementNotAvailableException error) { report["stale_hresult"] = $"0x{error.HResult:X8}"; }
            report["passed"] = true; report["stage"] = "complete"; return 0;
        }
        catch (Exception error) { report["error"] = error.ToString(); report["hresult"] = $"0x{error.HResult:X8}"; return 1; }
        finally { Write(path, report); }
    }
}
'@ | Set-Content -LiteralPath (Join-Path $cache 'Program.cs')
dotnet build $project -c Release --nologo | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'UIA client build failed.' }
$start = [Diagnostics.ProcessStartInfo]::new($exe)
$start.UseShellExecute = $false
$start.ArgumentList.Add($fixture)
$start.Environment['MOTE_HOME'] = Join-Path $scratch 'home'
$target = [Diagnostics.Process]::Start($start)
$client = $null
try {
    $clientStart = [Diagnostics.ProcessStartInfo]::new((Join-Path $cache 'bin/Release/net10.0-windows/Client.exe'))
    $clientStart.UseShellExecute = $false
    $clientStart.ArgumentList.Add([string]$target.Id)
    $clientStart.ArgumentList.Add($report)
    $client = [Diagnostics.Process]::Start($clientStart)
    if (-not $client.WaitForExit($TimeoutSeconds * 1000)) {
        $client.Kill($true)
        throw "UIA client exceeded independent ${TimeoutSeconds}s timeout; partial report: $report"
    }
    $exitCode = $client.ExitCode
    if ([IO.File]::ReadAllText($fixture) -cne "abcdef`nsecond line`n") { throw 'Fixture source changed.' }
    Get-Content -LiteralPath $report | Out-Host
    exit $exitCode
}
finally {
    # Own only the process launched above, never unrelated desktop windows.
    $forcedCleanup = $false
    if (-not $target.HasExited) {
        $target.CloseMainWindow() | Out-Null
        if (-not $target.WaitForExit(5000)) { $forcedCleanup = $true; $target.Kill($true); $target.WaitForExit() }
    }
    $observed = if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw | ConvertFrom-Json -AsHashtable } else { @{ passed = $false; stage = 'client-no-report' } }
    $observed['forced_cleanup'] = $forcedCleanup
    $observed['target_exit'] = $target.ExitCode
    $observed['source_file_unchanged'] = [IO.File]::ReadAllText($fixture) -ceq "abcdef`nsecond line`n"
    $observed['executable_sha256'] = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    $observed | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $report
    $target.Dispose()
    if ($null -ne $client) { $client.Dispose() }
}
