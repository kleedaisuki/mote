# Native macOS CSV Grid clipboard probe safety review

Date: 2026-10-01. Independent static review on the Windows development host.

## Decision

**The frozen probe draft is conditionally approved for integration into a dedicated,
fresh disposable GitHub-hosted macOS job. No substantive defect was found in the
reviewed probe. This is not permission to execute actual mode yet:** the final
Program route, hash-pinned invocation script, and dedicated CI job must receive a
separate integration safety review before any NSPasteboard mutation.

No actual probe, fake probe, clipboard operation, AppKit process, build, staging,
commit, or push was performed by this reviewer. No CI environment was spoofed.

## Reviewed scope and evidence

Reviewed `src/Mote.Native/Mac/MacCsvGridClipboardProbe.cs` and
`docs/validation/native-mac-csv-grid-clipboard.md`, with reachable production
`NativeEditorController.Grid.cs`, controller disposal/selection routes,
`MacCsvGrid`, `MacEditorShell.SetClipboardText`, `ObjC`, and `TextSnapshot`.
Existing Windows clipboard safety review was reused for the shared controller
contracts; macOS publication/ownership behavior was inspected independently.

| Area | Result and limitations |
| --- | --- |
| Actual admission | OS, explicit opt-in, GitHub-hosted identity, exact repository workspace, ordinary nonsymlink ancestry, exact approval/run markers, absent prior report, and ordinary source path are checked before AppKit initialization or clipboard access. Environment strings are operator consent checks, not authenticated isolation. The draft intentionally delegates fixed hash admission and checkout/build identity to the invocation. |
| Default fake route | `Run()` defaults to false. `_actual` controls the sole production publisher call and both native readback/change-count paths are conditional. `MacEditorShell` construction only initializes managed state. Hidden table construction does not resolve NSPasteboard. Fake mode still initializes AppKit and creates repository-local fixture/configuration files. |
| Thread/lifetime | AppKit main-thread status is checked; an owned autorelease pool encloses all six fixtures. Controller background completions are drained on that same thread. Per-case controller disposal precedes table disposal; production table disposal detaches delegates/data source/target, removes callback mappings, and releases owned views. No desktop window or global input injection is created. |
| Native ABI/AOT | Typed static Objective-C P/Invoke signatures use pointer-sized integer arguments, by-value CGPoint and NSRange, and byte BOOL matching the established native layer. No reflection, dynamic code generation, or JSON reflection serialization is added. Utf8JsonWriter is statically reachable. ABI/target behavior still requires actual x64 and ARM64 execution. |
| Selection and freshness | Shift-arrow handling plus native row selection captures a production rectangle; its exact emitted coordinates are asserted. Select intents only invalidate copy authority, not canonical source selection. Copy preparation and stale-presentation checks remain the production paths; exact row-count completion avoids deliberately bypassing the idle Full retirement race. |
| Six cases | Independent literal expectations cover multiline CRLF/tab/quote decoding, final empty row, ragged Missing refusal, explicit Missing padding, NUL refusal, and one-unit-over-cap refusal. Positive cases require one additional successful publisher call and no new error. Negative cases require a delivered error, no additional successful publisher call, exact sentinel, and unchanged native changeCount. A quiet timeout is not treated as success. |
| Canonical state | Record audit includes the TextSnapshot object (reference equality; TextSnapshot does not override equality), modified/Undo/Redo flags, and both navigation endpoints. Before/after identity equality therefore preserves snapshot and version; exact source/disk reads and an ordinary Undo request independently check that Copy adds no transaction. Source selection uses a diagnostic adapter, not real NSTextView editing. |
| Readback | NSString length and getCharacters:range: provide independent, bounded UTF-16 readback without null-terminated conversion or newline normalization. The sentinel includes a surrogate pair and CRLF. No preexisting clipboard content is inspected, saved, or restored. |
| Artifacts | Root/ancestor checks precede scratch creation. Fixtures/config roots/reports are beneath `.cache/native-mac-grid-clipboard`; GUID fixture children are fresh. No recursive deletion or user-home/config/input-source/TCC operation is requested. Checks are not race-proof filesystem sandboxing; fresh trusted runner ownership is required. |
| Failure handling | Managed failures return nonzero and normally write failed JSON; native exception/crash/watchdog termination may leave running JSON. Outer invocation must reject non-passed/missing/stale evidence. The 90-second watchdog does not replace an external job timeout. Publication can destroy old contents before a later OS error; rollback is not claimed. |

