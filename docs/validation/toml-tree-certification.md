# TOML valid-tree certification: independent validation

## Verdict and scope

The clean-tree optimization at production commit `a8138522f94c3d75b1dcd28f8c8dfa790f841578`
passes the retained independent semantic/compatibility checks below. No production
code was modified by this validator. This is Windows x64 managed .NET evidence,
not a Native AOT, graphical-input, macOS or latency certificate.

The expected behavior comes from the normative namespace construction in
[the single-grammar investigation](../architecture/toml-single-grammar-validation.md):
separate inline-value scopes, sealed assigned values, decoded key identity, and fresh
latest array-of-table element scopes. Literal tests establish these outcomes directly;
comparisons with an old binary independently protect established output contracts.
Any failed certification must leave the entire uniform recovery path authoritative,
including all existing diagnostic messages, identifiers, spans and counts.

## Evidence

Environment: Windows x64, .NET runtime 10.0.11, SDK 10.0.400, Tomlyn 2.10.1.
All artifacts are under repository `.cache/toml-tree-validation/` and experimental
harnesses under `.temp/toml-tree-validation/` / `.temp/toml-tree-cancellation/`.

| Check | Observed result |
| --- | --- |
| Independent retained directed tests | 73/73 pass within final integration run |
| Public uniform/recovery/reuse/token-projection plus directed tests | 915/915 pass, zero skips |
| Existing incremental policy workflow | 48/48 pass, zero skips |
| Frozen public-output comparison | 703/703 decoded corpus sources exactly equal |
| Strict byte encoding entries | Nine entries excluded from decoded comparison; existing decoder tests remain separate |
| Concurrent cancellation experiment | Five of five observed `OperationCanceledException`, source unchanged; subsequent independent certification succeeds |

The 915-test filter includes existing public corpus tests, historical latest-AoT
minimals, valid sources beyond **each** large-session cache budget, integer text beyond
Int64, exact UTF-16 diagnostics, independent recovery witnesses, statement reuse and
viewport token projection. It does not duplicate the 712 corpus entries in another
retained theory. The 73 new tests provide literal nested/dotted/sealed/escaped key,
array-element isolation, mixed containers, latest-AoT, malformed source, EOF-control,
format-preservation, read-only-parent/span and missing-required-structure checks.

### Frozen exact comparison

The baseline is the previously retained public implementation from `70def6d`:

- Runtime-loaded baseline `Mote.Formats.dll` SHA-256:
  `C8F3A5FFA6A0BC42B58F2A5D91EBC3AF35074B1F16191A9FB16A0C6125F296E2`.
- Runtime-loaded candidate SHA-256:
  `F97E4CCEFECFD320B3A5C173EC90A1C0C50669CCE237DA88D274CD2EDC11C2EF`.
- Baseline JSONL SHA-256:
  `666599abc4391f84d91a5ec543db44f8b94d0f03c0f700476ef029f185afbc53`.
- Candidate JSONL SHA-256:
  `1a8a0107e49eb51368ffed80c592a9870792c36c74e4e8429709e61bbc3d4507`.

`baseline.jsonl` and `candidate.jsonl` record all 703 exact decoded pinned sources;
`exact-comparison.json` records identities and **zero exact mismatches**. The comparison
covers complete serialized `FormatAnalysis` (source, all diagnostic fields and ordering,
all tokens, recursively all projected nodes) and exact `Format` output, not merely
valid/invalid classification or diagnostic-span sets.

A setup error was detected rather than silently accepted: initial candidate build
resolved/copied the baseline assembly despite candidate HintPath properties. Its
identity hash was still baseline; it was **not credited** as a candidate run. The
candidate runner's compatible dependency copy was explicitly replaced with the built
candidate, then the separate process printed its actual loaded identity before
classification. The comparison script rejects equal baseline/candidate identities.

### Read-only and structural fixtures

For every literal valid/refused source, tests snapshot public descendant identity,
parent references, source spans, token identity/parents/spans and textual serialization
before certification and assert them unchanged afterward. The installed public API
uses `ToString()` / `WriteTo(TextWriter)`, not a `ToFullString()` method. Tests do not
invoke private validators or reflection, and do not reparent nodes during certification.

Five deliberately mutated public fixture trees remove equal token, key, value, array
closing bracket or inline closing brace; each is refused. These mutations establish
structural refusal only, not a general guarantee for arbitrary fabricated node classes
or concurrent mutation, which are outside the certifier's parser-origin/read-only contract.
Implementation-owned tests separately cover absent scalar token and dotted components.

The 48-level mixed array/inline fixture checks separate local scopes, scalar value
identity, original comment text, CRLF counts and formatting idempotence. An initial
**test harness defect**, not a production defect, recursively serialized already-escaped
child JSON strings, causing exponential growth and an oversized JSON value. It was
repaired with an iterative flat `(depth, kind, name, value)` fingerprint; the original
fixture depth and semantic assertions were retained. `directed-build-run.log` and
`tree-directed.trx` retain that failure; final `tree-integration.trx` records acceptance.

