using Mote.Engine;
using Mote.Formats;

namespace Mote.Tests;

/// <summary>Bounded typed rendering and absolute provenance independent of native layout.</summary>
public sealed class RenderProjectionTests
{
    /// <summary>Literal order and nested inline flags survive without semantic-node guessing.</summary>
    [Fact]
    public void Markdown_renders_ordered_literals_styles_and_precise_origins()
    {
        const string source = "# Hello **bold *nested*** and `code`\n\nText &amp; [link](https://example.test) ![kitten](cat.png)";
        using var document = new Document(source);
        using var session = MarkdownSession();
        var request = Request(document);
        var analysis = session.Analyze(document.Snapshot, [], request);
        var render = session.Render(document.Snapshot, analysis, request);
        Assert.StartsWith("Hello bold nested and code\n", render.Text);
        Assert.Contains("Text & link [Image: kitten]", render.Text);
        Assert.Contains(render.Runs, r => r.Style.HasFlag(FlowInlineStyle.Strong) && r.Style.HasFlag(FlowInlineStyle.Emphasis));
        Assert.Contains(render.Runs, r => r.Style.HasFlag(FlowInlineStyle.Code));
        Assert.Contains(render.Runs, r => r.Style.HasFlag(FlowInlineStyle.Link));
        Assert.Equal("heading", render.Paragraphs[0].Kind);
        Assert.Equal(1, render.Paragraphs[0].Level);
        Assert.Equal(AnalysisCompleteness.Complete, render.Completeness);
        Assert.False(render.Truncated);
        foreach (var run in render.Runs.Where(r => r.Precision == RenderOriginPrecision.ExactText))
            Assert.Equal(source.Substring(run.SourceRange.Start, run.SourceRange.Length),
                render.Text.Substring(run.DisplayRange.Start, run.DisplayRange.Length));
        Assert.Contains(render.Runs, r => render.Text.Substring(r.DisplayRange.Start, r.DisplayRange.Length) == "&" &&
            r.Precision == RenderOriginPrecision.Item);
    }

    /// <summary>Reference links retain the existing full parser's cross-block resolution on scroll.</summary>
    [Fact]
    public void Cached_full_ast_preserves_reference_resolution_for_new_viewport()
    {
        const string source = "first paragraph\n\n[label][target]\n\n[target]: https://example.test\n";
        using var document = new Document(source);
        using var session = MarkdownSession();
        var first = new AnalysisRequest(new TextSpan(0, 4), AnalysisScope.Visible);
        session.Analyze(document.Snapshot, [], first);
        var start = source.IndexOf("[label]", StringComparison.Ordinal);
        var next = new AnalysisRequest(new TextSpan(start, 5), AnalysisScope.Visible);
        var analysis = session.Analyze(document.Snapshot, [], next);
        var render = session.Render(document.Snapshot, analysis, next);
        Assert.Contains("label", render.Text);
        Assert.DoesNotContain("[target]", render.Text);
        Assert.Contains(render.Runs, r => r.Style.HasFlag(FlowInlineStyle.Link));
    }

    /// <summary>Unresolved parser delimiter containers carry visible literal markers of their own.</summary>
    [Theory]
    [InlineData("[a][b][c][d]")]
    [InlineData("*unclosed")]
    [InlineData("![missing][unknown]")]
    public void Unresolved_delimiters_preserve_literal_text(string source)
    {
        using var document = new Document(source);
        using var session = MarkdownSession();
        var request = Request(document);
        var render = session.Render(document.Snapshot, session.Analyze(document.Snapshot, [], request), request);
        Assert.Equal(source + "\n", render.Text);
    }

    /// <summary>Lists preserve their start and quotes/code preserve block semantics and line breaks.</summary>
    [Fact]
    public void Markdown_preserves_list_start_quote_code_and_linebreaks()
    {
        using var document = new Document("7. seven\n8. eight\n\n> quoted\n\n```cs\na\nb\n```\n\nhard  \nbreak\nsoft\nbreak");
        using var session = MarkdownSession();
        var request = Request(document);
        var render = session.Render(document.Snapshot, session.Analyze(document.Snapshot, [], request), request);
        Assert.Equal(new[] { "7.", "8." }, render.Paragraphs.Where(p => p.Marker is not null).Select(p => p.Marker));
        Assert.StartsWith("7. seven\n8. eight\n", render.Text);
        Assert.All(render.Runs.Where(r => r.Role == "list-marker"), r => Assert.False(r.Navigable));
        Assert.Contains(render.Paragraphs, p => p.Kind == "quote" && p.Depth > 0);
        Assert.Contains(render.Paragraphs, p => p.Kind == "code");
        Assert.Contains("a\nb", render.Text);
        Assert.Contains("hard\nbreak soft break", render.Text);
    }

