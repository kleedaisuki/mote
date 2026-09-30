using System.Collections.ObjectModel;
using Mote.Engine;

namespace Mote.Formats;

/// <summary>A nonnegative logical coordinate range with a checked exclusive end.</summary>
public readonly record struct GridRange
{
    /// <summary>Constructs a range without allowing coordinate overflow.</summary>
    public GridRange(int start, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (start > int.MaxValue - count) throw new ArgumentOutOfRangeException(nameof(count));
        Start = start;
        Count = count;
    }
    /// <summary>First included zero-based coordinate.</summary>
    public int Start { get; }
    /// <summary>Number of requested coordinates.</summary>
    public int Count { get; }
    /// <summary>Exclusive coordinate end.</summary>
    public int End => checked(Start + Count);
}

/// <summary>Delivery state, not an assertion about whole-document semantic validity.</summary>
public enum GridValueState { Complete, Clipped, Oversized, Pending, Missing }

/// <summary>A logical field with independent syntax origin and bounded display coordinates.</summary>
public readonly record struct GridCell(int Column, TextSpan? SourceRange, TextSpan DisplayRange,
    GridValueState State, bool HasSyntaxError, bool DisplaySanitized = false);

/// <summary>An immutable grammar-proved record excluding its following line delimiter.</summary>
public sealed class GridRow
{
    /// <summary>Copies field descriptors and validates record-local ownership.</summary>
    public GridRow(int ordinal, TextSpan sourceRange, TextSpan recordDelimiter, int width,
        IReadOnlyList<GridCell> cells, bool diagnosticsTruncated = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentNullException.ThrowIfNull(cells);
        GridValidation.Range(sourceRange, int.MaxValue);
        GridValidation.Range(recordDelimiter, int.MaxValue);
        if (recordDelimiter.Start != sourceRange.End || recordDelimiter.Length > 2 || cells.Count > 64)
            throw new ArgumentException("Invalid record delimiter or field count.");
        var copy = cells.ToArray();
        var previous = -1;
        var previousOriginEnd = sourceRange.Start;
        foreach (var cell in copy)
        {
            GridValidation.Range(cell.DisplayRange, int.MaxValue);
            if (cell.Column <= previous || !Enum.IsDefined(cell.State))
                throw new ArgumentException("Fields must have strictly increasing valid columns.", nameof(cells));
            if (cell.State == GridValueState.Missing)
            {
                if (cell.Column < width || cell.SourceRange is not null || cell.HasSyntaxError)
                    throw new ArgumentException("Missing fields must be outside the proved row width.", nameof(cells));
            }
            else if (cell.State == GridValueState.Pending)
            {
                if (cell.Column >= width || cell.SourceRange is not null || cell.HasSyntaxError)
                    throw new ArgumentException("Pending fields have no invented syntax origin.", nameof(cells));
            }
            else
            {
                if (cell.Column >= width || cell.SourceRange is not { } origin)
                    throw new ArgumentException("Delivered fields require an actual field origin.", nameof(cells));
                GridValidation.Range(origin, int.MaxValue);
                if (origin.Start < previousOriginEnd || origin.End > sourceRange.End)
                    throw new ArgumentException("Field origin must belong to its record in source order.", nameof(cells));
                previousOriginEnd = origin.End;
            }
            previous = cell.Column;
        }
        Ordinal = ordinal; SourceRange = sourceRange; RecordDelimiter = recordDelimiter;
        Width = width; Cells = new ReadOnlyCollection<GridCell>(copy); DiagnosticsTruncated = diagnosticsTruncated;
    }
    /// <summary>Exact zero-based logical record ordinal.</summary>
    public int Ordinal { get; }
    /// <summary>Whole record syntax without its delimiter.</summary>
    public TextSpan SourceRange { get; }
    /// <summary>Following CR, LF, CRLF, or empty end-of-file range.</summary>
    public TextSpan RecordDelimiter { get; }
    /// <summary>Exact actual field count, independent of delivered columns.</summary>
    public int Width { get; }
    /// <summary>Copied fields in actual column order.</summary>
    public IReadOnlyList<GridCell> Cells { get; }
    /// <summary>Whether record diagnostic delivery omitted entries.</summary>
    public bool DiagnosticsTruncated { get; }
}

