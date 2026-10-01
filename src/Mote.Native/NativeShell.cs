using Mote.Formats;
using Mote.Themes;
using Mote.Telemetry;

namespace Mote.Native;

/// <summary>How the operating-system text control represents source line endings.</summary>
internal enum NativeLineEndingMode
{
    /// <summary>The control retains source CR, LF, and CRLF sequences.</summary>
    Preserve,
    /// <summary>The control projects every source line ending as CRLF.</summary>
    CrLf
}

/// <summary>Closed persistence commands sharing one admission and lifetime contract.</summary>
internal enum NativeSaveKind { Save, SaveAs }

/// <summary>Optional OS document-open delivery, independent of the application's modal file picker.</summary>
internal interface INativeExternalOpenShell
{
    /// <summary>
    /// Requests one path through canonical pending-input and discard admission.
    /// True means admitted for asynchronous open, not proof that I/O succeeded.
    /// The adapter must reject multi-file delivery rather than silently replace documents.
    /// </summary>
    event Func<string, bool>? ExternalOpenRequested;
}

/// <summary>Target-owned receipt; no native pointer, path, input text, or document is retained.</summary>
internal readonly record struct NativeSaveRequest(NativeSaveKind Kind, TelemetryRequest? Trace)
{
    /// <summary>Begins at the native command callback, not at physical key delivery.</summary>
    internal static NativeSaveRequest Receive(NativeSaveKind kind) => new(kind,
        MoteTelemetry.BeginRequest(kind == NativeSaveKind.Save
            ? TelemetryOperation.CommandSave : TelemetryOperation.CommandSaveAs));

    /// <summary>Dispatches once or truthfully ends an unhandled receipt without inventing admission.</summary>
    internal void Dispatch(Action<NativeSaveRequest>? callback)
    {
        if (callback is null)
        {
            Trace?.EndOnce(TelemetryStatus.Skipped, TelemetryReason.MissingHandler);
            return;
        }
        try { callback(this); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Trace?.EndOnce(TelemetryStatus.Failure, TelemetryReason.CallbackFailed);
            throw;
        }
    }

    /// <summary>Contains nonfatal Save receipt/handler failures before returning to an unmanaged command dispatcher.</summary>
    internal static bool DispatchContained(NativeSaveKind kind, Action<NativeSaveRequest>? callback)
    {
        try { Receive(kind).Dispatch(callback); return true; }
        catch (Exception error) when (error is not OutOfMemoryException) { return false; }
    }
}

/// <summary>
/// Identifies one immutable document version across native text and semantic
/// presentation. A new document may reuse a version number but never a generation.
/// </summary>
internal readonly record struct NativeDocumentStamp(long Generation, long Version);

/// <summary>
/// Identifies the exact installed presentation, including same-version viewport,
/// policy and theme changes. A document stamp alone cannot identify a render map.
/// </summary>
internal readonly record struct NativePresentationId(NativeDocumentStamp Document, long Sequence);

/// <summary>One bounded, editable projection of the canonical engine document.</summary>
internal sealed record NativeDocumentView(
    string Title,
    string Text,
    int PageStart,
    int TotalLength,
    bool IsModified,
    string Status,
    NativeDocumentStamp Stamp,
    int? FocusDisplayOffset = null);

/// <summary>A styled preview range mapped to its originating document source span.</summary>
internal sealed record NativePreviewSpan(
    int Start,
    int Length,
    string Kind,
    TextSpan SourceSpan,
    bool Emphasis = false,
    bool Navigable = true);

/// <summary>
/// A user activation in the already displayed, read-only native preview.
/// PreviewOffset is a UTF-16 text offset; it is not a source offset. The
/// controller resolves it only against the exact presented analysis stamp.
/// </summary>
internal readonly record struct NativePreviewActivation(
    NativeDocumentStamp Stamp, int PreviewOffset, long PresentationSequence = 0)
{
    /// <summary>Identity retained by the native control after a successful install.</summary>
    public NativePresentationId Identity => new(Stamp, PresentationSequence);
}

