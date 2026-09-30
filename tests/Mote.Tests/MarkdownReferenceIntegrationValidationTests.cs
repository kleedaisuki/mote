using Mote.Engine;
using Mote.Formats;

namespace Mote.Tests;

/// <summary>Independent public-workflow checks for bounded large Markdown reference semantics.</summary>
public sealed class MarkdownReferenceIntegrationValidationTests
{
    /// <summary>Forces the new path beyond sparse whole-document parser admission.</summary>
    private const int Copies = 5_600;
    /// <summary>Real owner ballast must survive projection rather than becoming a skeleton marker.</summary>
    private static readonly string Owner = "Alpha " + new string('a', 3_000) + " [use][id]";

    /// <summary>Cold Visible never asserts a whole-file count; Full may certify real distant winners.</summary>
    [Theory]
    [InlineData("\n", 0, "https://safe.example")]
    [InlineData("\r\n", 2_800, "javascript:bad")]
    [InlineData("\n", Copies, "https://safe.example")]
    public void Cold_visible_then_full_matches_real_owner_oracle(string newline, int definitionAt, string destination)
    {
        var definitions = $"[id]: {destination}{newline}{newline}[ID]: https://loser.example{newline}{newline}";
        var source = Source(Owner, newline, definitions, definitionAt);
        using var document = new Document(source);
        using var session = new MarkdownPolicy().CreateSession();
        var offset = source.IndexOf(Owner, StringComparison.Ordinal);
        var range = new TextSpan(offset + 2_995, 10);
        var cold = session.Analyze(document.Snapshot, [], new(range, AnalysisScope.Visible));
        Assert.Equal(AnalysisCompleteness.Provisional, cold.Completeness);
        Assert.Null(cold.TotalDiagnosticCount);
        var full = session.Analyze(document.Snapshot, [], new(range, AnalysisScope.Full));
        Complete(document.Snapshot, full, destination.StartsWith("javascript", StringComparison.Ordinal) ? Copies : 0);
        MatchOwner(full, Owner, offset, definitions);
        MatchRender((IRenderFormatSession)session, document.Snapshot, full, range, Owner, offset, definitions);
        var warm = session.Analyze(document.Snapshot, [], new(range, AnalysisScope.Visible));
        Complete(document.Snapshot, warm, full.TotalDiagnosticCount!.Value);
        MatchOwner(warm, Owner, offset, definitions);
    }

    /// <summary>Display-key presence cannot create accidental shortcut links when a target is missing.</summary>
    [Theory]
    [InlineData("[use]: javascript:bad\n\n", false)]
    [InlineData("[id]: https://safe.example\n\n[use]: javascript:bad\n\n", true)]
    [InlineData("", false)]
    public void Missing_target_and_text_key_reads_match_independent_parser(string definitions, bool targetPresent)
    {
        var source = Source(Owner, "\n", definitions, Copies);
        using var document = new Document(source);
        using var session = new MarkdownPolicy().CreateSession();
        var result = Analyze(session, document.Snapshot, [], 0);
        Complete(document.Snapshot, result, 0);
        MatchOwner(result, Owner, 0, definitions);
        Assert.Equal(targetPresent ? 1 : 0, Walk(result.Root.Children.Single()).Count(n => n.Kind == "link"));
    }

    /// <summary>Shared skeletons must not conflate original ballast length or distant absolute geometry.</summary>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Distant_variant_geometry_and_opaque_fence_use_actual_source(string newline)
    {
        var distant = "Alpha " + new string('b', 2_999) + " [use][id]";
        var definitions = $"```json{newline}[id]: javascript:hidden{newline}```{newline}{newline}" +
            $"[id]: https://real.example{newline}{newline}";
        var block = Owner + newline + newline;
        var before = string.Concat(Enumerable.Repeat(block, Copies));
        var offset = before.Length + definitions.Length;
        using var document = new Document(before + definitions + distant + newline + newline);
        using var session = new MarkdownPolicy().CreateSession();
        var result = Analyze(session, document.Snapshot, [], offset);
        Complete(document.Snapshot, result, 0);
        MatchOwner(result, distant, offset, definitions);
        var render = ((IRenderFormatSession)session).Render(document.Snapshot, result,
            new(new(offset + 6, 1), AnalysisScope.Full));
        Assert.Contains(new string('b', 2_999), render.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('a', 3_000), render.Text, StringComparison.Ordinal);
        Assert.Equal(new TextSpan(offset, distant.Length), Assert.Single(render.Paragraphs).SourceRange);
    }

