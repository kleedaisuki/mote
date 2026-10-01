using System.Text.Json;
using Mote.Formats;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace Mote.Tests;

/// <summary>Independent semantic and compatibility contracts for clean-tree TOML certification.</summary>
public sealed class TomlTreeCertificationTests
{
    /// <summary>Literal normative examples cover namespaces independently of a parser-generated oracle.</summary>
    public static IEnumerable<object[]> Sources()
    {
        yield return ["v={a.b=1,a.c=2}\n", true];
        yield return ["v={a={b=1},c={b=2}}\n", true];
        yield return ["v=[{x=1},{x=2},[true,\"x\",1],{x={y=3}}]\n", true];
        yield return ["v={\"\"=1,\"a.b\"=2,a.b=3}\n", true];
        yield return ["v={\"雪\"=\"火花\",n=9223372036854775808}\r\n", true];
        yield return ["[[a]]\n[a.b]\nx=1\n[[a]]\n[a.b]\nx=2\n", true];
        yield return ["a.b=1\na.c=2\n", true];
        yield return ["v={a.b=1,a.b=2}\n", false];
        yield return ["v={a=1,a.b=2}\n", false];
        yield return ["v={a.b=1,a={c=2}}\n", false];
        yield return ["v={a={},a.b=2}\n", false];
        yield return ["v={a=[],a.b=2}\n", false];
        yield return ["v={a=1,\"\\u0061\"=2}\n", false];
        yield return ["v=[{x=1},{x=1,x=2}]\n", false];
        yield return ["v={a={x=1,x=2}}\n", false];
        yield return ["a={x=1}\na.x=2\n", false];
        yield return ["[[a]]\n[a.b]\nx=1\n[a.b]\ny=2\n", false];
        yield return ["a=1\n\"\\u0061\"=2\n", false];
        yield return ["[a]\nx=1\n[broken\nx=2\n[b]\ny=1\ny=2\n", false];
        yield return ["a=1\n# bad\r", false];
        yield return ["a=1\n\0", false];
        yield return ["a=[1,\n[b]\nx=1\nx=2\n", false];
    }

    /// <summary>Fresh inline/array namespaces accept legal siblings and reject sealed or decoded-alias collisions.</summary>
    [Theory]
    [MemberData(nameof(Sources))]
    public void Literal_semantics_and_invalid_format_preservation(string source, bool valid)
    {
        var syntax = SyntaxParser.Parse(source, validate: false);
        Assert.Equal(valid, TomlTreeCertification.TryCertify(syntax));
        var policy = new TomlPolicy();
        var analysis = policy.Analyze(source);
        Assert.Equal(valid, analysis.Diagnostics.Count == 0);
        AssertSpans(source, analysis);
        string formatted = policy.Format(source);
        if (!valid) Assert.Equal(source, formatted);
        else
        {
            Assert.Empty(policy.Analyze(formatted).Diagnostics);
            Assert.Equal(formatted, policy.Format(formatted));
            Assert.Equal(ValueTree(analysis.Root), ValueTree(policy.Analyze(formatted).Root));
            Assert.Equal(source.Count(c => c == '\r'), formatted.Count(c => c == '\r'));
            Assert.Equal(source.Count(c => c == '\n'), formatted.Count(c => c == '\n'));
        }
    }

    /// <summary>Certification never adopts nodes or rewrites spans, parents, trivia, or source on either outcome.</summary>
    [Theory]
    [MemberData(nameof(Sources))]
    public void Certification_is_read_only_for_valid_and_refused_trees(string source, bool valid)
    {
        var syntax = SyntaxParser.Parse(source, validate: false);
        string serialized = syntax.ToString();
        var nodes = syntax.Descendants().Prepend(syntax).ToArray();
        var parents = nodes.Select(n => n.Parent).ToArray();
        var spans = nodes.Select(n => n.Span).ToArray();
        var tokens = syntax.Tokens(includeCommentsAndWhitespaces: true).ToArray();
        var tokenParents = tokens.Select(t => t.Parent).ToArray();
        var tokenSpans = tokens.Select(t => t.Span).ToArray();
        Assert.Equal(valid, TomlTreeCertification.TryCertify(syntax));
        Assert.Equal(serialized, syntax.ToString());
        Assert.Equal(nodes, syntax.Descendants().Prepend(syntax));
        for (int i = 0; i < nodes.Length; i++) { Assert.Same(parents[i], nodes[i].Parent); Assert.Equal(spans[i], nodes[i].Span); }
        Assert.Equal(tokens, syntax.Tokens(includeCommentsAndWhitespaces: true));
        for (int i = 0; i < tokens.Length; i++) { Assert.Same(tokenParents[i], tokens[i].Parent); Assert.Equal(tokenSpans[i], tokens[i].Span); }
    }

