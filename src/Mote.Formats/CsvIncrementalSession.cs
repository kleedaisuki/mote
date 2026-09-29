using Mote.Engine;

namespace Mote.Formats;

/// <summary>
/// Per-document CSV record index. Edits restart at the affected record and reuse the
/// suffix as soon as a parsed record ends at an unchanged old record boundary.
/// </summary>
/// <remarks>
/// A quoted field may span physical lines, so the index is by logical record rather
/// than line. The cache stores only boundaries, widths, and error counts; cell values
/// and diagnostics are reparsed from bounded snapshot ranges for projected records.
/// This session is single-caller and commits only after successful analysis.
/// </remarks>
internal sealed class CsvIncrementalSession : IFormatSession
{
    private const int BlockSize = 1024;
    private const int VisibleScanBudget = 64 * 1024;
    private const int ProjectionRowBudget = 4096;
    private const int ProjectionCellBudget = 8192;
    private const int MaxProjectedCellSourceLength = 64 * 1024;
    private List<Segment> _segments = [];
    private long? _version;
    private bool _complete;
    private bool _disposed;

    /// <inheritdoc />
    public DocumentAnalysis Analyze(TextSnapshot snapshot, IReadOnlyList<VersionedEdit> changesSinceCommittedState,
        AnalysisRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(changesSinceCommittedState);
        ValidateRange(snapshot, request.VisibleRange);
        if (request.Scope is not (AnalysisScope.Visible or AnalysisScope.Full))
            throw new ArgumentOutOfRangeException(nameof(request));

        List<Segment> rows;
        bool complete;
        if (_version == snapshot.Version && changesSinceCommittedState.Count == 0)
        {
            rows = _segments;
            complete = _complete;
            if (!complete && request.Scope == AnalysisScope.Full)
            {
                rows = ResumeAll(snapshot, rows, cancellationToken);
                complete = true;
            }
            else if (!complete && request.VisibleRange.End <= VisibleScanBudget)
            {
                rows = ExtendVisiblePrefix(snapshot, rows, request.VisibleRange, cancellationToken);
                complete = rows.Count == 0 ? snapshot.Length == 0 : rows[^1].After == snapshot.Length;
            }
        }
        else if (_complete && CanReuse(snapshot, changesSinceCommittedState))
        {
            rows = ParseIncremental(snapshot, changesSinceCommittedState[0].Change, cancellationToken);
            complete = true;
        }
        else if (request.Scope == AnalysisScope.Full)
        {
            rows = ParseAll(snapshot, cancellationToken);
            complete = true;
        }
        else
        {
            rows = ParseVisiblePrefix(snapshot, request.VisibleRange, cancellationToken);
            complete = rows.Count == 0 ? snapshot.Length == 0 : rows[^1].After == snapshot.Length;
        }
        var result = Project(snapshot, rows, request, complete, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _segments = rows;
        _version = snapshot.Version;
        _complete = complete;
        return result;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _segments = [];
        _version = null;
        _complete = false;
        _disposed = true;
    }

    private bool CanReuse(TextSnapshot snapshot, IReadOnlyList<VersionedEdit> edits)
    {
        var oldLength = _segments.Count == 0 ? 0 : _segments[^1].After;
        return _version is not null && edits.Count == 1 &&
            edits[0].BeforeVersion == _version && edits[0].AfterVersion == snapshot.Version &&
            edits[0].Change.InsertText is not null && edits[0].Change.Start >= 0 &&
            edits[0].Change.DeleteLength >= 0 &&
            edits[0].Change.Start <= oldLength &&
            edits[0].Change.DeleteLength <= oldLength - edits[0].Change.Start &&
            (long)oldLength - edits[0].Change.DeleteLength + edits[0].Change.InsertText.Length == snapshot.Length;
    }

    private List<Segment> ParseIncremental(TextSnapshot snapshot, TextChange change, CancellationToken ct)
    {
        if (_segments.Count == 0) return ParseAll(snapshot, ct);

        // Restart one preceding record: a new LF at a record start can merge
        // with that record's preceding CR into a CRLF delimiter.
        var first = FindAffected(change.Start);
        var (segmentIndex, localIndex) = FindRow(first);
        var start = _segments[segmentIndex].Get(localIndex).Start;
        var cursor = new SnapshotCursor(snapshot, start);
        var result = new List<Segment>(_segments.Count + 4);
        result.AddRange(_segments.Take(segmentIndex));
        if (localIndex > 0) result.Add(_segments[segmentIndex].Slice(0, localIndex));
        var parsed = new List<Row>();
        var oldEnd = (long)change.Start + change.DeleteLength;
        var delta = change.InsertText.Length - change.DeleteLength;
        var candidateSegment = segmentIndex;
        var candidateLocal = localIndex;
        while (cursor.Position < snapshot.Length)
        {
            ct.ThrowIfCancellationRequested();
            var row = ParseRow(cursor, ct, false, out _);
            parsed.Add(row);
            // A record delimiter reached at the translated old delimiter is a safe
            // lexical checkpoint, even if earlier records changed quote structure.
            Row old;
            while (TryCandidate(candidateSegment, candidateLocal, out old) && old.After < oldEnd)
                Next(ref candidateSegment, ref candidateLocal);
            if (TryCandidate(candidateSegment, candidateLocal, out old) && old.After >= oldEnd &&
                row.After == (long)old.After + delta)
            {
                AddParsed(result, parsed);
                Next(ref candidateSegment, ref candidateLocal);
                AddSuffix(result, candidateSegment, candidateLocal, delta);
                return result;
            }
            while (TryCandidate(candidateSegment, candidateLocal, out old) && (long)old.After + delta <= row.After)
                Next(ref candidateSegment, ref candidateLocal);
        }
        AddParsed(result, parsed);
        return result;
    }

    private int FindAffected(int offset)
    {
        var lo = 0;
        var hi = _segments.Count - 1;
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (_segments[mid].After > offset) hi = mid;
            else lo = mid + 1;
        }
        var segment = _segments[lo];
        var left = 0;
        var right = segment.Count - 1;
        while (left < right)
        {
            var mid = left + (right - left) / 2;
            if (segment.Get(mid).After > offset) right = mid;
            else left = mid + 1;
        }
        // Restart one preceding record: a new LF at a record start may merge
        // with the previous CR into a CRLF delimiter.
        return Math.Max(0, _segments.Take(lo).Sum(s => s.Count) + left - 1);
    }

