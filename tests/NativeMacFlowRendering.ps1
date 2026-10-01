# Runs a bounded, in-process AppKit attributed-Flow diagnostic against a published binary.
# It does not mutate input sources, user configuration, files, clipboard, or TCC state.
param([Parameter(Mandatory = $true)][string]$Executable)
$ErrorActionPreference = 'Stop'
if (-not $IsMacOS) { throw 'This diagnostic requires macOS.' }
$binary = (Resolve-Path -LiteralPath $Executable).Path
$output = & $binary --check-native-mac-flow-rendering 2>&1
$code = $LASTEXITCODE
$output | ForEach-Object { Write-Output $_ }
if ($code -ne 0 -or -not ($output -contains 'mote-native-mac-flow-rendering-ready')) {
    throw "AppKit Flow diagnostic failed (exit $code)."
}
