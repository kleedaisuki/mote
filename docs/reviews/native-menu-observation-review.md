# Native menu observation: independent source review

Date: 2026-10-01. Verdict: **no substantive defect found in the examined slice;
macOS runtime validation is still required**. This is not a certificate of
ordinary Command-S routing, complete input coverage, or performance.

## Scope and identity

Reviewed the uncommitted menu-only change above HEAD
`25834b1c42c6dccf7fcbfd1a7bd0c1404e4c5b50`: `MacMenuObservation.cs`,
`MacMenuObservationProbe.cs`, `NativeSaveFamilyCandidate.cs`,
`MoteTelemetry.Menu.cs`, appended telemetry enum/event mappings,
`MacEditorShell.CreateMenu`, and the `MacFlowRenderingProbe.Check` invocation.
Read [the design](../architecture/native-save-input-provenance.md) first, and
examined portable tests and existing producer-admission semantics. Reader
commit `21d9183` was identified, but its implementation is not independently
certified by this Native-source review.

Reviewed blob identities for the three main new files, respectively:

* Native observation: `43de830f587dc08f66451d9f6930a99338847fcb`.
* Native ABI probe: `e569f85d59547e4e6e4254e3c67555dab1083dbb`.
* Telemetry menu API: `a4e717ad6a9434c05dbc229073b6ad174db8053d`.

No production file was modified by this review. No workflow or external input
was dispatched.

## Contract assessment

| Contract | Examined implementation and assessment |
| --- | --- |
| Lexical superclass | Production stores the NSMenu class used to declare its owned subclass. `Forward` receives that exact superclass, never derives a superclass from the receiver's dynamic class. A further runtime subclass therefore cannot cause the inherited override to redispatch to itself. |
| Objective-C BOOL | Registration copies the existing method encoding after accepting only `c` or `B` return encoding. The unmanaged callback and `objc_msgSendSuper` return a byte, and the result is returned unchanged rather than normalized through managed Boolean marshaling. |
| Transparent dispatch | There is exactly one unconditional superclass call using the original receiver, selector, and event. Candidate detection and telemetry do not initiate Save or substitute an event. |
| Instrumentation failure | Candidate access and every telemetry checkpoint contain non-OOM managed exceptions. Setup failures preserve stock NSMenu construction. OOM exclusions match the surrounding policy. Objective-C exceptions are not represented as catchable managed failures. |
| Default off | Healthy enabled tracing is required before class registration and observed-menu allocation. Disabled startup allocates the existing NSMenu class and installs no managed per-event override. No Block bridge or application singleton replacement is added. |
| Privacy | Type/modifier filtering precedes character access. Characters remain native; only a length-one UTF-16 value is compared to fixed s/S. No character string, pointer, native timestamp, key code, document identity or arbitrary attribute reaches the telemetry API. |
| Causal ownership | Each checkpoint uses the acquired current sink's session identity and a fresh span, ignoring ambient Save scopes. There is no pending event or long producer lease. Entry and return are intentionally independent positives: they are not paired across reconfiguration or nested dispatch. |
| Schema and outcome | Enum entries are appended, operation strings are fixed, zero-duration success denotes execution of a checkpoint. A false superclass result is not reported as failed Save. Existing request ancestry is unchanged. |

The current-session checkpoint contract intentionally differs from a delayed
request phase: if configuration changes during superclass execution, the later
checkpoint may belong to the new session. Because no paired operation identity
is claimed, this does not transplant an old Save parent into that session.
Readers must retain the documented unknown cross-boundary edges.

Apple's [Objective-C runtime message header](https://github.com/apple-oss-distributions/objc4/blob/main/runtime/message.h)
defines `objc_super.super_class` as the first class searched and requires a
signature-appropriate function-pointer type. This supports the lexical cached
superclass and byte-return signature used here, not receiver-dynamic
superclass lookup.

## Evidence and limitations

Inspected `.cache/menu-observation/menu-observation-regression.trx`:
**42 executed, 42 passed, zero failed/error/aborted/not-executed**. Did not rerun
completed validation. The portable menu tests establish filtering, fixed
vocabulary, empty attributes, distinct session-child spans despite an ambient
Save operation, and zero warmed managed allocation for disabled checkpoint
calls. These are not AppKit dispatch or native allocation measurements.

The in-process probe shares production's `Forward` core through a separate
tiny unmanaged wrapper supplying its own lexical control superclass. It checks
false/true byte preservation, identical event pointers, one superclass call per
invocation, and nested invocation. A narrow final follow-up also inspected
`MoteMenuAbiDescendant`: this class is registered below the observed control
without overrides, and false/true calls require exactly one base-control call.
Its inherited IMP therefore exercises the shared core with a receiver whose
dynamic superclass differs from the wrapper's lexical superclass. The final
probe blob identity is the one recorded above. It creates synthetic events without posting
them to AppKit or the system. `MacFlowRenderingProbe.Run` reports a managed
check failure as exit 1 and closes its shell in the existing finally path.

No macOS x64/ARM64 runtime result exists for this slice at review time. In
particular, the probe does not prove that ordinary externally posted Command-S
uses the owned production main menu. The descendant control now explicitly
tests inherited-IMP lexical dispatch, but neither this new control nor actual
production NSMenu superclass execution has a macOS runtime result yet.
Setup-failure/managed-exception containment is likewise inspected, not exercised
through injected native failures. These limitations are not evidence of a
defect, and should not be converted into pass claims.

## Next validation boundary

Run the already designed Native AOT probe on both macOS RIDs and inspect its
actual exit status. Then inspect ordinary one-attempt Save traces for independent
menu-ready/entered/returned positives and the existing request/commit chain.
Do not infer callback absence from missing rows, manufacture menu-to-Save
parentage, add retries, or rescue a failed Save through another route. Measure
enabled/off overhead on the same new macOS binary before claiming a cost bound.

The originally suggested descendant control has been added and source-reviewed
without changing the production dispatch contract. It remains part of the next
macOS runtime validation, not an already passed test. The accompanying
[implementation/evidence note](../validation/native-menu-observation.md)
also explicitly distinguishes core ABI controls from real keyboard routing.
