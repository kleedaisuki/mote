using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mote.Formats;
using Mote.Native.Mac.Canvas;
using Mote.Themes;
using Mote.Telemetry;

namespace Mote.Native.Mac;

/// <summary>Whole-source replica. Native input/layout belongs to AppKit; canonical history belongs to the controller.</summary>
internal sealed unsafe partial class MacEditorShell
{
    /// <summary>Explicit product profile; never switched by file size.</summary>
    private readonly bool _nativeSource;
    /// <summary>Frozen during marked text, advanced only by successful guarded publication or acknowledgment.</summary>
    private NativeSourceInstallation? _sourceInstallation;
    /// <summary>Latest immutable canonical facts; not a semantic cache for offscreen native attributes.</summary>
    private NativeSourceSemantics? _sourceSemantics;
    /// <summary>Monotonic viewport and decoration invalidation counters.</summary>
    private long _sourceViewportSequence, _sourceStyleSequence;
    /// <summary>Failed replicas cannot emit edit candidates or receive source commands.</summary>
    private bool _sourceUnavailable;
    /// <summary>Unadmitted native text vetoes command settlement; retained canonical failures may still save the engine snapshot.</summary>
    private bool _sourceUnadmittedText;
    /// <summary>Every accepted fact publication, including same-sequence pending clears, invalidates palette authority.</summary>
    private long _sourceSemanticRevision;
    /// <summary>A programmatic endpoint witness is valid only while the observed ordered range still matches.</summary>
    private NativeSourceSelection? _sourceSelectionWitness;
    /// <summary>Deduplicates draw-triggered native viewport observations without confusing them with document versions.</summary>
    private NativeSourceViewObservation? _sourceObservedView;
    /// <summary>Last fully installed visible palette; incomplete batches never certify this cache.</summary>
    private (NativeDocumentStamp Stamp, long Nonce, TextSpan Visible, long Semantics, IThemePolicy Theme)? _sourceInstalledStyle;
    /// <summary>One supersedable UI-turn batch sequence, separate from successful installed state.</summary>
    private ((NativeDocumentStamp Stamp, long Nonce, TextSpan Visible, long Semantics, IThemePolicy Theme) Key, long Sequence)? _sourcePendingStyle;
    /// <summary>Unqualified geometry does not revoke an independently certified exact editable text replica.</summary>
    private bool _sourceGeometryUnknown;
    /// <summary>Native runtime probes can distinguish unknown geometry from disabled text input.</summary>
    internal bool ProbeSourceGeometryUnknown => _sourceGeometryUnknown;
    /// <summary>Chrome state remains independent from the native full-source string.</summary>
    private string _sourceTitle = "mote";
    /// <summary>Canonical dirty state, not NSTextStorage's unrelated native undo state.</summary>
    private bool _sourceModified;
    /// <summary>Canonical history availability used by native context-menu validation.</summary>
    private bool _sourceCanUndo, _sourceCanRedo;
    /// <summary>Owned recovery consent is nonreentrant across AppKit modal event pumping.</summary>
    private bool _sourceRecoveryPrompt;

    /// <inheritdoc />
    public bool NativeSourceEnabled => _nativeSource;
    /// <inheritdoc />
    public bool HasSourceMarkedText => _nativeSource && _editor != 0 &&
        SourceBool(_editor, ObjC.Sel("hasMarkedText")) != 0;
    /// <inheritdoc />
    public event Action<NativeSourceCandidate>? SourceCandidate;
    /// <inheritdoc />
    public event Action<NativeSourceViewObservation>? SourceViewChanged;
    /// <inheritdoc />
    public event Func<bool>? SourceRecoveryRequested;

