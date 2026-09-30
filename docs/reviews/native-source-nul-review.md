# Native source NUL safety: independent failure reproduction

## Verdict

**Original data-loss defect confirmed; the subsequent bounded read-only Windows
repair passes the focused safety regressions below.** The original failure was not
only a Grid cell-label truncation. Both established Windows RichEdit source
editing and the Continuous RichEdit input island lose canonical unedited text
after U+0000 on an ordinary prefix insertion. Original evidence is retained below;
the final section reports corrected behavior without claiming editable NUL support.

## Contract and method

The engine owns the complete canonical source. A native projection may normalize
display spelling, but a user inserting `X` at source offset zero must preserve
every other source code unit. The expected result of editing `a\0b` is `Xa\0b`.
An inert reversible display marker is acceptable; silently deleting the suffix
is not.

New isolated tests: `tests/Mote.Tests/NativeCsvGridSourceNulTests.cs`.
No production code edits, Save operations, clipboard changes, global keyboard
input, foreground windows, persistent settings, or external-directory writes
were used. HWND parents remain hidden. The source import probe reads RichEdit
with `EM_GETTEXTEX`, using its returned character count in
`Marshal.PtrToStringUni(buffer, count)`, **not** a NUL-terminated managed read.
Thus the observed truncation happens during native import rather than merely
in the test's string conversion.

The established-path integration wires a real `WindowsEditorShell` to the real
`NativeEditorController`, uses the controller's existing document replacement
method, inserts `X` with synchronous `EM_REPLACESEL`, and delivers the shell's
normal `OnTextChanged` handler. Reflection only avoids a visible application
message loop/file picker. The Continuous probe creates a real hidden
`WindowsRichEditIsland`, binds an exact immutable source interval, performs the
same insertion, invokes its normal final-text commit handler, and applies the
emitted source change to the engine Document. It is an actual island callback
reproduction, not a full Continuous controller/window acceptance test.

## Observed results

| Check | Expected | Observed |
| --- | --- | --- |
| Source native import `a\0b` | Entire projected source | `a` |
| Source native import `"a\0b",tail\r\n` | Entire projected CSV source | `"a` |
| Established source prefix insertion | `Xa\0b` | `Xa` in real controller Document |
| Established CSV prefix insertion | `X"a\0b",tail\r\n` | `X"a` in real controller Document |
| Continuous island prefix insertion | `Xa\0b` | `Xa` after applying emitted change |

Focused test run: **5 failures / 5 executed**. No harness or environment failure
occurred in that run. An initial harness compile error (missing test-only
FreeLibrary declaration) was corrected without changing the source contract.

Command from repository root:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --filter FullyQualifiedName~NativeCsvGridSourceNulTests --logger 'console;verbosity=normal' --no-restore
```

Raw output retained at `.temp/source-nul-probe.log`. Runtime: Windows x64,
.NET SDK 10.0.400, .NET 10.0.11 VSTest process, Windows NT 10.0.26200.0;
HEAD `a17ad93aee6311f127570d8b6e154a67519cef09`;
the active source tree includes uncommitted Grid
integration work. The tests are intentionally contract regressions, not assertions
that bless the bad behavior.

## Mechanism and repair boundary

`NativeTextProjection` retains U+0000. Both native source import paths call
`EM_SETTEXTEX` with a NUL-terminated Unicode string. RichEdit sees only the
prefix. The shell/island retains the longer intended projection as its diff
baseline. Its next native callback compares a truncated native string against
that baseline and emits a source deletion covering the NUL and suffix.

A repair needs a safe source/display contract shared by native editing paths:
for example, an offset-preserving visible marker for source NUL with contextual
inverse mapping that does not conflate literal marker text with source NUL.
Native installation should fail closed or verify the installed representation
before treating it as an authoritative editing baseline. A Grid label-only fix
does not address these failures. macOS behavior and physical input/IME behavior
were not exercised and are not inferred from this Windows result.

## Bounded read-only repair validation (2026-10-01)

The owner implemented one-code-unit U+2400 display for source NUL, native
read-only state, callback refusal, exact native import/readback certification,
and restoration after injected programmatic edits. The canonical engine source
is never decoded from a display marker. Literal U+2400 remains literal.

Original insertion-preservation cases were changed only after this explicit
safety contract landed: NUL-bearing host edits are refused, so the expected
canonical source is unchanged. This is not weakening the no-data-loss contract.

Results:

- **18 ordinary/adversarial host cases passed** in the expanded run. They cover
  complete explicit-length native readback; plain and quoted CSV NUL; real NUL
  adjacent to literal U+2400 in both orders; EOF NUL; prefix/glyph insertion;
  whole-source deletion; replacement across the marker/suffix; EOF insertion;
  WM_CHAR, Delete, native EM_UNDO and EM_REDO; unchanged real established
  controller Document/version; no Continuous island edit callback; native
  marker display restored after injected edits; pending-host commit success.
- Literal U+2400 without NUL is editable, without being converted to NUL.
- Same-native-display NUL-to-literal transition in the established controller
  and Continuous rebind both resume editing and preserve literal marker spelling.
- Exact UTF-8 CSV bytes survive refused native edits, CommitPendingText, engine
  Save, and engine reopen. The fixture includes adjacent NUL/literal marker and
  EOF NUL. It lives in `.temp/source-nul-probe/<unique-id>/` and its file/unique
  directory are removed afterward. Native/controller Save event dispatch itself
  is not exercised (no message-loop/file-picker harness).
- **Three guard/failure cases passed under Release `-warnaserror`** after review
  found an uncertified-map equality fast-path gap. The regression originally
  failed; the owner now recertifies exact import before enabling that host. An
  invalid native handle forces failed readback, confirming source import locks
  the host and clears certification until successful reimport. Continuous
  failed import clears binding authority and refuses final commit callbacks.
  This is unequal-readback fault injection, not a separately injected thrown
  reader exception or an out-of-memory test.

There are **22 distinct passing cases** in the final test file. The narrow
guard-only run has 3/3 passes, no build errors/warnings; the 18 accepted ordinary
cases were not rerun solely to obtain a fresh total. Earlier runs expanded
coverage as findings appeared; one harness CA1416 lambda warning and a targeted
xUnit blocking-I/O warning were resolved without weakening assertions.

Commands from repository root:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --filter FullyQualifiedName~NativeCsvGridSourceNulTests --no-restore --logger 'console;verbosity=minimal' -warnaserror
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --filter 'FullyQualifiedName~Identical_display_does_not_reenable_uncertified_source_map|FullyQualifiedName~Failed_source_import_is_readonly_until_successful_reimport|FullyQualifiedName~Failed_continuous_import_clears_binding_and_refuses_commit' --no-restore --logger 'console;verbosity=minimal' -warnaserror
```

