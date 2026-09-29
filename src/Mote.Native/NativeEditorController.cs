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
    private IDocumentPolicy _policy = DocumentPolicies.ForKind(DocumentKind.PlainText);
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
        shell.TextChanged += Edited;
        shell.NewRequested += New;
        shell.OpenRequested += Open;
        shell.SaveRequested += Save;
        shell.SaveAsRequested += SaveAs;
        shell.UndoRequested += Undo;
        shell.RedoRequested += Redo;
        shell.FormatRequested += Format;
        shell.PagePreviousRequested += PreviousPage;
        shell.PageNextRequested += NextPage;
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
        _document.Dispose();
        _document = replacement;
        ++_formatSerial;
        _operationStatus = "";
        _policy = replacement.FilePath is { } path
            ? DocumentPolicies.ForPath(path)
            : DocumentPolicies.ForKind(DocumentKind.PlainText);
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
                if (error is not null) _shell.ShowError($"Save failed; the original file was retained. {error.Message}");
                else if (!cancelled)
                {
                    _policy = document.FilePath is { } savedPath
                        ? DocumentPolicies.ForPath(savedPath) : _policy;
                    MoteTelemetry.Record(TelemetryEvent.SaveCompleted);
                    ShowDocument();
                    ScheduleAnalysis();
                }
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
            _document.Apply(new TextChange(_pageStart + change.Start, change.DeleteLength, change.InsertText));
            var newCaret = _pageStart + change.Start + change.InsertText.Length;
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
        if (_document.Undo()) { _pageLength = 0; ShowDocument(); ScheduleAnalysis(); }
    }

    private void Redo()
    {
        if (_document.Redo()) { _pageLength = 0; ShowDocument(); ScheduleAnalysis(); }
    }

    private void Format()
    {
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
        if (_pageStart == 0) return;
        _pageStart = Math.Max(0, _pageStart - PageSize);
        _pageLength = 0;
        _requestedCaretSource = _pageStart;
        ShowDocument();
        ScheduleAnalysis();
    }

    private void NextPage()
    {
        var length = _document.Snapshot.Length;
        if (_pageStart + _pageLength >= length) return;
        _pageStart += _pageLength;
        _pageLength = 0;
        _requestedCaretSource = _pageStart;
        ShowDocument();
        ScheduleAnalysis();
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
            $"Page {_pageStart:N0}–{_pageStart + _pageLength:N0} / {snapshot.Length:N0}; whole-document selection unavailable";
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

    private void Closing(object? sender, NativeClosingEventArgs e)
    {
        if (_saving) { e.Cancel = true; return; }
        if (_document.IsModified && !_shell.ConfirmDiscard()) e.Cancel = true;
    }

    private void Post(Action action) => TryPost(action);

    private bool TryPost(Action action)
    {
        try { _shell.Post(action); return true; }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException) { return false; }
    }

    private static TelemetryDimensions Dimensions(TextSnapshot snapshot) =>
        new(DocumentBytes: snapshot.Length * sizeof(char), Version: snapshot.Version);
}