    /// <inheritdoc />
    public NativeSourceObservation? InstallSource(NativeSourceInstallation installation)
    {
        if (!_nativeSource || _editor == 0 || IsTextComposing) return null;
        try
        {
            MacProductSourceModel.ValidateInstallation(installation);
            _settingText = true;
            _sourceUnavailable = true;
            _sourceDrawStamp = null;
            _sourceDrawTrace.ObserveDocument(installation.Stamp.Generation, installation.Stamp.Version);
            _sourceStyleSequence++;
            _sourceSemantics = null;
            _sourceObservedView = null;
            ClearAnalysisPreview();
            ObjC.Send(_editor, ObjC.Sel("setString:"), ObjC.String(installation.Projection.Display));
            ExactSource(installation.Projection.Display, installation.Stamp.Version);
            _sourceInstallation = installation;
            _visibleText = installation.Projection.Display;
            _sourceUnavailable = false;
            _sourceUnadmittedText = false;
            SetStatusNotice(null);
            ObjC.Send(_editor, ObjC.Sel("setEditable:"), 1);
            SetSourceSelectionCore(installation, false);
            _sourceDrawStamp = installation.Stamp;
            PublishSourceForeground();
            return ObserveSource();
        }
        catch (Exception error) { SetSourceUnavailable(error.Message); return null; }
        finally { _settingText = false; QueueSourceView(); }
    }

    /// <inheritdoc />
    public bool AcknowledgeSource(NativeSourceInstallation installation)
    {
        if (!_nativeSource || _sourceUnavailable || _editor == 0 || IsTextComposing ||
            _sourceInstallation is not { } before || before.Stamp.Generation != installation.Stamp.Generation ||
            before.Nonce != installation.Nonce) return false;
        try
        {
            MacProductSourceModel.ValidateInstallation(installation);
            ExactSource(installation.Projection.Display, installation.Stamp.Version);
            _sourceInstallation = installation;
            _visibleText = installation.Projection.Display;
            _sourceSemantics = null;
            _sourceStyleSequence++;
            _sourceDrawTrace.ObserveDocument(installation.Stamp.Generation, installation.Stamp.Version);
            _sourceDrawStamp = installation.Stamp;
            PublishSourceForeground();
            QueueSourceView();
            return true;
        }
        catch (Exception error) { SetSourceUnavailable(error.Message); return false; }
    }

    /// <inheritdoc />
    public NativeSourceObservation? ApplySourceChange(NativeSourceReplacement replacement)
    {
        if (!SourceMatches(replacement.Before) || IsTextComposing) return null;
        try
        {
            MacProductSourceModel.ValidateReplacement(replacement);
            ExactSource(replacement.Before.Projection.Display);
            var clip = ObjC.Send(_editorScroll, ObjC.Sel("contentView"));
            var origin = MacOnScreenCanvasNative.GetRect(clip, ObjC.Sel("bounds")).Origin;
            _settingText = true;
            _sourceDrawStamp = null;
            _sourceStyleSequence++;
            var storage = ObjC.Send(_editor, ObjC.Sel("textStorage"));
            ReplaceSourceCharacters(storage, ObjC.Sel("replaceCharactersInRange:withString:"),
                new ObjC.Range((nuint)replacement.DisplayStart, (nuint)replacement.DisplayDeleteLength),
                ObjC.String(replacement.DisplayInsert));
            ExactSource(replacement.After.Projection.Display, replacement.After.Stamp.Version);
            _sourceInstallation = replacement.After;
            _visibleText = replacement.After.Projection.Display;
            _sourceSemantics = null;
            SetSourceSelectionCore(replacement.After, false);
            ObjC.Send(clip, ObjC.Sel("scrollToPoint:"), origin);
            ObjC.Send(_editorScroll, ObjC.Sel("reflectScrolledClipView:"), clip);
            _sourceDrawTrace.ObserveDocument(replacement.After.Stamp.Generation, replacement.After.Stamp.Version);
            _sourceDrawStamp = replacement.After.Stamp;
            PublishSourceForeground();
            QueueSourceView();
            return ObserveSource();
        }
        catch (Exception error) { SetSourceUnavailable(error.Message); return null; }
        finally { _settingText = false; QueueSourceView(); }
    }

