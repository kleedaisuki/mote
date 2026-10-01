# Windows product release source qualification

Date: 2026-10-02. Scope: ordinary Windows NativeSource integrity, not a new
performance budget or a claim that every native input/accessibility workflow
has been accepted. The user permits multi-file delivery while retaining AOT.
This work does not choose a new UI framework or change the central launch default.

## Retrieved context and acceptance boundary

The native README, paused decision handoff, existing Windows source adapter,
source binding/controller tests, native theme controls and standards audit were
read before implementation. Engine remains the only source/history/I/O owner;
RichEdit owns native layout and selection. Ordinary literal source, accurate
CRLF/canonical offsets, no native history fork and stale-observation rejection
are integrity requirements, independent of the old 100 MiB stress workloads.

The historical `tests/NativeWindowsWorkflow.ps1` is **not** candidate acceptance:
it hardcodes `--legacy-page` and expects a bullet dirty marker, whereas
NativeSource uses an asterisk. The release coordinator was notified to create
the actual candidate/default workflow and use process-targeted controls rather
than changing global clipboard or keyboard settings.

## Release-critical defects corrected

### Literal source was mistaken for RTF

`InstallSource` used `EM_SETTEXTEX`, which recognizes a leading RTF header.
A legitimate plain-text or Markdown document starting with `{\rtf`-style
source could therefore lose its literal representation and fail exact
certification. The product preview importer already avoided this distinction
for plain text. Source installation now uses checked `SetWindowTextW` and keeps
the existing embedded-NUL rejection and complete exact readback certificate.
This is a transfer-correctness fix, **not** adoption of the diagnostic-only
`EM_STREAMIN` optimization. Native range publication continues using literal
`EM_REPLACESEL`.

### Deferred native selection could use the previous text map

RichEdit can issue `EN_SELCHANGE` before `EN_CHANGE`. Its posted selection
observation can consequently execute before deferred source admission. Native
endpoints already describe the new buffer while `_visibleText` and its CRLF map
still describe the old certified buffer. At an appended caret this can throw
an out-of-range mapping exception; other edits can publish inaccurate offsets.

`PublishSourceView` now defers observations while a source candidate message is
queued. The existing candidate admission/acknowledgment queues a new view after
the exact installation advances. This preserves native endpoints instead of
clamping or inventing positions against stale text. Synchronous command
settlement still uses the candidate's actual selection.

## Concrete local evidence

New `tests/Mote.Tests/WindowsReleaseSourceTests.cs` uses **real hidden system
RichEdit controls** with actual product `InstallSource`, `ReadSourceCandidate`,
acknowledgment and detached TOM decoration. Reflection attaches process-owned
handles and invokes internal handlers; it does not mock text/native attributes.
A hidden STATIC parent deliberately has no product window notification loop,
so the test explicitly invokes `OnTextChanged` and the candidate message.
This is not a claim of end-to-end visible-window input.

Nine cases passed on local Windows x64:

- Complete and incomplete leading RTF-like source stays literal.
- Chinese, emoji, empty source and LF/CRLF round-trip through the exact source
  projection; native collapsed end selections map back to canonical endpoints.
- Plain, CRLF and LF append cases suppress stale pre-admission observations,
  then publish the accepted version and exact actual native selection.
- Actual detached TOM foreground readback confirms semantic color and neutral
  revocation; source, selection, immutable engine snapshot and disabled native
  history remain unchanged.

Final focused command:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-restore `
  --filter FullyQualifiedName~WindowsReleaseSourceTests `
  --logger "trx;LogFileName=windows-release-source.trx" `
  --results-directory .temp/windows-release-source
```

Result: **9/9 passed**, 79 ms test duration; build output had no warnings/errors.
TRX remains under `.temp/windows-release-source/`. Immediately before the final
TOM case was added, the combined new source/portable Windows source/native
theme-override selection passed **29/29**, 131 ms; that earlier selection
contained eight new cases, nineteen portable contracts and two theme cases.
Do not add these overlapping counts into an artificial total.

