# Audit normal-close native trace artifacts; no GUI, user files, or clock mixing.
# Dot-source this helper from the ordinary-file driver or artifact-only tests.

function Get-NativeTraceEvidence {
    param(
        [Parameter(Mandatory)][string] $TraceDirectory,
        [Parameter(Mandatory)][string] $OutputDirectory,
        [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{64}$')][string] $BinarySha256
    )
    $root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    $source = [IO.Path]::GetFullPath($TraceDirectory)
    $output = [IO.Path]::GetFullPath($OutputDirectory)
    foreach ($pair in @(@($source, '.temp'), @($output, '.cache'))) {
        $allowed = [IO.Path]::GetFullPath((Join-Path $root $pair[1]))
        if (-not $pair[0].StartsWith($allowed + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) { throw 'Native trace artifact escaped its repository area.' }
        $cursor = $pair[0]
        while ($cursor) {
            if (Test-Path -LiteralPath $cursor) {
                $item = Get-Item -LiteralPath $cursor
                if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                    throw 'Native trace artifact ancestry contains a reparse point.'
                }
            }
            $parent = [IO.Path]::GetDirectoryName($cursor)
            if (-not $parent -or $parent -ceq $cursor) { break }
            $cursor = $parent
        }
    }
    if (-not (Test-Path -LiteralPath $source -PathType Container)) { throw 'Normal-close trace directory is missing.' }
    $files = @(Get-ChildItem -LiteralPath $source -Force)
    if ($files.Count -lt 1 -or $files.Count -gt 8) { throw 'Expected one to eight normal-close trace rotation files.' }
    foreach ($file in $files) {
        if ($file.PSIsContainer -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            $file.Name -cnotmatch '^mote-trace-[a-f0-9]{32}-[0-9]{6}\.jsonl$' -or $file.Length -gt 32MB) {
            throw 'Invalid normal-close trace inventory.'
        }
    }
    if (Test-Path -LiteralPath $output) { throw 'Native trace output already exists; refuse evidence overwrite.' }
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $traceNames = [Collections.Generic.List[string]]::new()
    foreach ($file in ($files | Sort-Object Name)) {
        $destination = Join-Path $output $file.Name
        Copy-Item -LiteralPath $file.FullName -Destination $destination
        $traceNames.Add([IO.Path]::GetRelativePath($root, $destination).Replace('\', '/'))
    }
    $manifestPath = Join-Path $output 'manifest.json'
    $summaryPath = Join-Path $output 'summary.json'
    $manifest = @{schema_version=1; samples=@(@{sample_id='normal-close-readonly'; binary_sha256=$BinarySha256;
        traces=$traceNames.ToArray(); expected_records=@{'mote.session'=1; 'document.open'=1; 'document.open_to_editable'=1}})}
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
    & python (Join-Path $root 'benchmarks/NativeAcceptance/acceptance.py') summarize `
        --manifest $manifestPath --output $summaryPath | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Normal-close trace failed causal/session/loss integrity audit.' }
    $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json -AsHashtable
    $sample = $summary.samples[0]
    if (@($sample.operations.Keys | Where-Object {
        $_ -in @('document.edit', 'edit.committed', 'document.edit_to_draw_submission', 'document.save', 'save.completed') -or
            $_.StartsWith('save.failure.', [StringComparison]::Ordinal)
    }).Count -ne 0) { throw 'Read-only natural-close trace contains an edit or Save operation.' }
    $endpoints = [ordered]@{}
    foreach ($operation in @('document.open', 'mote.startup_to_editable',
        'document.open_to_editable', 'document.open_to_draw_submission')) {
        $duration = $null
        $state = 'missing'
        $counts = @{success=0; cancelled=0; failure=0; skipped=0}
        if ($sample.operations.ContainsKey($operation)) {
            $data = $sample.operations[$operation]
            $counts = $data.outcomes
            $total = $counts.success + $counts.cancelled + $counts.failure + $counts.skipped
            $state = 'ambiguous-multiple-records'
            if ($total -eq 1) {
                $state = @('success','cancelled','failure','skipped') | Where-Object { $counts[$_] -eq 1 } | Select-Object -First 1
                if ($state -eq 'success') { $duration = $data.success_duration.p50_us / 1000.0 }
            }
        }
        $endpoints[$operation] = @{status=$state; duration_ms=$duration; outcomes=$counts}
    }
    $complete = @($endpoints.Values | Where-Object {$_.status -ne 'success'}).Count -eq 0
    return @{causal_integrity=$sample.causal_integrity; terminal_session='one-successful-root'; dropped_records=$sample.dropped_records;
        endpoint_status=if($complete){'collected-successful-endpoints'}else{'collected-with-missing-or-censored-endpoint'};
        endpoints=$endpoints; trace_artifact=[IO.Path]::GetRelativePath($root,$output).Replace('\','/');
        trace_sha256=$sample.trace_sha256; endpoint='native-source-draw-callback-return-not-physical-presentation'}
}