    /// <summary>Unsupported syntax anywhere must refuse global certification, not inherit cached winners.</summary>
    [Theory]
    [InlineData("Alpha [ü][id]")]
    [InlineData("Alpha [use][id][use][id]")]
    [InlineData("Alpha [id]")]
    [InlineData("- Alpha [use][id]")]
    [InlineData("Alpha [use][id]\ncontinued")]
    [InlineData("[id]: https://safe.example\n[other]: https://other.example")]
    public void Unsupported_offscreen_owners_are_provisional(string unsupported)
    {
        var source = Source(Owner, "\n", "[id]: https://safe.example\n\n" + unsupported + "\n\n", Copies);
        using var document = new Document(source);
        using var session = new MarkdownPolicy().CreateSession();
        var result = Analyze(session, document.Snapshot, [], 0);
        Assert.Equal(AnalysisCompleteness.Provisional, result.Completeness);
        Assert.Null(result.TotalDiagnosticCount);
        Assert.True(result.Coverage.Length <= 512 * 1024);
    }

    /// <summary>Real edit chains retarget all consumers and removing a first declaration promotes its duplicate.</summary>
    [Fact]
    public void Destination_consumer_and_winner_removal_edits_undo_redo_are_exact()
    {
        const string initialDefinitions = "[id]: https://first.example\n\n[ID]: javascript:second\n\n";
        var source = Source(Owner, "\n", initialDefinitions, Copies);
        using var document = new Document(source);
        using var session = new MarkdownPolicy().CreateSession();
        Complete(document.Snapshot, Analyze(session, document.Snapshot, [], 0), 0);
        var position = source.IndexOf("https://first.example", StringComparison.Ordinal);
        var edit = new TextChange(position, "https://first.example".Length, "javascript:first");
        var result = ApplyAnalyze(document, session, edit);
        Complete(document.Snapshot, result, Copies);
        MatchOwner(result, Owner, 0, "[id]: javascript:first\n\n[ID]: javascript:second\n\n");
        var consumerAt = Owner.IndexOf("[id]", StringComparison.Ordinal);
        result = ApplyAnalyze(document, session, new TextChange(consumerAt, 4, "[missing]"));
        Complete(document.Snapshot, result, Copies - 1);
        MatchOwner(result, Owner.Replace("[id]", "[missing]", StringComparison.Ordinal), 0,
            "[id]: javascript:first\n\n[ID]: javascript:second\n\n");
        var winnerAt = document.Snapshot.GetText().IndexOf("[id]: javascript:first", StringComparison.Ordinal);
        result = ApplyAnalyze(document, session, new TextChange(winnerAt, "[id]: javascript:first".Length, ""));
        Complete(document.Snapshot, result, Copies - 1);
        var secondOffset = Owner.Length + 2 + "[missing]".Length - "[id]".Length;
        var second = Analyze(session, document.Snapshot, [], secondOffset);
        MatchOwner(second, Owner, secondOffset, "[ID]: javascript:second\n\n");
        Assert.True(document.Undo());
        var undone = Analyze(session, document.Snapshot, [], secondOffset);
        Complete(document.Snapshot, undone, Copies - 1);
        MatchOwner(undone, Owner, secondOffset, "[id]: javascript:first\n\n[ID]: javascript:second\n\n");
        Assert.True(document.Redo());
        var redone = Analyze(session, document.Snapshot, [], secondOffset);
        Complete(document.Snapshot, redone, Copies - 1);
        MatchOwner(redone, Owner, secondOffset, "[ID]: javascript:second\n\n");
    }

    /// <summary>A caller that misses an edit must rebuild or refuse, never publish an old URL.</summary>
    [Fact]
    public void Noncontiguous_edit_chain_has_no_stale_semantics()
    {
        var source = Source(Owner, "\n", "[id]: https://safe.example\n\n", Copies);
        using var document = new Document(source);
        using var session = new MarkdownPolicy().CreateSession();
        Complete(document.Snapshot, Analyze(session, document.Snapshot, [], 0), 0);
        document.Apply(new TextChange(6, 1, "b"));
        var before = document.Snapshot;
        var position = before.GetText().IndexOf("https://safe.example", StringComparison.Ordinal);
        var change = new TextChange(position, "https://safe.example".Length, "javascript:bad");
        var after = document.Apply(change);
        var result = Analyze(session, after, [new(before.Version, after.Version, change)], 0);
        if (result.Completeness != AnalysisCompleteness.Complete) { Assert.Null(result.TotalDiagnosticCount); return; }
        Complete(after, result, Copies);
        MatchOwner(result, Owner[..6] + "b" + Owner[7..], 0, "[id]: javascript:bad\n\n");
    }

