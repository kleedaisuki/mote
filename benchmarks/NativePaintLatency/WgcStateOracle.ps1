# Pure target-state oracle for every sampled WGC callback in one edit session.

# A recoverable invalid intermediate frame still invalidates sampled continuity.
function Test-SampledTargetState {
    param([int] $Pre, [int] $Post, [int] $Required, [object[]] $Frames)
    $invalid = 0
    $transition = $Pre -ne $Post
    foreach ($frame in $Frames) {
        if (($frame.target_flags -band $Required) -ne $Required -or
            $frame.target_flags -ne $Pre) { $invalid++ }
        if ($frame.target_flags -ne $Pre) { $transition = $true }
    }
    return [pscustomobject]@{
        sampled_frames = $Frames.Count; invalid_frames = $invalid
        transition = $transition
        valid = $Frames.Count -gt 0 -and -not $transition -and
            ($Pre -band $Required) -eq $Required -and
            ($Post -band $Required) -eq $Required -and $invalid -eq 0
    }
}
