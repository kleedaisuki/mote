# Full-native source capability diagnostic review

## Objective and review boundary

Review the first, explicitly hosted-only full-native source capability diagnostic,
not a new default editing profile. Scope is three generated fixtures, one
controlled insertion per fixture, full engine-owned text/history, native
attribute publication, exact saved bytes and fresh-document reopen. There is no
universal arbitrary-edit, reader, IME, physical-input or paint certificate.

Inspected `NativeSourceDiagnosticHost`, `NativeSourceDiagnosticBinding`,
`NativeSourceDiagnosticFixtures`, `NativeSourceCapabilityProbe`, both platform
factories, the additive entry-point routes, shared projection and relevant
interop declarations. Existing explicit encoding diagnostics have their own
review; this review checks route placement, not that feature again. No production
or test files were modified by this reviewer. No local GUI/native calls or broad
test replay were performed.

## Finding and disposition

### P2: failed-phase measurements included report serialization

At the initially reviewed `PhaseReport.Measure` catch, `Failure(phase, ex)`
serialized a JSON record and durably flushed it before sampling elapsed time and
allocated bytes for the failed terminal. Any action exception therefore charged
diagnostic serialization and disk flush to the failed operation, unlike the
successful path and contrary to the documented serialization-outside-interval
contract. This can mislead diagnosis precisely on failed operations, especially
when durable flush latency dominates. Confidence: high, executable call order.

**Resolved in the final reviewed source.** The catch now captures elapsed time
and allocation immediately on entry, before status/evidence serialization and
durable flush. Failure evidence and rethrow remain. The original runner SHA-256
was `DB2458E2666086A9CD82FD2B82BD3ED208CBCFF08AE02CE173BCA95F6E6F19F1`;
the corrected source is identified below. No native fault timing experiment was
run; resolution is established by the explicit corrected call order.

No remaining substantive defect was found within the stated source-review scope.
This does not establish native runtime acceptance. A separate owner refinement
also records actual saved-byte hash/count and semantic token/diagnostic counts
before their assertions, preserving those observations on mismatch rather than
emitting them only after successful checks.

## Checked contracts

| Area | Source-level assessment |
| --- | --- |
| Admission | Hosted environment and matching OS are checked before config, files, telemetry or native objects. Fresh output is a descendant of root `.cache`/`.temp`; existing linked ancestors and existing targets are rejected. This is a controlled runner boundary, not adversarial concurrent filesystem security. |
| Configuration and evidence | Explicit isolated MoteHome bypasses inherited user home. Generated identities, fixed codes, numeric HRESULT, counts and hashes are serialized; no exception messages, user source or raw native identities are emitted. |
| Binding | Exact complete import certificate, snapshot identity, version/generation/nonce and retirement gate reconciliation. Full strings and projection maps are intentionally O(n). UI-owner exclusivity is the concurrency assumption; this is not a concurrent engine transaction API. |
| Engine ownership | Native insertion does not mutate Engine. Reconciliation uses shared scalar-corrected Difference and exactly one Apply. No-op consumes a binding without text history; Undo/Redo are Engine operations followed by explicit new native installations. Cancellation is checked before work and immediately before Apply, never used to disguise an already committed edit. |
| Windows lifetime | STA and managed owner guards precede native operations. RichEdit module is retained until owned parent/child destruction. Partial construction releases owned objects. Nonactivating show and SCF_NOKBUPDATE do not request global activation or keyboard-layout switching. |
| Windows offsets/state | CRLF projection is separate from RichEdit native paragraph offsets. Every import is exactly read back. Publication validates spans before mutation and restores/checks text, sorted selection, scroll tuple and disabled native history. FirstVisibleLine supplements documented 16-bit pixel coordinates; no distant pixel-perfect conclusion follows. |
| macOS lifetime/ABI | Main native thread is checked before AppKit messaging. Retained views and window are released before the autorelease pool. NSRange is pointer-sized; CGPoint/CGSize use doubles; CGRect result uses x64 stret and arm64 ordinary return. BOOL setters/results use the explicit byte bridge. |
| macOS backend | textLayoutManager is inspected before the legacy accessor. Calling layoutManager can trigger compatibility mode; the label is the observed diagnostic backend, not a promise to use TextKit 2 or a nonperturbing universal introspection API. |
| Style/paint | Complete foreground spans are published as native attributes, with text/selection/scroll and Engine history preservation checked. API acknowledgment is not physical foreground ink, compositor presentation or attribute-value readback. |
| Persistence/cost | Saved bytes are compared independently; reopen uses a fresh Document in the same process. Process-wide approximate GC allocation deltas are not exclusive UI-thread or native memory allocation; sampled working set is not peak memory. Native AOT hosted executable identity is still required. |