    /// <summary>Warm Visible may reuse a contained declaration, but must not scan or certify an edit-history gap.</summary>
    [Fact]
    public void Warm_visible_contained_destination_then_gap_respects_semantic_boundary()
    {
        var source = Source(Owner, "\n", "[id]: https://safe.example\n\n", Copies);
        using var document = new Document(source);
        using var session = new MarkdownPolicy().CreateSession();
        Complete(document.Snapshot, Analyze(session, document.Snapshot, [], 0), 0);
        var before = document.Snapshot;
        var position = source.IndexOf("https://safe.example", StringComparison.Ordinal);
        var change = new TextChange(position, "https://safe.example".Length, "javascript:bad");
        var after = document.Apply(change);
        var visibleRequest = new AnalysisRequest(new(6, 1), AnalysisScope.Visible);
        var updated = session.Analyze(after, [new(before.Version, after.Version, change)], visibleRequest);
        Complete(after, updated, Copies);
        MatchOwner(updated, Owner, 0, "[id]: javascript:bad\n\n");
        document.Apply(new TextChange(6, 1, "b"));
        var missed = document.Snapshot;
        var secondChange = new TextChange(7, 1, "c");
        var latest = document.Apply(secondChange);
        var gap = session.Analyze(latest, [new(missed.Version, latest.Version, secondChange)], visibleRequest);
        Assert.Equal(AnalysisCompleteness.Provisional, gap.Completeness);
        Assert.Null(gap.TotalDiagnosticCount);
        var full = Analyze(session, latest, [], 0);
        Complete(latest, full, Copies);
        MatchOwner(full, Owner[..6] + "bc" + Owner[8..], 0, "[id]: javascript:bad\n\n");
    }

    /// <summary>Unsupported local edits revoke global truth immediately; subsequent repair can certify again.</summary>
    [Fact]
    public void Unsupported_visible_edit_and_repair_never_keep_old_certificate()
    {
        using var document = new Document(Source(Owner, "\n", "[id]: https://safe.example\n\n", Copies));
        using var session = new MarkdownPolicy().CreateSession();
        Complete(document.Snapshot, Analyze(session, document.Snapshot, [], 0), 0);
        var before = document.Snapshot;
        var at = Owner.IndexOf("[use]", StringComparison.Ordinal) + 1;
        var change = new TextChange(at, 1, "ü");
        var after = document.Apply(change);
        var partial = session.Analyze(after, [new(before.Version, after.Version, change)],
            new(new(6, 1), AnalysisScope.Visible));
        Assert.Equal(AnalysisCompleteness.Provisional, partial.Completeness);
        Assert.Null(partial.TotalDiagnosticCount);
        Assert.Equal(AnalysisCompleteness.Provisional, Analyze(session, after, [], 0).Completeness);
        var repaired = ApplyAnalyze(document, session, new TextChange(at, 1, "u"));
        Complete(document.Snapshot, repaired, 0);
        MatchOwner(repaired, Owner, 0, "[id]: https://safe.example\n\n");
    }

    /// <summary>Whole-document viewports cannot cause unbounded nodes, source parsing, or native display.</summary>
    [Fact]
    public void Whole_viewport_projection_and_render_remain_bounded()
    {
        using var document = new Document(Source(Owner, "\n", "[id]: https://safe.example\n\n", Copies));
        using var session = new MarkdownPolicy().CreateSession();
        var request = new AnalysisRequest(new(0, document.Snapshot.Length), AnalysisScope.Full);
        var result = session.Analyze(document.Snapshot, [], request);
        Complete(document.Snapshot, result, 0);
        Assert.InRange(Walk(result.Root).Count(), 2, 513);
        Assert.InRange(result.Tokens.Count, 1, 512);
        Assert.InRange(result.Diagnostics.Count, 0, 128);
        Assert.InRange(result.Root.Children.Sum(n => n.Span.Length), 1, 262_144);
        Assert.True(result.Root.Children.Count < Copies);
        var render = ((IRenderFormatSession)session).Render(document.Snapshot, result, request);
        Assert.True(render.Truncated);
        Assert.True(render.Text.Length <= FlowRenderProjection.MaxTextLength);
        Assert.True(render.Runs.Count <= FlowRenderProjection.MaxRuns);
        Assert.True(render.Paragraphs.Count <= FlowRenderProjection.MaxParagraphs);
        Assert.Equal(AnalysisCompleteness.Complete, render.Completeness);
    }