    private static List<Segment> ParseAll(TextSnapshot snapshot, CancellationToken ct)
    {
        var segments = new List<Segment>();
        var rows = new List<Row>(BlockSize);
        var cursor = new SnapshotCursor(snapshot, 0);
        while (cursor.Position < snapshot.Length)
        {
            ct.ThrowIfCancellationRequested();
            rows.Add(ParseRow(cursor, ct, false, out _));
            if (rows.Count == BlockSize) { segments.Add(new Segment(rows.ToArray(), 0, rows.Count, 0)); rows.Clear(); }
        }
        if (rows.Count > 0) segments.Add(new Segment(rows.ToArray(), 0, rows.Count, 0));
        return segments;
    }

    private static List<Segment> ParseVisiblePrefix(TextSnapshot snapshot, TextSpan visible, CancellationToken ct) =>
        ExtendVisiblePrefix(snapshot, [], visible, ct);

    /// <summary>Scans at most one bounded prefix; no partial record is committed.</summary>
    private static List<Segment> ExtendVisiblePrefix(TextSnapshot snapshot, List<Segment> prefix,
        TextSpan visible, CancellationToken ct)
    {
        var start = prefix.Count == 0 ? 0 : prefix[^1].After;
        var limit = Math.Min(snapshot.Length, Math.Min(VisibleScanBudget, Math.Max(visible.End, 8192)));
        if (start >= limit) return prefix;
        var result = new List<Segment>(prefix);
        var rows = new List<Row>(BlockSize);
        var cursor = new SnapshotCursor(snapshot, start);
        while (cursor.Position < snapshot.Length && cursor.Position < limit)
        {
            ct.ThrowIfCancellationRequested();
            try { rows.Add(ParseRow(cursor, ct, false, out _, limit)); }
            catch (ScanBudgetExceededException) { break; }
            if (rows.Count == BlockSize)
            {
                result.Add(new Segment(rows.ToArray(), 0, rows.Count, 0));
                rows.Clear();
            }
        }
        if (rows.Count > 0) result.Add(new Segment(rows.ToArray(), 0, rows.Count, 0));
        return result;
    }

