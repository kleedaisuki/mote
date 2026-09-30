using Mote.Engine;
using Mote.Formats;
using Mote.Telemetry;

namespace Mote.Native;

/// <summary>Source-backed table commands and bounded interests for the single Engine document.</summary>
internal sealed partial class NativeEditorController
{
    /// <summary>Null follows source; detached ordinals are valid only until a source mutation.</summary>
    private CsvGridAnchor.Row? _gridAnchor;
    private GridRange _gridColumns = new(0, 16);
    private int _gridRowLimit = 64;
    private CancellationTokenSource? _gridCopyCancellation;

    /// <summary>Stops obsolete bounded clipboard preparation rather than retaining a backlog of payloads.</summary>
    private void CancelGridCopy()
    {
        _gridCopyCancellation?.Cancel();
        _gridCopyCancellation?.Dispose();
        _gridCopyCancellation = null;
    }

    /// <summary>Forgets coordinates after edits/format/lifetime changes rather than shifting quote-sensitive rows.</summary>
    private void ResetGridInterest()
    {
        RetireGridNavigation();
        CancelGridCopy();
        _gridAnchor = null;
        _gridColumns = new GridRange(0, 16);
        _gridRowLimit = 64;
        ++_clipboardSerial;
    }

    /// <summary>Captures UI-owned interest before entering the serialized background session lane.</summary>
    private CsvGridRequest CreateGridRequest(TextSnapshot snapshot, AnalysisRequest request) =>
        new([request.VisibleRange], _gridAnchor ?? (CsvGridAnchor)new CsvGridAnchor.Source(
            Math.Clamp(_navigation.Active, 0, snapshot.Length)), _gridRowLimit, _gridColumns, request.Scope);

    /// <summary>Rejects stale document and same-version native geometry before any source/clipboard action.</summary>
    private GridRenderProjection? CurrentGrid(NativePresentationId identity)
    {
        if (_disposed || _gridPending || _shell.IsTextComposing || _canvasShell?.IsCanvasComposing == true ||
            _presentedPreview is not { ShowPreview: true, Grid: { } grid } view ||
            view.Identity != identity || identity.Document != new NativeDocumentStamp(
                _canvasGeneration, _document.Snapshot.Version)) return null;
        return grid;
    }

    /// <summary>Coalesces table navigation without parsing in native paint or accessibility callbacks.</summary>
    private void GridWindowRequested(NativeGridWindowRequest request)
    {
        if (CurrentGrid(request.Identity) is not { } grid || !request.FollowSource && request.Row < 0 ||
            request.RowLimit is < 1 or > GridRenderProjection.MaxRows ||
            request.Columns.Count is < 1 or > GridRenderProjection.MaxColumns ||
            (long)request.RowLimit * request.Columns.Count > GridRenderProjection.MaxCells) return;
        if (!request.FollowSource && grid.Extent.ExactRowCount is { } count && request.Row >= count) return;
        _gridAnchor = request.FollowSource ? null : new CsvGridAnchor.Row(request.Row);
        _gridColumns = request.Columns;
        _gridRowLimit = request.RowLimit;
        CancelGridCopy();
        ++_clipboardSerial;
        // Keep the bounded readable table until the new candidate arrives, while
        // invalidating previously queued actions even at the same source version.
        if (_presentedPreview is { } old)
            PresentAnalysis(old with { Status = old.Status + " · requested Grid window pending" });
        ScheduleAnalysis(gridViewport: true);
    }

    /// <summary>Dispatches explicit table actions; ordinary selection neither changes nor focuses source.</summary>
    private void GridIntentRequested(NativeGridIntent intent)
    {
        if (intent.Kind == NativeGridIntentKind.Replace && !_disposed && !_shell.CommitPendingText()) return;
        if (CurrentGrid(intent.Identity) is not { } grid) return;
        if (intent.Kind == NativeGridIntentKind.Select)
        {
            CancelGridCopy();
            ++_clipboardSerial;
            return;
        }
        if (intent.Kind == NativeGridIntentKind.Reveal)
        {
            var cell = NativeCsvGrid.Row(grid, intent.Row) is { } row ? NativeCsvGrid.Cell(row, intent.Column) : null;
            if (cell?.SourceRange is not { } span) { _shell.ShowError("This cell has no proved source field yet."); return; }
            InvalidateFind();
            _navigation.SetSelection(_document.Snapshot, span.Start, span.End);
            _nativeProjectsGlobalSelection = false;
            RevealSelection();
            ProjectSelection();
            _shell.FocusSource();
            return;
        }
        if (intent.Kind == NativeGridIntentKind.Replace) { ReplaceGridCell(grid, intent); return; }
        CopyGrid(grid, intent);
    }

