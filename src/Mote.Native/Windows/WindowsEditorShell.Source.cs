using System.Runtime.InteropServices;
using Mote.Engine;
using Mote.Formats;
using Mote.Themes;
using Mote.Telemetry;

namespace Mote.Native.Windows;

/// <summary>Window-lifetime full source adapter. Canonical text and history remain controller-owned.</summary>
internal sealed partial class WindowsEditorShell
{
    /// <summary>Explicit profile flag; never selected by document size.</summary>
    private readonly bool _nativeSource;
    /// <summary>Only the certified replica identity may admit native callbacks.</summary>
    private NativeSourceInstallation? _sourceInstallation;
    /// <summary>Latest canonical facts, revocable when the installed text identity changes.</summary>
    private NativeSourceSemantics? _sourceSemantics;
    /// <summary>Coalesced visible attribute work; at most 128 native calls per UI turn.</summary>
    private readonly Queue<(int Start, int End, uint Color)> _sourceStyles = new();
    private long _sourceViewportSequence;
    /// <summary>A failed settlement cannot authorize a command using an uncommitted native replica.</summary>
    private bool _sourceCandidateFailed;
    /// <summary>Preserves the controller's typed failure instead of inventing whether a canonical commit occurred.</summary>
    private bool _sourceFailureReported;
    /// <summary>Modal confirmation is bound to one failure epoch, never a later posted document transition.</summary>
    private long _sourceRecoveryEpoch;
    /// <summary>Nested command dispatch during an owned recovery modal cannot open another prompt.</summary>
    private bool _sourceRecoveryPrompt;
    /// <summary>Controller-supplied failure classification retained for explicit recovery consent.</summary>
    private NativeSourceFailure _sourceFailure;
    private const nuint SourceStyleTimer = 2;
    private const uint SourceCandidateMessage = Win32.WM_APP + 3;
    /// <summary>Only one final native readback request is queued, never complete strings.</summary>
    private bool _sourceCandidatePostQueued;

    /// <summary>Actual thread-local native focus, not cached document or pane state.</summary>
    [LibraryImport("user32.dll", EntryPoint = "GetFocus")]
    private static partial nint SourceFocusedWindow();

    /// <inheritdoc />
    public bool NativeSourceEnabled => _nativeSource;
    /// <inheritdoc />
    public bool HasSourceMarkedText => _imeComposing;
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
        if (installation.Projection.Display.Contains('\0')) { SetSourceUnavailable("Embedded NUL cannot be imported exactly."); return null; }
        _settingText = true;
        try
        {
            _sourceMapInstalled = false;
            Win32.SendMessageW(_editor, Win32.EM_SETREADONLY, 0, 0);
            // EM_SETTEXTEX auto-detects a leading RTF header. Source is always
            // literal text, including a document that happens to start with it.
            if (!Win32.SetWindowTextW(_editor, installation.Projection.Display))
                throw new InvalidOperationException("Literal native source installation failed.");
            if (!CertifySource(installation)) return null;
            SetSourceSelection(installation, false);
            ScheduleSourceStyle();
            return ObserveSource();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { SetSourceUnavailable("Exact native source installation failed."); return null; }
        finally { _settingText = false; }
    }

    /// <summary>Measures only actual native readback; source size is omitted rather than mislabeled as encoded bytes.</summary>
    private string ReadProductSource(long? version = null)
    {
        using var scope = MoteTelemetry.Start(TelemetryOperation.NativeSourceReadback,
            new TelemetryDimensions(Version: version ?? _sourceInstallation?.Stamp.Version));
        scope?.SetStatus(TelemetryStatus.Failure);
        var text = WindowsNativeSourceSafety.Read(_editor);
        scope?.SetStatus(TelemetryStatus.Success);
        return text;
    }

    /// <summary>Certifies exact readback before granting offset or input authority.</summary>
    private bool CertifySource(NativeSourceInstallation installation)
    {
        if (!string.Equals(ReadProductSource(installation.Stamp.Version), installation.Projection.Display, StringComparison.Ordinal))
        { SetSourceUnavailable("Native source readback differs from canonical text."); return false; }
        _sourceCandidateFailed = false;
        _sourceFailure = NativeSourceFailure.CanonicalRetained;
        _sourceRecoveryEpoch++;
        if (_sourceInstallation?.Stamp != installation.Stamp) _sourceSemantics = null;
        _sourceDrawTrace.ObserveDocument(installation.Stamp.Generation, installation.Stamp.Version);
        _sourceInstallation = installation;
        _visibleText = installation.Projection.Display;
        _editorOffsets = new RichEditOffsetMap(_visibleText);
        _sourceMapInstalled = true;
        _sourceInputReadOnly = false;
        _sourceHostNotice = null;
        _sourceDrawStamp = installation.Stamp;
        ConfigureSourceHistory();
        Win32.SendMessageW(_editor, Win32.EM_SETREADONLY, 0, 0);
        return true;
    }