## Frozen bytes independently verified

The current raw file is UTF-8 LF, without BOM. Independently reading the draft,
normalizing CRLF to LF, and hashing UTF-8 bytes produced:

| Representation | SHA256 |
| --- | --- |
| LF (current raw source) | `EF9BFB12E55B0EDFC9ED5FC74A1BC0BE4793055AEAAFBCD05CC066263DD11867` |
| CRLF (all LF separators replaced) | `85D42F1DFEBBB5176F7CC735008151C0D1E3C9716B54A5177ADC8F4764B4AD34` |

These independently match the validation document. Other encodings, mixed endings,
whitespace changes, and executable edits are not covered. Final invocation must
pin these constants, not accept a hash derived from arbitrary current content.

## Conditions before actual publication

1. Review the materialized early Program route; only the exact `fake`/`actual`
   arguments may map to these routes, before ordinary config/telemetry loading.
2. Review a dedicated fresh GitHub-hosted macOS job with no concurrent clipboard
   writer in the same runner and an external three-to-five-minute timeout.
3. Review an invocation that refuses local/self-hosted and inherited opt-in,
   verifies exact clean checkout and HEAD/GITHUB_SHA plus fixed reviewed source
   and invocation hashes, and builds/publishes that checkout without opt-in.
4. Admit only ordinary repository-local scratch paths. Clear only exact known
   old evidence/approval filenames, then materialize this run's markers. Pass
   opt-in only to the specific actual subprocess and remove markers/opt-in in
   finally. Retain stdout/stderr/exit code/report on failure too.
5. Require exit zero, exact actual marker, fresh passed nativeClipboard report,
   current run key/source hash, and exactly all six case IDs. Fake-mode success,
   opt-out, stale report, and a non-gating CI step are not actual acceptance.
6. Fresh runner must have no personal Apple account or paired personal device.
   General pasteboard participates in Universal Clipboard; the probe cannot
   disable that OS behavior. Never execute actual mode on a development Mac.

## Platform evidence and privacy

Apple's current documentation states that the general pasteboard participates in
Universal Clipboard automatically and provides no macOS control API for that
feature. The production `clearContents` followed by `setString:forType:` destroys
old runner clipboard contents; setString may reject changed ownership or raise a
native communication exception. Synthetic text only reduces disclosure impact;
it does not make execution safe on a personal authenticated Mac.