/// <summary>Certified coordinate facts independent of semantic validity.</summary>
public readonly record struct GridExtent
{
    /// <summary>Constructs nonnegative prefix and paired optional exact whole-file facts.</summary>
    public GridExtent(int certifiedPrefixRows, int? exactRowCount, int? exactMaxWidth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(certifiedPrefixRows);
        if (exactRowCount is < 0 || exactMaxWidth is < 0 || exactRowCount.HasValue != exactMaxWidth.HasValue ||
            exactRowCount is { } count && certifiedPrefixRows > count || exactRowCount == 0 && exactMaxWidth != 0)
            throw new ArgumentException("Invalid coordinate extent.");
        CertifiedPrefixRows = certifiedPrefixRows; ExactRowCount = exactRowCount; ExactMaxWidth = exactMaxWidth;
    }
    /// <summary>Number of grammar-proved records in the contiguous source prefix.</summary>
    public int CertifiedPrefixRows { get; }
    /// <summary>Exact whole-file row count when known.</summary>
    public int? ExactRowCount { get; }
    /// <summary>Exact whole-file maximum field count when known.</summary>
    public int? ExactMaxWidth { get; }
}

/// <summary>A closed choice of source-follow and logical-row anchors.</summary>
public abstract record CsvGridAnchor
{
    private CsvGridAnchor() { }
    /// <summary>A nonnegative absolute UTF-16 source position, checked against the snapshot by the session.</summary>
    public sealed record Source : CsvGridAnchor
    {
        /// <summary>Constructs a source-follow anchor.</summary>
        public Source(int offset) { ArgumentOutOfRangeException.ThrowIfNegative(offset); Offset = offset; }
        /// <summary>Absolute source offset.</summary>
        public int Offset { get; }
    }
    /// <summary>A nonnegative exact logical record ordinal.</summary>
    public sealed record Row : CsvGridAnchor
    {
        /// <summary>Constructs an ordinal anchor.</summary>
        public Row(int ordinal) { ArgumentOutOfRangeException.ThrowIfNegative(ordinal); Ordinal = ordinal; }
        /// <summary>Requested zero-based row.</summary>
        public int Ordinal { get; }
    }
}

/// <summary>One bounded Grid query and its independent source-decoration interests.</summary>
public sealed class CsvGridRequest
{
    /// <summary>Copies interests; source positions are additionally checked against the snapshot by the session.</summary>
    public CsvGridRequest(IReadOnlyList<TextSpan> sourceInterests, CsvGridAnchor anchor,
        int rowLimit, GridRange columns, AnalysisScope scope)
    {
        ArgumentNullException.ThrowIfNull(sourceInterests);
        ArgumentNullException.ThrowIfNull(anchor);
        if (sourceInterests.Count is < 1 or > 8 || rowLimit is < 1 or > 256 || columns.Count is < 1 or > 64 ||
            (long)rowLimit * columns.Count > 8192 || !Enum.IsDefined(scope))
            throw new ArgumentException("Grid request exceeds bounded coordinate limits.");
        var copy = sourceInterests.ToArray();
        long width = 0;
        foreach (var span in copy) { GridValidation.Range(span, int.MaxValue); width += span.Length; }
        if (width > 512 * 1024) throw new ArgumentException("Source interests exceed the delivery budget.", nameof(sourceInterests));
        SourceInterests = new ReadOnlyCollection<TextSpan>(copy); Anchor = anchor;
        RowLimit = rowLimit; Columns = columns; Scope = scope;
    }
    /// <summary>Copied source interests; not giant-field origin requests.</summary>
    public IReadOnlyList<TextSpan> SourceInterests { get; }
    /// <summary>Source-follow or ordinal query.</summary>
    public CsvGridAnchor Anchor { get; }
    /// <summary>Maximum delivered records.</summary>
    public int RowLimit { get; }
    /// <summary>Requested actual column coordinates.</summary>
    public GridRange Columns { get; }
    /// <summary>Desired semantic analysis breadth.</summary>
    public AnalysisScope Scope { get; }
}