## External checks

- [Microsoft EM_GETSCROLLPOS](https://learn.microsoft.com/en-us/windows/win32/controls/em-getscrollpos)
  documents 16-bit coordinate values even in 32-bit POINT fields. The independent
  native line observation and explicit restoration limit are appropriate.
- [Microsoft EM_SETCHARFORMAT](https://learn.microsoft.com/en-us/windows/win32/controls/em-setcharformat)
  documents mask-scoped mutations, nonzero success and SCF_NOKBUPDATE's prevention
  of keyboard switching. The adapter checks return values and uses the existing
  character structure rather than introducing a private formatting ABI.
- [Apple NSTextView](https://developer.apple.com/documentation/appkit/nstextview)
  documents the legacy layoutManager compatibility transition. The diagnostic
  checks the modern manager first and does not equate the OS version with backend.

## Evidence and limits

The retained integration build log
`.cache/validation/native-source-capability/integration-build.log` reports a
Release tests-project build with zero warnings and zero errors (7.01 seconds).
The final runner-only correction build is retained in
`.cache/validation/native-source-capability/final-native-build.log`: zero warnings,
zero errors, 5.11 seconds. These are compilation evidence only. Independently
retained `.cache/validation/native-source-binding/binding.trx` counters and
`run.log` report **33/33 passed**, zero failures and zero skipped, 385 ms.
Their `hashes.json` identifies the unchanged binding, fixtures and shared
projection. Tests were not repeated for the runner-only evidence correction.
See `docs/validation/native-source-binding.md` for their scope and limitations.

Final raw-file SHA-256 identities (including line endings) are retained in
`.cache/validation/native-source-capability/final-source-hashes.json`:

| File under `src/Mote.Native/` | SHA-256 |
| --- | --- |
| NativeSourceDiagnosticHost.cs | `57EEB76FCCE4D962ADF09B5B07CE7B5BC569C0037097129E094821C6CE218E15` |
| NativeSourceDiagnosticBinding.cs | `4C6C870C7C9F5DAF9475947E42B2052C57E6E36D1D60FC55CAF496648B73D680` |
| NativeSourceDiagnosticFixtures.cs | `E57F160A654BB58A146E8BDF2E1FAD983C09F6C3B3EC04E7FC66B556F01C480C` |
| NativeSourceCapabilityProbe.cs | `E7872B18EEEF5682EC0E8D7F2B9D865DE80060AC727EEE75331A47F0E77573A3` |
| Windows/WindowsNativeSourceCapabilityProbe.cs | `B656B5D4B2CE677507301D31DDDAB988DE19DAC4B35627799A9DF7702FD435EE` |
| Mac/MacNativeSourceCapabilityProbe.cs | `735BE81D281F2178043200AF1411966D9374EFE74EA06391D26C64DC97B21ED8` |
| NativeTextProjection.cs (existing shared dependency) | `1187E829D2D06E1026CF5E2CEB634055EEDA6A8966388E8464EDF126F4098FFF` |
| Program.cs (additive routes only) | `0B2F90566E0DAFCB04AAB45F301CEE8CD353D718E952179B5D226520E0E0C4B7` |

Native behavior remains unverified until bounded hosted runs on Windows/macOS
x64/arm64 retain report, numeric process exit and final telemetry drain evidence.
`probe/complete` occurs before telemetry shutdown; it alone must not certify
successful final sink shutdown. A phase entered without a terminal is incomplete,
not success. External workflow timeout and artifact retention are required for
native hangs, not supplied by this diagnostic. No physical reader speech, IME,
interactive typing, arbitrary selection direction, high-DPI visual quality or
product-profile readiness is inferred from this review.
