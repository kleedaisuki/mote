# Windows ordinary Canvas theme probe: independent review

## Scope and evidence

Reviewed the unpublished `tests/NativeWindowsCanvasThemeWorkflow.ps1` and its
portable contract test, initially at SHA-256
`C5E15186D708C9ADC191177D0722C49C57DAC72DAFCBCAB11E92250345E91A68`.
Read existing theme/native file-workflow decisions first. Inspected the actual
`WindowsRichEditIsland.AttachAccessibility` and `WM_GETOBJECT` dispatch: the
Canvas HWND owns the source-backed provider; child 301 is only the bounded
native input island. No production changes or OS preference mutation were
performed for this review. Hosted target execution remains pending.

## Initial material findings (owner notified before CI integration)

1. **Cleanup could bypass registry restoration.** In the original `finally`,
   fixture hashing and process-environment restoration precede the registry
   restoration block without an enclosing protection. An I/O/access failure
   hashing the fixture (for example, the target retaining a conflicting handle)
   throws out of `finally` before HKCU restoration. Independent cleanup phases
   must record their own errors and never suppress the registry restoration
   attempt. Confidence: high, directly traced executable exception path.
2. **Close failure could leave the launched target alive.** The original process
   cleanup performs owner assertion and bounded WM_CLOSE in one try. If either
   fails, its catch records an error but does not terminate the stored launched
   process. The subsequent kill runs only when WM_CLOSE succeeds and its exit
   wait expires. Termination fallback must operate independently on the exact
   process object and wait/dispose even when no target HWND was ever found.
   Confidence: high.
3. **Loop deadline did not bound synchronous native/client calls.** PrintWindow
   and UIA run in the same script process that owns mutated HKCU. A hung call
   prevents loop timeout checks and restoration; terminating that script at a
   workflow deadline does not execute its `finally`. Use a separate bounded GUI
   worker while the supervising parent owns registry snapshot/restoration, or
   equivalent independent restoration ownership. A workflow timeout alone is
   not a restoration guarantee. Confidence: high for the structural weakness;
   no hosted hang reproduced.
4. **Executable trust boundary was incomplete.** The supplied executable was
   normalized and checked for leaf existence but not confined to the repository
   Native publish RID nor checked for reparse ancestors. The script's hosted
   guard is not an executable-origin check. Constrain the exact publish target
   before starting it, and keep RID/provenance explicit. Confidence: high.

These findings concern a disposable-runner validation harness, not demonstrated
editor production defects. They were sent to the owner and coordinator promptly.
## Revised owner/worker assessment

The revised owner retains HKCU snapshot/mutation/restoration and the exact
launched editor `Process`; `NativeWindowsCanvasThemeWorker.ps1` owns all UIA,
GetDC/GetPixel and PrintWindow operations. Each worker has a parent-owned 30 s
deadline. Worker exit/report failure remains a failed diagnostic, not success.

All four initial findings are resolved by the revised control flow:

- `Invoke-RestoredThemeSession` nests registry restoration outside the cleanup
  `finally`. Fixture hashing and final report writing occur only after a restore
  attempt; an outer restore retry precedes those fallible operations as well.
- Editor shutdown catches target-HWND close failure, then independently performs
  exact-Process wait/kill fallback; worker disposal cannot suppress editor cleanup
  or restoration because each phase uses a nested `finally`.
- Synchronous GUI calls no longer execute in the restoring parent. The parent
  can terminate an unresponsive worker, clean up the editor and restore HKCU.
- The executable must equal the repository publish path for the selected RID,
  and executable/scratch/output/report ancestors reject reparse points before
  use. Editor-only ProcessStartInfo environment sets the generated MOTE_HOME;
  the parent no longer changes its own environment.

Reviewed portable tests exercise the actual extracted restoration functions with
duck-typed non-OS keys, not a second reimplementation. They demonstrate the old
linear-finally defect, four body/cleanup combinations, raw ExpandString/Binary/
MultiString/QWord fidelity and retry, absent-value preservation, created-key
removal/retry, refusal to delete unrelated data, and corrupted restore rejection:
12 positive/negative restoration cases, plus an actual sleeping-child watchdog
case using the same `Wait-ThemeWorker` function with a 150 ms deadline followed
by fake-registry restoration: 13 owner/cleanup/fidelity cases in total. Seven separate source-range
cases exercise the extracted source assertion. The owner reported a fresh
PowerShell pass; this review inspected those contracts rather than redundantly
rerunning completed validation. No actual registry/GUI call was made.

