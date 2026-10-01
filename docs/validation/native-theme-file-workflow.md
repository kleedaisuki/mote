# File-backed native theme workflow validation

## Contract and scope

The user requires editable configuration under conventional `~/.mote`, configuration overriding theme-policy defaults, and native editing behavior preserved. The existing theme implementation contract adds explicit Reload Settings, transactional retention on rejected reads/maps, persistent nonmodal diagnostics, and process-lifetime trace/cache paths. These expectations are taken from `docs/native-theme-overrides.md`, `docs/theme-override-configuration.md`, and `MoteConfiguration` contracts rather than inferred from successful tests.

Independent fixture: `tests/Mote.Tests/NativeThemeOverrideFileWorkflowTests.cs`. No production or pre-existing test files were modified.

## Reproduction

Environment: Microsoft Windows 10.0.26200, .NET SDK 10.0.400, Release, parent baseline HEAD `5dc0fcfccf66aeee792f04ea878085962364fdc7` (other agents may have unrelated unstaged CSV work).

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --filter FullyQualifiedName~NativeThemeOverrideFileWorkflowTests -warnaserror --logger 'trx;LogFileName=theme-file-workflow.trx' --results-directory .temp/theme-file-workflow
```

Final result: 1 passed, 0 failed; 184 ms test duration; command exit 0; no compiler/analyzer warnings. Evidence: `.temp/theme-file-workflow/theme-file-workflow.trx`.

## High-information sequence and observations

| Check | Expected | Observed |
| --- | --- | --- |
| Startup conventional home | Actual file under repository-local test home `/.mote/config.toml`; no user-home writes | MoteConfigLoader loaded actual TOML; native preview background `#181818` |
| Same ID palette reload | New values installed despite unchanged `mote-dark` ID | Production command 217, asynchronous real file read, production WM_APP completion; native background changed to `#101820` |
| Half-written TOML | Retain previously installed palette and settings; persistent diagnostic | Real unterminated value rejected; prior native background retained; CONFIG diagnostic in actual STATIC status text |
| Open/read failure | Retain same prior palette, not defaults | Windows FileShare.None lock caused actual loader Rejected result and CONFIG_READ reload notice |
| Ordinary status/appearance refresh | Rejection remains visible without source mutation | Actual shell SetDocument status refresh and subscribed appearance event retained notice and displayed text |
| Subsequent fixed file | Recovery installs new palette and clears rejection | Unlock plus valid TOML applied `#121A22`, status notice cleared |
| Trace/cache override | Live writer paths retained; reload reports next launch | Running controller kept original directories/trace-disabled state; fresh actual load resolved relative next-cache/next-traces and trace enabled; no directories created |
| Document identity/history | Text, version, selection, Undo/Redo survive every palette/read transaction | Same engine snapshot object, same native view stamp, exact Unicode source and selection 2..7 throughout; engine CanUndo retained, CanRedo false |
| Native history | Palette updates do not become text undo units or clear undo | EM_CANUNDO remained set; actual native EM_UNDO removed source edit and EM_REDO restored it |
| Production history commands | Engine owns Undo/Redo | Production commands 206/207 removed/restored exact source after transactions |

The native color read is a same-value `EM_SETBKGNDCOLOR` call returning the prior installed color. It is not merely a managed policy assertion. Status is read using `GetWindowTextW` on the actual hidden STATIC control.

## Harness fidelity and limitations

The fixture creates actual hidden Windows parent/RichEdit/STATIC HWNDs, loads the actual platform adapter and controller, invokes the subscribed Shown event, production WM_COMMAND handler, production OnTextChanged/FlushSelection notifications and production WM_APP completion handler on one thread. The controller uses its **default file-backed reload loader**; no settings-loader delegate injects synthetic settings. Only fixture configuration under root `.temp/tests/` is modified. Handles, font/brush resources and module are released; RepoTemp verifies cleanup containment. No clipboard, visible UI, IME/input-source, TCC, global configuration, environment variables, or system settings are changed.

Reflection replaces window registration/startup and message-loop delivery; it does not replace parsing, theme composition, settings transaction, native controls or engine history. This is a managed Release hidden-control integration check, **not** a published Native AOT binary end-to-end GUI launch, pointer-menu click, full event-loop accessibility check, real user IME exercise, or screenshot acceptance. The Fact returns on non-Windows; a non-Windows green count is not Windows coverage.

No production defect was reproduced. Initial harness failures were isolated and fixed: RichEdit-specific text-reading API cannot read STATIC status text, and ES_MULTILINE applied to STATIC selects a non-text STATIC style. Correct control-class-specific styles and GetWindowTextW fixed those fixture errors without weakening product assertions. Platform guards were made analyzer-visible to nested delegates.

A future published-binary test could use an opt-in nonmutating launch probe which creates the normal hidden window, accepts a repository-contained configuration home, dispatches Reload Settings through the real message loop, and emits bounded privacy-safe state assertions. This is not required to make the current file-backed path testable and is not a request to alter production now.
