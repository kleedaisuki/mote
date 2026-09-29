# Exercise the published macOS binary's in-process AppKit NSTextView workflow seam.
# This is not external keyboard automation or a real CJK input-method test.
param([Parameter(Mandatory)][string] $ExecutablePath)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsMacOS) { throw 'This workflow requires macOS.' }

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$tempRoot = [IO.Path]::GetFullPath((Join-Path $root '.temp'))
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
$id = [guid]::NewGuid().ToString('N')
$input = [IO.Path]::GetFullPath((Join-Path $tempRoot "mac-native-input-$id.txt"))
$output = [IO.Path]::GetFullPath((Join-Path $tempRoot "mac-native-output-$id.txt"))
foreach ($path in @($input, $output)) {
    if (-not $path.StartsWith($tempRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal) -or
        [IO.Path]::GetDirectoryName($path) -cne $tempRoot) {
        throw 'Native macOS workflow fixture escaped direct-child repository .temp.'
    }
}
$stdout = [IO.Path]::GetFullPath((Join-Path $tempRoot "mac-native-stdout-$id.txt"))
$stderr = [IO.Path]::GetFullPath((Join-Path $tempRoot "mac-native-stderr-$id.txt"))
$source = "mote probe`n"
$expected = $source + "`n# mote-native-workflow中"
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText($input, $source, $utf8)
$originalHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($input)))
$success = $false
$process = $null
try {
    $exe = [IO.Path]::GetFullPath($ExecutablePath)
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Executable not found: $exe" }
    if (Test-Path -LiteralPath $output) { throw 'Output fixture must not exist before Save As.' }
    $start = [Diagnostics.ProcessStartInfo]::new($exe)
    $start.WorkingDirectory = $root
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment['MOTE_HOME'] = Join-Path $tempRoot "mac-workflow-home-$id"
    [void]$start.ArgumentList.Add('--check-native-mac-workflow')
    [void]$start.ArgumentList.Add($input)
    [void]$start.ArgumentList.Add($output)
    $process = [Diagnostics.Process]::Start($start)
    if (-not $process.WaitForExit(30000)) {
        $process.Kill()
        $process.WaitForExit()
        throw 'In-process AppKit workflow timed out after 30 seconds.'
    }
    $textOut = $process.StandardOutput.ReadToEnd()
    $textErr = $process.StandardError.ReadToEnd()
    [IO.File]::WriteAllText($stdout, $textOut, $utf8)
    [IO.File]::WriteAllText($stderr, $textErr, $utf8)
    if ($process.ExitCode -ne 0) { throw "Native AppKit workflow exited $($process.ExitCode): $textErr" }
    if ($textOut.Trim() -cne 'mote-native-mac-workflow-ready') {
        throw "Unexpected native AppKit workflow output: $textOut"
    }
    if (-not (Test-Path -LiteralPath $output -PathType Leaf)) { throw 'Save As output was not created.' }
    $actualBytes = [IO.File]::ReadAllBytes($output)
    $expectedBytes = $utf8.GetBytes($expected)
    if ([Convert]::ToHexString($actualBytes) -cne [Convert]::ToHexString($expectedBytes)) {
        throw 'Saved output differs from independent BOMless UTF-8 expected bytes.'
    }
    $inputHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($input)))
    if ($inputHash -cne $originalHash) { throw 'Original input was unexpectedly modified.' }
    $success = $true
    [pscustomobject]@{
        result = 'in-process-appkit-open-edit-save-new-reopen-ok'
        output_bytes = $actualBytes.Length
        external_keyboard_or_ime_tested = $false
    } | ConvertTo-Json -Compress
}
finally {
    if ($process -and -not $process.HasExited) { $process.Kill() }
    if ($process) { $process.Dispose() }
    if ($success) {
        foreach ($path in @($input, $output, $stdout, $stderr)) {
            if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
        }
    }
    else { Write-Warning "Retained failed native macOS workflow fixtures in $tempRoot (id $id)." }
}