    /// <inheritdoc />
    public void SetSourceSelection(NativeSourceInstallation installation, bool reveal)
    {
        if (!SourceMatches(installation) || IsTextComposing) return;
        try { SetSourceSelectionCore(installation, reveal); QueueSourceView(); }
        catch (Exception error) { SetSourceUnavailable(error.Message); }
    }

    /// <summary>Records direction only for the endpoint explicitly installed by a canonical command.</summary>
    private void SetSourceSelectionCore(NativeSourceInstallation installation, bool reveal)
    {
        var start = installation.Projection.ToDisplay(Math.Min(installation.Anchor, installation.Active));
        var end = installation.Projection.ToDisplay(Math.Max(installation.Anchor, installation.Active));
        var active = installation.Projection.ToDisplay(installation.Active);
        var range = new ObjC.Range((nuint)start, (nuint)(end - start));
        _settingSelection = true;
        try
        {
            ObjC.Send(_editor, ObjC.Sel("setSelectedRange:"), range);
            if (ObjC.SendRange(_editor, ObjC.Sel("selectedRange")) != range)
                throw new InvalidOperationException("AppKit refused the canonical source selection.");
            _sourceSelectionWitness = new(start, end, active);
            if (reveal) ObjC.Send(_editor, ObjC.Sel("scrollRangeToVisible:"), range);
        }
        finally { _settingSelection = false; }
    }

    /// <inheritdoc />
    public void SetSourceSemantics(NativeSourceSemantics semantics)
    {
        if (_sourceInstallation is not { } installed || _sourceUnavailable || IsTextComposing ||
            installed.Stamp != semantics.Stamp || installed.Nonce != semantics.Nonce ||
            _sourceSemantics is { } previous && previous.PresentationSequence > semantics.PresentationSequence) return;
        _sourceSemantics = semantics;
        _sourceSemanticRevision++;
        PublishSourceForeground();
    }

    /// <inheritdoc />
    public void SetSourceChrome(string title, string status, bool modified, bool canUndo = false, bool canRedo = false)
    {
        _statusText = status;
        _sourceTitle = title;
        _sourceModified = modified;
        _sourceCanUndo = canUndo;
        _sourceCanRedo = canRedo;
        if (_window != 0)
        {
            ObjC.Send(_window, ObjC.Sel("setTitle:"), ObjC.String(title));
            ObjC.Send(_window, ObjC.Sel("setDocumentEdited:"), modified ? 1 : 0);
        }
        SetStatus(status);
    }

    /// <inheritdoc />
    public void SetSourceUnavailable(string reason, NativeSourceFailure failure = NativeSourceFailure.CanonicalRetained)
    {
        _sourceUnavailable = true;
        if (failure == NativeSourceFailure.UnadmittedNativeText) _sourceUnadmittedText = true;
        _sourceStyleSequence++;
        _sourceDrawStamp = null;
        _sourceSemantics = null;
        if (_editor != 0) ObjC.Send(_editor, ObjC.Sel("setEditable:"), 0);
        SetStatusNotice($"Source input unavailable: {reason}");
    }

    /// <summary>
    /// Discards only uncommitted native characters after distinct explicit consent. Cancellation
    /// retains the read-only native replica; canonical dirty edits/history/file are never discarded.
    /// </summary>
    private bool RecoverUnadmittedSource()
    {
        if (_sourceRecoveryPrompt || HasSourceMarkedText || !_sourceUnadmittedText) return false;
        var installation = _sourceInstallation;
        _sourceRecoveryPrompt = true;
        var alert = ObjC.New("NSAlert");
        try
        {
            ObjC.Send(alert, ObjC.Sel("setMessageText:"), ObjC.String("Native input could not be committed."));
            ObjC.Send(alert, ObjC.Sel("setInformativeText:"), ObjC.String(
                "Discard ONLY that uncommitted input and restore the canonical document? Unsaved canonical edits are retained."));
            // Cancellation is the default Return action, not implicit discard approval.
            ObjC.Send(alert, ObjC.Sel("addButtonWithTitle:"), ObjC.String("Cancel"));
            ObjC.Send(alert, ObjC.Sel("addButtonWithTitle:"), ObjC.String("Discard Uncommitted Input"));
            if (ObjC.Send(alert, ObjC.Sel("runModal")) != 1001 ||
                !ReferenceEquals(installation, _sourceInstallation) || !_sourceUnadmittedText) return false;
            return SourceRecoveryRequested?.Invoke() == true && !_sourceUnavailable && !_sourceUnadmittedText;
        }
        finally { ObjC.Send(alert, ObjC.Sel("release")); _sourceRecoveryPrompt = false; }
    }