    /// <summary>Any failed certification must preserve the entire established recovery diagnostic sequence.</summary>
    [Theory]
    [MemberData(nameof(Sources))]
    public void Fallback_retains_exact_messages_ids_counts_and_utf16_spans(string source, bool valid)
    {
        if (valid) return;
        var grammar = SyntaxParser.Parse(source, validate: false);
        var expected = grammar.Diagnostics.Select(d =>
        {
            int start = Math.Clamp(d.Span.Offset, 0, source.Length);
            return new Diagnostic(d.Kind == DiagnosticMessageKind.Error ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                "TOML_PARSE", d.Message, new(start, Math.Clamp(d.Span.Length, 0, source.Length - start)));
        }).ToList();
        var seen = new HashSet<Diagnostic>(expected);
        foreach (var diagnostic in TomlDocumentValidation.Validate(source, default))
            if (seen.Add(diagnostic)) expected.Add(diagnostic);
        expected.Sort((a, b) => a.Span.Start.CompareTo(b.Span.Start));
        Assert.Equal(expected, new TomlPolicy().Analyze(source).Diagnostics);
    }

    /// <summary>Mixed deeply nested containers use independent scopes without introducing a certification depth cap.</summary>
    [Fact]
    public void Deep_mixed_containers_preserve_values_and_comments()
    {
        string value = "{x=1}";
        for (int i = 0; i < 48; i++) value = i % 2 == 0 ? "[" + value + ",{x=2}]" : "{v=" + value + "}";
        string source = "# 😀 untouched\r\na=" + value + " # tail\r\n";
        var syntax = SyntaxParser.Parse(source, validate: false);
        Assert.False(syntax.HasErrors);
        Assert.True(TomlTreeCertification.TryCertify(syntax));
        var policy = new TomlPolicy();
        string formatted = policy.Format(source);
        Assert.StartsWith("# 😀 untouched\r\n", formatted);
        Assert.EndsWith(" # tail\r\n", formatted);
        Assert.Equal(ValueTree(policy.Analyze(source).Root), ValueTree(policy.Analyze(formatted).Root));
        Assert.Equal(formatted, policy.Format(formatted));
    }

    /// <summary>Mutable public nodes with absent required structure must not receive a valid-tree certificate.</summary>
    [Theory]
    [InlineData("equal")]
    [InlineData("value")]
    [InlineData("array-close")]
    [InlineData("inline-close")]
    [InlineData("key")]
    public void Missing_structure_is_refused(string field)
    {
        var syntax = SyntaxParser.Parse("a=[1,{x=2}]\n", validate: false);
        var pair = syntax.KeyValues.GetChild(0)!;
        var array = (ArraySyntax)pair.Value!;
        switch (field)
        {
            case "equal": pair.EqualToken = null; break;
            case "value": pair.Value = null; break;
            case "array-close": array.CloseBracket = null; break;
            case "inline-close": ((InlineTableSyntax)array.Items.GetChild(1)!.Value!).CloseBrace = null; break;
            case "key": pair.Key = null; break;
        }
        Assert.False(TomlTreeCertification.TryCertify(syntax));
    }

    /// <summary>Pre-cancellation throws without changing the tree or poisoning later independent certification.</summary>
    [Fact]
    public void Cancellation_does_not_publish_or_retain_partial_state()
    {
        var syntax = SyntaxParser.Parse("v=[{x=1},{x=2}]\n", validate: false);
        string before = syntax.ToString();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => TomlTreeCertification.TryCertify(syntax, cancellation.Token));
        Assert.Equal(before, syntax.ToString());
        Assert.True(TomlTreeCertification.TryCertify(syntax));
    }

    /// <summary>All public spans are bounded by the current UTF-16 source, including nested projection.</summary>
    private static void AssertSpans(string source, FormatAnalysis analysis)
    {
        var spans = analysis.Diagnostics.Select(d => d.Span).Concat(analysis.Tokens.Select(t => t.Span));
        var pending = new Stack<SemanticNode>(); pending.Push(analysis.Root);
        while (pending.TryPop(out var node)) { spans = spans.Append(node.Span); foreach (var child in node.Children) pending.Push(child); }
        foreach (var span in spans) { Assert.InRange(span.Start, 0, source.Length); Assert.InRange(span.Length, 0, source.Length - span.Start); }
    }

    /// <summary>Positions may shift under gap normalization; kinds, names and exact scalar text may not.</summary>
    private static string ValueTree(SemanticNode node)
    {
        var values = new List<(int Depth, string Kind, string? Name, string? Value)>();
        var pending = new Stack<(SemanticNode Node, int Depth)>(); pending.Push((node, 0));
        while (pending.TryPop(out var current))
        {
            values.Add((current.Depth, current.Node.Kind, current.Node.Name, current.Node.Value));
            for (int i = current.Node.Children.Count - 1; i >= 0; i--) pending.Push((current.Node.Children[i], current.Depth + 1));
        }
        return JsonSerializer.Serialize(values, new JsonSerializerOptions { IncludeFields = true });
    }
}