    /// <inheritdoc />
    public bool AcknowledgeSource(NativeSourceInstallation installation)
    {
        if (!_nativeSource || _editor == 0 || _imeComposing || installation.Projection.Display.Contains('\0')) return false;
        try { var accepted = CertifySource(installation); if (accepted) { ScheduleSourceStyle(); QueueSourceView(); } return accepted; }
        catch (Exception error) when (error is not OutOfMemoryException)
        { SetSourceUnavailable("Native edit acknowledgement failed."); return false; }
    }

    /// <inheritdoc />
    public NativeSourceObservation? ApplySourceChange(NativeSourceReplacement replacement)
    {
        if (IsTextComposing || !SourceIdentity(replacement.Before) || _sourceInputReadOnly) return null;
        if (replacement.DisplayInsert.Contains('\0')) { SetSourceUnavailable("Embedded NUL replacement is unsupported."); return null; }
        _settingText = true;
        try
        {
            if (!string.Equals(ReadProductSource(), _visibleText, StringComparison.Ordinal))
                throw new InvalidOperationException("The guarded native baseline changed.");
            SetSelection(replacement.DisplayStart, checked(replacement.DisplayStart + replacement.DisplayDeleteLength));
            Win32.SendMessageW(_editor, 0x00C2, 0, replacement.DisplayInsert);
            if (!CertifySource(replacement.After)) return null;
            SetSourceSelection(replacement.After, false);
            ScheduleSourceStyle();
            return ObserveSource();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { SetSourceUnavailable("Native range publication failed; Engine text remains authoritative."); return null; }
        finally { _settingText = false; }
    }

    /// <inheritdoc />
    public void SetSourceSelection(NativeSourceInstallation installation, bool reveal)
    {
        if (!SourceIdentity(installation) || IsTextComposing) return;
        SetSelection(installation.Projection.ToDisplay(installation.Anchor), installation.Projection.ToDisplay(installation.Active));
        if (reveal) Win32.SendMessageW(_editor, 0x00B7, 0, 0);
        QueueSourceView();
    }

    /// <inheritdoc />
    public void SetSourceSemantics(NativeSourceSemantics semantics)
    {
        if (_sourceInstallation is not { } installed || installed.Stamp != semantics.Stamp || installed.Nonce != semantics.Nonce) return;
        _sourceSemantics = semantics;
        ScheduleSourceStyle();
    }

    /// <inheritdoc />
    public void SetSourceChrome(string title, string status, bool modified, bool canUndo = false, bool canRedo = false)
    {
        if (_window != 0) Win32.SetWindowTextW(_window, title + (modified ? " *" : ""));
        UpdateStatus(status);
    }

    /// <inheritdoc />
    public void SetSourceUnavailable(string reason, NativeSourceFailure failure = NativeSourceFailure.CanonicalRetained)
    {
        _sourceFailureReported = true;
        _sourceFailure = failure;
        _sourceRecoveryEpoch++;
        _sourceCandidateFailed = failure == NativeSourceFailure.UnadmittedNativeText;
        _sourceInputReadOnly = true;
        _sourceMapInstalled = false;
        _sourceInstallation = null;
        _sourceDrawStamp = null;
        _sourceStyles.Clear();
        _sourceHostNotice = reason;
        if (_editor != 0) Win32.SendMessageW(_editor, Win32.EM_SETREADONLY, 1, 0);
        RenderStatus();
    }

    /// <summary>Only explicit owned confirmation discards an unadmitted replica; canonical dirty edits and history survive.</summary>
    private bool RecoverUnadmittedSource()
    {
        if (_sourceRecoveryPrompt) return false;
        if (!_sourceCandidateFailed) return true;
        if (_imeComposing || _window == 0) return false;
        _imeSettling = false; // Actual marked text has ended; this state is unavailable, not a pending native composition.
        var epoch = _sourceRecoveryEpoch;
        var failure = _sourceFailure;
        var installation = _sourceInstallation;
        var owner = _window;
        _sourceRecoveryPrompt = true;
        try
        {
            if (Win32.MessageBoxW(owner,
                "Native input could not be committed. Discard ONLY that uncommitted input and restore the canonical document? Unsaved canonical edits are retained.",
                "mote", Win32.MB_YESNOCANCEL | Win32.MB_ICONQUESTION) != Win32.IDYES) return false;
            // MessageBox pumps posted completion work. Consent never transfers to
            // another document, a newer failure or an already replaced source.
            if (_sourceRecoveryEpoch != epoch || _sourceFailure != failure ||
                !_sourceCandidateFailed || !_sourceInputReadOnly || _sourceMapInstalled ||
                !ReferenceEquals(_sourceInstallation, installation) || _window != owner || _imeComposing) return false;
            return SourceRecoveryRequested?.Invoke() == true && !_sourceCandidateFailed &&
                _sourceMapInstalled && !_sourceInputReadOnly && _sourceInstallation is not null;
        }
        catch (Exception error) when (error is not OutOfMemoryException) { return false; }
        finally { _sourceRecoveryPrompt = false; }
    }

    /// <summary>Compares the original installation identity, not mutable native text.</summary>
    private bool SourceIdentity(NativeSourceInstallation installation) => _sourceInstallation is { } current &&
        current.Stamp == installation.Stamp && current.Nonce == installation.Nonce;

    /// <summary>Reads sorted native endpoints; only a collapsed selection certifies its active endpoint.</summary>
    private NativeSourceSelection SourceSelection(string display)
    {
        var native = new Win32.CharacterRange();
        Win32.SendMessageW(_editor, Win32.EM_EXGETSEL, 0, ref native);
        var map = new RichEditOffsetMap(display);
        var start = map.ToDisplay(native.Min);
        var end = map.ToDisplay(native.Max);
        return new NativeSourceSelection(start, end, start == end ? start : null);
    }

    /// <summary>Actual corner hit testing yields a bounded display interest, never a complete logical line.</summary>
    private TextSpan SourceVisible()
    {
        if (!Win32.GetClientRect(_editor, out var rect)) return new TextSpan(0, 0);
        var top = new Win32.Point { X = 0, Y = 0 };
        var bottom = new Win32.Point { X = Math.Max(0, rect.Right - 1), Y = Math.Max(0, rect.Bottom - 1) };
        var a = (int)Win32.SendMessageW(_editor, 0x00D7, 0, ref top);
        var b = (int)Win32.SendMessageW(_editor, 0x00D7, 0, ref bottom);
        var nativeLength = _visibleText.Length - _editorOffsets.NewlineCount;
        a = Math.Clamp(a, 0, nativeLength); b = Math.Clamp(b, a, nativeLength);
        var start = _editorOffsets.ToDisplay(a);
        var end = _editorOffsets.ToDisplay(Math.Min(nativeLength, b + 1));
        return WindowsSourceForegroundPlan.Trim(_visibleText, new TextSpan(start, Math.Min(8192, end - start)));
    }

    /// <summary>Observation is taken from the currently certified replica only.</summary>
    private NativeSourceObservation ObserveSource() => new(_visibleText, SourceSelection(_visibleText), SourceVisible());

    /// <summary>Defers readback until RichEdit finishes the input message and final native selection.</summary>
    private void QueueSourceCandidate()
    {
        if (_sourceCandidatePostQueued || _window == 0) return;
        _sourceCandidatePostQueued = true;
        if (!Win32.PostMessageW(_window, SourceCandidateMessage, 0, 0))
        { _sourceCandidatePostQueued = false; SetSourceUnavailable("Native input settlement could not be scheduled.", NativeSourceFailure.UnadmittedNativeText); }
    }

    /// <summary>Posts final viewport observations after controller programmatic-selection admission unwinds.</summary>
    private void QueueSourceView()
    {
        _pendingSelection = true;
        if (_selectionPostQueued || _window == 0) return;
        _selectionPostQueued = true;
        if (!Win32.PostMessageW(_window, SelectionMessage, 0, 0)) _selectionPostQueued = false;
    }

    /// <summary>Publishes one final native string against its frozen original identity.</summary>
    private bool ReadSourceCandidate()
    {
        if (_imeComposing) return false;
        if (_sourceInstallation is not { } original || _sourceInputReadOnly) return !_sourceCandidateFailed;
        try
        {
            var display = ReadProductSource();
            if (string.Equals(display, _visibleText, StringComparison.Ordinal)) return !_sourceCandidateFailed;
            _sourceFailureReported = false;
            SourceCandidate?.Invoke(new NativeSourceCandidate(original.Stamp, original.Nonce, display, SourceSelection(display)));
            // Emitting a candidate is not admission. A synchronous exact acknowledgement
            // must advance this same document/nonce before Save or history may proceed.
            if (WindowsSourceSettlement.Acknowledged(original, _sourceInstallation, display,
                _visibleText, _sourceMapInstalled, _sourceInputReadOnly)) return true;
            if (!_sourceFailureReported)
                SetSourceUnavailable("Settled native input was not acknowledged; canonical document retained.",
                    NativeSourceFailure.UnadmittedNativeText);
            return false;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { SetSourceUnavailable("Native candidate readback or admission failed.", NativeSourceFailure.UnadmittedNativeText); return false; }
    }

    /// <summary>Selection and viewport publication never expose provisional composition coordinates.</summary>
    private void PublishSourceView()
    {
        // EN_SELCHANGE may precede EN_CHANGE, and its posted observation may
        // precede our candidate message. Until admission, native endpoints refer
        // to different text and must not be mapped through the certified replica.
        if (_sourceInstallation is not { } installed || IsTextComposing || _settingText ||
            _settingSelection || _sourceCandidatePostQueued) return;
        SourceViewChanged?.Invoke(new(installed.Stamp, installed.Nonce, SourceSelection(_visibleText), SourceVisible(), ++_sourceViewportSequence));
        ScheduleSourceStyle();
    }

    /// <summary>Disables RichEdit-owned undo permanently for this profile.</summary>
    private void ConfigureSourceHistory()
    {
        Win32.SendMessageW(_editor, 0x0400 + 82, 0, 0);
        Win32.SendMessageW(_editor, 0x00CD, 0, 0);
    }

    /// <summary>Arbitrary native and contextual keyboard history goes through canonical engine commands.</summary>
    private bool InterceptSourceHistory(uint message, nuint key)
    {
        var undo = message is 0x00C7 or 0x0304;
        var redo = message == 0x0400 + 84;
        if (message == Win32.WM_KEYDOWN && Win32.GetKeyState(0x11) < 0)
        { undo |= key == 'Z' && Win32.GetKeyState(0x10) >= 0; redo |= key == 'Y' || key == 'Z' && Win32.GetKeyState(0x10) < 0; }
        if (!undo && !redo) return false;
        if (CommitPendingText()) { if (redo) RedoRequested?.Invoke(); else UndoRequested?.Invoke(); }
        return true;
    }

    /// <summary>Replaces superseded decoration work with current visible, identity-qualified attributes.</summary>
    private void ScheduleSourceStyle()
    {
        _sourceStyles.Clear();
        if (_sourceInstallation is not { } installed || IsTextComposing || _editor == 0) return;
        var visible = SourceVisible();
        var styles = new List<WindowsSourceForegroundPlan.Run>();
        if (_sourceSemantics is { } facts && facts.Stamp == installed.Stamp && facts.Nonce == installed.Nonce)
        {
            foreach (var token in facts.Tokens)
            {
                var start = installed.Projection.ToDisplay(token.Span.Start);
                var end = installed.Projection.ToDisplay(token.Span.End);
                if (start < visible.End && end > visible.Start)
                    styles.Add(new(start, end, ColorRef(_theme.SemanticColor(token.Kind))));
            }
        }
        foreach (var run in WindowsSourceForegroundPlan.Build(_visibleText, visible,
            ColorRef(_theme.Palette.EditorForeground), styles))
        {
            var a = _editorOffsets.ToNative(run.Start); var b = _editorOffsets.ToNative(run.End);
            if (b > a) _sourceStyles.Enqueue((a, b, run.Color));
        }
        if (_sourceStyles.Count != 0 && _window != 0) Win32.SetTimer(_window, SourceStyleTimer, 1, 0);
    }

    /// <summary>Detached TOM foreground never changes selection, source text or native keyboard attributes.</summary>
    private void ApplySourceStyleTurn()
    {
        if (IsTextComposing || _sourceInputReadOnly || _sourceStyles.Count == 0) return;
        using var scope = MoteTelemetry.Start(TelemetryOperation.NativeSourceStylePublish,
            new TelemetryDimensions(Version: _sourceInstallation?.Stamp.Version, Count: Math.Min(128, _sourceStyles.Count)));
        scope?.SetStatus(TelemetryStatus.Failure);
        _settingText = true;
        try
        {
            using var undo = _editorUndo.Suspend(_editor);
            using var range = new WindowsRichEditForegroundRange(_editor);
            for (var i = 0; i < 128 && _sourceStyles.TryDequeue(out var style); i++) range.Apply(style.Start, style.End, style.Color);
            if (_sourceStyles.Count != 0) Win32.SetTimer(_window, SourceStyleTimer, 1, 0);
            scope?.SetStatus(TelemetryStatus.Success);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { scope?.SetStatus(TelemetryStatus.Failure); _sourceStyles.Clear(); _sourceHostNotice = "Visible semantic decoration is unavailable."; RenderStatus(); }
        finally { _settingText = false; }
    }
}
