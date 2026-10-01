using System.Text;
using Mote.Formats;
using Tomlyn.Parsing;

namespace Mote.Tests;

/// <summary>Logical seam coverage independent of namespace ownership and snapshot integration.</summary>
public sealed class TomlStatementBoundaryTests
{
    /// <summary>Exercises delimiter runs, escaped content, comments and TOML 1.1 collections.</summary>
    public static IEnumerable<object[]> Statements()
    {
        yield return [new[] { "x = [\n  \"\"\"value\"\"\"\",\n]\n", "next = 1\n" }];
        yield return [new[] { "x = [\n  '''value''''',\n]\n", "next = 1" }];
        yield return [new[] { "x = \"\"\"\"leading\ntrailing\"\"\"\"\"\n", "next = 1\n" }];
        yield return [new[] { "x = '''''leading\ntrailing''''\n", "next = 1\n" }];
        yield return [new[] { "x = \"\"\"escaped \\\"\"\" content\\\n  continued\n\"\"\"\n", "next = 1\n" }];
        yield return [new[] { "x = [\r\n  \"# ] }\", # ignored ] } '''\r\n  {\r\n    a = 1,\r\n  },\r\n]\r\n", "next = 1\r\n" }];
        yield return [new[] { "x = \"backslash \\\\ and escaped \\\"\" # [\n", "next = 'literal # [ ]'\n" }];
        yield return [new[] { "x = \"\"\"\"\"\"\n", "y = ''''''\n", "next = 1" }];
    }

    /// <summary>Each expected unit is parser-valid and emitted with its exact original text.</summary>
    [Theory]
    [MemberData(nameof(Statements))]
    public void Valid_logical_units_preserve_exact_seams(string[] expected)
    {
        foreach (string statement in expected)
            Assert.Empty(SyntaxParser.Parse(statement, validate: true).Diagnostics);
        Assert.Equal(expected, Split(string.Concat(expected)));
    }

    /// <summary>Filters the normative corpus to valid sources; invalidity belongs to the parser.</summary>
    public static IEnumerable<object[]> PublishedSources()
    {
        foreach (object[] fixture in TomlConformanceTests.Cases())
            if ((bool)fixture[1]) yield return [fixture[0], fixture[2]];
    }

    /// <summary>Every valid published source splits into independently valid, single-action units.</summary>
    [Theory]
    [MemberData(nameof(PublishedSources))]
    public void Published_valid_sources_have_parser_valid_seams(string name, string encoded)
    {
        string source = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(encoded));
        var statements = Split(source);
        Assert.Equal(source, string.Concat(statements));
        foreach (string statement in statements)
        {
            var syntax = SyntaxParser.Parse(statement, validate: true);
            Assert.True(syntax.Diagnostics.Count == 0, name + ": " + string.Join("; ", syntax.Diagnostics));
            Assert.InRange(syntax.KeyValues.Count() + syntax.Tables.Count(), 0, 1);
        }
    }

    /// <summary>A seam detector never turns an invalid single-line string into accepted syntax.</summary>
    [Theory]
    [InlineData("x = \"unterminated\n")]
    [InlineData("x = 'unterminated\n")]
    [InlineData("x = \"unterminated\\\n")]
    [InlineData("x = \"unterminated")]
    public void Malformed_plain_string_does_not_require_continuation(string source)
    {
        var scanner = new TomlStatementBoundary();
        Assert.False(scanner.Continues(new StringBuilder(source), 0));
        Assert.NotEmpty(SyntaxParser.Parse(source, validate: true).Diagnostics);
    }

    /// <summary>Incomplete multiline strings and collections keep their state until EOF refusal.</summary>
    [Theory]
    [InlineData("x = \"\"\"unfinished\n")]
    [InlineData("x = '''unfinished\n")]
    [InlineData("x = [\n  1,\n")]
    [InlineData("x = {\n a = 1,\n")]
    public void Incomplete_logical_unit_requires_more_input(string source)
    {
        var scanner = new TomlStatementBoundary();
        Assert.True(scanner.Continues(new StringBuilder(source), 0));
        Assert.NotEmpty(SyntaxParser.Parse(source, validate: true).Diagnostics);
    }

    /// <summary>Scanning a large segmented builder allocates no accumulated-source copies.</summary>
    [Fact]
    public void Appended_line_scans_do_not_allocate_source_copies()
    {
        string line = new('x', 4096);
        var builder = new StringBuilder(200_000);
        builder.Append("x = \"\"\"\n").Append(line).Append('\n').Append("\"\"\"\n");
        // Warm the same code path before measuring managed allocations, not latency.
        var warm = new TomlStatementBoundary();
        warm.Continues(builder, 0);
        builder.Clear();
        long before = GC.GetAllocatedBytesForCurrentThread();
        var scanner = new TomlStatementBoundary();
        builder.Append("x = \"\"\"\n");
        scanner.Continues(builder, 0);
        for (int i = 0; i < 46; i++)
        {
            int start = builder.Length;
            builder.Append(line).Append('\n');
            scanner.Continues(builder, start);
        }
        int closingStart = builder.Length;
        builder.Append("\"\"\"\n");
        bool continues = scanner.Continues(builder, closingStart);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.False(continues);
        Assert.Equal(0, allocated);
    }

    /// <summary>Feeds full physical lines, resetting state only after an observed top-level seam.</summary>
    private static string[] Split(string source)
    {
        var result = new List<string>();
        var statement = new StringBuilder();
        var scanner = new TomlStatementBoundary();
        int lineStart = 0;
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] != '\n' && i != source.Length - 1) continue;
            int start = statement.Length;
            statement.Append(source, lineStart, i + 1 - lineStart);
            lineStart = i + 1;
            if (scanner.Continues(statement, start)) continue;
            result.Add(statement.ToString());
            statement.Clear();
            scanner = default;
        }
        Assert.Equal(0, statement.Length);
        return result.ToArray();
    }
}