Earlier compile attempts were blocked by a concurrent controller fake-event
declaration and had transient local test overload/property-name errors, all
resolved before this passing evidence. Initial unrelated Mac probe platform
warnings were reported to their owner and are absent from the final build.
No global focus, input source, clipboard, font, registry or user settings were
changed. Tests return without native work on non-Windows hosts; these are not
macOS-native passes.

## Remaining release integration

The coordinator owns hosted AOT publishing and the actual default-profile
ordinary workflow: real window open/edit, canonical Undo/Redo, Find/GoTo,
Save and a fresh GUI process reopening exact bytes. Those must run against the
release executable and all advertised architectures before release claims.
Hidden controls do not establish natural Microsoft Pinyin composition,
screen-reader quality, visible physical presentation or latency distributions.
The existing correctness and capacity tests remain; no numerical startup,
typing or memory hypothesis was silently promoted to a release gate.

## Hosted Find failure: external observer transport, not a product Find defect

The first hosted release candidate `9fecef2`, run `36921665382`, failed the
Windows x64 and ARM64 external Markdown task at its Find-selection assertion.
Retained evidence is under
`.cache/release-ci-36921665382/evidence/release-evidence-win-x64/product/markdown/`
(and the corresponding ARM64 directory). The initial report contained only
`stage=find`; its trace proved successful initial exact import/readback and
decoration but did not distinguish prompt acceptance from search selection.
No production Find/selection/style change was made on that evidence alone.

The coordinator then reproduced the actual managed product window locally,
using the same marker task and process-scoped external native messages:

- `.temp/windows-release-find-evidence-1/` added bounded expected/observed
  selection and prompt-control witnesses. It retained the assertion failure;
  it was not successful product qualification.
- `.temp/windows-release-find-evidence-2/windows-product.json` independently
  compared two selection transports on the **same actual native control**.
  Expected range was **[30,51)**; pointer-based `EM_EXGETSEL` reported **[0,0)**,
  while pointer-free packed `EM_GETSEL` returned **[30,51)**. The prompt edit
  existed, text-setting succeeded and actual prompt text matched the query.
  This discriminatory observation establishes an observer failure rather than
  a failed product Find selection. Probe 2 deliberately retained the failing
  old assertion; do not relabel it a passing complete workflow.