/// <summary>Version-matched semantic presentation for the current native text page.</summary>
internal sealed record NativeAnalysisView(
    IReadOnlyList<SemanticToken> Tokens,
    string DiagnosticsSummary,
    string PreviewText,
    string Status,
    NativeDocumentStamp Stamp,
    IReadOnlyList<NativePreviewSpan>? PreviewSpans = null,
    long PresentationSequence = 0,
    FlowRenderProjection? Flow = null,
    bool ShowPreview = true,
    GridRenderProjection? Grid = null,
    NativeGridScrollFrame? GridNavigation = null)
{
    /// <summary>Identity of this immutable text, style and source-map bundle.</summary>
    public NativePresentationId Identity => new(Stamp, PresentationSequence);
}

/// <summary>Cancelable native window close notification.</summary>
internal sealed class NativeClosingEventArgs : EventArgs
{
    /// <summary>Set when the unsaved-document prompt rejects closing.</summary>
    public bool Cancel { get; set; }
}

/// <summary>
/// Native preedit began after a controller palette preflight; no palette was
/// applied, and the same policy may be retried after CompositionSettled.
/// </summary>
internal sealed class NativeThemeDeferredException : InvalidOperationException
{
    /// <summary>Creates the internal, non-user-facing composition signal.</summary>
    internal NativeThemeDeferredException() : base("Native text composition is active.") { }
}

/// <summary>
/// The OS-native editor adapter. It owns only view state and user input; the engine
/// remains the sole owner of document text, file identity, and undo history.
/// </summary>
internal interface INativeEditorShell
{
    /// <summary>Line-ending projection used by the platform text control.</summary>
    NativeLineEndingMode LineEndingMode { get; }
    /// <summary>The current OS application appearance, used for the system theme preference.</summary>
    bool PrefersDark { get; }
    /// <summary>Whether the OS owns uncommitted text composition in either editor mode.</summary>
    bool IsTextComposing { get; }

    /// <summary>
    /// Raised on the UI thread after the effective OS appearance may have changed.
    /// The controller re-queries PrefersDark; the event carries no cached palette.
    /// </summary>
    event Action? AppearanceChanged;
    /// <summary>
    /// Raised on the UI thread after an IME commit or cancellation has finished
    /// its final native edit callback, never while marked/preedit text remains.
    /// </summary>
    event Action? CompositionSettled;

    /// <summary>Raised after a user edit with the complete bounded page text.</summary>
    event Action<string>? TextChanged;
    /// <summary>Raised for user-driven selection changes in visible page display offsets.</summary>
    event Action<int, int>? SelectionChanged;
    /// <summary>Raised for pointer or keyboard activation of displayed preview text.</summary>
    event Action<NativePreviewActivation>? PreviewActivated;
    /// <summary>Raises an identity-bound table selection or explicit source-backed command.</summary>
    event Action<NativeGridIntent>? GridIntentRequested { add { } remove { } }
    /// <summary>Raises bounded logical table interests; callbacks never parse source.</summary>
    event Action<NativeGridWindowRequest>? GridWindowRequested { add { } remove { } }
    /// <summary>Admits a fresh immutable gesture before any coordinate phase is dispatched.</summary>
    event Func<NativeGridGestureBegin, NativeGridGesture?>? GridGestureBeginning { add { } remove { } }
    /// <summary>Raises captured token phases; navigation never authorizes Copy or Reveal.</summary>
    event Action<NativeGridGestureAction>? GridGestureRequested { add { } remove { } }
    /// <summary>Retires coordinate gestures when native clipped geometry changes.</summary>
    event Action<int, int>? GridGeometryChanged { add { } remove { } }
    /// <summary>Raised by the New command.</summary>
    event Action? NewRequested;
    /// <summary>Raised by the Open command.</summary>
    event Action? OpenRequested;
    /// <summary>Raised once from Save or Save As receipt; shared admission settles any pending native text.</summary>
    event Action<NativeSaveRequest>? SaveRequested;
    /// <summary>Raised by the Undo command.</summary>
    event Action? UndoRequested;
    /// <summary>Raised by the Redo command.</summary>
    event Action? RedoRequested;
    /// <summary>Raised by the Format command.</summary>
    event Action? FormatRequested;
    /// <summary>Raised by the explicit Reload Settings action; never commits native preedit.</summary>
    event Action? ReloadSettingsRequested { add { } remove { } }
    /// <summary>Raised by previous-page navigation for large documents.</summary>
    event Action? PagePreviousRequested;
    /// <summary>Raised by next-page navigation for large documents.</summary>
    event Action? PageNextRequested;
    /// <summary>Raised when the user requests a new global search query.</summary>
    event Action? FindRequested;
    /// <summary>Raised when the user repeats the previous global search.</summary>
    event Action? FindNextRequested;
    /// <summary>Raised when the user requests global line navigation.</summary>
    event Action? GoToLineRequested;
    /// <summary>Raised by global Select All, never the page-local native default.</summary>
    event Action? SelectAllRequested;
    /// <summary>Raised by global Copy, never the page-local native default.</summary>
    event Action? CopyRequested;
    /// <summary>Raised by global Cut, never the page-local native default.</summary>
    event Action? CutRequested;
    /// <summary>Raised before the window closes and may veto it.</summary>
    event EventHandler<NativeClosingEventArgs>? ClosingRequested;
    /// <summary>Raised after the native window has become visible and editable.</summary>
    event Action? Shown;

