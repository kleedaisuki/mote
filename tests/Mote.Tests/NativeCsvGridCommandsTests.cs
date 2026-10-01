using Mote.Engine;
using Mote.Formats;
using Mote.Native;

namespace Mote.Tests;

/// <summary>Pure command preparation preserves exact source and refuses unrepresentable payloads.</summary>
public sealed class NativeCsvGridCommandsTests
{
    /// <summary>Decoded data comes from source, even when the display is escaped or clipped.</summary>
    [Fact]
    public void Decoded_copy_preserves_multiline_tabs_quotes_and_formula_text()
    {
        using var document = new Document("\"a\r\nb\t\"\"c\",=1+2");
        var grid = Grid(document);
        var result = Copy(document, grid, NativeGridIntentKind.CopyValue);
        Assert.True(result.Success, result.Error);
        Assert.Equal("a\r\nb\t\"c", result.Payload);
        result = Copy(document, grid, NativeGridIntentKind.CopyCsv, endColumn: 1);
        Assert.Equal("\"a\r\nb\t\"\"c\",=1+2", result.Payload);
        result = Copy(document, grid, NativeGridIntentKind.CopyTsv, endColumn: 1);
        Assert.Equal("\"a\r\nb\t\"\"c\"\t=1+2", result.Payload);
        Assert.Equal("\"a\r\nb\t\"\"c\",=1+2", document.Snapshot.GetText());
    }

    /// <summary>Every empty selected record has an explicit token, including consecutive final empties.</summary>
    [Fact]
    public void Serialized_empty_rows_roundtrip_without_synthetic_trailing_record()
    {
        using var document = new Document("a\n\"\"\n\"\"");
        var grid = Grid(document);
        var result = Copy(document, grid, NativeGridIntentKind.CopyCsv, endRow: 2);
        Assert.Equal("a\r\n\"\"\r\n\"\"", result.Payload);
        using var copied = new Document(result.Payload!);
        var roundtrip = Grid(copied);
        Assert.Equal(3, roundtrip.Extent.ExactRowCount);
        Assert.All(roundtrip.Rows, row => Assert.Equal(1, row.Width));
    }

    /// <summary>Independent delimiter grammar checks combinations of empty, delimiter, quote, and multiline values.</summary>
    [Fact]
    public void Csv_and_quoted_tsv_roundtrip_all_small_value_pairs()
    {
        string[] values = ["", "plain", "a,b", "a\tb", "a\"b", "a\r\nb"];
        var expected = values.SelectMany(left => values.Select(right => new[] { left, right })).ToArray();
        var source = string.Join("\n", expected.Select(row => string.Join(",", row.Select(value =>
            "\"" + value.Replace("\"", "\"\"") + "\""))));
        using var document = new Document(source);
        var grid = Grid(document);
        foreach (var kind in new[] { NativeGridIntentKind.CopyCsv, NativeGridIntentKind.CopyTsv })
        {
            var result = Copy(document, grid, kind, endRow: expected.Length - 1, endColumn: 1);
            Assert.True(result.Success, result.Error);
            var actual = ParseRecords(result.Payload!, kind == NativeGridIntentKind.CopyTsv ? '\t' : ',');
            Assert.Equal(expected.Length, actual.Count);
            for (var i = 0; i < expected.Length; i++) Assert.Equal(expected[i], actual[i]);
        }
    }

    /// <summary>Whole rows preserve mixed original delimiters and raggedness; padded Copy is explicit.</summary>
    [Fact]
    public void Ragged_copy_requires_padding_but_source_rows_are_lossless()
    {
        using var document = new Document("a,b\r\nx\r");
        var grid = Grid(document);
        var ordinary = Copy(document, grid, NativeGridIntentKind.CopyCsv, endRow: 1, endColumn: 1);
        Assert.False(ordinary.Success);
        Assert.Null(ordinary.Payload);
        var padded = Copy(document, grid, NativeGridIntentKind.CopyCsvPadded, endRow: 1, endColumn: 1);
        Assert.Equal("a,b\r\nx,\"\"", padded.Payload);
        var rows = Copy(document, grid, NativeGridIntentKind.CopyRows, endRow: 1);
        Assert.Equal(document.Snapshot.GetText(), rows.Payload);
    }

