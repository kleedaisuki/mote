using System.Text;
using Mote.Engine;

namespace Mote.Formats;

/// <summary>Explicit CSV export grammar; Source and Rows preserve original syntax.</summary>
public enum CsvGridCopyKind { Value, Csv, Tsv, Rows, Source, CsvPadded }

/// <summary>Logical delivered coordinates; reversed endpoints normalize to an inclusive rectangle.</summary>
public readonly record struct CsvGridSelection(int Row, int Column, int? EndRow = null,
    int? EndColumn = null, bool WholeRows = false);

/// <summary>A prepared, non-mutating command; failure carries neither clipboard data nor an edit.</summary>
public readonly record struct CsvGridCommandResult(bool Success, string? Payload, TextChange? Change, string? Error);

/// <summary>Prepares bounded CSV source-backed commands without parser state, I/O or document mutation.</summary>
public static class CsvGridCommands
{
    /// <summary>Maximum admitted UTF-16 output units, not bytes or display units.</summary>
    public const int MaxPayloadLength = 8 * 1024 * 1024;

    /// <summary>Reads proved origins without changing parser state, source, or the native clipboard.</summary>
    /// <remarks>
    /// The caller must supply a projection produced from this exact snapshot. Version and length
    /// checks reject obsolete data but cannot authenticate an arbitrary constructed projection
    /// or distinguish different documents with equal versions. Missing/off-window origins refuse
    /// export. CSV/quoted TSV explicitly quote empty values; Source/Rows preserve raw syntax.
    /// All text exports refuse embedded NUL before returning a payload.
    /// </remarks>
    /// <example><code>
    /// var result = CsvGridCommands.PrepareCopy(snapshot, grid,
    ///     new CsvGridSelection(0, 0), CsvGridCopyKind.Value);
    /// if (result.Success) PublishText(result.Payload!);
    /// </code></example>
    public static CsvGridCommandResult PrepareCopy(TextSnapshot snapshot, GridRenderProjection grid,
        CsvGridSelection selection, CsvGridCopyKind kind, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(grid);
        if (Validate(snapshot, grid, selection, kind == CsvGridCopyKind.Rows) is { } validationError) return Fail(validationError);
        string? error = null;
        cancellationToken.ThrowIfCancellationRequested();
        var firstRow = Math.Min(selection.Row, selection.EndRow ?? selection.Row);
        var lastRow = Math.Max(selection.Row, selection.EndRow ?? selection.Row);
        var firstColumn = Math.Min(selection.Column, selection.EndColumn ?? selection.Column);
        var lastColumn = Math.Max(selection.Column, selection.EndColumn ?? selection.Column);
        if (kind == CsvGridCopyKind.Rows || kind == CsvGridCopyKind.Source && selection.WholeRows)
            return CopyRows(snapshot, grid, firstRow, lastRow, cancellationToken);
        if (kind is CsvGridCopyKind.Source or CsvGridCopyKind.Value)
        {
            if (firstRow != lastRow || firstColumn != lastColumn) return Fail("Select one cell for this command.");
            var cell = Find(grid, firstRow, firstColumn);
            if (cell is not { SourceRange: { } span }) return Fail("This cell has no available source origin.");
            if (kind == CsvGridCopyKind.Source)
                return Source(snapshot, span, cancellationToken);
            if (!Decode(snapshot, cell.Value, cancellationToken, out var value, out error)) return Fail(error!);
            cancellationToken.ThrowIfCancellationRequested();
            return Payload(value!);
        }
        if (kind is not (CsvGridCopyKind.Csv or CsvGridCopyKind.Tsv or CsvGridCopyKind.CsvPadded))
            return Fail("This is not a Copy command.");
        var delimiter = kind == CsvGridCopyKind.Tsv ? '\t' : ',';
        var builder = new StringBuilder();
        for (var row = firstRow; row <= lastRow; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Row(grid, row) is not { } record) return Fail("Selected rows are not available in the current table window.");
            if (row != firstRow && !Append(builder, "\r\n")) return TooLarge();
            for (var column = firstColumn; column <= lastColumn; column++)
            {
                var cell = Cell(record, column);
                string? value;
                if (cell is { State: GridValueState.Missing } && kind == CsvGridCopyKind.CsvPadded) value = "";
                else if (cell is not { } available || !Decode(snapshot, available, cancellationToken, out value, out error))
                    return Fail(cell is null ? "Selected columns are not available in the current table window." : error ?? "Missing fields require explicit padded Copy.");
                if (column != firstColumn && !Append(builder, delimiter.ToString())) return TooLarge();
                if (!AppendEncoded(builder, value!, delimiter, false, cancellationToken)) return TooLarge();
            }
        }
        return Payload(builder.ToString());
    }

    /// <summary>Prepares one exact field replacement; complete fields alone enter the value editor.</summary>
    /// <remarks>
    /// Requires a projection from this exact snapshot, as for PrepareCopy. Preserves existing
    /// quote style and unrelated syntax. The caller owns document identity admission and applying
    /// the returned change; this method never modifies the document or creates an Undo transaction.
    /// </remarks>
    /// <example><code>
    /// var result = CsvGridCommands.PrepareReplace(snapshot, grid,
    ///     new CsvGridSelection(0, 1), "updated value");
    /// if (result.Success) document.Apply(result.Change!.Value);
    /// </code></example>
    public static CsvGridCommandResult PrepareReplace(TextSnapshot snapshot, GridRenderProjection grid,
        CsvGridSelection selection, string replacement, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(replacement);
        if (Validate(snapshot, grid, selection, false) is { } validationError) return Fail(validationError);
        string? error = null;
        cancellationToken.ThrowIfCancellationRequested();
        if (selection.EndRow is { } endRow && endRow != selection.Row ||
            selection.EndColumn is { } endColumn && endColumn != selection.Column || selection.WholeRows)
            return Fail("Replacement requires one selected cell.");
        var cell = Find(grid, selection.Row, selection.Column);
        if (cell is not { State: GridValueState.Complete, HasSyntaxError: false, SourceRange: { } span })
            return Fail("Replace requires a complete bounded field without syntax errors; edit its source instead.");
        if (!Decode(snapshot, cell.Value, cancellationToken, out _, out error)) return Fail(error!);
        if (replacement.Length > MaxPayloadLength) return TooLarge();
        if (!ValidUnicode(replacement)) return Fail("Replacement contains an unpaired UTF-16 surrogate.");
        var quoted = span.Length > 0 && snapshot.GetText(span.Start, 1)[0] == '"';
        var builder = new StringBuilder();
        if (!AppendEncoded(builder, replacement, ',', quoted, cancellationToken)) return TooLarge();
        return new(true, null, new TextChange(span.Start, span.Length, builder.ToString()), null);
    }

    /// <summary>Checks versions and bounded delivered coordinates before any source read.</summary>
    private static string? Validate(TextSnapshot snapshot, GridRenderProjection grid, CsvGridSelection selection, bool wholeRows)
    {
        if (snapshot.Version != grid.Version || snapshot.Length != grid.SourceLength)
            return "The table selection belongs to an obsolete document version.";
        var endRow = selection.EndRow ?? selection.Row;
        var endColumn = selection.EndColumn ?? selection.Column;
        if (selection.Row < 0 || endRow < 0 || selection.Column < 0 || endColumn < 0 ||
            Math.Min(selection.Row, endRow) < grid.RequestedRows.Start || Math.Max(selection.Row, endRow) >= grid.RequestedRows.End ||
            !selection.WholeRows && !wholeRows &&
            (Math.Min(selection.Column, endColumn) < grid.RequestedColumns.Start || Math.Max(selection.Column, endColumn) >= grid.RequestedColumns.End))
            return "The selection is outside the available table window.";
        return null;
    }

    /// <summary>Preserves contiguous record syntax and every actual delimiter, including the final one.</summary>
    private static CsvGridCommandResult CopyRows(TextSnapshot snapshot, GridRenderProjection grid,
        int first, int last, CancellationToken cancellationToken)
    {
        GridRow? previous = null;
        var start = 0;
        for (var ordinal = first; ordinal <= last; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = Row(grid, ordinal);
            if (row is null || previous is not null && previous.RecordDelimiter.End != row.SourceRange.Start)
                return Fail("Selected source rows are not available contiguously.");
            if (previous is null) start = row.SourceRange.Start;
            previous = row;
        }
        return Source(snapshot, new TextSpan(start, previous!.RecordDelimiter.End - start), cancellationToken);
    }

    /// <summary>Materializes only admitted syntax; oversized refusal does not claim a NUL scan occurred.</summary>
    private static CsvGridCommandResult Source(TextSnapshot snapshot, TextSpan span, CancellationToken cancellationToken)
    {
        if (span.Length > MaxPayloadLength) return TooLarge();
        cancellationToken.ThrowIfCancellationRequested();
        var result = Payload(snapshot.GetText(span.Start, span.Length));
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    /// <summary>Decodes exact field syntax, never sanitized or clipped display strings.</summary>
    private static bool Decode(TextSnapshot snapshot, GridCell cell, CancellationToken cancellationToken,
        out string? value, out string? error)
    {
        value = null; error = null;
        if (cell.HasSyntaxError || cell.SourceRange is not { } span || cell.State is GridValueState.Missing or GridValueState.Pending)
        { error = "The field is missing, pending, or syntactically invalid; Copy source instead."; return false; }
        var quoted = span.Length > 0 && snapshot.GetText(span.Start, 1)[0] == '"';
        if (span.Length > (quoted ? 2L * MaxPayloadLength + 2 : MaxPayloadLength))
        { error = "Copy exceeds the 8 Mi UTF-16 output limit; NUL validation was not performed."; return false; }
        var syntax = snapshot.GetText(span.Start, span.Length);
        if (!quoted)
        {
            if (syntax.Contains('"') || syntax.Contains('\r') || syntax.Contains('\n') || syntax.Contains(','))
            { error = "The field syntax is invalid."; return false; }
            value = syntax;
            return true;
        }
        if (syntax.Length < 2 || syntax[^1] != '"') { error = "The quoted field is incomplete."; return false; }
        var builder = new StringBuilder(Math.Min(syntax.Length, MaxPayloadLength));
        for (var i = 1; i < syntax.Length - 1; i++)
        {
            if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            var ch = syntax[i];
            if (ch == '"' && (++i >= syntax.Length - 1 || syntax[i] != '"'))
            { error = "The quoted field syntax is invalid."; return false; }
            if (builder.Length == MaxPayloadLength) { error = "Copy exceeds the 8 Mi UTF-16 output limit."; return false; }
            builder.Append(ch);
        }
        value = builder.ToString();
        return true;
    }

    /// <summary>Encodes the current CSV or quoted-TSV grammar with explicit empty tokens.</summary>
    private static bool AppendEncoded(StringBuilder builder, string value, char delimiter, bool preserveQuotes, CancellationToken cancellationToken)
    {
        var quotes = 0;
        var quoted = preserveQuotes || value.Length == 0;
        for (var i = 0; i < value.Length; i++)
        {
            if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            var ch = value[i];
            if (ch == '"') quotes++;
            if (ch == delimiter || ch is '"' or '\r' or '\n') quoted = true;
        }
        var length = (long)value.Length + (quoted ? quotes + 2L : 0);
        if (length > MaxPayloadLength - builder.Length) return false;
        if (!quoted) { builder.Append(value); return true; }
        builder.Append('"');
        for (var i = 0; i < value.Length; i++)
        {
            if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            var ch = value[i];
            builder.Append(ch); if (ch == '"') builder.Append('"');
        }
        builder.Append('"');
        return true;
    }

    /// <summary>Checks payload size before concatenation.</summary>
    private static bool Append(StringBuilder builder, string value)
    {
        if (value.Length > MaxPayloadLength - builder.Length) return false;
        builder.Append(value); return true;
    }

    /// <summary>Native text publication cannot losslessly represent embedded NUL.</summary>
    private static CsvGridCommandResult Payload(string value) => value.Contains('\0')
        ? Fail("Native text Copy cannot represent embedded NUL losslessly; reveal or save the source instead.")
        : new(true, value, null, null);

    /// <summary>Locates a delivered actual coordinate, independent of sparse list position.</summary>
    private static GridCell? Find(GridRenderProjection grid, int row, int column) =>
        Row(grid, row) is { } record ? Cell(record, column) : null;

    /// <summary>Finds a proved logical row without scanning or retaining source.</summary>
    private static GridRow? Row(GridRenderProjection grid, int ordinal)
    {
        var low = 0;
        var high = grid.Rows.Count - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var row = grid.Rows[middle];
            if (row.Ordinal == ordinal) return row;
            if (row.Ordinal < ordinal) low = middle + 1;
            else high = middle - 1;
        }
        return null;
    }

    /// <summary>Finds a delivered field by its actual coordinate, not sparse list position.</summary>
    private static GridCell? Cell(GridRow row, int column)
    {
        var start = row.Cells.Count == 0 ? 0 : row.Cells[0].Column;
        var local = column - start;
        if ((uint)local < (uint)row.Cells.Count && row.Cells[local].Column == column) return row.Cells[local];
        foreach (var cell in row.Cells) if (cell.Column == column) return cell;
        return null;
    }

    /// <summary>Prevents a replacement that the canonical Engine would reject.</summary>
    private static bool ValidUnicode(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i])) { if (++i >= value.Length || !char.IsLowSurrogate(value[i])) return false; }
            else if (char.IsLowSurrogate(value[i])) return false;
        }
        return true;
    }

    /// <summary>Reports refusal without any prepared payload or edit.</summary>
    private static CsvGridCommandResult Fail(string error) => new(false, null, null, error);
    /// <summary>Size refusal does not assert that an unread source interval was NUL-free.</summary>
    private static CsvGridCommandResult TooLarge() => Fail("The operation exceeds the 8 Mi UTF-16 output limit; no payload was prepared.");
}
