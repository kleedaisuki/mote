# Explicit-open encoding native/controller review

Date: 2026-10-01. Independent source review; no GUI or native library execution.

## Decision and scope

**No substantive defect found in the scoped changes inspected.** This decision
does not certify actual modal keyboard behavior, accessibility, Native AOT ABI
execution or four-RID encoding resource inclusion. Portable controller tests are
owned by a separate validator and are not reported as executed by this reviewer.

The review covers the encoding additions in `NativeEditorController`, the optional
`INativeOpenEncodingShell` contract and shared choices, Windows menu/dispatcher
integration and `Win32EncodingPrompt`, and macOS menu/selector integration and
`MacOpenEncodingPrompt`. Unrelated source-diagnostic changes in the same worktree
and historical controller/save behavior are outside this review.

Inspected source byte SHA-256 (working-tree changes plus macOS commit `afb7b30`):

| File under `src/Mote.Native/` | SHA-256 |
| --- | --- |
| `NativeEditorController.cs` | `C4033AA5FEE6F02DF97F8056E2AC805D50A284268FE08A951DBB623E84CC5E25` |
| `NativeOpenEncoding.cs` | `7DD7D3174E60D6D78F12C3C05C1E732AC5D011AD0A2C6DD9630D2935A817BF6B` |
| `Windows/Win32EncodingPrompt.cs` | `FB8A11954C1736D403D39BD4198D14D6F8C26925E4F936837B758B568797E525` |
| `Windows/WindowsEditorShell.cs` | `76E9E3ED03A4D692A41E8193FFDCBDF5AC2BCAD47445EF29F198312CC4A4F310` |
| `Mac/MacOpenEncodingPrompt.cs` | `30477DEC054EC3B90DB6A87CB28C01E8E399B9DFACEE316DD0F3CBF8791A8756` |
| `Mac/MacEditorShell.cs` | `A2FABF660A9B39203466E71B1DB3F43CEFC4CAE857812B06B55E555CAEBC24BE` |

## Controller admission and lifetime

- The optional interface preserves existing shell adapters: ordinary Open and
  its shortcuts remain unchanged; adapters lacking the capability retain the
  generic decode-error path. Subscription and disposal unsubscription are paired.
- Explicit menu entry calls `CanReplace` before a picker/chooser, retaining
  pending-input settlement, Save-in-flight exclusion and dirty-document consent.
  It captures the original document, version and open serial **before** modal
  operations. Cancellation does not create an open attempt. Reentrant disposal,
  New or a later Open invalidates the modal choice through disposed/serial checks.
- Automatic Open offers selection only for a `DecoderFallbackException` on the
  default route. It does not guess a codec. A null result does not retry; a chosen
  result creates a new explicit attempt. That attempt has `selected != null`, so
  a further decode failure cannot re-enter the chooser or silently loop codecs.
  BOM conflicts and inverse-byte rejection remain ordinary reported failures.
- Retry passes the original document and version, not the document state at the
  end of the chooser. Edits before/during the chooser therefore retain the
  existing result-side fresh replacement-consent check. The loaded document is
  not installed before the existing posted result checks settle input, exclude
  an active Save and handle stale identity/version. Rejected successful loads
  are disposed rather than adopted or saved.
- The default failed attempt terminates its existing open observation as Failure;
  an explicit retry receives a fresh ordinary open mark/request and child I/O
  observation. Cancellation/stale and success still use the original completion
  machinery. No codec guessing or content/filename telemetry fields were added.
  This is per-attempt instrumentation, not an asserted retry-to-first-attempt
  graph edge or native chooser runtime witness.
- Recoverable chooser exceptions are reported without replacing the document.
  Windows's new command wrapper and macOS's new unmanaged selector wrapper also
  contain secondary reporting failures so new entry points do not unwind managed
  errors through native callbacks. This review does not expand the claim to all
  historical Open callbacks.

## Windows modal and control semantics

The prompt uses the editor as owner and disables it during its modal loop;
`finally` restores ownership and clears the rooted active callback lifetime.
The callback dispatch matches the owned window, captures nonfatal failures, and
returns WM_CREATE failure or closes the owned window before rethrowing from the
managed loop. OutOfMemoryException retains the existing fatal policy.

The dropdown is closed-list, unsorted, and has a non-codec index-zero placeholder.
Exact insertion indices and initial selection are checked. Open begins disabled;
selection only enables it. Acceptance rereads and bounds-checks the actual index.
Cancellation never maps to UTF-8. Escape, close and Cancel do not assign a result.
Cancel is the initial focused/default button; explicit DM_GETDEFID/DM_SETDEFID
handling does not silently promote Open. Microsoft documents that IsDialogMessage
can process ordinary control-containing windows and that its handled messages
must not subsequently be translated/dispatched; this loop follows that contract.

These source checks do not prove the actual user32 Enter/Tab/dropdown behavior,
owner restoration under real shutdown, high-DPI geometry or assistive technology.

## macOS interop and ownership

The initializer has a dedicated `objc_msgSend` import accepting a sequential
32-byte rectangle (four 64-bit CGFloat coordinates) followed by a one-byte BOOL
argument, with pointer-sized object return. It does not substitute an NSInteger
for BOOL or rely on default CLR bool marshalling. Selection index and object
messages use the existing pointer-sized bridge. The new selector's function
pointer and Objective-C registration both describe a void method with one object
argument, and its outer catch contains secondary managed failures.

The alert and popup each have a local +1 allocation ownership. The accessory's
retain is separate; releasing alert then popup ends those independent references.
The initialized popup return is adopted rather than assuming init must return
the original receiver; nil initialization is treated as failure without another
release of its consumed allocation. Native alert/popup/button creation failure
does not masquerade as healthy user cancellation.

Cancel is the first/default alert button; Open response 1001 and a valid shared
index are both required. Placeholder acceptance produces an error, not a default
codec. There is no new callback delegate requiring a lifetime root inside the
popup helper and no file/document mutation in the helper itself.

## Evidence boundaries and next acceptance

Inspected portable test source exercises actual controller orchestration with
injected modal callbacks and posted completion, including explicit valid-UTF8
bytes intentionally interpreted as UTF-16, cancellation, failed selection,
reentrant New/Open/dispose, fresh dirty consent, Save/input veto and optional
adapter compatibility. Execution evidence belongs in the validator's artifact.

Require actual hosted Windows/macOS x64/arm64 Native AOT execution for codec
inclusion, and native modal acceptance for Cancel/default Return, all eight
index choices, placeholder refusal, keyboard navigation and preservation of the
current document. A successful compile, managed layout reflection or portable
controller adapter is not a substitute for those runtime observations. Native
allocation/population fault containment also requires its own controlled witness.

## Primary sources checked

- [Microsoft IsDialogMessageW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-isdialogmessagew), including ordinary-window use and DM_GETDEFID/DM_SETDEFID interaction.
- [Apple NSPopUpButton initializer](https://developer.apple.com/documentation/appkit/nspopupbutton/init%28frame%3Apullsdown%3A%29). The rendered page required JavaScript; its official documentation JSON at `https://developer.apple.com/tutorials/data/documentation/appkit/nspopupbutton/init(frame:pullsdown:).json` was fetched to check rectangle/Boolean parameters, false-as-popup semantics and possible nil initialization. This API documentation is not proof of actual cross-architecture P/Invoke execution.
- Existing platform implementation notes: `docs/validation/windows-open-encoding-ui.md` and `docs/validation/mac-open-encoding-ui.md`, with their explicit remaining native/runtime gaps.
