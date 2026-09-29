# Diagnose whether a hosted macOS runner permits external Accessibility GUI automation.
param(
    [Parameter(Mandatory)][string] $ExecutablePath,
    [Parameter(Mandatory)][string] $ReportPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsMacOS) { throw 'This probe requires macOS.' }

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$scratchRoot = [IO.Path]::GetFullPath((Join-Path $root '.temp/mac-accessibility'))
$scratch = [IO.Path]::GetFullPath((Join-Path $scratchRoot ([guid]::NewGuid().ToString('N'))))
if (-not $scratch.StartsWith($scratchRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
    throw 'Mac accessibility scratch path escaped the repository.'
}
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
$report = [IO.Path]::GetFullPath($ReportPath)
$inventoryRoot = [IO.Path]::GetFullPath((Join-Path $root '.cache/ci-inventory'))
if (-not $report.StartsWith($inventoryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
    throw 'Mac accessibility report must remain under repository .cache/ci-inventory.'
}
New-Item -ItemType Directory -Force -Path (Split-Path $report) | Out-Null

$editor = $null
$oldHome = $env:MOTE_HOME
$result = [ordered]@{
    status = 'unknown'
    accessibility_exit_code = $null
    accessibility_stdout = ''
    accessibility_stderr = ''
    editor_exited_early = $false
    note = 'External GUI automation probe only; no text was edited or saved.'
}
try {
    $exe = [IO.Path]::GetFullPath($ExecutablePath)
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Executable not found: $exe" }
    $path = Join-Path $scratch 'probe.txt'
    [IO.File]::WriteAllText($path, "accessibility probe`n", [Text.UTF8Encoding]::new($false))
    $env:MOTE_HOME = Join-Path $scratch 'home'
    $editor = Start-Process -FilePath $exe -ArgumentList $path -PassThru `
        -RedirectStandardOutput (Join-Path $scratch 'editor-stdout.txt') `
        -RedirectStandardError (Join-Path $scratch 'editor-stderr.txt')
    Start-Sleep -Milliseconds 1500
    $editor.Refresh()
    if ($editor.HasExited) {
        $result.status = 'editor-exited-before-accessibility-check'
        $result.editor_exited_early = $true
    }
    else {
        # Left Arrow is a harmless native key event; asking System Events to target the
        # application tests TCC/Accessibility permission without editing document bytes.
        $script = Join-Path $scratch 'probe.applescript'
        [IO.File]::WriteAllText($script, @'
tell application "System Events"
    set enabled to UI elements enabled
    set targetProcess to first process whose name is "mote"
    set frontmost of targetProcess to true
    key code 123
    return "ui_elements_enabled=" & enabled
end tell
'@)
        $start = [Diagnostics.ProcessStartInfo]::new('/usr/bin/osascript')
        [void]$start.ArgumentList.Add($script)
        $start.UseShellExecute = $false
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $automation = [Diagnostics.Process]::Start($start)
        if (-not $automation.WaitForExit(15000)) {
            $automation.Kill()
            $automation.WaitForExit()
            $result.status = 'accessibility-timed-out'
        }
        else {
            $result.accessibility_exit_code = $automation.ExitCode
            $result.status = if ($automation.ExitCode -eq 0) { 'external-key-event-succeeded' }
                else { 'external-accessibility-unavailable' }
        }
        $result.accessibility_stdout = $automation.StandardOutput.ReadToEnd().Trim()
        $result.accessibility_stderr = $automation.StandardError.ReadToEnd().Trim()
        $automation.Dispose()
    }
}
catch {
    $result.status = 'probe-error'
    $result.accessibility_stderr = $_.Exception.Message
}
finally {
    if ($editor -and -not $editor.HasExited) {
        try { Stop-Process -Id $editor.Id -Force }
        catch { Write-Warning "Could not stop GUI probe process: $($_.Exception.Message)" }
    }
    $env:MOTE_HOME = $oldHome
    $result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $report -Encoding utf8
    Get-Content -LiteralPath $report
    if ($scratch.StartsWith($scratchRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal) -and
        (Test-Path -LiteralPath $scratch)) { Remove-Item -LiteralPath $scratch -Recurse -Force }
}

# TCC denial is an observed limitation of this runner, not a product failure. The JSON
# report is uploaded in CI; unlike a skipped test, it records the attempted OS interaction.
if ($result.status -eq 'external-accessibility-unavailable') {
    Write-Warning 'Hosted runner denied external Accessibility scripting; use a pre-granted interactive runner.'
}
elseif ($result.status -ne 'external-key-event-succeeded') {
    Write-Warning "External GUI automation probe did not complete: $($result.status)."
}
