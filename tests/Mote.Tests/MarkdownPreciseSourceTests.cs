using Mote.Engine;
using Mote.Formats;
using Mote.Native;
using Markdig;

namespace Mote.Tests;

/// <summary>Independent source-string oracles for Markdown's public UTF-16 coordinates.</summary>
public sealed class MarkdownPreciseSourceTests
{
    /// <summary>References resolve globally, but nodes, tokens and warnings belong to each use.</summary>
    [Theory]
    [InlineData("\n", false, false)]
    [InlineData("\r\n", false, true)]
    [InlineData("\n", true, true)]
    [InlineData("\r\n", true, false)]
    public void Reference_positions_and_duplicate_winner_are_exact(string newline,
        bool definitionsFirst, bool unsafeWinner)
    {
        var destination = unsafeWinner ? "javascript:bad" : "https://safe.example";
        var duplicate = unsafeWinner ? "https://safe.example" : "javascript:bad";
        var definitions = $"[id]: {destination}{newline}[ID]: {duplicate}{newline}{newline}";
        var prefix = $"# 😀 Heading{newline}{newline}" + new string('x', 300) + newline + newline;
        var uses = $"Before [*😀 use*][id] after.{newline}{newline}Tail [other][ID].{newline}";
        var source = prefix + (definitionsFirst ? definitions + uses : uses + newline + definitions);
        var analysis = new MarkdownPolicy().Analyze(source);
        var atoms = new[] { "[*😀 use*][id]", "[other][ID]" };
        var expected = atoms.Select(atom => Exact(source, atom)).ToArray();
        var links = Descendants(analysis.Root).Where(node => node.Kind == "link").ToArray();
        Assert.Equal(expected, links.Select(node => node.Span));
        Assert.All(links, node => Assert.Equal(destination, node.Value));
        Assert.Equal(expected, analysis.Tokens.Where(token => token.Kind == "link").Select(token => token.Span));
        Assert.Equal(Exact(source, "*😀 use*"), Assert.Single(links[0].Children).Span);
        Assert.Equal("emphasis", links[0].Children[0].Kind);
        Assert.Equal(unsafeWinner ? expected : [], analysis.Diagnostics.Select(diagnostic => diagnostic.Span));
        Assert.All(analysis.Diagnostics, diagnostic => Assert.Equal("markdown.unsafe-link", diagnostic.Code));
        Assert.Equal(source, analysis.SourceText);
        var html = new MarkdownPolicy().RenderHtml(analysis);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("😀 use", html, StringComparison.Ordinal);

        // A session must preserve the same absolute positions, not page-relative offsets.
        using var document = new Document(source);
        using var session = new MarkdownPolicy().CreateSession();
        var projected = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(0, source.Length), AnalysisScope.Full));
        Assert.Equal(AnalysisCompleteness.Complete, projected.Completeness);
        Assert.Equal(expected, Descendants(projected.Root).Where(node => node.Kind == "link").Select(node => node.Span));
        Assert.Equal(analysis.Diagnostics.Select(diagnostic => diagnostic.Span), projected.Diagnostics.Select(diagnostic => diagnostic.Span));
    }

    /// <summary>Inline links, images, code and nested emphasis retain full source delimiters.</summary>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Nonreference_inline_positions_are_exact(string newline)
    {
        var source = $"# 😀 Title{newline}{newline}Lead **bold** `😀 code` [*text*](https://safe.example) ![img](/relative) [bad](javascript:bad).{newline}";
        var analysis = new MarkdownPolicy().Analyze(source);
        foreach (var (kind, atom) in new[]
        {
            ("emphasis", "**bold**"), ("code", "`😀 code`"),
            ("link", "[*text*](https://safe.example)"), ("emphasis", "*text*"),
            ("image", "![img](/relative)"), ("link", "[bad](javascript:bad)")
        })
        {
            var expected = Exact(source, atom);
            Assert.Contains(Descendants(analysis.Root), node => node.Kind == kind && node.Span == expected);
            Assert.Contains(analysis.Tokens, token => token.Kind == kind && token.Span == expected);
        }
        Assert.Equal(Exact(source, "[bad](javascript:bad)"), Assert.Single(analysis.Diagnostics).Span);
    }

    /// <summary>Precise inline positions do not change the existing paragraph-level native map.</summary>
    [Fact]
    public void Native_preview_maps_reference_display_to_its_paragraph()
    {
        const string source = "# 😀 Title\r\n\r\nLead [use][id] tail.\r\n\r\n[id]: https://safe.example\r\n";
        var analysis = new MarkdownPolicy().Analyze(source);
        var preview = NativePreviewBuilder.Build(analysis, DocumentKind.Markdown, 0, true);
        var run = Assert.Single(preview.Spans, span => span.Kind == "paragraph");
        Assert.Equal(Exact(source, "Lead [use][id] tail."), run.SourceSpan);
        Assert.Contains("Lead use tail.", preview.Text, StringComparison.Ordinal);
    }

    /// <summary>Location tracking does not add grammar extensions or change safe HTML output.</summary>
    [Fact]
    public void Precise_pipeline_preserves_safe_rendering_and_raw_html_policy()
    {
        const string source = "#   😀 Title\r\n\r\n> Quote **bold** and [use][id].\r\n\r\n- `code` ![image](/img)\r\n- <https://safe.example>\r\n\r\n```cs\r\nint x = 1;\r\n```\r\n\r\n<script>alert(1)</script>\r\n\r\n[id]: https://safe.example \"title\"\r\n";
        var policy = new MarkdownPolicy();
        var previousPipeline = new MarkdownPipelineBuilder().DisableHtml().Build();
        Assert.Equal(Markdown.ToHtml(source, previousPipeline), policy.RenderHtml(policy.Analyze(source)));
        var formatted = policy.Format(source);
        Assert.StartsWith("# 😀 Title", formatted, StringComparison.Ordinal);
        Assert.Equal(policy.RenderHtml(policy.Analyze(source)), policy.RenderHtml(policy.Analyze(formatted)));
        Assert.Equal(formatted, policy.Format(formatted));
    }

    /// <summary>Finds deliberately unique fixture atoms without consulting parser positions.</summary>
    private static TextSpan Exact(string source, string atom)
    {
        var start = source.IndexOf(atom, StringComparison.Ordinal);
        Assert.True(start >= 0);
        Assert.Equal(start, source.LastIndexOf(atom, StringComparison.Ordinal));
        return new TextSpan(start, atom.Length);
    }

    /// <summary>Traverses recursive semantic children, including nested reference emphasis.</summary>
    private static IEnumerable<SemanticNode> Descendants(SemanticNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Descendants(child)) yield return descendant;
    }
}
