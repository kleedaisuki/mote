using Mote.Formats;

namespace Mote.Native;

/// <summary>Document-scoped logical coordinates and per-gesture navigation authority.</summary>
internal sealed partial class NativeEditorController
{
    /// <summary>Retired by content/lifetime/geometry changes, never a palette refresh.</summary>
    private long _gridEpoch = 1;
    /// <summary>Nonreused gesture sequence across both coordinate axes.</summary>
    private long _gridGestureSequence;
    /// <summary>Latest accepted delivery interest; native frames expose this for reentrancy guards.</summary>
    private long _gridRequestSerial;
    /// <summary>Only this exact token may dispatch another phase.</summary>
    private NativeGridGesture? _gridGesture;
    /// <summary>Last controller-owned navigation frame, separately from ready command identity.</summary>
    private NativeGridScrollFrame? _gridFrame;
    /// <summary>Last accepted ready placement for fail-closed native-install recovery.</summary>
    private NativeGridScrollFrame? _gridDeliveredFrame;
    /// <summary>Immutable same-version payload used only to restore a canceled/failed placement.</summary>
    private NativeAnalysisView? _gridDeliveredView;
    /// <summary>Null retains source-follow mode when a canceled navigation restores ready data.</summary>
    private CsvGridAnchor.Row? _gridDeliveredAnchor;
    /// <summary>Session-proved coordinate facts for the current document version.</summary>
    private GridExtent _gridExtent;
    /// <summary>Largest observed row width, only a lower bound while totals are unknown.</summary>
    private int _gridObservedColumns;
    /// <summary>Measured fully visible row page, admitted against bounded delivery.</summary>
    private int _gridVisibleRows = 24;
    /// <summary>Measured fully visible column page, not the default cache capacity.</summary>
    private int _gridVisibleColumns = 4;
    /// <summary>Retires old ready commands while a new target is loading.</summary>
    private bool _gridPending;
    /// <summary>Unresolved symbolic End or exact numeric interest awaiting certification.</summary>
    private (NativeGridTargetKind Kind, int Row, int Column)? _gridTarget;
    /// <summary>One content/viewport/Full admission point before the driver gate.</summary>
    private NativeAnalysisDispatcher? _analysisDispatcher;
    /// <summary>Edit-owned work survives same-version viewport cancellation.</summary>
    private CancellationTokenSource? _csvContentCancellation;
    /// <summary>A source version registers its mandatory content pass once.</summary>
    private long? _csvContentVersion;
    /// <summary>One short UI-mailbox coalescing timer, never one task per thumb sample.</summary>
    private bool _gridDispatchPending;
    /// <summary>Retires queued coalescing callbacks independently of parser serials.</summary>
    private long _gridDispatchSerial;
    /// <summary>Visible Grid-specific indexing outcome, separate from settings/accessibility notices.</summary>
    private string? _gridIndexNotice;

    /// <summary>Retires all coordinate authority before native callbacks can reenter.</summary>
    private void RetireGridNavigation()
    {
        ++_gridEpoch;
        ++_gridRequestSerial;
        _gridGesture = null;
        _gridFrame = null;
        _gridDeliveredFrame = null;
        _gridDeliveredView = null;
        _gridDeliveredAnchor = null;
        _gridTarget = null;
        _gridIndexNotice = null;
        _gridPending = false;
        _gridExtent = default;
        _gridObservedColumns = 0;
        _gridDispatchPending = false;
        ++_gridDispatchSerial;
        _csvContentCancellation?.Cancel();
        _csvContentCancellation?.Dispose();
        _csvContentCancellation = null;
        _csvContentVersion = null;
        _idleFullAnalysis?.Cancel();
        _shell.SetGridNavigation(null);
    }

