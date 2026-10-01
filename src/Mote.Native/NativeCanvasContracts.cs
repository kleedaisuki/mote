using Mote.Engine;
using Mote.Formats;
using Mote.Native.Accessibility;
using Mote.Native.Viewport;

namespace Mote.Native;

/// <summary>
/// One final, source-coordinate input-host edit. Generation, snapshot version,
/// and island binding nonce together prevent late composition notifications
/// from mutating a replaced document or a rebased text window.
/// </summary>
internal readonly record struct CanvasCommittedEdit(
    long DocumentGeneration,
    long BaseVersion,
    long BindingNonce,
    TextChange Change,
    int ActiveSourceOffset);

/// <summary>
/// A platform-shaped horizontal target, expressed in canonical UTF-16 source
/// coordinates rather than an unbounded whole-line pixel width.
/// </summary>
internal readonly record struct CanvasHorizontalAnchorRequest(
    long DocumentGeneration,
    long BaseVersion,
    int SourceBoundary,
    HorizontalCaretAffinity Affinity,
    double IntraClusterPixels);

/// <summary>
/// Exact local caret geometry for one version-matched canvas frame. A source
/// window alone is not proof that the caret lies inside the physical viewport.
/// </summary>
internal readonly record struct CanvasCaretGeometry(
    double X,
    double Y,
    double Height,
    bool IsVisible);

/// <summary>
/// A bounded, caret-local native input binding and source-backed canvas frame.
/// The snapshot is immutable and canonical; InputSourceText is its only copied
/// source interval, never a hidden whole-document mirror.
/// </summary>
internal sealed record NativeCanvasBinding(
    long DocumentGeneration,
    long BaseVersion,
    long BindingNonce,
    TextSnapshot Snapshot,
    CanvasFrame Frame,
    int InputSourceStart,
    string InputSourceText,
    int Anchor,
    int Active,
    string Title,
    string Status,
    bool IsModified);

/// <summary>Absolute UTF-16 semantic overlay for the currently bound snapshot.</summary>
internal sealed record NativeCanvasSemantics(
    long Version,
    AnalysisCompleteness Completeness,
    TextSpan Coverage,
    IReadOnlyList<SemanticToken> Tokens,
    IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>
/// Optional canvas/input-island extension to the established native shell.
/// Existing RichEdit/NSTextView shells keep their exact default behavior.
/// </summary>
/// <remarks>
/// All callbacks occur on the native UI thread. While composing, the shell
/// withholds provisional text and refuses a rebind/scroll that would lose the
/// OS candidate. CommitPendingText settles or vetoes commands synchronously.
/// </remarks>
internal interface INativeCanvasShell : INativeEditorShell
{
    /// <summary>
    /// Explicit opt-in switch. A shell may implement this interface in both
    /// modes, but the existing editor remains the default when false.
    /// </summary>
    bool CanvasEnabled { get; }
    /// <summary>Whether the OS input method currently owns provisional text.</summary>
    bool IsCanvasComposing { get; }
    /// <summary>
    /// Maximum UTF-16 source context handed to the OS input host. This is a
    /// platform geometry limit, not an editor file-size or edit-size limit.
    /// </summary>
    int MaxCanvasInputLength { get; }

    /// <summary>One OS-confirmed source edit from the current input binding.</summary>
    event Action<CanvasCommittedEdit>? CanvasEditCommitted;
    /// <summary>A wheel/trackpad or scrollbar delta in logical view pixels.</summary>
    event Action<double>? CanvasScrollRequested;
    /// <summary>
    /// A platform-resolved horizontal source edge from wheel, scrollbar, or
    /// direct navigation. The controller rejects stale generation/version pairs.
    /// </summary>
    event Action<CanvasHorizontalAnchorRequest>? CanvasHorizontalAnchorRequested;
    /// <summary>A native view resize, in device-independent pixels.</summary>
    event Action<double>? CanvasViewportResized;
    /// <summary>A pointer-resolved global source anchor and active boundary.</summary>
    event Action<int, int>? CanvasSelectionRequested;
    /// <summary>
    /// A registered AX/UIA adapter failed after attachment and was detached.
    /// Failure must not disable the text-input host or mutate the document.
    /// </summary>
    event Action? CanvasAccessibilityFailed;

    /// <summary>Rebinds the OS input island only after composition has settled.</summary>
    void SetCanvasBinding(NativeCanvasBinding binding);
    /// <summary>Changes visible paint/selection without rebinding input text.</summary>
    void SetCanvasFrame(CanvasFrame frame);
    /// <summary>
    /// Disables the local input host when a bounded grapheme-safe binding cannot
    /// be constructed; the source-backed canvas remains visible and navigable.
    /// </summary>
    void SetCanvasInputUnavailable(TextSnapshot snapshot, CanvasFrame frame, string reason);
    /// <summary>Updates title and status without changing the input binding.</summary>
    void SetCanvasChrome(string title, string status, bool isModified);
    /// <summary>Publishes version-tagged absolute semantic colors to the canvas.</summary>
    void SetCanvasSemantics(NativeCanvasSemantics semantics);
    /// <summary>
    /// Shapes only bounded rows from the exact frame to prove a source caret's
    /// physical geometry. Returns null if the frame is stale or geometry cannot
    /// be certified; neither a source slice nor a guessed width is sufficient.
    /// </summary>
    CanvasCaretGeometry? GetCanvasCaretGeometry(CanvasFrame frame, int sourceOffset);
    /// <summary>
    /// Attaches a single source-backed AX/UIA document after the native canvas
    /// and input host exist; default native-control mode never calls this.
    /// </summary>
    void SetCanvasAccessibility(AccessibleDocument document, IAccessibleViewport viewport);
}
