# Artifact-only natural-close trace tests; never launch or call a native GUI.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'NativeTraceEvidence.ps1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$id = [guid]::NewGuid().ToString('N')
$scratch = Join-Path $root ".temp/native-trace-evidence-tests/$id"
$output = Join-Path $root ".cache/native-trace-evidence-tests/$id"
foreach ($path in @($scratch, $output)) {
    $cursor = [IO.Path]::GetFullPath($path)
    while ($cursor) {
        if ((Test-Path -LiteralPath $cursor) -and
            ((Get-Item -LiteralPath $cursor).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'Native trace test ancestry contains a reparse point.'
        }
        $parent = [IO.Path]::GetDirectoryName($cursor)
        if (-not $parent -or $parent -ceq $cursor) { break }
        $cursor = $parent
    }
}
$checks = [Collections.Generic.List[string]]::new()
function New-Record {
    param([string] $Operation, [int] $Span, [int] $Parent=1, [string] $Status='success')
    return @{schema_version=1;utc_time='2026-10-01T00:00:00Z';session_id=('a'*32);trace_id=('b'*32);
        span_id=$Span.ToString('x16');parent_span_id=if($Operation -eq 'mote.session'){$null}else{$Parent.ToString('x16')};
        operation=$Operation;duration_us=1000*$Span;status=$Status;
        attributes=if($Operation -eq 'mote.session'){@{}}else{@{version=0}}}
}
function Invoke-EvidenceCase {
    param([string] $Name, [object[]] $Records)
    $source = Join-Path $scratch $Name
    New-Item -ItemType Directory -Path $source -Force | Out-Null
    $file = Join-Path $source ('mote-trace-'+('a'*32)+'-000001.jsonl')
    $lines = @($Records | ForEach-Object { $_ | ConvertTo-Json -Depth 5 -Compress })
    [IO.File]::WriteAllLines($file, $lines, [Text.UTF8Encoding]::new($false))
    return Get-NativeTraceEvidence -TraceDirectory $source -OutputDirectory (Join-Path $output $Name) -BinarySha256 ('c'*64)
}
New-Item -ItemType Directory -Path $scratch, $output -Force | Out-Null
try {
    $base = @((New-Record 'mote.session' 1), (New-Record 'document.open' 2),
        (New-Record 'document.open_to_editable' 3), (New-Record 'mote.startup_to_editable' 4))
    $success = Invoke-EvidenceCase 'complete' ($base + (New-Record 'document.open_to_draw_submission' 5 3))
    if ($success.endpoint_status -cne 'collected-successful-endpoints' -or
        $success.endpoints['document.open_to_draw_submission'].duration_ms -ne 5) { throw 'Complete endpoint failed.' }
    $checks.Add('normal-session-and-child-duration')
    $cancelled = Invoke-EvidenceCase 'cancelled' ($base + (New-Record 'document.open_to_draw_submission' 5 3 'cancelled'))
    if ($cancelled.endpoints['document.open_to_draw_submission'].status -cne 'cancelled' -or
        $null -ne $cancelled.endpoints['document.open_to_draw_submission'].duration_ms) { throw 'Cancelled draw became a successful duration.' }
    $checks.Add('cancelled-draw-censored')
    $missing = Invoke-EvidenceCase 'missing' $base
    if ($missing.endpoints['document.open_to_draw_submission'].status -cne 'missing' -or
        $null -ne $missing.endpoints['document.open_to_draw_submission'].duration_ms) { throw 'Missing draw became success.' }
    $checks.Add('missing-draw-explicit')
    try {
        Invoke-EvidenceCase 'unexpected-edit' ($base + (New-Record 'document.edit' 7)) | Out-Null
        throw 'Canonical edit unexpectedly accepted.'
    } catch {
        if ($_.Exception.Message -cne 'Read-only natural-close trace contains an edit or Save operation.') { throw }
        $checks.Add('canonical-edit-refused')
    }
    try {
        Invoke-EvidenceCase 'unexpected-save' ($base + (New-Record 'document.save' 7)) | Out-Null
        throw 'Save unexpectedly accepted.'
    } catch {
        if ($_.Exception.Message -cne 'Read-only natural-close trace contains an edit or Save operation.') { throw }
        $checks.Add('save-refused')
    }
    try {
        Invoke-EvidenceCase 'no-terminal' ($base | Where-Object {$_.operation -ne 'mote.session'}) | Out-Null
        throw 'Missing terminal session unexpectedly accepted.'
    } catch {
        if ($_.Exception.Message -cne 'Normal-close trace failed causal/session/loss integrity audit.') { throw }
        $checks.Add('missing-terminal-refused')
    }
    $drop = New-Record 'telemetry.dropped' 6
    $drop.attributes = @{count=1}
    try {
        Invoke-EvidenceCase 'dropped' ($base + $drop) | Out-Null
        throw 'Dropped records unexpectedly accepted.'
    } catch {
        if ($_.Exception.Message -cne 'Normal-close trace failed causal/session/loss integrity audit.') { throw }
        $checks.Add('dropped-records-refused')
    }
    [pscustomobject]@{scope='synthetic-artifact-only-no-native-process';passed=$checks.Count;checks=$checks.ToArray()}
} finally {
    # Only files and empty directories created by this test are removed, never
    # recursive traversal or an arbitrary path reconstructed from input records.
    foreach ($basePath in @($scratch, $output)) {
        foreach ($directory in @(Get-ChildItem -LiteralPath $basePath -Directory)) {
            foreach ($file in @(Get-ChildItem -LiteralPath $directory.FullName -File)) {
                Remove-Item -LiteralPath $file.FullName
            }
            [IO.Directory]::Delete($directory.FullName)
        }
        [IO.Directory]::Delete($basePath)
    }
}
