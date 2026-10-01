# Clipboard probe causal API migration: independent pin review

Date: 2026-10-01. Reviewed immutable source delta: `8a24e90..b2f4661` for
`tests/Mote.Tests/NativeCsvGridClipboardWorkflowTests.cs` and
`src/Mote.Native/Mac/MacCsvGridClipboardProbe.cs` only.

## Verdict

**Approved for static source-pin refresh; no substantive defect found.** Each
file changes exactly the fake shell declaration from separate parameterless
Save/Save As events to one typed `Action<NativeSaveRequest>? SaveRequested`.
The probe shells never invoke these events. This is required internal interface
migration, not a new clipboard or Save experiment.

Neither delta changes hosted-runner approval markers, run identity, workspace
checks, clipboard publication, prior-clipboard handling, synthetic fixtures,
refusal sentinels, case count, snapshot/modified/undo/navigation invariants,
report validation, or failure behavior. The reviewed byte change therefore
explains source-pin drift without authorizing weaker safety oracles.

## Approved source hashes

SHA-256 is calculated from each immutable `git show b2f4661:<path>` blob after
normalizing CRLF to LF, then separately converting every LF to CRLF. UTF-8 bytes
and trailing newline are otherwise preserved. These are reviewed constants,
not values generated from current source to grant permission dynamically.

| Source | Ending | SHA-256 |
| --- | --- | --- |
| Windows workflow test | LF | `40310CB3B0D8EB1EC68EE29C3BAC8D98EE2F5DB9C40EF55004646C5AB50D0EE8` |
| Windows workflow test | CRLF | `7F23EF2EF31C85D6E4177C2B71590B47B4D977014A51D06979B5606BA32BB855` |
| macOS clipboard probe | LF | `D0E868D78B91D621EB06FEB80923C519E0305A7361761E0546EF88AEAB84E48B` |
| macOS clipboard probe | CRLF | `9E4A5AEF6B39DB38B05396B068514E0F04331FEE59D63231FF5EF0524AB95DDC` |

## Scope limits and follow-up

No actual OS clipboard probe was launched locally and no clipboard contents
were read. No production, invocation or manifest files were modified by this
review. The forthcoming invocation constants and detached macOS invocation
manifest require a separate final byte-level review. Existing strict clean
checkout, static source/invocation matching and report gates must stay intact.
A prelaunch hash refusal is not evidence that clipboard publication itself
failed, nor does this source review certify the next hosted run.
