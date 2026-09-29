using Mote.Engine;
using Mote.Formats;
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

    /// <summary>One OS-confirmed source edit from the current input binding.</summary>
    event Action<CanvasCommittedEdit>? CanvasEditCommitted;
    /// <summary>A wheel/trackpad or scrollbar delta in logical view pixels.</summary>
    event Action<double>? CanvasScrollRequested;
    /// <summary>A native view resize, in device-independent pixels.</summary>
    event Action<double>? CanvasViewportResized;
    /// <summary>A pointer-resolved global source anchor and active boundary.</summary>
    event Action<int, int>? CanvasSelectionRequested;

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
}
