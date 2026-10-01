using Mote.Engine;
using Mote.Formats;

namespace Mote.Tests;

/// <summary>Separates a completed error scan from an unavailable proof of YAML key uniqueness.</summary>
public sealed class YamlSmallRecoveryCertificateTests
{
    /// <summary>Semantic errors in keys prevent Complete, without losing the original error.</summary>
    [Theory]
    [InlineData("? *missing\n: one\n", "yaml.undefined-alias")]
    [InlineData("? [*missing]\n: one\n", "yaml.undefined-alias")]
    [InlineData("? {a: *missing}\n: one\n", "yaml.undefined-alias")]
    [InlineData("? !!int nope\n: one\n", "yaml.invalid-tagged-scalar")]
    [InlineData("? !!str [x]\n: one\n", "yaml.invalid-tagged-collection")]
    [InlineData("? [!!int nope]\n: one\n", "yaml.invalid-tagged-scalar")]
    [InlineData("? {a: !!int nope}\n: one\n", "yaml.invalid-tagged-scalar")]
    public void Erroneous_key_is_explicitly_unverified(string source, string errorCode)
    {
        var result = new YamlPolicy().Analyze(source);
        Assert.Single(result.Diagnostics, d => d.Code == errorCode && d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(result.Diagnostics, d => d.Code == "yaml.key-equality-unsupported" && d.Severity == DiagnosticSeverity.Warning);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "yaml.duplicate-key");
        var actual = AnalyzeSession(source);
        Assert.Equal(AnalysisCompleteness.Provisional, actual.Completeness);
        Assert.Null(actual.TotalDiagnosticCount);
        Assert.Equal(result.Diagnostics, actual.Diagnostics);
        AssertExactAliasError(source, result);
    }

    /// <summary>An ordinary erroneous value does not by itself make all mapping keys undecidable.</summary>
    [Theory]
    [InlineData("value: *missing\n", "yaml.undefined-alias")]
    [InlineData("value: !!int nope\n", "yaml.invalid-tagged-scalar")]
    [InlineData("value: !!str [x]\n", "yaml.invalid-tagged-collection")]
    public void Ordinary_value_error_remains_a_complete_error_scan(string source, string errorCode)
    {
        var actual = AnalyzeSession(source);
        Assert.Equal(AnalysisCompleteness.Complete, actual.Completeness);
        Assert.Equal(1, actual.TotalDiagnosticCount);
        Assert.Single(actual.Diagnostics, d => d.Code == errorCode && d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(actual.Diagnostics, d => d.Code == "yaml.key-equality-unsupported");
        AssertExactAliasError(source, new YamlPolicy().Analyze(source));
    }

    /// <summary>A later alias key must inherit uncertainty from its bound erroneous value graph.</summary>
    [Theory]
    [InlineData("value: &a [*missing]\n? *a\n: one\n? *a\n: two\n", "yaml.undefined-alias")]
    [InlineData("value: &a [!!int nope]\n? *a\n: one\n? *a\n: two\n", "yaml.invalid-tagged-scalar")]
    public void Aliased_erroneous_anchor_does_not_guess_duplicate(string source, string errorCode)
    {
        var result = new YamlPolicy().Analyze(source);
        Assert.Single(result.Diagnostics, d => d.Code == errorCode);
        Assert.Equal(2, result.Diagnostics.Count(d => d.Code == "yaml.key-equality-unsupported"));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "yaml.duplicate-key");
        Assert.Equal(AnalysisCompleteness.Provisional, AnalyzeSession(source).Completeness);
    }

    /// <summary>Event parsing continues across semantic errors and reports independent later failures.</summary>
    [Theory]
    [InlineData("? *missing\n: one\n", "yaml.undefined-alias", AnalysisCompleteness.Provisional)]
    [InlineData("value: !!int nope\n", "yaml.invalid-tagged-scalar", AnalysisCompleteness.Complete)]
    public void Later_errors_survive_recovery(string prefix, string firstCode, AnalysisCompleteness completeness)
    {
        var source = prefix + "later: one\nlater: two\nother: *also\n";
        var result = new YamlPolicy().Analyze(source);
        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Equal(new[] { firstCode, "yaml.undefined-alias", "yaml.duplicate-key" }, errors.Select(d => d.Code));
        Assert.Equal(source.LastIndexOf("later", StringComparison.Ordinal), errors[2].Span.Start);
        Assert.Equal(new TextSpan(source.IndexOf("*also", StringComparison.Ordinal), 5), errors[1].Span);
        Assert.Equal("Alias '*also' has no preceding anchor.", errors[1].Message);
        var session = AnalyzeSession(source);
        Assert.Equal(completeness, session.Completeness);
        Assert.Equal(result.Diagnostics, session.Diagnostics);
        Assert.Equal(completeness == AnalysisCompleteness.Complete ? result.Diagnostics.Count : (int?)null,
            session.TotalDiagnosticCount);
    }

