# Explicit causal telemetry requests: independent review

Date: 2026-10-01. Reviewed production commits: `612a0dc` and `fda5c36`.
Scope: `Mote.Telemetry` request/context API, JSONL reason serialization, request
tests, and retained independent prefix-recovery evidence. Native controller,
engine Save observers, GUI input delivery, hosted cross-platform acceptance,
and the current unrelated working-tree edits are outside this review.

## Assessment

**No substantive production defect identified in this slice.** The explicit
request graph fixes the previous unpublished-parent problem without holding an
ambient Activity or a producer lease across callbacks. This is a pre-hosted
API review, not a certificate of complete end-to-end Save coverage.

No production file was edited, and no completed test suite was rerun. Existing
fault controls already exercise writer stalls, queue overflow and bounded
shutdown; this review did not add a redundant fault harness.

## Context ownership and graph

- `BeginRequest` acquires one short producer lease, creates an enabled-only
  request, and enqueues `command.save.received` or
  `command.save_as.received` with the request mark's own identity. Command kind
  survives a receipt-only terminated prefix without waiting for a terminal.
- `BeginPhase` similarly emits its own fixed `.entered` anchor before returning
  to the caller. Nested phases parent their entries to the supplied explicit
  context. Receipt and phase anchors are zero-duration boundary records, not
  unfinished duration records.
- `RecordChild` creates a fresh event ID. Phase and request terminal durations
  also receive fresh IDs, parented to the corresponding entry anchor and
  measured from its original monotonic timestamp. There is no begin/end ID
  reuse in these methods.