    /// <summary>Marker-only items have an empty body, not an absent list ordinal or paragraph.</summary>
    [Theory]
    [InlineData("- ", "• \n", "•")]
    [InlineData("1. \n2. two", "1. \n2. two\n", "1.")]
    [InlineData("7. \n8. eight", "7. \n8. eight\n", "7.")]
    public void Empty_list_items_preserve_visible_marker_paragraph_and_origin(string source, string expected, string marker)
    {
        using var document = new Document(source);
        using var session = MarkdownSession();
        var request = Request(document);
        var render = session.Render(document.Snapshot, session.Analyze(document.Snapshot, [], request), request);
        Assert.Equal(expected, render.Text);
        Assert.False(render.Truncated);
        var first = render.Paragraphs[0];
        Assert.Equal("list-item", first.Kind);
        Assert.Equal(marker, first.Marker);
        Assert.Equal(0, first.SourceRange.Start);
        Assert.InRange(first.SourceRange.Length, 1, source.Length);
        Assert.Equal(marker + " ", render.Text.Substring(first.DisplayRange.Start, first.DisplayRange.Length));
        var run = Assert.Single(render.Runs, r => r.DisplayRange.Start == first.DisplayRange.Start);
        Assert.Equal(first.SourceRange, run.SourceRange);
        Assert.Equal("list-marker", run.Role);
        Assert.False(run.Navigable);
        Assert.Equal(RenderOriginPrecision.Item, run.Precision);
    }

    /// <summary>Nested lists cannot consume an outer item's marker, including through quote containers.</summary>
    [Theory]
    [InlineData("1. - child", "1. \n• child\n", "1.")]
    [InlineData("- - child", "• \n• child\n", "•")]
    [InlineData("1. > - child", "1. \n• child\n", "1.")]
    public void Nested_only_items_keep_their_own_marker_before_descendant_list(string source, string expected, string marker)
    {
        using var document = new Document(source);
        using var session = MarkdownSession();
        var request = Request(document);
        var render = session.Render(document.Snapshot, session.Analyze(document.Snapshot, [], request), request);
        Assert.Equal(expected, render.Text);
        Assert.Equal(2, render.Paragraphs.Count);
        Assert.Equal(marker, render.Paragraphs[0].Marker);
        Assert.Equal("•", render.Paragraphs[1].Marker);
        Assert.Equal(0, render.Paragraphs[0].SourceRange.Start);
        Assert.True(render.Paragraphs[0].SourceRange.Length >= render.Paragraphs[1].SourceRange.Length);
        Assert.All(render.Runs.Where(r => r.Role == "list-marker"), r => Assert.False(r.Navigable));
        Assert.False(render.Truncated);
    }

    /// <summary>Hidden syntax consumes visit budget even when it creates no display runs or paragraphs.</summary>
    [Fact]
    public void Hidden_reference_nodes_cannot_bypass_the_syntax_visit_budget()
    {
        using var document = new Document("");
        var analysis = new DocumentAnalysis(document.Snapshot.Version, new TextSpan(0, 0),
            AnalysisCompleteness.Complete, new SemanticNode("document", new TextSpan(0, 0)), [], [], 0);
        var builder = new MarkdownRenderProjection(document.Snapshot, analysis, CancellationToken.None);
        for (var i = 0; i < MarkdownRenderProjection.MaxSyntaxVisits; i++)
        {
            Assert.False(builder.Full);
            builder.Add(new Markdig.Syntax.LinkReferenceDefinitionGroup(), 0);
        }
        Assert.True(builder.Full);
        builder.Add(new Markdig.Syntax.LinkReferenceDefinitionGroup(), 0);
        var render = builder.Finish(false);
        Assert.True(render.Truncated);
        Assert.Contains(render.Runs, r => r.Role == "notice" && !r.Navigable);
        Assert.Equal(AnalysisCompleteness.Complete, render.Completeness);
    }