Outputs: `.temp/source-nul-safe-probe.log`, `.temp/source-nul-guard-probe.log`.
Environment remains Windows x64, SDK 10.0.400 / runtime 10.0.11. No other
completed Grid suites were rerun. The Save fixture uses a narrowly documented
analyzer suppression to keep hidden HWND creation, callbacks, and destruction
on one thread; engine I/O does not marshal to that thread.

Native EM_UNDO/EM_REDO inertness is distinct from safe engine-level history
commands, which need not be disabled. This validates bounded Windows source
host safety, not physical pointer input, real IME, complete Continuous window
acceptance, macOS behavior, or newly editable NUL handling.

### Late failed-import notification refinement

The owner subsequently restricted read-only restoration to already certified
maps. One new focused real-controller regression first installs `old source`,
forces the replacement `new source` import to fail via an invalid native handle,
then restores the owned handle and invokes the normal late text notification.
The map stays uncertified/read-only; canonical `new source` and version zero
remain exact. Explicit controller ShowDocument recertifies the new page and a
subsequent insertion safely yields `Xnew source`. Thus an old cached display
cannot be mistaken for the failed new source map.

Only this newly affected test was executed: **1/1 passed**, Release
`-warnaserror`, no warnings/errors. The prior 21 accepted cases were not rerun.
Output `.temp/source-nul-late-notification.log`:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --filter FullyQualifiedName~Failed_new_import_notification_does_not_recertify_old_page --no-restore --logger 'console;verbosity=minimal' -warnaserror
```

## Grid-independent source Copy/Cut guard

`tests/Mote.Tests/NativeSourceClipboardNulTests.cs` adds six independent pure
controller cases: source NUL at the beginning, middle or EOF, each with Copy and
Cut after source Select All. The established FakeShell only holds an in-memory
clipboard; no Windows host read-only/Cut gate or Grid API is involved. This
exercises the controller's prepublication NUL guard rather than relying on shell
mutation refusal.

The in-memory clipboard is seeded with `sentinel`, then configured to throw if
SetClipboardText is reached. Each operation produces the exact U+0000 refusal
diagnostic, not the different clipboard-publication exception diagnostic. That
trap establishes that publication was not called, without depending solely on
sentinel retention. After root-authorized exclusive ownership of the narrow
FakeShell helper hunk in `tests/Mote.Tests/NativeControllerTests.cs`, its
documented `ClipboardSetCalls` counter increments before either success or
rejection. All six cases directly assert no counter change from the seeded
sentinel baseline. Only those six new cases were rerun once after adding the
counter assertion; they again passed Release `-warnaserror` clean.
Sentinel, full document text, document stamp/version zero,
unmodified state, total length, and original disk source are unchanged. Cut does
not delete the selection.

Focused Release `-warnaserror`: **6/6 passed**, no warnings/errors; output
`.temp/source-clipboard-nul-probe.log`:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --filter FullyQualifiedName~Source_clipboard_nul_guard_refuses_before_publication --no-restore --logger 'console;verbosity=minimal' -warnaserror
```

No actual clipboard/global input is touched, and prior 22 host regressions were
not repeated. This adds six distinct accepted contracts to those 22 host cases.
