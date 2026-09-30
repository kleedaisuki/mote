# Windows typed Flow rendering

Status: implementation and focused host acceptance, 2026-09-30. This is the Windows
adapter portion of `native-rendering-architecture.md`, not full product acceptance.

## Installation contract

`WindowsFlowRtf` serializes a validated, bounded Formats `FlowRenderProjection` into
one escaped RTF transaction. Its only destinations are font/color tables and text;
there are no fields, embedded objects, images, hyperlink effects or URL actions.
Signed UTF-16 Unicode escapes preserve Chinese and surrogate pairs. RichEdit remains
the read-only selectable native surface; Engine source/Undo/Save ownership is unchanged.

Theme UI font/size governs body text. Heading size derives from semantic level;
strong/emphasis flags are flattened, code uses the theme editor font, and quote/list
depth selects indentation. Theme spacing selects paragraph spacing. Source origins
come only from the policy projection; native glyph geometry does not infer Markdown
source offsets. Synthetic markers already present in projection text are not invented
by the adapter.

The shell clears installed identity before preparing replacement, validates projection
version/display ranges, imports text and layout, reads all native text back under the
CRLF contract, then retains `NativePresentationId`. Native activation carries the
retained sequence, including same-version changes. Invalid imports cannot authorize
an old origin map. Source-stamp transitions revoke the old identity. `ShowPreview`
hides the pane and resizes the ordinary Canvas/source pane to full width; no format
conditionals live in the shell.

Same-display replacement saves native selection and pixel scroll, imports once, then
restores both. The OS may clamp pixel scroll when typography changes reduce layout
extent, per [EM_SETSCROLLPOS](https://learn.microsoft.com/en-us/windows/win32/controls/em-setscrollpos).
[EM_GETSCROLLPOS](https://learn.microsoft.com/en-us/windows/win32/controls/em-getscrollpos)
uses virtual text pixels and documents 16-bit returned values; the bounded 120-paragraph
surface limits practical reach, but this is not an arbitrary million-line scroll guarantee.

## Read-only RichEdit import boundary

The hidden actual-HWND test exposed empty readback when Unicode `EM_SETTEXTEX` was
sent to `ES_READONLY`. This is also handled explicitly by mature upstream
[wxWidgets](https://github.com/wxWidgets/wxWidgets/blob/master/src/msw/textctrl.cpp).
The adapter temporarily removes read-only solely around synchronous programmatic
import and restores it in `finally`, without pumping messages or yielding input.
Clearing a stale preview uses the same transaction. No editable preview or second
text owner is introduced. Native automatic URLs are explicitly disabled using
[EM_AUTOURLDETECT](https://learn.microsoft.com/en-us/windows/win32/controls/em-autourldetect).

## Focused verification

`WindowsFlowRtfTests` covers RTF escaping, Unicode surrogate pairs, deterministic
paragraph/inline styling, CRLF delimiters, embedded-NUL rejection and paragraph
boundary rejection. Its Windows-only actual hidden `RICHEDIT50W` test exercises the
production install method, exact Unicode CRLF readback, heading bold/size, selection
and pixel-scroll retention through light-theme restyle, and installed-sequence
activation/revocation. `EM_GETSELTEXT` verifies the native selected-text payload; it
does **not** claim actual OS clipboard/Copy execution. No foreground or user clipboard
state is changed. Host verification does not establish physical mouse/keyboard,
screen-reader, macOS, Native AOT or four-RID acceptance.

Host result: Windows 11 10.0.26200 x64, .NET SDK 10.0.400, Release managed:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj -c Release --no-build `
  --filter FullyQualifiedName~WindowsFlowRtfTests --logger 'console;verbosity=normal'
```

Result: **6/6 passed**, including the actual hidden HWND test (53 ms test body).
The heading reported `CFE_BOLD` and `Height=380` twips; retained selected text was
`Heading`. Same-text restyle preserved sampled selection and pixel scroll. The test
uses reflection solely to exercise the production private install/activation methods;
it does not establish an end-to-end user gesture or controller integration by itself.

## Independent-review corrections

Legacy `Flow == null` text is imported through literal `SetWindowTextW`, never
`EM_SETTEXTEX` RTF auto-detection. Only the adapter-generated escaped Flow payload
uses the RTF reader. Clearing the preview uses the literal path. Leading RTF syntax
in a source-derived preview must remain visible verbatim and must not instantiate
RTF fields or objects before readback rejects an altered buffer.

Visibility/layout can synchronously reenter controller analysis publication. After
layout, `SetAnalysis` verifies the retained analysis reference still equals its
incoming view before installing text or updating status. A nested newer presentation
therefore wins even at the same source stamp. Focused hidden-HWND tests exercise both
leading-RTF fallback and nested `WM_SHOWWINDOW` publication without foreground input.

Review-fix host result: **9/9 WindowsFlowRtfTests passed**, including two literal
leading-RTF fixture imports and the synchronous nested visibility publication test.
The latter retained `newer` text and presentation sequence 2 instead of reinstalling
outer sequence 1. The combined `dotnet test -warnaserror` command returned exit 1
because an independent integration-test file had xUnit2031; the Windows test execution
itself passed and no Windows-file compiler/analyzer warning remained. The integration
owner was notified to repair that separate warning.
