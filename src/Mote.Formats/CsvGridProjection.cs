using System.Text;
using Mote.Engine;

namespace Mote.Formats;

/// <summary>CSV coordinate delivery shares the candidate cache and grammar with source analysis.</summary>
internal sealed partial class CsvIncrementalSession
{
    /// <summary>A proved next-record start and its logical ordinal, never a physical-line estimate.</summary>
    private readonly record struct RecordCheckpoint(int SourceStart, int Ordinal);
    /// <summary>A proved field start and actual column, including empty fields skipped in bulk.</summary>
    private readonly record struct FieldCheckpoint(int SourceStart, int Ordinal);
    private const int GridReplayBudget = 2 * VisibleScanBudget;

    /// <inheritdoc />
    public CsvGridAnalysis AnalyzeGrid(TextSnapshot snapshot, IReadOnlyList<VersionedEdit> changesSinceCommittedState,
        CsvGridRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(changesSinceCommittedState);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = NormalizeWindows(snapshot, request.SourceInterests, true);
        if (request.Anchor is CsvGridAnchor.Source source && source.Offset > snapshot.Length)
            throw new ArgumentOutOfRangeException(nameof(request));
        var furthest = normalized[^1];
        if (request.Anchor is CsvGridAnchor.Source anchor && anchor.Offset > furthest.End)
            furthest = new TextSpan(anchor.Offset, 0);
        var work = new ScanWork();
        var candidate = BuildCandidate(snapshot, changesSinceCommittedState, furthest, request.Scope, cancellationToken, work);
        var sourceAnalysis = Project(snapshot, candidate, normalized, request.Scope, false, cancellationToken, work);
        var grid = ProjectGrid(snapshot, candidate, request, cancellationToken, work);
        cancellationToken.ThrowIfCancellationRequested();
        _cache = candidate;
        _lastScannedSourceUnits = work.Units;
        return new CsvGridAnalysis(sourceAnalysis, grid);
    }

    /// <summary>Binary search compatible with Array.BinarySearch's exact/insertion encoding.</summary>
    private static int FindCheckpoint(RecordCheckpoint[] checkpoints, int value, bool ordinal)
    {
        var lo = 0;
        var hi = checkpoints.Length - 1;
        while (lo <= hi)
        {
            var mid = lo + (hi - lo) / 2;
            var key = ordinal ? checkpoints[mid].Ordinal : checkpoints[mid].SourceStart;
            if (key == value) return mid;
            if (key < value) lo = mid + 1; else hi = mid - 1;
        }
        return ~lo;
    }

    /// <summary>Locates certified records without retaining a second graph or scanning an unknown suffix.</summary>
    private static IEnumerable<(int Ordinal, Row Row)> GridRows(TextSnapshot snapshot, CacheState cache,
        CsvGridAnchor anchor, int limit, CancellationToken ct, ScanWork work)
    {
        var targetRow = anchor is CsvGridAnchor.Row rowAnchor ? rowAnchor.Ordinal : -1;
        var targetSource = anchor is CsvGridAnchor.Source sourceAnchor ? sourceAnchor.Offset : -1;
        if (targetRow >= cache.RowCount || targetSource >= cache.IndexedUntil && !cache.Complete)
            yield break;
        var delivered = 0;
        if (cache.Mode != CsvCacheMode.SparseFull)
        {
            var ordinal = 0;
            foreach (var segment in cache.Segments)
            {
                ct.ThrowIfCancellationRequested();
                if (targetRow >= ordinal + segment.Count || targetSource >= segment.After && segment.After < snapshot.Length)
                { ordinal += segment.Count; continue; }
                for (var i = 0; i < segment.Count; i++, ordinal++)
                {
                    var row = segment.Get(i);
                    if (ordinal < targetRow || row.After <= targetSource && row.After < snapshot.Length) continue;
                    yield return (ordinal, row);
                    if (++delivered == limit) yield break;
                }
            }
            yield break;
        }
        var search = FindCheckpoint(cache.Checkpoints, targetRow >= 0 ? targetRow : targetSource, targetRow >= 0);
        var checkpoint = cache.Checkpoints[search >= 0 ? search : Math.Max(0, ~search - 1)];
        // EOF belongs to the last existing record, not to the zero-row sentinel checkpoint.
        if (checkpoint.SourceStart == snapshot.Length && targetSource == snapshot.Length && cache.RowCount > 0)
        {
            var index = search >= 0 ? search : Math.Max(0, ~search - 1);
            checkpoint = cache.Checkpoints[Math.Max(0, index - 1)];
        }
        var cursor = new SnapshotCursor(snapshot, checkpoint.SourceStart, work);
        var current = checkpoint.Ordinal;
        var beginWork = work.Units;
        while (cursor.Position < snapshot.Length && work.Units - beginWork <= GridReplayBudget)
        {
            ct.ThrowIfCancellationRequested();
            Row row;
            if (cache.LargeRecords.TryGetValue(cursor.Position, out var certificate))
            { row = certificate.Row; cursor = new SnapshotCursor(snapshot, row.After, work); }
            else row = ParseRow(cursor, ct, false, out _);
            if (current >= targetRow && (targetSource < 0 || row.After > targetSource || row.After == snapshot.Length))
            {
                yield return (current, row);
                if (++delivered == limit) yield break;
            }
            current++;
        }
    }