    /// <summary>Compares the original controller identity, not a mutable selection or viewport sequence.</summary>
    private bool SourceMatches(NativeSourceInstallation installation) => _nativeSource && !_sourceUnavailable &&
        _editor != 0 && _sourceInstallation is { } current &&
        current.Stamp == installation.Stamp && current.Nonce == installation.Nonce &&
        ReferenceEquals(current.Snapshot, installation.Snapshot);

    /// <summary>Copies final NSString using its explicit UTF-16 length and rejects partial native replicas.</summary>
    private string ReadSource(long? version = null)
    {
        using var scope = MoteTelemetry.Start(TelemetryOperation.NativeSourceReadback,
            new(Version: version ?? _sourceInstallation?.Stamp.Version));
        try
        {
            var text = ObjC.ManagedString(ObjC.Send(_editor, ObjC.Sel("string")));
            scope?.SetStatus(TelemetryStatus.Success);
            return text;
        }
        catch { scope?.SetStatus(TelemetryStatus.Failure); throw; }
    }
    /// <summary>A failed complete readback is never advertised as editable success.</summary>
    private void ExactSource(string expected, long? version = null)
    {
        if (!string.Equals(ReadSource(version), expected, StringComparison.Ordinal))
            throw new InvalidOperationException("AppKit source readback does not match the complete canonical projection.");
    }

    /// <summary>Only a collapsed selection or an unchanged explicit command range certifies an active endpoint.</summary>
    private NativeSourceSelection ReadSourceSelection(int length)
    {
        var range = ObjC.SendRange(_editor, ObjC.Sel("selectedRange"));
        var selection = MacProductSourceModel.Selection(range.Location, range.Length, length, _sourceSelectionWitness);
        if (_sourceSelectionWitness is { } witness && (witness.Start != selection.Start || witness.End != selection.End))
            _sourceSelectionWitness = null;
        return selection;
    }

    /// <summary>Captures native UTF-16 range and actual visible layout, never a page-local source span.</summary>
    private NativeSourceObservation ObserveSource()
    {
        var display = ReadSource();
        return new(display, ReadSourceSelection(display.Length), SourceVisibleRange(display.Length));
    }

