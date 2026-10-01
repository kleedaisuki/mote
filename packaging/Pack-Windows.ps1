# Assemble a noncompliant, multi-file Windows diagnostic package for GUI smoke tests.
param(
    [Parameter(Mandatory)][string] $PublishDirectory,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [Parameter(Mandatory)][ValidateSet('win-x64', 'win-arm64')][string] $RuntimeIdentifier,
    [ValidatePattern('^\d+\.\d+\.\d+$')][string] $Version = '0.1.0'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Windows portable packages must be assembled on Windows.' }
. (Join-Path $PSScriptRoot 'Pack-Common.ps1')

$paths = Resolve-PackageInput $PublishDirectory $OutputDirectory $RuntimeIdentifier 'win-'
$sourceExecutable = Join-Path $paths.Publish 'Mote.Desktop.exe'
if (-not (Test-Path -LiteralPath $sourceExecutable -PathType Leaf)) {
    throw "Native AOT executable missing: $sourceExecutable"
}

$stem = "mote-$Version-$RuntimeIdentifier-unsigned"
$package = Join-Path $paths.Output $stem
$archive = Join-Path $paths.Output "$stem.zip"
if ((Test-Path -LiteralPath $package) -or (Test-Path -LiteralPath $archive)) {
    throw "Output already exists; choose a fresh output directory: $package"
}

Copy-PublishPayload $paths.Publish $package
$manifestPath = Join-Path $package 'package-manifest.json'
Write-PackageManifest $package $manifestPath $RuntimeIdentifier $Version 'Mote.Desktop.exe' 'windows-portable'
Compress-Archive -LiteralPath $package -DestinationPath $archive -CompressionLevel Optimal

Write-Host "Package: $package"
Write-Host "Archive: $archive"
Write-Host 'NONCOMPLIANT: unsigned multi-file diagnostic artifact, not a mote release.'