    /// <summary>Prepares bounded clipboard data off-thread and rechecks the installed map before publishing.</summary>
    private void CopyGrid(GridRenderProjection grid, NativeGridIntent intent)
    {
        CancelGridCopy();
        var cancellation = new CancellationTokenSource();
        _gridCopyCancellation = cancellation;
        var token = cancellation.Token;
        var snapshot = _document.Snapshot;
        var document = _document;
        var serial = ++_clipboardSerial;
        _ = Task.Run(() =>
        {
            NativeCsvGridCommandResult result;
            try { result = NativeCsvGridCommands.PrepareCopy(snapshot, grid, intent, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception error) when (error is not OutOfMemoryException)
            { result = new(false, null, null, "The selected table data could not be prepared."); }
            Post(() =>
            {
                if (_disposed || token.IsCancellationRequested || serial != _clipboardSerial || !ReferenceEquals(document, _document) ||
                    CurrentGrid(intent.Identity) is null) return;
                if (!result.Success || result.Payload is null)
                { _shell.ShowError(result.Error ?? "The selection is not ready for Copy."); return; }
                try { _shell.SetClipboardText(result.Payload); }
                catch (Exception error) when (error is not OutOfMemoryException)
                { _shell.ShowError("The system clipboard rejected the prepared table data; source was not changed."); }
            });
        });
    }

    /// <summary>One explicit field replacement is one Engine transaction, with a second stale-map check after the dialog.</summary>
    private void ReplaceGridCell(GridRenderProjection grid, NativeGridIntent intent)
    {
        var row = NativeCsvGrid.Row(grid, intent.Row);
        var cell = row is null ? null : NativeCsvGrid.Cell(row, intent.Column);
        if (cell is not { State: GridValueState.Complete, HasSyntaxError: false })
        { _shell.ShowError("Replace cell requires a complete, syntax-valid bounded field; edit this value in source."); return; }
        if (CurrentGrid(intent.Identity) is null) return;
        var snapshot = _document.Snapshot;
        var current = NativeCsvGridCommands.PrepareCopy(snapshot, grid, intent with { Kind = NativeGridIntentKind.CopyValue });
        if (!current.Success || current.Payload is null)
        { _shell.ShowError(current.Error ?? "The field value is not available."); return; }
        var value = _shell.PromptGridReplacement(current.Payload);
        if (value is null) return;
        if (CurrentGrid(intent.Identity) is null)
        { _shell.ShowError("Replace cell cancelled: the source or table changed while the value editor was open."); return; }
        var replacement = NativeCsvGridCommands.PrepareReplace(snapshot, grid, intent, value);
        if (!replacement.Success || replacement.Change is not { } change)
        { _shell.ShowError(replacement.Error ?? "The field cannot be safely replaced."); return; }
        InvalidateFind();
        var mark = MoteTelemetry.Mark();
        try { ApplyTraced(change, mark); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { _shell.ShowError("Replace cell could not safely change the source; retry after current analysis."); return; }
        _navigation.MoveCaret(_document.Snapshot, change.Start + change.InsertText.Length);
        // Structured edits bypass the bounded native text-change callback, so
        // recompute page capacity instead of retaining its old source length.
        _pageLength = 0;
        TraceSourceDraw(MoteTelemetry.Fork(mark));
        _requestedCaretSource = _navigation.Active;
        RevealSelection();
        ShowDocument(mark);
        ProjectSelection();
        ScheduleAnalysis(mark);
    }
}
