# Run only in a dedicated disposable GitHub-hosted Mac job with a five-minute timeout.
# Reviewed hashes live in a separate committed manifest so approving them never changes these bytes.
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('osx-arm64', 'osx-x64')]
    [string]$RuntimeIdentifier
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Refuse before any filesystem/environment mutation or subprocess can receive permission.
if (!$IsMacOS -or $env:GITHUB_ACTIONS -cne 'true' -or $env:RUNNER_ENVIRONMENT -cne 'github-hosted') {
    throw 'Disposable GitHub-hosted macOS runner required'
}
foreach ($name in @('MOTE_DISPOSABLE_MAC_GRID_CLIPBOARD', 'MOTE_DISPOSABLE_GRID_CLIPBOARD')) {
    if (![string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($name))) {
        throw "Unexpected inherited clipboard opt-in: $name"
    }
}
foreach ($name in @('GITHUB_WORKSPACE', 'GITHUB_RUN_ID', 'GITHUB_RUN_ATTEMPT', 'GITHUB_SHA')) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
        throw "Required CI identity missing: $name"
    }
}
if ($env:GITHUB_SHA -cnotmatch '^[0-9a-f]{40}$' -or $env:GITHUB_RUN_ID -notmatch '^\d+$' -or
    $env:GITHUB_RUN_ATTEMPT -notmatch '^\d+$') { throw 'Malformed CI identity' }

# Check lexical paths and every existing ancestor, including dangling symbolic links.
function Assert-OrdinaryPath([string]$Path, [bool]$Directory, [bool]$AllowMissing = $false) {
    $current = [IO.Path]::GetFullPath($Path)
    $leaf = $true
    while ($null -ne $current) {
        $item = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
        if ($null -ne $item) {
            if ($null -ne $item.LinkTarget -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Redirected path rejected: $current"
            }
            if ($leaf -and $item.PSIsContainer -ne $Directory) { throw "Unexpected path kind: $current" }
            if (!$leaf -and !$item.PSIsContainer) { throw "Non-directory ancestor: $current" }
        } elseif (!$AllowMissing -or !$leaf) {
            # Missing ancestors are allowed only while planning a new scratch subtree.
            if (!$AllowMissing) { throw "Required ordinary path missing: $current" }
        }
        $leaf = $false
        $parent = [IO.Directory]::GetParent($current)
        $current = if ($null -eq $parent) { $null } else { $parent.FullName }
    }
}

$root = [IO.Path]::GetFullPath($env:GITHUB_WORKSPACE).TrimEnd('/')
$scriptRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('/')
if ($root -cne $scriptRoot -or [IO.Path]::GetFullPath((Get-Location).Path).TrimEnd('/') -cne $root) {
    throw 'Invocation and current directory must be the exact GitHub checkout'
}
Assert-OrdinaryPath $root $true
$sourceRelative = 'src/Mote.Native/Mac/MacCsvGridClipboardProbe.cs'
$scriptRelative = 'tests/Invoke-NativeMacCsvGridClipboardWorkflow.ps1'
$manifestRelative = 'docs/validation/native-mac-grid-clipboard-invocation.md'
foreach ($relative in @($sourceRelative, $scriptRelative, $manifestRelative, 'src/Mote.Native/Program.cs', 'mote.sln')) {
    Assert-OrdinaryPath (Join-Path $root $relative) $false
}
$checkout = & git rev-parse --show-toplevel
if ($LASTEXITCODE -ne 0 -or $checkout -cne $root) { throw 'Not the exact checkout root' }
$head = & git rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $head -cne $env:GITHUB_SHA) { throw 'Checkout differs from CI commit' }
& git diff --quiet HEAD -- .
if ($LASTEXITCODE -ne 0) { throw 'Tracked checkout differs from its exact commit' }
& git ls-files --error-unmatch -- $sourceRelative $scriptRelative $manifestRelative src/Mote.Native/Program.cs mote.sln | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Reviewed artifacts must be committed' }
$changes = @(& git status --porcelain --untracked-files=all -- src tests docs/validation/native-mac-grid-clipboard-invocation.md)
if ($LASTEXITCODE -ne 0 -or $changes.Count -ne 0) { throw 'Reviewed source/test/manifest checkout is not clean' }
$sourceHash = (Get-FileHash -LiteralPath (Join-Path $root $sourceRelative) -Algorithm SHA256).Hash
$reviewedSourceHashes = @(
    'D0E868D78B91D621EB06FEB80923C519E0305A7361761E0546EF88AEAB84E48B', # LF
    '9E4A5AEF6B39DB38B05396B068514E0F04331FEE59D63231FF5EF0524AB95DDC'  # CRLF
)
if ($sourceHash -cnotin $reviewedSourceHashes) { throw 'Probe differs from independently reviewed bytes' }

