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
