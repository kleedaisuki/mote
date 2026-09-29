# Build an isolated WPF UIA client and inspect a published one-file Windows canvas.
# The diagnostic intentionally returns nonzero for both behavioral failures and
# the known duplicate-Document release blocker; CI should use continue-on-error.
param(
    [Parameter(Mandatory)][string] $ExecutablePath,
    [Parameter(Mandatory)][string] $Rid,
    [string] $ReportPath,
    [switch] $FragmentExperiment
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'External UI Automation probing requires Windows.' }
if ($Rid -notin @('win-x64', 'win-arm64')) { throw "Unsupported Windows RID: $Rid" }

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$cache = [IO.Path]::GetFullPath((Join-Path $root ".cache/windows-ax-external/$Rid"))
$scratch = [IO.Path]::GetFullPath((Join-Path $root ".temp/windows-ax-external/$Rid"))
$defaultReport = if ($FragmentExperiment) { 'fragment-report.json' } else { 'report.json' }
$report = if ($ReportPath) { [IO.Path]::GetFullPath($ReportPath) } else { Join-Path $cache $defaultReport }
$cacheRoot = [IO.Path]::GetFullPath((Join-Path $root '.cache'))
if (-not $report.StartsWith($cacheRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw 'ReportPath must be inside the repository .cache directory.'
}
New-Item -ItemType Directory -Force -Path $cache, $scratch, (Split-Path -Parent $report) | Out-Null

$project = Join-Path $PSScriptRoot 'WindowsAxExternalProbe.csproj'
$exe = [IO.Path]::GetFullPath($ExecutablePath)
$output = Join-Path $cache 'bin'
$intermediate = Join-Path $cache 'obj'
try {
    dotnet build $project -c Release -p:BaseOutputPath="$output/" `
        -p:BaseIntermediateOutputPath="$intermediate/" --nologo
    if ($LASTEXITCODE -ne 0) { throw "Isolated WPF UIA probe build failed with $LASTEXITCODE." }
    $probe = Join-Path $output 'Release/net10.0-windows/WindowsAxExternalProbe.exe'
    if (-not (Test-Path -LiteralPath $probe -PathType Leaf)) { throw "Probe executable missing: $probe" }
    if ($FragmentExperiment) {
        & $probe $exe $scratch $report '--uia-fragment-experimental'
    }
    else {
        & $probe $exe $scratch $report
    }
    exit $LASTEXITCODE
}
catch {
    # Infrastructure failures must still produce an artifact. The C# probe writes
    # its own richer report for all observed product/client/UIA failures.
    if (-not (Test-Path -LiteralPath $report -PathType Leaf)) {
        [pscustomobject]@{
            schema = 'mote-windows-ax-external-v1'
            infrastructure_failure = $_.Exception.Message
            rid = $Rid
        } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $report -Encoding utf8
    }
    throw
}
