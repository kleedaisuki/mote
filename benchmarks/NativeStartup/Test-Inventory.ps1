# Validate the strict one-file preflight without launching a GUI process.
param(
    [Parameter(Mandatory)][string] $ExecutablePath,
    [switch] $CheckLaunchFailure
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$tempRoot = [IO.Path]::GetFullPath((Join-Path $root '.temp/native-startup-inventory-test'))
$startupScratchRoot = [IO.Path]::GetFullPath((Join-Path $root '.temp/native-startup'))
$scratch = [IO.Path]::GetFullPath((Join-Path $tempRoot ([guid]::NewGuid().ToString('N'))))
if (-not $scratch.StartsWith($tempRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) { throw 'Inventory test scratch escaped the repository.' }
$probe = Join-Path $PSScriptRoot 'Measure-WindowsOrdinary.ps1'
$copied = Join-Path $scratch 'mote.exe'
$hidden = Join-Path $scratch 'sidecar.dat'
$nested = Join-Path $scratch 'nested'

function Get-StartupScratchNames {
    if (-not (Test-Path -LiteralPath $startupScratchRoot)) { return @() }
    return @(
        Get-ChildItem -LiteralPath $startupScratchRoot -Directory -Force |
            Select-Object -ExpandProperty Name | Sort-Object
    )
}

$scratchBefore = @(Get-StartupScratchNames)

function Assert-Rejected {
    param([string] $Path)
    try {
        & $probe -ExecutablePath $Path -CheckInventoryOnly | Out-Null
        throw 'Invalid publish inventory was accepted.'
    }
    catch {
        if ($_.Exception.Message -notlike 'Publish directory must contain exactly one real file*') {
            throw
        }
    }
}

New-Item -ItemType Directory -Force -Path $scratch | Out-Null
try {
    Copy-Item -LiteralPath $ExecutablePath -Destination $copied
    $good = & $probe -ExecutablePath $copied -CheckInventoryOnly
    if ($good.inventory_entries -ne 1) { throw 'Single real executable was rejected.' }
    [IO.File]::WriteAllText($hidden, 'synthetic sidecar')
    [IO.File]::SetAttributes($hidden, [IO.FileAttributes]::Hidden)
    Assert-Rejected $copied
    Remove-Item -LiteralPath $hidden -Force
    New-Item -ItemType Directory -Path $nested | Out-Null
    Assert-Rejected $copied
    $scratchAfterInventory = @(Get-StartupScratchNames)
    if (@(Compare-Object $scratchBefore $scratchAfterInventory).Count -ne 0) {
        throw 'Inventory-only preflight leaked a native-startup GUID scratch directory.'
    }
    Remove-Item -LiteralPath $nested -Force
    $launchRow = 'not-requested'
    if ($CheckLaunchFailure) {
        # A malformed sole mote.exe must produce a bounded failed JSONL row,
        # without launching any GUI or leaving the generated 1 MiB fixture.
        Remove-Item -LiteralPath $copied -Force
        [IO.File]::WriteAllText($copied, 'not a PE image')
        try {
            & $probe -ExecutablePath $copied -SizeMiB 1 -Runs 1 | Out-Null
            throw 'Malformed executable unexpectedly launched.'
        }
        catch {
            if ($_.Exception.Message -notlike 'Startup sample 0 failed:*') { throw }
        }
        $report = Join-Path $root '.cache/native-startup/ordinary-windows.jsonl'
        $last = Get-Content -LiteralPath $report | Select-Object -Last 1 | ConvertFrom-Json
        if ($last.status -ne 'failed' -or $null -ne $last.process_create_return_ms -or
            [string]::IsNullOrWhiteSpace($last.error)) {
            throw 'Process.Start failure did not retain a classified JSONL row.'
        }
        $launchRow = 'failed-row-preserved'
    }
    [pscustomobject]@{ one_real_executable = 'accepted'; hidden_sidecar = 'rejected';
        nested_directory = 'rejected'; inventory_scratch_delta = 0;
        launch_failure = $launchRow;
        executable_sha256 = $good.executable_sha256 }
}
finally {
    # Delete only the exact generated leaves; never recurse through a computed path.
    foreach ($file in @($hidden, $copied)) {
        if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Force }
    }
    if (Test-Path -LiteralPath $nested) { Remove-Item -LiteralPath $nested -Force }
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Force }
}
