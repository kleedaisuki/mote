using Mote.Engine;
using Mote.Formats;

namespace Mote.Tests;

/// <summary>Checks the streaming YAML checker against the whole-source semantic oracle.</summary>
public sealed class YamlStreamDifferentialTests
{
    /// <summary>Canonical key equality and explicit tags retain exact global diagnostic spans.</summary>
    [Theory]
    [InlineData("0xB: a\n11: b\n", "yaml.duplicate-key")]
    [InlineData("? [a, b]\n: first\n? [a, b]\n: second\n", "yaml.duplicate-key")]
    [InlineData("? {a: 1, b: 2}\n: first\n? {b: 2, a: 1}\n: second\n", "yaml.duplicate-key")]
    [InlineData("? &anchor [a, b]\n: first\n? *anchor\n: second\n", "yaml.duplicate-key")]
    [InlineData("11: number\n\"11\": string\n", null)]
    [InlineData("1_000: extended\n1000: core\n", null)]
    [InlineData("value: !!int nope\n", "yaml.invalid-tagged-scalar")]
    [InlineData("item: *missing\n", "yaml.undefined-alias")]
    public void Large_streamed_yaml_diagnostics_match_legacy_oracle(string tail, string? expectedCode)
    {
        var source = "# " + new string('p', 300 * 1024) + "\n" + tail;
        using var document = new Document(source);
        var policy = (IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Yaml);
        var expected = policy.Analyze(source);
        using var session = policy.CreateSession();
        var actual = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(0, 16), AnalysisScope.Full));
        Assert.Equal(AnalysisCompleteness.Complete, actual.Completeness);
        Assert.Equal(expected.Diagnostics.Count, actual.TotalDiagnosticCount);
        Assert.Equal(expected.Diagnostics.Select(d => (d.Code, d.Span.Start, d.Span.Length)),
            actual.Diagnostics.Select(d => (d.Code, d.Span.Start, d.Span.Length)));
        if (expectedCode is not null)
            Assert.Contains(actual.Diagnostics, d => d.Code == expectedCode &&
                d.Span.Start >= source.Length - tail.Length);
        else
            Assert.DoesNotContain(actual.Diagnostics, d => d.Code == "yaml.duplicate-key");
    }

    /// <summary>An unsupported custom-tag key never receives a false whole-document claim.</summary>
    [Fact]
    public void Large_streamed_custom_key_downgrades_without_false_duplicate()
    {
        var source = "# " + new string('p', 300 * 1024) + "\n" +
            "? !custom thing\n: first\n? !custom thing\n: second\n";
        using var document = new Document(source);
        using var session = ((IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Yaml)).CreateSession();
        var actual = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(0, 16), AnalysisScope.Full));
        Assert.Equal(AnalysisCompleteness.Provisional, actual.Completeness);
        Assert.Null(actual.TotalDiagnosticCount);
        Assert.Contains(actual.Diagnostics, d => d.Code == "yaml.key-equality-unsupported");
        Assert.DoesNotContain(actual.Diagnostics, d => d.Code == "yaml.duplicate-key");
    }

    /// <summary>
    /// An unbound alias in a complex key prevents a proof of key uniqueness. The event
    /// stream may continue, but a missing duplicate must not be reported as complete.
    /// </summary>
    [Theory]
    [InlineData("? *missing\n: first\n? *missing\n: second\n")]
    [InlineData("? [*missing]\n: first\n? [*missing]\n: second\n")]
    [InlineData("? {a: *missing}\n: first\n? {a: *missing}\n: second\n")]
    public void Large_streamed_unbound_alias_keys_are_provisional(string tail)
    {
        var source = "# " + new string('p', 300 * 1024) + "\n" + tail;
        using var document = new Document(source);
        using var session = ((IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Yaml)).CreateSession();
        var actual = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(0, 16), AnalysisScope.Full));

        Assert.Equal(AnalysisCompleteness.Provisional, actual.Completeness);
        Assert.Null(actual.TotalDiagnosticCount);
        Assert.Contains(actual.Diagnostics, d => d.Code == "yaml.undefined-alias");
        Assert.Contains(actual.Diagnostics, d => d.Code == "yaml.key-equality-unsupported");
        Assert.DoesNotContain(actual.Diagnostics, d => d.Code == "yaml.duplicate-key");
    }

    /// <summary>Offscreen key recovery remains honest beyond the small-document threshold.</summary>
    [Fact]
    public void Multi_megabyte_unbound_alias_key_does_not_claim_complete()
    {
        var source = string.Concat(Enumerable.Repeat("# " + new string('p', 16 * 1024) + "\n", 192)) +
            "? *missing\n: value\n";
        Assert.True(source.Length > 2 * 1024 * 1024);
        using var document = new Document(source);
        using var session = ((IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Yaml)).CreateSession();
        var actual = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(0, 16), AnalysisScope.Full));

        Assert.Equal(AnalysisCompleteness.Provisional, actual.Completeness);
        Assert.Null(actual.TotalDiagnosticCount);
        var aliasStart = source.IndexOf("*missing", StringComparison.Ordinal);
        Assert.True(aliasStart > 2 * 1024 * 1024);
        Assert.Contains(actual.Diagnostics, d => d.Code == "yaml.undefined-alias" &&
            d.Span.Start == aliasStart && d.Span.Length == "*missing".Length);
        Assert.Contains(actual.Diagnostics, d => d.Code == "yaml.key-equality-unsupported" &&
            d.Span.Start == aliasStart && d.Span.Length == "*missing".Length);
    }

    /// <summary>An anchor used by a later key must carry an unresolved child alias as uncertainty.</summary>
    [Fact]
    public void Anchored_value_with_unbound_child_alias_cannot_certify_later_alias_key()
    {
        var tail = "data: &a [*missing]\n? *a\n: first\n? *a\n: second\n";
        var source = "# " + new string('p', 300 * 1024) + "\n" + tail;
        using var document = new Document(source);
        using var session = ((IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Yaml)).CreateSession();
        var actual = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(0, 16), AnalysisScope.Full));

        Assert.Equal(AnalysisCompleteness.Provisional, actual.Completeness);
        Assert.Null(actual.TotalDiagnosticCount);
        var missing = source.IndexOf("*missing", StringComparison.Ordinal);
        Assert.Contains(actual.Diagnostics, d => d.Code == "yaml.undefined-alias" &&
            d.Span.Start == missing && d.Span.Length == "*missing".Length);
        Assert.Contains(actual.Diagnostics, d => d.Code == "yaml.key-equality-unsupported" &&
            d.Span.Start == missing && d.Span.Length == "*missing".Length);
        Assert.DoesNotContain(actual.Diagnostics, d => d.Code == "yaml.duplicate-key");
    }
}