    /// <summary>Display limits cannot masquerade as incomplete semantics or split surrogate pairs.</summary>
    [Fact]
    public void Truncation_is_bounded_surrogate_safe_and_independent_of_complete_semantics()
    {
        using var document = new Document(string.Concat(Enumerable.Repeat("😀", 20_000)));
        using var session = MarkdownSession();
        var request = Request(document);
        var render = session.Render(document.Snapshot, session.Analyze(document.Snapshot, [], request), request);
        Assert.True(render.Truncated);
        Assert.Equal(AnalysisCompleteness.Complete, render.Completeness);
        Assert.InRange(render.Text.Length, 1, FlowRenderProjection.MaxTextLength);
        Assert.Contains(render.Runs, r => r.Role == "notice" && !r.Navigable);
        for (var i = 0; i < render.Text.Length; i++)
            if (char.IsHighSurrogate(render.Text[i])) Assert.True(++i < render.Text.Length && char.IsLowSurrogate(render.Text[i]));
    }

    /// <summary>Display caps include many tiny paragraphs and inline runs, not just text bytes.</summary>
    [Fact]
    public void Many_paragraphs_and_runs_leave_space_for_a_non_navigable_notice()
    {
        foreach (var source in new[] { string.Concat(Enumerable.Repeat("x\n\n", 200)),
                     string.Concat(Enumerable.Repeat("*x* ", 5_000)) })
        {
            using var document = new Document(source);
            using var session = MarkdownSession();
            var request = Request(document);
            var render = session.Render(document.Snapshot, session.Analyze(document.Snapshot, [], request), request);
            Assert.True(render.Truncated);
            Assert.InRange(render.Paragraphs.Count, 1, FlowRenderProjection.MaxParagraphs);
            Assert.InRange(render.Runs.Count, 1, FlowRenderProjection.MaxRuns);
            Assert.Contains(render.Runs, r => r.Role == "notice" && !r.Navigable);
        }
    }

    /// <summary>A large provisional source line stays bounded and explicitly provisional.</summary>
    [Fact]
    public void Giant_line_returns_bounded_excerpt_without_complete_claim()
    {
        using var document = new Document(new string('x', 5 * 1024 * 1024));
        using var session = MarkdownSession();
        var request = new AnalysisRequest(new TextSpan(0, 100), AnalysisScope.Visible);
        var analysis = session.Analyze(document.Snapshot, [], request);
        var render = session.Render(document.Snapshot, analysis, request);
        Assert.Equal(AnalysisCompleteness.Provisional, render.Completeness);
        Assert.True(render.Truncated);
        Assert.Contains("provisional", render.Text);
        Assert.InRange(render.Text.Length, 1, FlowRenderProjection.MaxTextLength);
    }

    /// <summary>Distant definitions must not turn locally ambiguous brackets into certified links.</summary>
    [Fact]
    public void Distant_rebinding_definitions_keep_large_local_render_provisional()
    {
        using var document = new Document("[a][b][c][d]\n\n" + new string('x', 5 * 1024 * 1024) +
            "\n\n[b]: /first\n[d]: /second\n");
        using var session = MarkdownSession();
        var request = new AnalysisRequest(new TextSpan(0, 12), AnalysisScope.Visible);
        var analysis = session.Analyze(document.Snapshot, [], request);
        var render = session.Render(document.Snapshot, analysis, request);
        Assert.Equal(AnalysisCompleteness.Provisional, render.Completeness);
        Assert.StartsWith("[a][b][c][d]", render.Text);
        Assert.DoesNotContain(render.Runs, r => r.Style.HasFlag(FlowInlineStyle.Link));
        Assert.Contains("provisional", render.Text);
    }