    /// <summary>Native NUL admission applies uniformly to value, syntax, rectangle, and whole-row modes.</summary>
    [Theory]
    [InlineData((int)NativeGridIntentKind.CopyValue)]
    [InlineData((int)NativeGridIntentKind.CopySource)]
    [InlineData((int)NativeGridIntentKind.CopyCsv)]
    [InlineData((int)NativeGridIntentKind.CopyTsv)]
    [InlineData((int)NativeGridIntentKind.CopyCsvPadded)]
    [InlineData((int)NativeGridIntentKind.CopyRows)]
    public void Nul_is_refused_before_payload_publication(int kind)
    {
        using var document = new Document("\"a\0b\"");
        var result = Copy(document, Grid(document), (NativeGridIntentKind)kind);
        Assert.False(result.Success);
        Assert.Contains("NUL", result.Error);
        Assert.Null(result.Payload);
        Assert.Null(result.Change);
    }

    /// <summary>Bad grammar is copied only as syntax, never represented as successfully decoded data.</summary>
    [Fact]
    public void Syntax_errors_allow_source_copy_only()
    {
        using var document = new Document("a\"b,c");
        var grid = Grid(document);
        Assert.False(Copy(document, grid, NativeGridIntentKind.CopyValue).Success);
        Assert.Equal("a\"b", Copy(document, grid, NativeGridIntentKind.CopySource).Payload);
        Assert.False(NativeCsvGridCommands.PrepareReplace(document.Snapshot, grid,
            Intent(document, NativeGridIntentKind.Replace), "x").Success);
    }

    /// <summary>Actual column coordinates remain authoritative after a horizontal window rebase.</summary>
    [Fact]
    public void Sparse_columns_and_clipped_values_use_exact_origins()
    {
        var longValue = new string('x', 2000);
        using var document = new Document("ignored,second," + longValue);
        var grid = Grid(document, 1, 2);
        var result = Copy(document, grid, NativeGridIntentKind.CopyCsv, column: 1, endColumn: 2);
        Assert.Equal("second," + longValue, result.Payload);
        Assert.False(Copy(document, grid, NativeGridIntentKind.CopyValue, column: 0).Success);
    }

    /// <summary>An oversized origin is refused before a giant materialization or claimed NUL validation.</summary>
    [Fact]
    public void Giant_copy_refuses_before_allocating_source_payload()
    {
        using var document = new Document(new string('x', NativeCsvGridCommands.MaxPayloadLength + 1));
        var grid = Grid(document);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = Copy(document, grid, NativeGridIntentKind.CopyValue);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.False(result.Success);
        Assert.Contains("NUL validation was not performed", result.Error);
        Assert.InRange(allocated, 0, 16 * 1024);
        Assert.False(Copy(document, grid, NativeGridIntentKind.CopySource).Success);
    }

    /// <summary>The cap is inclusive for raw output; serialization adds no hidden clipping.</summary>
    [Fact]
    public void Exact_output_limit_is_admitted()
    {
        using var document = new Document(new string('x', NativeCsvGridCommands.MaxPayloadLength));
        var grid = Grid(document);
        var result = Copy(document, grid, NativeGridIntentKind.CopyValue);
        Assert.True(result.Success, result.Error);
        Assert.Equal(NativeCsvGridCommands.MaxPayloadLength, result.Payload!.Length);
        Assert.Equal(result.Payload, Copy(document, grid, NativeGridIntentKind.CopyCsv).Payload);
    }

    /// <summary>Escaping may exceed the output limit even when decoded values individually fit.</summary>
    [Fact]
    public void Serialization_checks_expanded_size_without_clipping()
    {
        var source = "\"" + new string('"', NativeCsvGridCommands.MaxPayloadLength) + "\"";
        using var document = new Document(source);
        var grid = Grid(document);
        Assert.True(Copy(document, grid, NativeGridIntentKind.CopyValue).Success);
        Assert.False(Copy(document, grid, NativeGridIntentKind.CopyCsv).Success);
    }