    /// <summary>Starts the platform event loop on the calling thread.</summary>
    void Run();
    /// <summary>Replaces the editor's bounded text page after an engine transition.</summary>
    void SetDocument(NativeDocumentView view);
    /// <summary>
    /// Arms an opt-in source draw interval before this exact revision is installed.
    /// Preview/chrome paint is not an endpoint; unsupported diagnostic shells are inert.
    /// </summary>
    void TraceSourceDraw(NativeDocumentStamp stamp, TelemetryMark mark,
        TelemetryDimensions dimensions, TelemetryOperation operation = TelemetryOperation.EditToDrawSubmission) { }
    /// <summary>Cancels a pending source draw when the controller closes.</summary>
    void CancelSourceDrawTrace() { }
    /// <summary>Updates semantic decoration without altering text or selection.</summary>
    void SetAnalysis(NativeAnalysisView view);
    /// <summary>Installs bounded pending navigation without relabeling old ready cells.</summary>
    void SetGridNavigation(NativeGridScrollFrame? frame) { }
    /// <summary>
    /// Applies one policy to all platform controls before returning; it must
    /// never silently defer a subset. Throws NativeThemeDeferredException
    /// before any mutation if native preedit starts after controller preflight.
    /// </summary>
    void SetTheme(IThemePolicy theme);
    /// <summary>
    /// Sets or clears a persistent, nonmodal status notice without touching
    /// document text, selection, native undo, input composition, or layout.
    /// The notice remains visible when ordinary status text is refreshed.
    /// </summary>
    void SetStatusNotice(string? notice);
    /// <summary>
    /// Settles native IME preedit into a synchronous TextChanged callback before a command
    /// may replace the document or persist its bytes; false vetoes that command.
    /// </summary>
    bool CommitPendingText();
    /// <summary>Projects a global source selection into visible display positions.</summary>
    void SetSelection(int displayAnchor, int displayActive);
    /// <summary>Focuses the source editor after an accepted preview navigation.</summary>
    void FocusSource();
    /// <summary>Prompts for a search term; null means cancel.</summary>
    string? PromptFind();
    /// <summary>Edits a temporary decoded value; null cancels without any Engine mutation.</summary>
    string? PromptGridReplacement(string currentValue) => null;
    /// <summary>Prompts for a one-based document line number; null means cancel.</summary>
    int? PromptGoToLine();
    /// <summary>Places an exact selected source string on the OS clipboard.</summary>
    void SetClipboardText(string text);
    /// <summary>Returns a selected absolute local path, or null on cancel.</summary>
    string? PickOpenFile();
    /// <summary>Returns a selected absolute target path, or null on cancel.</summary>
    string? PickSaveFile(string? currentPath);
    /// <summary>Explicitly approves replacing a captured, existing Save As target.</summary>
    bool ConfirmOverwrite(string path);
    /// <summary>Asks whether the user permits discarding unsaved edits.</summary>
    bool ConfirmDiscard();
    /// <summary>Shows a recoverable document or I/O error.</summary>
    void ShowError(string message);
    /// <summary>Schedules work on the OS UI thread from a background completion.</summary>
    void Post(Action action);
    /// <summary>Requests orderly native window closure on the UI thread.</summary>
    void Close();
}
