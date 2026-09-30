using System.Runtime.CompilerServices;
using Mote.Engine;

[assembly: InternalsVisibleTo("Mote.Tests")]
[assembly: InternalsVisibleTo("CsvSparseBenchmark")]

namespace Mote.Formats;

/// <summary>
/// Per-document CSV record index. Edits restart at the affected record and reuse the
/// suffix as soon as a parsed record ends at an unchanged old record boundary.
/// </summary>
/// <remarks>
/// A quoted field may span physical lines, so the index is by logical record rather
/// than line. The cache stores boundaries, widths, error counts and certified oversized-field
/// source spans; no cell values or source text are retained. Projected values and
/// local diagnostics are reconstructed from bounded snapshot ranges.
/// This session is single-caller and commits only after successful analysis.
/// </remarks>
internal sealed class CsvIncrementalSession : IWindowedFormatSession
{
    private const int BlockSize = 1024;
    private const int VisibleScanBudget = 64 * 1024;
    private const int ProjectionRowBudget = 4096;
    private const int ProjectionCellBudget = 8192;
    private const int MaxProjectedCellSourceLength = 64 * 1024;
    private const int MaxWindows = 8;
    private const int MaxWindowWidth = 512 * 1024;
    private const int MaxDenseSegments = 256;
    private const int CheckpointInterval = 64 * 1024;
    private CacheState _cache = CacheState.Empty;
    private long _lastScannedSourceUnits;

    /// <summary>Aggregate structural estimate; excludes text, output, transient graphs, and CLR layout guarantees.</summary>
    internal CsvIndexStatistics CacheStatistics => new(_cache.Mode.ToString(), _cache.Segments.Count,
        _cache.Checkpoints.Length, 256L + _cache.Segments.Count * 92_160L +
        _cache.Checkpoints.Length * 4L + _cache.LargeRecords.Values.Sum(record => record.EstimatedBytes),
        _cache.Version, _lastScannedSourceUnits, _cache.LargeRecords.Count,
        _cache.LargeRecords.Values.Sum(record => record.Fields.Length));

    /// <summary>Content-free diagnostic facts for tests and opt-in instrumentation.</summary>
    internal readonly record struct CsvIndexStatistics(string Mode, int SegmentCount, int CheckpointCount,
        long EstimatedRetainedIndexBytes, long? Version, long ScannedSourceUnits,
        int LargeRecordCount, int GiantFieldCount);

    /// <summary>Complete validation is independent of retained dense row payload.</summary>
    private enum CsvCacheMode { None, Prefix, DenseFull, SparseFull }

    /// <summary>A single immutable commit owns one representation at one document version.</summary>
    private sealed record CacheState(long? Version, CsvCacheMode Mode, List<Segment> Segments,
        int[] Checkpoints, int ExpectedWidth, int Total)
    {
        internal Dictionary<int, LargeRecord> LargeRecords { get; init; } = [];
        internal static CacheState Empty { get; } = new(null, CsvCacheMode.None, [], [], -1, 0);
        internal bool Complete => Mode is CsvCacheMode.DenseFull or CsvCacheMode.SparseFull;
        internal int IndexedUntil => Segments.Count == 0 ? 0 : Segments[^1].After;
    }

    /// <summary>Counts parsed source units, including replay for projection, not bytes fetched or source length.</summary>
    private sealed class ScanWork { internal long Units; }
    private bool _disposed;

    /// <inheritdoc />
    public DocumentAnalysis Analyze(TextSnapshot snapshot, IReadOnlyList<VersionedEdit> changesSinceCommittedState,
        AnalysisRequest request, CancellationToken cancellationToken = default)
    {
        var result = AnalyzeWindowsCore(snapshot, changesSinceCommittedState,
            [request.VisibleRange], request.Scope, false, cancellationToken);
        // The legacy API is a single contiguous certificate, never a convex hull.
        var coverage = result.CertifiedCoverage.Count == 0 ? new TextSpan(0, 0) : result.CertifiedCoverage[0];
        return new DocumentAnalysis(result.Version, coverage, result.Completeness,
            result.Root, result.Diagnostics, result.Tokens, result.TotalDiagnosticCount);
    }

    /// <inheritdoc />
    public WindowedAnalysis AnalyzeWindows(TextSnapshot snapshot,
        IReadOnlyList<VersionedEdit> changesSinceCommittedState, IReadOnlyList<TextSpan> windows,
        AnalysisScope scope, CancellationToken cancellationToken = default)
        => AnalyzeWindowsCore(snapshot, changesSinceCommittedState, windows, scope, true, cancellationToken);

