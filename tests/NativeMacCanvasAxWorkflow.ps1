# Exercise the published AppKit canvas accessibility selector and lifecycle in-process.
# This is not an external AXUIElement client, VoiceOver, keyboard, or IME check.
param(
    [Parameter(Mandatory)][string] $ExecutablePath,
    [Parameter(Mandatory)][ValidateSet('osx-x64', 'osx-arm64')][string] $RuntimeIdentifier
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsMacOS) { throw 'Mac canvas AX probe requires macOS.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$temp = Join-Path $root '.temp'
$inventory = Join-Path $root ".cache/ci-inventory/$RuntimeIdentifier"
New-Item -ItemType Directory -Force $temp, $inventory | Out-Null
$id = [guid]::NewGuid().ToString('N')
$input = Join-Path $temp "canvas-ax-in-$id.txt"
$probeHome = Join-Path $temp "canvas-ax-home-$id"
$report = Join-Path $inventory 'mac-canvas-ax.json'
$metrics = Join-Path $inventory 'mac-canvas-ax-metrics.txt'
$stdoutPath = Join-Path $inventory 'mac-canvas-ax-stdout.txt'
$stderrPath = Join-Path $inventory 'mac-canvas-ax-stderr.txt'
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$result = [ordered]@{
    status = 'unverified'
    rid = $RuntimeIdentifier
    marker = ''
    fixture_utf16_units = $null
    marker_utf16_offset = $null
    input_sha256_unchanged = $false
    metrics_lines = 0
    error = ''
    scope = 'in-process-AppKit-canvas-AX-selector-lifecycle-not-external-AXUIElement-or-VoiceOver'
}

try {
    $exe = [IO.Path]::GetFullPath($ExecutablePath)
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Native executable absent: $exe" }
    $builder = [Text.StringBuilder]::new()
    [void]$builder.Append("HEAD`r`n")
    for ($i = 0; $i -lt 9000; $i++) { [void]$builder.AppendFormat("line-{0:D5}`n", $i) }
    [void]$builder.Append("LONE`rCR`nPAIR`r`nEMOJI-😀`nAX_OFFSCREEN_MARKER`n")
    $source = $builder.ToString()
    $offset = $source.IndexOf('AX_OFFSCREEN_MARKER', [StringComparison]::Ordinal)
    if ($offset -le 65536 -or $source.Length -ge 512 * 1024) { throw 'AX fixture does not meet probe bounds.' }
    [IO.File]::WriteAllText($input, $source, $utf8)
    $before = (Get-FileHash -LiteralPath $input -Algorithm SHA256).Hash
    $result.fixture_utf16_units = $source.Length
    $result.marker_utf16_offset = $offset

    $start = [Diagnostics.ProcessStartInfo]::new($exe)
    $start.WorkingDirectory = $root
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment['MOTE_HOME'] = $probeHome
    [void]$start.ArgumentList.Add('--check-native-mac-canvas-ax')
    [void]$start.ArgumentList.Add($input)
    $process = [Diagnostics.Process]::Start($start)
    try {
        if (-not $process.WaitForExit(120000)) {
            $process.Kill()
            $process.WaitForExit()
            throw 'In-process Mac canvas AX probe timed out after two minutes.'
        }
        $stdout = $process.StandardOutput.ReadToEnd().Trim()
        $stderr = $process.StandardError.ReadToEnd().Trim()
        [IO.File]::WriteAllText($stdoutPath, $stdout, $utf8)
        [IO.File]::WriteAllText($stderrPath, $stderr, $utf8)
        if ($process.ExitCode -ne 0) { throw "Mac canvas AX probe exited $($process.ExitCode): $stderr" }
        if ($stdout -cne 'mote-native-mac-canvas-ax-ready') {
            throw "Unexpected Mac canvas AX success marker: $stdout"
        }
        $result.marker = $stdout
    }
    finally { $process.Dispose() }

    $result.input_sha256_unchanged = (Get-FileHash -LiteralPath $input -Algorithm SHA256).Hash -ceq $before
    if (-not $result.input_sha256_unchanged) { throw 'Mac canvas AX probe mutated its input fixture.' }
    if (-not (Test-Path -LiteralPath $metrics -PathType Leaf)) { throw 'Mac canvas AX metrics were not written.' }
    $result.metrics_lines = [IO.File]::ReadAllLines($metrics).Length
    if ($result.metrics_lines -lt 1) { throw 'Mac canvas AX metrics are empty.' }
    $result.status = 'mac-canvas-ax-workflow-ok'
}
catch { $result.error = $_.Exception.Message }
finally {
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $report -Encoding utf8NoBOM
    Write-Host ($result | ConvertTo-Json -Depth 5 -Compress)
}
if ($result.status -ne 'mac-canvas-ax-workflow-ok') { throw $result.error }
