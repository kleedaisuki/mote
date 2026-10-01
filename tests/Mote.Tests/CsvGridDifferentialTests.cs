using System.Text;
using Mote.Engine;
using Mote.Formats;

namespace Mote.Tests;

/// <summary>Independent legacy-parser and explicit-coordinate checks for bounded CSV Grid delivery.</summary>
public sealed class CsvGridDifferentialTests
{
    /// <summary>Every source offset, including CRLF interiors and EOF, resolves to its actual record.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("a,")]
    [InlineData("\r\n\n\r")]
    [InlineData("a,b\r\n\"c\nd\",\"e\"\"f\"\r,\nlast")]
    [InlineData("a,b,c\nq\n,,\n")]
    public void All_source_boundaries_and_ordinals_match_legacy_parser(string source)
    {
        using var document = new Document(source);
        using var session = new CsvIncrementalSession();
        var oracle = new CsvPolicy().Analyze(source);
        for (var offset = 0; offset <= source.Length; offset++)
        {
            var ordinal = ExpectedOwner(oracle.Root.Children, offset);
            AssertProjection(source, oracle, Query(session, document, new CsvGridAnchor.Source(offset)), ordinal, 8);
        }
        for (var ordinal = 0; ordinal <= oracle.Root.Children.Count; ordinal++)
            AssertProjection(source, oracle, Query(session, document, new CsvGridAnchor.Row(ordinal)), ordinal, 8);
    }

