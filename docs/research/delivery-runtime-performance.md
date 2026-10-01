# Delivery runtime performance: bounded headless mechanism probe

Date: 2026-10-02 (Asia/Singapore); measurements completed at 2026-10-01T18:20:22Z.
Status: **local Windows x64 control completed; no mote GUI comparison, production
change, CI dispatch, or delivery-contract change.**

## Decision and evidence boundary

Native AOT remains a technically useful option when strict one-on-disk-binary
distribution, fast fresh-process initialization, and a small shipped runtime matter.
Relaxing delivery constraints makes self-contained CoreCLR JIT and ReadyToRun
viable alternatives, but does not establish that either improves mote's real user
experience. In this small, identical-input control, AOT had materially lower
launch-to-parse-ready latency, artifact footprint, and post-ready process memory.
R2R reduced observed JIT-startup cost without changing the runtime architecture.
**None of the numbers below measures mote's GUI, engine, format policies, large
files, first editable source, first paint, IME, typing, Save, or sustained work.**

This experiment intentionally compares normal untrimmed self-contained JIT/R2R
deployment against necessarily trimmed Native AOT. It is not an equal-dead-code
comparison or an experiment isolating code-generation effects from trimming.
Nor does a 191-file folder publish predict a bundled, compressed, or extracted
single-file publish. Those were not built or timed here.

## Existing repository knowledge, reused rather than rebenchmarked

- [Native performance baseline](../native-performance-baseline.md): hosted AOT
  engine timings and GUI smoke timings have different endpoints. GUI smoke times
  launch through exit; its ready marker and a shown window are not physical paint.
- [Ordinary startup probe](../../benchmarks/NativeStartup/README.md): the later
  Windows benchmark separately observes HWND, requested-file title, exact source
  prefix, selection acknowledgement, and Save. Synthetic messages bypass physical
  keyboard delivery. Existing local source-readiness measurements have explicit
  cache, foreground, and sampling limits; they are not JIT-vs-AOT comparisons.
- [Requested-file open attribution](../performance/native-json-open-attribution.md):
  initial empty-buffer readiness, requested-file readiness, full analysis, and
  draw submission are different endpoints. Changing compilation cannot be assumed
  to fix a document/projection/native-control bottleneck.
- [Strict one-binary feasibility](../single-binary-feasibility.md): self-extraction
  is not strict one-on-disk-file operation, and dynamic native UI dependencies
  cannot be removed just by switching a publish property.

The probe references **no production project or live engine/policy source**. Its
entire C# workload and project file are frozen in the reproduction appendix.
Checkout HEAD at setup was `6827cd1a19142c9ad766d296c18d0770e600e79c`; that HEAD
identifies research context, not a mote executable being timed. Concurrent source
changes cannot change this standalone program's workload.

## Workload, host, and controlled variables

| Item | Recorded configuration |
| --- | --- |
| Host | Windows 10.0.26200, x64; Intel Core i9-12900H, 20 logical processors |
| Installed physical RAM | 34,087,665,664 bytes (31.75 GiB) |
| SDK | .NET SDK 10.0.400, commit `14fbf8d527`; MSBuild 18.9.6 |
| Selected runtime | Explicit `RuntimeFrameworkVersion=10.0.11`; child-reported version 10.0.11 in all 111 observations |
| Python | 3.14.6; `perf_counter_ns` parent monotonic clock |
| Native build prerequisite | Visual Studio 18 Community, C++ x64 tools present; no toolchain installation |
| RID / architecture | `win-x64`; child-reported `X64` in every mode |
| Input | One pre-generated compact JSON array, 4,096 records, 224,171 bytes |
| Work | `File.ReadAllBytes`, `Utf8JsonReader`, numeric token count and checksum; all bytes consumed |
| Correctness marker | Exact `READY 4096 8386560 224171` before every recorded timing completes |
| Runtime environment | No inherited `DOTNET_*` or `COMPlus_*` variables in timed children |
| Scratch | `.temp/delivery-runtime-research-20261002/`; all experiment artifacts and SDK build intermediates inside this directory |
| Build | Release, self-contained, `DebugType=none`, `StripSymbols=true`, `-warnaserror`, `-m:1`; compiler processes below normal priority |
| GUI / OS changes | None; no GUI, cache eviction, foreground stealing, process-wide machine tuning, or new workflow |

