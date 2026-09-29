# Exercise explicit empty-host global Delete/Backspace in the published AppKit probe.
# The binary drives real NSTextView commands and verifies source/Undo internally;
# this script independently retains exact input bytes and the CLI result.
param(
    [Parameter(Mandatory)][string] $ExecutablePath,
    [Parameter(Mandatory)][ValidateSet('osx-x64', 'osx-arm64')][string] $RuntimeIdentifier
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsMacOS) { throw 'Mac canvas delete probe requires macOS.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$temp = Join-Path $root '.temp'
$inventory = Join-Path $root ".cache/ci-inventory/$RuntimeIdentifier"
New-Item -ItemType Directory -Force $temp, $inventory | Out-Null
$exe = [IO.Path]::GetFullPath($ExecutablePath)
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$report = Join-Path $inventory 'mac-canvas-delete.json'
$result = [ordered]@{
    status = 'unverified'
    rid = $RuntimeIdentifier
    cases = @()
    error = ''
    scope = 'in-process-AppKit-empty-host-forward-reverse-global-delete-and-undo-not-external-IME'
}

try {
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Native executable absent: $exe" }
    foreach ($case in @(
        [pscustomobject]@{ Name = 'lf'; Text = "a`n`n" },
        [pscustomobject]@{ Name = 'crlf'; Text = "a`r`n`r`n" }
    )) {
        $input = Join-Path $temp "canvas-delete-$($case.Name)-$([guid]::NewGuid().ToString('N')).txt"
        [IO.File]::WriteAllText($input, $case.Text, $utf8)
        $before = (Get-FileHash -LiteralPath $input -Algorithm SHA256).Hash
        $start = [Diagnostics.ProcessStartInfo]::new($exe)
        $start.WorkingDirectory = $root
        $start.UseShellExecute = $false
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        [void]$start.ArgumentList.Add('--check-native-mac-canvas-delete')
        [void]$start.ArgumentList.Add($input)
        $process = [Diagnostics.Process]::Start($start)
        try {
            if (-not $process.WaitForExit(90000)) {
                $process.Kill()
                $process.WaitForExit()
                throw "Canvas delete $($case.Name) timed out after 90 seconds."
            }
            $stdout = $process.StandardOutput.ReadToEnd().Trim()
            $stderr = $process.StandardError.ReadToEnd().Trim()
            [IO.File]::WriteAllText((Join-Path $inventory "mac-canvas-delete-$($case.Name)-stdout.txt"), $stdout, $utf8)
            [IO.File]::WriteAllText((Join-Path $inventory "mac-canvas-delete-$($case.Name)-stderr.txt"), $stderr, $utf8)
            if ($process.ExitCode -ne 0 -or $stdout -cne 'mote-native-mac-canvas-delete-ready') {
                throw "Canvas delete $($case.Name) failed (exit $($process.ExitCode), stdout '$stdout'): $stderr"
            }
        }
        finally { $process.Dispose() }
        $after = (Get-FileHash -LiteralPath $input -Algorithm SHA256).Hash
        if ($after -cne $before) { throw "Canvas delete $($case.Name) modified the original input file." }
        $result.cases += [ordered]@{
            name = $case.Name
            utf16_units = $case.Text.Length
            input_sha256_unchanged = $true
            marker = $stdout
        }
    }
    $result.status = 'mac-canvas-empty-host-delete-ok'
}
catch {
    $result.error = $_.Exception.Message
}
finally {
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $report -Encoding utf8NoBOM
    Write-Host ($result | ConvertTo-Json -Depth 5 -Compress)
}
if ($result.status -ne 'mac-canvas-empty-host-delete-ok') { throw $result.error }