    /// <summary>
    /// TextKit 2 is inspected before any legacy-manager accessor. TextKit 1 converts only
    /// already-visible glyph layout to characters; no logical-line expansion or forced backend conversion.
    /// </summary>
    private TextSpan SourceVisibleRange(int length)
    {
        if (length == 0) return new(0, 0);
        var modern = SourceResponds(_editor, "textLayoutManager") ? ObjC.Send(_editor, ObjC.Sel("textLayoutManager")) : 0;
        if (modern != 0)
        {
            var viewport = ObjC.Send(modern, ObjC.Sel("textViewportLayoutController"));
            var range = ObjC.Send(viewport, ObjC.Sel("viewportRange"));
            if (range == 0) return UnknownSourceGeometry("Native viewport layout has not completed."); // No native viewport layout has completed yet.
            var manager = ObjC.Send(modern, ObjC.Sel("textContentManager"));
            var document = ObjC.Send(manager, ObjC.Sel("documentRange"));
            var origin = ObjC.Send(document, ObjC.Sel("location"));
            var start = ObjC.Send(manager, ObjC.Sel("offsetFromLocation:toLocation:"), origin,
                ObjC.Send(range, ObjC.Sel("location")));
            var end = ObjC.Send(manager, ObjC.Sel("offsetFromLocation:toLocation:"), origin,
                ObjC.Send(range, ObjC.Sel("endLocation")));
            if (start < 0 || end < start || end > length) throw new InvalidOperationException("Invalid TextKit 2 viewport range.");
            // TextKit 2 may report an entire enormous paragraph. That is not a qualified
            // visible-character witness; do not turn it into a whole-long-line style operation.
            if (end - start > MacProductSourceModel.MaximumVisibleCharacters)
                return UnknownSourceGeometry("TextKit 2 paragraph viewport needs fine-grained visible segment geometry.");
            return QualifiedSourceGeometry(new(checked((int)start), checked((int)(end - start))));
        }
        var legacy = ObjC.Send(_editor, ObjC.Sel("layoutManager"));
        var container = ObjC.Send(_editor, ObjC.Sel("textContainer"));
        var visible = MacOnScreenCanvasNative.GetRect(_editor, ObjC.Sel("visibleRect"));
        // Container coordinates exclude the NSTextView inset, not the scroll origin.
        var inset = SourceSize(_editor, ObjC.Sel("textContainerInset"));
        var bounds = new ObjC.Rect(visible.Origin.X - inset.Width, visible.Origin.Y - inset.Height,
            visible.Size.Width, visible.Size.Height);
        var glyphs = SourceGlyphRange(legacy, ObjC.Sel("glyphRangeForBoundingRectWithoutAdditionalLayout:inTextContainer:"), bounds, container);
        var characters = SourceCharacterRange(legacy, ObjC.Sel("characterRangeForGlyphRange:actualGlyphRange:"), glyphs, 0);
        if (characters.Location > (nuint)length || characters.Length > (nuint)length - characters.Location ||
            characters.Length > MacProductSourceModel.MaximumVisibleCharacters)
            return UnknownSourceGeometry("Native layout has not certified a bounded visible character range.");
        return QualifiedSourceGeometry(MacProductSourceModel.Visible(characters.Location, characters.Length, length));
    }

    /// <summary>Clears only this capability's pending geometry notice after actual native layout supplies a witness.</summary>
    private TextSpan QualifiedSourceGeometry(TextSpan range)
    {
        _sourceGeometryUnknown = false;
        if (_statusNotice?.StartsWith("Source decoration pending:", StringComparison.Ordinal) == true)
            SetStatusNotice(null);
        return range;
    }

    /// <summary>Reports unknown interest without inventing a visible span or disabling certified exact editing.</summary>
    private TextSpan UnknownSourceGeometry(string reason)
    {
        _sourceGeometryUnknown = true;
        SetStatusNotice($"Source decoration pending: {reason}");
        return new(0, 0);
    }

    /// <summary>Coalesces superseded selection/scroll observations into the existing UI-thread selection delivery.</summary>
    private void QueueSourceView()
    {
        if (_selectionDeliveryScheduled || _delegate == 0 || _settingText || _settingSelection) return;
        _selectionDeliveryScheduled = true;
        ObjC.Send(_delegate, ObjC.Sel("performSelector:withObject:afterDelay:"),
            ObjC.Sel("moteDeliverSelection:"), 0, 0d);
    }

    /// <summary>Never publishes a provisional native selection as committed source coordinates.</summary>
    private void PublishSourceView()
    {
        if (!_nativeSource || _sourceUnavailable || _settingText || IsTextComposing ||
            _sourceInstallation is not { } installation) return;
        var observed = ObserveSource();
        if (!string.Equals(observed.Display, installation.Projection.Display, StringComparison.Ordinal)) return;
        if (_sourceObservedView is { } previous && previous.Stamp == installation.Stamp &&
            previous.Nonce == installation.Nonce && previous.Selection == observed.Selection &&
            previous.VisibleDisplay == observed.VisibleDisplay) return;
        var current = new NativeSourceViewObservation(installation.Stamp, installation.Nonce, observed.Selection,
            observed.VisibleDisplay, ++_sourceViewportSequence);
        _sourceObservedView = current;
        SourceViewChanged?.Invoke(current);
        PublishSourceForeground();
    }