    private WindowedAnalysis AnalyzeWindowsCore(TextSnapshot snapshot,
        IReadOnlyList<VersionedEdit> changesSinceCommittedState, IReadOnlyList<TextSpan> windows,
        AnalysisScope scope, bool enforceBudget, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(changesSinceCommittedState);
        var normalized = NormalizeWindows(snapshot, windows, enforceBudget);
        if (scope is not (AnalysisScope.Visible or AnalysisScope.Full))
            throw new ArgumentOutOfRangeException(nameof(scope));
        var furthest = normalized[^1];

        var work = new ScanWork();
        CacheState candidate;
        if (_cache.Version == snapshot.Version && changesSinceCommittedState.Count == 0)
        {
            candidate = _cache;
            if (!candidate.Complete && scope == AnalysisScope.Full)
                candidate = ScanFull(snapshot, candidate.Segments, cancellationToken, work);
            else if (!candidate.Complete && furthest.End <= VisibleScanBudget)
                candidate = PrefixState(snapshot,
                    ExtendVisiblePrefix(snapshot, candidate.Segments, furthest, cancellationToken, work));
        }
        else if (_cache.Mode == CsvCacheMode.DenseFull && _cache.LargeRecords.Count == 0 && CanReuse(snapshot, changesSinceCommittedState))
        {
            List<Segment>? rows;
            try { rows = ParseIncremental(snapshot, changesSinceCommittedState[0].Change, cancellationToken, work,
                scope == AnalysisScope.Visible); }
            catch (ScanBudgetExceededException) { rows = null; }
            candidate = rows is not null ? DenseState(snapshot, rows) : scope == AnalysisScope.Full
                ? ScanFull(snapshot, [], cancellationToken, work)
                : PrefixState(snapshot, ExtendVisiblePrefix(snapshot, [], furthest, cancellationToken, work));
        }
        else if (scope == AnalysisScope.Full)
            candidate = ScanFull(snapshot, [], cancellationToken, work);
        else
            candidate = PrefixState(snapshot,
                ExtendVisiblePrefix(snapshot, [], furthest, cancellationToken, work));

        var result = Project(snapshot, candidate, normalized, scope, !enforceBudget, cancellationToken, work);
        cancellationToken.ThrowIfCancellationRequested();
        _cache = candidate;
        _lastScannedSourceUnits = work.Units;
        return result;
    }

    private static TextSpan[] NormalizeWindows(TextSnapshot snapshot, IReadOnlyList<TextSpan> windows, bool enforceBudget)
    {
        ArgumentNullException.ThrowIfNull(windows);
        if (windows.Count is < 1 or > MaxWindows)
            throw new ArgumentOutOfRangeException(nameof(windows), "CSV accepts one to eight source windows.");
        var sorted = windows.ToArray();
        long width = 0;
        foreach (var window in sorted)
        {
            ValidateRange(snapshot, window);
            width += window.Length;
        }
        if (enforceBudget && width > MaxWindowWidth)
            throw new ArgumentOutOfRangeException(nameof(windows), "CSV projection windows exceed 512 Ki UTF-16 units.");
        Array.Sort(sorted, (a, b) => a.Start.CompareTo(b.Start));
        var merged = new List<TextSpan>(sorted.Length);
        foreach (var window in sorted)
        {
            if (merged.Count > 0 && window.Start <= merged[^1].End)
            {
                var previous = merged[^1];
                merged[^1] = new TextSpan(previous.Start, Math.Max(previous.End, window.End) - previous.Start);
            }
            else merged.Add(window);
        }
        return merged.ToArray();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cache = CacheState.Empty;
        _lastScannedSourceUnits = 0;
        _disposed = true;
    }

    private bool CanReuse(TextSnapshot snapshot, IReadOnlyList<VersionedEdit> edits)
    {
        var oldLength = _cache.Segments.Count == 0 ? 0 : _cache.Segments[^1].After;
        return _cache.Version is not null && edits.Count == 1 &&
            edits[0].BeforeVersion == _cache.Version && edits[0].AfterVersion == snapshot.Version &&
            edits[0].Change.InsertText is not null && edits[0].Change.Start >= 0 &&
            edits[0].Change.DeleteLength >= 0 &&
            edits[0].Change.Start <= oldLength &&
            edits[0].Change.DeleteLength <= oldLength - edits[0].Change.Start &&
            (long)oldLength - edits[0].Change.DeleteLength + edits[0].Change.InsertText.Length == snapshot.Length;
    }

