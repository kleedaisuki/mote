# Run a predeclared same-binary off/on series on one disposable hosted desktop.
# No local opt-in, adaptive retry, focus repair, or cross-host pooling is allowed.
param(
    [Parameter(Mandatory)][string] $ExecutablePath,
    [ValidateRange(1, 30)][int] $Pairs = 20
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Native nonzero statuses are retained in the immutable index before failure
# classification; they must not become an early PowerShell exception here.
$PSNativeCommandUseErrorActionPreference = $false
if (-not $IsWindows -or $env:GITHUB_ACTIONS -cne 'true' -or
    $env:RUNNER_OS -cne 'Windows' -or $env:RUNNER_ENVIRONMENT -cne 'github-hosted') {
    throw 'Paired tracing measurement requires a disposable GitHub-hosted Windows desktop.'
}
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$exe = [IO.Path]::GetFullPath($ExecutablePath)
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Published executable is absent.' }
$cache = [IO.Path]::GetFullPath((Join-Path $root '.cache'))
if (-not $exe.StartsWith($cache + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) { throw 'Paired published binary must be under repository .cache.' }
$series = Join-Path $root ('.cache/benchmarks/native-trace-pairs/' + [guid]::NewGuid().ToString('N'))

# Every existing ancestor is checked before any evidence write. There is no
# recursive cleanup here; the original driver owns and checks its fresh home.
foreach ($path in @($series, $exe)) {
    $cursor = $path
    while ($cursor) {
        if ((Test-Path -LiteralPath $cursor) -and
            ((Get-Item -LiteralPath $cursor).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'Paired artifact ancestry contains a reparse point.'
        }
        $parent = [IO.Path]::GetDirectoryName($cursor)
        if (-not $parent -or $parent -ceq $cursor) { break }
        $cursor = $parent
    }
}
$sha = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
New-Item -ItemType Directory -Path $series | Out-Null
$manifest = [ordered]@{
    schema_version = 1; planned_pairs = $Pairs; binary_sha256 = $sha
    fixture = 'many-1'; ordering = 'odd-off-on-even-on-off'
    qualification = 'all-declared-series-hosted-exact-foreground'
    powershell_version = $PSVersionTable.PSVersion.ToString()
    runner_arch = $env:RUNNER_ARCH; runner_image_version = $env:ImageVersion
    harness_sha256 = [ordered]@{}
}
foreach ($relative in @('benchmarks/NativePaintLatency/Measure-WindowsTracePairs.ps1',
    'benchmarks/NativePaintLatency/Measure-WindowsScreen.ps1',
    'benchmarks/NativePaintLatency/WindowsScreenObserver.cs',
    'benchmarks/NativePaintLatency/summarize_trace_pairs.py',
    'benchmarks/NativeCanvasGui/CanvasFixture.cs', 'benchmarks/NativeCanvasGui/Win32Probe.cs',
    'tests/causal_save_trace_reader.py', 'benchmarks/NativeAcceptance/acceptance.py')) {
    $manifest.harness_sha256[$relative] = (Get-FileHash -LiteralPath (Join-Path $root $relative) `
        -Algorithm SHA256).Hash.ToLowerInvariant()
}
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $series 'manifest.json'),
    ($manifest | ConvertTo-Json -Depth 6), $utf8)
$index = Join-Path $series 'index.jsonl'
$pwsh = (Get-Process -Id $PID).Path
$failed = $false
try {
    :Series for ($pair = 1; $pair -le $Pairs; $pair++) {
        $modes = if ($pair % 2) { @('off', 'on') } else { @('on', 'off') }
        foreach ($mode in $modes) {
            $name = "pair-$pair-$mode"
            $output = Join-Path $series $name
            # One fresh interpreter prevents Add-Type state from crossing samples.
            # Existing driver activation/foreground requirements are unchanged.
            & $pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Measure-WindowsScreen.ps1') `
                -ExecutablePath $exe -Cases many-1 -Repetitions 1 -TraceMode $mode `
                -ArtifactDirectory $output *> (Join-Path $series "$name-driver.log")
            $driverExit = $LASTEXITCODE
            $entry = [ordered]@{
                pair = $pair; mode = $mode; driver_exit_code = $driverExit
                report = [IO.Path]::GetRelativePath($root,
                    (Join-Path $output 'screen-observations.jsonl')).Replace('\', '/')
                binary_sha256_after = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
            }
            [IO.File]::AppendAllText($index, ($entry | ConvertTo-Json -Compress) + "`n", $utf8)
            # A pending half-pair is not failure. A failed sample or equivalence
            # mismatch stops here; never schedule a rescue or discard this entry.
            & python -B (Join-Path $PSScriptRoot 'summarize_trace_pairs.py') --series $series
            if ($LASTEXITCODE -ne 0) { $failed = $true; break Series }
        }
    }
}
finally {
    Write-Output "native-trace-pairs-artifacts $series"
}
if ($failed) { throw 'Paired tracing series did not qualify; retained summary owns the reason.' }