    /// <summary>Settles one arbitrary native ingress against its original installation; engine admission is synchronous.</summary>
    private void SourceTextDidChange()
    {
        if (_sourceUnavailable || _sourceInstallation is null) return;
        _sourceSelectionWitness = null;
        if (HasSourceMarkedText)
        {
            _compositionDirty = true;
            _sourceStyleSequence++;
            _sourceInstalledStyle = null;
            ObserveCompositionState();
            return;
        }
        _compositionDirty = false;
        EmitSourceCandidate();
        ObserveCompositionState();
    }

    /// <summary>Unchanged cancellation commits nothing; settlement keeps baseline frozen until controller acknowledgment.</summary>
    private void CommitSourceComposition()
    {
        if (!_compositionDirty || HasSourceMarkedText) return;
        _compositionDirty = false;
        EmitSourceCandidate();
    }

    /// <summary>No legacy TextChanged/SelectionChanged event is emitted from this profile.</summary>
    private void EmitSourceCandidate()
    {
        if (_sourceUnavailable || _sourceInstallation is not { } installation) return;
        var display = ReadSource();
        if (display.Contains('\0')) { SetSourceUnavailable("Embedded NUL is not supported by this source profile.", NativeSourceFailure.UnadmittedNativeText); return; }
        if (display != installation.Projection.Display)
        {
            _sourceStyleSequence++;
            _sourceDrawStamp = null;
            SourceCandidate?.Invoke(new(installation.Stamp, installation.Nonce, display, ReadSourceSelection(display.Length)));
            _compositionCommitRejected = _sourceUnavailable || _sourceInstallation?.Projection.Display != display;
            if (_compositionCommitRejected && !_sourceUnavailable)
                SetSourceUnavailable("Native source change was not acknowledged by the canonical controller.", NativeSourceFailure.UnadmittedNativeText);
        }
        else _compositionCommitRejected = false;
        QueueSourceView();
    }

    /// <summary>Publishes only actual native viewport colors, with at most 256 native runs per UI turn.</summary>
    private void PublishSourceForeground()
    {
        if (!_nativeSource || _sourceUnavailable || _editor == 0 || IsTextComposing ||
            _sourceInstallation is not { } installed || _theme is not { } theme) return;
        var visible = SourceVisibleRange(installed.Projection.Display.Length);
        var key = (installed.Stamp, installed.Nonce, visible, _sourceSemantics is null ? -1 : _sourceSemanticRevision, theme);
        if (_sourceInstalledStyle is { } applied && applied == key ||
            _sourcePendingStyle is { } pending && pending.Key == key && pending.Sequence == _sourceStyleSequence) return;
        var runs = MacProductSourceModel.Foreground(installed.Projection, visible, _sourceSemantics, theme);
        var sequence = ++_sourceStyleSequence;
        if (runs.Count == 0) { _sourceInstalledStyle = key; _sourcePendingStyle = null; return; }
        _sourcePendingStyle = (key, sequence);
        ApplyRuns(0);
        void ApplyRuns(int offset)
        {
            if (sequence != _sourceStyleSequence || !SourceMatches(installed) || IsTextComposing || _theme != theme) return;
            using var style = MoteTelemetry.Start(TelemetryOperation.NativeSourceStylePublish,
                new(Version: installed.Stamp.Version));
            try
            {
                var selection = ObjC.SendRange(_editor, ObjC.Sel("selectedRange"));
                var clip = ObjC.Send(_editorScroll, ObjC.Sel("contentView"));
                var origin = MacOnScreenCanvasNative.GetRect(clip, ObjC.Sel("bounds")).Origin;
                var storage = ObjC.Send(_editor, ObjC.Sel("textStorage"));
                var end = Math.Min(offset + 256, runs.Count);
                var settingText = _settingText;
                _settingText = true;
                ObjC.Send(storage, ObjC.Sel("beginEditing"));
                try
                {
                    for (var i = offset; i < end; i++)
                    {
                        var run = runs[i];
                        ObjC.Send(storage, ObjC.Sel("addAttribute:value:range:"), ObjC.String("NSColor"), Color(run.Color),
                            new ObjC.Range((nuint)run.Start, (nuint)run.Length));
                    }
                }
                finally { ObjC.Send(storage, ObjC.Sel("endEditing")); _settingText = settingText; }
                if (ObjC.SendRange(_editor, ObjC.Sel("selectedRange")) != selection)
                    throw new InvalidOperationException("Source attributes changed native selection.");
                ObjC.Send(clip, ObjC.Sel("scrollToPoint:"), origin);
                ObjC.Send(_editorScroll, ObjC.Sel("reflectScrolledClipView:"), clip);
                if (end == runs.Count && sequence == _sourceStyleSequence)
                {
                    _sourceInstalledStyle = key;
                    _sourcePendingStyle = null;
                }
                if (end < runs.Count) Post(() =>
                {
                    try { ApplyRuns(end); }
                    catch (Exception error) { SetSourceUnavailable(error.Message); }
                });
                style?.SetStatus(TelemetryStatus.Success);
            }
            catch { style?.SetStatus(TelemetryStatus.Failure); throw; }
        }
    }

