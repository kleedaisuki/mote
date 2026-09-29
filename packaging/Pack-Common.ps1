# Shared, intentionally small packaging primitives. Source this file from a platform packer.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-PackageInput {
    <# .SYNOPSIS Validates an existing Native AOT publish directory and output location. #>
    param(
        [Parameter(Mandatory)][string] $PublishDirectory,
        [Parameter(Mandatory)][string] $OutputDirectory,
        [Parameter(Mandatory)][string] $RuntimeIdentifier,
        [Parameter(Mandatory)][string] $ExpectedPrefix
    )

    if (-not $RuntimeIdentifier.StartsWith($ExpectedPrefix, [StringComparison]::Ordinal) -or
        $RuntimeIdentifier -notmatch '^(win|osx)-(x64|arm64)$') {
        throw "Unsupported runtime identifier for this packer: $RuntimeIdentifier"
    }

    $publish = (Resolve-Path -LiteralPath $PublishDirectory -ErrorAction Stop).ProviderPath.TrimEnd([IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath $publish -PathType Container)) {
        throw "Publish directory does not exist: $publish"
    }

    # Resolve a possibly not-yet-created output without changing the current directory.
    $output = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    $sep = [IO.Path]::DirectorySeparatorChar
    if ($output.Equals($publish, $comparison) -or
        $output.StartsWith("$publish$sep", $comparison) -or
        $publish.StartsWith("$output$sep", $comparison)) {
        throw 'Publish and output directories must not contain each other.'
    }

    New-Item -ItemType Directory -Path $output -Force | Out-Null
    return [pscustomobject]@{ Publish = $publish; Output = $output }
}

function Copy-PublishPayload {
    <# .SYNOPSIS Copies every runtime asset, excluding standalone debugger symbols. #>
    param([Parameter(Mandatory)][string] $Publish, [Parameter(Mandatory)][string] $Destination)

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $Publish -Force) {
        if ($item.Name -match '(?i)\.(pdb|dbg|dSYM)$') { continue }
        Copy-Item -LiteralPath $item.FullName -Destination $Destination -Recurse -Force
    }
}

function Write-PackageManifest {
    <# .SYNOPSIS Records a portable SHA-256 inventory without hashing the manifest itself. #>
    param(
        [Parameter(Mandatory)][string] $PackageRoot,
        [Parameter(Mandatory)][string] $ManifestPath,
        [Parameter(Mandatory)][string] $RuntimeIdentifier,
        [Parameter(Mandatory)][string] $Version,
        [Parameter(Mandatory)][string] $Executable,
        [Parameter(Mandatory)][string] $PackageKind
    )

    $files = @(Get-ChildItem -LiteralPath $PackageRoot -File -Recurse -Force |
        Where-Object { $_.FullName -ne $ManifestPath -and -not $_.LinkType } |
        Sort-Object FullName |
        ForEach-Object {
            [ordered]@{
                path = [IO.Path]::GetRelativePath($PackageRoot, $_.FullName).Replace('\', '/')
                bytes = $_.Length
                sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        })
    $manifest = [ordered]@{
        schemaVersion = 1
        product = 'mote'
        packageKind = $PackageKind
        runtimeIdentifier = $RuntimeIdentifier
        version = $Version
        executable = $Executable
        signing = 'unsigned-development-artifact'
        files = $files
    }
    $json = $manifest | ConvertTo-Json -Depth 6
    [IO.File]::WriteAllText($ManifestPath, $json + "`n", [Text.UTF8Encoding]::new($false))
}