The shared developer machine was not reserved, CPU affinity/power state was not
pinned, and the main team could be active. Five existing dotnet processes were
present before the measured batch; their printed cumulative CPU counters were
unchanged at the subsequent snapshot, but two snapshots do not certify an idle
machine. All child launches were sequential and used identical process/pipe
creation. Compilation was completed before timing; no failed or successful
publish operation was included in a startup stopwatch.

## Measurement procedure

1. Generate the JSON fixture once **before** launch timing. Enumerate and hash
   every final publish-root file before any measured sample. This deliberately
   warms filesystem pages; no sample is disk-cold. The prior AOT functional sanity
   invocation also means the first AOT measurement is not first-ever execution.
2. Collect three labelled setup observations in AOT/JIT/R2R order. Preserve but
   exclude them from the main summary. Their times were 15.8645 / 127.3788 /
   107.1195 ms, respectively. They are not cold, first-user-launch, or balanced
   measurements.
3. Run all six permutations of AOT/JIT/R2R, repeat the six-order cycle six times:
   36 blocks, **36 fresh processes per mode**, 108 main observations. Sleep 50 ms
   between samples outside the stopwatch. The exact block-order table is below.
4. Start the parent stopwatch immediately before `subprocess.Popen`. A reused
   observer thread reads the child's stdout line and timestamps the completed
   read. Stop latency at the **synchronous flushed READY line**, not child exit.
   Pipe/observer dispatch and scheduler delay are part of this measurement.
5. Validate exact marker, then send a newline to release the child from its READY
   hold. The child records process working set/private bytes/peak working set/CPU
   and runtime architecture/features, then exits normally. Validate metrics shape,
   exit code 0 and empty stderr. Every one of 111 observations passed.

Memory is a **post-ready, live-process** point observation, after the release
signal and `Process.GetCurrentProcess` creation. It is not memory at the precise
marker, a sustained workload footprint, managed allocation, or an after-exit
counter. The retained peak field extends through counter collection; CPU includes
that instrumentation and Windows CPU-accounting quantization. Neither is inside
the launch-to-ready stopwatch. The instrumentation can allocate a little memory,
so cross-mode values are a bounded control, not a GUI memory budget.

### Main results

| Mode | Final publish files | Final publish bytes | Launch→READY median | MAD | Observed min–max | Post-ready working-set median | Post-ready private-byte median |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Native AOT | 1 | 1,675,264 (1.60 MiB) | 10.73 ms | 0.76 ms | 9.25–13.91 ms | 9.31 MiB | 4.07 MiB |
| Self-contained JIT | 191 | 80,369,503 (76.65 MiB) | 71.69 ms | 1.48 ms | 66.77–85.52 ms | 30.10 MiB | 12.19 MiB |
| Self-contained R2R | 191 | 80,384,351 (76.66 MiB) | 58.74 ms | 1.45 ms | 56.20–64.88 ms | 24.91 MiB | 7.46 MiB |

MAD is median absolute deviation. Within-block differences were all positive:
JIT minus AOT median **60.60 ms**, range **56.26–75.52 ms**; R2R minus AOT median
**47.96 ms**, range **44.54–54.88 ms**. Those gaps are much larger than the local
spread and are a useful mechanism signal for this workload. Do not transfer the
gap, ratio, or a multiplicative speedup to mote's GUI.

