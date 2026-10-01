# Diagnose a published Mach-O's window-local AppKit appearance transitions.
# This does not alter global macOS settings or exercise an external IME/keyboard.
param(
    [Parameter(Mandatory)][string] $ExecutablePath,
    [Parameter(Mandatory)][ValidateSet('osx-x64', 'osx-arm64')][string] $RuntimeIdentifier
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsMacOS) { throw 'NativeThemeWorkflow-Mac requires macOS.' }

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$temp = [IO.Path]::GetFullPath((Join-Path $root '.temp'))
$cache = [IO.Path]::GetFullPath((Join-Path $root '.cache'))
$inventory = [IO.Path]::GetFullPath((Join-Path $cache "ci-inventory/$RuntimeIdentifier"))
New-Item -ItemType Directory -Force -Path $temp, $cache, $inventory | Out-Null
$id = [guid]::NewGuid().ToString('N')
$fixture = [IO.Path]::GetFullPath((Join-Path $temp "native-theme-$id.md"))
if (-not [string]::Equals([IO.Path]::GetDirectoryName($fixture), $temp,
    [StringComparison]::Ordinal)) { throw 'Theme fixture escaped repository .temp.' }
$content = "# MOTE_THEME_HEADING`n`nMOTE_THEME_BODY`n"
[IO.File]::WriteAllText($fixture, $content, [Text.UTF8Encoding]::new($false))
$inputHash = (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash
$exe = [IO.Path]::GetFullPath($ExecutablePath)
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Published Mach-O is absent.' }
$reportPath = Join-Path $inventory 'native-theme-mac.json'
$result = [ordered]@{
    status = 'failed'; rid = $RuntimeIdentifier
    scope = 'published Mach-O; process-local AppKit effective appearance, not global macOS theme or IME'
    executable_sha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    input_sha256_unchanged = $false
    cases = @(); error = $null
}
$pngMagic = [byte[]](137, 80, 78, 71, 13, 10, 26, 10)

function Assert-NearThemeRgb([string] $Actual, [string] $Expected) {
    if ($Actual -cnotmatch '^#[0-9A-Fa-f]{6}$') {
        throw 'Native status background RGB is absent or malformed.'
    }
    for ($part = 1; $part -le 5; $part += 2) {
        $measured = [Convert]::ToInt32($Actual.Substring($part, 2), 16)
        $wanted = [Convert]::ToInt32($Expected.Substring($part, 2), 16)
        if ([Math]::Abs($measured - $wanted) -gt 1) {
            throw 'Native status background differs from the active window palette.'
        }
    }
}

foreach ($mode in @('default', 'canvas')) {
    $case = [ordered]@{
        mode = $mode; status = 'failed'; marker = $null; report = $null
        output_directory = $null; appearance_scope = $null
        callback_count = $null; states = @(); error = $null
    }
    $process = $null
    try {
        $output = [IO.Path]::GetFullPath((Join-Path $cache "native-theme-$RuntimeIdentifier-$mode-$id"))
        if (-not [string]::Equals([IO.Path]::GetDirectoryName($output), $cache,
            [StringComparison]::Ordinal) -or (Test-Path -LiteralPath $output)) {
            throw 'Theme output is not a new direct .cache child.'
        }
        $case.output_directory = $output
        $start = [Diagnostics.ProcessStartInfo]::new($exe)
        $start.WorkingDirectory = $root
        $start.UseShellExecute = $false
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        foreach ($argument in @('--check-native-mac-theme', $fixture, $output, $mode)) {
            [void]$start.ArgumentList.Add($argument)
        }
        $process = [Diagnostics.Process]::Start($start)
        if ($null -eq $process) { throw 'Published Mach-O did not start.' }
        $outTask = $process.StandardOutput.ReadToEndAsync()
        $errTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(145000)) {
            $process.Kill($true)
            [void]$process.WaitForExit(10000)
            throw 'Theme probe exceeded its 145-second bound.'
        }
        $stdout = $outTask.GetAwaiter().GetResult()
        $stderr = $errTask.GetAwaiter().GetResult()
        if ($stdout.Length -gt 8192) { $stdout = $stdout.Substring(0, 8192) + "`n<truncated>" }
        if ($stderr.Length -gt 8192) { $stderr = $stderr.Substring(0, 8192) + "`n<truncated>" }
        [IO.File]::WriteAllText((Join-Path $inventory "native-theme-$mode-stdout.txt"), $stdout)
        [IO.File]::WriteAllText((Join-Path $inventory "native-theme-$mode-stderr.txt"), $stderr)
        if ($process.ExitCode -ne 0) { throw "Native theme probe exited $($process.ExitCode)." }
        $marker = "mote-native-mac-theme-ready mode=$mode"
        if ($stdout.Trim() -cne $marker) { throw 'Native theme success marker was absent or malformed.' }
        $case.marker = $marker

        $nativeReportPath = Join-Path $output "theme-$mode.json"
        if (-not (Test-Path -LiteralPath $nativeReportPath -PathType Leaf)) {
            throw 'Native theme JSON report is absent.'
        }
        $native = Get-Content -LiteralPath $nativeReportPath -Raw | ConvertFrom-Json
        if ($native.Mode -cne $mode -or -not $native.Succeeded -or
            $native.AppearanceScope -cne 'window-local-appkit-appearance' -or
            -not $native.UndoRestored -or -not $native.RedoRestored -or
            -not $native.InputUnchanged -or $native.States.Count -ne 3 -or
            $native.CallbackCount -lt 2) {
            throw 'Native theme source/callback/Undo or scope contract failed.'
        }
        $case.appearance_scope = $native.AppearanceScope
        $case.callback_count = $native.CallbackCount
        $expectedSteps = @('dark-before', 'light', 'dark-after')
        $expectedIds = @('mote-dark', 'mote-light', 'mote-dark')
        $expectedStatusBackgrounds = @('#202124', '#F1F2F4', '#202124')
        $first = $native.States[0]
        for ($index = 0; $index -lt 3; $index++) {
            $state = $native.States[$index]
            if ($state.Step -cne $expectedSteps[$index] -or
                $state.ThemeId -cne $expectedIds[$index] -or
                $state.Generation -ne $first.Generation -or
                $state.Version -ne $first.Version -or
                $state.SelectionAnchor -ne $first.SelectionAnchor -or
                $state.SelectionActive -ne $first.SelectionActive) {
                throw 'Theme transition changed source identity, version, selection, or order.'
            }
            if ($index -gt 0 -and
                $state.CallbackCount -le $native.States[$index - 1].CallbackCount) {
                throw 'AppKit appearance callback did not advance for the theme change.'
            }
            if ($state.PreviewHeadingRgb -cnotmatch '^#[0-9A-Fa-f]{6}$' -or
                ($mode -eq 'default' -and $state.EditorHeadingRgb -cnotmatch '^#[0-9A-Fa-f]{6}$') -or
                ($mode -eq 'canvas' -and $null -ne $state.EditorHeadingRgb)) {
                throw 'Native heading foreground was not represented for the active editor mode.'
            }
            Assert-NearThemeRgb $state.StatusBackgroundRgb $expectedStatusBackgrounds[$index]
            if ($null -eq $state.StatusBackgroundAlpha -or
                [Math]::Abs([double]$state.StatusBackgroundAlpha - 1.0) -gt 0.001) {
                throw 'Native status background is not fully opaque.'
            }
            $expectedImage = "theme-$mode-$($expectedSteps[$index]).png"
            if ($state.Image -cne $expectedImage) { throw 'Theme PNG filename escaped the fixed contract.' }
            $image = Join-Path $output $state.Image
            if (-not (Test-Path -LiteralPath $image -PathType Leaf) -or
                (Get-Item -LiteralPath $image).Length -le 100) {
                throw 'Theme PNG is absent or empty.'
            }
            $stream = [IO.File]::OpenRead($image)
            try {
                $header = [byte[]]::new(8)
                if ($stream.Read($header, 0, 8) -ne 8 -or
                    -not [Collections.StructuralComparisons]::StructuralEqualityComparer.Equals(
                        $header, $pngMagic)) {
                    throw 'Theme raster is not PNG.'
                }
            }
            finally { $stream.Dispose() }
            $hash = (Get-FileHash -LiteralPath $image -Algorithm SHA256).Hash
            if ($hash -cne $state.ImageSha256) { throw 'Theme PNG hash disagrees with native JSON.' }
            $case.states += [ordered]@{
                step = $state.Step; theme_id = $state.ThemeId
                callback_count = $state.CallbackCount
                generation = $state.Generation; version = $state.Version
                selection_anchor = $state.SelectionAnchor
                selection_active = $state.SelectionActive
                preview_heading_rgb = $state.PreviewHeadingRgb
                editor_heading_rgb = $state.EditorHeadingRgb
                status_background_rgb = $state.StatusBackgroundRgb
                status_background_alpha = $state.StatusBackgroundAlpha
                image = $state.Image; image_sha256 = $hash
            }
        }
        if ($case.states[0].image_sha256 -ceq $case.states[1].image_sha256 -or
            $case.states[1].image_sha256 -ceq $case.states[2].image_sha256 -or
            $case.states[0].preview_heading_rgb -ceq $case.states[1].preview_heading_rgb -or
            $case.states[0].preview_heading_rgb -cne $case.states[2].preview_heading_rgb) {
            throw 'Dark/light/dark native color or raster transition was not observed.'
        }
        $case.report = $nativeReportPath
        $case.status = 'passed'
    }
    catch {
        # Do not copy exception messages: native errors may include fixture paths.
        $case.error = $_.Exception.GetType().Name
    }
    finally {
        if ($null -ne $process) {
            try {
                if (-not $process.HasExited) {
                    $process.Kill($true)
                    [void]$process.WaitForExit(10000)
                }
            }
            finally { $process.Dispose() }
        }
        $result.cases += $case
    }
}
$result.input_sha256_unchanged =
    (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash -ceq $inputHash
if ($result.input_sha256_unchanged -and
    @($result.cases | Where-Object { $_.status -eq 'passed' }).Count -eq 2) {
    $result.status = 'passed'
}
else { $result.error = 'One native mode failed or changed the synthetic input.' }
$result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $reportPath -Encoding utf8NoBOM
Write-Host ($result | ConvertTo-Json -Depth 10 -Compress)
if ($result.status -ne 'passed') { throw 'Native Mac theme workflow failed; inspect repository .cache report.' }