    private static List<Segment> ResumeAll(TextSnapshot snapshot, List<Segment> prefix, CancellationToken ct)
    {
        var result = new List<Segment>(prefix);
        var cursor = new SnapshotCursor(snapshot, prefix.Count == 0 ? 0 : prefix[^1].After);
        var rows = new List<Row>(BlockSize);
        while (cursor.Position < snapshot.Length)
        {
            ct.ThrowIfCancellationRequested();
            rows.Add(ParseRow(cursor, ct, false, out _));
            if (rows.Count == BlockSize)
            {
                result.Add(new Segment(rows.ToArray(), 0, rows.Count, 0));
                rows.Clear();
            }
        }
        if (rows.Count > 0) result.Add(new Segment(rows.ToArray(), 0, rows.Count, 0));
        return result;
    }

    private static Row ParseRow(SnapshotCursor cursor, CancellationToken ct, bool capture,
        out RowPayload? payload, int maxPosition = int.MaxValue,
        TextSpan? captureRange = null, int maxCells = ProjectionCellBudget)
    {
        if (cursor.Position >= maxPosition) throw new ScanBudgetExceededException();
        var start = cursor.Position;
        var cells = capture ? new List<Cell>() : null;
        var diagnostics = capture ? new List<Diagnostic>() : null;
        var tokens = capture ? new List<SemanticToken>() : null;
        var width = 0;
        var errors = 0;
        void AddDiagnostic(string code, string message, TextSpan span)
        {
            errors++;
            if (diagnostics is { Count: < ProjectionCellBudget } &&
                (captureRange is null || Intersects(span.Start, span.End, captureRange.Value)))
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, code, message, span));
        }
        while (true)
        {
            if (cursor.Position >= maxPosition) throw new ScanBudgetExceededException();
            var fieldStart = cursor.Position;
            var quoted = cursor.Peek() == '"';
            var contentEnd = fieldStart;
            if (quoted)
            {
                cursor.Advance();
                var closed = false;
                while (cursor.Position < cursor.Length)
                {
                    if ((cursor.Position & 4095) == 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (cursor.Position >= maxPosition) throw new ScanBudgetExceededException();
                    }
                    var c = cursor.Read();
                    if (c != '"') continue;
                    if (cursor.Peek() == '"') { cursor.Advance(); continue; }
                    closed = true;
                    break;
                }
                contentEnd = cursor.Position - (closed ? 1 : 0);
                if (!closed)
                    AddDiagnostic("CSV001", "Unterminated quoted field.",
                        new TextSpan(fieldStart, cursor.Position - fieldStart));
                if (closed && cursor.Position < cursor.Length && cursor.Peek() is not (',' or '\r' or '\n'))
                {
                    var invalidStart = cursor.Position;
                    while (cursor.Position < cursor.Length && cursor.Peek() is not (',' or '\r' or '\n'))
                    {
                        if ((cursor.Position & 4095) == 0)
                        {
                            ct.ThrowIfCancellationRequested();
                            if (cursor.Position >= maxPosition) throw new ScanBudgetExceededException();
                        }
                        cursor.Advance();
                    }
                    AddDiagnostic("CSV002",
                        "Characters after a closing quote are not valid in a CSV field.",
                        new TextSpan(invalidStart, cursor.Position - invalidStart));
                }
                var tokenSpan = new TextSpan(fieldStart, cursor.Position - fieldStart);
                if (tokens is { Count: < ProjectionCellBudget } &&
                    (captureRange is null || Intersects(tokenSpan.Start, tokenSpan.End, captureRange.Value)))
                    tokens.Add(new SemanticToken("string", tokenSpan));
            }
            else
            {
                while (cursor.Position < cursor.Length && cursor.Peek() is not (',' or '\r' or '\n'))
                {
                    if ((cursor.Position & 4095) == 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (cursor.Position >= maxPosition) throw new ScanBudgetExceededException();
                    }
                    if (cursor.Peek() == '"')
                        AddDiagnostic("CSV003", "Quotes must enclose an entire field.",
                            new TextSpan(cursor.Position, 1));
                    cursor.Advance();
                }
            }
            width++;
            var cellSpan = new TextSpan(fieldStart, cursor.Position - fieldStart);
            if (cells is not null && cells.Count < maxCells &&
                cellSpan.Length <= MaxProjectedCellSourceLength &&
                (captureRange is null || CellIntersects(cellSpan, captureRange.Value)))
            {
                var value = quoted
                    ? cursor.Slice(fieldStart + 1, contentEnd - fieldStart - 1)
                        .Replace("\"\"", "\"", StringComparison.Ordinal)
                    : cursor.Slice(fieldStart, cellSpan.Length);
                cells.Add(new Cell(cellSpan, value));
            }
            if (cursor.Peek() != ',') break;
            cursor.Advance();
        }
        var end = cursor.Position;
        if (cursor.Peek() == '\r')
        {
            cursor.Advance();
            if (cursor.Peek() == '\n') cursor.Advance();
        }
        else if (cursor.Peek() == '\n') cursor.Advance();
        payload = capture ? new RowPayload(cells!, diagnostics!, tokens!) : null;
        return new Row(start, end, cursor.Position, width, errors);
    }

    private static bool CellIntersects(TextSpan cell, TextSpan range) =>
        cell.Length == 0
            ? cell.Start >= range.Start && cell.Start <= range.End
            : Intersects(cell.Start, cell.End, range);

    private sealed class ScanBudgetExceededException : Exception;

    private static DocumentAnalysis Project(TextSnapshot snapshot, List<Segment> segments,
        AnalysisRequest request, bool complete, CancellationToken ct)
    {
        var visible = request.VisibleRange;
        var fullProjection = request.Scope == AnalysisScope.Full;
        var nodes = new List<SemanticNode>();
        var diagnostics = new List<Diagnostic>();
        var tokens = new List<SemanticToken>();
        var expected = segments.Count == 0 ? -1 : segments[0].Get(0).Width;
        var total = 0;
        // A full request still gets complete *semantic validation* on giant files,
        // but projecting every cell would create another document-sized object graph.
        var projectAll = complete && fullProjection && snapshot.Length <= 1024 * 1024 &&
            segments.Sum(segment => segment.Count) <= ProjectionRowBudget &&
            segments.Sum(segment => segment.CellCount) <= ProjectionCellBudget &&
            segments.All(segment => segment.MaxRowLength <= MaxProjectedCellSourceLength);
        var projectedCells = 0;
        foreach (var segment in segments)
        {
            ct.ThrowIfCancellationRequested();
            if (complete) total += segment.ErrorCount + segment.Count - segment.WidthCount(expected);
            if (!projectAll && (nodes.Count >= ProjectionRowBudget ||
                !Intersects(segment.Start, segment.After, visible))) continue;
            foreach (var row in segment.Enumerate(visible, projectAll))
            {
                if (!projectAll && nodes.Count >= ProjectionRowBudget) break;
                var projected = ParseRow(new SnapshotCursor(snapshot, row.Start), ct, true, out var payload,
                    captureRange: projectAll ? null : visible,
                    maxCells: ProjectionCellBudget - projectedCells);
                if (projected.End != row.End || projected.After != row.After || projected.Width != row.Width ||
                    projected.ErrorCount != row.ErrorCount)
                    throw new InvalidOperationException("CSV record index no longer matches its snapshot.");
                var widthMismatch = expected >= 0 && row.Width != expected;
                var children = payload!.Cells.Select(cell => new SemanticNode("cell", cell.Span, value: cell.Value)).ToArray();
                projectedCells += children.Length;
                nodes.Add(new SemanticNode("row", new TextSpan(row.Start, row.End - row.Start), children: children));
                diagnostics.AddRange(payload.Diagnostics);
                if (widthMismatch)
                    diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "CSV004",
                        $"Row has {row.Width} columns; the first row has {expected}.",
                        new TextSpan(row.Start, row.End - row.Start)));
                tokens.AddRange(payload.Tokens);
            }
        }
        var root = new SemanticNode("table", new TextSpan(0, snapshot.Length), children: nodes);
        var indexedUntil = segments.Count == 0 ? 0 : segments[^1].After;
        var completeness = complete ? AnalysisCompleteness.Complete :
            indexedUntil > 0 && visible.End <= indexedUntil
                ? AnalysisCompleteness.CoveredRegion : AnalysisCompleteness.Provisional;
        var coverage = new TextSpan(0, complete ? snapshot.Length : indexedUntil);
        return new DocumentAnalysis(snapshot.Version, coverage, completeness,
            root, diagnostics, tokens, complete ? total : null);
    }

    private static bool Intersects(int start, int end, TextSpan range) =>
        start < range.End && end > range.Start || range.Length == 0 && start <= range.Start && end >= range.Start;

    private static void ValidateRange(TextSnapshot snapshot, TextSpan range)
    {
        if (range.Start < 0 || range.Length < 0 || range.Start > snapshot.Length || range.Length > snapshot.Length - range.Start)
            throw new ArgumentOutOfRangeException(nameof(range));
    }

    private (int Segment, int Local) FindRow(int index)
    {
        for (var i = 0; i < _segments.Count; i++)
        {
            if (index < _segments[i].Count) return (i, index);
            index -= _segments[i].Count;
        }
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    private bool TryCandidate(int segment, int local, out Row row)
    {
        if (segment < _segments.Count) { row = _segments[segment].Get(local); return true; }
        row = default;
        return false;
    }

    private void Next(ref int segment, ref int local)
    {
        local++;
        if (segment < _segments.Count && local == _segments[segment].Count)
        {
            segment++;
            local = 0;
        }
    }

    private static void AddParsed(List<Segment> target, List<Row> rows)
    {
        for (var i = 0; i < rows.Count; i += BlockSize)
        {
            var count = Math.Min(BlockSize, rows.Count - i);
            var block = rows.GetRange(i, count).ToArray();
            target.Add(new Segment(block, 0, count, 0));
        }
    }

    private void AddSuffix(List<Segment> target, int segmentIndex, int localIndex, int delta)
    {
        if (segmentIndex >= _segments.Count) return;
        if (localIndex != 0)
            target.Add(_segments[segmentIndex].Slice(localIndex, _segments[segmentIndex].Count).Shift(delta));
        else target.Add(_segments[segmentIndex].Shift(delta));
        for (var i = segmentIndex + 1; i < _segments.Count; i++)
            target.Add(_segments[i].Shift(delta));
    }

    private readonly record struct Cell(TextSpan Span, string Value);

    private sealed record RowPayload(List<Cell> Cells, List<Diagnostic> Diagnostics, List<SemanticToken> Tokens);

    /// <summary>Cached row payload with a lazy translation for unchanged suffixes.</summary>
    private readonly record struct Row
    {
        internal Row(int start, int end, int after, int width, int errorCount)
        {
            RawStart = start;
            RawEnd = end;
            RawAfter = after;
            Offset = 0;
            Width = width;
            ErrorCount = errorCount;
        }

        private int RawStart { get; init; }
        private int RawEnd { get; init; }
        private int RawAfter { get; init; }
        internal int Offset { get; init; }
        internal int Start => checked(RawStart + Offset);
        internal int End => checked(RawEnd + Offset);
        internal int After => checked(RawAfter + Offset);
        internal int Width { get; init; }
        internal int ErrorCount { get; init; }
        internal Row Shift(int delta) => this with { Offset = checked(Offset + delta) };
    }

    /// <summary>
    /// Shared record block. Slicing and translating a block never clone its rows or
    /// decoded field values; summaries make whole-document width counts cheap.
    /// </summary>
    private sealed class Segment
    {
        private readonly Row[] _rows;
        private readonly int _begin;
        private readonly int _end;
        private readonly int _shift;
        private readonly Dictionary<int, int> _widths;

        internal Segment(Row[] rows, int begin, int end, int shift)
        {
            _rows = rows;
            _begin = begin;
            _end = end;
            _shift = shift;
            _widths = [];
            for (var i = begin; i < end; i++)
            {
                var width = rows[i].Width;
                _widths[width] = _widths.GetValueOrDefault(width) + 1;
                ErrorCount += rows[i].ErrorCount;
                CellCount += width;
                MaxRowLength = Math.Max(MaxRowLength, rows[i].End - rows[i].Start);
            }
        }

        private Segment(Row[] rows, int begin, int end, int shift,
            Dictionary<int, int> widths, int errorCount, long cellCount, int maxRowLength)
        {
            _rows = rows;
            _begin = begin;
            _end = end;
            _shift = shift;
            _widths = widths;
            ErrorCount = errorCount;
            CellCount = cellCount;
            MaxRowLength = maxRowLength;
        }

        internal int Count => _end - _begin;
        internal int Start => Get(0).Start;
        internal int After => Get(Count - 1).After;
        internal int ErrorCount { get; }
        internal long CellCount { get; }
        internal int MaxRowLength { get; }
        internal int WidthCount(int width) => _widths.GetValueOrDefault(width);
        internal Row Get(int index) => _rows[_begin + index].Shift(_shift);
        internal Segment Slice(int start, int end) => new(_rows, _begin + start, _begin + end, _shift);
        internal Segment Shift(int delta) => new(_rows, _begin, _end, checked(_shift + delta),
            _widths, ErrorCount, CellCount, MaxRowLength);

        internal IEnumerable<Row> Enumerate(TextSpan range, bool all)
        {
            var start = 0;
            if (!all)
            {
                var lo = 0;
                var hi = Count;
                while (lo < hi)
                {
                    var mid = lo + (hi - lo) / 2;
                    if (Get(mid).After <= range.Start) lo = mid + 1;
                    else hi = mid;
                }
                start = Math.Max(0, lo - 1);
            }
            for (var i = start; i < Count; i++)
            {
                var row = Get(i);
                if (!all && row.Start > range.End) yield break;
                if (all || Intersects(row.Start, row.After, range)) yield return row;
            }
        }
    }

    /// <summary>Small ranged reader over a rope snapshot, independent of chunk boundaries.</summary>
    private sealed class SnapshotCursor
    {
        private const int WindowSize = 8192;
        private readonly TextSnapshot _snapshot;
        private string _window = string.Empty;
        private int _windowStart = -1;

        internal SnapshotCursor(TextSnapshot snapshot, int start) { _snapshot = snapshot; Position = start; }
        internal int Position { get; private set; }
        internal int Length => _snapshot.Length;

        internal char Peek()
        {
            if (Position >= Length) return '\0';
            if (Position < _windowStart || Position >= _windowStart + _window.Length)
            {
                _windowStart = Position;
                _window = _snapshot.GetText(Position, Math.Min(WindowSize, Length - Position));
            }
            return _window[Position - _windowStart];
        }

        internal char Read() { var c = Peek(); Position++; return c; }
        internal void Advance() { Position++; }
        internal string Slice(int start, int length) => _snapshot.GetText(start, length);
    }
}
