# Native full-source diagnostic binding validation

## Scope and expected contract

This is a pure-model check of `NativeSourceDiagnosticBinding`, not approval of a
full-resident native source product profile. The binding owns a complete immutable
replica of the engine snapshot: extent starts at zero and ends at the full source
length, independently of viewport or bounded LegacyPage. Engine history and file
persistence remain authoritative.

Expected outcomes derive from the binding's documented import/admission contract
and the engine's scalar-boundary, atomic replacement and exact-byte persistence
contracts, rather than from the helper's current output:

- Exact installation readback is required. A failed import is permanently refused.
- Generation, installation nonce, snapshot identity and mutation version must not
  let a retired or foreign document callback modify the current document.
- A final native readback produces at most one atomic engine replacement. Original
  mixed CRLF/LF/CR delimiters outside the change remain byte-identical.
- A replacement of Unicode scalars sharing surrogate halves must include complete
  scalar boundaries. CJK, emoji, combining marks and bidi controls survive edits.
- No-op text echoes may advance observed sorted selection, but create no history;
  they still retire the callback binding and return a certified successor.
- Accepted edits preserve installation nonce; native reinstallation is not implied.
- Embedded NUL and non-round-trippable display boundaries are unsupported, not
  truncated or silently normalized.
- Disposal revokes the binding, not the engine-owned document.
- Cancellation before work leaves the document and certificate usable. Cancellation
  raised after atomic Apply must not disguise an already committed mutation.
- Save and fresh open preserve exact UTF-8 bytes, including original mixed endings.

## Artifacts and execution

Independent checks live in `tests/Mote.Tests/NativeSourceDiagnosticBindingTests.cs`.
Persistence fixtures use uniquely named directories under repository root
`.temp/native-source-binding/`; no system temporary directory, user configuration,
GUI, native host, clipboard or input source is used.

On 2026-10-01, the production owner froze common/OS/runner sources after the
coordinated Release integration build (zero warnings/errors, 7.01 seconds;
`.cache/validation/native-source-capability/integration-build.log`). The validator
then ran only this new suite, without rebuilding or executing native UI:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-build `
  --filter FullyQualifiedName~NativeSourceDiagnosticBindingTests `
  --logger "trx;LogFileName=binding.trx" `
  --results-directory .cache/validation/native-source-binding
```

Observed: **33/33 passed, zero failures/skips**, process exit **0**, test duration
385 ms. Environment: Windows 10.0.26200, x64, .NET SDK 10.0.400, net10.0 test target.
The command output, actual TRX and source/assembly identity inventory are retained
in `.cache/validation/native-source-binding/{run.log,binding.trx,hashes.json}`.
The native project assembly is named `mote.dll`, not `Mote.Native.dll`; the first
inventory attempt used the latter and found no such file. The inventory was
corrected to the actual loaded output name without repeating tests.

Frozen identities (SHA-256 of actual file bytes):

| File | SHA-256 |
| --- | --- |
| NativeSourceDiagnosticBinding.cs | `4C6C870C7C9F5DAF9475947E42B2052C57E6E36D1D60FC55CAF496648B73D680` |
| NativeTextProjection.cs | `1187E829D2D06E1026CF5E2CEB634055EEDA6A8966388E8464EDF126F4098FFF` |
| NativeSourceDiagnosticFixtures.cs | `E57F160A654BB58A146E8BDF2E1FAD983C09F6C3B3EC04E7FC66B556F01C480C` |
| NativeSourceDiagnosticBindingTests.cs | `31870384A60ED189745D7F654DDE264AA64874B379CF0326D79DE6628BCF859B` |
| Mote.Tests.dll | `BBA9A31940A2144B07F97F46DA67B3A413B202A674539F998E23BADA67CEDD6B` |
| mote.dll | `473DA812DD4F824BD503750F15538196C2BADF2F8D1813CF7A07E3289A1F5F1F` |
| Mote.Engine.dll | `9B5386EBDD6FE998C3B951B080667E23FA639F2AA437CC43D4637CC104E5E017` |

## Results and interpretation

The checks verified complete extent/offset maps in preserve and CRLF display modes,
exact import refusal, independent admission fences, callback retirement and
successor reuse, one-history-entry mixed-script edits, scalar-complete emoji
replacement, selection-only echoes, unsupported readback, cancellation and disposal.
The IO workflow edited mixed-endings UTF-8, undid/redid the single edit, saved and
fresh-opened it, asserting actual bytes rather than just a displayed string.

Three generated ordinary fixtures passed strict UTF-8 roundtrip, NUL exclusion and
exact insertion-marker boundaries. The novel is **3,711,959 UTF-8 bytes**, not that
many UTF-16 units; dense JSON is **524,288 UTF-8 bytes**. Mixed text includes all
three newline spellings and multiple scripts. Dense JSON before and after `新🧪`
insertion passed the independent `System.Text.Json` parser and the product policy
with zero error diagnostics and more than 1,000 semantic tokens.

During test design the validator identified the shared projection's surrogate-half
prefix/suffix risk and notified the owner before execution. The dedicated shared
projection fix was already qualified when this binding suite ran. Both independent
binding regressions now pass (`😀` to `😁`, and U+1F600 to U+1FA00 sharing a low
surrogate). This run does **not** claim a historical unfixed binary was executed;
see the shared projection owner's separate regression evidence for that claim.

**Verdict:** the scoped pure whole-source binding and controlled engine persistence
contracts passed. Full-resident native behavior and product suitability remain
unverified by this suite.

## Limits

This does not verify AppKit/RichEdit offset interpretation, physical selection
direction, paint/compositor latency, scrolling, actual IME, native undo or hosted
Native AOT publication. Reconciliation consumes full strings/projections and is
O(n); the tests do not imply sublinear edit reconciliation or a large-file claim.
The immediate-before-Apply cancellation check is an explicit implementation phase
boundary. The API has no deterministic injection hook between projection work and
Apply; it is source-inspected, not verified through a scheduler-sensitive race.

