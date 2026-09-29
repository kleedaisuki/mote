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
}