    /// <summary>Syntax termination is not semantic recovery and cannot certify unseen later text.</summary>
    [Fact]
    public void Malformed_syntax_stops_with_provisional_certificate()
    {
        const string source = "? [unclosed\n: one\nlater: one\nlater: two\nother: *also\n";
        var actual = AnalyzeSession(source);
        Assert.Equal(AnalysisCompleteness.Provisional, actual.Completeness);
        Assert.Null(actual.TotalDiagnosticCount);
        Assert.Single(actual.Diagnostics, d => d.Code == "yaml.syntax");
        Assert.DoesNotContain(actual.Diagnostics, d => d.Code is "yaml.duplicate-key" or "yaml.undefined-alias");
    }

    /// <summary>One threshold-crossing alias case compares certificates without comparing differing messages.</summary>
    [Fact]
    public void Small_and_streamed_alias_key_both_disclose_uncertainty()
    {
        const string tail = "? [*missing]\n: one\nlater: one\nlater: two\n";
        var small = AnalyzeSession(tail);
        var padded = AnalyzeSession("# " + new string('p', 257 * 1024) + "\n" + tail);
        Assert.Equal(AnalysisCompleteness.Provisional, small.Completeness);
        Assert.Equal(small.Completeness, padded.Completeness);
        Assert.Null(padded.TotalDiagnosticCount);
        Assert.Contains(small.Diagnostics, d => d.Code == "yaml.duplicate-key");
        // Existing stream uncertainty stops later key comparisons; do not imply identical recovery.
        Assert.DoesNotContain(padded.Diagnostics, d => d.Code == "yaml.duplicate-key");
        foreach (var code in new[] { "yaml.undefined-alias", "yaml.key-equality-unsupported" })
            Assert.Contains(padded.Diagnostics, d => d.Code == code);
    }

    /// <summary>Viewport filtering never turns an offscreen undecidable key into a complete proof.</summary>
    [Fact]
    public void Offscreen_bad_key_keeps_provisional_visible_certificate()
    {
        const string source = "visible: yes\n? *missing\n: one\n";
        using var document = new Document(source);
        using var session = new YamlPolicy().CreateSession();
        var actual = session.Analyze(document.Snapshot, [], new AnalysisRequest(new TextSpan(0, 7), AnalysisScope.Visible));
        Assert.Empty(actual.Diagnostics);
        Assert.Equal(AnalysisCompleteness.Provisional, actual.Completeness);
        Assert.Null(actual.TotalDiagnosticCount);
    }

    /// <summary>A genuine repaired snapshot clears uncertainty instead of retaining stale provisional state.</summary>
    [Fact]
    public void Repaired_alias_key_recovers_complete_certificate()
    {
        const string source = "value: &a x\n? *missing\n: one\n";
        using var document = new Document(source);
        using var session = new YamlPolicy().CreateSession();
        var before = session.Analyze(document.Snapshot, [], new AnalysisRequest(new TextSpan(0, source.Length), AnalysisScope.Full));
        Assert.Equal(AnalysisCompleteness.Provisional, before.Completeness);
        document.Apply(new TextChange(source.IndexOf("missing", StringComparison.Ordinal), 7, "a"));
        var after = session.Analyze(document.Snapshot, [], new AnalysisRequest(new TextSpan(0, document.Snapshot.Length), AnalysisScope.Full));
        Assert.True(after.Version > before.Version);
        Assert.Equal(AnalysisCompleteness.Complete, after.Completeness);
        Assert.Equal(0, after.TotalDiagnosticCount);
        Assert.Empty(after.Diagnostics);
    }
    /// <summary>Whole-file requests expose all diagnostics in the authoritative immutable snapshot.</summary>
    private static DocumentAnalysis AnalyzeSession(string source)
    {
        using var document = new Document(source);
        using var session = new YamlPolicy().CreateSession();
        return session.Analyze(document.Snapshot, [], new AnalysisRequest(new TextSpan(0, source.Length), AnalysisScope.Full));
    }

    /// <summary>The original undefined-alias error retains message, severity, and exact source range.</summary>
    private static void AssertExactAliasError(string source, FormatAnalysis result)
    {
        if (!source.Contains("*missing", StringComparison.Ordinal)) return;
        var error = Assert.Single(result.Diagnostics, d => d.Code == "yaml.undefined-alias");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal("Alias '*missing' has no preceding anchor.", error.Message);
        Assert.Equal(new TextSpan(source.IndexOf("*missing", StringComparison.Ordinal), 8), error.Span);
    }
}