    /// <summary>Owns notification observation on the exact source clip view, never a global event monitor.</summary>
    private void ObserveSourceScrolling()
    {
        var clip = ObjC.Send(_editorScroll, ObjC.Sel("contentView"));
        ObjC.Send(clip, ObjC.Sel("setPostsBoundsChangedNotifications:"), 1);
        var center = ObjC.Send(ObjC.Class("NSNotificationCenter"), ObjC.Sel("defaultCenter"));
        SourceAddObserver(center, ObjC.Sel("addObserver:selector:name:object:"), _delegate,
            ObjC.Sel("moteSourceBoundsChanged:"), ObjC.String("NSViewBoundsDidChangeNotification"), clip);
    }

    /// <summary>Removes the owned observation before shell teardown and delegate lifetime ends.</summary>
    private void RemoveSourceScrolling()
    {
        if (!_nativeSource || _editorScroll == 0 || _delegate == 0) return;
        var center = ObjC.Send(ObjC.Class("NSNotificationCenter"), ObjC.Sel("defaultCenter"));
        ObjC.Send(center, ObjC.Sel("removeObserver:name:object:"), _delegate,
            ObjC.String("NSViewBoundsDidChangeNotification"), ObjC.Send(_editorScroll, ObjC.Sel("contentView")));
    }

    /// <summary>Scroll/resize observations are queued after AppKit finishes its own layout transaction.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void SourceBoundsChanged(nint self, nint selector, nint notification)
    {
        try { s_current?.QueueSourceView(); }
        catch { /* No managed exception may cross the native notification boundary. */ }
    }

    /// <summary>
    /// Salvages selected unadmitted read-only native characters without settlement, engine
    /// copy, history or document mutation. True means this source-only command was handled;
    /// a failed native copy must not fall through to copying an unrelated canonical selection.
    /// </summary>
    private bool TryCopyUnadmittedSource(nint view, nint sender)
    {
        if (view != _editor || _editor == 0 || _window == 0 ||
            !MacProductSourceModel.CanCopyUnadmittedSource(_nativeSource, _sourceUnavailable,
                _sourceUnadmittedText, ObjC.Send(_window, ObjC.Sel("firstResponder")) == _editor)) return false;
        try
        {
            var superclass = new MacOnScreenCanvasNative.Super(_editor, ObjC.Class("NSTextView"));
            SendSuperEvent(ref superclass, ObjC.Sel("copy:"), sender);
        }
        catch (Exception error) { ShowError(error.Message); }
        return true;
    }

