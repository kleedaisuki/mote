# macOS theme override and explicit reload probe

## Scope and safety boundary

`MacThemeOverrideProbe.Run()` is a non-gating published-binary AppKit integration
probe. It creates an ordinary LegacyPage shell, the production
`NativeEditorController`, and one untitled in-memory document. It does not call
`MoteConfigLoader.Load`, read user configuration, create directories, open/save a
document, write the clipboard, inject external events, request TCC permissions,
switch input sources, or modify system settings. Synthetic paths below repository
`.temp/mac-theme-probe-unused-home` exist only as strings in immutable settings;
the probe does not check whether those paths exist.

The optional controller settings-loader delegate returns an already constructed
snapshot and increments a process-local counter. Thus the probe exercises the
real asynchronous reload publication and native menu dispatch, but intentionally
does not exercise filesystem decoding, persistence or malformed-config recovery.
Those are separate configuration/controller test concerns.

The probe opens a visible native window and temporarily uses that application's
normal UI. All mutations are confined to the probe's own untitled source/view.
No physical keyboard, real IME, external accessibility client or screen pixel
claim follows from it. There is no synthetic marked-text exercise in this probe.

## Assertions

1. Startup composes `mote-dark` with `preview.background = #181818` and installs
   that color in the actual preview `NSTextView`, without calling the reload loader.
2. Native `insertText:replacementRange:` inserts `theme reload 中 😀`. Both native
   source and the controller's projected document must contain the exact string.
3. A source selection of UTF-16 range `(13, 1)` selects the Chinese character.
   The document stamp and selection are captured before reload.
4. Calling the installed `moteReloadSettings:` delegate selector asynchronously
   installs a second immutable snapshot with the **same theme ID** and
   `preview.background = #101010`. Exactly one loader invocation is required.
5. Native color readback, source, projected source, document stamp and selection
   establish that the reload changed appearance without editing source or adding
   an engine transaction.
6. Routed `moteUndo:` removes the one source insertion in one step, and routed
   `moteRedo:` restores it. Both preserve the new palette. This detects an extra
   theme-created undo entry without depending on the native undo manager.

A monotonic 20-second deadline bounds polling. Delayed checks return to the
existing shell UI dispatcher every 50 ms, leaving AppKit and the background
reload worker runnable. Completion or failure approves discard of this probe's
untitled document once, closes the shell, and returns `0` only after all checks.

## Native interop review

The probe reuses the existing `ObjC.SendRange` bridge for `NSRange` readback and
`ObjC.Send` pointer-return/object-argument bridge for native colors. Before reading
components, it converts the preview background to `NSColorSpace.sRGBColorSpace`
with `colorUsingColorSpace:` and rejects a nil result. Apple documents that the
conversion can return nil and that component getters require a compatible color
space. The opaque RGB components and alpha are checked with absolute tolerance
`0.0001`, not a screen/ICC screenshot comparison.

The new `Scalar` P/Invoke has `double` return and no method arguments for
`redComponent`, `greenComponent`, `blueComponent` and `alphaComponent`. `CGFloat`
is double on both supported 64-bit Macs; this follows the existing
`MacFlowRenderingProbe` scalar ABI. It does not route floating-point returns
through the pointer-return bridge, and introduces no struct-return ABI.

Primary references:

- [Apple: colorUsingColorSpace:](https://developer.apple.com/documentation/appkit/nscolor/usingcolorspace%28_%3A%29?language=objc)
- [Apple: NSColorSpace](https://developer.apple.com/documentation/appkit/nscolorspace?language=objc)
- [Apple: NSColor component getters](https://developer.apple.com/documentation/AppKit/NSColor?language=objc)

## Evidence and integration status

On Windows, 2026-09-30:

```powershell
dotnet build src/Mote.Native/Mote.Native.csproj -c Release --no-restore -warnaserror
```

Result: **0 warnings, 0 errors**. This is compilation evidence only; AppKit was
not executed and neither Mac RID's Native AOT publication has been validated by
this work. Program/CLI registration and GitHub Actions invocation are owned by
the integrating parent task, not this probe implementation. Keep the probe
non-gating until actual published-binary macOS evidence is recorded.