    /// <summary>Captures exact extent facts without treating a ready subset as the entire file.</summary>
    private NativeGridScrollFrame MakeGridFrame(NativePresentationId? ready, GridRenderProjection? grid = null)
    {
        if (grid is not null)
        {
            // Same-version certification is monotonic, including cancellation
            // restoring an older bounded payload after Full learned exact totals.
            var incoming = grid.Extent;
            _gridExtent = new(Math.Max(_gridExtent.CertifiedPrefixRows, incoming.CertifiedPrefixRows),
                incoming.ExactRowCount ?? _gridExtent.ExactRowCount,
                incoming.ExactMaxWidth ?? _gridExtent.ExactMaxWidth);
            foreach (var row in grid.Rows) _gridObservedColumns = Math.Max(_gridObservedColumns, row.Width);
        }
        var kind = _gridExtent.ExactRowCount.HasValue ? NativeGridExtentKind.Exact :
            _gridExtent.CertifiedPrefixRows > 0 ? NativeGridExtentKind.Prefix : NativeGridExtentKind.Unavailable;
        var rows = _gridExtent.ExactRowCount ?? _gridExtent.CertifiedPrefixRows;
        var columns = _gridExtent.ExactMaxWidth ?? _gridObservedColumns;
        var unresolvedSource = _gridAnchor is null && grid is { RequestedRows.Count: 0,
            RequestedAnchor: CsvGridAnchor.Source } && !_gridExtent.ExactRowCount.HasValue;
        if (unresolvedSource) { kind = NativeGridExtentKind.Unavailable; rows = 0; }
        var first = _gridAnchor?.Ordinal ?? grid?.RequestedRows.Start ?? _gridFrame?.Rows.First ?? 0;
        var rowAxis = NativeGridScrollAxis.Create(kind, rows, _gridVisibleRows, first);
        var columnAxis = NativeGridScrollAxis.Create(kind, columns, _gridVisibleColumns, _gridColumns.Start);
        if (_gridGesture is { } gesture)
        {
            rowAxis = gesture.Frame.Rows with { First = Math.Min(first, gesture.Frame.Rows.Last) };
            columnAxis = gesture.Frame.Columns with { First = Math.Min(_gridColumns.Start, gesture.Frame.Columns.Last) };
        }
        var count = Math.Min(_gridRowLimit, Math.Max(0, rows - rowAxis.First));
        var columnCount = Math.Min(_gridColumns.Count, Math.Max(0, columns - columnAxis.First));
        var status = kind == NativeGridExtentKind.Exact
            ? $"File rows {rows:N0}; file columns {columns:N0}"
            : $"Indexed prefix rows {rows:N0}; known columns {columns:N0}; file totals unknown";
        status += $" · {(_gridPending ? "requested" : "shown")} window rows {GridCoordinateLabel(rowAxis.First, count)}; " +
            $"columns {GridCoordinateLabel(columnAxis.First, columnCount)}";
        if (_gridPending)
        {
            status += " · loading";
            if (_gridDeliveredFrame is { } delivered)
                status += $"; last ready rows {GridCoordinateLabel(delivered.RequestedRows.Start, delivered.RequestedRows.Count)}";
            if (_gridTarget is { } target) status += target.Kind == NativeGridTargetKind.End
                ? "; End pending certification"
                : $"; unproved target row {(long)target.Row + 1:N0}, column {(long)target.Column + 1:N0}";
        }
        if (unresolvedSource) status = "Indexing; source anchor row coordinates unavailable; file totals unknown";
        if (_gridIndexNotice is not null) status += " · " + _gridIndexNotice;
        return new(new(new(_canvasGeneration, _document.Snapshot.Version), _gridEpoch), ready,
            rowAxis, columnAxis, new(rowAxis.First, count), new(columnAxis.First, columnCount),
            _gridRequestSerial, _gridPending, status);
    }

    /// <summary>One-based user labels use wide arithmetic; zero slots are explicitly empty.</summary>
    private static string GridCoordinateLabel(int first, int count) => count == 0 ? "none" :
        $"{(long)first + 1:N0}–{(long)first + count:N0}";

    /// <summary>Begins atomically: late phases of A cannot acquire the authority of B.</summary>
    private NativeGridGesture? BeginGridGesture(NativeGridGestureBegin begin)
    {
        if (_disposed || _shell.IsTextComposing || _canvasShell?.IsCanvasComposing == true ||
            _gridFrame is not { } current || begin.Frame != current ||
            current.Navigation.Document != new NativeDocumentStamp(_canvasGeneration, _document.Snapshot.Version))
        {
            if (_shell.IsTextComposing || _canvasShell?.IsCanvasComposing == true) RetireGridNavigation();
            return null;
        }
        _gridGesture = null;
        _gridTarget = null;
        ++_gridRequestSerial;
        CancelAnalysis(preservePreview: true);
        CancelGridCopy();
        ++_clipboardSerial;
        var page = NativeGridPlanner.Page(begin.VisibleRows, begin.VisibleColumns);
        if (page.Rows != _gridVisibleRows || page.Columns != _gridVisibleColumns) ++_gridEpoch;
        _gridVisibleRows = page.Rows; _gridVisibleColumns = page.Columns;
        _gridRowLimit = Math.Max(64, page.Rows);
        var columns = Math.Max(16, page.Columns);
        _gridRowLimit = Math.Min(_gridRowLimit, GridRenderProjection.MaxCells / columns);
        _gridColumns = new(_gridColumns.Start, Math.Min(columns, int.MaxValue - _gridColumns.Start));
        _gridFrame = MakeGridFrame(current.Ready);
        var admitted = new NativeGridGesture(new(_gridFrame.Navigation, ++_gridGestureSequence), _gridFrame);
        _gridGesture = admitted;
        _shell.SetGridNavigation(_gridFrame);
        return _gridGesture?.Id == admitted.Id ? admitted : null;
    }