The supervision is process isolation for liveness, **not** reduced OS privileges:
worker and parent share the runner account. A hard kill of the supervisor/runner
cannot promise execution of its finally; configure the outer CI timeout with
ample cushion beyond three 30 s workers plus bounded cleanup (5 min recommended).
Retain phase JSON, PNGs and stdout/stderr as well as the owner report. Hosted
execution is still pending; this review authorizes the scoped diagnostic design,
not live theme/ARM acceptance.

Initially reviewed file hashes (before semantic-neutral whitespace cleanup):

| File | SHA-256 |
| --- | --- |
| `tests/NativeWindowsCanvasThemeWorkflow.ps1` | `846534FD67513C5A3A3270AAFED749E03A458026C69FD8C55BA60A162EE3AE64` |
| `tests/NativeWindowsCanvasThemeWorker.ps1` | `8B90381054FE458D4E290197482E1D109AB7EEB524714BB8F83DF68F7252C364` |

No substantive unresolved issue was found in those revised files within the
reviewed scope. Neither live HKCU restoration nor actual target pixel capture
was run by this reviewer.

## CI integration follow-up

Reviewed only the new region inserted after legacy theme upload in
`.github/workflows/ci.yml`; the diff does not alter existing steps. The existing
matrix has `win-x64` and `win-arm64`, and both new diagnostic/upload steps cover
those RIDs. Its five-minute outer timeout gives cushion beyond three 30 s workers
and cleanup. Upload runs on failure and retains the owner report plus the whole
RID-specific `.cache/native-canvas-theme` subtree (phase JSON, PNGs, raw worker
stdout/stderr). Non-gating conclusions remain insufficient evidence.

Found and resolved one concrete integration defect before commit: same-session
script invocation left `$LASTEXITCODE` unset while the portable script's strict
mode persisted into its caller. A fresh PowerShell strict-mode reproduction
confirmed unset-variable failure. Both scripts now run as external
`pwsh -NoProfile -File` processes, establishing exit codes and preventing fake
test types/strict-mode state from leaking into the driver.

Recomputed SHA-256 directly from Git LF bytes and their CRLF transform; all four
configured pins match the final whitespace-normalized scripts:

| File | LF SHA-256 | CRLF SHA-256 |
| --- | --- | --- |
| Owner | `EF59B864E76869096FB5254AB0961911CEF2E22C57DEC4147A77A7F7C34F0291` | `846534FD67513C5A3A3270AAFED749E03A458026C69FD8C55BA60A162EE3AE64` |
| GUI worker | `CE29B4A2985AEAC906DE05A7AD2A217C8F4F7D818A227A33477629C5B5702683` | `54CF0E489DA42884A3AD17C2B8DA0B0D836F815233F7988F21F628AE7F77ED99` |

No unresolved substantive issue in the reviewed CI insertion. Actual hosted
runtime behavior remains unverified.

## Positive contracts and interpretation limits

- Ordinary one-file launch, no legacy/experimental flag, exact launched PID HWND
  checks, target-only WM_SETTINGCHANGE, no broadcast or global keyboard input.
- Source TextPattern verifies complete synthetic LF text plus exact endpoints
  [1,3) and selected `lp`; it does not infer global coordinates from RichEdit.
- Original registry value existence/kind/data (including unexpanded strings)
  is captured, with equality verification; a newly created key is deleted only
  if no unrelated data has appeared. Repository scratch/artifact paths reject
  reparse ancestors.
- GetDC source-body pixels and target PrintWindow PNG/chrome samples are raster
  evidence, not compositor presentation or callback-timing evidence. Expected
  foreground color proximity plus a pixel count is not a measured contrast
  distribution. Engine version is explicitly unverified externally. Source-file
  SHA equality certifies only the synthetic on-disk fixture.
- Portable source-range cases and embedded C# compilation do not establish GUI,
  live registry restoration, published Native AOT, or ARM runtime acceptance.

## Primary API references

- [PrintWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-printwindow):
  documented as synchronous/blocking; the owner renders into the supplied DC.
- [SendMessageTimeoutW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendmessagetimeoutw):
  the target-specific message call has a timeout; that timeout does not bound
  PrintWindow or a surrounding UIA invocation.
- [UI Automation threading](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-threading):
  Windows-message interactions can become unresponsive; isolate client calls
  from supervisory restoration rather than assuming a polling loop interrupts
  them.
