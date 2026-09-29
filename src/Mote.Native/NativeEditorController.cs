using System.Text;
using Mote.Configuration;
using Mote.Engine;
using Mote.Formats;
using Mote.Telemetry;
using Mote.Themes;
using Mote.Native.Accessibility;
using Mote.Native.Viewport;

namespace Mote.Native;

/// <summary>
/// Composes the canonical document engine with one bounded native text viewport.
/// The platform shell never owns persistence state or a second document model.
/// </summary>
internal sealed class NativeEditorController : IDisposable, IAccessibleViewport
{
    internal const int PageSize = 64 * 1024;
    internal const int PageSlack = 8 * 1024;
    private const int FullAnalysisLimit = 2 * 1024 * 1024;
    private readonly INativeEditorShell _shell;
    private readonly INativeCanvasShell? _canvasShell;
    private readonly AccessibleDocument? _accessibleDocument;
    private readonly int _uiThreadId = Environment.CurrentManagedThreadId;
    private readonly MoteConfiguration _configuration;
    private readonly IThemePolicy _theme;
    private readonly string? _startupPath;
    private Document _document = new();
    private NativeNavigationModel _navigation = new();
    private CanvasInteraction? _canvas;
    private long _canvasGeneration = 1;
    private long _canvasBindingNonce;
    private long _canvasBoundGeneration = -1;
    private long _canvasBoundVersion = -1;
    private int _canvasBoundActive = -1;
    private int _canvasInputStart;
    private int _canvasInputEnd;
    private string? _findQuery;
    private CancellationTokenSource? _findCancellation;
    private long _findSerial;
    private long _clipboardSerial;
    private bool _projectingSelection;
    private bool _nativeProjectsGlobalSelection;
    private IDocumentPolicy _policy = DocumentPolicies.ForKind(DocumentKind.PlainText);
    private NativeFormatSessionDriver? _sessionDriver;
    private NativeIdleFullAnalysis? _idleFullAnalysis;
    private NativeAnalysisView? _visibleSessionAnalysis;
    private NativeTextProjection? _projection;
    private CanvasFrame? _lastCanvasFrame;
    private CancellationTokenSource? _analysisCancellation;
    private long _analysisSerial;
    private int _pageStart;
    private int _pageLength;
    private int? _requestedCaretSource;
    private int _openSerial;
    private long _formatSerial;
    private string _operationStatus = "";
    private bool _saving;
    private volatile bool _accessibilityUnavailable;
    private bool _disposed;

    /// <summary>Wires platform events to engine transactions and static format policies.</summary>
    public NativeEditorController(INativeEditorShell shell, MoteConfiguration configuration,
        IThemePolicy theme, string? startupPath)
    {
        _shell = shell;
        _canvasShell = shell is INativeCanvasShell { CanvasEnabled: true } canvas
            ? canvas : null;
        _configuration = configuration;
        _theme = theme;
        _startupPath = startupPath;
        if (_canvasShell is not null)
        {
            _canvas = NewCanvas(_document.Snapshot);
            _accessibleDocument = new AccessibleDocument(new AccessibleCanvasState(
                _canvasGeneration, _document.Snapshot, _canvas.Frame()));
        }
        _sessionDriver = CreateSessionDriver(_policy);
        _idleFullAnalysis = CreateIdleFullAnalysis(_sessionDriver, _document, _policy);
        _document.Changed += DocumentChanged;
        shell.TextChanged += Edited;
        shell.SelectionChanged += SelectionChanged;
        shell.NewRequested += New;
        shell.OpenRequested += Open;
        shell.SaveRequested += Save;
        shell.SaveAsRequested += SaveAs;
        shell.UndoRequested += Undo;
        shell.RedoRequested += Redo;
        shell.FormatRequested += Format;
        shell.PagePreviousRequested += PreviousPage;
        shell.PageNextRequested += NextPage;
        shell.FindRequested += Find;
        shell.FindNextRequested += FindNext;
        shell.GoToLineRequested += GoToLine;
        shell.SelectAllRequested += SelectAll;
        shell.CopyRequested += Copy;
        shell.CutRequested += Cut;
        shell.ClosingRequested += Closing;
        shell.Shown += Shown;
        if (_canvasShell is { } canvasShell)
        {
            canvasShell.CanvasEditCommitted += CanvasEdited;
            canvasShell.CanvasScrollRequested += CanvasScrolled;
            canvasShell.CanvasViewportResized += CanvasResized;
            canvasShell.CanvasSelectionRequested += CanvasSelected;
            canvasShell.CanvasAccessibilityFailed += CanvasAccessibilityFailed;
        }
    }