    /// <summary>Native context-menu Undo/Redo availability is exactly canonical history, never NSTextStorage undo.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte SourceValidateMenuItem(nint self, nint selector, nint item)
    {
        try
        {
            var shell = s_current;
            if (shell?._nativeSource == true && self == shell._editor)
            {
                var action = ObjC.Send(item, ObjC.Sel("action"));
                if (action == ObjC.Sel("undo:") || action == ObjC.Sel("moteUndo:"))
                    return (byte)(shell._sourceCanUndo && !shell.HasSourceMarkedText && !shell._sourceUnadmittedText ? 1 : 0);
                if (action == ObjC.Sel("redo:") || action == ObjC.Sel("moteRedo:"))
                    return (byte)(shell._sourceCanRedo && !shell.HasSourceMarkedText && !shell._sourceUnadmittedText ? 1 : 0);
            }
            var superclass = new MacOnScreenCanvasNative.Super(self, ObjC.Class("NSTextView"));
            return SourceValidateSuper(ref superclass, selector, item);
        }
        catch { return 0; }
    }

    /// <summary>Context-menu and responder history are engine commands, not native NSTextStorage undo.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void SourceResponderUndo(nint self, nint selector, nint sender)
    { var shell = s_current; shell?.NotifyAfterComposition(shell.UndoRequested); }
    /// <summary>Routes the actual responder selector, not only the keyboard shortcut.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void SourceResponderRedo(nint self, nint selector, nint sender)
    { var shell = s_current; shell?.NotifyAfterComposition(shell.RedoRequested); }

    /// <summary>BOOL-returning selector availability uses the actual byte return ABI.</summary>
    private static bool SourceResponds(nint receiver, string selector) =>
        SourceRespondsNative(receiver, ObjC.Sel("respondsToSelector:"), ObjC.Sel(selector)) != 0;
    /// <summary>Four object/pointer arguments use the native notification registration ABI.</summary>
    [DllImport(ObjcRuntime, EntryPoint = "objc_msgSend")]
    private static extern void SourceAddObserver(nint receiver, nint selector, nint observer, nint callback, nint name, nint source);
    [DllImport(ObjcRuntime, EntryPoint = "objc_msgSend")]
    private static extern byte SourceRespondsNative(nint receiver, nint selector, nint argument);
    /// <summary>BOOL without arguments uses a byte return rather than a pointer-sized result.</summary>
    [DllImport(ObjcRuntime, EntryPoint = "objc_msgSend")]
    private static extern byte SourceBool(nint receiver, nint selector);
    /// <summary>Preserves superclass validation for all selectors outside canonical history commands.</summary>
    [DllImport(ObjcRuntime, EntryPoint = "objc_msgSendSuper")]
    private static extern byte SourceValidateSuper(ref MacOnScreenCanvasNative.Super receiver, nint selector, nint item);
    /// <summary>Range then object uses the replacement method's distinct aggregate argument order.</summary>
    [DllImport(ObjcRuntime, EntryPoint = "objc_msgSend")]
    private static extern void ReplaceSourceCharacters(nint receiver, nint selector, ObjC.Range range, nint value);
    /// <summary>CGSize is a two-CGFloat return aggregate on both target ABIs.</summary>
    [DllImport(ObjcRuntime, EntryPoint = "objc_msgSend")]
    private static extern ObjC.Size SourceSize(nint receiver, nint selector);
    /// <summary>Returns the already-laid-out glyph range for one native viewport rectangle.</summary>
    [DllImport(ObjcRuntime, EntryPoint = "objc_msgSend")]
    private static extern ObjC.Range SourceGlyphRange(nint receiver, nint selector, ObjC.Rect bounds, nint container);
    /// <summary>Converts native glyph coordinates to native UTF-16 characters; optional output pointer is null.</summary>
    [DllImport(ObjcRuntime, EntryPoint = "objc_msgSend")]
    private static extern ObjC.Range SourceCharacterRange(nint receiver, nint selector, ObjC.Range glyphs, nint actualGlyphRange);
}