    /// <summary>The result is one whole-field edit retaining all surrounding source syntax.</summary>
    [Theory]
    [InlineData("a,\"b\",c\r\n", "x", "a,\"x\",c\r\n")]
    [InlineData("a,b,c\r\n", "x,y\n\"z", "a,\"x,y\n\"\"z\",c\r\n")]
    [InlineData("a,b,c\r\n", "", "a,\"\",c\r\n")]
    public void Replacement_preserves_style_and_surrounding_bytes(string source, string value, string expected)
    {
        using var document = new Document(source);
        var result = NativeCsvGridCommands.PrepareReplace(document.Snapshot, Grid(document),
            Intent(document, NativeGridIntentKind.Replace, column: 1), value);
        Assert.True(result.Success, result.Error);
        Assert.Null(result.Payload);
        Assert.Equal(source, document.Snapshot.GetText());
        document.Apply(result.Change!.Value);
        Assert.Equal(expected, document.Snapshot.GetText());
        Assert.True(document.Undo());
        Assert.Equal(source, document.Snapshot.GetText());
    }

    /// <summary>Unavailable origins, obsolete versions, and cancellation cannot produce a command.</summary>
    [Fact]
    public void Stale_and_cancelled_commands_do_not_prepare_payloads()
    {
        using var document = new Document("a,b");
        var grid = Grid(document);
        document.Apply(new TextChange(0, 1, "x"));
        Assert.False(Copy(document, grid, NativeGridIntentKind.CopyValue).Success);
        grid = Grid(document);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => NativeCsvGridCommands.PrepareCopy(document.Snapshot, grid,
            Intent(document, NativeGridIntentKind.CopyValue), cancellation.Token));
        Assert.False(NativeCsvGridCommands.PrepareReplace(document.Snapshot, grid,
            Intent(document, NativeGridIntentKind.Replace), "\ud800").Success);
    }

    /// <summary>Uses the real CSV grammar as a source ownership oracle, not an invented display fixture.</summary>
    private static GridRenderProjection Grid(Document document, int firstColumn = 0, int columns = 3)
    {
        using var session = (CsvIncrementalSession)new CsvPolicy().CreateSession();
        return session.AnalyzeGrid(document.Snapshot, [], new CsvGridRequest([new TextSpan(0, Math.Min(document.Snapshot.Length, 100))],
            new CsvGridAnchor.Row(0), 64, new GridRange(firstColumn, columns), AnalysisScope.Full)).Grid;
    }

    /// <summary>Creates an admitted-version selection; controller tests separately cover presentation nonces.</summary>
    private static NativeGridIntent Intent(Document document, NativeGridIntentKind kind, int column = 0,
        int? endRow = null, int? endColumn = null) => new(new(new(1, document.Snapshot.Version), 1), kind, 0, column, endRow, endColumn);

    /// <summary>Invokes pure preparation without touching a platform adapter.</summary>
    private static NativeCsvGridCommandResult Copy(Document document, GridRenderProjection grid, NativeGridIntentKind kind,
        int column = 0, int? endRow = null, int? endColumn = null) =>
        NativeCsvGridCommands.PrepareCopy(document.Snapshot, grid, Intent(document, kind, column, endRow, endColumn));

    /// <summary>Small independent test oracle for the declared quoted delimiter grammar.</summary>
    private static List<string[]> ParseRecords(string text, char delimiter)
    {
        var result = new List<string[]>();
        var row = new List<string>();
        var field = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (!quoted && ch == delimiter) { row.Add(field.ToString()); field.Clear(); }
            else if (!quoted && ch == '\r')
            {
                Assert.True(i + 1 < text.Length && text[++i] == '\n');
                row.Add(field.ToString()); field.Clear(); result.Add(row.ToArray()); row.Clear();
            }
            else field.Append(ch);
        }
        Assert.False(quoted);
        row.Add(field.ToString()); result.Add(row.ToArray());
        return result;
    }
}
