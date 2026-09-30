# Deterministic no-GUI test of sampled target-state continuity, including recovery.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'WgcStateOracle.ps1')

# A foreground/focus loss between valid endpoints must never be accepted.
$recovered = @(
    [pscustomobject]@{ target_flags = 31 },
    [pscustomobject]@{ target_flags = 23 },
    [pscustomobject]@{ target_flags = 31 }
)
$result = Test-SampledTargetState 31 31 31 $recovered
if ($result.valid -or -not $result.transition -or $result.invalid_frames -ne 1) {
    throw 'Recovered invalid intermediate frame was falsely accepted.'
}

# Every sampled frame and both boundary states must satisfy all required bits.
$valid = @([pscustomobject]@{ target_flags = 31 },
    [pscustomobject]@{ target_flags = 31 })
$result = Test-SampledTargetState 31 31 31 $valid
if (-not $result.valid -or $result.transition -or $result.invalid_frames -ne 0) {
    throw 'Unchanged fully valid sampled state was rejected.'
}

# Stable local background still cannot be promoted to visible latency evidence.
$background = @([pscustomobject]@{ target_flags = 23 })
$result = Test-SampledTargetState 23 23 31 $background
if ($result.valid -or $result.transition -or $result.invalid_frames -ne 1) {
    throw 'Stable missing-foreground state was falsely accepted.'
}
Write-Output 'wgc-sampled-state-oracle-ok'
