<#
.SYNOPSIS
Publish current clean source to a new repo cache directory and pin its provenance.
.DESCRIPTION
No GUI is launched. This is deliberately separate from the diagnostic so the
executable hash can be reviewed before authorizing a desktop experiment.
#>
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Windows PowerShell 7 required.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$base = Join-Path $root '.cache/windows-save-diagnostic-publish'
$cursor = $base
while ($cursor.Length -ge $root.Length) {
    if (Test-Path -LiteralPath $cursor) {
        $item = Get-Item -LiteralPath $cursor -Force
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse ancestor rejected.' }
        $owner = (Get-Acl -LiteralPath $cursor).GetOwner([Security.Principal.SecurityIdentifier]).Value
        if ($owner -notin @($sid, 'S-1-5-18', 'S-1-5-32-544')) { throw 'Unexpected cache owner.' }
    }
    $cursor = [IO.Path]::GetDirectoryName($cursor)
}

# Fingerprint all tracked inputs, not just the files named in a project today.
function Get-Inputs {
    $result = @{}
    foreach ($file in @(git -C $root ls-files)) {
        $path = Join-Path $root $file
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'Tracked input missing.' }
        if ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Tracked reparse file rejected.' }
        $result[$file] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate tracked inputs.' }
    return $result
}
$dirty = @(git -C $root status --porcelain -- src global.json Directory.Build.props Directory.Build.targets NuGet.config)
if ($LASTEXITCODE -ne 0 -or $dirty.Count -ne 0) { throw 'Source/build inputs must be clean before publish.' }
$head = (git -C $root rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot pin HEAD.' }
$before = Get-Inputs
$sdk = (dotnet --version).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot pin SDK.' }
$dir = Join-Path $base ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dir | Out-Null
$output = Join-Path $dir 'publish'
$log = Join-Path $dir 'publish.log'
$build = Join-Path $dir 'build'
$started = [DateTimeOffset]::UtcNow
$args = @('publish', 'src/Mote.Native/Mote.Native.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
    '-p:PublishAot=true', '-p:DebugType=none', '-p:ContinuousIntegrationBuild=true',
    '-p:UseArtifactsOutput=true', "-p:ArtifactsPath=$build", '-warnaserror', '-o', $output)
Push-Location $root
try {
    & dotnet @args 2>&1 | Tee-Object -FilePath $log | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Native AOT publish failed; no manifest produced.' }
}
finally { Pop-Location }
$after = Get-Inputs
if ((git -C $root rev-parse HEAD).Trim() -cne $head -or $after.Count -ne $before.Count) { throw 'Checkout changed during publish.' }
foreach ($file in $before.Keys) { if ($after[$file] -cne $before[$file]) { throw 'Tracked inputs changed during publish.' } }
$dirty = @(git -C $root status --porcelain -- src global.json Directory.Build.props Directory.Build.targets NuGet.config)
if ($dirty.Count -ne 0) { throw 'Source changed during publish.' }
$inventory = @(Get-ChildItem -LiteralPath $output -Force)
if ($inventory.Count -ne 1 -or $inventory[0].Name -cne 'mote.exe' -or $inventory[0].PSIsContainer -or
    ($inventory[0].Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Publish inventory is not exactly one real mote.exe.' }
if ([DateTimeOffset]$inventory[0].LastWriteTimeUtc -lt $started.AddSeconds(-5)) { throw 'Publish executable is not freshly built; no manifest produced.' }
$assets = Get-Content -LiteralPath (Join-Path $build 'obj/Mote.Native/project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
$packs = @($assets.project.frameworks.Values | ForEach-Object { $_.downloadDependencies } |
    Where-Object { $_.name -ceq 'Microsoft.NETCore.App.Runtime.win-x64' })
if ($packs.Count -ne 1 -or $packs[0].version -notmatch '^\[([0-9.]+), \1\]$') { throw 'Cannot determine exact resolved runtime pack.' }
$packVersion = $Matches[1]
$exe = $inventory[0].FullName
$manifest = @{ schema = 'mote.windows-save-publish.v1'; head = $head; sdk = $sdk;
    runtime_pack = "Microsoft.NETCore.App.Runtime.win-x64/$packVersion";
    published_utc = $started.ToString('O'); completed_utc = [DateTimeOffset]::UtcNow.ToString('O');
    executable = [IO.Path]::GetRelativePath($root, $exe); sha256 = (Get-FileHash -LiteralPath $exe).Hash.ToLowerInvariant();
    bytes = $inventory[0].Length; inputs = $before; publish_log = [IO.Path]::GetRelativePath($root, $log);
    publish_log_sha256 = (Get-FileHash -LiteralPath $log).Hash.ToLowerInvariant();
    publish_command = 'dotnet publish src/Mote.Native/Mote.Native.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true -p:DebugType=none -p:ContinuousIntegrationBuild=true -p:UseArtifactsOutput=true -p:ArtifactsPath=<unique-repo-cache-build> -warnaserror' }
$manifestPath = Join-Path $dir 'publish-manifest.json'
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8
Write-Output $manifestPath
