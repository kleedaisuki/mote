param([string]$RunName = 'inline-detached', [int]$Pairs = 5)
$ErrorActionPreference = 'Stop'
# Fresh-process interleaving compares the same Engine and exact edit geometry;
# the 100 ms continuation-unwind control stays outside measured Open/Edit intervals.
$fixture = '.temp/JsonArrayPages/corpora/ordinary-lf-100.json'
$output = Join-Path '.cache/engine-retained-memory' $RunName
New-Item -ItemType Directory -Force $output | Out-Null
for ($pair = 1; $pair -le $Pairs; $pair++) {
    $modes = if ($pair % 2) { @('inline', 'detached') } else { @('detached', 'inline') }
    foreach ($mode in $modes) {
        dotnet .temp/EngineRetainedMemory/control-bin/EngineRetainedMemory.dll $mode $fixture (Join-Path $output "$mode-$pair.json")
        if ($LASTEXITCODE -ne 0) { throw "Control process failed: $mode / $pair" }
    }
}
