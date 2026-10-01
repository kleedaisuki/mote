# Build the benchmark-only C++/WinRT WGC observer inside repository .cache.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'WGC observer builds only on Windows.' }

# Keep compiler output inside the real repository rather than a redirected .cache.
function Assert-NoReparseAncestors {
    param([string] $Path)
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -ErrorAction Stop
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Reparse-point ancestor forbidden for WGC build output: $cursor"
            }
        }
        $parent = [IO.Path]::GetDirectoryName($cursor)
        if (-not $parent -or $parent -ceq $cursor) { break }
        $cursor = $parent
    }
}

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$outDir = Join-Path $root '.cache/benchmarks/native-paint-latency/wgc'
$compilerTemp = Join-Path $root '.temp/benchmarks/native-paint-latency/compiler'
Assert-NoReparseAncestors $outDir
Assert-NoReparseAncestors $compilerTemp
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
New-Item -ItemType Directory -Force -Path $compilerTemp | Out-Null
$out = Join-Path $outDir 'WgcEditObserver.exe'
Assert-NoReparseAncestors $out
$source = Join-Path $PSScriptRoot 'WgcEditObserver.cpp'
$clockOut = Join-Path $outDir 'WgcClockProbe.exe'
$clockSource = Join-Path $PSScriptRoot 'WgcClockProbe.cpp'
Assert-NoReparseAncestors $clockOut
$kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/Include'
$sdk = Get-ChildItem -LiteralPath $kits -Directory -ErrorAction Stop |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'cppwinrt/winrt/Windows.Graphics.Capture.h') } |
    Sort-Object Name -Descending | Select-Object -First 1
if (-not $sdk) { throw 'Windows SDK C++/WinRT headers are absent.' }
$include = Join-Path $sdk.FullName 'cppwinrt'
$gpp = @(Get-Command g++.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source)
$gpp += @('C:\msys64\mingw64\bin\g++.exe', 'D:\Program Files\msys2\mingw64\bin\g++.exe') |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }
$compiler = $gpp | Select-Object -First 1
if (-not $compiler) {
    throw 'MinGW-w64 g++ is absent; install it or build WgcEditObserver.cpp with C++/WinRT and the listed Windows import libraries.'
}
$oldTmp = $env:TMP
$oldTemp = $env:TEMP
$oldTmpDir = $env:TMPDIR
try {
    $env:TMP = $compilerTemp
    $env:TEMP = $compilerTemp
    $env:TMPDIR = $compilerTemp
    foreach ($target in @(@($source, $out), @($clockSource, $clockOut))) {
        & $compiler -std=c++20 -O2 -municode "-I$include" $target[0] -o $target[1] `
            -ld3d11 -ldxgi -ldwmapi -lgdi32 -lruntimeobject -lwindowsapp -lole32 -luser32
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $target[1] -PathType Leaf)) {
            throw "WGC benchmark compile failed for $($target[0]) with $compiler."
        }
    }
}
finally {
    $env:TMP = $oldTmp
    $env:TEMP = $oldTemp
    $env:TMPDIR = $oldTmpDir
}
Write-Output $out, $clockOut