The local results JSON also stores a sample-bootstrap median interval (5,000
resamples, Python seed 20261002) and an observed nearest-rank order statistic.
Those are **not** a reliable p95 SLA or a confidence interval over machines, cold
cache states, real users, or background-load distributions. Balanced process
repetitions do not remove systematic environmental bias or establish independence.
The report deliberately uses median/MAD/range rather than publishing a tail claim.

### Artifact identity and compilation validation

The inventories above count only **final publish-root recursive files**, including
all shipped framework/native files. AOT intermediate `bin`/`obj` inventories are
irrelevant to a final one-file delivery claim. No symbols or hidden sidecars were
discarded manually to obtain AOT's one-file result.

| Artifact | SHA-256 |
| --- | --- |
| AOT `RuntimeProbe.exe` | `67992083abc5d3c330e7f3312bd00bab9db11d96c753bb09302de70b1fae897f` |
| JIT and R2R `RuntimeProbe.exe` (same apphost) | `7cf3b9a6f2213dadaffabacca40746e18d9c92f88ce2a3e09bff5cec6d21ea90` |
| JIT `RuntimeProbe.dll`, 5,632 bytes | `4d60629771310a3063a7e0cdba360d6ce266511c84fe45ec5d46c66770bced33` |
| R2R `RuntimeProbe.dll`, 20,480 bytes | `32e3210a19bff5564074169e5766b4feaf3ebf1301d46162dd6bd00f2890a22e` |
| Program source | `c635eff5e4f79b386bbe46962316d95784943e5e574bc24d36c75a083854fd0e` |
| Final project file | `96c82cb87c99b9c877aac6a102e9cfbfaf3c98fdafc8cf6142a835cabcde7d56` |
| Fixture | `dfc146c511aa98d3e0be533d8628821486561aa84e4b97f5389862f76e01f4f8` |

Every JIT/R2R inventory entry was byte-identical **except `RuntimeProbe.dll`**.
The 14,848-byte difference is not a forecast for a larger GUI's R2R size increase.
The exact same apphost hash does not identify an entire CoreCLR application;
the managed DLL and full inventory must accompany it. Dynamic code supported /
compiled was `False/False` for AOT and `True/True` for both CoreCLR modes in all
samples. This validates runtime regimes, not how many methods were actually JITed.

### Negative result and build costs

The first JIT publish succeeded in 1.931 s. Initial R2R and AOT builds failed with
CS0579 duplicate assembly attributes because SDK default compile globs included
another mode's generated C# under the custom in-project `artifacts/` directory.
The fix was **one explicit `Compile Include="Program.cs"` and
`EnableDefaultCompileItems=false`**, eliminating the special case rather than
adding path-specific excludes. Initial failure commands/logs are retained. JIT's
first build compiled the same frozen `Program.cs` before other modes' generated
files existed; its completed validation was not repeated. The corrected project
changes compile inventory, not source behavior.

After the fix R2R publish succeeded in **1.994 s**, AOT in **8.464 s**, both with
warnings treated as errors. These one-off SDK/package-cache observations are not
build-time benchmarks; the first R2R attempt had already restored dependencies.
No repeat build-time distribution or product-scale cost estimate is claimed.

## Mechanisms and choices: production documentation

