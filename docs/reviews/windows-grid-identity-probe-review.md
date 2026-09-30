# Windows Grid identity probe: independent assertion review

Date: 2026-10-01. Verdict: no blocking assertion defect found.

Scope: additions to tests/WindowsGridExternalProbe/Program.cs only; no production edits or broad test execution.
Reviewed source SHA-256: A1B87971E4975786D67909209E1B2133432C6894710D623F2CD24ECD948940D4

No blocking defect found within the requested assertion contract.

- Raw/Control/Content sibling walks are each capped at 1104 children and assert exact count, unique nonempty AutomationIds and unique nonempty runtime identity strings.
- Runtime identity strings preserve the complete integer vector rather than wrapper reference identity. Per-view sets are compared without enumeration-order assumptions.
- Selection references independently require local coordinates (0,0)/(0,1) and absolute Row 1 / Column 1 / Column 2 header relationships. Returned selection must have exact cardinality, no duplicate records, and exact identity/coordinate/header/value set membership.
- Rejected sparse AddToSelection must throw and preserve that same exact selection set. Incorrect count-only preservation no longer passes.
- New reads concern the launched synthetic fixture's table/cells; they add no foreign focus reads, physical input, or production mutations. Existing foreign-focus ownership guard is unchanged.
- git diff --check for the scoped file passed.

Limits: static assertion review, not an actual UIA target-host result. Cross-view checks compare AutomationId and runtime-ID sets separately, not their pairwise mapping; pairwise mapping is not claimed by this result. Exceptions still fail the probe nonzero via its established inconclusive classification.

## Narrow follow-up review

The strengthened delta remains sound: first and second reference identities are captured before their respective mutation; AfterSelect requires exactly the pre-captured first cell; independent fixture expectations now require R1C1 and R1C2; a child-limit violation records a product error before abort, so established final classification cannot downgrade that demonstrated violation to inconclusive. Scoped diff whitespace validation passed. No new blocking defect found; no target-host execution performed by this review.


## Reviewed bytes and separate target-execution evidence

The reviewed Program.cs raw LF SHA256 is
`A1B87971E4975786D67909209E1B2133432C6894710D623F2CD24ECD948940D4`.
The exact equivalent CRLF byte stream SHA256 is
`C6894B4EDCF931BAA322B878AF8965759AC7E44EFC10291200737A18C1AD5509`.
These are two exact reviewed byte streams, not permission to normalize arbitrary
untrusted source content. The workflow pin update belongs to the workflow owner.

The implementing validator separately built the final client with zero warnings
and errors and ran it once against an existing local win-x64 Native AOT editor:

```powershell
dotnet build tests/WindowsGridExternalProbe/WindowsGridExternalProbe.csproj --configuration Release --artifacts-path .cache/windows-grid-identity-probe/build
dotnet .cache/windows-grid-identity-probe/build/bin/WindowsGridExternalProbe/release/WindowsGridExternalProbe.dll .cache/windows-grid-accessibility/aot/mote.exe .temp/windows-grid-identity-probe .cache/windows-grid-identity-probe/report.json
```

That target binary SHA256 is
`BE2D17B41853A586F080F4DA3094203C2E9DD0A2AF57FF6B653DDB5B8708D0A0`:
**a previously published binary, not a fresh current-HEAD build**. The synthetic
fixture SHA256 is
`86796C9AF5EADC2DB5A0B8FBE3F14245DF7AB2F0456E6EE5189ED6987819E0B4`.
On Windows NT 10.0.26200.0/X64, the final client exited 0, classified `pass`,
and reported empty Errors/Inconclusive arrays. Each view reported 1104 children,
1104 unique AutomationIds and 1104 unique runtime identities with empty duplicate
arrays. All three exact selection snapshots passed. The execution evidence is
`.cache/windows-grid-identity-probe/report.json`; its durable interpretation and
historical hosted results are in
[the CI validation record](../validation/windows-grid-accessibility-ci.md).

This target result was produced by the implementing validator, **not an
independent target execution by the assertion reviewer**. The review's
independence concerns assertion semantics and the final delta. No production
code change, broad solution-suite rerun or new AOT publication was performed for
this task. No product defect was observed within this one binary's tested scope.

The previous binary's production coverage limitations remain as recorded in
[Windows accessibility validation](../validation/windows-grid-accessibility.md).
New hosted x64/ARM64 execution is pending for these new client assertions.
The local x64 result does not supersede the historical ARM64 foreign-global-focus
blocked classification. There is no reader-speech, physical-IME, successful
off-owner SetFocus, RangeValue-write, large-file-memory-teardown or default/release
enablement claim. Synthetic fixture isolation, exact-PID ownership checks, no
clipboard/global key input, and owned-process cleanup are unchanged.
