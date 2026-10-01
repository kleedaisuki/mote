using System.Text;
using Mote.Engine;
using Mote.Formats;

namespace Mote.Tests;

/// <summary>Checks YAML representation-graph equality independently of identity allocation.</summary>
public sealed class YamlKeyGraphIdentityTests
{
    /// <summary>Normative scalar tags, sequence order, mapping permutation, and bound aliases.</summary>
    public static IEnumerable<object[]> EqualityCases()
    {
        yield return ["? [a, b]\n: first\n? [a, b]\n: second\n", 1];
        yield return ["? [a, b]\n: first\n? [b, a]\n: second\n", 0];
        yield return ["? {a: 1, b: 2}\n: first\n? {b: 2, a: 1}\n: second\n", 1];
        yield return ["? {a: 1, b: 2}\n: first\n? {a: 2, b: 1}\n: second\n", 0];
        yield return ["11: first\n0xB: second\n", 1];
        yield return ["11: first\n\"11\": second\n", 0];
        yield return ["!!str 11: first\n\"11\": second\n", 1];
        yield return ["? &a [x, y]\n: first\n? &b [x, y]\n: second\n? *a\n: third\n", 2];
        yield return ["? !left [x]\n: first\n? !right [x]\n: second\n", 0];
        yield return ["? !same [x]\n: first\n? !same [x]\n: second\n", 1];
        yield return ["? {a: 1, a: 1}\n: first\n? {a: 1}\n: second\n", 1];
        yield return ["? {a: 1, a: 1}\n: first\n? {a: 1, a: 1}\n: second\n", 3];
        yield return ["v1: &a [old]\nv2: *a\nv3: &a [new]\n? *a\n: first\n? [new]\n: second\n? [old]\n: third\n", 1];
        yield return ["---\n&a x: one\n---\n*a: two\n", 0];
    }

    /// <summary>Expected duplicates count mapping-local errors, including malformed nested maps.</summary>
    [Theory]
    [MemberData(nameof(EqualityCases))]
    public void Key_equality_matches_representation_graph(string source, int duplicates)
    {
        var result = new YamlPolicy().Analyze(source);
        Assert.Equal(duplicates, result.Diagnostics.Count(d => d.Code == "yaml.duplicate-key"));
        AssertSpans(source, result);
    }