[Microsoft's Native AOT documentation](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
describes publish-time native compilation without runtime JIT and an
OS/architecture-specific deployment. Its static world excludes runtime dynamic
code generation and loading patterns; trimming/reflection compatibility must be
verified across the complete application's dependencies. AOT does **not** mean no
GC, no runtime, no startup work, or no OS library dependencies.

[Microsoft's R2R documentation](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run)
explains the IL-plus-native-code trade-off, remaining JIT cases, and tiered
replacement of frequently used R2R methods. Framework libraries already contain
R2R code, so a small application's R2R difference can be modest in artifact size.
The docs warn that extra code size can increase loading and working-set cost;
that potential trade-off was not realized as a slowdown in this warm-cache control.
No composite R2R or ahead-of-time profile tuning is justified by this probe.

[Runtime compilation configuration](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/compilation)
distinguishes quick initial compilation/precompiled code from background optimized
tiers and dynamic profile-guided optimization (PGO). Restarting fresh processes
keeps measuring initialization/first-use effects; it does **not** evaluate warmed,
long-lived JIT throughput. Avoid changing tiered compilation or PGO defaults to
win this startup microprobe at the expense of actual sustained editing.

[Single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
is a packaging decision separate from compilation. Native runtime libraries are
normally sidecars; `IncludeNativeLibrariesForSelfExtract=true` embeds files that
are then extracted to disk. Thus relaxing strict no-extraction may enable a
single download, but it adds cache/temp-directory, integrity and first-extraction
costs. A warmed extracted cache, first extraction, and disk-cold executable launch
must be reported as separate conditions, not mixed into one “cold startup.”

[The .NET team's .NET 10 performance report](https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-10/)
documents ongoing JIT/PGO and Native AOT code-size improvements. Its many benchmark
examples concern their stated workloads, not this editor. That production lesson
supports measuring initialization and sustained application behavior separately,
rather than assuming “AOT is always faster” or “JIT eventually wins everywhere.”

## Academic frontier: useful design signal, not a replacement runtime

Fallin and Bernstein, *Partial Evaluation, Whole-Program Compilation*,
Proceedings of the ACM on Programming Languages 9 (PLDI), 2025,
[DOI 10.1145/3729259](https://doi.org/10.1145/3729259),
[author manuscript](https://cfallin.org/pubs/pldi2025_weval.pdf), derive ahead-of-time
guest-language code from an existing interpreter using partial evaluation. The
important connection is that compilation timing, runtime adaptation, and a single
source of semantic truth need not be treated as inseparable choices. Its
SpiderMonkey/Lua and WebAssembly setting is **not a .NET GUI comparison or a
drop-in option for mote**. The grounded near-term design consequence is to retain
one canonical engine/policy implementation when experimenting with publish modes,
not fork semantics to chase runtime-specific microbenchmark wins. Research on
moving specialization out of user startup may matter later, but implementing a
new compiler or embedding this tooling is unjustified here.

## Next experiment only if the delivery decision makes it worthwhile

The next discriminating experiment is a **root-coordinated GitHub experiment**,
not dispatched by this research agent. Use an immutable source revision or full
source snapshot, isolated workspaces/outputs per publish variant and RID, fixed
runtime/SDK, explicit trim settings, identical fixtures, and equivalent ordinary
application arguments. Any override of mote's present `PublishAot=true` and its
AOT analyzers is experimental, not a production default change.

| Required dimension | Test and decision relevance |
| --- | --- |
| First source usability | External ordinary launch → exact requested-file source prefix and selection acknowledgement, separate from blank-window readiness |
| First presentation | External paint/presentation-capable observer; distinguish draw submission from physical first paint |
| User interaction | OS-delivered focused typing, paste, undo and Save, then IME/accessibility; synthetic WM_CHAR is a separate capability label |
| Representative files | Small real document plus 1/10/100 MiB text/JSON/Markdown and a long line; do not make a large full parse the startup default |
| Sustained runtime | Warm editing/scrolling workload after JIT tiers stabilize; allocation, pause behavior, responsiveness and process memory together |
| Packaging variants | Folder self-contained first; separately measure true bundled/extracted alternatives only if the new contract permits them |
| Cache/security classes | Warm process launches, first observed launch, true controlled disk-cold, first bundle extraction, and signed first user launch labelled separately |
| Comparison design | Balanced within-host paired orders, adequate repetitions, raw samples and artifact manifests; never cross-compare hosted OSes as a runtime speed ranking |

Do not use the existing strict-one-file startup harness unchanged for folder
JIT/R2R: its correct inventory preflight will reject 191-file publishes. An
experiment-specific manifest/launcher adapter must preserve the old strict
contract and retain every published file. The small JSON checksum workload here
is merely a cheap mechanism probe; shipping a new runtime is worthwhile only if
the product's installation, compatibility or real user workload benefits justify
its additional runtime payload, update policy, security and validation cost.

## Durable main timing samples

Values are parent launch-to-READY milliseconds, in original block order. The
complete 111-row JSONL (including memory/runtime evidence and setup rows), full
per-file hashes, publish commands, build logs and failures are retained under
`.temp/delivery-runtime-research-20261002/`. The main timings are also preserved
here so losing ignored scratch does not erase the measured latency baseline.

| Block | Mode order | AOT ms | JIT ms | R2R ms |
| ---: | --- | ---: | ---: | ---: |
| 0 | aot→jit→r2r | 12.1403 | 77.7239 | 64.0730 |
| 1 | aot→r2r→jit | 13.9097 | 71.8077 | 63.2642 |
| 2 | jit→aot→r2r | 11.6106 | 71.4663 | 59.2826 |
| 3 | jit→r2r→aot | 11.5052 | 70.3976 | 64.5148 |
| 4 | r2r→aot→jit | 11.4855 | 79.1027 | 64.7532 |
| 5 | r2r→jit→aot | 10.2391 | 72.0502 | 61.5047 |
| 6 | aot→jit→r2r | 12.1347 | 74.8301 | 59.2996 |
| 7 | aot→r2r→jit | 10.4454 | 69.1201 | 57.7087 |
| 8 | jit→aot→r2r | 9.2507 | 69.9064 | 58.0858 |
| 9 | jit→r2r→aot | 11.7438 | 70.5650 | 56.2836 |
| 10 | r2r→aot→jit | 10.7412 | 69.6338 | 58.2577 |
| 11 | r2r→jit→aot | 9.8898 | 69.5949 | 57.0181 |
| 12 | aot→jit→r2r | 11.3217 | 71.8970 | 61.5707 |
| 13 | aot→r2r→jit | 12.4326 | 70.5932 | 60.0866 |
| 14 | jit→aot→r2r | 10.7031 | 71.3760 | 57.6098 |
| 15 | jit→r2r→aot | 11.8093 | 70.6482 | 57.1045 |
| 16 | r2r→aot→jit | 11.5367 | 70.0258 | 58.3523 |
| 17 | r2r→jit→aot | 9.3828 | 72.6572 | 62.6091 |
| 18 | aot→jit→r2r | 11.5014 | 74.8544 | 60.3366 |
| 19 | aot→r2r→jit | 9.9998 | 85.5242 | 64.8828 |
| 20 | jit→aot→r2r | 10.6871 | 74.3281 | 62.0353 |
| 21 | jit→r2r→aot | 10.0266 | 73.0817 | 58.7812 |
| 22 | r2r→aot→jit | 11.4057 | 73.2020 | 57.6253 |
| 23 | r2r→jit→aot | 9.7044 | 72.7625 | 60.9983 |
| 24 | aot→jit→r2r | 12.2408 | 72.7011 | 57.8120 |
| 25 | aot→r2r→jit | 11.2529 | 72.3607 | 59.1860 |
| 26 | jit→aot→r2r | 11.4240 | 74.8955 | 60.2712 |
| 27 | jit→r2r→aot | 10.4054 | 73.0585 | 59.8482 |
| 28 | r2r→aot→jit | 9.4667 | 69.2214 | 57.5682 |
| 29 | r2r→jit→aot | 9.9826 | 69.0094 | 57.3787 |
| 30 | aot→jit→r2r | 10.7099 | 69.6632 | 58.6897 |
| 31 | aot→r2r→jit | 10.5042 | 66.7680 | 57.1649 |
| 32 | jit→aot→r2r | 9.9573 | 70.2392 | 58.4149 |
| 33 | jit→r2r→aot | 9.6362 | 69.2408 | 56.2005 |
| 34 | r2r→aot→jit | 10.0649 | 71.5781 | 57.1199 |
| 35 | r2r→jit→aot | 11.3299 | 71.9498 | 57.4725 |

## Reproduction appendix: independent source and scripts

Create the files below inside a new repository-local directory whose name begins
`.temp/delivery-runtime-research-`. The fixture generator writes exact expected
bytes using Python's insertion-preserving object order and compact JSON separators.
Use the recorded SDK/runtime, Windows x64 native compiler and Python to recreate
the workload; toolchain updates will legitimately change binary hashes/results.
All working/build paths must stay under the new directory. No project reference,
copy from mutable mote source, or shared build output is required.

```powershell
python .temp/delivery-runtime-research-20261002/build_probe.py
python .temp/delivery-runtime-research-20261002/measure_probe.py
```

The measurement script regenerates neither fixture nor executables. It hashes
final publish-root artifacts, validates readiness for every run, and serializes
raw samples plus environment/inventories. Review `publish-*.log` and exit codes
before running it. For reproduction on a new host, also record CPU/RAM,
`dotnet --info`, OS build, architecture and any background load. Source hashes
above describe the original byte-for-byte snapshot; the code blocks below retain
its semantics and comments even if Markdown extraction changes final newlines.

### `RuntimeProbe.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <!-- This independent workload does not reference or rebuild mote sources. -->
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RuntimeFrameworkVersion>10.0.11</RuntimeFrameworkVersion>
    <DebugType>none</DebugType>
    <InvariantGlobalization>false</InvariantGlobalization>
    <AssemblyName>RuntimeProbe</AssemblyName>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <!-- Exclude other modes' generated assembly metadata from SDK compile globs. -->
  <ItemGroup><Compile Include="Program.cs" /></ItemGroup>
</Project>
```

### `Program.cs`

```csharp
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

/// <summary>Parses identical frozen JSON bytes before exposing a ready marker.</summary>
internal static class Program
{
    /// <summary>Reads one fixture, checks every numeric token, and waits for observer release.</summary>
    /// <remarks>READY is a headless parse milestone, not a GUI or first-paint claim.</remarks>
    private static int Main(string[] args)
    {
        byte[] bytes = File.ReadAllBytes(args[0]);
        var reader = new Utf8JsonReader(bytes);
        long sum = 0;
        int numbers = 0;
        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.Number)
                continue;
            sum += reader.GetInt64();
            numbers++;
        }
        if (sum != 8386560 || numbers != 4096)
            throw new InvalidDataException("Unexpected frozen fixture result.");
        // The observer stops its timer at this flushed line, before memory instrumentation.
        Console.WriteLine($"READY {numbers} {sum} {bytes.Length}");
        Console.Out.Flush();
        Console.ReadLine();
        // Process counters are measured after readiness and before graceful process exit.
        using Process process = Process.GetCurrentProcess();
        Console.WriteLine($"METRICS {process.WorkingSet64} {process.PrivateMemorySize64} {process.PeakWorkingSet64} {process.TotalProcessorTime.TotalMilliseconds:F3} {RuntimeInformation.ProcessArchitecture} {RuntimeFeature.IsDynamicCodeSupported} {RuntimeFeature.IsDynamicCodeCompiled} {Environment.Version}");
        return 0;
    }
}
```

### `build_probe.py`

```python
"""Publish three independent modes with all build scratch inside this directory."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parent


def main():
    """Freeze fixture bytes and record every exact publish invocation and exit code."""
    fixture = json.dumps([{"id": i, "name": "mote-runtime-probe", "enabled": True}
                          for i in range(4096)], separators=(",", ":")).encode()
    (ROOT / "fixture.json").write_bytes(fixture)
    (ROOT / "global.json").write_text('{"sdk":{"version":"10.0.400","rollForward":"disable"}}\n')
    scratch = ROOT / "scratch"
    scratch.mkdir(exist_ok=True)
    env = os.environ | {"TEMP": str(scratch), "TMP": str(scratch),
                       "DOTNET_CLI_TELEMETRY_OPTOUT": "1"}
    commands = []
    for mode in (sys.argv[1:] or ("jit", "r2r", "aot")):
        command = ["dotnet", "publish", str(ROOT / "RuntimeProbe.csproj"),
                   "-c", "Release", "-r", "win-x64", "--self-contained", "true",
                   "--artifacts-path", str(ROOT / "artifacts" / mode),
                   "-o", str(ROOT / "publish" / mode), "-m:1", "-warnaserror",
                   "-p:PublishAot=" + str(mode == "aot").lower(),
                   "-p:PublishReadyToRun=" + str(mode == "r2r").lower(),
                   "-p:PublishTrimmed=" + str(mode == "aot").lower(),
                   "-p:StripSymbols=true"]
        started = time.perf_counter()
        with (ROOT / f"publish-{mode}.log").open("w", encoding="utf-8") as output:
            result = subprocess.run(command, cwd=ROOT, env=env, stdout=output,
                                    stderr=subprocess.STDOUT,
                                    creationflags=subprocess.BELOW_NORMAL_PRIORITY_CLASS)
        seconds = time.perf_counter() - started
        commands.append({"mode": mode, "command": command, "exit_code": result.returncode,
                         "publish_seconds": seconds})
        print(mode, result.returncode, round(seconds, 3), flush=True)
        if result.returncode:
            print((ROOT / f"publish-{mode}.log").read_text(encoding="utf-8"), flush=True)
    (ROOT / "build-results.json").write_text(json.dumps(commands, indent=2), encoding="utf-8")
    hashes = {path.name: hashlib.sha256(path.read_bytes()).hexdigest()
              for path in (ROOT / "Program.cs", ROOT / "RuntimeProbe.csproj", ROOT / "fixture.json")}
    (ROOT / "source-hashes.json").write_text(json.dumps(hashes, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
```

### `measure_probe.py`

```python
"""Measure parent launch-to-flushed-READY for balanced, fresh headless processes."""
from concurrent.futures import ThreadPoolExecutor
import hashlib
import itertools
import json
import math
import os
from pathlib import Path
import platform
import random
import statistics
import subprocess
import time

ROOT = Path(__file__).resolve().parent
MODES = ("aot", "jit", "r2r")


def observe_line(stream):
    """Timestamp the observer's completed line read, not the child's own clock."""
    line = stream.readline()
    return time.perf_counter_ns(), line.strip()


def sample(mode, batch, order, executor, env, inventory):
    """Start one fresh process, verify readiness, then release it for memory counters."""
    exe = ROOT / "publish" / mode / "RuntimeProbe.exe"
    start = time.perf_counter_ns()
    child = subprocess.Popen([str(exe), str(ROOT / "fixture.json")], cwd=ROOT,
                             env=env, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                             stderr=subprocess.PIPE, text=True)
    try:
        ready_ns, ready = executor.submit(observe_line, child.stdout).result(timeout=10)
        if ready != "READY 4096 8386560 224171":
            raise RuntimeError(f"Unexpected marker: {ready!r}")
        child.stdin.write("\n")
        child.stdin.flush()
        metrics = child.stdout.readline().strip().split()
        child.wait(timeout=10)
        stderr = child.stderr.read()
        if child.returncode or stderr or len(metrics) != 9 or metrics[0] != "METRICS":
            raise RuntimeError(f"Child failure: {metrics}, {stderr}")
        return {"mode": mode, "batch": batch, "order": order,
                "launch_to_ready_ms": (ready_ns-start)/1e6,
                "working_set_after_ready_bytes": int(metrics[1]),
                "private_bytes_after_ready": int(metrics[2]),
                "peak_working_set_through_counters_bytes": int(metrics[3]),
                "cpu_ms_through_counters": float(metrics[4]), "architecture": metrics[5],
                "dynamic_code_supported": metrics[6], "dynamic_code_compiled": metrics[7],
                "runtime": metrics[8],
                "executable_sha256": inventory[mode]["executable_sha256"]}
    finally:
        if child.poll() is None:
            child.kill()
            child.wait()
        child.stdin.close()
        child.stdout.close()
        child.stderr.close()


def median_ci(values, rng):
    """Return an exploratory percentile bootstrap interval over process samples."""
    estimates = sorted(statistics.median(rng.choices(values, k=len(values))) for _ in range(5000))
    return [estimates[124], estimates[4874]]


def main():
    """Inventory artifacts, retain first launches, then measure all six balanced orders."""
    fixture_bytes = (ROOT / "fixture.json").stat().st_size
    print("fixture_bytes", fixture_bytes, flush=True)
    inventory = {}
    for mode in MODES:
        directory = ROOT / "publish" / mode
        files = sorted(directory.rglob("*"))
        files = [file for file in files if file.is_file()]
        inventory[mode] = {"file_count": len(files), "bytes": sum(file.stat().st_size for file in files),
                           "executable_sha256": hashlib.sha256((directory / "RuntimeProbe.exe").read_bytes()).hexdigest(),
                           "files": [{"name": str(file.relative_to(directory)), "bytes": file.stat().st_size,
                                      "sha256": hashlib.sha256(file.read_bytes()).hexdigest()} for file in files]}
    env = os.environ.copy()
    # Hashing inventories warms executable pages; no sample is labelled disk-cold.
    rows = []
    with ThreadPoolExecutor(max_workers=1) as executor:
        for order, mode in enumerate(MODES):
            rows.append(sample(mode, "setup-observation-after-inventory", order, executor, env, inventory))
        for batch, modes in enumerate(list(itertools.permutations(MODES)) * 6):
            for order, mode in enumerate(modes):
                rows.append(sample(mode, batch, order, executor, env, inventory))
                time.sleep(0.05)
    (ROOT / "samples.jsonl").write_text("".join(json.dumps(row) + "\n" for row in rows), encoding="utf-8")
    rng = random.Random(20261002)
    summary = {}
    for mode in MODES:
        selected = [row for row in rows if row["mode"] == mode and isinstance(row["batch"], int)]
        values = [row["launch_to_ready_ms"] for row in selected]
        center = statistics.median(values)
        summary[mode] = {"n": len(values), "median_ms": center,
                         "median_bootstrap_95_interval_ms": median_ci(values, rng),
                         "mad_ms": statistics.median(abs(value-center) for value in values),
                         "min_ms": min(values), "max_ms": max(values),
                         "observed_p95_ms": sorted(values)[math.ceil(len(values)*0.95)-1],
                         "working_set_after_ready_median_bytes": statistics.median(row["working_set_after_ready_bytes"] for row in selected),
                         "private_after_ready_median_bytes": statistics.median(row["private_bytes_after_ready"] for row in selected)}
    pairs = {}
    for alternative in ("jit", "r2r"):
        values = []
        for batch in range(36):
            match = {row["mode"]: row["launch_to_ready_ms"] for row in rows if row["batch"] == batch}
            values.append(match[alternative] - match["aot"])
        pairs[alternative + "_minus_aot"] = {"median_ms": statistics.median(values),
                                               "median_bootstrap_95_interval_ms": median_ci(values, rng),
                                               "min_ms": min(values), "max_ms": max(values)}
    report = {"created_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
              "python": platform.python_version(), "os": platform.platform(),
              "inherited_runtime_env": {key: value for key, value in env.items()
                                        if key.startswith(("DOTNET_", "COMPlus_"))},
              "summary": summary, "paired_deltas": pairs, "inventory": inventory}
    (ROOT / "results.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({"summary": summary, "paired_deltas": pairs}, indent=2), flush=True)


if __name__ == "__main__":
    main()
```
