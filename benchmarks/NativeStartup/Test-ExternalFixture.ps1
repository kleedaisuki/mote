# Validate external-fixture preflight without executing an editor or GUI code.
# All synthetic files remain under repo .temp; cleanup uses exact leaves only.
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'External Windows fixture preflight requires Windows.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$base = [IO.Path]::GetFullPath((Join-Path $root ('.temp/native-startup-external-tests/' + [guid]::NewGuid().ToString('N'))))
$temp = [IO.Path]::GetFullPath((Join-Path $root '.temp'))
if (-not $base.StartsWith($temp + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) { throw 'External preflight test escaped repository .temp.' }
$probe = Join-Path $PSScriptRoot 'Measure-WindowsOrdinary.ps1'
$publish = Join-Path $base 'publish'
$exe = Join-Path $publish 'mote.exe'
$fixture = Join-Path $base 'json-long-1.json'
$unsupported = Join-Path $base 'unsupported.txt'
$short = Join-Path $base 'short.json'
$weak = Join-Path $base 'weak.json'
$junction = Join-Path $base 'linked'
$checks = [Collections.Generic.List[string]]::new()

# Check existing ancestry before any write, including a preexisting .temp or
# test-root junction. Lexical containment alone does not constrain NTFS routing.
function Assert-NoReparseAncestors {
    param([string] $Path)
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'External preflight test ancestry contains a reparse point.'
            }
        }
        $parent = [IO.Path]::GetDirectoryName($cursor)
        if (-not $parent -or $parent -ceq $cursor) { break }
        $cursor = $parent
    }
}
Assert-NoReparseAncestors $base

# Only preflight is called. A sole malformed executable is sufficient because
# the inventory contract intentionally does not execute or certify an AOT image.
function Invoke-Rejected {
    param([hashtable] $Arguments, [string] $ExpectedError, [string] $Name)
    try {
        & $probe -ExecutablePath $exe -CheckInventoryOnly @Arguments | Out-Null
        throw 'External fixture unexpectedly passed preflight.'
    }
    catch {
        if ($_.Exception.Message -notlike $ExpectedError) { throw }
        $checks.Add($Name)
    }
}

New-Item -ItemType Directory -Path $publish -Force | Out-Null
try {
    [IO.File]::WriteAllText($exe, 'preflight-only; never execute')
    $stream = [IO.File]::Open($fixture, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    try {
        $head = [Text.Encoding]::ASCII.GetBytes('{"value":"')
        $tail = [Text.Encoding]::ASCII.GetBytes('"}')
        $stream.Write($head)
        $block = [byte[]]::new(65536)
        [Array]::Fill($block, [byte][char]'a')
        $remaining = 1048576 - $head.Length - $tail.Length
        while ($remaining -gt 0) {
            $count = [Math]::Min($remaining, $block.Length)
            $stream.Write($block, 0, $count)
            $remaining -= $count
        }
        $stream.Write($tail)
    }
    finally { $stream.Dispose() }
    $sha = (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash.ToLowerInvariant()
    $args = @{ FixturePath = $fixture; FixtureSha256 = $sha; ReadinessOnly = $true }
    $good = & $probe -ExecutablePath $exe -CheckInventoryOnly @args
    if ($good.inventory_entries -ne 1) { throw 'Valid external fixture preflight failed.' }
    $checks.Add('owned-exact-json-accepted')
    Invoke-Rejected @{FixturePath=$fixture; FixtureSha256=$sha} `
        'External fixture requires*ReadinessOnly.' 'edit-save-route-refused'
    Invoke-Rejected @{FixturePath=$fixture; ReadinessOnly=$true} `
        'External fixture requires*ReadinessOnly.' 'unpinned-fixture-refused'
    Invoke-Rejected @{FixtureSha256=$sha; ReadinessOnly=$true} `
        'External fixture requires*ReadinessOnly.' 'missing-fixture-refused'
    Invoke-Rejected @{FixturePath=$fixture; FixtureSha256=('0'*64); ReadinessOnly=$true} `
        'External fixture SHA-256 differs*' 'wrong-digest-refused'
    Invoke-Rejected @{FixturePath=$fixture; FixtureSha256=$sha; ReadinessOnly=$true; DiagnosticTrace=$true} `
        'External readiness pilot does not collect child traces*' 'unsupported-trace-refused'
    Invoke-Rejected @{FixturePath=(Join-Path $root 'README.md'); FixtureSha256=$sha; ReadinessOnly=$true} `
        'External fixture must remain below repository .temp.' 'outside-scratch-refused'
    Copy-Item -LiteralPath $fixture -Destination $unsupported
    Invoke-Rejected @{FixturePath=$unsupported; FixtureSha256=$sha; ReadinessOnly=$true} `
        'External fixture format is unsupported.' 'unsupported-format-refused'
    [IO.File]::WriteAllText($short, '{"value":1}')
    Invoke-Rejected @{FixturePath=$short; FixtureSha256=$sha; ReadinessOnly=$true} `
        'External fixture must have the exact requested MiB size.' 'wrong-size-refused'
    Copy-Item -LiteralPath $fixture -Destination $weak
    $stream = [IO.File]::OpenWrite($weak)
    try { $stream.WriteByte(10) } finally { $stream.Dispose() }
    $weakSha = (Get-FileHash -LiteralPath $weak -Algorithm SHA256).Hash.ToLowerInvariant()
    Invoke-Rejected @{FixturePath=$weak; FixtureSha256=$weakSha; ReadinessOnly=$true} `
        'External fixture needs 20 printable ASCII bytes*' 'weak-prefix-refused'
    New-Item -ItemType Junction -Path $junction -Target $base | Out-Null
    Invoke-Rejected @{FixturePath=(Join-Path $junction 'json-long-1.json'); FixtureSha256=$sha; ReadinessOnly=$true} `
        'Reparse-point ancestor forbidden:*' 'junction-refused'
    Remove-Item -LiteralPath $junction
    $original = & $probe -ExecutablePath $exe -CheckInventoryOnly
    if ($original.inventory_entries -ne 1) { throw 'Default Markdown inventory compatibility failed.' }
    $checks.Add('default-preflight-preserved')
    if ((Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash.ToLowerInvariant() -cne $sha) {
        throw 'Preflight modified original fixture.'
    }
    $checks.Add('fixture-bytes-unchanged')
    [pscustomobject]@{scope='preflight-only-no-native-process'; checks=$checks.ToArray(); passed=$checks.Count}
}
finally {
    Assert-NoReparseAncestors $base
    if (Test-Path -LiteralPath $junction) { Remove-Item -LiteralPath $junction }
    foreach ($file in @($exe, $fixture, $unsupported, $short, $weak)) {
        if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Force }
    }
    if (Test-Path -LiteralPath $publish) { [IO.Directory]::Delete($publish) }
    if (Test-Path -LiteralPath $base) { [IO.Directory]::Delete($base) }
}
