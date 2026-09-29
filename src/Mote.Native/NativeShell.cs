using Mote.Formats;
using Mote.Themes;

namespace Mote.Native;

/// <summary>How the operating-system text control represents source line endings.</summary>
internal enum NativeLineEndingMode
{
    /// <summary>The control retains source CR, LF, and CRLF sequences.</summary>
    Preserve,
    /// <summary>The control projects every source line ending as CRLF.</summary>
    CrLf
}

/// <summary>One bounded, editable projection of the canonical engine document.</summary>
internal sealed record NativeDocumentView(
    string Title,
    string Text,
    int PageStart,
    int TotalLength,
    bool IsModified,
    string Status,
    int? FocusDisplayOffset = null);

/// <summary>A styled preview range mapped to its originating document source span.</summary>
internal sealed record NativePreviewSpan(
    int Start,
    int Length,
    string Kind,
    TextSpan SourceSpan,
    bool Emphasis = false);

/// <summary>Version-matched semantic presentation for the current native text page.</summary>
internal sealed record NativeAnalysisView(
    IReadOnlyList<SemanticToken> Tokens,
    string DiagnosticsSummary,
    string PreviewText,
    string Status,
    IReadOnlyList<NativePreviewSpan>? PreviewSpans = null);

/// <summary>Cancelable native window close notification.</summary>
internal sealed class NativeClosingEventArgs : EventArgs
{
    /// <summary>Set when the unsaved-document prompt rejects closing.</summary>
    public bool Cancel { get; set; }
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

    /// <summary>Raised after a user edit with the complete bounded page text.</summary>
    event Action<string>? TextChanged;
    /// <summary>Raised for user-driven selection changes in visible page display offsets.</summary>
    event Action<int, int>? SelectionChanged;
    /// <summary>Raised by the New command.</summary>
    event Action? NewRequested;
    /// <summary>Raised by the Open command.</summary>
    event Action? OpenRequested;
    /// <summary>Raised by the Save command.</summary>
    event Action? SaveRequested;
    /// <summary>Raised by the Save As command.</summary>
    event Action? SaveAsRequested;
    /// <summary>Raised by the Undo command.</summary>
    event Action? UndoRequested;
    /// <summary>Raised by the Redo command.</summary>
    event Action? RedoRequested;
    /// <summary>Raised by the Format command.</summary>
    event Action? FormatRequested;
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
    /// <summary>Updates semantic decoration without altering text or selection.</summary>
    void SetAnalysis(NativeAnalysisView view);
    /// <summary>Applies a compile-time theme policy to platform controls.</summary>
    void SetTheme(IThemePolicy theme);
    /// <summary>
    /// Settles native IME preedit into a synchronous TextChanged callback before a command
    /// may replace the document or persist its bytes; false vetoes that command.
    /// </summary>
    bool CommitPendingText();
    /// <summary>Projects a global source selection into visible display positions.</summary>
    void SetSelection(int displayAnchor, int displayActive);
    /// <summary>Prompts for a search term; null means cancel.</summary>
    string? PromptFind();
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
