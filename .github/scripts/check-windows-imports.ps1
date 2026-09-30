# Inspect static PE imports so a strict one-file publish cannot quietly acquire a private DLL dependency.
# Dynamic LoadLibrary calls still require source review and native GUI smoke coverage.
param(
    [Parameter(Mandatory = $true)][string]$Executable,
    [Parameter(Mandatory = $true)][string]$Report
)

$ErrorActionPreference = 'Stop'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
    throw "Visual Studio discovery tool is missing: $vswhere"
}

$architecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
$candidates = @(& $vswhere -latest -products '*' -find '**\dumpbin.exe')
if ($LASTEXITCODE -ne 0 -or $candidates.Count -eq 0) {
    throw 'Visual Studio dumpbin.exe is unavailable; PE imports cannot be verified.'
}
$nativePattern = "Host$architecture\\$architecture\\dumpbin.exe$"
$dumpbin = @($candidates | Where-Object { $_ -match $nativePattern } | Select-Object -First 1)
if ($dumpbin.Count -eq 0) {
    $dumpbin = @($candidates | Where-Object { $_ -match 'Hostx64\\x64\\dumpbin.exe$' } | Select-Object -First 1)
}
if ($dumpbin.Count -eq 0) {
    throw "No runnable dumpbin.exe was found for $architecture."
}

$lines = @(& $dumpbin[0] /dependents (Resolve-Path -LiteralPath $Executable).Path)
if ($LASTEXITCODE -ne 0) { throw "dumpbin failed for $Executable" }
$heading = [Array]::FindIndex($lines, [Predicate[string]] { param($line) $line -match 'Image has the following dependencies:' })
if ($heading -lt 0) { throw 'dumpbin dependency section was not found.' }

$imports = @()
foreach ($line in $lines[($heading + 1)..($lines.Count - 1)]) {
    if ($line -match '^\s*Summary\s*$') { break }
    if ($line -match '^\s*([A-Za-z0-9_.-]+\.dll)\s*$') { $imports += $Matches[1].ToLowerInvariant() }
}
if ($imports.Count -eq 0) { throw 'No PE imports were parsed; refusing to assume the binary is self-contained.' }

# This deliberately small allowlist makes each newly linked OS API an explicit, reviewed change.
# UIA's BSTR/SAFEARRAY interop calls the Windows OS Ole Automation library.
# Native title-bar theming calls the documented Windows DWM system library.
$systemDlls = @(
    'advapi32.dll', 'bcrypt.dll', 'comdlg32.dll', 'dwmapi.dll', 'gdi32.dll', 'kernel32.dll',
    'ole32.dll', 'oleaut32.dll', 'shell32.dll', 'user32.dll'
)
$unexpected = @($imports | Where-Object {
    $_ -notin $systemDlls -and $_ -notmatch '^(api-ms-win|ext-ms-win)-[a-z0-9-]+\.dll$'
})

$directory = Split-Path -Parent $Report
New-Item -ItemType Directory -Force -Path $directory | Out-Null
[pscustomobject]@{
    executable = (Split-Path -Leaf $Executable)
    static_imports = @($imports | Sort-Object -Unique)
    unexpected_imports = @($unexpected | Sort-Object -Unique)
} | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $Report -Encoding utf8
Get-Content -LiteralPath $Report
if ($unexpected.Count -gt 0) {
    throw "Non-allowlisted PE imports violate the one-binary system-library contract: $($unexpected -join ', ')"
}
