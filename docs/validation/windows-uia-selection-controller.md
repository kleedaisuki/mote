# Canonical accessibility selection controller contract

## Scope and implementation

`IAccessibleSelection` is an optional internal callback implemented by
`NativeEditorController`; the existing `IAccessibleViewport` shell attachment is
unchanged. A provider may discover selection support through an interface cast.
No platform accessibility source is changed by this assignment.

`TrySelect` accepts a half-open absolute UTF-16 `AccessibleRange`. It synchronously
checks the owning managed UI thread, controller/canvas lifetime, generation and
version, both endpoint bounds, and both safe boundaries (surrogate pair and CRLF
interiors are rejected). Native shell text composition or native canvas
composition returns `CompositionBlocked` before any navigation, viewport, input
binding, or selection mutation. It never posts and waits or commits marked text.

On acceptance, the controller invalidates an outstanding Find, sets the canonical
navigation selection, and uses the existing `RevealSelection` canvas path. That
path synchronously publishes a matching `AccessibleDocument` state and native
canvas frame before `Selected` is returned. Selection does not edit the document,
append Undo entries, or call `FocusSource`. The requested range is not truncated
to the bounded caret-local native input window. `Selected` proves selection
publication, not guaranteed glyph visibility where the OS supplies no caret
geometry; the existing reveal path attempts the geometry-backed horizontal
correction without inventing a visibility proof.

Outcomes: `Selected`, `StaleRange`, `CompositionBlocked`, `InvalidBoundary`,
`WrongThread`. Stale identity and malformed bounds share `StaleRange`, matching
the existing viewport callback's identity contract. Legacy-page and disposed
controllers reject requests with `StaleRange`.

## Verification

Local Windows / .NET 10 test command:

```powershell
dotnet test tests/Mote.Tests/Mote.Tests.csproj --no-restore --filter 'FullyQualifiedName~Accessible_selection|FullyQualifiedName~Canvas_accessibility_reveal' --verbosity minimal
```

Result: **18 passed, 0 failed, 0 skipped** (155 ms reported test duration).
The new selection suite contributes 13 cases covering collapsed/start/end/full
selection; same source snapshot and unchanged Undo/Redo; both native composition
sources; independently unsafe start/end surrogate and CRLF boundaries; stale
version/generation, negative/reversed/past-end bounds, New and disposal; a genuine
other-thread call without post-and-wait; legacy rejection; and a 140,000-code-unit
offscreen full-document selection with exact published endpoints and viewport
movement. The remaining five cases exercise the existing reveal callback.

These are deterministic controller/fake-shell contracts. They are not evidence
that an external Windows UIA client can Select: platform bridge integration and
external client verification remain the parent task's responsibility.