    /// <summary>Seeded quoted, escaped, multiline, empty and ragged records use a separate semantic oracle.</summary>
    [Fact]
    public void Randomized_valid_csv_matches_exact_values_spans_and_missing_columns()
    {
        var random = new Random(0x435356);
        string[] values = ["", "x", "a,b", "say \"hi\"", "\r\n", "猫😀", "\t", "x\ry\nz", " spaced "];
        string[] delimiters = ["\n", "\r", "\r\n"];
        for (var trial = 0; trial < 120; trial++)
        {
            var source = new StringBuilder();
            var count = random.Next(1, 20);
            for (var row = 0; row < count; row++)
            {
                var width = random.Next(1, 7);
                for (var column = 0; column < width; column++)
                {
                    if (column > 0) source.Append(',');
                    var value = values[random.Next(values.Length)];
                    source.Append('"').Append(value.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
                }
                if (row + 1 < count || random.Next(2) == 0) source.Append(delimiters[random.Next(3)]);
            }
            var text = source.ToString();
            using var document = new Document(text);
            using var session = new CsvIncrementalSession();
            var oracle = new CsvPolicy().Analyze(text);
            for (var probe = 0; probe < 8; probe++)
            {
                var ordinal = random.Next(count);
                AssertProjection(text, oracle, Query(session, document, new CsvGridAnchor.Row(ordinal)), ordinal, 8);
                var offset = random.Next(text.Length + 1);
                AssertProjection(text, oracle, Query(session, document, new CsvGridAnchor.Source(offset)),
                    ExpectedOwner(oracle.Root.Children, offset), 8);
            }
        }
    }

    /// <summary>Versioned replacements and delimiter-changing edits cannot reuse stale coordinates.</summary>
    [Fact]
    public void Edits_and_disjoint_source_interests_match_new_snapshot()
    {
        using var document = new Document("a,b\r\nc,d\r\ne,f");
        using var session = new CsvIncrementalSession();
        Query(session, document, new CsvGridAnchor.Row(0));
        TextChange[] changes = [new(0, 1, "\"alpha\nbeta\""), new(0, 0, "p,q\n"), new(4, 1, ""), new(0, 4, "")];
        foreach (var change in changes)
        {
            var before = document.Snapshot;
            var after = document.Apply(change);
            var source = after.GetText(0, after.Length);
            var request = new CsvGridRequest([new TextSpan(0, 1), new TextSpan(source.Length - 1, 1)],
                new CsvGridAnchor.Row(1), 8, new GridRange(0, 8), AnalysisScope.Full);
            var result = session.AnalyzeGrid(after, [new VersionedEdit(before.Version, after.Version, change)], request);
            Assert.Equal(after.Version, result.Source.Version);
            Assert.Equal(after.Version, result.Grid.Version);
            Assert.Equal(2, result.Source.Windows.Count);
            AssertProjection(source, new CsvPolicy().Analyze(source), result.Grid, 1, 8);
        }
    }

    /// <summary>Sparse checkpoints preserve exact ordinals and both CRLF offsets near EOF.</summary>
    [Fact]
    public void Sparse_index_seek_matches_short_row_oracle()
    {
        const int count = 262_150;
        var source = string.Concat(Enumerable.Repeat("a,b\r\n", count));
        using var document = new Document(source);
        using var session = new CsvIncrementalSession();
        var oracle = new CsvPolicy().Analyze(source);
        Query(session, document, new CsvGridAnchor.Row(count - 3));
        Assert.Equal("SparseFull", session.CacheStatistics.Mode);
        foreach (var ordinal in new[] { 0, 1023, 1024, 13_107, count - 2, count - 1, count })
            AssertProjection(source, oracle, Query(session, document, new CsvGridAnchor.Row(ordinal)), ordinal, 8);
        foreach (var offset in new[] { 65_535, 65_536, source.Length - 2, source.Length - 1, source.Length })
            AssertProjection(source, oracle, Query(session, document, new CsvGridAnchor.Source(offset)),
                ExpectedOwner(oracle.Root.Children, offset), 8);
    }

    /// <summary>An omitted giant display value does not renumber its following ordinary field.</summary>
    [Fact]
    public void Giant_first_field_keeps_tail_at_column_one()
    {
        var source = "\"" + new string('x', 70_000) + "\",tail\nend,z";
        using var document = new Document(source);
        using var session = new CsvIncrementalSession();
        var grid = Query(session, document, new CsvGridAnchor.Row(0));
        var first = grid.Rows[0];
        Assert.Equal(2, first.Width);
        Assert.Equal(GridValueState.Oversized, first.Cells[0].State);
        Assert.Equal(new TextSpan(0, 70_002), first.Cells[0].SourceRange);
        Assert.Equal(1, first.Cells[1].Column);
        Assert.Equal(new TextSpan(70_003, 4), first.Cells[1].SourceRange);
        Assert.Equal("tail", grid.DisplayText.Substring(first.Cells[1].DisplayRange.Start, first.Cells[1].DisplayRange.Length));
        var tailOnly = Query(session, document, new CsvGridAnchor.Source(70_003), columns: new GridRange(1, 1));
        Assert.Equal(1, tailOnly.Rows[0].Cells[0].Column);
        Assert.Equal("tail", tailOnly.DisplayText[..4]);
    }

    /// <summary>Cold unknown ordinals are pending status rather than fabricated row origins.</summary>
    [Fact]
    public void Cold_visible_unknown_row_is_provisional_and_empty()
    {
        using var document = new Document(string.Concat(Enumerable.Repeat("a,b\n", 100_000)));
        using var session = new CsvIncrementalSession();
        var result = Query(session, document, new CsvGridAnchor.Row(90_000), AnalysisScope.Visible);
        Assert.Empty(result.Rows);
        Assert.Null(result.Extent.ExactRowCount);
        Assert.Equal(AnalysisCompleteness.Provisional, result.Completeness);
        Assert.True(result.RowsTruncated);
    }

    /// <summary>Deterministic cancellation leaves committed version and all instrumentation unchanged.</summary>
    [Fact]
    public void Precanceled_new_snapshot_does_not_publish_cache_or_statistics()
    {
        using var document = new Document("a,b\nc,d");
        using var session = new CsvIncrementalSession();
        Query(session, document, new CsvGridAnchor.Row(0));
        var statistics = session.CacheStatistics;
        var old = document.Snapshot;
        var change = new TextChange(0, 1, "changed");
        var current = document.Apply(change);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var request = new CsvGridRequest([new TextSpan(0, 1)], new CsvGridAnchor.Row(0), 8, new GridRange(0, 8), AnalysisScope.Full);
        Assert.Throws<OperationCanceledException>(() => session.AnalyzeGrid(current,
            [new VersionedEdit(old.Version, current.Version, change)], request, canceled.Token));
        Assert.Equal(statistics, session.CacheStatistics);
    }

    /// <summary>Detached narrow columns keep actual coordinates, including missing ragged-row fields.</summary>
    [Fact]
    public void Narrow_column_windows_match_legacy_field_origins()
    {
        const string source = "zero,\"one,two\",,three,four\r\nshort\r\n,,,tail,";
        using var document = new Document(source);
        using var session = new CsvIncrementalSession();
        var oracle = new CsvPolicy().Analyze(source);
        for (var start = 0; start < 9; start++)
        {
            var grid = Query(session, document, new CsvGridAnchor.Row(0), columns: new GridRange(start, 3));
            AssertProjection(source, oracle, grid, 0, 8);
            Assert.All(grid.Rows, row => Assert.Equal(Enumerable.Range(start, 3), row.Cells.Select(cell => cell.Column)));
        }
    }

    /// <summary>Adversarial edit chains invalidate multiline boundaries before comparing the new oracle.</summary>
    [Fact]
    public void Randomized_versioned_edits_match_reparsed_snapshot()
    {
        using var document = new Document(string.Concat(Enumerable.Repeat("a,\"b\nc\",d\r\n", 20)));
        using var session = new CsvIncrementalSession();
        Query(session, document, new CsvGridAnchor.Row(0));
        var random = new Random(0x45444954);
        string[] insertions = ["", ",", "\"", "\r", "\n", "\r\n", "\"x,y\"", "😀", "tail"];
        for (var step = 0; step < 80; step++)
        {
            var before = document.Snapshot;
            var position = random.Next(before.Length + 1);
            var removal = random.Next(Math.Min(4, before.Length - position) + 1);
            // Edit complete Unicode scalars rather than manufacturing malformed display expectations.
            var prior = before.GetText(0, before.Length);
            if (position > 0 && position < prior.Length && char.IsLowSurrogate(prior[position])) position--;
            removal = Math.Min(removal, before.Length - position);
            if (position + removal < prior.Length && removal > 0 && char.IsLowSurrogate(prior[position + removal])) removal++;
            var change = new TextChange(position, removal, insertions[random.Next(insertions.Length)]);
            var after = document.Apply(change);
            var source = after.GetText(0, after.Length);
            var oracle = new CsvPolicy().Analyze(source);
            var ordinal = oracle.Root.Children.Count == 0 ? 0 : random.Next(oracle.Root.Children.Count);
            var request = new CsvGridRequest([new TextSpan(0, Math.Min(source.Length, 8))],
                new CsvGridAnchor.Row(ordinal), 8, new GridRange(0, 8), AnalysisScope.Full);
            var grid = session.AnalyzeGrid(after, [new VersionedEdit(before.Version, after.Version, change)], request).Grid;
            AssertProjection(source, oracle, grid, ordinal, 8);
            Assert.Equal(oracle.Diagnostics.Count, grid.TotalDiagnosticCount);
        }
    }

    /// <summary>Invalid syntax still has exact decoded values, field origins and legacy diagnostic spans.</summary>
    [Theory]
    [InlineData("a\"b,c\n\"q\"suffix,z\n\"unterminated")]
    [InlineData("\"\"bad,\"\"\n\"x\"\"y\"oops,z")]
    public void Invalid_csv_preserves_oracle_diagnostics_and_field_error_flags(string source)
    {
        using var document = new Document(source);
        using var session = new CsvIncrementalSession();
        var oracle = new CsvPolicy().Analyze(source);
        var grid = Query(session, document, new CsvGridAnchor.Row(0));
        AssertProjection(source, oracle, grid, 0, 8);
        Assert.Equal(oracle.Diagnostics.Select(d => (d.Code, d.Severity, d.Span)),
            grid.Diagnostics.Select(d => (d.Code, d.Severity, d.Span)));
        foreach (var row in grid.Rows)
        foreach (var cell in row.Cells.Where(cell => cell.SourceRange is not null))
        {
            var origin = cell.SourceRange!.Value;
            var error = oracle.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error &&
                d.Span.Start >= origin.Start && d.Span.End <= origin.End);
            Assert.Equal(error, cell.HasSyntaxError);
        }
    }

