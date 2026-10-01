# Production native source binding validation

## Contract and scope

The expected behavior comes from section 8 of
[`ordinary-editing-locus.md`](../architecture/ordinary-editing-locus.md), not
from the diagnostic binding or its tests. The production model must prepare
edits without mutating the engine. The controller owns the one actual
`Document.Apply`. Native readback is admitted against the original snapshot,
document generation, version, and installation nonce. Selection direction is
known only when a collapsed selection or an actual native active-endpoint
witness supports it. Engine-originated display replacement must preserve
complete UTF-16 scalars and CRLF delimiters at both old and new boundaries.

`tests/Mote.Tests/NativeSourceBindingTests.cs` uses the production
`NativeSourceBinding` and actual `Document`, not the older diagnostic binding.
It independently splices returned display replacements and checks boundaries
directly from the string code units. It exercises:

- Pure preparation, one controller-style commit, notification count, and one
  Undo/Redo entry.
- Generation, version, nonce, equal-text unrelated snapshot, and mutated
  baseline rejection; old callback rejection after advancement.
- Repeated pure preparation and unchanged selection changes without history.
- Shared high/low surrogate replacements and mixed newline preservation.
- Runtime-constructed malformed UTF-16 and embedded NUL rejection.
- Synthetic CRLF interior, scalar interior, invalid range, and invalid active
  endpoint rejection.
- Forward/backward witnessed selections versus unknown ordered direction.
- Null/truncated/boundary-invalid import observations.
- Advance rejection of an unchanged unrelated snapshot, uncommitted text, and
  wrong successor versions.
- Explicit newline-seam replacements and 1,000 deterministic mixed-atom
  replacements, independently checked against the successor display.

## Qualified execution, 2026-10-02

Windows x64, .NET SDK `10.0.400`, Release, xUnit `2.9.3`. The coordinated
isolated fixture is
`.temp/native-source-product-binding-qualified-b3e355acbbd449e0876281676bced883/`.
It contains byte-exact frozen copies of the actual production binding,
contracts, projection and tests, and frozen actual Engine/Formats DLLs.
`SharedTypes.cs` extracts only the exact `NativeLineEndingMode` and
`NativeDocumentStamp` declarations from production `NativeShell.cs`; no binding
implementation or algorithm is substituted. This avoids unrelated in-flight
shell/controller compilation and shared build outputs.

Command, from the repository root:

```powershell
dotnet test .temp/native-source-product-binding-qualified-b3e355acbbd449e0876281676bced883/Binding.csproj -c Release --logger 'trx;LogFileName=binding.trx' --results-directory .temp/native-source-product-binding-qualified-b3e355acbbd449e0876281676bced883/results
```

Result: **34 passed, 0 failed, 0 skipped**, 68 ms reported test duration, exit
zero. The randomized case executed all 1,000 iterations. All three production
binding/contracts/projection files matched their frozen fixture hashes after
the run. No concrete model defect was reproduced in this qualified scope.

| Input / loaded artifact | SHA-256 |
| --- | --- |
| `NativeSourceBinding.cs` | `2BD50614B47361B8AAF2C9944AF7E5AC2D6B533A44C59C033D6D91D3EA108674` |
| `NativeSourceContracts.cs` | `A2F44DC21BB268BB0D259C971246A9318BC5FB7F3B96AF07EA6593B74009D9AD` |
| `NativeTextProjection.cs` | `1187E829D2D06E1026CF5E2CEB634055EEDA6A8966388E8464EDF126F4098FFF` |
| `NativeSourceBindingTests.cs` | `9E5E2E05343621408F7490C46C6DCA5471126844C7EB544310A4FFAC71769C72` |
| Loaded `Mote.Engine.dll` | `AC67B35FD8490E0D66C2BBDBFFF9CC62FB96CB076E22EAF4FDFAD672EC5BD7DA` |
| Loaded `Mote.Formats.dll` | `C58527F9633CC3D79CA30C979884F818B546C46235A22E110E901E8311874AEE` |
| Actual isolated `Binding.dll` | `C2E4C7E58915FD733230704114178BB70A48E8D5A2E0DF48E440958346B4F287` |

Fixture `test.log`, `results/binding.trx`, `input-hashes.json` and
`loaded-hashes.json` retain reproducible evidence. An initial directly linked
run under `.temp/native-source-product-binding-a961895ca7c3452a8da89770c723b2c1/`
also returned 34/34. Its two shared DLL source paths changed during concurrent
build activity (its loaded copies still matched its pre-run hashes). The
fully frozen run above is the authoritative qualification; the initial run is
retained, not pooled or counted as additional cases.

No native GUI, input source, global environment, or diagnostic probe was
started. This is not runtime acceptance of RichEdit, AppKit, controller
integration, IME, visible decoration, or accessible native ranges. The reported
test duration is not an application latency measurement.

## Model limitation

An unchanged preparation can require reference identity with its original
snapshot. A changed preparation checks successor version and exact source.
The bare immutable snapshot API does not identify its owning `Document`, so a
different document with identical source and the expected version cannot be
distinguished by this model alone. The controller must supply its own actual
committed snapshot and maintain the original document admission fence.