    /// <summary>Cancellation at the publication hook keeps the previous exact snapshot certificate.</summary>
    [Fact]
    public void Canceled_index_publication_preserves_previous_state()
    {
        using var document = new Document(Owner + "\n\n[id]: https://safe.example\n\n");
        var index = new MarkdownReferenceIndex();
        var old = document.Snapshot;
        Assert.True(index.Build(old).Complete);
        var change = new TextChange(6, 1, "b");
        var next = document.Apply(change);
        using var canceled = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => index.Build(next, canceled.Token,
            index.Current, new(old.Version, next.Version, change),
            phase => { if (phase == "before-commit") canceled.Cancel(); }));
        Assert.Same(old, index.Current!.Snapshot);
        Assert.Equal("canceled", index.LastAttempt!.Failure);
        Assert.True(index.Build(next, previous: index.Current, edit: new(old.Version, next.Version, change)).Complete);
        Assert.Same(next, index.Current!.Snapshot);
    }

    /// <summary>Budget exhaustion is a resource refusal, never a globally valid empty document.</summary>
    [Theory]
    [InlineData("retained-accounting")]
    [InlineData("thread-allocation")]
    [InlineData("admission-work")]
    [InlineData("parser-work")]
    public void Index_resource_refusals_do_not_publish(string reason)
    {
        using var document = new Document(Owner + "\n\n[id]: https://safe.example\n\n");
        var limits = reason switch
        {
            "retained-accounting" => new MarkdownReferenceLimits(RetainedBytes: 0),
            "thread-allocation" => new MarkdownReferenceLimits(ThreadAllocatedBytes: 0),
            "admission-work" => new MarkdownReferenceLimits(WorkUnits: 0),
            _ => new MarkdownReferenceLimits(ParserCalls: 0)
        };
        var index = new MarkdownReferenceIndex(limits);
        var refused = index.Build(document.Snapshot);
        Assert.False(refused.Complete);
        Assert.Equal(reason, refused.Failure);
        Assert.Null(index.Current);
        Assert.Null(refused.PublishedVersion);
        Assert.True(new MarkdownReferenceIndex().Build(document.Snapshot).Complete);
    }

    /// <summary>Disposed sessions reject both semantics and rendering rather than exposing retained state.</summary>
    [Fact]
    public void Cancellation_retry_and_disposal_preserve_public_session_contract()
    {
        using var document = new Document(Source(Owner, "\n", "[id]: https://safe.example\n\n", Copies));
        var session = new MarkdownPolicy().CreateSession();
        var result = Analyze(session, document.Snapshot, [], 0);
        var old = document.Snapshot;
        var change = new TextChange(6, 1, "b");
        var next = document.Apply(change);
        VersionedEdit[] chain = [new(old.Version, next.Version, change)];
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var request = new AnalysisRequest(new(0, Owner.Length), AnalysisScope.Full);
        Assert.Throws<OperationCanceledException>(() => session.Analyze(next, chain, request, canceled.Token));
        Complete(old, Analyze(session, old, [], 0), 0);
        Complete(next, session.Analyze(next, chain, request), 0);
        session.Dispose();
        Assert.Throws<ObjectDisposedException>(() => session.Analyze(next, [], request));
        Assert.Throws<ObjectDisposedException>(() => ((IRenderFormatSession)session).Render(old, result, request));
    }

    /// <summary>Constructs isolated bounded real source owners; definition placement is independent of parsing.</summary>
    private static string Source(string owner, string newline, string definitions, int definitionAt)
    {
        var block = owner + newline + newline;
        var source = string.Concat(Enumerable.Repeat(block, definitionAt)) + definitions +
            string.Concat(Enumerable.Repeat(block, Copies - definitionAt));
        Assert.True(source.Length > 16 * 1024 * 1024);
        return source;
    }

    /// <summary>Uses a one-unit viewport strictly inside one owner, independent of owner-start parser logic.</summary>
    private static DocumentAnalysis Analyze(IFormatSession session, TextSnapshot snapshot,
        IReadOnlyList<VersionedEdit> edits, int ownerStart) =>
        session.Analyze(snapshot, edits, new(new(ownerStart + 6, 1), AnalysisScope.Full));

    /// <summary>Supplies the actual engine before/after version chain for one mutation.</summary>
    private static DocumentAnalysis ApplyAnalyze(Document document, IFormatSession session, TextChange change)
    {
        var before = document.Snapshot;
        var after = document.Apply(change);
        return Analyze(session, after, [new(before.Version, after.Version, change)], 0);
    }

    /// <summary>Checks whole-file guarantees separately from visible materialization.</summary>
    private static void Complete(TextSnapshot snapshot, DocumentAnalysis result, int warnings)
    {
        Assert.Equal(snapshot.Version, result.Version);
        Assert.Equal(new TextSpan(0, snapshot.Length), result.Coverage);
        Assert.Equal(AnalysisCompleteness.Complete, result.Completeness);
        Assert.Equal(warnings, result.TotalDiagnosticCount);
    }

    /// <summary>Parses an independently constructed small real-owner context and compares all nested source fields.</summary>
    private static void MatchOwner(DocumentAnalysis actual, string owner, int start, string definitions)
    {
        var oracle = new MarkdownPolicy().Analyze(owner + "\n\n" + definitions);
        var expectedOwner = oracle.Root.Children.Single(n => n.Kind == "paragraph");
        var actualOwner = Assert.Single(actual.Root.Children);
        Assert.Equal(Walk(expectedOwner).Select(n => Signature(n, start)), Walk(actualOwner).Select(n => Signature(n, 0)));
        Assert.Equal(oracle.Tokens.Where(t => t.Span.Start < owner.Length).Select(t => t with { Span = Shift(t.Span, start) }), actual.Tokens);
        Assert.Equal(oracle.Diagnostics.Where(d => d.Span.Start < owner.Length).Select(d => d with { Span = Shift(d.Span, start) }), actual.Diagnostics);
        Assert.Equal(new TextSpan(start, owner.Length), actualOwner.Span);
    }

    /// <summary>Compares real text, inline styles, and source provenance with the existing small-document native renderer.</summary>
    private static void MatchRender(IRenderFormatSession session, TextSnapshot snapshot, DocumentAnalysis analysis,
        TextSpan range, string owner, int start, string definitions)
    {
        using var oracleDocument = new Document(owner + "\n\n" + definitions);
        using var oracleSession = new MarkdownPolicy().CreateSession();
        var request = new AnalysisRequest(new(0, owner.Length), AnalysisScope.Full);
        var oracleAnalysis = oracleSession.Analyze(oracleDocument.Snapshot, [], request);
        var expected = ((IRenderFormatSession)oracleSession).Render(oracleDocument.Snapshot, oracleAnalysis, request);
        var actual = session.Render(snapshot, analysis, new(range, AnalysisScope.Full));
        Assert.Equal(expected.Text, actual.Text);
        Assert.Equal(expected.Runs.Select(r => r with { SourceRange = Shift(r.SourceRange, start) }), actual.Runs);
        Assert.Equal(expected.Paragraphs.Select(p => p with { SourceRange = Shift(p.SourceRange, start) }), actual.Paragraphs);
        Assert.DoesNotContain("[use][id]", actual.Text, StringComparison.Ordinal);
        Assert.Contains(new string('a', 3_000), actual.Text, StringComparison.Ordinal);
    }

    /// <summary>Creates absolute UTF-16 source coordinates without consulting production index geometry.</summary>
    private static TextSpan Shift(TextSpan span, int start) => new(span.Start + start, span.Length);
    /// <summary>Flattens immutable syntax in order to compare nesting as well as outer owner fields.</summary>
    private static IEnumerable<SemanticNode> Walk(SemanticNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Walk(child)) yield return descendant;
    }
    /// <summary>Includes child arity so flattening cannot hide a changed tree shape.</summary>
    private static (string, TextSpan, string?, string?, int) Signature(SemanticNode node, int shift) =>
        (node.Kind, Shift(node.Span, shift), node.Name, node.Value, node.Children.Count);
}
