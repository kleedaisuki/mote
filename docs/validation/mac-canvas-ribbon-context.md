# Mac canvas ribbon current-frame validation

## Contract and scope

The retained native input binding owns a source interval, not the current caret.
When a same-version frame changes selection within that interval, the ribbon must
display the frame's active source offset and project that same frame's selection
into the retained host interval. Null binding/frame or a frame version different
from the binding snapshot must return false with empty/default outputs. Formatting
uses invariant grouping. This preserves the existing native host admission and
projection semantics; composition remains the native caller's guard.

`tests/Mote.Tests/MacCanvasRibbonContextTests.cs` calls the actual production
`MacTextInputIsland.TryGetRibbonContext` static helper. It constructs immutable
engine snapshots and frames without constructing a native island, loading AppKit,
opening a GUI, or changing machine input state. Narrow CA1416 suppressions cover
only that pure helper and integer fields of the native range value.

## Evidence

Environment: Windows 10.0.26200, x64, .NET SDK 10.0.400. Starting repository HEAD:
`3a0552a691e46470ac88ef65ce3c621276ec852c`, with the parent's production helper
change present. Tested source SHA-256 for `MacTextInputIsland.cs`:
`707058E7E688FD20AD7ACF746ABF56F8C0619A7FF2AC93F19CC77099F1D9083D`.

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --filter FullyQualifiedName~MacCanvasRibbonContextTests --logger 'trx;LogFileName=ribbon-context.trx' --results-directory .cache/validation/mac-canvas-ribbon-context
dotnet build tests/Mote.Tests/Mote.Tests.csproj -c Release --no-restore
```

The single focused test run passed **18/18**, zero failures/skips (64 ms).
Four platform-analysis warnings initially concerned reading the pure range
fields outside the suppression scope. The only subsequent test-file change
extended those existing suppressions to include those fields; no expectations
or executable behavior changed. The final build succeeded with **zero warnings
and errors**. No tests were repeated. Logs and TRX are under
`.cache/validation/mac-canvas-ribbon-context/` (`focused-release.log`,
`ribbon-context.trx`, `final-build.log`). Final native assembly SHA-256:
`0CDC01EBFACE7A3D61D765053382E0331B671C86E4DA1BC889BFB1794BC6618E`.

| Claim | Expected and observed |
| --- | --- |
| Retained-binding movement | Binding stays at active 1,200; newer frame labels 1,207 and projects caret to host offset 17, rather than original offset 10. |
| Regression discrimination | Constructing the former binding-active label yields `Input @ 1,200`, explicitly unequal to required `Input @ 1,207`. No production mutation was needed. |
| Range projection | Twelve literal expected cases cover forward/reverse selection, partial overlap on both ends, full overlap, selections outside either end, and endpoint carets. |
| Admission | Missing binding, frame, both, and mismatched version each refuse with empty label and zero range. |
| Culture | Under task-local German current culture, normal grouping is `1.234`, but the actual helper returns `Input @ 1,234`; prior culture is restored in `finally`. |

## Verdict and limitations

The pure current-frame label/selection contract is verified. This is **not**
evidence that AppKit actually installs or draws the ribbon, that composition
callbacks retain marked text correctly, or that physical typography, accessibility,
and input-method integration work. Native caller ordering and composition guards
require source review; rendered/native interaction needs separate macOS evidence.