    /// <summary>Geometry changes retire old tokens without discarding same-version certified facts.</summary>
    private void GridGeometryChanged(int rows, int columns)
    {
        var page = NativeGridPlanner.Page(rows, columns);
        if (_disposed || _gridFrame is null || page == (_gridVisibleRows, _gridVisibleColumns)) return;
        _gridGesture = null;
        ++_gridEpoch; ++_gridRequestSerial;
        _gridVisibleRows = page.Rows; _gridVisibleColumns = page.Columns;
        _gridRowLimit = Math.Max(64, page.Rows);
        var capacity = Math.Max(16, page.Columns);
        _gridRowLimit = Math.Min(_gridRowLimit, GridRenderProjection.MaxCells / capacity);
        _gridColumns = new(_gridColumns.Start, Math.Min(capacity, int.MaxValue - _gridColumns.Start));
        CancelGridCopy(); ++_clipboardSerial;
        CancelAnalysis();
        _gridPending = true;
        _gridFrame = MakeGridFrame(null);
        _shell.SetGridNavigation(_gridFrame);
        ScheduleAnalysis(gridViewport: true);
    }

    /// <summary>Consumes terminal authority before work; reentrant newer gestures survive the tail.</summary>
    private void GridGestureRequested(NativeGridGestureAction action)
    {
        if (_disposed || _gridGesture is not { } gesture || action.Id != gesture.Id ||
            action.Id.Navigation.Document != new NativeDocumentStamp(_canvasGeneration, _document.Snapshot.Version) ||
            action.Row < 0 || action.Column < 0 || !Enum.IsDefined(action.Phase) || !Enum.IsDefined(action.Kind)) return;
        if (_shell.IsTextComposing || _canvasShell?.IsCanvasComposing == true) { RetireGridNavigation(); return; }
        if (action.Phase != NativeGridGesturePhase.Track) _gridGesture = null;
        if (action.Phase == NativeGridGesturePhase.Cancel)
        {
            ++_gridRequestSerial;
            _gridTarget = null;
            if (!_gridPending)
            {
                _gridFrame = MakeGridFrame(_presentedPreview?.Identity);
                _shell.SetGridNavigation(_gridFrame);
                return;
            }
            CancelAnalysis();
            _gridDispatchPending = false; ++_gridDispatchSerial;
            if (_gridDeliveredFrame is { } delivered && _gridDeliveredView is { } view)
            {
                _gridAnchor = _gridDeliveredAnchor;
                _gridColumns = new(delivered.Columns.First, Math.Min(_gridColumns.Count,
                    int.MaxValue - delivered.Columns.First));
                _gridPending = false;
                PresentAnalysis(view);
                return;
            }
            ScheduleAnalysis(gridViewport: true);
            return;
        }
        var rows = _gridExtent.ExactRowCount ?? _gridExtent.CertifiedPrefixRows;
        var columns = _gridExtent.ExactMaxWidth ?? _gridObservedColumns;
        var unknown = !_gridExtent.ExactRowCount.HasValue;
        if (action.Kind == NativeGridTargetKind.Retry || unknown &&
            (action.Kind == NativeGridTargetKind.End || action.Row >= rows || action.Column >= columns))
        {
            _gridTarget = (action.Kind, action.Row, action.Column);
            _gridPending = true;
            ++_gridRequestSerial;
            CancelAnalysis();
            CancelGridCopy(); ++_clipboardSerial;
            _gridFrame = MakeGridFrame(null);
            var serial = _gridRequestSerial;
            _shell.SetGridNavigation(_gridFrame);
            if (serial != _gridRequestSerial || _disposed) return;
            var outcome = _idleFullAnalysis?.Demand(_document.Snapshot,
                new(_pageStart, _pageLength), action.Kind == NativeGridTargetKind.Retry);
            if (outcome is IdleFullOffer.MemoryLimited or IdleFullOffer.Deferred or IdleFullOffer.Failed)
                SetGridIndexNotice("CSV indexing deferred or failed; Retry indexing explicitly.");
            else if (action.Kind == NativeGridTargetKind.Retry) SetGridIndexNotice(null);
            return;
        }
        if (action.Kind == NativeGridTargetKind.Cell && (action.Row >= rows || action.Column >= columns))
        {
            _gridFrame = MakeGridFrame(_presentedPreview?.Identity);
            _shell.SetGridNavigation(_gridFrame);
            _shell.ShowError("The requested CSV cell is outside the certified file extent.");
            return;
        }
        var row = action.Kind == NativeGridTargetKind.End ? Math.Max(0, rows - Math.Min(rows, _gridVisibleRows)) : action.Row;
        var rowAxis = NativeGridScrollAxis.Create(gesture.Frame.Rows.Kind, rows, _gridVisibleRows, row);
        var columnAxis = NativeGridScrollAxis.Create(gesture.Frame.Columns.Kind, columns, _gridVisibleColumns, action.Column);
        _gridAnchor = new CsvGridAnchor.Row(rowAxis.First);
        _gridColumns = new(columnAxis.First, Math.Min(_gridColumns.Count, int.MaxValue - columnAxis.First));
        ++_gridRequestSerial;
        _gridPending = true;
        CancelAnalysis();
        CancelGridCopy(); ++_clipboardSerial;
        _gridFrame = MakeGridFrame(null);
        var requestSerial = _gridRequestSerial;
        _shell.SetGridNavigation(_gridFrame);
        if (requestSerial == _gridRequestSerial && !_disposed) QueueGridAnalysis();
    }