    private List<Segment>? ParseIncremental(TextSnapshot snapshot, TextChange change, CancellationToken ct, ScanWork work, bool bounded)
    {
        if (_cache.Segments.Count == 0) return null;

        // Restart one preceding record: a new LF at a record start can merge
        // with that record's preceding CR into a CRLF delimiter.
        var first = FindAffected(change.Start);
        var (segmentIndex, localIndex) = FindRow(first);
        var start = _cache.Segments[segmentIndex].Get(localIndex).Start;
        var cursor = new SnapshotCursor(snapshot, start, work);
        var result = new List<Segment>(_cache.Segments.Count + 4);
        result.AddRange(_cache.Segments.Take(segmentIndex));
        if (localIndex > 0) result.Add(_cache.Segments[segmentIndex].Slice(0, localIndex));
        var parsed = new List<Row>(BlockSize);
        var oldEnd = (long)change.Start + change.DeleteLength;
        var delta = change.InsertText.Length - change.DeleteLength;
        var candidateSegment = segmentIndex;
        var candidateLocal = localIndex;
        while (cursor.Position < snapshot.Length)
        {
            ct.ThrowIfCancellationRequested();
            var row = ParseRow(cursor, ct, false, out _, bounded && (long)start + VisibleScanBudget < snapshot.Length ? start + VisibleScanBudget : int.MaxValue);
            if (row.End - row.Start > VisibleScanBudget) return null;
            parsed.Add(row);
            if (parsed.Count == BlockSize)
            {
                if (!AddParsed(result, parsed)) return null;
                parsed.Clear();
            }
            // A record delimiter reached at the translated old delimiter is a safe
            // lexical checkpoint, even if earlier records changed quote structure.
            Row old;
            while (TryCandidate(candidateSegment, candidateLocal, out old) && old.After < oldEnd)
                Next(ref candidateSegment, ref candidateLocal);
            if (TryCandidate(candidateSegment, candidateLocal, out old) && old.After >= oldEnd &&
                row.After == (long)old.After + delta)
            {
                if (!AddParsed(result, parsed)) return null;
                Next(ref candidateSegment, ref candidateLocal);
                if (!AddSuffix(result, candidateSegment, candidateLocal, delta)) return null;
                return result;
            }
            while (TryCandidate(candidateSegment, candidateLocal, out old) && (long)old.After + delta <= row.After)
                Next(ref candidateSegment, ref candidateLocal);
        }
        return AddParsed(result, parsed) ? result : null;
    }