    /// <summary>Lazy syntax displacement follows local edits and remains valid across forced arena rebuilds.</summary>
    [Fact]
    public void Reused_syntax_origins_follow_edits_and_history_is_periodically_rebuilt()
    {
        using var document = new Document("plain\n\n# tail **bold**");
        using var session = MarkdownSession();
        session.Analyze(document.Snapshot, [], Request(document));
        var retainedEdits = session.GetType().GetField("_syntaxReuseEdits",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(retainedEdits);
        for (var i = 0; i < 20; i++)
        {
            var before = document.Snapshot.Version;
            var change = new TextChange(1, 0, "x");
            document.Apply(change);
            var request = Request(document);
            var analysis = session.Analyze(document.Snapshot,
                [new VersionedEdit(before, document.Snapshot.Version, change)], request);
            var render = session.Render(document.Snapshot, analysis, request);
            var tail = Assert.Single(render.Paragraphs, p => p.Kind == "heading");
            Assert.Equal(document.Snapshot.GetText().IndexOf('#'), tail.SourceRange.Start);
            foreach (var run in render.Runs.Where(r => r.Precision == RenderOriginPrecision.ExactText))
                Assert.Equal(document.Snapshot.GetText(run.SourceRange.Start, run.SourceRange.Length),
                    render.Text.Substring(run.DisplayRange.Start, run.DisplayRange.Length));
            Assert.Equal((i + 1) % 17, (int)retainedEdits.GetValue(session)!);
        }
        // Assert the implementation's arena-admission bound, not a process-RSS claim.
        Assert.InRange((int)retainedEdits.GetValue(session)!, 0, 16);
    }

    /// <summary>Rendering rejects stale versions and cancellation does not poison the semantic cache.</summary>
    [Fact]
    public void Render_checks_version_and_cancellation_without_changing_session_state()
    {
        using var document = new Document("plain");
        using var session = MarkdownSession();
        var request = Request(document);
        var analysis = session.Analyze(document.Snapshot, [], request);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => session.Render(document.Snapshot, analysis, request, canceled.Token));
        Assert.Equal("plain\n", session.Render(document.Snapshot, analysis, request).Text);
        document.Apply(new TextChange(0, 0, "new "));
        Assert.Throws<ArgumentException>(() => session.Render(document.Snapshot, analysis, Request(document)));
    }

    /// <summary>The public contract copies caller arrays and rejects impossible provenance/layout.</summary>
    [Fact]
    public void Contract_defensively_copies_and_rejects_invalid_ranges()
    {
        var runs = new[] { new FlowRun(new TextSpan(0, 1), new TextSpan(2, 1), "text", FlowInlineStyle.None, RenderOriginPrecision.ExactText) };
        var paragraphs = new[] { new FlowParagraph(new TextSpan(0, 1), new TextSpan(2, 1), "paragraph") };
        var projection = new FlowRenderProjection(0, "x", runs, paragraphs, false, AnalysisCompleteness.Complete);
        runs[0] = default;
        paragraphs[0] = default;
        Assert.Equal("text", projection.Runs[0].Role);
        Assert.Equal("paragraph", projection.Paragraphs[0].Kind);
        Assert.Throws<ArgumentException>(() => new FlowRenderProjection(0, "😀",
            [new FlowRun(new TextSpan(0, 1), new TextSpan(0, 1), "text", FlowInlineStyle.None, RenderOriginPrecision.ExactText)], [], false, AnalysisCompleteness.Complete));
        Assert.Throws<ArgumentException>(() => new FlowRenderProjection(0, "x",
            [new FlowRun(new TextSpan(0, 1), new TextSpan(int.MaxValue, 1), "text", FlowInlineStyle.None, RenderOriginPrecision.Item)], [], false, AnalysisCompleteness.Complete));
    }

    /// <summary>Non-CommonMark table syntax remains literal; malformed display UTF-16 is rejected.</summary>
    [Fact]
    public void Unadmitted_table_extension_stays_literal_and_malformed_display_is_rejected()
    {
        using var document = new Document("| a | b |\n|---|---|\n|1|2|");
        using var session = MarkdownSession();
        var request = Request(document);
        var render = session.Render(document.Snapshot, session.Analyze(document.Snapshot, [], request), request);
        Assert.Contains("| a | b | |---|---| |1|2|", render.Text);
        Assert.DoesNotContain(render.Paragraphs, p => p.Kind == "table");
        Assert.Throws<ArgumentException>(() => new FlowRenderProjection(0, "\uD800", [], [],
            false, AnalysisCompleteness.Complete));
    }

    private static IRenderFormatSession MarkdownSession() => Assert.IsAssignableFrom<IRenderFormatSession>(
        ((IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Markdown)).CreateSession());

    private static AnalysisRequest Request(Document document) =>
        new(new TextSpan(0, document.Snapshot.Length), AnalysisScope.Visible);
}
