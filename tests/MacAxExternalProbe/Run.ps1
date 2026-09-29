# Launch a published mote binary and test its source-backed AppKit AX provider externally.
# Output is a diagnostic JSON contract, suitable for an initially non-gating CI step.
param(
    [Parameter(Mandatory)][string] $ExecutablePath,
    [Parameter(Mandatory)][string] $ReportPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsMacOS) { throw 'The external AX probe requires macOS.' }

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$scratchRoot = [IO.Path]::GetFullPath((Join-Path $root '.temp/mac-ax-external'))
$scratch = [IO.Path]::GetFullPath((Join-Path $scratchRoot ([guid]::NewGuid().ToString('N'))))
$report = [IO.Path]::GetFullPath($ReportPath)
$reportRoot = [IO.Path]::GetFullPath((Join-Path $root '.cache/ci-inventory'))
if (-not $scratch.StartsWith($scratchRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
    throw 'AX scratch directory escaped repository .temp.'
}
if (-not $report.StartsWith($reportRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
    throw 'AX report must reside under repository .cache/ci-inventory.'
}
New-Item -ItemType Directory -Force -Path $scratch, (Split-Path $report) | Out-Null

$exe = [IO.Path]::GetFullPath($ExecutablePath)
$editor = $null
$oldHome = $env:MOTE_HOME
$status = 'probe-error'
$result = [ordered]@{
    status = $status
    method = 'external-AXUIElementCreateApplication-pid'
    client = 'Swift ApplicationServices; separately compiled executable'
    editor_pid = $null
    fixture_utf16_length = $null
    fixture_sha256 = $null
    swift_typecheck_passed = $false
    swift_exit_code = $null
    swift_report = $null
    error = ''
    limitation = 'AX API calls do not validate VoiceOver speech, physical keyboard input, or CJK IME.'
}

try {
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Published executable not found: $exe" }
    $builder = [Text.StringBuilder]::new()
    [void]$builder.Append("HEAD`r`n")
    for ($i = 0; $i -lt 80; $i++) { [void]$builder.AppendFormat("line-{0:D3}`n", $i) }
    [void]$builder.Append("CR`rLF`nPAIR`r`nEMOJI-😀-TAIL`n")
    [void]$builder.Append('q', 70000)
    [void]$builder.Append("`nOFFSCREEN-TARGET-😀`n")
    $source = $builder.ToString()
    $fixture = Join-Path $scratch 'ax-fixture.txt'
    [IO.File]::WriteAllText($fixture, $source, [Text.UTF8Encoding]::new($false, $true))
    $result.fixture_utf16_length = $source.Length
    $result.fixture_sha256 = (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash.ToLowerInvariant()

    $client = Join-Path $scratch 'mote-ax-probe'
    $swiftSource = Join-Path $PSScriptRoot 'Probe.swift'
    $typecheckLog = & /usr/bin/xcrun swiftc -typecheck $swiftSource 2>&1
    if ($LASTEXITCODE -ne 0) { throw "swiftc -typecheck failed: $($typecheckLog -join [Environment]::NewLine)" }
    $result.swift_typecheck_passed = $true
    $compileLog = & /usr/bin/xcrun swiftc -O $swiftSource -o $client 2>&1
    if ($LASTEXITCODE -ne 0) { throw "swiftc failed: $($compileLog -join [Environment]::NewLine)" }

    $start = [Diagnostics.ProcessStartInfo]::new($exe)
    $start.WorkingDirectory = $root
    $start.UseShellExecute = $false
    $start.Environment['MOTE_HOME'] = Join-Path $scratch 'home'
    [void]$start.ArgumentList.Add('--canvas-experimental')
    [void]$start.ArgumentList.Add($fixture)
    $editor = [Diagnostics.Process]::Start($start)
    $result.editor_pid = $editor.Id

    $probeStart = [Diagnostics.ProcessStartInfo]::new($client)
    $probeStart.UseShellExecute = $false
    $probeStart.RedirectStandardOutput = $true
    $probeStart.RedirectStandardError = $true
    [void]$probeStart.ArgumentList.Add([string]$editor.Id)
    [void]$probeStart.ArgumentList.Add($fixture)
    $probe = [Diagnostics.Process]::Start($probeStart)
    try {
        if (-not $probe.WaitForExit(45000)) {
            $probe.Kill()
            $probe.WaitForExit()
            throw 'External AX client timed out after 45 seconds.'
        }
        $result.swift_exit_code = $probe.ExitCode
        $stdout = $probe.StandardOutput.ReadToEnd()
        $stderr = $probe.StandardError.ReadToEnd()
        if ($stderr.Trim().Length -ne 0) { $result.error = $stderr.Trim() }
        if ($stdout.Trim().Length -eq 0) { throw 'External AX client returned no JSON.' }
        $result.swift_report = $stdout | ConvertFrom-Json -Depth 12
        $status = [string]$result.swift_report.status
        if ($status -eq 'passed' -and $probe.ExitCode -ne 0) {
            throw "AX client reported pass but exited $($probe.ExitCode)."
        }
    }
    finally { $probe.Dispose() }

    $editor.Refresh()
    if ($editor.HasExited -and $status -eq 'passed') {
        $status = 'failed'
        $result.error = "Editor exited during external AX probe: $($editor.ExitCode)."
    }
    $diskHash = (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($diskHash -cne $result.fixture_sha256) {
        $status = 'failed'
        $result.error = 'Read-only AX probe changed fixture bytes on disk.'
    }
}
catch {
    $status = 'probe-error'
    $result.error = $_.Exception.Message
}
finally {
    if ($editor -and -not $editor.HasExited) {
        try { $editor.Kill(); $editor.WaitForExit(10000) | Out-Null }
        catch { Write-Warning "Could not stop AX fixture editor: $($_.Exception.Message)" }
    }
    if ($editor) { $editor.Dispose() }
    $env:MOTE_HOME = $oldHome
    $result.status = $status
    $result | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $report -Encoding utf8
    Get-Content -LiteralPath $report
}

if ($status -eq 'external-accessibility-unavailable') {
    Write-Warning 'Hosted macOS Accessibility permission unavailable; this is an environment limitation, not a product verdict.'
}
elseif ($status -ne 'passed') {
    throw "External macOS AX provider probe failed: $status. See $report."
}
