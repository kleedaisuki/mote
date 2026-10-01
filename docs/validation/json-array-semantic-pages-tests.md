# JSON array semantic-page directed validation

2026-10-01. Test owner: `tests/Mote.Tests/JsonArrayCertificateTests.cs`.
Basis: `docs/json-array-semantic-pages.md`, not inferred current behavior.

## Independent expectations

The ordinary corpus contains 2,400 complete nested objects, each with one
decoded-equivalent `a` / `\u0061` duplicate, delimiters inside an escaped string,
Chinese and a non-BMP character, and 480 padding characters. Its source exceeds
1 Mi UTF-16 units. LF and CRLF variants are tested. System.Text.Json independently
accepts the syntax and confirms the root element count; duplicate counts are
hand-derived from the generated object structure (System.Text.Json is not an
oracle for mote's duplicate-key policy).

Every directed accepted Complete checks current version, whole-document coverage,
root span and exact diagnostic total. Projection comparison uses a *fresh Full*
production session, comparing ordered diagnostics, tokens, and node shape including
name/value/absolute UTF-16 spans. This is a differential oracle for the reuse path,
not an independent replacement parser. The syntax/count expectations above remain
independent of both sessions.

Cases cover cold near-start/near-end Visible, local nested structure/count change,
decoded-name change, a contiguous growth-shifting edit batch, viewport away/back,
quote deletion and repair, missing history, shell/cross-page/exact-seam edits,
large other-root Full fallback, oversized owner refusal, compact primitive arrays,
Undo/Redo as new versions, pre-canceled candidate retry, deterministic page/precommit
cancellation and 100 distributed edits (seed 64517).

Private instrumentation is read through reflection so it does not create a public
policy API: LastVisitedUnits, ArrayPageCount, ArrayPageSpans,
ArrayCertificateVersion, ArrayDirtyPageCount, AnalysisHook. Hook tests require the
selected stage actually to execute, and assert unchanged committed version/dirty
state before retrying the same edit chain.

## Baseline discriminator

Command:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --filter FullyQualifiedName~JsonArrayCertificateTests --nologo --no-restore
```

Initial public-API-only 12 cases ran against the unchanged streaming implementation:
6 passed, 6 failed. Failures were precisely old Complete instead of proposed
Provisional for both cold Visible cases, shell/cross-owner edits, oversized-owner
edit, and missing edit history. This verifies those assertions detect the proposed
policy change. Windows x64, .NET 10 Release CoreCLR; not Native AOT or GUI evidence.

## Implemented directed and seeded result

The command above subsequently passed **28/28**, zero skipped, approximately
18 seconds reported test duration on the implementation. Log:
`.cache/json-array-expanded-tests.log`. This is Windows x64 Release CoreCLR,
not a Native AOT or external input-to-draw measurement.

The expanded suite additionally exercises:

- **100 directed boundary edits**, distributed across actual certified owner seams
  and the array shell: whitespace insertion, valid same-token replacement, and
  invalid closing-token replacement. Every edited Visible is Provisional with
  unknown total. System.Text.Json accepts all intended valid variants and rejects
  the invalid variants. Undo produces new versions before the next Full rebuild.
- **1,000 seeded nested-value replacements** (seed 64517) on a >1 Mi-unit CRLF
  analogue. Eight variants include numbers/exponents, primitives, nested arrays
  and objects, escaped quotes/backslashes/surrogate pairs, and one exact
  escaped-equivalent duplicate. Fixed-width value slots make the independently
  known edit coordinates stable while grammar and local duplicate totals change.
  Every accepted Complete matches hand-maintained global counts, a fresh Full's
  exact tree/token/diagnostic projection, and System.Text.Json acceptance/root count.
  Actual interactive source visits remain at most 512 Ki units per turn.
- Same-version **different snapshot identity** invalidation; two distant dirty
  owners repaired separately (Complete only after both repair); interior whole-value
  replacement introducing extra root elements; final-owner trailing-comma refusal
  and repair; escaped quotes, surrogate escapes and a number crossing the nominal
  65,536-unit cut coordinate.
- A non-inlined fixture returns weak references to initial/current snapshots after
  disposing the document, while the certified session remains alive. Both snapshot
  objects become unreachable after full GC/finalizer/full GC; the session still has
  page metadata. This tests absence of strong snapshot-object retention by the
  tested session, not retained rope bytes or process memory under long history.

One first expanded run had 24 passing cases and one **test harness defect**:
xUnit `Assert.Throws<JsonException>` requires an exact runtime exception type,
whereas System.Text.Json emitted its derived JsonReaderException. The assertion
was corrected to `ThrowsAny<JsonException>` without changing the expected syntax
rejection. The final 28-case run above includes this correction and three added
nominal-cut cases. No product defect was encountered by this expanded suite.

## Coverage limits

This suite is not the 100 MiB latency/retained-memory experiment, Native AOT input
or paint acceptance, hash-collision revisit budget proof, or a retained-memory
measurement. Cancellation uses explicit deterministic stage callbacks rather
than flaky wall-clock delays. Page-count assertions distinguish source-sized
metadata from per-element indexing but do not measure retained bytes. Those
separate claims must retain their own evidence before product promotion.
