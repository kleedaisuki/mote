# Native macOS CSV Grid clipboard acceptance

Date: 2026-10-01. Implemented probe: `src/Mote.Native/Mac/MacCsvGridClipboardProbe.cs`.
The frozen probe and exact invocation have independent static safety approval,
recorded in `../reviews/native-mac-grid-clipboard-safety.md`. Actual mutation is
permitted only through the reviewed dedicated disposable GitHub-hosted workflow;
target acceptance remains unverified until that workflow runs successfully.

## Objective and scope

The existing AppKit Grid acceptance proves ready native table descriptors and command
identity but deliberately never writes NSPasteboard. This complementary probe exercises
the actual production `NativeEditorController`, a hidden production `MacCsvGrid`
NSTableView, source-backed background Copy preparation/freshness checks, and
`MacEditorShell.SetClipboardText`. It does not fabricate prepared command results.

The dispatcher/source shell is a diagnostic adapter, not a full running desktop shell.
`NSApplication.sharedApplication` and an autorelease pool are initialized on the main
thread. No window is created, ordered front, activated or focused. Native table cell
creation is observed through `viewAtColumn:row:makeIfNecessary:`. Rectangle selection
uses production Shift-arrow handling followed by `selectRowIndexes:byExtendingSelection:`;
the emitted intent's exact coordinates are asserted. Single-cell Copy uses the production
`CopySelection` method; explicit CSV/padded choices use the table's `Emit` method.

The root-owned Program registration is an early diagnostic route, before loading
normal application configuration or telemetry:

```csharp
if (args.Length == 2 && args[0] == "--check-native-mac-grid-clipboard")
{
    if (!OperatingSystem.IsMacOS()) return 3;
    if (args[1] is not ("fake" or "actual")) return 2;
    return Mac.MacCsvGridClipboardProbe.Run(args[1] == "actual");
}
```

Do not wire an arbitrary flag or unknown argument to actual mode. `Run()` defaults to
fake; the real publisher/readback/change-count paths are reachable only with `actual=true`.

## Discriminating checks

Each fixture is an actual fresh CSV file, opened by the normal controller. The probe waits
for matching source/analysis stamps **and an exact proved row count** before selection.
This matters for the over-cap file: idle Full can legitimately replace the first visible
presentation and retire its queued Copy. No freshness guard is disabled to avoid that race.

| Case ID | Literal independent expected result |
| --- | --- |
| `quoted-crlf` | Decoded `a\r\nb\t"c`, not sanitized table display |
| `empty-final-row` | `a\r\n""`, not invented trailing record separator |
| `missing-refusal` | Ragged Missing selected as ordinary CSV; observed rejection, unchanged sentinel |
| `explicit-missing-padding` | Explicit padded choice returns `a,b\r\nx,""` |
| `nul-refusal` | Embedded U+0000 refused before any publication, unchanged sentinel |
| `over-cap-refusal` | Selected payload one unit beyond the production cap refused, unchanged sentinel |

The sentinel includes a surrogate pair and exact CRLF. Positive results are read directly
with `NSPasteboard.stringForType:`, then a bounded NSString UTF-16 `length` and
`getCharacters:range:` copy. No newline conversion or null-terminated UTF-8 readback is used.
Negative cases require delivered controller error, unchanged successful publisher-call
count, exact sentinel readback **and unchanged NSPasteboard changeCount**. A quiet delay
alone is never acceptance evidence.

The new, internal, probe-only controller partial accessor returns an immutable audit of
the existing canonical snapshot, modified state, Undo/Redo availability and selection.
It uses direct private-field access, not reflection or a public API, and never mutates.
Fixture selection goes through normal `SelectionChanged(1,2)`; this proves controller
source-selection semantics, not a real NSTextView source control. Every case checks:

- nonempty canonical selection before Copy;
- identical snapshot identity and version before/after Copy;
- exact canonical source and exact original disk text;
- unchanged modified flag, CanUndo/CanRedo, anchor and active endpoint;
- normal Undo request adds no transaction and leaves all audit state identical.

No physical keyboard or pointer, context-menu hit testing, accessibility (AX), input
method editor (IME), source NSTextView editing, compositor paint or clipboard contention
recovery is tested. Published Native AOT execution becomes evidence only after that
actual binary is run on its target. A Windows build alone is not macOS acceptance.

## Safety contract

**Do not run actual mode on a development Mac or self-hosted runner. Never spoof gates.**
Environment/marker checks are deliberate operator consent checks, not an authenticated
security sandbox. The disposable runner loses its previous clipboard contents; the probe
does not read/save/restore them or claim transactional rollback after OS publication fails.

Before AppKit initialization or any possible clipboard call, actual mode requires:

1. macOS OS check;
2. `MOTE_DISPOSABLE_MAC_GRID_CLIPBOARD=1`;
3. `GITHUB_ACTIONS=true` and `RUNNER_ENVIRONMENT=github-hosted`;
4. repository current directory, nonsymlink/reparse ancestry and exact `GITHUB_WORKSPACE`;
5. nonempty `GITHUB_RUN_ID`, `GITHUB_RUN_ATTEMPT`, `GITHUB_SHA`;
6. preexisting ordinary `.cache/native-mac-grid-clipboard/approval.txt` containing exactly
   `disposable-github-hosted-macos-only` (no newline);
7. ordinary `approval-run.txt` containing exactly `<run>:<attempt>:<commit>` (no newline);
8. no prior `report.json` or report symlink, and ordinary probe source path/ancestry.

