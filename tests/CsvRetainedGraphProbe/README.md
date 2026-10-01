# CoreCLR CSV retained-graph probe

This is a **CoreCLR-only** developer experiment, not a Native AOT test, application startup benchmark, or GUI acceptance test. It uses the public policy/session API. After creating one session, it leaves a non-inlined setup frame, performs a full GC, prints its PID, and keeps only the session intentionally rooted for three minutes. The text owner and projection are not part of the measured session graph. `dense` retains exactly 256 full row blocks; `widths` has one 1,024-record block with 1,024 distinct column widths; `sparse` contains roughly 100 Mi UTF-16 units of 49-unit CRLF records.

On Windows, from repository root:

```powershell
dotnet build tests/CsvRetainedGraphProbe/CsvRetainedGraphProbe.csproj -c Release -warnaserror --artifacts-path .cache/csv-retained-probe
New-Item .cache/dotnet-tools, .cache/csv-retained-graph -ItemType Directory -Force | Out-Null
# Scope NuGet tool-install caches/scratch to the repository; restore environment afterward.
$names = @('NUGET_PACKAGES', 'NUGET_HTTP_CACHE_PATH', 'NUGET_SCRATCH', 'TEMP', 'TMP')
$saved = @{}
foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
  foreach ($name in $names) {
    $path = Join-Path (Get-Location).Path ('.cache/dotnet-tool-install/' + $name)
    New-Item $path -ItemType Directory -Force | Out-Null
    [Environment]::SetEnvironmentVariable($name, $path, 'Process')
  }
  dotnet tool install dotnet-dump --version 10.0.745401 --tool-path .cache/dotnet-tools
} finally {
  foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
}
$root = (Get-Location).Path
$exe = Join-Path $root '.cache/csv-retained-probe/bin/CsvRetainedGraphProbe/release/CsvRetainedGraphProbe.exe'
$p = Start-Process -FilePath $exe -ArgumentList 'dense' -WindowStyle Hidden -PassThru `
  -RedirectStandardOutput (Join-Path $root '.cache/csv-retained-graph/dense-ready.txt') `
  -RedirectStandardError (Join-Path $root '.cache/csv-retained-graph/dense-stderr.txt')
# Wait for READY, then collect before the three-minute timeout.
Get-Content .cache/csv-retained-graph/dense-ready.txt
.cache/dotnet-tools/dotnet-dump.exe collect --process-id $p.Id --type Heap --output .cache/csv-retained-graph/dense.dmp
.cache/dotnet-tools/dotnet-dump.exe analyze .cache/csv-retained-graph/dense.dmp `
  -c 'dumpheap -type Mote.Formats.CsvIncrementalSession -stat' -c exit
```

Identify the exact session MethodTable (`Class Name` is exactly `Mote.Formats.CsvIncrementalSession`, not a nested type), use `dumpheap -mt <methodtable> -short` to find its address, then run `objsize <address> -stat` and `gcroot <address>`. Addresses differ each process. `objsize` measures transitive reachable objects, deduplicating shared child objects; the `dumpheap -type` substring total alone is **not** the session's retained graph because it includes unrelated/static nested instances and omits dictionaries and their arrays. Repeat with `widths` and `sparse`. A `gcroot` generated static-variable label was misleading on the local runtime (`Object.s_osVersion`); the selected session graph and strong static root address are inspectable independently, so do not interpret that label as a source ownership fact.

Keep all dumps under root `.cache/` or `.temp/`, do not upload them as CI artifacts: source can remain elsewhere in the dump even after collection. Stop only the specific probe PID once collection completes, or let its bounded wait finish. The session has no source strings or returned projections in its transitive graph in the observed dumps. See [the budget design](../../docs/csv-index-budget-design.md) for actual local measurements, assumptions, and platform limitations.