- [Apple NSPasteboard](https://developer.apple.com/documentation/appkit/nspasteboard/)
- [Apple setString:forType:](https://developer.apple.com/documentation/appkit/nspasteboard/setstring(_:fortype:)?language=objc)

The JavaScript-facing pages were opened with the web tool; their authoritative
DocC JSON (`https://developer.apple.com/tutorials/data/documentation/appkit/nspasteboard.json`
and corresponding `setstring(_:fortype:).json`) was retrieved with PowerShell to
verify the Universal Clipboard and ownership/exception statements. No downloaded
file or experiment artifact was created outside the repository.

## Coverage boundary

No target behavior has been validated by this review. Approval does not cover
physical shortcuts/menu/pointer routing, NSTextView source input, IME, AX readers,
compositor presentation, clipboard contention recovery, performance percentiles,
or general product acceptance. The next useful evidence is hash-pinned published
Native AOT fake and actual execution on separate disposable x64/ARM64 Mac runners,
not further speculative clipboard implementation.
## Final integrated invocation review

Date: 2026-10-01. This section supersedes the draft-only execution restriction
above for the exact integrated route and bytes recorded here.

**Approved for the two dedicated fresh GitHub-hosted macOS matrix jobs only,
after root replaces the two PENDING manifest entries with the independently
verified invocation pins below and commits all reviewed artifacts. No substantive
static safety or correctness blocker was found in this integrated scope.**
This is bounded execution-safety approval, not evidence that either target has
passed. The current PENDING manifest still correctly refuses execution.

### Scope and independent checks

Reviewed the frozen probe, the early Program route, the complete invocation
script, detached invocation manifest, and new `native-mac-grid-clipboard` matrix
job. Reused the initial production-controller/table/publisher review rather than
repeating its validation. Independently hashed raw UTF-8 bytes, LF-normalized
bytes and their CRLF-only counterpart. Source and invocation are currently LF,
without BOM or lone CR. Program is LF; the current whole workflow has mixed line
separators, so its normalized LF hash is recorded separately below.

PowerShell 7 static parsing of the invocation produced **zero parse errors**.
An isolated regex-expression check (not script execution) confirmed that a single
valid 64-uppercase-hex approval line matches and the PENDING line does not.
No invocation, refusal route, publish, AppKit process, clipboard call, environment
spoof, staging, commit, or push was performed. Only this review file was edited.

| Approved artifact | LF SHA256 | CRLF-only SHA256 |
| --- | --- | --- |
| Probe | `EF9BFB12E55B0EDFC9ED5FC74A1BC0BE4793055AEAAFBCD05CC066263DD11867` | `85D42F1DFEBBB5176F7CC735008151C0D1E3C9716B54A5177ADC8F4764B4AD34` |
| Invocation | `A351A2C9F173E2F927030B9B82FFE4B2B7451017D9E046EB547489EE29A9058B` | `4055AE25AFE6DFFBC94E474210C2A5BFA86C063BA0935467DFFFB3CAB410B962` |

Additional integrated review identity (not runtime self-approval):

- Program raw/LF SHA256: `D725F5C3D8DD650DA4920C93D550DF9E9C2AAD3D112D120B8D42AB933F6F90BA`.
- Whole workflow raw SHA256: `D84BEDE77EDBA3AA7B763C9A3196FD5F8A8C2A8933E5CA6F42922D76763DBAC6`.
- Whole workflow normalized LF SHA256: `4F524D93CF45EBB02469FC3F26668D7F7B7B32E5B5C3A193B7FE2995927895F6`.

Root may insert these exact detached approval lines; the reviewer did not edit
the manifest or derive runtime approval from arbitrary current script content:

```text
Approved invocation LF SHA256: A351A2C9F173E2F927030B9B82FFE4B2B7451017D9E046EB547489EE29A9058B
Approved invocation CRLF SHA256: 4055AE25AFE6DFFBC94E474210C2A5BFA86C063BA0935467DFFFB3CAB410B962
```

### Integrated findings and execution contract

| Boundary | Assessment |
| --- | --- |
| Program dispatch | Exact two arguments admit only `fake` or `actual`; macOS is checked first. Route returns before normal configuration/telemetry/desktop-shell startup. Actual mode is independently gated again in the probe. Unknown arguments never become actual mode. |
| Admission before mutation | The script rejects non-Mac/local/self-hosted identity, inherited Mac or Windows clipboard opt-in, malformed CI identity, mismatched root/current directory, redirected ancestry, noncommitted reviewed artifacts, any tracked change and untracked source/test/manifest changes. Probe and detached invocation hashes are fixed independently reviewed values. Native `uname -m` must match the requested RID. No report cleanup, build or approval write precedes these checks. |
| Path and checkout | All touched scratch paths are under exact `.cache/native-mac-grid-clipboard`; lexical containment and ordinary ancestry are checked. Only six named old files are removed, never recursively. GUID-isolated build/output paths select a fresh binary. Clean committed HEAD/GITHUB_SHA admission includes the Program route and job trust boundary; the manifest is consent policy, not cryptographic isolation against a trusted checkout editor. |
| Build/inventory | Native AOT publish runs without mutation opt-in, with warnings as errors and GUID-isolated project-separated artifacts. Strict publish inventory requires exactly `mote`; `otool -L` rejects non-system imports. DebugType/StripSymbols flags match the existing strict native project/CI path. Missing/failed toolchain, inventory or imports fail before approval markers are written. |
| Process environment | `ProcessStartInfo.Environment` modifies only the admitted child environment, with `UseShellExecute=false` and exact ArgumentList values. Parent process/job environment remains unopted-in; subsequent artifact upload does not inherit permission. |
| Runner isolation | The matrix uses native x64 `macos-15-intel` and ARM64 `macos-latest` jobs, each with its own checkout and no cache restore, other clipboard probe or parallel writer in that job. Five-minute job timeout and gating invocation are explicit; `fail-fast:false` does not make either job nongating. |
| Evidence freshness | All old evidence is removed before launch. Exit zero and exactly one exact actual marker line are required. Fresh bounded ordinary JSON must be passed, nativeClipboard must be boolean true, run key and actual admitted raw source hash must match, UTC must lie within invocation bounds, and exactly the six case IDs must be present. Duplicate/missing/extra cases, stale/future reports, fake marker and nonzero/timeout exits fail. PowerShell automatic ISO-date conversion is handled without discarding fractional precision. |
| Failure/cleanup | The probe has a 90-second watchdog; subprocess has a 110-second external wait/kill bound; job covers build/native hangs. stdout/stderr are drained asynchronously, then preserved with exit code on ordinary success or failure. Nested finally removes both approval markers even if evidence writing throws. OS/job termination may bypass finally, and native crash may leave running JSON; neither is acceptance. Disposable-runner ownership remains essential. |

### Hosted-platform verification and material limits

GitHub's current official runner documentation independently confirms that these
labels select Intel and ARM64 respectively and ordinary hosted jobs receive fresh
VMs. Microsoft's artifacts-output documentation confirms project-separated
intermediate/output layout, avoiding cross-project collisions in the GUID root.
Its ProcessStartInfo documentation confirms child-specific environment mutation
and the required UseShellExecute=false contract. PowerShell's ConvertFrom-Json
documentation supports the ISO-date handling noted above.

- [GitHub hosted runner architectures and fresh VM ownership](https://docs.github.com/en/actions/reference/runners/github-hosted-runners)
- [.NET artifacts output layout](https://learn.microsoft.com/en-us/dotnet/core/sdk/artifacts-output)
- [.NET ProcessStartInfo.Environment](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.environment?view=net-10.0)
- [PowerShell ConvertFrom-Json](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.utility/convertfrom-json?view=powershell-7.5)

Retain the initial Apple Universal Clipboard warning: general pasteboard has no
probe-level suppression/rollback. These jobs must not sign into a personal Apple
account or pair a personal device; the reviewed steps do neither. Synthetic
publication does not justify running actual mode on a developer Mac. The five-minute
cold restore/AOT budget and headless AppKit behavior remain target-dependent and
must be observed in the first gating run; a timeout/failure is not permission to
weaken admission or accept absent evidence. No specific static target blocker
was demonstrated, so speculative platform objections are not raised as defects.

Approved exact commands, **PowerShell 7 inside the respective dedicated fresh
GitHub-hosted job only**:

```powershell
# macos-latest native ARM64 job
& ./tests/Invoke-NativeMacCsvGridClipboardWorkflow.ps1 -RuntimeIdentifier osx-arm64
# macos-15-intel native x64 job
& ./tests/Invoke-NativeMacCsvGridClipboardWorkflow.ps1 -RuntimeIdentifier osx-x64
```

Approval does not cover direct actual-binary invocation bypassing the script,
local/self-hosted execution, changed executable bytes, a shared runner job,
inherited permission, or prebuilt binary selection. Target result remains pending.
Physical input/menu routing, NSTextView editing, IME, external AX readers,
compositor presentation, contention recovery and latency are still unexamined.