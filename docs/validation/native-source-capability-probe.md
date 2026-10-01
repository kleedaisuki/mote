# Full-native source capability probe

## Scope and admission

`mote --check-native-source-capability <fresh-repo-output-dir>` is an explicit,
declared-hosted diagnostic, not the default source editor or a product profile.
Before any configuration load, artifact creation, telemetry writer or native
object, it requires `GITHUB_ACTIONS=true`, `RUNNER_ENVIRONMENT=github-hosted`,
and `RUNNER_OS=Windows` on Windows or `RUNNER_OS=macOS` on macOS. These are
mutable declared runner identifiers, not security attestation of a hosted
machine; callers can forge environment values. The guard prevents accidental
invocation outside the declared workflow contract, not adversarial invocation. The caller must run from a repository root with a
`.git` directory. The output must be a previously nonexistent descendant of
root `.cache/` or `.temp/`; existing linked/reparse ancestors are rejected.
Each output ancestor is inspected with File.GetAttributes; reparse entries are
rejected even when their target is missing. Only FileNotFoundException and
DirectoryNotFoundException mean absent; other I/O/access failures reject
admission. Existing output entries and non-directory ancestors are refused.
Files use exclusive CreateNew. These path checks do not claim protection against
an adversarial concurrent filesystem rename; the runner controls its scratch.

Configuration is loaded through the existing loader using the isolated
`<output>/mote-home` root, ignoring an inherited MOTE_HOME. Existing bounded
JSONL telemetry is explicitly enabled there. No settings in the real user home
are read or mutated. Existing telemetry operations are reused; there is no new
trace schema, network exporter or runtime dependency.

## Controlled experiment

Three generated nonprivate fixtures run sequentially with one native view:

- Mixed scripts, supplementary characters, combining marks and CR/LF/CRLF.
- A synthetic novel-shaped TXT of exactly 3,711,959 UTF-8 bytes (3.54 MiB
  rounded); byte size is not UTF-16 length.
- Dense valid JSON of exactly 524,288 UTF-8 bytes.

Each input is written once; exact UTF-8 byte counts, source UTF-16 lengths and
SHA-256 identities are reported. The actual engine opens these bytes. A complete
immutable engine snapshot is bound at generation 1 with an installation nonce.
Windows uses CRLF projection; macOS preserves canonical newline spelling.

The probe imports the full display, separately times complete native readback,
and certifies exact installation. Selection uses the global projection of the
fixture edit offset. Scroll-to-end, draw submission and restoration must leave
text and selection exact; before/after scroll tuples are recorded. Equality is
not a claim of distant pixel-perfect restoration or physical paint.

Full policy analysis must preserve source and produce zero diagnostics for the
valid fixtures. Every semantic token is projected and published as foreground
attributes using the theme policy. Publication timing includes exact text,
selection, scroll and engine snapshot/history verification. Analysis is a full
policy call, not separately instrumented parser and semantic internals.

One controlled native insertion is followed by complete text and selection
readback. Reconciliation must produce Applied and exactly one engine version /
mutation advance. A no-op echo consumes a successor binding without another
engine mutation. Old stamp, wrong nonce and retired binding attempts must be
Stale. Accepted edits do not reinstall native text. Engine Undo and Redo each
explicitly reinstall the whole native display with a new nonce and exact
selection certificates; semantic attributes are republished after each state.

Save targets a new output path through the existing engine SaveAsync under the
existing Save telemetry scope. Saved bytes must match the independently encoded
expected source exactly. A fresh Document.OpenAsync must recover that source,
with clean state and no history. This is **fresh-document-reopen**, not a fresh
process, GUI reopening or a native command-to-save trace certificate.

## Evidence and limits

`report.jsonl` uses explicit Utf8JsonWriter serialization suitable for Native AOT.
Each timed phase flushes a durable entered record before work, followed by a
completed/failed record with elapsed milliseconds and process-wide cumulative
allocated-byte delta. Working set is a sampled process value, not peak memory.
Activity trace/span IDs are included only when available from the exact existing
scope. Fixed phase/fixture/backend labels, counts, scroll tuples and hashes are
allowed; generated text, raw paths, handles, PIDs and exception messages are not.
Partial records are retained on failure; an entered record without terminal
means incomplete evidence, not success. Native process hangs require the hosted
workflow's external timeout; the probe creates no supervisor or child process.

Import, complete readback, projection, mapping/difference, whole-policy analysis,
all-token projection/publication, draw return, engine history, Save and reopen
are separate intervals. Native insertion includes internal adapter readback cost.
These are not physical-paint measurements, real-input latency percentiles, IME
coverage, accessibility speech, user typing or a universal arbitrary-edit proof.
No local native GUI execution is authorized by this contract. Hosted runs on
both platforms/architectures remain necessary evidence.

## Numeric field interpretation

`fixture` / `saved-bytes` count_a is actual UTF-8 byte length and count_b is
canonical UTF-16 length. `semantic-counts` count_a is semantic token count and
count_b is diagnostic count. Other Note records use zero placeholder counts,
not inferred byte sizes. `allocated_bytes` is the difference of approximate
process-wide GC.GetTotalAllocatedBytes(false) samples, not exact owner-thread
allocation or exclusive phase allocation. Working-set samples <= 0 serialize as
null/unavailable. Executable SHA identifies Environment.ProcessPath; hosted
Native AOT execution is required to interpret this as the actual mote binary.
The runtime architecture is reported separately.