    /// <summary>Runs one native UI event loop on the calling thread.</summary>
    public void Run() => _shell.Run();

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _analysisCancellation?.Cancel();
        _analysisCancellation?.Dispose();
        _idleFullAnalysis?.Dispose();
        _visibleSessionAnalysis = null;
        _document.Changed -= DocumentChanged;
        _sessionDriver?.Dispose();
        _findCancellation?.Cancel();
        _findCancellation?.Dispose();
        _accessibleDocument?.Invalidate();
        _document.Dispose();
    }

    private void Shown()
    {
        _shell.SetTheme(_theme);
        ShowDocument();
        if (_canvasShell is not null)
        {
            try { _canvasShell.SetCanvasAccessibility(_accessibleDocument!, this); }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                // AX is not allowed to make the opt-in editor itself unusable.
                // Keep the failure visible, but do not disclose paths or text.
                _accessibilityUnavailable = true;
                ShowDocument();
            }
        }
        ScheduleAnalysis();
        if (_startupPath is not null) StartOpen(_startupPath);
    }

    private void New()
    {
        if (!CanReplace()) return;
        ReplaceDocument(new Document());
    }

    private void Open()
    {
        if (!CanReplace()) return;
        var path = _shell.PickOpenFile();
        if (path is not null) StartOpen(path);
    }

    private bool CanReplace()
    {
        if (!_shell.CommitPendingText()) return false;
        if (_saving)
        {
            _shell.ShowError("Wait for the current save to finish before replacing this document.");
            return false;
        }
        return !_document.IsModified || _shell.ConfirmDiscard();
    }

    private void StartOpen(string path)
    {
        var request = ++_openSerial;
        var previous = _document;
        var version = previous.Snapshot.Version;
        var openMark = MoteTelemetry.Mark();
        _ = Task.Run(async () =>
        {
            Document? opened = null;
            Exception? error = null;
            try { opened = await Document.OpenAsync(path).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { error = ex; }
            Post(() =>
            {
                if (_disposed || request != _openSerial)
                {
                    opened?.Dispose();
                    return;
                }
                if (error is not null)
                {
                    MoteTelemetry.RecordElapsed(TelemetryOperation.OpenToEditable,
                        openMark, status: TelemetryStatus.Failure);
                    _shell.ShowError($"Cannot open file: {error.Message}");
                    return;
                }
                if (opened is null) return;
                if (!SettleInputBeforeAsyncResult())
                {
                    opened.Dispose();
                    MoteTelemetry.RecordElapsed(TelemetryOperation.OpenToEditable,
                        openMark, status: TelemetryStatus.Cancelled);
                    return;
                }
                if (_saving)
                {
                    opened.Dispose();
                    MoteTelemetry.RecordElapsed(TelemetryOperation.OpenToEditable,
                        openMark, status: TelemetryStatus.Cancelled);
                    _shell.ShowError("Wait for the current save to finish before opening another file.");
                    return;
                }
                if (!ReferenceEquals(previous, _document) || version != _document.Snapshot.Version)
                {
                    if (!CanReplace())
                    {
                        opened.Dispose();
                        MoteTelemetry.RecordElapsed(TelemetryOperation.OpenToEditable,
                            openMark, status: TelemetryStatus.Cancelled);
                        return;
                    }
                }
                ReplaceDocument(opened);
                MoteTelemetry.RecordElapsed(TelemetryOperation.OpenToEditable, openMark,
                    Dimensions(opened.Snapshot));
            });
        });
    }

    private void ReplaceDocument(Document replacement)
    {
        ++_openSerial;
        CancelAnalysis();
        _idleFullAnalysis?.Dispose();
        _visibleSessionAnalysis = null;
        _document.Changed -= DocumentChanged;
        _sessionDriver?.Dispose();
        _document.Dispose();
        _document = replacement;
        _document.Changed += DocumentChanged;
        _navigation = new NativeNavigationModel();
        ++_canvasGeneration;
        _canvasBoundGeneration = -1;
        _canvasBoundVersion = -1;
        _canvasBoundActive = -1;
        if (_canvasShell is not null)
            _canvas = NewCanvas(replacement.Snapshot);
        _nativeProjectsGlobalSelection = false;
        _findCancellation?.Cancel();
        _findQuery = null;
        ++_findSerial;
        ++_clipboardSerial;
        ++_formatSerial;
        _operationStatus = "";
        _policy = replacement.FilePath is { } path
            ? DocumentPolicies.ForPath(path)
            : DocumentPolicies.ForKind(DocumentKind.PlainText);
        _sessionDriver = CreateSessionDriver(_policy);
        _idleFullAnalysis = CreateIdleFullAnalysis(_sessionDriver, _document, _policy);
        _pageStart = 0;
        _pageLength = 0;
        _requestedCaretSource = 0;
        ShowDocument();
        ScheduleAnalysis();
    }

    private void Save() => StartSave(saveAs: false);

    private void SaveAs() => StartSave(saveAs: true);

    private void StartSave(bool saveAs)
    {
        if (!_shell.CommitPendingText()) return;
        if (_saving) return;
        var pickerWasUsed = saveAs || _document.FilePath is null;
        var path = pickerWasUsed
            ? _shell.PickSaveFile(_document.FilePath)
            : _document.FilePath;
        if (path is null) return;
        var document = _document;
        var samePath = document.FilePath is { } current && string.Equals(
            Path.GetFullPath(current), Path.GetFullPath(path),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        _saving = true;
        _ = Task.Run(async () =>
        {
            Exception? error = null;
            var cancelled = false;
            using var scope = MoteTelemetry.Start(TelemetryOperation.Save);
            try
            {
                if (pickerWasUsed && !samePath && File.Exists(path))
                {
                    var approved = await FileOverwriteToken.CaptureAsync(path).ConfigureAwait(false);
                    if (await ConfirmOverwriteAsync(path).ConfigureAwait(false))
                        await document.SaveOverAsync(approved).ConfigureAwait(false);
                    else cancelled = true;
                }
                else await document.SaveAsync(path).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                scope?.SetStatus(TelemetryStatus.Failure);
                error = ex;
            }
            Post(() =>
            {
                _saving = false;
                if (_disposed) return;
                if (error is not null)
                {
                    _shell.ShowError($"Save failed; the original file was retained. {error.Message}");
                    return;
                }
                if (cancelled) return;
                // File identity already changed in Document.SaveAsync. Policy selection
                // must not depend on whether a newly started IME composition can settle.
                if (document.FilePath is { } savedPath)
                    SelectPolicy(DocumentPolicies.ForPath(savedPath));
                MoteTelemetry.Record(TelemetryEvent.SaveCompleted);
                ScheduleAnalysis();
                if (!SettleInputBeforeAsyncResult())
                {
                    _operationStatus = "Save view update postponed during text composition.";
                    ShowDocument();
                    return;
                }
                ShowDocument();
            });
        });
    }

    private Task<bool> ConfirmOverwriteAsync(string path)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!TryPost(() =>
            completion.TrySetResult(!_disposed && _shell.ConfirmOverwrite(path))))
            completion.TrySetResult(false);
        return completion.Task;
    }

    private void Edited(string editedDisplay)
    {
        if (_canvasShell is not null) return;
        if (_projection is null) return;
        if (_projection.Difference(editedDisplay) is not { } change) return;
        var mark = MoteTelemetry.Mark();
        try
        {
            var globalSelection = _nativeProjectsGlobalSelection &&
                _navigation.SelectionLength > 0 &&
                (_navigation.SelectionStart < _pageStart ||
                 _navigation.SelectionStart + _navigation.SelectionLength > _pageStart + _pageLength);
            var applied = globalSelection
                ? new TextChange(_navigation.SelectionStart, _navigation.SelectionLength,
                    change.InsertText)
                : new TextChange(_pageStart + change.Start, change.DeleteLength,
                    change.InsertText);
            _document.Apply(applied);
            var newCaret = applied.Start + applied.InsertText.Length;
            _navigation.MoveCaret(_document.Snapshot, newCaret);
            _nativeProjectsGlobalSelection = false;
            InvalidateFind();
            if (globalSelection)
            {
                _pageStart = Math.Max(0, applied.Start - 1024);
                _pageLength = 0;
                _requestedCaretSource = newCaret;
                ShowDocument();
                ScheduleAnalysis(mark);
                return;
            }
            var desiredLength = _pageLength + change.InsertText.Length - change.DeleteLength;
            if (desiredLength > PageSize + PageSlack)
            {
                _pageStart = Math.Max(0, newCaret - PageSize + 2048);
                _pageLength = 0;
                _requestedCaretSource = newCaret;
            }
            else
            {
                _pageLength = Math.Max(0, desiredLength);
                if (_pageLength < PageSize / 2 &&
                    _pageStart + _pageLength < _document.Snapshot.Length)
                {
                    _pageLength = 0;
                    _requestedCaretSource = newCaret;
                }
            }
            MoteTelemetry.Record(TelemetryEvent.EditCommitted,
                dimensions: Dimensions(_document.Snapshot));
            ShowDocument();
            ScheduleAnalysis(mark);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _shell.ShowError($"This edit cannot be represented safely: {ex.Message}");
            ShowDocument();
        }
    }

    private void Undo()
    {
        if (!_shell.CommitPendingText()) return;
        if (_document.Undo())
        {
            _pageLength = 0;
            ShowDocument();
            RevealSelection();
            ProjectSelection();
            ScheduleAnalysis();
        }
    }

    private void Redo()
    {
        if (!_shell.CommitPendingText()) return;
        if (_document.Redo())
        {
            _pageLength = 0;
            ShowDocument();
            RevealSelection();
            ProjectSelection();
            ScheduleAnalysis();
        }
    }

    private void Format()
    {
        if (!_shell.CommitPendingText()) return;
        var snapshot = _document.Snapshot;
        if (snapshot.Length > FullAnalysisLimit)
        {
            _shell.ShowError("Formatting this large document requires a bounded, trivia-preserving formatter; it is not available yet.");
            return;
        }
        var document = _document;
        var policy = _policy;
        var serial = ++_formatSerial;
        _operationStatus = "Formatting in background…";
        ShowDocument();
        _ = Task.Run(() =>
        {
            string? formatted = null;
            var changed = false;
            Exception? error = null;
            try
            {
                var original = snapshot.GetText();
                formatted = policy.Format(original);
                changed = formatted != original;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { error = ex; }
            Post(() =>
            {
                if (_disposed || serial != _formatSerial) return;
                if (!ReferenceEquals(document, _document) ||
                    _document.Snapshot.Version != snapshot.Version)
                {
                    _operationStatus = "Format result discarded after a newer edit.";
                    ShowDocument();
                    return;
                }
                if (!SettleInputBeforeAsyncResult())
                {
                    _operationStatus = "Formatting cancelled during text composition.";
                    ShowDocument();
                    return;
                }
                _operationStatus = "";
                if (error is not null)
                {
                    _shell.ShowError($"Formatting failed without changing the file: {error.Message}");
                    ShowDocument();
                    return;
                }
                if (!ReferenceEquals(document, _document) ||
                    _document.Snapshot.Version != snapshot.Version)
                {
                    _operationStatus = "Format result discarded after a newer edit.";
                    ShowDocument();
                    return;
                }
                if (changed && formatted is not null)
                {
                    _document.Apply(new TextChange(0, snapshot.Length, formatted));
                    _navigation.MoveCaret(_document.Snapshot, 0);
                    _pageStart = 0;
                    _pageLength = 0;
                    _requestedCaretSource = 0;
                }
                ShowDocument();
                if (changed) ScheduleAnalysis();
            });
        });
    }

    private void PreviousPage()
    {
        if (!_shell.CommitPendingText()) return;
        if (_canvas is not null)
        {
            CanvasScrolled(-_canvas.ViewportHeight);
            return;
        }
        if (_pageStart == 0) return;
        InvalidateFind();
        _pageStart = Math.Max(0, _pageStart - PageSize);
        _pageLength = 0;
        _requestedCaretSource = _pageStart;
        ShowDocument();
        ProjectSelectionOrMoveToPage();
        ScheduleAnalysis();
    }

    private void NextPage()
    {
        if (!_shell.CommitPendingText()) return;
        if (_canvas is not null)
        {
            CanvasScrolled(_canvas.ViewportHeight);
            return;
        }
        var length = _document.Snapshot.Length;
        if (_pageStart + _pageLength >= length) return;
        InvalidateFind();
        _pageStart += _pageLength;
        _pageLength = 0;
        _requestedCaretSource = _pageStart;
        ShowDocument();
        ProjectSelectionOrMoveToPage();
        ScheduleAnalysis();
    }

    private void SelectionChanged(int displayAnchor, int displayActive)
    {
        if (_canvasShell is not null) return;
        if (_projectingSelection || _projection is null) return;
        if ((uint)displayAnchor > (uint)_projection.Display.Length ||
            (uint)displayActive > (uint)_projection.Display.Length) return;
        var forward = displayActive >= displayAnchor;
        var anchor = _projection.ToSourceBoundary(displayAnchor, towardEnd: !forward);
        var active = _projection.ToSourceBoundary(displayActive, towardEnd: forward &&
            displayActive != displayAnchor);
        var nextAnchor = _pageStart + anchor;
        var nextActive = _pageStart + active;
        if (nextAnchor != _navigation.Anchor || nextActive != _navigation.Active)
        {
            var showedSearch = _operationStatus == "Searching document…";
            InvalidateFind();
            _navigation.SetSelection(_document.Snapshot, nextAnchor, nextActive);
            if (showedSearch) ShowDocument();
        }
        _nativeProjectsGlobalSelection = _navigation.SelectionLength > 0;
    }

    /// <summary>
    /// Accepts exactly one final, version- and binding-tagged native edit. The
    /// source document changes synchronously before the input callback returns,
    /// so its next keystroke uses the new binding nonce and engine version.
    /// </summary>
    private void CanvasEdited(CanvasCommittedEdit edit)
    {
        if (_canvasShell is null || _disposed) return;
        var snapshot = _document.Snapshot;
        if (edit.DocumentGeneration != _canvasGeneration ||
            edit.BaseVersion != snapshot.Version ||
            edit.BindingNonce != _canvasBindingNonce) return;
        // The OS adapter must report the exact global transaction, including
        // any selection outside its bounded host. Never substitute a possibly
        // stale controller selection for a later native selection change.
        var change = edit.Change;
        if (change.InsertText is null || change.Start < 0 || change.DeleteLength < 0 ||
            change.Start > snapshot.Length - change.DeleteLength ||
            edit.ActiveSourceOffset != (long)change.Start + change.InsertText.Length ||
            (_navigation.SelectionLength > 0 &&
             (change.Start != _navigation.SelectionStart ||
              change.DeleteLength != _navigation.SelectionLength)) ||
            (_navigation.SelectionLength == 0 &&
             (change.Start < _canvasInputStart ||
              (long)change.Start + change.DeleteLength > _canvasInputEnd)))
        {
            _shell.ShowError("The native input could not be mapped to the document safely.");
            _canvasBoundVersion = -1;
            ShowDocument();
            return;
        }
        try
        {
            var mark = MoteTelemetry.Mark();
            _document.Apply(change);
            var newCaret = change.Start + change.InsertText.Length;
            _navigation.MoveCaret(_document.Snapshot, newCaret);
            _canvas!.Reveal(newCaret);
            InvalidateFind();
            MoteTelemetry.Record(TelemetryEvent.EditCommitted,
                dimensions: Dimensions(_document.Snapshot));
            ShowDocument();
            ScheduleAnalysis(mark);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _shell.ShowError("The native input could not be committed safely.");
            _canvasBoundVersion = -1;
            ShowDocument();
        }
    }

    /// <summary>Advances the same source-backed viewport, never a native text page.</summary>
    private void CanvasScrolled(double pixels)
    {
        if (_canvas is null || !double.IsFinite(pixels) || pixels == 0) return;
        _canvas.ScrollBy(pixels);
        ShowDocument();
        ScheduleAnalysis();
    }

    /// <summary>Recomputes only visible geometry after a native view resize.</summary>
    private void CanvasResized(double height)
    {
        if (_canvas is null || !double.IsFinite(height) || height <= 0) return;
        _canvas.Resize(height);
        ShowDocument();
        ScheduleAnalysis();
    }

    /// <summary>Stores OS hit-test endpoints as one global UTF-16 selection.</summary>
    private void CanvasSelected(int anchor, int active)
    {
        if (_canvas is null || _disposed) return;
        var snapshot = _document.Snapshot;
        if ((uint)anchor > (uint)snapshot.Length || (uint)active > (uint)snapshot.Length)
            return;
        if (SafeBoundary(snapshot, anchor, backwards: true) != anchor ||
            SafeBoundary(snapshot, active, backwards: true) != active) return;
        if (anchor == _navigation.Anchor && active == _navigation.Active) return;
        InvalidateFind();
        _navigation.SetSelection(snapshot, anchor, active);
        ShowDocument();
    }

    /// <summary>
    /// Keeps a detached provider failure visible across subsequent Open/New
    /// without allowing an accessibility callback to unwind through the OS.
    /// </summary>
    private void CanvasAccessibilityFailed()
    {
        if (_disposed) return;
        _accessibilityUnavailable = true;
        Post(() => { if (!_disposed) ShowDocument(); });
    }

    /// <summary>
    /// Reveals one version-bound source interval for an OS accessibility
    /// provider without ever settling or moving an active IME composition.
    /// A successful result includes a synchronous matching frame publication.
    /// </summary>
    public AccessibleRevealResult TryReveal(AccessibleRange range, bool alignToTop)
    {
        if (Environment.CurrentManagedThreadId != _uiThreadId)
            return AccessibleRevealResult.WrongThread;
        if (_disposed || _canvas is null || _canvasShell is null)
            return AccessibleRevealResult.StaleRange;
        var snapshot = _document.Snapshot;
        if (range.Generation != _canvasGeneration || range.Version != snapshot.Version ||
            range.Start < 0 || range.End < range.Start || range.End > snapshot.Length)
            return AccessibleRevealResult.StaleRange;
        // Neither half of CRLF nor a surrogate interior is a painted caret
        // boundary; fail before moving the viewport at all.
        if (SafeBoundary(snapshot, range.Start, backwards: true) != range.Start)
            return AccessibleRevealResult.NotVisible;
        if (_canvasShell.IsCanvasComposing)
            return AccessibleRevealResult.CompositionBlocked;

        _canvas.Reveal(range.Start);
        if (!alignToTop)
        {
            var rowHeight = Math.Max(12, _theme.Typography.EditorFontSize *
                _theme.Typography.LineHeightMultiplier);
            _canvas.ScrollBy(-Math.Max(0, _canvas.ViewportHeight - rowHeight),
                preserveSourceFocus: true);
        }
        ShowDocument();
        if (_lastCanvasFrame is not { } published ||
            !ContainsPaintedBoundary(published, range.Start))
            return AccessibleRevealResult.NotVisible;
        ScheduleAnalysis();
        return AccessibleRevealResult.Revealed;
    }

    /// <summary>
    /// A successful accessibility reveal must include the requested source
    /// boundary in a bounded painted slice, not merely the logical row.
    /// </summary>
    private static bool ContainsPaintedBoundary(CanvasFrame frame, int sourceOffset)
    {
        foreach (var slice in frame.Slices)
        {
            var end = slice.SourceStart + slice.SourceLength;
            if (sourceOffset >= slice.SourceStart &&
                (sourceOffset < end || sourceOffset == end && !slice.HasHiddenSuffix))
                return true;
        }
        return false;
    }

    private void Find()
    {
        if (!_shell.CommitPendingText()) return;
        var query = _shell.PromptFind();
        if (string.IsNullOrEmpty(query)) return;
        _findQuery = query;
        StartFind(query);
    }

    private void FindNext()
    {
        if (!_shell.CommitPendingText()) return;
        if (_findQuery is null) { Find(); return; }
        StartFind(_findQuery);
    }

    private void StartFind(string query)
    {
        InvalidateFind();
        var cancellation = new CancellationTokenSource();
        _findCancellation = cancellation;
        var serial = _findSerial;
        var document = _document;
        var snapshot = document.Snapshot;
        var startAnchor = _navigation.Anchor;
        var startActive = _navigation.Active;
        _operationStatus = "Searching document…";
        ShowDocument();
        _ = Task.Run(() =>
        {
            try
            {
                var result = new NativeNavigationModel();
                result.SetSelection(snapshot, startAnchor, startActive);
                var found = result.FindNext(snapshot, query, wrap: true, cancellation.Token);
                Post(() =>
                {
                    if (_disposed || cancellation.IsCancellationRequested ||
                        serial != _findSerial || !ReferenceEquals(document, _document) ||
                        snapshot.Version != _document.Snapshot.Version) return;
                    if (!SettleInputBeforeAsyncResult())
                    {
                        _operationStatus = "Search cancelled during text composition.";
                        ShowDocument();
                        return;
                    }
                    if (_disposed || cancellation.IsCancellationRequested || serial != _findSerial ||
                        !ReferenceEquals(document, _document) ||
                        snapshot.Version != _document.Snapshot.Version) return;
                    _operationStatus = found ? $"Found at {result.SelectionStart:N0}" :
                        "No match in document";
                    if (found)
                    {
                        _navigation.SetSelection(snapshot, result.Anchor, result.Active);
                        RevealSelection();
                        ProjectSelection();
                    }
                    else ShowDocument();
                });
            }
            catch (OperationCanceledException) { }
        });
    }

    private void GoToLine()
    {
        if (!_shell.CommitPendingText()) return;
        var line = _shell.PromptGoToLine();
        if (line is null) return;
        try
        {
            InvalidateFind();
            _navigation.GoToLine(_document.Snapshot, line.Value);
            RevealSelection();
            ProjectSelection();
        }
        catch (ArgumentOutOfRangeException)
        {
            _shell.ShowError($"Line must be between 1 and {_document.Snapshot.LineCount:N0}.");
        }
    }

    private void SelectAll()
    {
        if (!_shell.CommitPendingText()) return;
        InvalidateFind();
        _navigation.SelectAll(_document.Snapshot);
        ProjectSelection();
    }

    private void Copy() => CopyOrCut(cut: false);

    private void Cut() => CopyOrCut(cut: true);

    private void CopyOrCut(bool cut)
    {
        if (!_shell.CommitPendingText()) return;
        var length = _navigation.SelectionLength;
        if (length == 0) return;
        var serial = ++_clipboardSerial;
        var start = _navigation.SelectionStart;
        var anchor = _navigation.Anchor;
        var active = _navigation.Active;
        var document = _document;
        var snapshot = document.Snapshot;
        _operationStatus = cut ? "Preparing cut…" : "Preparing copy…";
        ShowDocument();
        _ = Task.Run(() =>
        {
            string? selected = null;
            Exception? error = null;
            try { selected = snapshot.GetText(start, length); }
            catch (Exception ex) { error = ex; }
            Post(() =>
            {
                if (_disposed || serial != _clipboardSerial) return;
                if (cut && (!ReferenceEquals(document, _document) ||
                    snapshot.Version != _document.Snapshot.Version ||
                    anchor != _navigation.Anchor || active != _navigation.Active))
                {
                    _operationStatus = "Cut cancelled after a newer edit or selection change.";
                    ShowDocument();
                    return;
                }
                if (cut && !SettleInputBeforeAsyncResult())
                {
                    _operationStatus = "Cut cancelled during text composition.";
                    ShowDocument();
                    return;
                }
                _operationStatus = "";
                if (error is not null || selected is null)
                {
                    _shell.ShowError("The selection could not be copied; the document was not changed.");
                    ShowDocument();
                    return;
                }
                if (cut && (!ReferenceEquals(document, _document) ||
                    snapshot.Version != _document.Snapshot.Version ||
                    anchor != _navigation.Anchor || active != _navigation.Active))
                {
                    _operationStatus = "Cut cancelled after a newer edit or selection change.";
                    ShowDocument();
                    return;
                }
                try { _shell.SetClipboardText(selected); }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    _shell.ShowError("The system clipboard rejected the selection; the document was not changed.");
                    ShowDocument();
                    return;
                }
                if (cut)
                {
                    InvalidateFind();
                    try { _document.Apply(new TextChange(start, length, "")); }
                    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                    {
                        _shell.ShowError($"Cut could not safely change the selection: {ex.Message}");
                        ShowDocument();
                        return;
                    }
                    _navigation.MoveCaret(_document.Snapshot, start);
                    _pageStart = Math.Max(0, start - 1024);
                    _pageLength = 0;
                    _requestedCaretSource = start;
                    ShowDocument();
                    ProjectSelection();
                    ScheduleAnalysis();
                }
                else ShowDocument();
            });
        });
    }

    private void RevealSelection()
    {
        if (_canvas is { } canvas)
        {
            var frame = canvas.Frame();
            var activeSource = _navigation.Active;
            var first = frame.Slices.Count == 0 ? frame.TopAnchor.SourceOffset :
                frame.Slices[0].SourceStart;
            var last = frame.Slices.Count == 0 ? first :
                frame.Slices[^1].SourceStart + frame.Slices[^1].SourceLength;
            if (activeSource < first || activeSource > last) canvas.Reveal(activeSource);
            ShowDocument();
            ScheduleAnalysis();
            return;
        }
        var caret = _navigation.Active;
        if (caret >= _pageStart && caret <= _pageStart + _pageLength &&
            _navigation.SelectionStart >= _pageStart) return;
        _pageStart = Math.Max(0, _navigation.SelectionStart - 1024);
        _pageLength = 0;
        _requestedCaretSource = caret;
        ShowDocument();
        ScheduleAnalysis();
    }

    private void ProjectSelectionOrMoveToPage()
    {
        if (_canvas is not null) { ShowDocument(); return; }
        if (_projection is null) return;
        if (_navigation.Project(_pageStart, _projection) is null)
        {
            // An off-page selection remains global; only the native caret is parked
            // at this page. Copy/Find continue to use canonical engine coordinates.
            _projectingSelection = true;
            try { _shell.SetSelection(0, 0); }
            finally { _projectingSelection = false; }
            _nativeProjectsGlobalSelection = false;
        }
        else ProjectSelection();
    }

    private void ProjectSelection()
    {
        if (_canvas is not null) { ShowDocument(); return; }
        if (_projection is null) return;
        if (_navigation.Project(_pageStart, _projection) is not { } selection) return;
        _projectingSelection = true;
        try { _shell.SetSelection(selection.Anchor, selection.Active); }
        finally { _projectingSelection = false; }
        _nativeProjectsGlobalSelection = _navigation.SelectionLength > 0;
    }

    private void ShowDocument()
    {
        var snapshot = _document.Snapshot;
        var file = _document.FilePath is { } path ? Path.GetFileName(path) : "Untitled";
        var title = $"{file}{(_document.IsModified ? " •" : "")} — mote";
        var warnings = _configuration.Diagnostics.Count == 0 ? "" :
            $" · Config warnings: {_configuration.Diagnostics.Count}";
        var health = MoteTelemetry.Health;
        var traceWarning = health.SinkFaulted || health.DroppedRecords > 0
            ? " · Trace degraded" : "";
        var accessibilityWarning = _accessibilityUnavailable
            ? " · Accessibility provider unavailable" : "";
        var statusSuffix = warnings + traceWarning + accessibilityWarning +
            (_operationStatus.Length == 0 ? "" : " · " + _operationStatus);
        if (_canvasShell is not null)
        {
            ShowCanvasDocument(snapshot, title, statusSuffix);
            return;
        }
        _pageStart = Math.Min(_pageStart, snapshot.Length);
        _pageStart = SafeBoundary(snapshot, _pageStart, backwards: true);
        var targetLength = _pageLength > 0 ? _pageLength : PageSize;
        var end = SafeBoundary(snapshot, Math.Min(snapshot.Length, _pageStart + targetLength), backwards: false);
        _pageLength = end - _pageStart;
        _projection = new NativeTextProjection(snapshot.GetText(_pageStart, _pageLength), _shell.LineEndingMode);
        int? focus = _requestedCaretSource is { } caret && caret >= _pageStart &&
            caret <= _pageStart + _pageLength
            ? _projection.ToDisplay(caret - _pageStart) : null;
        _requestedCaretSource = null;
        var pageStatus = snapshot.Length <= PageSize ? "" :
            $"Page {_pageStart:N0}–{_pageStart + _pageLength:N0} / {snapshot.Length:N0}; page navigation is discrete";
        _shell.SetDocument(new NativeDocumentView(title, _projection.Display, _pageStart,
            snapshot.Length, _document.IsModified, pageStatus + statusSuffix, focus));
    }

    /// <summary>
    /// Rebinds only when source version or caret-local input window changes.
    /// Scrolling and status updates paint a new source-backed frame without
    /// replacing the native input host's text or disturbing composition.
    /// </summary>
    private void ShowCanvasDocument(TextSnapshot snapshot, string title,
        string statusSuffix)
    {
        var canvas = _canvas!;
        var shell = _canvasShell!;
        var frame = canvas.Frame();
        _lastCanvasFrame = frame;
        _accessibleDocument!.Publish(new AccessibleCanvasState(
            _canvasGeneration, snapshot, frame));
        var firstVisible = frame.Slices.Count == 0
            ? Math.Clamp(frame.TopAnchor.SourceOffset, 0, snapshot.Length)
            : frame.Slices[0].SourceStart;
        _pageStart = SafeBoundary(snapshot, firstVisible, backwards: true);
        var visibleEnd = frame.Slices.Count == 0 ? _pageStart :
            frame.Slices[^1].SourceStart + frame.Slices[^1].SourceLength;
        // Two visible slices can be separated by a 50 MiB logical line. The
        // analysis/preview bridge is still contiguous, so bound that bridge
        // independently of the source-backed canvas and never mirror the gap.
        var bridgeEnd = SafeBoundary(snapshot,
            (int)Math.Min(visibleEnd, (long)_pageStart + PageSize), backwards: true);
        _pageLength = Math.Max(0, bridgeEnd - _pageStart);
        _projection = new NativeTextProjection(snapshot.GetText(_pageStart, _pageLength),
            _shell.LineEndingMode);
        _requestedCaretSource = null;
        var line = snapshot.GetLineIndexFromOffset(frame.TopAnchor.SourceOffset) + 1;
        var status = $"Line {line:N0} / {snapshot.LineCount:N0} · continuous canvas (experimental)" +
            statusSuffix;
        var needsBinding = _canvasBoundGeneration != _canvasGeneration ||
            _canvasBoundVersion != snapshot.Version ||
            (_navigation.Active != _canvasBoundActive &&
             (_navigation.Active < _canvasInputStart || _navigation.Active > _canvasInputEnd));
        if (needsBinding)
        {
            CanvasInputWindow input;
            try { input = CanvasInputWindowSelector.Select(snapshot, _navigation.Active); }
            catch (CanvasInputWindowBoundaryException)
            {
                // Do not offer a half-grapheme context to the OS input method.
                // The canvas remains readable; moving the caret can retry.
                _canvasInputStart = _canvasInputEnd = _navigation.Active;
                _canvasBoundActive = _navigation.Active;
                _canvasBoundGeneration = _canvasGeneration;
                _canvasBoundVersion = snapshot.Version;
                ++_canvasBindingNonce;
                shell.SetCanvasInputUnavailable(snapshot, frame,
                    "No safe bounded text-input window at this caret.");
                shell.SetCanvasSemantics(new NativeCanvasSemantics(snapshot.Version,
                    AnalysisCompleteness.Provisional,
                    new Mote.Formats.TextSpan(_pageStart, _pageLength), [], []));
                shell.SetCanvasChrome(title, status + " · Input unavailable at this caret",
                    _document.IsModified);
                return;
            }
            _canvasInputStart = input.SourceStart;
            _canvasInputEnd = input.SourceEnd;
            _canvasBoundActive = _navigation.Active;
            _canvasBoundGeneration = _canvasGeneration;
            _canvasBoundVersion = snapshot.Version;
            var nonce = ++_canvasBindingNonce;
            shell.SetCanvasBinding(new NativeCanvasBinding(_canvasGeneration,
                snapshot.Version, nonce, snapshot, frame, input.SourceStart,
                input.SourceText, _navigation.Anchor, _navigation.Active,
                title, status, _document.IsModified));
            shell.SetCanvasSemantics(new NativeCanvasSemantics(snapshot.Version,
                AnalysisCompleteness.Provisional,
                new Mote.Formats.TextSpan(_pageStart, _pageLength), [], []));
        }
        else shell.SetCanvasFrame(frame);
        shell.SetCanvasChrome(title, status, _document.IsModified);
    }

    private static int SafeBoundary(TextSnapshot snapshot, int offset, bool backwards)
    {
        if (offset <= 0 || offset >= snapshot.Length) return offset;
        var pair = snapshot.GetText(offset - 1, 2);
        if (char.IsHighSurrogate(pair[0]) && char.IsLowSurrogate(pair[1]))
            return backwards ? offset - 1 : offset + 1;
        if (pair[0] == '\r' && pair[1] == '\n')
            return backwards ? offset - 1 : offset + 1;
        return offset;
    }

    private void ScheduleAnalysis(TelemetryMark editMark = default)
    {
        CancelAnalysis();
        _idleFullAnalysis?.Cancel();
        _visibleSessionAnalysis = null;
        var cancellation = new CancellationTokenSource();
        _analysisCancellation = cancellation;
        var serial = ++_analysisSerial;
        var document = _document;
        var snapshot = document.Snapshot;
        var policy = _policy;
        var pageStart = _pageStart;
        var pageLength = _pageLength;
        if (_sessionDriver is { } driver)
        {
            ScheduleSessionAnalysis(driver, document, snapshot, policy,
                pageStart, pageLength, cancellation, serial, editMark);
            return;
        }
        var full = snapshot.Length <= FullAnalysisLimit;
        if (!full && policy.Kind is not (DocumentKind.PlainText or DocumentKind.Markdown or DocumentKind.Csv))
        {
            _shell.SetAnalysis(new NativeAnalysisView([], "Global diagnostics deferred for large files.",
                "Structure preview requires complete semantic analysis.", "Large file: editable viewport; semantic analysis deferred"));
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(80, cancellation.Token).ConfigureAwait(false);
                var text = full ? snapshot.GetText() : snapshot.GetText(pageStart, pageLength);
                using var scope = MoteTelemetry.Start(TelemetryOperation.AnalysisParse, Dimensions(snapshot));
                var analysis = policy.Analyze(text, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                Post(() =>
                {
                    if (_disposed || cancellation.IsCancellationRequested ||
                        serial != _analysisSerial || !ReferenceEquals(document, _document) ||
                        snapshot.Version != _document.Snapshot.Version || pageStart != _pageStart)
                    {
                        MoteTelemetry.Record(TelemetryEvent.AnalysisDiscarded);
                        return;
                    }
                    using var present = MoteTelemetry.Start(TelemetryOperation.AnalysisToPresentation,
                        Dimensions(snapshot));
                    var visible = ProjectTokens(analysis.Tokens, full ? pageStart : 0,
                        pageLength, _projection!);
                    var diagnostics = full
                        ? DiagnosticSummary(analysis.Diagnostics)
                        : "Partial viewport analysis only; global diagnostics unavailable.";
                    var preview = NativePreviewBuilder.Build(analysis, policy.Kind,
                        full ? 0 : pageStart, full);
                    _shell.SetAnalysis(new NativeAnalysisView(visible, diagnostics, preview.Text,
                        full ? $"{policy.DisplayName} semantic analysis · v{snapshot.Version}"
                             : $"{policy.DisplayName} sample · partial", preview.Spans));
                    MoteTelemetry.Record(TelemetryEvent.AnalysisPublished,
                        dimensions: Dimensions(snapshot));
                    MoteTelemetry.RecordElapsed(TelemetryOperation.EditToPresentation,
                        editMark, Dimensions(snapshot));
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Post(() =>
                {
                    if (!_disposed && serial == _analysisSerial)
                        _shell.SetAnalysis(new NativeAnalysisView([], "Analysis failed.", "",
                            $"{policy.DisplayName}: {ex.Message}"));
                });
            }
        });
    }

    private void ScheduleSessionAnalysis(NativeFormatSessionDriver driver,
        Document document, TextSnapshot snapshot, IDocumentPolicy policy,
        int pageStart, int pageLength, CancellationTokenSource cancellation,
        long serial, TelemetryMark editMark)
    {
        var scope = snapshot.Length <= FullAnalysisLimit ? AnalysisScope.Full : AnalysisScope.Visible;
        var request = new AnalysisRequest(new Mote.Formats.TextSpan(pageStart, pageLength), scope);
        _ = Task.Run(async () =>
        {
            try
            {
                // JSON Visible checks the whole source to certify global semantics.
                // A longer debounce on huge files coalesces typing rather than
                // repeatedly starting a multi-second scan after each keystroke.
                var delay = policy.Kind == DocumentKind.Json ? snapshot.Length switch
                {
                    > 32 * 1024 * 1024 => 500,
                    > 8 * 1024 * 1024 => 200,
                    _ => 80
                } : 80;
                await Task.Delay(delay, cancellation.Token).ConfigureAwait(false);
                using var parse = MoteTelemetry.Start(TelemetryOperation.AnalysisParse,
                    Dimensions(snapshot));
                var result = await driver.AnalyzeAsync(snapshot, request,
                    cancellation.Token).ConfigureAwait(false);
                cancellation.Token.ThrowIfCancellationRequested();
                Post(() =>
                {
                    if (_disposed || cancellation.IsCancellationRequested ||
                        serial != _analysisSerial || !ReferenceEquals(document, _document) ||
                        !ReferenceEquals(driver, _sessionDriver) ||
                        result.Version != _document.Snapshot.Version || pageStart != _pageStart)
                    {
                        MoteTelemetry.Record(TelemetryEvent.AnalysisDiscarded);
                        return;
                    }
                    using var present = MoteTelemetry.Start(
                        TelemetryOperation.AnalysisToPresentation, Dimensions(snapshot));
                    var tokens = ProjectTokens(result.Tokens, pageStart, pageLength,
                        _projection!);
                    var diagnostics = SessionDiagnosticSummary(result, pageStart, pageLength);
                    var preview = NativePreviewBuilder.Build(result, policy.Kind, snapshot,
                        pageStart, pageLength);
                    _visibleSessionAnalysis = new NativeAnalysisView(tokens, diagnostics,
                        preview.Text, $"{policy.DisplayName} · {result.Completeness} · v{result.Version}",
                        preview.Spans);
                    _shell.SetAnalysis(_visibleSessionAnalysis);
                    _canvasShell?.SetCanvasSemantics(new NativeCanvasSemantics(result.Version,
                        result.Completeness, result.Coverage,
                        VisibleSourceTokens(result.Tokens, pageStart, pageLength),
                        VisibleSourceDiagnostics(result.Diagnostics, pageStart, pageLength)));
                    MoteTelemetry.Record(TelemetryEvent.AnalysisPublished,
                        dimensions: Dimensions(snapshot));
                    MoteTelemetry.RecordElapsed(TelemetryOperation.EditToPresentation,
                        editMark, Dimensions(snapshot));
                    var idle = _idleFullAnalysis?.Offer(snapshot, result, request.VisibleRange);
                    if (idle is IdleFullOffer.MemoryLimited or IdleFullOffer.PolicyLimited)
                    {
                        var reason = idle == IdleFullOffer.MemoryLimited
                            ? "memory pressure"
                            : "format work limit";
                        _shell.SetAnalysis(_visibleSessionAnalysis with
                        {
                            Status = $"{policy.DisplayName} · Full pass deferred: {reason}; " +
                                $"global diagnostics unknown · v{result.Version}"
                        });
                    }
                });
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Post(() =>
                {
                    if (!_disposed && serial == _analysisSerial &&
                        ReferenceEquals(driver, _sessionDriver))
                        _shell.SetAnalysis(new NativeAnalysisView([], "Analysis failed.", "",
                            $"{policy.DisplayName}: {ex.Message}"));
                });
            }
        });
    }

    /// <summary>
    /// Separates a certified document-wide count from diagnostics actually visible
    /// on the current source page. Full projections may include off-page errors.
    /// </summary>
    private static string SessionDiagnosticSummary(DocumentAnalysis result,
        int pageStart, int pageLength)
    {
        var pageEnd = pageStart + pageLength;
        var shown = result.Diagnostics.Where(d => d.Span.Length == 0
            ? d.Span.Start >= pageStart &&
                (d.Span.Start < pageEnd ||
                 pageEnd == result.Root.Span.End && d.Span.Start == pageEnd)
            : d.Span.Start < pageEnd && d.Span.End > pageStart).ToArray();
        if (result.Completeness != AnalysisCompleteness.Complete)
            return $"{result.Completeness} in {result.Coverage.Start:N0}–" +
                $"{result.Coverage.End:N0}; global diagnostics unknown. " +
                (shown.Length == 0 ? "No diagnostics in displayed viewport." :
                    DiagnosticSummary(shown));
        var count = result.TotalDiagnosticCount ?? result.Diagnostics.Count;
        if (count == 0) return "No diagnostics.";
        if (shown.Length == 0)
            return $"{count:N0} document diagnostics; none in displayed viewport.";
        return $"{count:N0} document diagnostics; shown: {DiagnosticSummary(shown)}";
    }

    private void DocumentChanged(object? sender, DocumentChangedEventArgs change)
    {
        _sessionDriver?.Record(change);
        _navigation.ApplyChange(change.Change, change.After);
        _canvas?.ApplyEdit(change.After, change.Change);
    }

    /// <summary>Creates one source-backed viewport sharing the controller's navigation state.</summary>
    private CanvasInteraction NewCanvas(TextSnapshot snapshot) =>
        new(snapshot, Math.Max(12, _theme.Typography.EditorFontSize *
            _theme.Typography.LineHeightMultiplier), 640,
            maxSliceLength: CanvasInputWindowSelector.MaxLength,
            selection: _navigation);

    private static NativeFormatSessionDriver? CreateSessionDriver(IDocumentPolicy policy) =>
        policy is IIncrementalDocumentPolicy incremental
            ? new NativeFormatSessionDriver(incremental) : null;

    /// <summary>
    /// Binds the optional idle lane to one document/policy identity. Worker results
    /// cross the UI thread only after rechecking that identity and source version.
    /// </summary>
    private NativeIdleFullAnalysis? CreateIdleFullAnalysis(NativeFormatSessionDriver? driver,
        Document document, IDocumentPolicy policy)
    {
        if (driver is null) return null;
        return new NativeIdleFullAnalysis(driver, policy.Kind,
            (snapshot, result, visibleRange) => Post(() =>
                PublishIdleFullAnalysis(driver, document, policy, snapshot, result,
                    visibleRange)),
            (snapshot, error) => Post(() =>
            {
                if (!_disposed && ReferenceEquals(driver, _sessionDriver) &&
                    ReferenceEquals(document, _document) &&
                    snapshot.Version == _document.Snapshot.Version)
                {
                    MoteTelemetry.Record(TelemetryEvent.AnalysisDiscarded,
                        dimensions: Dimensions(snapshot), status: TelemetryStatus.Failure);
                    if (_visibleSessionAnalysis is { } visible)
                        _shell.SetAnalysis(visible with
                        {
                            Status = $"{policy.DisplayName} · Full pass failed; " +
                                $"visible diagnostics retained · v{snapshot.Version}"
                        });
                }
            }));
    }

    /// <summary>
    /// Promotes only a certified whole-document result. A policy may correctly
    /// answer a Full request with Provisional coverage, which must not erase the
    /// more relevant visible-page analysis or claim a global problem count.
    /// </summary>
    private void PublishIdleFullAnalysis(NativeFormatSessionDriver driver, Document document,
        IDocumentPolicy policy, TextSnapshot snapshot, DocumentAnalysis result,
        Mote.Formats.TextSpan visibleRange)
    {
        if (_disposed || !ReferenceEquals(driver, _sessionDriver) ||
            !ReferenceEquals(document, _document) || !ReferenceEquals(policy, _policy) ||
            snapshot.Version != _document.Snapshot.Version || result.Version != snapshot.Version ||
            visibleRange.Start != _pageStart || visibleRange.Length != _pageLength)
        {
            MoteTelemetry.Record(TelemetryEvent.AnalysisDiscarded);
            return;
        }
        if (result.Completeness != AnalysisCompleteness.Complete)
        {
            // The Full request did run, but the policy could not certify the
            // entire file. Keep visible facts and make the finite result clear.
            if (_visibleSessionAnalysis is { } visible)
                _shell.SetAnalysis(visible with
                {
                    Status = $"{policy.DisplayName} · Full pass {result.Completeness}; " +
                        $"global diagnostics unknown · v{result.Version}"
                });
            return;
        }

        using var present = MoteTelemetry.Start(TelemetryOperation.AnalysisToPresentation,
            Dimensions(snapshot));
        var tokens = ProjectTokens(result.Tokens, _pageStart, _pageLength, _projection!);
        var preview = NativePreviewBuilder.Build(result, policy.Kind, snapshot,
            _pageStart, _pageLength);
        _shell.SetAnalysis(new NativeAnalysisView(tokens,
            SessionDiagnosticSummary(result, _pageStart, _pageLength),
            preview.Text, $"{policy.DisplayName} · Complete · v{result.Version}", preview.Spans));
        _canvasShell?.SetCanvasSemantics(new NativeCanvasSemantics(result.Version,
            result.Completeness, result.Coverage,
            VisibleSourceTokens(result.Tokens, _pageStart, _pageLength),
            VisibleSourceDiagnostics(result.Diagnostics, _pageStart, _pageLength)));
        MoteTelemetry.Record(TelemetryEvent.AnalysisPublished,
            dimensions: Dimensions(snapshot));
    }

    private void SelectPolicy(IDocumentPolicy policy)
    {
        if (ReferenceEquals(policy, _policy)) return;
        ++_formatSerial;
        CancelAnalysis();
        _idleFullAnalysis?.Dispose();
        _visibleSessionAnalysis = null;
        _sessionDriver?.Dispose();
        _policy = policy;
        _sessionDriver = CreateSessionDriver(policy);
        _idleFullAnalysis = CreateIdleFullAnalysis(_sessionDriver, _document, _policy);
    }

    private static IReadOnlyList<SemanticToken> ProjectTokens(IReadOnlyList<SemanticToken> tokens,
        int analysisStart, int pageLength, NativeTextProjection projection)
    {
        var projected = new List<SemanticToken>(Math.Min(tokens.Count, 4096));
        var pageEnd = analysisStart + pageLength;
        foreach (var token in tokens)
        {
            if (projected.Count >= 4096) break;
            var start = Math.Max(token.Span.Start, analysisStart);
            var end = Math.Min(token.Span.End, pageEnd);
            if (start >= end) continue;
            var displayStart = projection.ToDisplay(start - analysisStart);
            var displayEnd = projection.ToDisplay(end - analysisStart);
            if (displayEnd > displayStart)
                projected.Add(new SemanticToken(token.Kind,
                    new Mote.Formats.TextSpan(displayStart, displayEnd - displayStart)));
        }
        return projected;
    }

    /// <summary>Keeps only bounded absolute semantic spans needed by the canvas.</summary>
    private static IReadOnlyList<SemanticToken> VisibleSourceTokens(
        IReadOnlyList<SemanticToken> tokens, int start, int length)
    {
        var end = start + length;
        var visible = new List<SemanticToken>(Math.Min(tokens.Count, 4096));
        foreach (var token in tokens)
        {
            if (visible.Count == 4096) break;
            var clippedStart = Math.Max(start, token.Span.Start);
            var clippedEnd = Math.Min(end, token.Span.End);
            if (clippedEnd > clippedStart)
                visible.Add(new SemanticToken(token.Kind,
                    new Mote.Formats.TextSpan(clippedStart, clippedEnd - clippedStart)));
        }
        visible.Sort(static (left, right) => left.Span.Start.CompareTo(right.Span.Start));
        return visible;
    }

    /// <summary>Keeps at most one visible-page batch of absolute diagnostic spans.</summary>
    private static IReadOnlyList<Diagnostic> VisibleSourceDiagnostics(
        IReadOnlyList<Diagnostic> diagnostics, int start, int length)
    {
        var end = start + length;
        var visible = new List<Diagnostic>(Math.Min(diagnostics.Count, 4096));
        foreach (var diagnostic in diagnostics)
        {
            if (visible.Count == 4096) break;
            var span = diagnostic.Span;
            if (span.Length == 0 ? span.Start >= start && span.Start <= end :
                span.Start < end && span.End > start)
                visible.Add(diagnostic);
        }
        visible.Sort(static (left, right) => left.Span.Start.CompareTo(right.Span.Start));
        return visible;
    }

    private static string DiagnosticSummary(IReadOnlyList<Diagnostic> diagnostics)
    {
        if (diagnostics.Count == 0) return "No diagnostics.";
        var errors = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        var warnings = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
        var first = diagnostics[0];
        return $"{errors} errors · {warnings} warnings · {first.Code}: {first.Message}";
    }

    private void CancelAnalysis()
    {
        _analysisCancellation?.Cancel();
        _analysisCancellation?.Dispose();
        _analysisCancellation = null;
    }

    private void InvalidateFind()
    {
        _findCancellation?.Cancel();
        _findCancellation?.Dispose();
        _findCancellation = null;
        ++_findSerial;
        if (_operationStatus == "Searching document…") _operationStatus = "";
    }

    private void Closing(object? sender, NativeClosingEventArgs e)
    {
        if (!_shell.CommitPendingText()) { e.Cancel = true; return; }
        if (_saving) { e.Cancel = true; return; }
        if (_document.IsModified && !_shell.ConfirmDiscard()) e.Cancel = true;
    }

    private void Post(Action action) => TryPost(action);

    private bool TryPost(Action action)
    {
        try { _shell.Post(action); return true; }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException) { return false; }
    }

    /// <summary>
    /// Every asynchronous UI completion that can mutate the engine or current projection
    /// first settles native IME preedit against the still-current document. Callers then
    /// recheck their captured document identity and version before applying the result.
    /// </summary>
    private bool SettleInputBeforeAsyncResult() => !_disposed && _shell.CommitPendingText();

    private static TelemetryDimensions Dimensions(TextSnapshot snapshot) =>
        new(DocumentBytes: snapshot.Length * sizeof(char), Version: snapshot.Version);
}