# Detached fixed pins are materialized ONLY after independent integration review.
# Pending/missing/duplicate/malformed entries fail closed, never approve current arbitrary bytes.
$manifest = [IO.File]::ReadAllText((Join-Path $root $manifestRelative))
$reviewedInvocationHashes = foreach ($ending in @('LF', 'CRLF')) {
    $matches = [regex]::Matches($manifest, "(?m)^Approved invocation $ending SHA256: ([A-F0-9]{64})\r?`$")
    if ($matches.Count -ne 1) { throw "Independent invocation $ending pin not approved" }
    $matches[0].Groups[1].Value
}
$invocationHash = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
if ($invocationHash -cnotin $reviewedInvocationHashes) { throw 'Invocation differs from approved bytes' }
$machine = & /usr/bin/uname -m
if ($LASTEXITCODE -ne 0) { throw 'Cannot determine native architecture' }
$expectedMachine = if ($RuntimeIdentifier -ceq 'osx-arm64') { 'arm64' } else { 'x86_64' }
if ($machine -cne $expectedMachine) { throw 'Native target does not match runner architecture' }

$directory = Join-Path $root '.cache/native-mac-grid-clipboard'
Assert-OrdinaryPath $directory $true $true
if (!$directory.StartsWith($root + '/', [StringComparison]::Ordinal)) { throw 'Scratch path escaped checkout' }
# First filesystem mutations occur after all admission checks and approved byte pins.
[IO.Directory]::CreateDirectory($directory) | Out-Null
Assert-OrdinaryPath $directory $true
$artifactNames = @('report.json', 'approval.txt', 'approval-run.txt', 'stdout.txt', 'stderr.txt', 'exit-code.txt')
foreach ($name in $artifactNames) {
    $path = Join-Path $directory $name
    Assert-OrdinaryPath $path $false $true
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
}
# GUID-isolated outputs/intermediates guarantee no cached binary can be selected.
$buildRoot = Join-Path $directory ('build-' + [Guid]::NewGuid().ToString('N'))
if (Test-Path -LiteralPath $buildRoot) { throw 'Fresh build path already exists' }
$publish = Join-Path $buildRoot 'publish'
dotnet publish src/Mote.Native/Mote.Native.csproj --configuration Release --runtime $RuntimeIdentifier `
    --self-contained true --output $publish -p:UseArtifactsOutput=true "-p:ArtifactsPath=$buildRoot/artifacts" `
    -p:PublishAot=true -p:ContinuousIntegrationBuild=true -p:TreatWarningsAsErrors=true `
    -p:DebugType=none -p:GenerateDocumentationFile=false -p:StripSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Fresh Native AOT publish failed' }
Assert-OrdinaryPath $publish $true
$payload = @(Get-ChildItem -LiteralPath $publish -Force)
if ($payload.Count -ne 1 -or $payload[0].Name -cne 'mote' -or $payload[0].PSIsContainer) {
    throw 'Publish must contain strictly one executable and no sidecars'
}
$binary = Join-Path $publish 'mote'
Assert-OrdinaryPath $binary $false
$imports = @(& /usr/bin/otool -L $binary)
if ($LASTEXITCODE -ne 0 -or $imports.Count -lt 2) { throw 'Native Mach-O import inspection failed' }
foreach ($line in $imports | Select-Object -Skip 1) {
    $dependency = ($line.Trim() -split '\s+')[0]
    if ($dependency -notmatch '^(/usr/lib/|/System/Library/)') { throw "Non-system native dependency: $dependency" }
}

$runKey = "$($env:GITHUB_RUN_ID):$($env:GITHUB_RUN_ATTEMPT):$($env:GITHUB_SHA)"
$process = $null
$stdoutTask = $null
$stderrTask = $null
$utf8 = [Text.UTF8Encoding]::new($false)
try {
    # Permission is created only after build/inventory and never enters this parent environment.
    [IO.File]::WriteAllText((Join-Path $directory 'approval.txt'), 'disposable-github-hosted-macos-only', $utf8)
    [IO.File]::WriteAllText((Join-Path $directory 'approval-run.txt'), $runKey, $utf8)
    $started = [DateTimeOffset]::UtcNow
    $info = [Diagnostics.ProcessStartInfo]::new($binary)
    $info.WorkingDirectory = $root
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.ArgumentList.Add('--check-native-mac-grid-clipboard')
    $info.ArgumentList.Add('actual')
    $info.Environment['MOTE_DISPOSABLE_MAC_GRID_CLIPBOARD'] = '1'
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    if (!$process.Start()) { throw 'Native clipboard subprocess did not start' }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    if (!$process.WaitForExit(110000)) {
        $process.Kill($true)
        $process.WaitForExit()
        throw 'Native clipboard subprocess exceeded external 110-second bound'
    }
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    [IO.File]::WriteAllText((Join-Path $directory 'stdout.txt'), $stdout, $utf8)
    [IO.File]::WriteAllText((Join-Path $directory 'stderr.txt'), $stderr, $utf8)
    [IO.File]::WriteAllText((Join-Path $directory 'exit-code.txt'), [string]$process.ExitCode, $utf8)
    if ($process.ExitCode -ne 0) { throw "Native clipboard subprocess failed: $($process.ExitCode)" }
    $marker = 'mote-native-mac-grid-clipboard-ready mode=actual; cases=6; desktop-input=not-tested'
    if (@(($stdout -split '\r?\n') | Where-Object { $_ -ceq $marker }).Count -ne 1) {
        throw 'Missing or duplicate exact actual-mode success marker'
    }
    $reportPath = Join-Path $directory 'report.json'
    Assert-OrdinaryPath $reportPath $false
    if ((Get-Item -LiteralPath $reportPath).Length -gt 131072) { throw 'Report exceeds bounded evidence size' }
    $report = [IO.File]::ReadAllText($reportPath) | ConvertFrom-Json
    # New PowerShell versions deserialize ISO dates automatically; preserve subsecond precision.
    $reportedAt = if ($report.utc -is [DateTime]) {
        [DateTimeOffset]::new($report.utc)
    } else {
        [DateTimeOffset]::Parse([string]$report.utc, [Globalization.CultureInfo]::InvariantCulture)
    }
    if ($report.status -cne 'passed' -or $report.nativeClipboard -isnot [bool] -or !$report.nativeClipboard -or
        $report.runKey -cne $runKey -or $report.sourceHash -cne $sourceHash -or
        $reportedAt -lt $started -or $reportedAt -gt [DateTimeOffset]::UtcNow) {
        throw 'Invalid, future-dated or stale actual clipboard report'
    }
    $cases = @('quoted-crlf', 'empty-final-row', 'missing-refusal', 'explicit-missing-padding', 'nul-refusal', 'over-cap-refusal')
    if (@($report.cases).Count -ne 6 -or @(Compare-Object $cases @($report.cases) -CaseSensitive).Count -ne 0) {
        throw 'Report lacks exactly the six reviewed case IDs'
    }
    Write-Host $marker
} finally {
    # Preserve bounded process evidence on failure; never leave permission markers behind.
    try {
        if ($null -ne $process -and $null -ne $stdoutTask) {
            if (!$process.HasExited) { $process.Kill($true); $process.WaitForExit() }
            [IO.File]::WriteAllText((Join-Path $directory 'stdout.txt'), $stdoutTask.GetAwaiter().GetResult(), $utf8)
            [IO.File]::WriteAllText((Join-Path $directory 'stderr.txt'), $stderrTask.GetAwaiter().GetResult(), $utf8)
            [IO.File]::WriteAllText((Join-Path $directory 'exit-code.txt'), [string]$process.ExitCode, $utf8)
        }
    } finally {
        foreach ($name in @('approval.txt', 'approval-run.txt')) {
            $path = Join-Path $directory $name
            Assert-OrdinaryPath $path $false $true
            if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
        }
        if ($null -ne $process) { $process.Dispose() }
    }
}