    /// <summary>One latest-interest mailbox crosses a short dispatch interval without parsing on UI callbacks.</summary>
    private void QueueGridAnalysis()
    {
        if (_gridDispatchPending) return;
        _gridDispatchPending = true;
        var serial = ++_gridDispatchSerial;
        _ = Task.Run(async () =>
        {
            await Task.Delay(8).ConfigureAwait(false);
            Post(() =>
            {
                if (_disposed || serial != _gridDispatchSerial) return;
                _gridDispatchPending = false;
                ScheduleAnalysis(gridViewport: true);
            });
        });
    }

    /// <summary>A failed delivery has no command authority and never reports its requested target as ready.</summary>
    private void GridDeliveryFailed()
    {
        _presentedPreview = null;
        _gridPending = true;
        _gridGesture = null;
        ++_gridRequestSerial;
        if (_gridDeliveredFrame is { } delivered)
        {
            _gridAnchor = _gridDeliveredAnchor;
            _gridColumns = new(delivered.Columns.First, Math.Min(_gridColumns.Count,
                int.MaxValue - delivered.Columns.First));
        }
        _gridFrame = MakeGridFrame(null, _gridDeliveredView?.Grid) with
        { Status = "CSV delivery failed; last known placement retained; retry navigation." };
        _shell.SetGridNavigation(_gridFrame);
    }

    /// <summary>Updates the nearby navigation status without overwriting unrelated persistent notices.</summary>
    private void SetGridIndexNotice(string? notice)
    {
        _gridIndexNotice = notice;
        if (_gridFrame is null) return;
        _gridFrame = MakeGridFrame(_gridPending ? null : _presentedPreview?.Identity);
        _shell.SetGridNavigation(_gridFrame);
    }

    /// <summary>Resolves symbolic interests only from certified totals; numeric commands fail rather than alias.</summary>
    private bool ResolveGridTarget(GridRenderProjection grid)
    {
        if (_gridTarget is not { } target || grid.Extent.ExactRowCount is not { } rows ||
            grid.Extent.ExactMaxWidth is not { } columns) return false;
        _gridTarget = null;
        if (target.Kind == NativeGridTargetKind.Cell && (target.Row >= rows || target.Column >= columns))
        { _shell.ShowError("The requested CSV cell is outside the certified file extent."); return false; }
        _gridAnchor = new CsvGridAnchor.Row(target.Kind == NativeGridTargetKind.End
            ? Math.Max(0, rows - Math.Min(rows, _gridVisibleRows))
            : Math.Min(target.Row, Math.Max(0, rows - Math.Min(rows, _gridVisibleRows))));
        var first = Math.Min(target.Column, Math.Max(0, columns - Math.Min(columns, _gridVisibleColumns)));
        _gridColumns = new(first, Math.Min(_gridColumns.Count, int.MaxValue - first));
        ScheduleAnalysis(gridViewport: true);
        return true;
    }
}
