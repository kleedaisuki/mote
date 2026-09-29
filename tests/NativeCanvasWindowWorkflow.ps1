# Inspect the opt-in read-only native canvas against large source-backed fixtures.
# The initial hosted workflow runs this as a diagnostic, not a release gate.
param(
    [Parameter(Mandatory)][string] $ExecutablePath,
    [Parameter(Mandatory)][string] $RuntimeIdentifier
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($RuntimeIdentifier -notin @('win-x64', 'win-arm64', 'osx-x64', 'osx-arm64')) {
    throw "Unsupported canvas probe runtime identifier: $RuntimeIdentifier"
}
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$scratch = Join-Path $root ".temp/canvas-window/$RuntimeIdentifier"
$captureRoot = Join-Path $root ".cache/canvas-window/$RuntimeIdentifier"
$reportPath = Join-Path $root ".cache/ci-inventory/$RuntimeIdentifier/canvas-window.json"
$themes = @('mote-dark', 'mote-light', 'mote-high-contrast-dark')
$result = [ordered]@{
    status = 'unverified'
    rid = $RuntimeIdentifier
    many_bytes = $null
    long_bytes = $null
    themes = @()
    error = ''
    interpretation = 'read-only-on-screen-view-callback-not-physical-present-or-IME'
}

# This script's generated files and captures are confined to repository-local roots.
function Assert-RepoPath {
    param([string] $Path, [string] $Directory)
    $full = [IO.Path]::GetFullPath($Path)
    $parent = [IO.Path]::GetFullPath((Join-Path $root $Directory))
    if (-not $full.StartsWith($parent + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "Canvas artifact escaped repository $Directory : $Path"
    }
}

Assert-RepoPath $scratch '.temp'
Assert-RepoPath $captureRoot '.cache'
Assert-RepoPath $reportPath '.cache'
New-Item -ItemType Directory -Force $scratch, $captureRoot, (Split-Path $reportPath) | Out-Null
$many = Join-Path $scratch 'many-unicode.txt'
$long = Join-Path $scratch 'long-latin.txt'
$exe = [IO.Path]::GetFullPath($ExecutablePath)

# Write byte-exact corpora with constant memory. Repeated CJK/RTL/ZWJ rows put
# real fallback and bidirectional text around the old 64 Ki source seam.
function Write-ManyLines {
    param([string] $Path)
    $target = 100L * 1024 * 1024
    $row = "canvas 你好 אבג 👩‍💻 Z`r`n"
    $encoding = [Text.UTF8Encoding]::new($false, $true)
    $rowBytes = $encoding.GetBytes($row)
    $block = [byte[]]::new($rowBytes.Length * 1024)
    for ($i = 0; $i -lt 1024; $i++) {
        [Array]::Copy($rowBytes, 0, $block, $i * $rowBytes.Length, $rowBytes.Length)
    }
    $stream = [IO.File]::Create($Path)
    try {
        while ($stream.Position + $block.Length -le $target) {
            $stream.Write($block, 0, $block.Length)
        }
        while ($stream.Position + $rowBytes.Length -le $target) {
            $stream.Write($rowBytes, 0, $rowBytes.Length)
        }
        $remain = [int]($target - $stream.Position)
        if ($remain -gt 0) {
            $tail = [Text.Encoding]::ASCII.GetBytes(('x' * [Math]::Max(0, $remain - 1)) + "`n")
            $stream.Write($tail, 0, $tail.Length)
        }
    }
    finally { $stream.Dispose() }
    if ((Get-Item -LiteralPath $Path).Length -ne $target) { throw 'Many-line corpus length mismatch.' }
}

function Write-LongLine {
    param([string] $Path)
    $target = 50L * 1024 * 1024
    $block = [Text.Encoding]::ASCII.GetBytes('a' * (64 * 1024))
    $stream = [IO.File]::Create($Path)
    try {
        while ($stream.Position -lt $target) {
            $count = [int][Math]::Min($block.Length, $target - $stream.Position)
            $stream.Write($block, 0, $count)
        }
    }
    finally { $stream.Dispose() }
    if ((Get-Item -LiteralPath $Path).Length -ne $target) { throw 'Long-line corpus length mismatch.' }
}

try {
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Native executable absent: $exe" }
    Write-ManyLines $many
    Write-LongLine $long
    $result.many_bytes = (Get-Item -LiteralPath $many).Length
    $result.long_bytes = (Get-Item -LiteralPath $long).Length
    $platform = if ($IsWindows) { 'windows' } elseif ($IsMacOS) { 'macos' } else { throw 'Unsupported host.' }
    foreach ($theme in $themes) {
        $output = Join-Path $captureRoot $theme
        New-Item -ItemType Directory -Force $output | Out-Null
        $stdout = Join-Path $output 'stdout.txt'
        $stderr = Join-Path $output 'stderr.txt'
        $start = [Diagnostics.ProcessStartInfo]::new($exe)
        $start.WorkingDirectory = $root
        foreach ($arg in @('--check-native-canvas-window', $many, $long, $output, $theme)) {
            [void]$start.ArgumentList.Add($arg)
        }
        $start.UseShellExecute = $false
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $process = [Diagnostics.Process]::Start($start)
        try {
            if (-not $process.WaitForExit(180000)) {
                $process.Kill()
                $process.WaitForExit()
                throw "Canvas $theme timed out after three minutes."
            }
            $outputText = $process.StandardOutput.ReadToEnd().Trim()
            $errorText = $process.StandardError.ReadToEnd().Trim()
            [IO.File]::WriteAllText($stdout, $outputText)
            [IO.File]::WriteAllText($stderr, $errorText)
            if ($process.ExitCode -ne 0) {
                throw "Canvas $theme exited $($process.ExitCode): $errorText"
            }
        }
        finally { $process.Dispose() }
        $expected = '^mote-native-canvas-window-ready platform=' + $platform +
            ' theme=' + $theme +
            ' before=(\d+) after=(\d+) selection=(\d+)\+(\d+) long-slice=(\d+) hits=(\d+) screenshots=(\d+)$'
        if ($outputText -cnotmatch $expected) { throw "Canvas marker mismatch: $outputText" }
        $before = [int]$Matches[1]
        $after = [int]$Matches[2]
        $selection = [int]$Matches[3]
        $length = [int]$Matches[4]
        $slice = [int]$Matches[5]
        $hits = [int]$Matches[6]
        $count = [int]$Matches[7]
        if ($before -ge 65536 -or $after -le 65536 -or
            $selection -ge 65536 -or $selection + $length -le 65536 -or
            $slice -lt 1 -or $slice -gt 16384 -or $hits -lt 2 -or $count -lt 2) {
            throw "Canvas geometry contract failed: $outputText"
        }
        $pngs = @(Get-ChildItem -LiteralPath $output -Filter '*.png' -File)
        if ($pngs.Count -ne $count) { throw "Canvas screenshot count disagrees with marker: $outputText" }
        foreach ($png in $pngs) {
            if ($png.Length -lt 1024) { throw "Canvas PNG is suspiciously small: $($png.FullName)" }
            $stream = [IO.File]::OpenRead($png.FullName)
            try {
                $magic = [byte[]]::new(8)
                if ($stream.Read($magic, 0, 8) -ne 8 -or
                    [Convert]::ToHexString($magic) -ne '89504E470D0A1A0A') {
                    throw "Canvas capture is not PNG: $($png.FullName)"
                }
            }
            finally { $stream.Dispose() }
        }
        $result.themes += [ordered]@{
            id = $theme
            marker = $outputText
            screenshot_count = $pngs.Count
            screenshot_sha256 = @($pngs | Sort-Object Name | ForEach-Object {
                [ordered]@{ name = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
            })
        }
    }
    $result.status = 'read-only-on-screen-canvas-probe-ok'
}
catch {
    $result.error = $_.Exception.Message
}
finally {
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding utf8NoBOM
    Write-Host ($result | ConvertTo-Json -Depth 8 -Compress)
}
if ($result.status -ne 'read-only-on-screen-canvas-probe-ok') { throw $result.error }
