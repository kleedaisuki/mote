# Native acceptance corpus and causal evidence

## Scope: useful infrastructure, not an unrun GUI benchmark

`acceptance.py` is a standard-library Python 3.11+ artifact tool. It prepares
exact-size **synthetic** workloads and audits existing native trace JSONL. It
does not launch mote, drive an OS event, set focus, scroll a view, sample memory,
publish AOT, capture pixels, touch a clipboard/input source, or create a user-home
configuration. There is no timing threshold and no claimed product latency win.
Do not rename its integrity pass to a performance acceptance pass.

This complements, rather than duplicates, the reviewed OS-specific drivers in
[`NativeStartup`](../NativeStartup/README.md),
[`NativeCanvasGui`](../NativeCanvasGui/README.md), and
[`NativePaintLatency`](../NativePaintLatency/README.md). The next matrix and
endpoint/CI design are in
[`native-latency-acceptance.md`](../../docs/native-latency-acceptance.md).
The bounded existing-driver adapter and first fresh-hosted Windows x64 capability
result are documented in [`WindowsReadinessPilot.md`](WindowsReadinessPilot.md).
Its [natural-close trace variant](WindowsNaturalCloseTrace.md) separates child
monotonic endpoints from parent launch observations without edit/Save; target
normal-close/trace validation is pending.

## Exact corpus contract

```powershell
# Default inexpensive grammar/control set: six exact 1 MiB files.
$env:PYTHONDONTWRITEBYTECODE = '1'
python benchmarks/NativeAcceptance/acceptance.py prepare --sizes 1
# Full set when a target-host driver is ready: 18 files, 666 MiB in total.
python benchmarks/NativeAcceptance/acceptance.py prepare --sizes 1 10 100
```

The output prints a new `.cache/native-acceptance/corpus-<GUID>.json` manifest.
Fixtures remain in `.temp/native-acceptance/<GUID>/`. Generation/hashing must
occur **before** any editor launch timer. These freshly written bytes are likely
OS-cache-resident; no command evicts caches and no sample can be called disk-cold.
The manifest carries exact bytes, SHA-256, fixed format/shape, and an explicit
cache-state label. Different corpora must not be pooled just because their byte
counts match. The script has no recursive cleanup; operator cleanup must check
the resolved `.temp` path and only remove a finished run's synthetic directory.

| Format | Many-line corpus | Single-line corpus |
| --- | --- | --- |
| JSON | Array of complete objects (`id`, string value, Boolean); legal trailing whitespace fills the exact size | One object with one huge ASCII string value; no newline |
| CSV | Four fields per row, quoted comma and doubled quote, CRLF; final complete four-field row has no terminator | One huge quoted ASCII field; no newline |
| Markdown | Repeated heading, paragraph, emphasis and inline local link; final paragraph fills exact size | One unbroken ASCII paragraph |

This controls size and layout shape, **not** semantic diversity. JSON object
members are not unique-key or deep-nesting stress, CSV has no embedded quoted
CRLF, and Markdown has no global reference dependencies. Use the existing
format-specific semantic corpora separately; do not hide their known limits in
an aggregate “all formats” number. ASCII also does not establish CJK, grapheme,
bidirectional, tab-layout, or IME performance.

## Causal trace audit

Build a manifest under `.cache/` or `.temp/` with one **known editor process** per
sample and all of its rotation files. Input identities are caller assertions,
not independently verified native binary provenance. `binary_sha256` may be
null for a retained trace whose binary hash is unavailable; the output then
states `binary_identity: unavailable`. A supplied hash is explicitly labeled
`caller-supplied-not-verified`, not AOT-certified. Never substitute a fabricated
all-zero hash or infer AOT from a trace record.

```json
{
  "schema_version": 1,
  "samples": [{
    "sample_id": "win-x64-json-many-1-trace-on-process-01",
    "binary_sha256": null,
    "traces": [".temp/native-acceptance/KNOWN_RUN/home/traces/KNOWN_TRACE.jsonl"],
    "expected_records": {
      "document.open_to_editable": 1,
      "document.open_to_draw_submission": 1,
      "document.edit_to_draw_submission": 20,
      "mote.session": 1
    }
  }]
}
```

The example is a **contract template**, not an existing path or a claim that
these records have been produced. `expected_records` counts all terminal
outcomes, including `cancelled`, `failure`, and `skipped`. Set the counts from
the actual reviewed driver protocol, not by counting whatever output happens
to exist after a failure. Absence of expectations is labeled
`coverage_contract: not-specified`; an integrity pass then cannot certify
workload coverage. Wrong counts, missing parents, duplicate spans, causal
cycles, missing/mismatched draw revisions, invalid loss counters, observed
drops, and absence of exactly one normal terminal session invalidate integrity.
Terminal-session presence still does not prove that no uninstrumented action
was omitted; retain the external driver's independent source/action oracle.

