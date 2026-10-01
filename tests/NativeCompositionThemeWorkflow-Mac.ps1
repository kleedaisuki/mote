# Diagnose published AppKit synthetic marked-text commit and cancellation during
# window-local theme changes; this is not a real CJK IME or global OS switch.
param(
    [Parameter(Mandatory)][string] $ExecutablePath,
    [Parameter(Mandatory)][ValidateSet('osx-x64', 'osx-arm64')][string] $RuntimeIdentifier
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsMacOS) { throw 'NativeCompositionThemeWorkflow-Mac requires macOS.' }

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$temp = [IO.Path]::GetFullPath((Join-Path $root '.temp'))
$cache = [IO.Path]::GetFullPath((Join-Path $root '.cache'))
$inventory = [IO.Path]::GetFullPath((Join-Path $cache "ci-inventory/$RuntimeIdentifier"))
New-Item -ItemType Directory -Force -Path $temp, $cache, $inventory | Out-Null
$id = [guid]::NewGuid().ToString('N')
$fixture = [IO.Path]::GetFullPath((Join-Path $temp "native-composition-theme-$id.md"))
if (-not [string]::Equals([IO.Path]::GetDirectoryName($fixture), $temp,
    [StringComparison]::Ordinal)) { throw 'Composition fixture escaped repository .temp.' }
$content = "# MOTE_THEME_HEADING`n`nMOTE_THEME_BODY`n"
[IO.File]::WriteAllText($fixture, $content, [Text.UTF8Encoding]::new($false))
$inputHash = (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash
$initialSourceHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
    [Text.Encoding]::Unicode.GetBytes($content)))
$exe = [IO.Path]::GetFullPath($ExecutablePath)
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Published Mach-O is absent.' }
$reportPath = Join-Path $inventory 'native-composition-theme-mac.json'
$result = [ordered]@{
    status = 'failed'; rid = $RuntimeIdentifier
    scope = 'synthetic AppKit marked-text commit/cancel and window-local appearance; no real CJK IME/global OS'
    executable_sha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    input_sha256_unchanged = $false; cases = @(); error = $null
}
$pngMagic = [byte[]](137, 80, 78, 71, 13, 10, 26, 10)