    /// <summary>Produces ready, bounded cells. Unknown coordinates are status, never invented rows.</summary>
    private static GridRenderProjection ProjectGrid(TextSnapshot snapshot, CacheState cache, CsvGridRequest request,
        CancellationToken ct, ScanWork work)
    {
        var display = new StringBuilder();
        var rows = new List<GridRow>();
        var diagnostics = new List<Diagnostic>();
        var valuesTruncated = false;
        var columnsTruncated = false;
        var diagnosticsTruncated = false;
        var startWork = work.Units;
        foreach (var item in GridRows(snapshot, cache, request.Anchor, request.RowLimit, ct, work))
        {
            ct.ThrowIfCancellationRequested();
            if (work.Units - startWork > GridReplayBudget) break;
            cache.LargeRecords.TryGetValue(item.Row.Start, out var certificate);
            var capture = new GridCapture(request.Columns, display);
            var parsed = ParseRow(new SnapshotCursor(snapshot, item.Row.Start, work), ct, false, out _,
                certificate: certificate, grid: capture);
            if (certificate is null && (parsed.Start != item.Row.Start || parsed.End != item.Row.End ||
                parsed.After != item.Row.After || parsed.Width != item.Row.Width || parsed.ErrorCount != item.Row.ErrorCount))
                throw new InvalidOperationException("CSV Grid record no longer matches its snapshot.");
            var filled = new List<GridCell>(request.Columns.Count);
            var next = 0;
            for (var column = request.Columns.Start; column < request.Columns.End; column++)
            {
                if (next < capture.Cells.Count && capture.Cells[next].Column == column)
                    filled.Add(capture.Cells[next++]);
                else
                {
                    var pending = column < item.Row.Width;
                    filled.Add(new GridCell(column, null, new TextSpan(display.Length, 0),
                        pending ? GridValueState.Pending : GridValueState.Missing, false));
                    columnsTruncated |= pending;
                    valuesTruncated |= pending;
                }
            }
            valuesTruncated |= capture.ValuesTruncated;
            // Exact diagnostic spans are reconstructed only for the bounded requested syntax.
            // Giant syntax facts are summarized; unquoted quote streams are explicitly capped.
            var rowDiagnosticStart = diagnostics.Count;
            foreach (var diagnostic in capture.Diagnostics)
                if (diagnostics.Count < GridRenderProjection.MaxDiagnostics) diagnostics.Add(diagnostic);
            var rowTruncated = diagnostics.Count - rowDiagnosticStart < item.Row.ErrorCount;
            if (cache.ExpectedWidth >= 0 && item.Row.Width != cache.ExpectedWidth)
            {
                if (diagnostics.Count < GridRenderProjection.MaxDiagnostics)
                    diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "CSV004",
                        $"Row has {item.Row.Width} columns; the first row has {cache.ExpectedWidth}.",
                        new TextSpan(item.Row.Start, item.Row.End - item.Row.Start)));
                else rowTruncated = true;
            }
            diagnosticsTruncated |= rowTruncated;
            rows.Add(new GridRow(item.Ordinal, new TextSpan(item.Row.Start, item.Row.End - item.Row.Start),
                new TextSpan(item.Row.End, item.Row.After - item.Row.End), item.Row.Width, filled, rowTruncated));
        }
        var rowStart = request.Anchor is CsvGridAnchor.Row ordinal ? ordinal.Ordinal : rows.Count > 0 ? rows[0].Ordinal : 0;
        var unresolvedSource = request.Anchor is CsvGridAnchor.Source && rows.Count == 0;
        var requestedRows = unresolvedSource ? new GridRange(0, 0) :
            new GridRange(rowStart, Math.Min(request.RowLimit, int.MaxValue - rowStart));
        var available = cache.Complete ? Math.Max(0, cache.RowCount - rowStart) : request.RowLimit;
        var rowsTruncated = rows.Count < Math.Min(request.RowLimit, available);
        var completeness = cache.Complete ? AnalysisCompleteness.Complete :
            rows.Count > 0 ? AnalysisCompleteness.CoveredRegion : AnalysisCompleteness.Provisional;
        var coverage = new TextSpan(0, cache.Complete ? snapshot.Length : cache.IndexedUntil);
        return new GridRenderProjection(snapshot.Version, snapshot.Length,
            new GridExtent(cache.RowCount, cache.Complete ? cache.RowCount : null, cache.Complete ? cache.MaxWidth : null),
            completeness, [coverage], cache.Complete ? cache.Total : null, requestedRows, request.Columns,
            display.ToString(), rows, diagnostics, rowsTruncated, columnsTruncated, diagnosticsTruncated, valuesTruncated, request.Anchor);
    }

    /// <summary>One-row capture writes a shared arena, never a full decoded giant value.</summary>
    private sealed class GridCapture(GridRange columns, StringBuilder display)
    {
        internal GridRange Columns { get; } = columns;
        internal List<GridCell> Cells { get; } = [];
        internal bool ValuesTruncated { get; private set; }
        internal int ErrorCount { get; private set; }
        internal List<Diagnostic> Diagnostics { get; } = [];
        internal void AddDiagnostic(int column, Diagnostic diagnostic)
        {
            if (column >= Columns.Start && column < Columns.End && Diagnostics.Count < GridRenderProjection.MaxDiagnostics)
                Diagnostics.Add(diagnostic);
        }
        internal void Add(TextSnapshot snapshot, int column, TextSpan span, bool quoted, int contentEnd, int errorCount, GiantField? certificate, ScanWork work, CancellationToken ct)
        {
            if (column < Columns.Start || column >= Columns.End) return;
            ct.ThrowIfCancellationRequested();
            var start = display.Length;
            ErrorCount = checked(ErrorCount + errorCount);
            var error = errorCount > 0;
            if (certificate is { Quoted: false, ErrorCount: > 0 } giant)
            {
                // A giant unquoted quote-error stream can be huge: expose a bounded leading sample
                // and preserve the exact omitted diagnostic count independently.
                var sample = snapshot.GetText(span.Start, Math.Min(VisibleScanBudget, span.Length));
                work.Units += sample.Length;
                for (var i = 0; i < sample.Length && Diagnostics.Count < GridRenderProjection.MaxDiagnostics; i++)
                {
                    if ((i & 4095) == 0) ct.ThrowIfCancellationRequested();
                    if (sample[i] == '"') AddDiagnostic(column, new Diagnostic(DiagnosticSeverity.Error,
                        "CSV003", "Quotes must enclose an entire field.", new TextSpan(span.Start + i, 1)));
                }
            }
            if (span.Length > MaxProjectedCellSourceLength)
            {
                Cells.Add(new GridCell(column, span, new TextSpan(start, 0), GridValueState.Oversized, error));
                ValuesTruncated = true;
                return;
            }
            var valueStart = span.Start + (quoted ? 1 : 0);
            var valueLength = quoted ? Math.Max(0, contentEnd - valueStart) : span.Length;
            var readLength = Math.Min(valueLength, 2 * GridRenderProjection.MaxCellDisplayLength + 2);
            var source = snapshot.GetText(valueStart, readLength);
            var max = Math.Min(GridRenderProjection.MaxCellDisplayLength, GridRenderProjection.MaxDisplayLength - display.Length);
            var sanitized = false;
            var consumed = 0;
            while (consumed < source.Length && display.Length - start < max)
            {
                var c = source[consumed++];
                if (quoted && c == '"' && consumed < source.Length && source[consumed] == '"') consumed++;
                if (char.IsHighSurrogate(c))
                {
                    if (consumed < source.Length && char.IsLowSurrogate(source[consumed]))
                    {
                        if (display.Length - start + 2 > max) { consumed--; break; }
                        display.Append(c).Append(source[consumed++]);
                        continue;
                    }
                    c = '\uFFFD'; sanitized = true;
                }
                else if (char.IsLowSurrogate(c)) { c = '\uFFFD'; sanitized = true; }
                else if (char.IsControl(c))
                {
                    c = c switch { '\t' => '\u21E5', '\r' => '\u240D', '\n' => '\u21B5', _ => '\uFFFD' };
                    sanitized = true;
                }
                display.Append(c);
            }
            var clipped = consumed < valueLength;
            ValuesTruncated |= clipped;
            Cells.Add(new GridCell(column, span, new TextSpan(start, display.Length - start),
                clipped ? GridValueState.Clipped : GridValueState.Complete, error, sanitized));
        }
    }

}