    private int FindAffected(int offset)
    {
        var lo = 0;
        var hi = _cache.Segments.Count - 1;
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (_cache.Segments[mid].After > offset) hi = mid;
            else lo = mid + 1;
        }
        var segment = _cache.Segments[lo];
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
        return Math.Max(0, _cache.Segments.Take(lo).Sum(s => s.Count) + left - 1);
    }

    /// <summary>Validates in one scan, dropping dense rows before their structural budget is exceeded.</summary>
    private static CacheState ScanFull(TextSnapshot snapshot, List<Segment> prefix, CancellationToken ct, ScanWork work)
    {
        var segments = new List<Segment>(prefix);
        var checkpoints = new List<int> { 0 };
        var pending = new List<Row>(BlockSize);
        var dense = true;
        var largeRecords = new Dictionary<int, LargeRecord>();
        var expected = -1;
        var total = 0;
        long largeRecordBytes = 0;
        void Account(Row row)
        {
            if (expected < 0) expected = row.Width;
            total = checked(total + row.ErrorCount + (row.Width == expected ? 0 : 1));
            if (row.After - checkpoints[^1] >= CheckpointInterval) checkpoints.Add(row.After);
        }
        // A prefix already consists of certified whole records; reuse its summaries
        // while constructing NEW-version checkpoints and exact width dependencies.
        foreach (var segment in prefix)
            for (var i = 0; i < segment.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                Account(segment.Get(i));
            }
        var cursor = new SnapshotCursor(snapshot, prefix.Count == 0 ? 0 : prefix[^1].After, work);
        var builder = new RecordBuilder();
        while (cursor.Position < snapshot.Length)
        {
            ct.ThrowIfCancellationRequested();
            builder.Reset(cursor.Position);
            var row = ParseRow(cursor, ct, false, out _, builder: builder);
            if (row.End - row.Start > VisibleScanBudget)
            {
                var record = builder.Finish(row);
                largeRecords.Add(row.Start, record);
                largeRecordBytes += record.EstimatedBytes;
            }
            Account(row);
            if (!dense) continue;
            if (segments.Count == MaxDenseSegments ||
                256L + (segments.Count + 1) * 92_160L + largeRecordBytes >= 32L * 1024 * 1024)
            {
                // No prospective block is allocated beyond the cap, including a
                // partial block. Keep scanning with only certified checkpoints.
                dense = false;
                segments = [];
                pending.Clear();
                continue;
            }
            pending.Add(row);
            if (pending.Count != BlockSize) continue;
            segments.Add(new Segment(pending.ToArray(), 0, pending.Count, 0));
            pending.Clear();
        }
        if (pending.Count > 0) segments.Add(new Segment(pending.ToArray(), 0, pending.Count, 0));
        return new CacheState(snapshot.Version, dense ? CsvCacheMode.DenseFull : CsvCacheMode.SparseFull,
            segments, dense ? [] : checkpoints.ToArray(), expected, total) { LargeRecords = largeRecords };
    }

    /// <summary>Calculates fresh global width dependencies from a bounded dense candidate.</summary>
    private static CacheState DenseState(TextSnapshot snapshot, List<Segment> rows)
    {
        var expected = rows.Count == 0 ? -1 : rows[0].Get(0).Width;
        var total = 0;
        foreach (var segment in rows)
            total = checked(total + segment.ErrorCount + segment.Count - segment.WidthCount(expected));
        return new CacheState(snapshot.Version, CsvCacheMode.DenseFull, rows, [], expected, total);
    }

    /// <summary>Prefix validity is never confused with an empty complete file or sparse full cache.</summary>
    private static CacheState PrefixState(TextSnapshot snapshot, List<Segment> rows)
    {
        if (snapshot.Length == 0 || rows.Count > 0 && rows[^1].After == snapshot.Length)
            return DenseState(snapshot, rows);
        return new CacheState(snapshot.Version, CsvCacheMode.Prefix, rows, [],
            rows.Count == 0 ? -1 : rows[0].Get(0).Width, 0);
    }

    /// <summary>Scans at most one bounded prefix; no partial record is committed.</summary>
    private static List<Segment> ExtendVisiblePrefix(TextSnapshot snapshot, List<Segment> prefix,
        TextSpan visible, CancellationToken ct, ScanWork work)
    {
        var start = prefix.Count == 0 ? 0 : prefix[^1].After;
        var limit = Math.Min(snapshot.Length, Math.Min(VisibleScanBudget, Math.Max(visible.End, 8192)));
        if (start >= limit) return prefix;
        var result = new List<Segment>(prefix);
        var rows = new List<Row>(BlockSize);
        var cursor = new SnapshotCursor(snapshot, start, work);
        while (cursor.Position < snapshot.Length && cursor.Position < limit)
        {
            ct.ThrowIfCancellationRequested();
            try { rows.Add(ParseRow(cursor, ct, false, out _, limit)); }
            catch (ScanBudgetExceededException) { break; }
            if (rows.Count == BlockSize)
            {
                if (result.Count == MaxDenseSegments)
                    return ExtendVisiblePrefix(snapshot, [], visible, ct, work);
                result.Add(new Segment(rows.ToArray(), 0, rows.Count, 0));
                rows.Clear();
            }
        }
        if (rows.Count > 0)
        {
            if (result.Count == MaxDenseSegments)
                return ExtendVisiblePrefix(snapshot, [], visible, ct, work);
            result.Add(new Segment(rows.ToArray(), 0, rows.Count, 0));
        }
        return result;
    }

    private static Row ParseRow(SnapshotCursor cursor, CancellationToken ct, bool capture,
        out RowPayload? payload, int maxPosition = int.MaxValue,
        IReadOnlyList<TextSpan>? captureRanges = null, int maxCells = ProjectionCellBudget,
        int[]? windowCellQuota = null, int[]? projectedWindowCells = null,
        RecordBuilder? builder = null, LargeRecord? certificate = null)
    {
        var start = cursor.Position;
        try
        {
            return ParseRowCore(cursor, ct, capture, out payload, maxPosition, captureRanges,
                maxCells, windowCellQuota, projectedWindowCells, builder, certificate);
        }
        finally { cursor.Work.Units += cursor.Position - start; }
    }

    private static Row ParseRowCore(SnapshotCursor cursor, CancellationToken ct, bool capture,
        out RowPayload? payload, int maxPosition,
        IReadOnlyList<TextSpan>? captureRanges, int maxCells,
        int[]? windowCellQuota, int[]? projectedWindowCells, RecordBuilder? builder, LargeRecord? certificate)
    {
        if (cursor.Position >= maxPosition) throw new ScanBudgetExceededException();
        var start = cursor.Position;
        var cells = capture ? new List<Cell>() : null;
        var diagnostics = capture ? new List<Diagnostic>() : null;
        var tokens = capture ? new List<SemanticToken>() : null;
        var width = 0;
        var errors = 0;
        var truncated = capture && maxCells == 0;
        var projectionStartUnits = cursor.Work.Units;
        var projectionStartPosition = cursor.Position;
        var minimumProjectionWindow = 0;
        var activeProjectionWindow = -1;
        void AddDiagnostic(string code, string message, TextSpan span)
        {
            errors++;
            if (diagnostics is null ||
                captureRanges is not null && !IntersectsAny(span.Start, span.End, captureRanges)) return;
            if (diagnostics.Count < ProjectionCellBudget)
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, code, message, span));
            else truncated = true;
        }
        while (true)
        {
            if (cursor.Position >= maxPosition) throw new ScanBudgetExceededException();
            if ((cursor.Position & 4095) == 0) ct.ThrowIfCancellationRequested();
            if (certificate is not null && captureRanges is not null)
            {
                var interested = minimumProjectionWindow;
                while (interested < captureRanges.Count &&
                    (captureRanges[interested].End < cursor.Position ||
                     windowCellQuota is not null && projectedWindowCells![interested] >= windowCellQuota[interested]))
                {
                    if (cursor.Position <= captureRanges[interested].End) truncated = true;
                    interested++;
                }
                if (interested == captureRanges.Count)
                { cursor.Seek(certificate.Row.End); break; }
                if (activeProjectionWindow != interested)
                {
                    activeProjectionWindow = interested;
                    projectionStartUnits = cursor.Work.Units;
                    projectionStartPosition = cursor.Position;
                }
                // Budget certified-checkpoint lookbehind separately from requested
                // delivery: a tail ending just beyond a checkpoint multiple must
                // not lose its last cells to work done before its own start.
                var consumed = cursor.Work.Units - projectionStartUnits + cursor.Position - projectionStartPosition;
                var replayAllowance = 2L * VisibleScanBudget + Math.Min(captureRanges[interested].Length, MaxWindowWidth) + 1;
                if (consumed >= replayAllowance && cursor.Position < certificate.Row.End)
                { truncated = true; minimumProjectionWindow = interested + 1; continue; }
                var seek = certificate.Seek(captureRanges[interested].Start);
                if (seek > cursor.Position) cursor.Seek(seek);
            }
            builder?.Boundary(cursor.Position);
            if (cursor.Peek() == ',')
            {
                if (capture && cells!.Count >= maxCells) truncated = true;
                if (capture && windowCellQuota is not null)
                    for (var i = 0; i < captureRanges!.Count; i++)
                        if (projectedWindowCells![i] >= windowCellQuota[i] &&
                            cursor.Position <= captureRanges[i].End)
                            truncated = true;
                // Offscreen empty fields have no payload or diagnostics. Preserve
                // the boundary cell itself, including the zero-width viewport edge.
                var skipUntil = maxPosition;
                if (capture && cells!.Count < maxCells)
                {
                    if (captureRanges is { } ranges)
                    {
                        for (var i = 0; i < ranges.Count; i++)
                        {
                            if (windowCellQuota is not null && projectedWindowCells![i] >= windowCellQuota[i])
                                continue;
                            var range = ranges[i];
                            if (cursor.Position < range.Start)
                            {
                                skipUntil = Math.Min(skipUntil, range.Start);
                                break;
                            }
                            if (cursor.Position <= range.End)
                            {
                                skipUntil = cursor.Position;
                                break;
                            }
                        }
                    }
                    else skipUntil = cursor.Position;
                }
                if (skipUntil > cursor.Position)
                {
                    if (builder is not null) skipUntil = Math.Min(skipUntil, cursor.Position + CheckpointInterval);
                    width += cursor.SkipCommas(skipUntil, ct);
                    continue;
                }
            }
            var fieldStart = cursor.Position;
            var fieldErrorStart = errors;
            var quoted = cursor.Peek() == '"';
            var contentEnd = fieldStart;
            TextSpan? unclosedSpan = null;
            TextSpan? invalidSpan = null;
            var certifiedField = certificate?.FindField(fieldStart);
            if (certifiedField is { } giant)
            {
                cursor.Seek(giant.Span.End);
                contentEnd = giant.ContentEnd;
                if (giant.Unclosed is { } unclosed) AddDiagnostic("CSV001", "Unterminated quoted field.", unclosed);
                if (giant.Invalid is { } invalid) AddDiagnostic("CSV002",
                    "Characters after a closing quote are not valid in a CSV field.", invalid);
                if (giant.Quoted && IntersectsAny(giant.Span.Start, giant.Span.End, captureRanges!))
                {
                    if (tokens!.Count < ProjectionCellBudget) tokens.Add(new SemanticToken("string", giant.Span));
                    else truncated = true;
                }
                if (!giant.Quoted && giant.ErrorCount > 0)
                {
                    var seenQuotes = new HashSet<int>();
                    foreach (var range in captureRanges!)
                    {
                        var begin = Math.Max(giant.Span.Start, range.Start - (range.Length == 0 ? 1 : 0));
                        var localEnd = (int)Math.Min(giant.Span.End, (long)range.End + (range.Length == 0 ? 1 : 0));
                        if ((long)localEnd - begin > VisibleScanBudget)
                        { localEnd = begin + VisibleScanBudget; truncated = true; }
                        var local = new SnapshotCursor(cursor.Snapshot, begin, cursor.Work);
                        while (local.Position < localEnd)
                        {
                            if ((local.Position & 4095) == 0) ct.ThrowIfCancellationRequested();
                            if (local.Read() == '"' && diagnostics!.Count < ProjectionCellBudget && seenQuotes.Add(local.Position - 1)) AddDiagnostic("CSV003", "Quotes must enclose an entire field.", new TextSpan(local.Position - 1, 1));
                        }
                        cursor.Work.Units += localEnd > begin ? localEnd - begin : 0;
                    }
                }
            }
            else if (quoted)
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
                {
                    unclosedSpan = new TextSpan(fieldStart, cursor.Position - fieldStart);
                    AddDiagnostic("CSV001", "Unterminated quoted field.", unclosedSpan.Value);
                }
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
                    invalidSpan = new TextSpan(invalidStart, cursor.Position - invalidStart);
                    AddDiagnostic("CSV002",
                        "Characters after a closing quote are not valid in a CSV field.", invalidSpan.Value);
                }
                var tokenSpan = new TextSpan(fieldStart, cursor.Position - fieldStart);
                if (tokens is not null &&
                    (captureRanges is null || IntersectsAny(tokenSpan.Start, tokenSpan.End, captureRanges)))
                {
                    if (tokens.Count < ProjectionCellBudget)
                        tokens.Add(new SemanticToken("string", tokenSpan));
                    else truncated = true;
                }
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
            builder?.Field(cellSpan, quoted, contentEnd, unclosedSpan, invalidSpan, errors - fieldErrorStart);
            if (cells is not null &&
                (captureRanges is null || CellIntersectsAny(cellSpan, captureRanges)))
            {
                var hasWindowBudget = windowCellQuota is null;
                if (windowCellQuota is not null)
                    for (var i = 0; i < captureRanges!.Count; i++)
                        if (CellIntersects(cellSpan, captureRanges[i]) &&
                            projectedWindowCells![i] < windowCellQuota[i])
                            hasWindowBudget = true;
                if (hasWindowBudget && cells.Count < maxCells && cellSpan.Length <= MaxProjectedCellSourceLength)
                {
                    var value = quoted
                        ? cursor.Slice(fieldStart + 1, contentEnd - fieldStart - 1)
                            .Replace("\"\"", "\"", StringComparison.Ordinal)
                        : cursor.Slice(fieldStart, cellSpan.Length);
                    cells.Add(new Cell(cellSpan, value));
                    if (windowCellQuota is not null)
                        for (var i = 0; i < captureRanges!.Count; i++)
                            if (CellIntersects(cellSpan, captureRanges[i]) &&
                                projectedWindowCells![i] < windowCellQuota[i])
                                projectedWindowCells[i]++;
                }
                else truncated = true;
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
        payload = capture ? new RowPayload(cells!, diagnostics!, tokens!, truncated) : null;
        return new Row(start, end, cursor.Position, width, errors);
    }

    private static bool CellIntersects(TextSpan cell, TextSpan range) =>
        cell.Length == 0
            ? cell.Start >= range.Start && cell.Start <= range.End
            : Intersects(cell.Start, cell.End, range);

    private static bool CellIntersectsAny(TextSpan cell, IReadOnlyList<TextSpan> ranges)
    {
        foreach (var range in ranges)
            if (CellIntersects(cell, range)) return true;
        return false;
    }

    private static bool IntersectsAny(int start, int end, IReadOnlyList<TextSpan> ranges)
    {
        foreach (var range in ranges)
            if (Intersects(start, end, range)) return true;
        return false;
    }

    private sealed class ScanBudgetExceededException : Exception;

    private static WindowedAnalysis Project(TextSnapshot snapshot, CacheState cache,
        IReadOnlyList<TextSpan> windows, AnalysisScope scope, bool legacyProjection, CancellationToken ct, ScanWork work)
    {
        var segments = cache.Segments;
        var complete = cache.Complete;
        var fullProjection = scope == AnalysisScope.Full;
        var nodes = new List<SemanticNode>();
        var diagnostics = new List<Diagnostic>();
        var tokens = new List<SemanticToken>();
        var expected = cache.ExpectedWidth;
        // A full request still gets complete *semantic validation* on giant files,
        // but projecting every cell would create another document-sized object graph.
        var projectAll = legacyProjection && cache.Mode == CsvCacheMode.DenseFull && complete && fullProjection && snapshot.Length <= 1024 * 1024 &&
            segments.Sum(segment => segment.Count) <= ProjectionRowBudget &&
            segments.Sum(segment => segment.CellCount) <= ProjectionCellBudget &&
            segments.All(segment => segment.MaxRowLength <= MaxProjectedCellSourceLength);
        var projectedCells = 0;
        var cellQuota = new int[windows.Count];
        var projectedWindowCells = new int[windows.Count];
        for (var i = 0; i < cellQuota.Length; i++)
            cellQuota[i] = ProjectionCellBudget / windows.Count +
                (i < ProjectionCellBudget % windows.Count ? 1 : 0);
        var emittedRows = new HashSet<int>();
        var truncatedRows = new HashSet<int>();
        var indexedUntil = cache.IndexedUntil;
        var delivery = new List<WindowProjection>(windows.Count);
        for (var windowIndex = 0; windowIndex < windows.Count; windowIndex++)
        {
            var window = windows[windowIndex];
            var quota = ProjectionRowBudget / windows.Count +
                (windowIndex < ProjectionRowBudget % windows.Count ? 1 : 0);
            var sourceIndexed = complete || indexedUntil > 0 && window.End <= indexedUntil;
            var truncated = !sourceIndexed;
            var rowsInWindow = 0;
            foreach (var row in EnumerateRows(snapshot, cache, window, projectAll, ct, work))
            {
                ct.ThrowIfCancellationRequested();
                if (emittedRows.Contains(row.Start))
                {
                    rowsInWindow++;
                    truncated |= truncatedRows.Contains(row.Start);
                    continue;
                }
                if (!projectAll && (rowsInWindow >= quota || nodes.Count >= ProjectionRowBudget))
                {
                    truncated = true;
                    break;
                }
                emittedRows.Add(row.Start);
                rowsInWindow++;
                cache.LargeRecords.TryGetValue(row.Start, out var certificate);
                var projected = ParseRow(new SnapshotCursor(snapshot, row.Start, work), ct, true, out var payload,
                    captureRanges: projectAll ? null : windows,
                    maxCells: ProjectionCellBudget - projectedCells,
                    windowCellQuota: projectAll ? null : cellQuota,
                    projectedWindowCells: projectAll ? null : projectedWindowCells, certificate: certificate);
                if (certificate is null && (projected.End != row.End || projected.After != row.After || projected.Width != row.Width ||
                    projected.ErrorCount != row.ErrorCount))
                    throw new InvalidOperationException("CSV record index no longer matches its snapshot.");
                var widthMismatch = expected >= 0 && row.Width != expected;
                var children = payload!.Cells.Select(cell => new SemanticNode("cell", cell.Span, value: cell.Value)).ToArray();
                projectedCells += children.Length;
                if (payload.Truncated)
                {
                    truncated = true;
                    truncatedRows.Add(row.Start);
                }
                nodes.Add(new SemanticNode("row", new TextSpan(row.Start, row.End - row.Start), children: children));
                diagnostics.AddRange(payload.Diagnostics);
                if (widthMismatch)
                    diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "CSV004",
                        $"Row has {row.Width} columns; the first row has {expected}.",
                        new TextSpan(row.Start, row.End - row.Start)));
                tokens.AddRange(payload.Tokens);
            }
            delivery.Add(new WindowProjection(window, rowsInWindow, sourceIndexed, truncated));
        }
        var root = new SemanticNode("table", new TextSpan(0, snapshot.Length), children: nodes);
        var completeness = complete ? AnalysisCompleteness.Complete :
            indexedUntil > 0 && windows[^1].End <= indexedUntil
                ? AnalysisCompleteness.CoveredRegion : AnalysisCompleteness.Provisional;
        var coverage = new TextSpan(0, complete ? snapshot.Length : indexedUntil);
        return new WindowedAnalysis(snapshot.Version, completeness, [coverage],
            complete ? cache.Total : null, root, diagnostics, tokens, delivery);
    }

    /// <summary>Seeks only parser-certified record starts, with preceding checkpoint ownership at exact edges.</summary>
    private static IEnumerable<Row> EnumerateRows(TextSnapshot snapshot, CacheState cache,
        TextSpan window, bool all, CancellationToken ct, ScanWork work)
    {
        if (cache.Mode != CsvCacheMode.SparseFull)
        {
            foreach (var segment in cache.Segments)
            {
                ct.ThrowIfCancellationRequested();
                if (!all && !Intersects(segment.Start, segment.After, window)) continue;
                foreach (var row in segment.Enumerate(window, all)) yield return row;
            }
            yield break;
        }
        var index = Array.BinarySearch(cache.Checkpoints, window.Start);
        // Equality must retain the preceding row for a zero-width request; using
        // one lookbehind for all equal edges is simpler and remains bounded.
        index = index >= 0 ? Math.Max(0, index - 1) : Math.Max(0, ~index - 1);
        var cursor = new SnapshotCursor(snapshot, cache.Checkpoints[index], work);
        while (cursor.Position < snapshot.Length && cursor.Position <= window.End)
        {
            ct.ThrowIfCancellationRequested();
            Row row;
            if (cache.LargeRecords.TryGetValue(cursor.Position, out var certificate))
            { row = certificate.Row; cursor = new SnapshotCursor(snapshot, row.After, work); }
            else row = ParseRow(cursor, ct, false, out _);
            if (Intersects(row.Start, row.After, window)) yield return row;
        }
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
        for (var i = 0; i < _cache.Segments.Count; i++)
        {
            if (index < _cache.Segments[i].Count) return (i, index);
            index -= _cache.Segments[i].Count;
        }
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    private bool TryCandidate(int segment, int local, out Row row)
    {
        if (segment < _cache.Segments.Count) { row = _cache.Segments[segment].Get(local); return true; }
        row = default;
        return false;
    }

    private void Next(ref int segment, ref int local)
    {
        local++;
        if (segment < _cache.Segments.Count && local == _cache.Segments[segment].Count)
        {
            segment++;
            local = 0;
        }
    }

    private static bool AddParsed(List<Segment> target, List<Row> rows)
    {
        for (var i = 0; i < rows.Count; i += BlockSize)
        {
            if (target.Count == MaxDenseSegments) return false;
            var count = Math.Min(BlockSize, rows.Count - i);
            var block = rows.GetRange(i, count).ToArray();
            target.Add(new Segment(block, 0, count, 0));
        }
        return true;
    }

    private bool AddSuffix(List<Segment> target, int segmentIndex, int localIndex, int delta)
    {
        if (segmentIndex >= _cache.Segments.Count) return true;
        if (target.Count + _cache.Segments.Count - segmentIndex > MaxDenseSegments) return false;
        if (localIndex != 0)
            target.Add(_cache.Segments[segmentIndex].Slice(localIndex, _cache.Segments[segmentIndex].Count).Shift(delta));
        else target.Add(_cache.Segments[segmentIndex].Shift(delta));
        for (var i = segmentIndex + 1; i < _cache.Segments.Count; i++)
            target.Add(_cache.Segments[i].Shift(delta));
        return true;
    }

    /// <summary>A verified field too large to decode for bounded output; no source text is retained.</summary>
    private readonly record struct GiantField(TextSpan Span, bool Quoted, int ContentEnd,
        TextSpan? Unclosed, TextSpan? Invalid, int ErrorCount);

    /// <summary>One verified oversized record and O(record-length / 64 Ki) field-start seek facts.</summary>
    private sealed record LargeRecord(Row Row, int[] Boundaries, GiantField[] Fields)
    {
        internal long EstimatedBytes => 256L + Boundaries.Length * 8L + Fields.Length * 128L;
        internal int Seek(int position)
        {
            var index = Array.BinarySearch(Boundaries, position);
            return Boundaries[index >= 0 ? index : Math.Max(0, ~index - 1)];
        }
        internal GiantField? FindField(int position)
        {
            var lo = 0;
            var hi = Fields.Length - 1;
            while (lo <= hi)
            {
                var mid = lo + (hi - lo) / 2;
                if (Fields[mid].Span.Start == position) return Fields[mid];
                if (Fields[mid].Span.Start < position) lo = mid + 1; else hi = mid - 1;
            }
            return null;
        }
    }

    /// <summary>Builds sparse field facts during authoritative validation, never guesses quote state.</summary>
    private sealed class RecordBuilder
    {
        private List<int>? _boundaries;
        private List<GiantField>? _fields;
        private int _last;
        private int _start;
        internal void Reset(int start) { _start = _last = start; _boundaries?.Clear(); _fields?.Clear(); }
        internal void Boundary(int position)
        {
            if (position - _last < CheckpointInterval) return;
            if (_boundaries is null) _boundaries = [];
            if (_boundaries.Count == 0) _boundaries.Add(_start);
            _boundaries.Add(position);
            _last = position;
        }
        internal void Field(TextSpan span, bool quoted, int contentEnd, TextSpan? unclosed, TextSpan? invalid, int errorCount)
        {
            if (span.Length > MaxProjectedCellSourceLength)
                (_fields ??= []).Add(new GiantField(span, quoted, contentEnd, unclosed, invalid, errorCount));
        }
        internal LargeRecord Finish(Row row) => new(row, _boundaries is { Count: > 0 } ? _boundaries.ToArray() : [_start], _fields?.ToArray() ?? []);
    }

    private readonly record struct Cell(TextSpan Span, string Value);

    private sealed record RowPayload(List<Cell> Cells, List<Diagnostic> Diagnostics,
        List<SemanticToken> Tokens, bool Truncated);

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

        internal SnapshotCursor(TextSnapshot snapshot, int start, ScanWork work)
        { _snapshot = snapshot; Position = start; Work = work; }
        internal ScanWork Work { get; }
        internal TextSnapshot Snapshot => _snapshot;

        /// <summary>Skips only source certified by this version, excluding skipped units from parser instrumentation.</summary>
        internal void Seek(int position) { Work.Units -= position - Position; Position = position; }
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

        /// <summary>Consumes consecutive delimiters without crossing a visible-scan limit.</summary>
        internal int SkipCommas(int maxPosition, CancellationToken ct)
        {
            var count = 0;
            while (Position < Length && Position < maxPosition)
            {
                ct.ThrowIfCancellationRequested();
                if (Position < _windowStart || Position >= _windowStart + _window.Length)
                {
                    _windowStart = Position;
                    _window = _snapshot.GetText(Position, Math.Min(WindowSize, Length - Position));
                }
                var offset = Position - _windowStart;
                var available = Math.Min(4096, Math.Min(_window.Length - offset, maxPosition - Position));
                var next = _window.AsSpan(offset, available).IndexOfAnyExcept(',');
                var consumed = next < 0 ? available : next;
                Position += consumed;
                count += consumed;
                if (next >= 0) break;
            }
            return count;
        }
    }
}
