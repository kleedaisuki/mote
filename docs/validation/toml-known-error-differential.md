# Independent large-TOML known-error validation

## Contract and scope

The expected behavior comes from `docs/semantic-policy-frontier.md`, not from the
implementation's ownership trie. A completely certified prefix followed by one
individually valid conflicting statement may expose exactly one located ownership
counterexample. Analysis must remain Provisional, and the whole-document diagnostic
total must remain unknown. An unsupported or exhausted prefix, including certificate
loss during the conflicting transition, cannot justify that counterexample.

The dedicated public-session suite is
`tests/Mote.Tests/TomlKnownErrorDifferentialTests.cs`. It does not call, copy or reflect
over the ownership implementation. Diagnostic spans are literal source coordinates.
The negative controls intentionally contain invalid TOML which must **not** produce
an ownership witness because the prefix is uncertified. Independent parser rejection
of these files does not license publishing a diagnostic after that loss of knowledge.

## Fixtures and boundaries

Every fixture crosses 4 MiB using 1,025 comment lines of exactly 4,096 UTF-16 units
each. These comments do not alter ownership. The tests retain the exact UTF-8 files
under `.temp/toml-known-error-differential/fixtures/` for Python `tomllib` validation.

| Group | Independent expectation |
| --- | --- |
| Duplicate, escaped-equivalent key, scalar/inline parent, duplicate table, dotted-defined table, multiline value, EOF without LF | One exact first ownership diagnostic, Provisional, unknown total |
| Unknown prefix, certificate loss in the same transition, malformed preceding statement, unclosed EOF | Provisional without ownership witness |
| Valid implicit parent | Complete, zero diagnostics, exact total zero |
| 256 Ki UTF-16 statement length versus one unit beyond | Witness at admitted boundary, no witness beyond |
| 64 physical lines per logical statement versus 65 | Witness at admitted boundary, no witness beyond |
| 120,000 logical statements versus 120,001 | Witness at admitted boundary, no witness beyond |
| Exactly 200,000 bindings versus one three-binding dotted assignment beyond | Witness at admitted boundary, no witness beyond |
| Repair/Undo/Redo with absent or false edit chain; old immutable snapshot | Facts belong to the requested snapshot and version, never prior cached results |
| Pre-canceled call, then uncanceled reuse | Cancellation throws; subsequent authoritative witness remains correct |

The binding fixtures have two initial scalar bindings and 66,666 independent
three-component dotted assignments: `2 + 3 * 66,666 = 200,000`. The over-cap case
adds one more dotted assignment, independently separating binding exhaustion from
the 120,000-statement limit. The test does not promise that arbitrary over-cap files
are diagnosed; refusal is precisely the expected behavior.

## Reproduction

Run the focused tests only after the coordinated Native controller build is ready:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release -warnaserror --filter FullyQualifiedName~TomlKnownErrorDifferentialTests --logger "trx;LogFileName=toml-known-error-differential.trx" --results-directory .temp/toml-known-error-differential/results
```

Then run the retained independent oracle:

```powershell
python .temp/toml-known-error-differential/oracle.py
dotnet run --project .temp/toml-known-error-differential/baseline/Baseline.csproj -c Release -warnaserror
```

The oracle reads every exact fixture as UTF-8 using `tomllib.loads`, records the raw
SHA-256 and parser error in `python-oracle.json`, and expects acceptance only for
`valid-implicit.toml`. It asserts exactly 21 files and equality of every classification.
Its essential independent procedure can be reproduced without production code:

```python
from pathlib import Path
import tomllib
paths = sorted(Path(".temp/toml-known-error-differential/fixtures").glob("*.toml"))
assert len(paths) == 21
for path in paths:
    try:
        tomllib.loads(path.read_bytes().decode("utf-8"))
        valid = True
    except tomllib.TOMLDecodeError:
        valid = False
    assert valid == (path.stem == "valid-implicit"), path.name
```

## Observed results and verdict

Executed on 2026-10-01 on Windows 11 x64 build 26200 with .NET SDK 10.0.400,
target `net10.0`, Python 3.14.6, and pinned Tomlyn 2.10.1:

- Focused Release `-warnaserror`: **24 passed, 0 failed, 0 skipped** (reported test
  duration 1 second; not an editor latency benchmark).
- Python exact-fixture oracle: **21/21** matched, with 20 rejected files and one
  accepted valid implicit-parent control. Python's TOML 1.0 dialect is sufficient
  for these particular shared-subset fixtures; this is not TOML 1.1 certification.
- Baseline session from commit `4ea4a2ac21a3000ba17c7c812650ae0d91625914`, compiled
  in the isolated repo-local harness against the unchanged remaining Formats
  sources: the same exact `duplicate.toml` produces **Provisional, 0 diagnostics,
  unknown total**. Thus the new one-witness assertion detects the original defect;
  the corrected production session passes it. The harness's initial incorrect
  relative project path was fixed locally before this successful reproduction;
  that setup error was not a product failure.

Artifacts are the TRX under `results/`, `python-oracle.json`, exact `fixtures/`,
`oracle.py`, `baseline/`, and `baseline.log`, all below
`.temp/toml-known-error-differential/`.

Validated source SHA-256:

| Source | SHA-256 |
| --- | --- |
| `src/Mote.Formats/TomlIncrementalSession.cs` | `A4AB0C96584DA1400629669B50D0AC85F1EADEB46813BC15774813FF58E8E43D` |
| `tests/Mote.Tests/TomlKnownErrorDifferentialTests.cs` | `1614F667E81FC8A86C2FB75731F19AB6DBEB0E6EBD5FB58E8BA8FE6B7FB38C2D` |

**Verdict:** the tested public Formats slice preserves known certified ownership
errors while correctly refusing unproved downstream facts, preserves all four
resource limits, handles EOF without LF, and binds repair/history results to the
authoritative snapshot despite missing or false edit chains.

No real GUI, native idle publication, mid-scan cancellation timing, arbitrary TOML
grammar completeness, or target-platform behavior is certified by this suite.
Those claims require separate controller and hosted-native evidence. Pre-cancel
and subsequent reuse are verified; internal committed-state mutation is not
directly observed through a public API. Finite differential coverage is evidence
for the stated slice, not a proof of every ownership transition.