foreach ($mode in @('default', 'canvas')) {
    $case = [ordered]@{
        mode = $mode; status = 'failed'; marker = $null
        output_directory = $null; appearance_callbacks = $null
        composition_settled_callbacks = $null; insertion_offset = $null
        cancel_isolated = $null; cancel_version_unchanged = $null
        cancel_selection_preserved = $null
        phases = @(); error = $null
    }
    $process = $null
    try {
        $output = [IO.Path]::GetFullPath((Join-Path $cache "native-composition-theme-$RuntimeIdentifier-$mode-$id"))
        if (-not [string]::Equals([IO.Path]::GetDirectoryName($output), $cache,
            [StringComparison]::Ordinal) -or (Test-Path -LiteralPath $output)) {
            throw 'Composition output is not a new direct .cache child.'
        }
        $case.output_directory = $output
        $start = [Diagnostics.ProcessStartInfo]::new($exe)
        $start.WorkingDirectory = $root
        $start.UseShellExecute = $false
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        foreach ($argument in @('--check-native-mac-composition-theme', $fixture, $output, $mode)) {
            [void]$start.ArgumentList.Add($argument)
        }
        $process = [Diagnostics.Process]::Start($start)
        if ($null -eq $process) { throw 'Published Mach-O did not start.' }
        $outTask = $process.StandardOutput.ReadToEndAsync()
        $errTask = $process.StandardError.ReadToEndAsync()
        $timedOut = -not $process.WaitForExit(145000)
        if ($timedOut) {
            $process.Kill($true)
            [void]$process.WaitForExit(10000)
        }
        $stdout = $outTask.GetAwaiter().GetResult()
        $stderr = $errTask.GetAwaiter().GetResult()
        if ($stdout.Length -gt 8192) { $stdout = $stdout.Substring(0, 8192) + "`n<truncated>" }
        if ($stderr.Length -gt 8192) { $stderr = $stderr.Substring(0, 8192) + "`n<truncated>" }
        [IO.File]::WriteAllText((Join-Path $inventory "native-composition-theme-$mode-stdout.txt"), $stdout)
        [IO.File]::WriteAllText((Join-Path $inventory "native-composition-theme-$mode-stderr.txt"), $stderr)
        if ($timedOut) { throw 'Composition probe exceeded its 145-second bound.' }
        if ($process.ExitCode -ne 0) { throw "Native composition probe exited $($process.ExitCode)." }
        $marker = "mote-native-mac-composition-theme-ready mode=$mode"
        if ($stdout.Trim() -cne $marker) { throw 'Native composition success marker was absent.' }
        $case.marker = $marker

        $nativeReportPath = Join-Path $output "composition-theme-$mode.json"
        if (-not (Test-Path -LiteralPath $nativeReportPath -PathType Leaf)) {
            throw 'Native composition JSON report is absent.'
        }
        $native = Get-Content -LiteralPath $nativeReportPath -Raw | ConvertFrom-Json
        $expectedSpace = if ($mode -eq 'canvas') { 'global-source' } else { 'native-text-view' }
        if ($native.Mode -cne $mode -or -not $native.Succeeded -or
            -not $native.PreeditIsolated -or -not $native.PolicyDeferred -or
            -not $native.CancelIsolated -or
            -not $native.CancelVersionUnchanged -or
            -not $native.CancelSelectionPreserved -or
            -not $native.UndoRestored -or -not $native.RedoRestored -or
            -not $native.InputUnchanged -or $native.Scope -cne
            'synthetic-appkit-marked-text-window-appearance' -or
            $native.SelectionSpace -cne $expectedSpace -or
            $native.SourceHashEncoding -cne 'UTF-16LE-no-BOM' -or
            $native.Phases.Count -ne 5 -or $native.AppearanceCallbacks -lt 1 -or
            $native.CompositionSettledCallbacks -lt 1 -or
            $native.InsertionOffset -lt 0 -or $native.InsertionOffset -gt $content.Length) {
            throw 'Native marked-text isolation/deferred-policy/Undo or scope contract failed.'
        }
        $case.appearance_callbacks = $native.AppearanceCallbacks
        $case.composition_settled_callbacks = $native.CompositionSettledCallbacks
        $case.insertion_offset = $native.InsertionOffset
        $case.cancel_isolated = $native.CancelIsolated
        $case.cancel_version_unchanged = $native.CancelVersionUnchanged
        $case.cancel_selection_preserved = $native.CancelSelectionPreserved
        $case.undo_selection = @($native.UndoSelectionAnchor, $native.UndoSelectionActive)
        $case.redo_selection = @($native.RedoSelectionAnchor, $native.RedoSelectionActive)
        if ($native.UndoSelectionAnchor -ne $native.InsertionOffset -or
            $native.UndoSelectionActive -ne $native.InsertionOffset -or
            $native.RedoSelectionAnchor -ne ($native.InsertionOffset + 1) -or
            $native.RedoSelectionActive -ne ($native.InsertionOffset + 1)) {
            throw 'Undo/Redo did not restore the exact global or native caret.'
        }
        $expectedCommitted = $content.Insert([int]$native.InsertionOffset, '候')
        $committedHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
            [Text.Encoding]::Unicode.GetBytes($expectedCommitted)))
        $expectedSteps = @('dark-before', 'light-marked', 'light-settled',
            'dark-marked', 'dark-cancelled')
        $expectedThemes = @('mote-dark', 'mote-dark', 'mote-light',
            'mote-light', 'mote-dark')
        $expectedColors = @('#8DB9ED', '#8DB9ED', '#215FAD',
            '#215FAD', '#8DB9ED')
        $expectedDark = @($true, $false, $false, $true, $true)
        $expectedMarked = @($false, $true, $false, $true, $false)
        $expectedHashes = @($initialSourceHash, $initialSourceHash,
            $committedHash, $committedHash, $committedHash)
        $first = $native.Phases[0]
        for ($index = 0; $index -lt 5; $index++) {
            $phase = $native.Phases[$index]
            if ($phase.Step -cne $expectedSteps[$index] -or
                $phase.AppliedThemeId -cne $expectedThemes[$index] -or
                $phase.OsPrefersDark -ne $expectedDark[$index] -or
                $phase.HasMarkedText -ne $expectedMarked[$index] -or
                $phase.Generation -ne $first.Generation -or
                $phase.SourceSha256 -cne $expectedHashes[$index] -or
                $phase.PreviewHeadingRgb -cne $expectedColors[$index]) {
                throw 'Composition phase changed source or palette at the wrong time.'
            }
            if (($index -lt 2 -and $phase.Version -ne $first.Version) -or
                ($index -eq 2 -and $phase.Version -le $first.Version) -or
                ($index -gt 2 -and $phase.Version -ne $native.Phases[2].Version)) {
                throw 'Commit/cancel advanced canonical source version at the wrong time.'
            }
            if ($index -eq 0 -and
                $phase.SelectionAnchor -ne $phase.SelectionActive) {
                throw 'The initial native/global source caret was not collapsed.'
            }
            $expectedCaret = if ($index -eq 1 -and $mode -eq 'canvas') {
                [int]$first.SelectionAnchor
            }
            elseif ($index -eq 3 -and $mode -eq 'default') {
                [int]$native.InsertionOffset + 2
            }
            elseif ($index -gt 0) { [int]$native.InsertionOffset + 1 }
            else { [int]$first.SelectionAnchor }
            if ($phase.SelectionAnchor -ne $expectedCaret -or
                $phase.SelectionActive -ne $expectedCaret) {
                throw 'Composition phase caret was not the exact collapsed source/native boundary.'
            }
            $imageName = "composition-theme-$mode-$($expectedSteps[$index]).png"
            if ($phase.Image -cne $imageName) { throw 'Composition image name escaped the fixed contract.' }
            $image = Join-Path $output $imageName
            if (-not (Test-Path -LiteralPath $image -PathType Leaf) -or
                (Get-Item -LiteralPath $image).Length -le 100) {
                throw 'Composition PNG is absent or empty.'
            }
            $stream = [IO.File]::OpenRead($image)
            try {
                $header = [byte[]]::new(8)
                if ($stream.Read($header, 0, 8) -ne 8 -or
                    -not [Collections.StructuralComparisons]::StructuralEqualityComparer.Equals(
                        $header, $pngMagic)) {
                    throw 'Composition raster is not PNG.'
                }
            }
            finally { $stream.Dispose() }
            $hash = (Get-FileHash -LiteralPath $image -Algorithm SHA256).Hash
            if ($hash -cne $phase.ImageSha256) { throw 'Composition PNG hash disagrees with native JSON.' }
            $case.phases += [ordered]@{
                step = $phase.Step; os_prefers_dark = $phase.OsPrefersDark
                applied_theme_id = $phase.AppliedThemeId
                has_marked_text = $phase.HasMarkedText
                appearance_callbacks = $phase.AppearanceCallbacks
                composition_settled_callbacks = $phase.CompositionSettledCallbacks
                generation = $phase.Generation; version = $phase.Version
                selection_anchor = $phase.SelectionAnchor
                selection_active = $phase.SelectionActive
                source_sha256 = $phase.SourceSha256
                preview_heading_rgb = $phase.PreviewHeadingRgb
                image = $imageName; image_sha256 = $hash
            }
        }
        if ($native.Phases[1].AppearanceCallbacks -le $native.Phases[0].AppearanceCallbacks -or
            $native.Phases[1].CompositionSettledCallbacks -ne
                $native.Phases[0].CompositionSettledCallbacks -or
            $native.Phases[2].CompositionSettledCallbacks -le
                $native.Phases[1].CompositionSettledCallbacks -or
            $native.Phases[3].AppearanceCallbacks -le
                $native.Phases[2].AppearanceCallbacks -or
            $native.Phases[3].CompositionSettledCallbacks -ne
                $native.Phases[2].CompositionSettledCallbacks -or
            $native.Phases[4].CompositionSettledCallbacks -le
                $native.Phases[3].CompositionSettledCallbacks -or
            $case.phases[0].image_sha256 -ceq $case.phases[1].image_sha256 -or
            $case.phases[1].image_sha256 -ceq $case.phases[2].image_sha256 -or
            $case.phases[2].image_sha256 -ceq $case.phases[3].image_sha256 -or
            $case.phases[3].image_sha256 -ceq $case.phases[4].image_sha256) {
            throw 'Appearance/settlement callback or cached-raster transition was not observed.'
        }
        $case.status = 'passed'
    }
    catch {
        # Native stderr includes only fixed stage/check/type codes, never fixture text.
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
if ($result.status -ne 'passed') {
    throw 'Native Mac composition/theme workflow failed; inspect repository .cache report.'
}