/// <summary>Immutable, bounded, snapshot-free native table presentation.</summary>
public sealed class GridRenderProjection
{
    /// <summary>Maximum display arena length in UTF-16 units.</summary>
    public const int MaxDisplayLength = 64 * 1024;
    /// <summary>Maximum display length of one field in UTF-16 units.</summary>
    public const int MaxCellDisplayLength = 1024;
    /// <summary>Maximum delivered row count.</summary>
    public const int MaxRows = 256;
    /// <summary>Maximum requested column count.</summary>
    public const int MaxColumns = 64;
    /// <summary>Maximum delivered field count.</summary>
    public const int MaxCells = 8192;
    /// <summary>Maximum delivered diagnostics.</summary>
    public const int MaxDiagnostics = 8192;
    /// <summary>Copies bounded data and validates coordinates, ownership, Unicode and completeness.</summary>
    public GridRenderProjection(long version, int sourceLength, GridExtent extent, AnalysisCompleteness completeness,
        IReadOnlyList<TextSpan> certifiedCoverage, int? totalDiagnosticCount, GridRange requestedRows,
        GridRange requestedColumns, string displayText, IReadOnlyList<GridRow> rows,
        IReadOnlyList<Diagnostic> diagnostics, bool rowsTruncated, bool columnsTruncated,
        bool diagnosticsTruncated, bool valuesTruncated, CsvGridAnchor? requestedAnchor = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(version);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceLength);
        ArgumentNullException.ThrowIfNull(certifiedCoverage); ArgumentNullException.ThrowIfNull(displayText);
        ArgumentNullException.ThrowIfNull(rows); ArgumentNullException.ThrowIfNull(diagnostics);
        if (!Enum.IsDefined(completeness) || rows.Count > MaxRows || diagnostics.Count > MaxDiagnostics ||
            certifiedCoverage.Count > 8192 || displayText.Length > MaxDisplayLength || requestedRows.Count > MaxRows ||
            requestedColumns.Count is < 1 or > 64 || (long)requestedRows.Count * requestedColumns.Count > MaxCells)
            throw new ArgumentException("Grid projection exceeds its resource limits.");
        if ((completeness == AnalysisCompleteness.Complete) != totalDiagnosticCount.HasValue || totalDiagnosticCount is < 0 ||
            totalDiagnosticCount < diagnostics.Count || completeness == AnalysisCompleteness.Complete && extent.ExactRowCount is null)
            throw new ArgumentException("Complete projection requires exact extent and diagnostic total.");
        var coverageCopy = certifiedCoverage.ToArray(); var rowCopy = rows.ToArray(); var diagnosticCopy = diagnostics.ToArray();
        ValidateCoverage(coverageCopy, sourceLength, completeness);
        GridValidation.Unicode(displayText);
        ValidateRows(rowCopy, sourceLength, extent, requestedRows, requestedColumns, displayText);
        foreach (var diagnostic in diagnosticCopy)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            GridValidation.Range(diagnostic.Span, sourceLength);
            if (!Enum.IsDefined(diagnostic.Severity) || string.IsNullOrEmpty(diagnostic.Code) || diagnostic.Message is null)
                throw new ArgumentException("Invalid diagnostic.", nameof(diagnostics));
        }
        Version = version; SourceLength = sourceLength; Extent = extent; Completeness = completeness;
        CertifiedCoverage = new ReadOnlyCollection<TextSpan>(coverageCopy); TotalDiagnosticCount = totalDiagnosticCount;
        RequestedRows = requestedRows; RequestedAnchor = requestedAnchor ?? new CsvGridAnchor.Row(requestedRows.Start);
        RequestedColumns = requestedColumns; DisplayText = displayText;
        Rows = new ReadOnlyCollection<GridRow>(rowCopy); Diagnostics = new ReadOnlyCollection<Diagnostic>(diagnosticCopy);
        RowsTruncated = rowsTruncated; ColumnsTruncated = columnsTruncated;
        DiagnosticsTruncated = diagnosticsTruncated; ValuesTruncated = valuesTruncated;
    }
    /// <summary>Snapshot version of all source coordinates.</summary>
    public long Version { get; }
    /// <summary>Snapshot UTF-16 length used to validate origins.</summary>
    public int SourceLength { get; }
    /// <summary>Whole-file coordinate facts.</summary>
    public GridExtent Extent { get; }
    /// <summary>Semantic certification independent of delivery omissions.</summary>
    public AnalysisCompleteness Completeness { get; }
    /// <summary>Copied sorted disjoint certified source ranges.</summary>
    public IReadOnlyList<TextSpan> CertifiedCoverage { get; }
    /// <summary>Exact whole-file diagnostic count for Complete only.</summary>
    public int? TotalDiagnosticCount { get; }
    /// <summary>Resolved or explicit ordinal range; empty (0, 0) for an unindexed source-follow anchor.</summary>
    public GridRange RequestedRows { get; }
    /// <summary>Original query identity; unindexed source-follow requests do not invent an ordinal.</summary>
    public CsvGridAnchor RequestedAnchor { get; }
    /// <summary>Requested actual field coordinates.</summary>
    public GridRange RequestedColumns { get; }
    /// <summary>Bounded display arena; never authoritative clipboard or source data.</summary>
    public string DisplayText { get; }
    /// <summary>Copied ordered grammar-proved rows.</summary>
    public IReadOnlyList<GridRow> Rows { get; }
    /// <summary>Copied exact source diagnostics.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    /// <summary>Whether requested records were unavailable or omitted.</summary>
    public bool RowsTruncated { get; }
    /// <summary>Whether columns were omitted.</summary>
    public bool ColumnsTruncated { get; }
    /// <summary>Whether diagnostic delivery was omitted.</summary>
    public bool DiagnosticsTruncated { get; }
    /// <summary>Whether values were clipped or unavailable.</summary>
    public bool ValuesTruncated { get; }

    /// <summary>Preserves interval gaps and requires full coverage for complete semantics.</summary>
    private static void ValidateCoverage(TextSpan[] coverage, int length, AnalysisCompleteness completeness)
    {
        var end = -1;
        foreach (var span in coverage)
        {
            GridValidation.Range(span, length);
            if (span.Start < end) throw new ArgumentException("Coverage must be sorted and nonoverlapping.");
            end = span.End;
        }
        if (completeness == AnalysisCompleteness.Complete && (coverage.Length != 1 || coverage[0] != new TextSpan(0, length)))
            throw new ArgumentException("Complete projection must certify the entire source.");
    }

    /// <summary>Validates requested coordinates and arena order without parsing source.</summary>
    private static void ValidateRows(GridRow[] rows, int length, GridExtent extent, GridRange requestedRows,
        GridRange columns, string display)
    {
        var ordinal = -1; var sourceEnd = -1; var displayEnd = 0; var cellCount = 0;
        foreach (var row in rows)
        {
            ArgumentNullException.ThrowIfNull(row);
            GridValidation.Range(row.SourceRange, length); GridValidation.Range(row.RecordDelimiter, length);
            if (row.Ordinal <= ordinal || row.SourceRange.Start < sourceEnd || row.Ordinal < requestedRows.Start ||
                row.Ordinal >= requestedRows.End || extent.ExactRowCount is { } count && row.Ordinal >= count ||
                extent.ExactMaxWidth is { } width && row.Width > width)
                throw new ArgumentException("Rows are not ordered within the requested certified coordinates.");
            ordinal = row.Ordinal; sourceEnd = row.RecordDelimiter.End;
            cellCount += row.Cells.Count;
            if (cellCount > MaxCells) throw new ArgumentException("Too many delivered fields.");
            foreach (var cell in row.Cells)
            {
                GridValidation.Range(cell.DisplayRange, display.Length);
                if (cell.Column < columns.Start || cell.Column >= columns.End || cell.DisplayRange.Start < displayEnd ||
                    cell.DisplayRange.Length > MaxCellDisplayLength)
                    throw new ArgumentException("Invalid field display coordinates.");
                GridValidation.Boundary(display, cell.DisplayRange.Start); GridValidation.Boundary(display, cell.DisplayRange.End);
                displayEnd = cell.DisplayRange.End;
            }
        }
    }
}