The probe never creates approval markers. All files, synthetic fixtures and isolated
configuration roots are below `.cache/native-mac-grid-clipboard`; existing scratch
ancestry is checked before creation. GUID fixture directories are fresh and left as
reproducibility artifacts. No recursive deletion occurs. No normal user configuration,
input source, TCC database, preferences, registry, network or global input hook is touched.

The actual route deliberately reaches **generalPasteboard**, not a uniquely named test
pasteboard. This is essential to exercise the unchanged production publisher. Per Apple,
the general pasteboard automatically participates in Universal Clipboard, with no macOS
API for controlling that feature. A fresh GitHub-hosted runner with no personal Apple
account/paired device is therefore a required invocation assumption; this is not safe
permission for a personal logged-in Mac. [Apple NSPasteboard documentation](https://developer.apple.com/documentation/appkit/nspasteboard/)

Fake mode constructs the identical hidden table and controller but `GridShell.SetClipboardText`
only stores managed text, and the independent native readback/change-count methods are
never called. It does not even resolve `NSPasteboard`. Fake execution still creates synthetic
repository-local files and AppKit views; it is not an entirely side-effect-free route.

## Required root-owned invocation integration

Use a dedicated fresh GitHub-hosted Mac job, separately from any existing canvas clipboard
writer. Do not run concurrent clipboard writers within the same runner. Give it an external
job timeout of at least three and no more than five minutes. The internal failure watchdog
exits 124 after 90 seconds; each completion wait has a 12-second bound. External timeout
is still required for process/native/runner failure.

The materialized invocation and final Program route have been independently
reviewed. Before creating approval markers or passing opt-in to a subprocess it must:

- reject local/self-hosted execution and inherited mutation opt-in;
- verify exact checkout root, `HEAD == GITHUB_SHA`, tracked reviewed artifacts and clean
  source/test checkout (including nonignored untracked files);
- pin the independently reviewed raw probe and invocation source hashes, admitting only
  reviewed LF/CRLF representations, not hashes calculated from arbitrary current content;
- build/publish the exact current checkout with warnings as errors, without restoring a
  cached/downloaded binary; build while mutation opt-in is absent;
- clear only the exact old report/approval/stdout/stderr artifact filenames beneath the
  checked `.cache` directory, with symlink/ancestry checks and no recursive deletion;
- create current run-bound approval files, then pass the opt-in only to the exact
  `--check-native-mac-grid-clipboard actual` subprocess;
- capture process exit code, stdout, stderr and fresh report even on failure;
- in `finally`, remove opt-in and both exact approval markers.

Success requires zero exit code and the exact marker:

`mote-native-mac-grid-clipboard-ready mode=actual; cases=6; desktop-input=not-tested`

It also requires fresh `report.json`: `status=passed`, `nativeClipboard=true`, exact current
run key and admitted raw source hash, UTC newer than invocation start, and exactly the six
case identifiers listed above. A missing/opt-out route, stale report, zero tests or only
an exit-code check is not evidence. Upload the JSON/stdout/stderr on failure too. Final
coverage must remain scoped to hidden table + real controller + production NSPasteboard.

### Frozen reviewed probe bytes

| Source representation | SHA256 |
| --- | --- |
| LF | `EF9BFB12E55B0EDFC9ED5FC74A1BC0BE4793055AEAAFBCD05CC066263DD11867` |
| CRLF | `85D42F1DFEBBB5176F7CC735008151C0D1E3C9716B54A5177ADC8F4764B4AD34` |

Only replacement of all LF line separators by CRLF produced the alternate hash; no BOM,
mixed ending, whitespace, Git clean filter or code change is admitted. These hashes identify
the reviewed probe. They are admitted only through the separately approved
invocation, not permission for a direct actual-binary call. Any later executable
change requires delta safety review and updated fixed invocation constants.

## Local evidence and remaining acceptance

Windows host, .NET 10, 2026-10-01:

`dotnet build src/Mote.Native/Mote.Native.csproj -c Release -p:TreatWarningsAsErrors=true --no-restore`

Successful build, zero warnings/errors, including enabled AOT/trim analyzers. No actual Mac
execution, NSPasteboard read/write, source-input injection, safety-approved invocation or
target clipboard acceptance has been performed. The early Program route and
dedicated hosted invocation are now integrated in the working tree and statically
reviewed; they are not target evidence. Both x64/ARM64 Native AOT actual hosted-target
execution and fresh invocation reports remain required.

## Platform contracts consulted

- Apple documents `clearContents` as clearing old contents and the convenient write APIs
  as writing the first item; publication therefore destroys the old runner clipboard and
  is not an atomic old-content-preserving operation.
  [NSPasteboard](https://developer.apple.com/documentation/appkit/nspasteboard/)
- `setString:forType:` returns false on lost ownership; other failures can raise a native
  communication exception. The production publisher's failure is not converted into a
  passed test. Native exceptions/process crashes may leave a `running` report, which must
  fail outer invocation freshness/status verification.
  [setString:forType:](https://developer.apple.com/documentation/appkit/nspasteboard/setstring(_:fortype:)?language=objc)

This bounded platform acceptance does not need a new academic parsing method. It reuses
the already-reviewed source-backed CSV semantics and production architecture rather than
changing grammar or inserting a test-only publisher. The decisive next evidence is target
execution, not additional speculative implementation.