Root cause: the PowerShell observer passed its own process's `CHARRANGE` pointer
to the child's `EM_EXGETSEL` (`WM_USER + 52`). Windows does not automatically
marshal messages at or above `WM_USER` across process boundaries; this was not
a valid cross-process buffer-transfer contract. Microsoft documents both the
[SendMessage marshalling boundary](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendmessagew)
and the [EM_EXGETSEL output-structure parameter](https://learn.microsoft.com/en-us/windows/win32/controls/em-exgetsel).
The product's own same-process RichEdit calls and the hidden-control tests above
do not cross that boundary and are unaffected.

The release coordinator owns the harness correction: use pointer-free
`EM_GETSEL` with explicit ordinary-fixture bounds and reject its `-1` overflow
result, rather than treating packed 16-bit positions as arbitrary-size support.
Microsoft specifies the
[EM_GETSEL packed return and 65,535 endpoint limit](https://learn.microsoft.com/en-us/windows/win32/controls/em-getsel).
The corrected harness must still run the complete edit/history/dirty-close/
Save/fresh-process-reopen task and later hosted AOT qualification. This finding
does not authorize weakening the exact selection assertion, extrapolating
managed local success to published AOT, or discarding the original failed run.

## Corrected ordinary workflows and prompt readiness

The corrected transport ran the complete Markdown task successfully in
`.temp/windows-release-find-corrected-1/`, including exact native Find, selected
Unicode replacement, canonical/menu and native Undo/Redo, dirty-close Cancel,
Go to Line, Save, protected original, fresh GUI process reopen and native PNG.
The executable was the local **managed apphost**, not a Native AOT release.

The first six-format local run is retained in
`.temp/windows-release-corrected-suite-1/`. Markdown and TOML passed; JSON
stopped at a newly explicit prompt-input acknowledgment. Its fixed evidence
showed `prompt_control_present=false`, `prompt_text_set=false`, and
`prompt_text_matches=false`. A top-level HWND is enumerable during `WM_CREATE`,
before its Edit and accept-button children exist. The original observer assumed
the top-level HWND alone certified input readiness. It could send its answer to
a zero child handle; this is another proven observer precondition failure.

The harness now waits for the actual edit `301` and accept button `302` before
one answer is sent, verifies the actual prompt value, and only then accepts.
This uses the unchanged 15-second condition watchdog: no repeated edit attempts,
retry-success policy, oracle relaxation or increased timeout was added.

The existing suite command then completed all six formats:

```powershell
pwsh -NoProfile -File tests/NativeReleaseProductWorkflow.ps1 `
  -ExecutablePath src/Mote.Native/bin/Release/net10.0/mote.exe `
  -OutputDirectory .temp/windows-release-corrected-suite-2 `
  -RuntimeIdentifier win-x64
```

Result: **passed**, six protected inputs, six independently exact saved outputs,
six fresh GUI-process reopens, six native window PNGs, strict opt-in trace/Save
checks, five CLI cases and three configuration cases. The local managed apphost
SHA-256 was `BF251AA2AF45B5906F7667BE22D42CE4290556CABD2A61512BB0C414B9661943`;
its managed assemblies/runtime remain dependencies. This identifies the local
observer validation, not an AOT executable or installation-independence claim.

All six task traces contain successful `analysis.published` at edited version 5.
The JSON image displays the edited value in its structured tree. The CSV image
has a Complete version-5 status but pending placeholders in two visible rows at
the immediate post-Save capture. That may reflect asynchronously resolving
visible cells; the Save oracle alone does **not** certify complete rendered-cell
readiness or every semantic feature. The release coordinator must inspect actual
hosted current-version publications/captures rather than promote these byte
checks into universal semantic/UX proof. Original failures and the screenshot
boundary are retained; no production code was changed for either observer fix.

## Ordinary default and final current semantic view

After source `f2b2de7` passed all four explicit NativeSource AOT release-package
jobs in run `36925090282`, the coordinator authorized a separately qualified
ordinary default. Both Windows edit and fresh reopen now launch only the path;
`--native-source` remains an explicit equivalent, and `--continuous` retains the
old product for historical probes. Initial Find acknowledgment is stored as
`find_expected`/`find_observed`; `last_observed_selection` separately describes
later Go to Line, avoiding an apparent mismatch between unrelated stages.

The final-view condition derives the version from the **actual successful
`document.save` trace**, not a hardcoded command-count prediction. It requires
same-version successful `analysis.parse`, `analysis.published` and
`native.source.style_publish`, plus the actual native status naming the current
policy and saved version. Markdown/TOML/JSON/YAML also require the actual rendered
preview to contain the independently expected edited value. Plain text retains
its source-only convention. This is an observation condition, not repeated
editing or a larger watchdog.

CSV adds exact native owner-data cell labels. `LVM_GETITEMW` is correctly
custom-marshaled: only the started child PID and its descendant table are
admitted; naturally aligned 64-bit `LVITEMW` and 512-byte UTF-16 text buffers are
allocated in that process with VM-only access; structures are written and read
with exact byte counts, and local/remote allocations and the process handle are
released in `finally`. No client-local pointer is sent across processes. The
observer bounds rows, columns and text rather than claiming arbitrary capacity.
An independent .NET `TextFieldParser` parses the expected output; all nine
actual callback labels must equal the three-record/three-field fixture.

The complete integrated local command was:

```powershell
pwsh -NoProfile -File tests/NativeReleaseProductWorkflow.ps1 `
  -ExecutablePath src/Mote.Native/bin/Release/net10.0/mote.exe `
  -OutputDirectory .temp/windows-release-default-semantic-suite-1 `
  -RuntimeIdentifier win-x64
```

Result: **all six bare-default tasks passed**, with independent exact bytes,
protected originals, fresh GUI reopens, version-linked final-view sidecars,
current native PNGs, five CLI cases and three configuration cases. The CSV
current image has three actual resolved rows, replacing the former persistent
pending placeholders; its native callback labels all match the independent
expected values. The product correction is documented separately in
[CSV readiness](release-product-csv-readiness.md), including its failed actual
terminal-state reproduction and serialized-driver cancellation safety.

This was a **managed** local integration run. Apphost SHA-256:
`C9D2981190D22AE435ADB00FAD70991C188D085BA33432D7923FEAFE30AEE0F9`;
managed `mote.dll` SHA-256:
`8138E011E099642D38A778440D0FB2A33DB723D1B1492910B430B3B9BD8A5DE5`.
Retained `release-acceptance.json` and per-format `native-product-semantics.json`
identify its results; this is not a new AOT binary qualification. The ten-field
Windows sidecar distinguishes native status/preview/callback and trace evidence
from the richer in-process AppKit identity/coverage witness. Neither asserts
physical pixels, real Pinyin, readers or full format conformance. The final bare
default and semantic-capture changes still require four-RID hosted validation.

## Save target-read ordering: Windows ARM64 retained failure

Hosted run `36931198293` passed both corrected macOS AOT full task suites and
Windows x64, but Windows ARM64 CSV failed at the observer's `save` stage. Retained
evidence is under
`.cache/release-ci-36931198293/evidence/release-evidence-win-arm64/product/csv`.
`workflow.stderr.txt` records a sharing violation from the observer's
`ReadAllText(edited.csv)`, not a failed Save result. The target already exists
because the task copies its protected original before launching the editor;
existence cannot certify replacement completion.

The trace shows `save.commit_replace.entered` at `21:52:59.6466635`, successful
replacement at `21:52:59.6831222` (36,461 microseconds), and successful
`document.save` version 5 at `21:52:59.6832008`. It has `save.ui_post_returned`,
but no successful `save.completed` or terminal `command.save`: the observer
threw and its cleanup terminated the owned process before UI command completion.
The evidence does not establish a product Save failure. It establishes that
the prior observation attempted a file read while Save could still hold its
target guard. Neither the failed run nor its original error is overwritten.

The corrected observer sends Save once, observes clean native modified chrome,
then requires successful `document.save`, `save.completed` and `command.save`
with the **same actual version and trace session**. Only after this condition
does it read the saved target **once** and compare its full text exactly.
Final semantic observation retains the same version-linked parse, publication,
style and actual native view requirements. No sharing-exception catch/retry,
repeated Save/edit, watchdog increase or byte-oracle relaxation was introduced.
The task report records `save_completed_version`, `save_completion_session_id`
and `exact_saved_read_after_completion` separately from final view evidence.

`tests/Test-NativeWindowsReleaseSaveObserver.ps1` extracts the production
observer function through its PowerShell AST and exercises an owned fixture
with a deterministic `FileShare.None` guard. It reproduces the old sharing
exception, refuses any read after engine persistence alone, rejects a
mismatched-version command completion, then admits exactly one exact read after
the guard is released and matching Save completion is present. Its result is
retained at
`.temp/release-save-observer/508c1ae678df47a38d7591cceb8ea5f8/observer-result.json`:
passed, zero reads before completion, one read afterwards, derived version 7.
This controlled observer contract is not a GUI or ARM64 runtime test.

The affected ordinary GUI integration command then passed all six formats:

```powershell
pwsh -NoProfile -File tests/NativeReleaseProductWorkflow.ps1 `
  -ExecutablePath src/Mote.Native/bin/Release/net10.0/mote.exe `
  -OutputDirectory .temp/windows-release-completed-save-suite-1 `
  -RuntimeIdentifier win-x64
```

The retained `release-acceptance.json` reports six exact-byte outputs, protected
originals, fresh GUI-process reopens and current final semantic views, plus five
CLI and three configuration cases. CSV records completed Save version 5,
`exact_saved_read_after_completion=true`, and nine independently exact native
cell labels. This was a **managed Windows x64 local integration** run, not a
new Native AOT or Windows ARM64 qualification. Apphost SHA-256:
`61B3D65E4BCFAD004176FBBC491839826519366613CAD8221357703D4476A0DA`;
managed `mote.dll` SHA-256:
`E78584A8B4B1C0C8384026693C7388F47390DF8E0D3F88A6D9B69E40174B5ED6`.
The hosted aggregate remains failed pending a new coordinated four-RID run;
successful local evidence does not retroactively qualify the failed ARM64 task.