- New explicit methods do not read or install `Activity.Current`. A native
  callback's explicit parent therefore does not accidentally inherit unrelated
  ambient work. This is compatible with the general context-propagation model
  in the [OpenTelemetry tracing API](https://opentelemetry.io/docs/specs/otel/trace/api/),
  but the local JSONL boundary rows are a mote-specific contract, not a claim
  that an OpenTelemetry exporter was implemented.
- `MarkChild` alone does not serialize an anchor. Callers requiring a recoverable
  entry must use `BeginPhase`, as the README now states. The phase mark is an
  immutable value; callers still own operation matching and once-only phase
  completion. Request terminal once-only selection is enforced separately.

The `fda5c36` typed receipt refinement is consequential, not cosmetic: a missing
terminal cannot be used to recover whether the original request was Save or
Save As. The new receipt names carry that distinction directly.

## Session isolation, races and terminal truth

`TryAcquireOriginal` rejects a context whose sink is no longer the current sink,
whose original sink is faulted, or whose original admission has closed. Rejected
work is counted on the original sink, never rerouted into the replacement
session. Default marks are inert. Successful admission holds only the short
enqueue lease; a concurrent shutdown waits for an already acquired lease using
the existing producer protocol.

`TelemetryRequest.EndOnce` uses `Interlocked.CompareExchange` to select exactly
one terminal enqueue attempt. A losing completion neither writes another
terminal nor overwrites the first disposition. The winner's Boolean result is
**not** an enqueue or persistence acknowledgment; `_ended` remains selected even
if transport subsequently rejects the record. This agrees with the documented
[CompareExchange atomic operation](https://learn.microsoft.com/en-us/dotnet/api/system.threading.interlocked.compareexchange).

An unfinished explicit request owns no long-lived producer lease, so normal
shutdown can drain transport while leaving that request without a terminal.
Consequently:

| Observed fact | Safe interpretation | Unsafe inference |
| --- | --- | --- |
| Receipt/entry present | That target-owned boundary was recorded | File committed or the phase completed |
| `EndOnce` returns true | This caller won terminal selection | Terminal reached disk |
| `mote.session` success | Admitted transport drained normally | Every request finished or no callback was missed |
| Original sink's rejection counter increases after shutdown | A delayed enqueue was rejected by that original sink | An already closed file now contains the rejection |
| Entry absent, or terminal absent | Evidence is missing/censored | Target boundary did not execute |

Queue overflow can lose an entry while retaining a later child. Entries are
enqueue attempts rather than synchronously durable anchors; the reader must
retain such children as unlinked positive evidence, not synthesize their parent.
An enqueue failure before an entry and a late rejection after final drain cannot
be repaired by a reader. The independent reader's `legacy_health_unknown` and
`absence_certified: false` preserve this limitation; no health watermark or
sequence-completeness certificate has been added here.

Legacy Activity-backed `StartChild` keeps its previous fallback semantics. This
slice deliberately guarantees original-sink isolation only for the new explicit
methods; it does not silently strengthen the legacy contract.

## Schema, privacy and disabled cost

The optional `attributes.reason` is selected through a closed enum-to-fixed-name
mapping. None and unknown enum values are omitted. New operation/entry names
also come from closed mappings; no path, exception message, title, key text,
document text, arbitrary label or native pointer is accepted. Phase HResult is
numeric and emitted only for failure. Unknown dimensions retain the existing
fixed format and numeric filtering rules.

Schema version remains 1. Old no-reason records retain their prior shape; strict
readers must explicitly accept the optional reason field. The request tests
cover top-level shape, allowed attributes, reason omission and signed HResult.

The disabled path of `BeginRequest` returns before creating the request or IDs.
The other explicit methods return on the default mark before creating IDs or
records. The allocation claim is scoped to these steady-state disabled calls,
not static initialization, configuration, enabled tracing, caller closures, or
the application as a whole. The retained test warms the methods and compares
thread allocation counters across 10,000 disabled iterations.

## Evidence inspected, without re-execution

| Retained artifact | Observed result | Scope |
| --- | --- | --- |
| `.cache/telemetry-requests/telemetry-requests.trx` | 26 executed, 26 passed, zero failures | Original selected request/legacy checks |
| `.cache/telemetry-requests/telemetry-request-kinds.trx` | 9 executed, 9 passed, zero failures | Typed receipt follow-up request checks |
| `.temp/causal-recovery-typed-managed-20261001/report.json` | `passed: true`, four cases | Normal, nested held-phase kill, Save receipt kill, Save As receipt kill |
| `.temp/causal-recovery-typed-aot-win-x64-20261001/report.json` | `passed: true`, four cases | Same synthetic controls in actual local Native AOT child |

The two test runs overlap; their counts must not be summed into 35 unique tests.
The recovery reports preserve linked positive pre-kill boundaries and censored
post-kill requests, rather than fabricating completion. The standalone probe
does not perform real document Save I/O. See the separate
[independent recovery validation](../validation/causal-trace-recovery.md) for
the reader fixtures, reproduction commands and limits.

Independently recomputed SHA-256 for the reviewed working files:

| File | SHA-256 |
| --- | --- |
| `src/Mote.Telemetry/MoteTelemetry.Requests.cs` | `FDA2614F76BC3203D353D9176F77EB5F1CB79A5B907427C3CD6B245D175D3A24` |
| `src/Mote.Telemetry/MoteTelemetry.cs` | `5BC2CD5FBFF1A60D11A58CBAF18D46E6CCF20EAD35ACE50967E7EA15650CDA35` |
| `src/Mote.Telemetry/JsonlTraceSink.cs` | `E026DE5727E7E12DA8FCD1442A1577D447FECACC29785F2D4F9369A2F1AB4D00` |
| `src/Mote.Telemetry/TelemetryTypes.cs` | `5FBB503B803EDBFE38C45717142D0A727F136DCA91FDD5DF17362E554F929AAE` |
| `tests/Mote.Tests/TelemetryRequestTests.cs` | `7648C0EA6F86EDD30ED60EA557CB034D770FA34D10586B2654123041D0A2BA5E` |

Remaining acceptance belongs to the integration owners: real native Save request
receipt through admission/worker/engine/UI terminal, exact saved-byte oracle,
and hosted Native AOT on both operating systems and architectures. This review
does not certify OS event delivery, power-loss durability, an exact flush
deadline, global zero loss, or native editor performance distributions.
