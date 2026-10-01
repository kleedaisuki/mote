# Permanent macOS menu observation: implementation and evidence

Date: 2026-10-01. This is observability infrastructure, not a Save-routing fix.

## Implemented boundary

Healthy opt-in tracing substitutes an owned `NSMenu` subclass for mote's existing
main menu. Default-off construction still uses stock `NSMenu`; no local event
monitor, Block bridge, application replacement, SDK swizzle, new CLI flag or
retry was introduced. Menu installation and runtime class initialization occur
on the existing AppKit UI thread. The registered class remains process-lifetime.

The callback checks key-down metadata and Command without Control/Option,
then reads one UTF-16 code unit only if `charactersIgnoringModifiers.length == 1`.
Only `s`/`S` produces candidate checkpoints. This is not command interpretation:
Shift, Caps Lock, remapping and keyboard layouts do not determine Save/Save As.
Input text, event pointers, modifiers, timestamps and key codes are never
serialized. Instrumentation errors other than the repository's existing fatal
OutOfMemoryException exclusion are contained; optional setup failure falls back
to the ordinary menu. Valid caller events are still forwarded unchanged.

The exact original event is forwarded once using `objc_msgSendSuper`; the exact
BOOL byte is returned. The return encoding is obtained from NSMenu's inherited
method (`c` on macOS x64, `B` on ARM64) and must be one of those encodings.
The import and callback use an explicit byte return and native object arguments.
Only existing system AppKit/Objective-C runtime imports are used.

**Lexical superclass is essential.** The production wrapper caches NSMenu,
the superclass of the declaring observed class. It does not ask for the
receiver's dynamic superclass. Apple's [objc4 message.h](https://github.com/apple-oss-distributions/objc4/blob/main/runtime/message.h)
defines `super_class` as the first class searched and requires a matching call
signature. Apple's [KVO implementation guide](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/KeyValueObserving/Articles/KVOImplementation.html)
documents intermediate `isa` classes. An inherited observation IMP would recurse
if dispatch started at its dynamic superclass. This is a correctness invariant,
not evidence that current NSMenu instances are KVO-subclassed.

## Evidence semantics

Five appended events preserve existing enum values and schema-v1 vocabulary:

| Operation | Positive meaning |
| --- | --- |
| `native.menu.observation.ready` | Instrumented owned main menu installed |
| `native.menu.observation.unavailable` | Optional setup/installation observation unavailable |
| `native.menu.save_family.entered` | Owned method received a matching candidate |
| `native.menu.save_family.returned_true` | Its superclass returned true |
| `native.menu.save_family.returned_false` | Its superclass returned false |

`RecordNativeMenuCheckpoint` is deliberately closed to these five events. Each
record is a fresh session child, zero-duration success, with empty attributes;
ambient Activities are ignored. False is a successful observation of false,
not failed Save. No entry/return pair or input-to-request edge is invented.
There is no retained event, long producer lease or ambient input Activity.
Native JSON acceptance inventories these checkpoints independently of the
unchanged mandatory Save chain. Missing checkpoints remain unobserved, never
proof that a callback did not execute. Killed prefixes are censored.

## Validation performed locally

- Release strict-warning Native build: success, zero warnings/errors.
- `NativeMenuObservationTests` plus existing Telemetry/TelemetryRequest tests:
  **42/42 passed, 0 skipped**. Retained TRX:
  `.cache/menu-observation/menu-observation-regression.trx`.
- New tests cover 12 candidate/noncandidate cases, Shift/Caps Lock ambiguity,
  Control/Option rejection, no ambient-parent inheritance, unique fixed
  checkpoints/empty attributes, closed vocabulary, and warmed default-off
  **zero managed allocations for 5,000 valid checkpoint calls**.
- Artifact-reader tests (separate reader commit `21d9183`): NativeAcceptance
  **7/7**, ordinary JSON probe **38/38**, including privacy, unknown operations,
  forced-prefix inventory and actual exit codes (`0` and `-9`, unknown null).

The existing strict `--check-native-mac-flow-rendering` route now additionally
executes an isolated owned ABI control. No synthetic event is posted externally
or to the application. A separate tiny unmanaged wrapper passes its cached
control superclass to the same production forwarding core. This permits exact
false/true return, unchanged-event, once-only dispatch, nested invocation and
inherited-IMP descendant assertions without changing SDK methods. It proves
shared forwarding core/import ABI only when actually executed on each Mac RID;
it is not a certificate of ordinary keyboard routing or the stock NSMenu's
business behavior. Probe state/pointers are cleared and owned instances released.

## Still required

Windows local tests/build cannot execute AppKit. Both Mac Native AOT RIDs must
run the updated strict Flow probe. An ordinary one-attempt external Command-S
workflow must positively retain menu entry/return checkpoints before claiming
this boundary covers that route. There is no current coverage or enabled-cost
certificate. Measure tracing off/on on the same new Mac binary; do not borrow
Windows measurements or interpret a green non-gating job as a successful probe.
Failure containment is source-reviewed, not a claim of injected native framework
exceptions (Objective-C exceptions are not assumed catchable by managed catch).