### Cancellation evidence and limits

The retained deterministic test checks pre-cancellation throws, source preservation,
and a subsequent successful independent call. The separate experiment parses 80,000
three-component assignments before starting certification. A second task requests
cancellation after the call begins; five recorded request times were 21.7967, 12.2576,
17.1993, 12.3044 and 2.6662 ms after start. Completion times were respectively 26.5672,
12.9513, 17.2540, 12.3357 and 2.7026 ms; every call threw and preserved serialization.
A later uncancelled call certifies the same tree. `cancellation.jsonl` records actual
runtime candidate identity and these observations.

This exercises asynchronous cancellation during an outstanding certification call,
not a deterministic witness of a particular internal instruction, nor a cancellation
latency service-level guarantee. The test suite deliberately avoids a flaky timer-based
assertion. The certifier has no retained published findings to leak; no hook or production
instrumentation was added solely to force a mid-loop schedule.

## Reproduction

From repository root, with production and retained tests built:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release -p:PublishAot=false --filter 'FullyQualifiedName~TomlTreeCertificationTests|FullyQualifiedName~TomlUniformValidationTests|FullyQualifiedName~TomlKnownError|FullyQualifiedName~TomlStatementReuseTests|FullyQualifiedName~TomlTokenProjectionTests' --logger 'trx;LogFileName=tree-integration.trx' --results-directory .cache/toml-tree-validation
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release -p:PublishAot=false --no-build --filter FullyQualifiedName~IncrementalPolicyTests --logger 'trx;LogFileName=tree-incremental.trx' --results-directory .cache/toml-tree-validation
python -B .temp/toml-tree-validation/compare.py
dotnet run --project .temp/toml-tree-cancellation/Probe.csproj -c Release
```

The comparison command checks retained observations; rerunning two versions requires
keeping their matching binaries in distinct directories and verifying each runner's
actual identity row. The baseline runner references
`.temp/toml-small-policy-cost/final-dll`; the candidate runner uses the current built
assembly. Do not regenerate a supposed baseline from today's production source.

## Remaining boundaries

Finite conformance plus directed tests are not a proof for every TOML program. Full
native workflow and performance measurements belong to separate owners after source
freeze. No GUI, operating-system input source, global hook, AOT publication, network
lookup or workflow file was modified or executed by this validator. The existing
large-document refusal/census limits are unchanged by this public clean-tree slice.

## Hosted clean-tree integration — CI 36863766458

Date: 2026-10-01. Exact pushed source `5d0fcb6ee40ae5522c129006c4ffc201df599a6d`, [completed CI 36863766458](https://github.com/kleedaisuki/mote/actions/runs/36863766458). All ten jobs conclude success. Actual strict logs on **both Windows and macOS** report **Mote.Tests 3218/3218** (86 more than the prior 3132 integration), Themes **14/14**, Configuration **9/9**, zero failed/skipped. This establishes cross-platform managed integration of the retained clean-tree contracts, not a new native TOML input/performance experiment.

All four Native AOT delivery inventories contain exactly one root executable, zero non-executable/bundled-native payloads: win-x64 **7,215,616 bytes**, win-arm64 **7,361,024**, osx-x64 **17,143,424**, osx-arm64 **16,781,960**. Both **blocking** Mac Flow/callback steps actually succeed and print the posted-primary/report-fault marker, local-monitor Block ABI/install/remove marker and Flow-ready (x64 12:50:43 UTC; ARM64 12:49:08 UTC), with the existing exit-0 predicate. Both Windows **blocking** owned-HWND passive-publication/pane-focus steps actually execute **2/2**, matching native RID test assemblies and architecture logs. The original external AOT Grid probe remains **product-fail / numeric exit 1 on both RIDs**, unchanged source pin `C6894B4EDCF931BAA322B878AF8965759AC7E44EFC10291200737A18C1AD5509`; the existing distinction between owned-HWND contracts and the external focus oracle is reused, not reinterpreted as a fix.

All four ordinary JSON inventories report **8/8 pass**, explicit editor/reopen exits **0/0** for 1/100 MiB. These JSON/native controls establish scoped compatibility regression only: they do **not** certify native TOML GUI editing, TOML startup/latency, arbitrary physical input or AppKit monitor cost. The completed managed 50-row clean-tree performance evidence remains the relevant cost study; no additional Benchmarks dispatch or redundant raw Save/recovery/Windows-focus audit was needed here. The next Windows provenance implementation is not part of this pushed source and has no coverage claim in this section.

Selected completed metadata, strict/AOT logs, four single-binary inventories, both Windows TRX/original external reports and four ordinary summaries are retained under `.cache/ci-36863766458-toml-clean-tree/`; artifacts were enumerated with `per_page=100`. No production/workflow edits, local native experiment, push, amend or reset were performed by this audit.