    /// <summary>Equal expansions with different anchor-sharing topology have equal YAML keys.</summary>
    [Theory]
    [InlineData(4)]
    [InlineData(35)]
    public void Alias_dag_does_not_expand_exponentially(int depth)
    {
        var source = Dag(depth);
        Assert.True(source.Length < 4096);
        var result = new YamlPolicy().Analyze(source);
        Assert.Single(result.Diagnostics, d => d.Code == "yaml.duplicate-key");
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "yaml.key-equality-unsupported");
        AssertSpans(source, result);
        Assert.Equal(source, new YamlPolicy().Format(source));
        using var document = new Document(source);
        using var session = new YamlPolicy().CreateSession();
        var full = session.Analyze(document.Snapshot, [], new AnalysisRequest(new TextSpan(0, source.Length), AnalysisScope.Full));
        Assert.Equal(AnalysisCompleteness.Complete, full.Completeness);
        Assert.Equal(result.Diagnostics.Count, full.TotalDiagnosticCount);
    }

    /// <summary>Builds two equal binary DAGs without materializing the exponential expansion.</summary>
    public static string Dag(int depth)
    {
        var source = new StringBuilder("left0: &a0 x\nright0: &b0 x\n");
        for (var i = 1; i <= depth; i++)
        {
            source.Append($"left{i}: &a{i} [*a{i - 1}, *a{i - 1}]\n");
            source.Append($"right{i}: &b{i} [*b{i - 1}, *a{i - 1}]\n");
        }
        return source.Append($"? *a{depth}\n: first\n? *b{depth}\n: second\n").ToString();
    }

    /// <summary>Unknown scalar canonicalizers and cyclic graphs must not guess uniqueness.</summary>
    [Theory]
    [InlineData("? !custom text\n: first\n? !custom text\n: second\n")]
    [InlineData("value: &a [*a]\n? *a\n: first\n? *a\n: second\n")]
    public void Unsupported_keys_have_honest_small_session_certificate(string source)
    {
        var result = new YamlPolicy().Analyze(source);
        Assert.Contains(result.Diagnostics, d => d.Code == "yaml.key-equality-unsupported");
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "yaml.duplicate-key");
        using var document = new Document(source);
        using var session = new YamlPolicy().CreateSession();
        var full = session.Analyze(document.Snapshot, [], new AnalysisRequest(new TextSpan(0, source.Length), AnalysisScope.Full));
        Assert.Equal(AnalysisCompleteness.Provisional, full.Completeness);
        Assert.Null(full.TotalDiagnosticCount);
        AssertSpans(source, result);
    }

    /// <summary>An earlier alias retains its original anchor despite later name redefinition.</summary>
    [Fact]
    public void Alias_binding_is_not_retroactive()
    {
        const string source = "old: &a [x]\n? [*a]\n: first\nnew: &a [y]\n? [[x]]\n: second\n? [[y]]\n: third\n";
        var result = new YamlPolicy().Analyze(source);
        var duplicate = Assert.Single(result.Diagnostics, d => d.Code == "yaml.duplicate-key");
        Assert.Equal(source.IndexOf("[[x]]", StringComparison.Ordinal), duplicate.Span.Start);
    }

    /// <summary>
    /// Characterizes an inherited flow-key span omission; this is not a correct full-span claim.
    /// Frozen baseline and candidate both omit the closing bracket for BMP and supplementary text.
    /// </summary>
    [Theory]
    [InlineData("猫", "犬")]
    [InlineData("猫", "😀")]
    public void Existing_flow_key_span_omits_closing_bracket(string left, string right)
    {
        var key = $"[{left}, {right}]";
        var source = $"# 猫😀\n? {key}\n: first\n? {key}\n: second\n";
        var result = new YamlPolicy().Analyze(source);
        var duplicate = Assert.Single(result.Diagnostics, d => d.Code == "yaml.duplicate-key");
        Assert.Equal(source.LastIndexOf(key, StringComparison.Ordinal), duplicate.Span.Start);
        // Correct full collection coverage would be key.Length; retain the observed gap explicitly.
        Assert.Equal(key.Length - 1, duplicate.Span.Length);
        Assert.Equal(key[..^1], source.Substring(duplicate.Span.Start, duplicate.Span.Length));
        AssertSpans(source, result);
    }
    /// <summary>A cached shallow success cannot bypass the existing deep-path comparison limit.</summary>
    [Fact]
    public void Reused_identity_keeps_path_depth_limit()
    {
        var text = new StringBuilder("v0: &a0 x\n");
        for (var i = 1; i <= 125; i++) text.Append($"v{i}: &a{i} [*a{i - 1}]\n");
        text.Append("? *a125\n: shallow\n");
        for (var i = 126; i <= 130; i++) text.Append($"v{i}: &a{i} [*a{i - 1}]\n");
        text.Append("? *a130\n: deep\n");
        var result = new YamlPolicy().Analyze(text.ToString());
        var unsupported = Assert.Single(result.Diagnostics, d => d.Code == "yaml.key-equality-unsupported");
        Assert.Contains("key nesting exceeds comparison limit", unsupported.Message);
        Assert.Equal(text.ToString().LastIndexOf("*a130", StringComparison.Ordinal), unsupported.Span.Start);
    }
    /// <summary>Cancellation never poisons a later independent policy call.</summary>
    [Fact]
    public void Cancelled_analysis_throws_and_next_call_succeeds()
    {
        var policy = new YamlPolicy();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => policy.Analyze("key: value\n", cancellation.Token));
        Assert.Empty(policy.Analyze("key: value\n").Diagnostics);
    }

    /// <summary>Formatting retains idempotence and refuses invalid duplicate-key changes.</summary>
    [Theory]
    [InlineData("key:    value\n", "key: value\n")]
    [InlineData("key:    one\nkey:    two\n", "key:    one\nkey:    two\n")]
    [InlineData("? [x, y]\n:   value\n", "? [x, y]\n:   value\n")]
    public void Formatting_contract_remains_conservative(string source, string expected)
    {
        var policy = new YamlPolicy();
        Assert.Equal(expected, policy.Format(source));
        Assert.Equal(expected, policy.Format(expected));
    }

    /// <summary>Checks all projected offsets against immutable UTF-16 input.</summary>
    private static void AssertSpans(string source, FormatAnalysis result)
    {
        Assert.Equal(source, result.SourceText);
        foreach (var span in result.Diagnostics.Select(d => d.Span).Concat(result.Tokens.Select(t => t.Span)))
        {
            Assert.InRange(span.Start, 0, source.Length);
            Assert.InRange(span.Length, 0, source.Length - span.Start);
        }
    }
}



