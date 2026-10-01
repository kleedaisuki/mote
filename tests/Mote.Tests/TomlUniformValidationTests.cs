using System.Text;
using Mote.Formats;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace Mote.Tests;

/// <summary>Normative public-policy validity and conservative formatting across unbounded small-file validation.</summary>
public sealed class TomlUniformValidationTests
{
    /// <summary>Reuses exact pinned upstream bytes, never a reconstructed parser-generated corpus.</summary>
    public static IEnumerable<object[]> Cases() => TomlConformanceTests.Cases();

    /// <summary>Classifies every decoded fixture and checks formatting does not rewrite invalid documents.</summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void Public_policy_classifies_published_corpus_and_preserves_format_semantics(string name, bool valid, string encoded)
    {
        string source;
        try { source = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(encoded)); }
        catch (DecoderFallbackException)
        {
            Assert.False(valid, name);
            return; // Encoding is not a policy grammar rejection; counted separately below.
        }
        var policy = new TomlPolicy();
        var analysis = policy.Analyze(source);
        AssertAnchored(source, analysis);
        Assert.Equal(valid, !analysis.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));
        string formatted = policy.Format(source);
        if (!valid)
        {
            Assert.Equal(source, formatted);
            return;
        }
        var reparsed = policy.Analyze(formatted);
        AssertAnchored(formatted, reparsed);
        Assert.Empty(reparsed.Diagnostics);
        Assert.Equal(formatted, policy.Format(formatted));
        EqualSemanticValueTree(analysis.Root, reparsed.Root);
        Assert.Equal(Comments(analysis), Comments(reparsed));
    }

    /// <summary>Records separate finite corpus populations rather than crediting decoding failures to the parser.</summary>
    [Fact]
    public void Corpus_population_has_218_valid_485_decodable_invalid_and_9_encoding_boundaries()
    {
        int valid = 0, invalid = 0, encoding = 0;
        foreach (object[] row in Cases())
        {
            try { _ = new UTF8Encoding(false, true).GetString(Convert.FromBase64String((string)row[2])); }
            catch (DecoderFallbackException) { Assert.False((bool)row[1]); encoding++; continue; }
            if ((bool)row[1]) valid++; else invalid++;
        }
        Assert.Equal(218, valid);
        Assert.Equal(485, invalid);
        Assert.Equal(9, encoding);
    }

    /// <summary>These two independently minimized latest-array-element cases were rejected by the old public policy.</summary>
    [Theory]
    [InlineData("q=[1,2]\n[[a]]\n[[a]]\n[[a.b]]\n[[a]]\nx={a=1}\na=1\nb=2\n")]
    [InlineData("[[a]]\n[[a]]\ns=\"\"\"x\ny\"\"\"\n[a.b]\n[[a]]\nb=2\na.b=3\n[[a]]\ns=\"\"\"x\ny\"\"\"\n[a.b]\n")]
    public void Historical_array_reentry_minimals_validate_and_format_without_semantic_change(string source)
    {
        var policy = new TomlPolicy();
        var before = policy.Analyze(source);
        Assert.Empty(before.Diagnostics);
        string formatted = policy.Format(source);
        Assert.NotEqual(source, formatted); // Assignment gaps should actually be normalized.
        var after = policy.Analyze(formatted);
        Assert.Empty(after.Diagnostics);
        Assert.Equal(formatted, policy.Format(formatted));
        EqualSemanticValueTree(before.Root, after.Root);
    }

    /// <summary>Legacy semantic diagnostics retain their existing ID and absolute UTF-16 source anchoring.</summary>
    [Theory]
    [InlineData("# 😀\r\na=1\r\na=2\r\n", "a=2", 1)]
    [InlineData("a=1\n\"\\u0061\"=2\n", "\"\\u0061\"", 8)]
    [InlineData("[a]\nx=1\n[a]\n", "[a]\n", 1)]
    [InlineData("a={x=1}\na.y=2\n", "a.y", 3)]
    public void Ownership_diagnostics_keep_public_id_and_exact_current_key_span(string source, string marker, int length)
    {
        var policy = new TomlPolicy();
        var error = Assert.Single(policy.Analyze(source).Diagnostics);
        int start = source.LastIndexOf(marker, StringComparison.Ordinal);
        if (marker.StartsWith('[')) start++;
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal("TOML_PARSE", error.Code);
        Assert.Equal(new TextSpan(start, length), error.Span);
        Assert.Equal(source, policy.Format(source));
    }

    /// <summary>Local statement semantics must still reject internal duplicates and forbidden trivia.</summary>
    [Theory]
    [InlineData("a={x=1,x=2}\n")]
    [InlineData("a=1\n# invalid\r")]
    [InlineData("\f")]
    [InlineData("\v")]
    public void Local_semantic_or_trivia_errors_are_not_lost(string source)
    {
        var policy = new TomlPolicy();
        Assert.Contains(policy.Analyze(source).Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Code == "TOML_PARSE");
        Assert.Equal(source, policy.Format(source));
    }

    /// <summary>Integer nodes preserve exact text, including optional values beyond signed 64-bit range.</summary>
    [Theory]
    [InlineData("-9223372036854775808")]
    [InlineData("9223372036854775807")]
    [InlineData("9223372036854775808")]
    public void Integer_values_are_preserved_losslessly(string value)
    {
        string source = "a=" + value + "\n";
        var policy = new TomlPolicy();
        var before = policy.Analyze(source);
        Assert.Empty(before.Diagnostics);
        var number = Assert.Single(Assert.Single(before.Root.Children).Children);
        Assert.Equal("number", number.Kind);
        Assert.Equal(value, number.Value);
        var after = policy.Analyze(policy.Format(source));
        Assert.Empty(after.Diagnostics);
        EqualSemanticValueTree(before.Root, after.Root);
    }

    /// <summary>Formatting preserves original comments, line endings and Unicode strings while normalizing assignment gaps.</summary>
    [Fact]
    public void Tabs_crlf_unicode_and_comment_contents_survive_formatting()
    {
        const string source = "# 😀 comment\r\n\"雪\"\t=\t\"火花✨\" # tail\r\na\t=\t{x\t=\t1}\r\n";
        var policy = new TomlPolicy();
        var before = policy.Analyze(source);
        Assert.Empty(before.Diagnostics);
        string formatted = policy.Format(source);
        Assert.Contains("\"雪\" = \"火花✨\" # tail\r\n", formatted);
        Assert.Equal(source.Count(ch => ch == '\r'), formatted.Count(ch => ch == '\r'));
        Assert.Equal(source.Count(ch => ch == '\n'), formatted.Count(ch => ch == '\n'));
        var after = policy.Analyze(formatted);
        Assert.Empty(after.Diagnostics);
        EqualSemanticValueTree(before.Root, after.Root);
        Assert.Equal(Comments(before), Comments(after));
    }

    /// <summary>Small public analysis is not silently limited by the large-cache resource budgets.</summary>
    [Theory]
    [InlineData("length")]
    [InlineData("lines")]
    [InlineData("statements")]
    [InlineData("bindings")]
    public void Public_policy_accepts_valid_sources_beyond_each_cache_budget(string boundary)
    {
        string source = boundary switch
        {
            "length" => "a=\"" + new string('x', 256 * 1024 + 1) + "\"\n",
            "lines" => "a=[\n" + string.Concat(Enumerable.Repeat("1,\n", 65)) + "]\n",
            "statements" => string.Concat(Enumerable.Repeat("#\n", 120_001)) + "a=1\n",
            "bindings" => ManyBindings(),
            _ => throw new ArgumentOutOfRangeException(nameof(boundary))
        };
        Assert.True(source.Length < 4 * 1024 * 1024);
        var analysis = new TomlPolicy().Analyze(source);
        Assert.Empty(analysis.Diagnostics);
        Assert.Equal(source, analysis.SourceText);
    }

    /// <summary>A cancellation requested before grammar work is honored without producing partial semantic output.</summary>
    [Fact]
    public void Precancelled_analysis_throws()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new TomlPolicy().Analyze("a=1\n", cancellation.Token));
    }

    /// <summary>Independent table scopes each retain their later duplicate witness.</summary>
    [Fact]
    public void Independent_duplicate_witnesses_survive_recovery()
    {
        const string source = "[a]\nx=1\nx=2\n[b]\ny=1\ny=2\n";
        AssertExactOwnershipErrors(source,
            new(source.IndexOf("x=2", StringComparison.Ordinal), 1),
            new(source.IndexOf("y=2", StringComparison.Ordinal), 1));
    }

    /// <summary>An invalid header cannot rebind assignments to the previous valid table.</summary>
    [Fact]
    public void Conflicting_header_quarantines_scope_until_next_valid_header()
    {
        const string source = "[a]\nx=1\n[a]\nx=2\n[b]\nx=1\nx=2\n";
        AssertExactOwnershipErrors(source,
            new(source.LastIndexOf("[a]", StringComparison.Ordinal) + 1, 1),
            new(source.LastIndexOf("x=2", StringComparison.Ordinal), 1));
    }

    /// <summary>A failed dotted traversal must roll back a previously implicit parent's origin.</summary>
    [Fact]
    public void Failed_assignment_rolls_back_implicit_origin_before_later_header()
    {
        const string source = "[a.x.y]\n[a]\nx.y.z=1\n[a.x]\nw=1\nw=2\n";
        AssertExactOwnershipErrors(source,
            new(source.IndexOf("x.y.z", StringComparison.Ordinal), 5),
            new(source.IndexOf("w=2", StringComparison.Ordinal), 1));
    }

    /// <summary>A malformed one-line header leaves assignment scope unknown but permits valid-header recovery.</summary>
    [Fact]
    public void Malformed_header_recovers_without_previous_scope_false_positive()
    {
        const string source = "[a]\nx=1\n[broken\nx=2\n[b]\ny=1\ny=2\n";
        var policy = new TomlPolicy();
        var result = policy.Analyze(source);
        var grammarWitnesses = GrammarDiagnostics(source);
        Assert.Contains(result.Diagnostics,
            diagnostic => diagnostic.Span == new TextSpan(source.IndexOf("y=2", StringComparison.Ordinal), 1));
        // The whole grammar parser legitimately reports an unexpected x token here.
        // Only an additional namespace witness at that key would be a false scope claim.
        Assert.DoesNotContain(result.Diagnostics.Except(grammarWitnesses),
            diagnostic => diagnostic.Span == new TextSpan(source.IndexOf("x=2", StringComparison.Ordinal), 1));
        Assert.DoesNotContain(result.Diagnostics,
            diagnostic => diagnostic.Span == new TextSpan(source.IndexOf("[b]", StringComparison.Ordinal) + 1, 1));
        AssertWholeGrammarWitnessesPreserved(source, result);
        Assert.Equal(source, policy.Format(source));
    }

    /// <summary>Illegal leading trivia does not conceal an invalid header and revive the prior root scope.</summary>
    [Theory]
    [InlineData("\f")]
    [InlineData("\v")]
    [InlineData("\u00a0")]
    [InlineData("\0")]
    public void Invalid_header_trivia_quarantines_previous_scope(string trivia)
    {
        string source = "a=1\n" + trivia + "[b]\na=2\n[c]\nz=1\nz=2\n";
        var policy = new TomlPolicy();
        var result = policy.Analyze(source);
        AssertAnchored(source, result);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Span.Start < source.IndexOf("a=2", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics,
            diagnostic => diagnostic.Span == new TextSpan(source.IndexOf("z=2", StringComparison.Ordinal), 1));
        Assert.DoesNotContain(result.Diagnostics.Except(GrammarDiagnostics(source)),
            diagnostic => diagnostic.Span == new TextSpan(source.IndexOf("a=2", StringComparison.Ordinal), 1));
        Assert.Equal(source, policy.Format(source));
    }

    /// <summary>Local inline ownership errors do not suppress a later independent global duplicate.</summary>
    [Fact]
    public void Local_inline_error_and_later_namespace_error_are_both_retained()
    {
        const string source = "bad={x=1,x=2}\n[b]\ny=1\ny=2\n";
        var policy = new TomlPolicy();
        var result = policy.Analyze(source);
        AssertAnchored(source, result);
        Assert.Equal(2, result.Diagnostics.Count);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Span.Start < source.IndexOf('[', StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics,
            diagnostic => diagnostic.Span == new TextSpan(source.IndexOf("y=2", StringComparison.Ordinal), 1));
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal("TOML_PARSE", diagnostic.Code));
        Assert.Equal(source, policy.Format(source));
    }

    /// <summary>An unrecoverable multiline owner cannot erase independent whole-parser grammar evidence.</summary>
    [Theory]
    [InlineData("a=\"\"\"unfinished\n[b]\nx=1\nx=2\n")]
    [InlineData("a=[1,\n[b]\nx=1\nx=2\n")]
    [InlineData("a=???\n[b]\nx=1\nx=2\n")]
    public void Malformed_value_preserves_whole_grammar_witnesses(string source)
    {
        var policy = new TomlPolicy();
        var result = policy.Analyze(source);
        AssertAnchored(source, result);
        AssertWholeGrammarWitnessesPreserved(source, result);
        Assert.Equal(source, policy.Format(source));
    }

    /// <summary>Literal key spans establish the complete expected ownership-error set for grammar-valid examples.</summary>
    private static void AssertExactOwnershipErrors(string source, params TextSpan[] spans)
    {
        var policy = new TomlPolicy();
        var result = policy.Analyze(source);
        Assert.Equal(spans.OrderBy(span => span.Start), result.Diagnostics.Select(error => error.Span).OrderBy(span => span.Start));
        Assert.All(result.Diagnostics, error =>
        {
            Assert.Equal("TOML_PARSE", error.Code);
            Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        });
        Assert.Equal(source, policy.Format(source));
    }

    /// <summary>The unvalidated lossless parser is an independent reference only for preserved grammar diagnostics.</summary>
    private static void AssertWholeGrammarWitnessesPreserved(string source, FormatAnalysis analysis)
    {
        AssertAnchored(source, analysis);
        var grammar = GrammarDiagnostics(source);
        Assert.NotEmpty(grammar);
        foreach (var expected in grammar) Assert.Contains(expected, analysis.Diagnostics);
        Assert.Equal(analysis.Diagnostics.Count, analysis.Diagnostics.Distinct().Count());
    }

    /// <summary>Converts only independent whole-parser grammar evidence, preserving exact message identity.</summary>
    private static Diagnostic[] GrammarDiagnostics(string source) => SyntaxParser.Parse(source, validate: false).Diagnostics
        .Select(witness =>
        {
            int start = Math.Clamp(witness.Span.Offset, 0, source.Length);
            return new Diagnostic(witness.Kind == DiagnosticMessageKind.Error ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                "TOML_PARSE", witness.Message, new(start, Math.Clamp(witness.Span.Length, 0, source.Length - start)));
        }).ToArray();

    /// <summary>Recovery and EOF may not expose spans outside the current UTF-16 source.</summary>
    private static void AssertAnchored(string source, FormatAnalysis analysis)
    {
        foreach (TextSpan span in analysis.Diagnostics.Select(error => error.Span).Concat(analysis.Tokens.Select(token => token.Span)))
        {
            Assert.InRange(span.Start, 0, source.Length);
            Assert.InRange(span.Length, 0, source.Length - span.Start);
        }
    }

    /// <summary>67,000 independent three-component paths require 201,000 namespace bindings.</summary>
    private static string ManyBindings()
    {
        var source = new StringBuilder();
        for (int i = 0; i < 67_000; i++) source.Append('k').Append(i).Append(".a.b=1\n");
        return source.ToString();
    }

    /// <summary>Comment text is recovered from source anchors, independent of formatter output expectations.</summary>
    private static string[] Comments(FormatAnalysis analysis) => analysis.Tokens
        .Where(token => token.Kind == "comment")
        .Select(token => analysis.SourceText.Substring(token.Span.Start, token.Span.Length)).ToArray();

    /// <summary>Compare semantic contents recursively while deliberately permitting formatting-induced span changes.</summary>
    private static void EqualSemanticValueTree(SemanticNode expected, SemanticNode actual)
    {
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Value, actual.Value);
        Assert.Equal(expected.Children.Count, actual.Children.Count);
        for (int i = 0; i < expected.Children.Count; i++)
            EqualSemanticValueTree(expected.Children[i], actual.Children[i]);
    }
}
