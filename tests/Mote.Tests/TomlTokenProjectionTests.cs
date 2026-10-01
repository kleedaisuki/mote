using Mote.Engine;
using Mote.Formats;

namespace Mote.Tests;

/// <summary>Validated scalar/key intervals eliminate false lexical state inside cached owners.</summary>
public sealed class TomlTokenProjectionTests
{
    /// <summary>Valid trivia enters the production large-session path without changing scalar ownership.</summary>
    private static readonly string Padding = string.Concat(Enumerable.Repeat("#" + new string('x', 4094) + "\n", 1025));

    /// <summary>String content is never a comment/table/number merely because the viewport lacks opening quotes.</summary>
    [Theory]
    [InlineData("\"\"\"")]
    [InlineData("'''")]
    public void Multiline_string_interior_is_clipped_from_validated_owner(string quote)
    {
        var body = string.Concat(Enumerable.Repeat("# [fake] true 123 ", 5000));
        var source = "big=" + quote + body + "\n" + body + quote + "\n" + Padding;
        using var document = new Document(source);
        using var session = new TomlPolicy().CreateSession();
        var range = new TextSpan(50_000, 40);
        var cold = session.Analyze(document.Snapshot, [], new(range, AnalysisScope.Visible));
        Assert.Equal(AnalysisCompleteness.Provisional, cold.Completeness);
        var full = session.Analyze(document.Snapshot, [], new(range, AnalysisScope.Full));
        AssertString(full, range);
        var warm = session.Analyze(document.Snapshot, [], new(range, AnalysisScope.Visible));
        AssertString(warm, range);
        Assert.Equal(full.Tokens, warm.Tokens);
        var before = document.Snapshot;
        var change = new TextChange(range.Start, 1, "q");
        document.Apply(change);
        var edited = session.Analyze(document.Snapshot, [new(before.Version, document.Snapshot.Version, change)],
            new(range, AnalysisScope.Visible));
        AssertString(edited, range);
        Assert.Equal(document.Snapshot.Version, edited.Version);
        using var fresh = new TomlPolicy().CreateSession();
        var oracle = fresh.Analyze(document.Snapshot, [], new(range, AnalysisScope.Full));
        Assert.Equal(oracle.Tokens, edited.Tokens);
    }

    /// <summary>Resetting lexical gaps after the real scalar boundary preserves trailing comments and later scalar roles.</summary>
    [Fact]
    public void Scalar_end_comment_and_following_boolean_have_current_roles()
    {
        var source = "big=\"\"\"" + new string('x', 100_000) + "\n" + new string('y', 100_000) +
            "\"\"\" # after\nflag=true\n" + Padding;
        int end = source.IndexOf(" # after", StringComparison.Ordinal);
        using var document = new Document(source);
        using var session = new TomlPolicy().CreateSession();
        var analysis = session.Analyze(document.Snapshot, [], new(new TextSpan(end - 10, 35), AnalysisScope.Full));
        Assert.Equal(AnalysisCompleteness.Complete, analysis.Completeness);
        Assert.Contains(analysis.Tokens, token => token.Kind == "string" && token.Span.End == end);
        Assert.Contains(analysis.Tokens, token => token.Kind == "comment" && token.Span.Start == end + 1);
        Assert.Contains(analysis.Tokens, token => token.Kind == "key" && token.Span.Start == end + 9);
        Assert.Contains(analysis.Tokens, token => token.Kind == "boolean" && token.Span.Start == end + 14);
        AssertBounds(analysis, document.Snapshot.Length);
    }

    /// <summary>A viewport inside a long quoted name is a key, not a standalone string value.</summary>
    [Fact]
    public void Quoted_key_interior_uses_validated_key_role()
    {
        using var document = new Document("\"" + new string('k', 20_000) + "\"=1\n" + Padding);
        using var session = new TomlPolicy().CreateSession();
        var analysis = session.Analyze(document.Snapshot, [], new(new TextSpan(10_000, 10), AnalysisScope.Full));
        Assert.Equal(AnalysisCompleteness.Complete, analysis.Completeness);
        var token = Assert.Single(analysis.Tokens);
        Assert.Equal("key", token.Kind);
        Assert.True(token.Span.Start <= 10_000 && token.Span.End >= 10_010);
        AssertBounds(analysis, document.Snapshot.Length);
    }

    /// <summary>All top-level primitive categories come from validated spans, not independently restarted lexer state.</summary>
    [Theory]
    [InlineData("n=123\n", "number", 2, 3)]
    [InlineData("b=false\n", "boolean", 2, 5)]
    [InlineData("d=1979-05-27\n", "datetime", 2, 10)]
    [InlineData("s='text'\n", "string", 2, 6)]
    public void Primitive_roles_preserve_exact_source_spans(string prefix, string kind, int start, int length)
    {
        using var document = new Document(prefix + Padding);
        using var session = new TomlPolicy().CreateSession();
        var analysis = session.Analyze(document.Snapshot, [], new(new TextSpan(0, prefix.Length), AnalysisScope.Full));
        Assert.Contains(analysis.Tokens, token => token.Kind == kind && token.Span == new TextSpan(start, length));
        Assert.Contains(analysis.Tokens, token => token.Kind == "key" && token.Span == new TextSpan(0, 1));
        AssertBounds(analysis, document.Snapshot.Length);
    }

    /// <summary>Checks semantic validity separately from the exact clipped string classification.</summary>
    private static void AssertString(DocumentAnalysis analysis, TextSpan range)
    {
        Assert.Equal(AnalysisCompleteness.Complete, analysis.Completeness);
        Assert.Empty(analysis.Diagnostics);
        Assert.Equal(0, analysis.TotalDiagnosticCount);
        var token = Assert.Single(analysis.Tokens);
        Assert.Equal("string", token.Kind);
        Assert.True(token.Span.Start <= range.Start && token.Span.End >= range.End);
        AssertBounds(analysis, analysis.Root.Span.Length);
    }

    /// <summary>Every emitted projection coordinate belongs to the current immutable snapshot.</summary>
    private static void AssertBounds(DocumentAnalysis analysis, int length)
    {
        foreach (var token in analysis.Tokens)
        {
            Assert.InRange(token.Span.Start, 0, length);
            Assert.InRange(token.Span.Length, 0, length - token.Span.Start);
        }
    }
}