```powershell
python benchmarks/NativeAcceptance/acceptance.py summarize `
  --manifest .cache/native-acceptance/SAMPLES.json `
  --output .cache/native-acceptance/NEW_SUMMARY.json
```

Output is exclusive-create, so a repeated output name fails rather than replacing
evidence. Its nonzero exit means integrity is incomplete/invalid, not that mote
necessarily has a performance defect. The bounded reader rejects unknown schema
fields/operations and lines over 16 KiB, over 100,000 records per sample, files
over 32 MiB, and over eight rotation files per process. Failures must be retained
and classified; do not remove inconvenient samples until the summary is green.

The schema-one reader checks **dimension values**, not only permitted attribute
names: `format` and `size_bucket` use the closed vocabulary emitted by
[`JsonlTraceSink`](../../src/Mote.Telemetry/JsonlTraceSink.cs), including its
`unknown` format fallback; `hresult` is a signed 32-bit integer, never a Boolean
or string. These constraints prevent document content disguised as a format or
bucket from becoming accepted evidence. Schema version must be an integer 1.
The focused parser regression suite passes **9/9** (2026-10-01), retained at
`.cache/validation/closed-trace-dimensions/reader-tests.log`; this is artifact-only
validation, not another GUI or product persistence experiment.

Records are joined by `(session_id, trace_id, span_id)`, not adjacency, UTC
subtraction, or duration sorting. The first version-matched source draw callback
may precede semantic handoff and parent serialization. Successful durations have
ordinary median p50 and nearest-rank p95; cancellation is a **censored** outcome
and is counted separately, never assigned a fabricated successful duration.
`n < 100` is explicitly flagged as a small sample. Even `n >= 100` does not prove
an independent population tail or an SLA: repeated edits within one process
share state. The tool intentionally never pools samples, operating systems,
render modes, formats, workload sizes, or host instances.

All artifact paths must remain below the resolved repository `.temp` or
`.cache`; symlink/junction ancestry is rejected. There is no support for
concurrent hostile filesystem mutation. Output includes fixed operations,
counts, numeric durations and opaque hashes/IDs, not raw trace attribute strings,
source bodies, source paths, window titles, exception messages, or screenshots.

## Completed inexpensive checks

Windows x64 developer host; Python **3.14.6**. No target-native process ran.

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
python -m unittest discover -s benchmarks/NativeAcceptance -p test_acceptance.py -v
```

**6/6 tests passed**, including six 1 MiB grammar/size cases, correct cancellation
separation, out-of-order parent joins, null/missing parents and versions,
self/two-node cycles, duplicate/mixed-session data, absent/zero/Boolean/negative
loss counts, privacy/schema negatives, exclusive output, traversal confinement,
and descriptive quantiles. Test scratch was removed using exact file names,
without recursive deletion. No new bytecode cache is required.

A separate repository-local Windows junction negative control disabled the
Python-3.12+ `os.path.isjunction` helper and still rejected an output below the
junction using `lstat().st_file_attributes`. This supports the Python 3.11
compatibility path on Windows, **not** an execution on Python 3.11 itself. The
owned empty target/junction were removed by exact paths without recursive
deletion; no writes escaped the scratch subtree.

The four **already retained** traces from
[CI 36754713708](https://github.com/kleedaisuki/mote/actions/runs/36754713708)
under `.cache/ci-36754713708-mac-draw-{arm,x64}` were re-parsed, **not rerun**.
Each contains two `edit_to_draw_submission` and two `edit_to_presentation`
records (one cancelled version 0, one successful version 1), exactly one terminal
session, matching parent/revision, no duplicate/cycle, and no reported drops.
The revised audit passes all four with exact expected counts.

Reproduction artifacts are
`.cache/native-acceptance/retained-mac-draw-manifest.json` and
`retained-mac-draw-summary-reviewed.json`. Binary hashes are unavailable in this
trace-only envelope and explicitly null. These in-memory/forced-draw probes
validate serialization compatibility and endpoint attribution, **not** 1/10/100
MiB open latency, natural input, scrolling, physical paint, a p95 distribution,
allocation, RSS, or a new performance baseline. See [independent review](REVIEW.md)
for the initially discovered false-integrity paths and their corrections.
