# Windows preview access-violation investigation

## Status and scope (2026-10-01)

**Unresolved native crash; diagnostic/test changes only. No production fix is claimed.**

[CI 36800944850](https://github.com/kleedaisuki/mote/actions/runs/36800944850),
commit `99fbe39`, strict `Test / windows-latest`, aborted after reporting 365 completed
Mote.Tests cases with fatal `0xC0000005`. The managed boundary stack is:

```text
Win32.SetWindowTextW
WindowsEditorShell.ImportPreviewPayload(string, bool)
WindowsEditorShell.InstallPreview(NativeAnalysisView)
WindowsEditorShell.SetTheme(IThemePolicy)
NativeThemeOverrideWindowsTests.Same_id_override_updates_native_background_without_source_or_history_changes(bool)
```

The run reports Windows Server 2025, build 10.0.26100, hosted image
`windows-2025-vs2026` version `20260925.250.1`. The log has no native fault address,
registers or native caller stack, so it cannot distinguish an internal RichEdit
fault from earlier memory corruption. The reported `Passed!` count does not make
this aborted strict job successful.

The earlier [CI 36797859586](https://github.com/kleedaisuki/mote/actions/runs/36797859586)
at `833ef480` passed. Comparing that commit with `99fbe39` shows **no changes** in
`WindowsEditorShell.cs`, `Win32Interop.cs`, `WindowsRichEditUndoScope.cs` or the
crashing theme fixture. Recent persistent-notice controller changes are not
directly reachable from this fixture: it constructs a shell and engine Document,
but no NativeEditorController or analysis dispatcher. Changed suite scheduling or
memory pressure remains possible; attributing this crash to those changes would
be unsupported.

The prior passing run used the same OS family/build but image version
`20260922.246.2`, **not** the failing run's `20260925.250.1`. The logs do not retain
msftedit.dll hash/version. Thus these runs are not an identical-host-image
regression comparison; an OS/native-component difference is another open
discriminator, not an established cause.

## Fixture and native contract audit

The fixture creates hidden STATIC parent/status and two actual `RICHEDIT50W`
controls; creation, import, theme updates and destruction occur synchronously on
the same test thread. It does not set focus, mutate clipboard, show a top-level
window, synthesize input or change OS/user settings. The `semantic=true` branch
installs `_analysis` with plain preview text `preview`; only this branch reaches
the indicated InstallPreview call from SetTheme. Its literal-preview importer
temporarily clears read-only, calls Unicode SetWindowTextW, then restores
read-only. The false branch has no `_analysis`.

- The SetWindowTextW P/Invoke uses the Unicode character set and pointer-sized
  HWND, with BOOL result. No obvious signature or payload mismatch was found.
- The native TOM undo scope is balanced and per-editor; query/release use direct
  native vtables. This audit is not proof against an internal native defect.
- All fixture controls are destroyed before its acquired msftedit library
  reference is released. Other native fixtures likewise acquire their own module
  references; no concrete premature release was identified.

Microsoft documents that
[SetWindowTextW sends WM_SETTEXT to controls owned by the current process](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowtextw),
and [GetWindowThreadProcessId identifies the creating thread and returns zero for invalid HWNDs](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowthreadprocessid).
[IsWindow is not a foreign-thread lifetime guarantee](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-iswindow),
because handles can be destroyed/recycled after inspection. Consequently this
investigation adds owner-thread **test preconditions**, not a speculative product
`IsWindow` check or blanket serialization/API replacement.

## Bounded local probes and negative results

Environment: Windows 11 build 10.0.26200, x64; SDK 10.0.400; .NET runtime 10.0.11;
System32 msftedit.dll version 10.0.26100.8875, SHA-256
`117D6B3DB6706F3731CFA56C16358C83D23271ECDD6A20F5B67B254465E551ED`.
Artifacts stay under repository `.cache/windows-preview-av/` and `.temp/windows-preview-av/`.

| Probe at baseline `99fbe39` | Result | Limit |
| --- | --- | --- |
| 20 fresh dotnet test processes, only NativeThemeOverrideWindowsTests | 20 exit 0; 3 cases/process | A clean isolated fixture is not the whole hosted suite. |
| Whole Mote.Tests, Release/no-build/no-restore, `--blame-crash`, local results directory | 1158/1158; exit 0; ~50.6 s | One successful local suite does not close the hosted crash. |
| 20 fresh native-mix test processes (filter below) | 20 exit 0; 61 cases/process | Probes concurrent classes but not all allocation workloads. |
| Separate console runner, 6 Task.Run workers ×150 iterations ×both bool cases; GC every 20 iterations | 1800 completed fixture workflows; exit 0 | This stresses native controls and module/TOM lifetimes on local Windows, not identical Server 2025. |

Native-mix filter:

```text
FullyQualifiedName~NativeThemeOverrideWindowsTests|FullyQualifiedName~WindowsFlowRtfTests|FullyQualifiedName~NativeFlowHeadingIdentityTests|FullyQualifiedName~NativeThemeOverrideFileWorkflowTests|FullyQualifiedName~NativeCsvGridSourceNulTests|FullyQualifiedName~NativeCsvGridWindowsTests
```

The temporary console project references `../../tests/Mote.Tests/Mote.Tests.csproj`,
targets net10.0 and executes this bounded orchestration (the loop does not modify
real user files or invoke any global input):

```csharp
await Task.WhenAll(Enumerable.Range(0, 6).Select(worker => Task.Run(() =>
{
    var fixture = new Mote.Tests.NativeThemeOverrideWindowsTests();
    for (var iteration = 0; iteration < 150; iteration++)
    {
        fixture.Same_id_override_updates_native_background_without_source_or_history_changes(true);
        fixture.Same_id_override_updates_native_background_without_source_or_history_changes(false);
        if (iteration % 20 != 0) continue;
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
})));
```

These negative probes make a simple deterministic single-fixture import error
less likely. They do **not** establish a probability, eliminate a native race,
justify retrying/ignoring the strict failure or prove Native AOT safety.

## Retained diagnostic regression

The existing theme fixture now checks the exact creating OS thread, current PID
and `RICHEDIT50W` class before initial and same-ID palette application. Its semantic
branch additionally repeats eight alternating original/composed imports with
forced GC and finalizer settlement, asserting exact source/preview readback and
unchanged native selection each time, then executes the existing one-step
text-undo, redo, nested exceptional lease and engine-history checks. This helps
classify a future invalid/wrong-thread/wrong-class fixture handle as an ordinary
assertion instead of silently feeding it into the importer.

The GC runs **between** synchronous imports, not inside the native P/Invoke; this
is call-boundary collection/lifetime pressure, not direct proof of marshalled
string rooting or COM reference correctness during an import. An independent
scoped review is recorded in
`docs/reviews/windows-preview-av-diagnostic-review.md`.

After the test change, Release build with warnings-as-errors and the combined
theme/Flow/file-reload filter passed **13/13**, exit 0 (1.02 s total, semantic
fixture 127 ms). No production source changed.

## Next discriminating evidence

The root workstream has added strict Windows VSTest `--blame`, results under
`.cache/ci-test-results/windows`, and failure-only `Sequence.xml` upload without
masking the job outcome. This captures test sequence, **not a native dump**; a
narrow local CLI smoke passed 3/3. If the next hosted failure requires native
exception instruction/registers/stack, arrange explicit scoped crash collection
as a subsequent discriminator rather than assume `--blame` supplies it. If the
added preconditions fail first, their exact thread/PID/class assertion is the
next causal lead. The issue remains open until the evidence supports a concrete
mechanism and an appropriate regression.
