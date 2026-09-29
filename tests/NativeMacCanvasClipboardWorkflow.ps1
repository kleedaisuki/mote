# Probe published Native AOT AppKit canvas paste through the real NSPasteboard.
# The binary internally checks 40 Ki direct paste/Undo, explicit nonmutating
# rejection of a non-undoable 50 MiB paste, bounded host text, repeated-text
# selection, and Save/reopen. This is an
# in-process AppKit workflow, not an external keyboard or IME test.
param(
    [Parameter(Mandatory)][string] $ExecutablePath,
    [Parameter(Mandatory)][ValidateSet('osx-x64', 'osx-arm64')][string] $RuntimeIdentifier
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsMacOS) { throw 'Canvas clipboard probe requires macOS.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$temp = Join-Path $root '.temp'
$reportDir = Join-Path $root ".cache/ci-inventory/$RuntimeIdentifier"
New-Item -ItemType Directory -Force $temp, $reportDir | Out-Null
$id = [guid]::NewGuid().ToString('N')
$input = Join-Path $temp "canvas-clipboard-in-$id.txt"
$output = Join-Path $temp "canvas-clipboard-out-$id.txt"
$stdout = Join-Path $reportDir 'canvas-clipboard-stdout.txt'
$stderr = Join-Path $reportDir 'canvas-clipboard-stderr.txt'
$reportPath = Join-Path $reportDir 'canvas-clipboard.json'
$source = 'abc'
$expected = 'Zac'
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$result = [ordered]@{
    status = 'unverified'
    rid = $RuntimeIdentifier
    marker = ''
    source_utf16_units = $source.Length
    saved_utf16_units = $null
    input_sha256_unchanged = $false
    exact_bomless_utf8 = $false
    error = ''
    scope = 'in-process-AppKit-NSPasteboard-40Ki-accepted-50MiB-undo-budget-rejected-selection-save-reopen-not-external-IME'
}

try {
    $exe = [IO.Path]::GetFullPath($ExecutablePath)
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Native executable absent: $exe" }
    if (Test-Path -LiteralPath $output) { throw 'Canvas clipboard output unexpectedly exists.' }
    [IO.File]::WriteAllText($input, $source, $utf8)
    $inputHash = (Get-FileHash -LiteralPath $input -Algorithm SHA256).Hash
    $start = [Diagnostics.ProcessStartInfo]::new($exe)
    $start.WorkingDirectory = $root
    foreach ($argument in @('--check-native-mac-canvas-clipboard', $input, $output)) {
        [void]$start.ArgumentList.Add($argument)
    }
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    try {
        if (-not $process.WaitForExit(180000)) {
            $process.Kill()
            $process.WaitForExit()
            throw 'Mac canvas clipboard workflow timed out after three minutes.'
        }
        $out = $process.StandardOutput.ReadToEnd().Trim()
        $err = $process.StandardError.ReadToEnd().Trim()
        [IO.File]::WriteAllText($stdout, $out, $utf8)
        [IO.File]::WriteAllText($stderr, $err, $utf8)
        if ($process.ExitCode -ne 0) { throw "Canvas clipboard exited $($process.ExitCode): $err" }
        if ($out -cne 'mote-native-mac-canvas-clipboard-ready') {
            throw "Unexpected canvas clipboard success marker: $out"
        }
        $result.marker = $out
    }
    finally { $process.Dispose() }

    if (-not (Test-Path -LiteralPath $output -PathType Leaf)) { throw 'Canvas clipboard Save As output absent.' }
    $outputBytes = [IO.File]::ReadAllBytes($output)
    $expectedBytes = $utf8.GetBytes($expected)
    if ([Convert]::ToHexString($outputBytes) -cne [Convert]::ToHexString($expectedBytes)) {
        throw 'Canvas clipboard Save As bytes differ from exact BOMless UTF-8 Zac.'
    }
    $result.saved_utf16_units = $expected.Length
    $result.exact_bomless_utf8 = $true
    $result.input_sha256_unchanged =
        (Get-FileHash -LiteralPath $input -Algorithm SHA256).Hash -ceq $inputHash
    if (-not $result.input_sha256_unchanged) { throw 'Input file changed during clipboard probe.' }
    $result.status = 'mac-canvas-clipboard-workflow-ok'
}
catch {
    $result.error = $_.Exception.Message
}
finally {
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $reportPath -Encoding utf8NoBOM
    Write-Host ($result | ConvertTo-Json -Depth 5 -Compress)
}
if ($result.status -ne 'mac-canvas-clipboard-workflow-ok') { throw $result.error }