    /// <summary>Constructs a bounded request independent of the grid's implementation helpers.</summary>
    private static GridRenderProjection Query(CsvIncrementalSession session, Document document, CsvGridAnchor anchor,
        AnalysisScope scope = AnalysisScope.Full, GridRange? columns = null) =>
        session.AnalyzeGrid(document.Snapshot, [], new CsvGridRequest([new TextSpan(0, Math.Min(document.Snapshot.Length, 32))],
            anchor, 8, columns ?? new GridRange(0, 8), scope)).Grid;

    /// <summary>Record delimiters belong to their preceding record; EOF to the last existing record.</summary>
    private static int ExpectedOwner(IReadOnlyList<SemanticNode> rows, int offset)
    {
        for (var i = 0; i + 1 < rows.Count; i++)
            if (offset < rows[i + 1].Span.Start) return i;
        return Math.Max(0, rows.Count - 1);
    }

    /// <summary>Compares exact grammar origins and explicitly specified display control substitution.</summary>
    private static void AssertProjection(string source, FormatAnalysis oracle, GridRenderProjection grid, int start, int limit)
    {
        Assert.Equal(AnalysisCompleteness.Complete, grid.Completeness);
        Assert.Equal(oracle.Root.Children.Count, grid.Extent.ExactRowCount);
        Assert.Equal(oracle.Root.Children.Count == 0 ? 0 : oracle.Root.Children.Max(row => row.Children.Count), grid.Extent.ExactMaxWidth);
        var expected = oracle.Root.Children.Skip(start).Take(limit).ToArray();
        Assert.Equal(expected.Length, grid.Rows.Count);
        for (var i = 0; i < expected.Length; i++)
        {
            var row = grid.Rows[i];
            Assert.Equal(start + i, row.Ordinal);
            Assert.Equal(expected[i].Span, row.SourceRange);
            var next = start + i + 1 < oracle.Root.Children.Count ? oracle.Root.Children[start + i + 1].Span.Start : source.Length;
            Assert.Equal(new TextSpan(expected[i].Span.End, next - expected[i].Span.End), row.RecordDelimiter);
            Assert.Equal(expected[i].Children.Count, row.Width);
            foreach (var cell in row.Cells)
            {
                if (cell.Column >= row.Width)
                {
                    Assert.Equal(GridValueState.Missing, cell.State);
                    Assert.Null(cell.SourceRange);
                    Assert.Equal(0, cell.DisplayRange.Length);
                    continue;
                }
                var original = expected[i].Children[cell.Column];
                Assert.Equal(original.Span, cell.SourceRange);
                Assert.Equal(GridValueState.Complete, cell.State);
                var value = original.Value!.Replace("\t", "⇥", StringComparison.Ordinal)
                    .Replace("\r", "␍", StringComparison.Ordinal).Replace("\n", "↵", StringComparison.Ordinal);
                Assert.Equal(value, grid.DisplayText.Substring(cell.DisplayRange.Start, cell.DisplayRange.Length));
            }
        }
    }
}