Dedicated import/edit/semantic draw-return phases call FlushDraw separately;
scroll-draw-return still includes scroll, draw submission and restoration. Style
publication certifies API return and preservation, not physical foreground ink
or color attribute readback. Telemetry enabled/nonfaulted/zero-dropped health is
required before the final completion marker; normal sink shutdown follows it,
so final writer/drain evidence must also be inspected by the workflow reader.

## Local integration checkpoint (not native execution)

The coordinated Release tests-project build completed with **zero warnings and
errors** (7.01 s), then the independent binding/fixture suite passed **33/33**,
zero failures/skips. That run used the explicitly frozen `mote.dll` identity
`473DA812...A1F5F1F`; [binding evidence](native-source-binding.md) retains its full
source/assembly hashes and TRX rather than attributing that run to a later build.

Independent [review](../reviews/native-source-capability-review.md) found that
failed-phase timing sampled after error-report serialization/fsync. The runner
now samples elapsed time and allocation immediately upon catching the action
failure, before recording it; the original counterexample is retained in that
review. Saved byte identity is also emitted as **observed before assertion**;
`exact` is emitted only after verification. At that checkpoint the runner SHA-256 was
`E7872B18EEEF5682EC0E8D7F2B9D865DE80060AC727EEE75331A47F0E77573A3`.

Those runner-only changes received a final Native Release compilation:
**zero warnings/errors**, 5.11 s, assembly SHA-256
`D41D0BAA892487CD7EF1CE6B2D15DE638E3A04646D70B55D56618663ABECC1C9`.
Unchanged model tests were not replayed. Build logs and the final source manifest
are under `.cache/validation/native-source-capability/`. The current Mac source
already includes its exact byte-return `respondsToSelector:` bridge in the
original adapter commit; an earlier uncommitted draft is not the final ABI.

No local GUI, native factory or CLI diagnostic was executed for this checkpoint.
Hosted Native AOT import, native selection/style/draw return and actual cost
observations remain **pending**, not inferred from compilation or pure tests.
The final global report marker is identified by `phase=probe,state=complete`;
its fixture context remains the last processed fixture. A successful process
must still retain numeric exit and normal telemetry shutdown evidence.

Failures carry a private source-defined invariant code, or a closed exception
category with numeric HResult. Messages, Data, stack traces and arbitrary type
names are never serialized. Critical import/edit/selection/history/style/Save
mismatches have distinct codes. `post-edit-native-range` count_a/count_b mean
observed display start/length; `post-edit-source-range` means canonical
anchor/active; `commit-counts` means mutation count/version advance. Import
readback records UTF-8 display-byte identity and UTF-16 display length before
certification; it is not the original file-byte hash on Windows CRLF projection.
`saved-bytes` is observed before the exact comparison, so failure preserves the
actual byte identity without claiming success. Closed failure code plus durable
last-entered phase identifies the failed invariant without exposing text.
Semantic counts are recorded before valid-fixture diagnostics are asserted, then a completion marker follows successful publication.


## Admission correction qualification boundary

Commit `8bc2919` records the earlier runner qualification and remains historical
build evidence. The later admission-only correction adds the declared
RUNNER_ENVIRONMENT check and authoritative File.GetAttributes ancestor checks.
It does not change source binding, native edit, analysis or persistence APIs.
Earlier build/test identities do not qualify the changed guard; focused portable
guard tests, independent review and the coordinated final build are required.
No local native invocation or process-global environment test accompanies this
correction.


### Private testability seam and excluded harness attempt

The first 12 focused admission cases failed before exercising their intended
guards because VSTest's current directory was its output bin directory, not the
repository root. Those results are excluded as unqualified harness evidence;
they do not qualify or refute ancestor-link admission behavior.

The private AdmitDirectory helper now receives an explicit repositoryRoot. Run
always passes its actual Environment.CurrentDirectory; relative output arguments
resolve against that same root, preserving normal invocation semantics. Portable
tests pass their discovered actual repository root without changing process cwd
or environment variables. This adds no public API, CLI option or environment
override. The matching affected Release build completed with **zero warnings and
errors**, 12.00 s; the same 12 cases then passed **12/12**, zero failures/skips,
84 ms. Live-target and dangling-target directory links, plus a dangling output
link, were all actually created/read as reparse points and rejected without
elevation or target changes. No global cwd/environment mutation, subprocess,
native object or GUI was used. The prior 33 binding/model cases were not replayed.

The qualified [admission evidence](native-source-admission.md) freezes runner
SHA-256 `7097EF0924FC5F792EB1E9A118EF4613FE8824EAD8B7B2EEB4CA65CCAF3971E0`
and actual loaded `mote.dll` SHA-256
`A9DC93829CCE6115C76FEB63463E4216DCE8C97A06D92D86113E4D3D65EE6DFF`.
This closes the scoped pure admission qualification, not hosted native editing
or secure host attestation. Those native observations remain pending.