/// <summary>Source decoration and Grid delivery produced by one serialized session update.</summary>
public sealed record CsvGridAnalysis(WindowedAnalysis Source, GridRenderProjection Grid);

/// <summary>Optional CSV-specific capability; cancellation cannot publish candidate cache state.</summary>
public interface ICsvGridFormatSession : IWindowedFormatSession
{
    /// <summary>Updates once, then delivers independent source and Grid interests at the same snapshot version.</summary>
    CsvGridAnalysis AnalyzeGrid(TextSnapshot snapshot, IReadOnlyList<VersionedEdit> changesSinceCommittedState,
        CsvGridRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Shared construction checks; no parser state or source snapshot is retained.</summary>
internal static class GridValidation
{
    /// <summary>Rejects negative, overflowing or out-of-bounds UTF-16 spans.</summary>
    internal static void Range(TextSpan span, int length)
    {
        if (span.Start < 0 || span.Length < 0 || span.Start > length - span.Length)
            throw new ArgumentException("Invalid UTF-16 range.");
    }
    /// <summary>Rejects display cutoffs inside a Unicode scalar.</summary>
    internal static void Boundary(string text, int offset)
    {
        if (offset > 0 && offset < text.Length && char.IsHighSurrogate(text[offset - 1]) && char.IsLowSurrogate(text[offset]))
            throw new ArgumentException("Display range splits a surrogate pair.");
    }
    /// <summary>Requires well-formed display UTF-16 even when source contains malformed surrogates.</summary>
    internal static void Unicode(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (++i >= text.Length || !char.IsLowSurrogate(text[i])) throw new ArgumentException("Unpaired display surrogate.");
            }
            else if (char.IsLowSurrogate(text[i])) throw new ArgumentException("Unpaired display surrogate.");
        }
    }
}
