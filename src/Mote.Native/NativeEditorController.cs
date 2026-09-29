using System.Text;
using Mote.Configuration;
using Mote.Engine;
using Mote.Formats;
using Mote.Telemetry;
using Mote.Themes;

namespace Mote.Native;

/// <summary>
/// Composes the canonical document engine with one bounded native text viewport.
/// The platform shell never owns persistence state or a second document model.
/// </summary>
internal sealed class NativeEditorController : IDisposable
{
    internal const int PageSize = 64 * 1024;
    internal const int PageSlack = 8 * 1024;
    private const int FullAnalysisLimit = 2 * 1024 * 1024;
    private readonly INativeEditorShell _shell;
    private readonly MoteConfiguration _configuration;
    private readonly IThemePolicy _theme;
    private readonly string? _startupPath;
    private Document _document = new();
    private NativeNavigationModel _navigation = new();
    private string? _findQuery;
    private CancellationTokenSource? _findCancellation;
    private long _findSerial;
    private long _clipboardSerial;
    private bool _projectingSelection;
    private bool _nativeProjectsGlobalSelection;
    private IDocumentPolicy _policy = DocumentPolicies.ForKind(DocumentKind.PlainText);
    private NativeFormatSessionDriver? _sessionDriver;
    private NativeTextProjection? _projection;
    private CancellationTokenSource? _analysisCancellation;
    private long _analysisSerial;
    private int _pageStart;
    private int _pageLength;
    private int? _requestedCaretSource;
    private int _openSerial;
    private long _formatSerial;
    private string _operationStatus = "";
    private bool _saving;
    private bool _disposed;

    /// <summary>Wires platform events to engine transactions and static format policies.</summary>
    public NativeEditorController(INativeEditorShell shell, MoteConfiguration configuration,
        IThemePolicy theme, string? startupPath)
    {
        _shell = shell;
        _configuration = configuration;
        _theme = theme;
        _startupPath = startupPath;
        _sessionDriver = CreateSessionDriver(_policy);
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
        _document.Changed -= DocumentChanged;
        _sessionDriver?.Dispose();
        _findCancellation?.Cancel();
        _findCancellation?.Dispose();
        _document.Dispose();
    }

    private void Shown()
    {
        _shell.SetTheme(_theme);
        ShowDocument();
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
        _document.Changed -= DocumentChanged;
        _sessionDriver?.Dispose();
        _document.Dispose();
        _document = replacement;
        _document.Changed += DocumentChanged;
        _navigation = new NativeNavigationModel();
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
        var file = _document.FilePath is { } path ? Path.GetFileName(path) : "Untitled";
        var title = $"{file}{(_document.IsModified ? " •" : "")} — mote";
        var pageStatus = snapshot.Length <= PageSize ? "" :
            $"Page {_pageStart:N0}–{_pageStart + _pageLength:N0} / {snapshot.Length:N0}; page navigation is discrete";
        var warnings = _configuration.Diagnostics.Count == 0 ? "" :
            $" · Config warnings: {_configuration.Diagnostics.Count}";
        var health = MoteTelemetry.Health;
        var traceWarning = health.SinkFaulted || health.DroppedRecords > 0 ? " · Trace degraded" : "";
        _shell.SetDocument(new NativeDocumentView(title, _projection.Display, _pageStart,
            snapshot.Length, _document.IsModified, pageStatus + warnings + traceWarning +
            (_operationStatus.Length == 0 ? "" : " · " + _operationStatus), focus));
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
                await Task.Delay(80, cancellation.Token).ConfigureAwait(false);
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
                    var diagnostics = SessionDiagnosticSummary(result);
                    var preview = NativePreviewBuilder.Build(result, policy.Kind, snapshot,
                        pageStart, pageLength);
                    _shell.SetAnalysis(new NativeAnalysisView(tokens, diagnostics,
                        preview.Text, $"{policy.DisplayName} · {result.Completeness} · v{result.Version}",
                        preview.Spans));
                    MoteTelemetry.Record(TelemetryEvent.AnalysisPublished,
                        dimensions: Dimensions(snapshot));
                    MoteTelemetry.RecordElapsed(TelemetryOperation.EditToPresentation,
                        editMark, Dimensions(snapshot));
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

    private static string SessionDiagnosticSummary(DocumentAnalysis result)
    {
        if (result.Completeness != AnalysisCompleteness.Complete)
            return $"{result.Completeness} in {result.Coverage.Start:N0}–" +
                $"{result.Coverage.End:N0}; global diagnostics unknown. " +
                (result.Diagnostics.Count == 0 ? "No diagnostics in current projection." :
                    DiagnosticSummary(result.Diagnostics));
        var count = result.TotalDiagnosticCount ?? result.Diagnostics.Count;
        if (count == 0) return "No diagnostics.";
        if (result.Diagnostics.Count == 0)
            return $"{count:N0} document diagnostics; none in displayed viewport.";
        var shown = DiagnosticSummary(result.Diagnostics);
        return $"{count:N0} document diagnostics; shown: {shown}";
    }

    private void DocumentChanged(object? sender, DocumentChangedEventArgs change)
    {
        _sessionDriver?.Record(change);
        _navigation.ApplyChange(change.Change, change.After);
    }

    private static NativeFormatSessionDriver? CreateSessionDriver(IDocumentPolicy policy) =>
        policy is IIncrementalDocumentPolicy incremental
            ? new NativeFormatSessionDriver(incremental) : null;

    private void SelectPolicy(IDocumentPolicy policy)
    {
        if (ReferenceEquals(policy, _policy)) return;
        ++_formatSerial;
        CancelAnalysis();
        _sessionDriver?.Dispose();
        _policy = policy;
        _sessionDriver = CreateSessionDriver(policy);
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
