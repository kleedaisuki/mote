# External Windows fixture adapter: independent review

## Scope and decision

Reviewed the uncommitted `Measure-WindowsOrdinary.ps1` delta against checkout
`9e817abcce61e545148bc1eb7878cedf7c610f9d` and the new
`Test-ExternalFixture.ps1`, frozen on 2026-10-01. No production, CI, existing
test, or driver implementation was modified by this reviewer. No native process,
GUI, clipboard, user home, or input-source operation was run.

**Decision:** no unresolved substantive finding in this bounded adapter after
the path-ancestry correction described below. Suitable for integration and a
subsequent separately authorized current-source Native AOT target capability
run. This is not approval of an existing binary as current performance evidence,
nor a completed GUI performance acceptance.

Frozen raw-file SHA-256:

| File | SHA-256 |
| --- | --- |
| `Measure-WindowsOrdinary.ps1` | `4B6E58ACBA180B51FBE2CBBE9CB4007E2EE9FD3998D331DFD08896E80BF1C418` |
| `Test-ExternalFixture.ps1` | `3D9D58291CE48B036E9FEFF40B80D0F699CAE7302851FFB85347BECDEFFCA124` |

Hashes matched both before and after independent validation. A source change
after this freeze requires delta review; these hashes describe local file bytes,
not a line-ending-normalized Git blob.

## Material checks

- External fixture use requires both a SHA-256 digest and `ReadinessOnly`.
  `DiagnosticTrace` is refused for this adapter. Thus its ordinary execution
  cannot enter the existing `WM_CHAR`/Save path, and no child trace endpoint is
  silently inferred from a parent clock.
- Resolved paths must remain beneath repository `.temp`; existing reparse-point
  ancestors are rejected. Exact 1 or 100 MiB size and `.json`, `.csv`, or `.md`
  extension are checked before launch. Each process receives a fresh copy with
  the same extension; copied bytes must match the pinned digest before launch.
  Both input and copied file hashes are rechecked before a passed result.
- The first 20 bytes must be printable ASCII without a line break. Their exact
  value is compared ordinally against bounded native source readback, in memory
  only. The report contains an assertion label, never those fixture bytes.
  This intentionally excludes BOM-prefixed, short-first-line, or other fixtures;
  it is not a general text-decoding validator.
- External-window discovery is process-owned and requires the actual
  `MoteNativeEditorWindow` class, verified against
  `src/Mote.Native/Windows/WindowsEditorShell.cs`. Canvas and RichEdit lookup,
  bounded source length, and selection acknowledgement remain required.
- The original Markdown generator, generated filename extension, default helper
  window behavior, edit/dirty/Save branch, and exact-save oracle remain intact.
  New report fields are additive. Binary SHA, source HEAD, fixture digest/format,
  child PID, and foreground PID retain distinct identities.
- Existing scratch containment and reparse-ancestor checks still precede
  recursive cleanup. The new test deletes only named leaves and empty owned
  directories, with ancestry checked before initial creation and final cleanup.

## Finding corrected during review

**Path safety, high confidence, resolved:** the initial new test checked only a
lexical `.temp` prefix before creating its synthetic files. A preexisting
`.temp` or `native-startup-external-tests` junction could therefore route its
writes and later cleanup outside the repository before the driver rejected the
fixture. The owner added `Assert-NoReparseAncestors $base` before the first
`New-Item` and in `finally`; final static inspection confirms both checks.
The driver's independent fixture-junction refusal is also exercised below.

These checks protect against preexisting routing mistakes in an owned test
workspace. They do not claim race-free isolation against another process
actively replacing ancestors during execution. A disposable, non-concurrently
mutated workspace remains an execution assumption.

## Independent validation

Windows PowerShell 7.6.5, repository working directory `D:\Code\mote`:

1. Parsed both frozen scripts using
   `System.Management.Automation.Language.Parser.ParseFile`: zero parse errors.
2. Extracted only the driver's embedded C# literal and compiled it with
   `Add-Type`: compilation succeeded. No Win32 method was invoked, and the
   ordinary script path was not executed.
3. Ran `./benchmarks/NativeStartup/Test-ExternalFixture.ps1`: **13/13 passed**,
   reported scope `preflight-only-no-native-process`:
   - owned exact JSON accepted;
   - edit/Save route, missing digest, missing fixture, wrong digest, trace option,
     out-of-scratch path, unsupported extension, wrong size, weak prefix, and
     fixture-junction path refused;
   - default Markdown inventory preflight preserved;
   - original fixture bytes unchanged.

All generated synthetic data remained under repository `.temp`; the test used
a deliberately non-executable `mote.exe` placeholder only because inventory
validation returns before native compilation and launch. This does not certify
that placeholder, or any other binary, as Native AOT.

## Evidence limits and next acceptance

Source-prefix binding plus `EM_SETSEL`/`EM_GETSEL` proves bounded source binding
and direct selection acknowledgement, **not editability**, real key dispatch,
semantic publication/completeness, compositor presentation, or physical paint.
The new child endpoint fields are null and explicitly not collected. Current
binary/fixture/host target execution is still pending; no latency, allocation,
working-set, peak-memory, or p95 figure is established by this review.

Use a freshly published current-source one-file AOT binary on a disposable
Windows target for the next small capability run, retaining its exact binary
digest and source provenance. Keep its external source/selection metrics
separate from any later child trace phases, and do not reuse old local binary
timings as evidence for this source delta.
