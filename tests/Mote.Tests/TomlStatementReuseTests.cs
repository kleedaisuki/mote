using Mote.Engine;
using Mote.Formats;

namespace Mote.Tests;

/// <summary>Retained edit-reuse checks against cold authoritative analysis and literal validity expectations.</summary>
public sealed class TomlStatementReuseTests
{
    private static readonly string Padding = string.Concat(Enumerable.Repeat("#" + new string('x', 4094) + "\n", 1025));

    /// <summary>Changes syntax categories, inline ownership, decoded aliases and logical seams.</summary>
    [Theory]
    [InlineData("a=1\n", "a=[1, {b=2}]\n", true)]
    [InlineData("a=1\n", "a=1979-05-27T07:32:00Z\n", true)]
    [InlineData("a=1\n", "a=1979-05-27\n", true)]
    [InlineData("a=1\n", "a=07:32:00.999\n", true)]
    [InlineData("a=1\n", "a=0xDEAD_BEEF\n", true)]
    [InlineData("a=1\n", "a=-inf\n", true)]
    [InlineData("a=1\n", "a={b=1,b=2}\n", false)]
    [InlineData("a=1\n", "a=\"unterminated\n", false)]
    [InlineData("a=1\n", "a=[1,\n", false)]
    [InlineData("a=1\n", "a=\"\"\"line\nline\"\"\"\"\n", true)]
    [InlineData("a=1\n", "a=\"\"\"line\nline\"\"\"\"\"\n", true)]
    [InlineData("a=1\n", "a='''line\nline'''''\n", true)]
    [InlineData("a=1\n", "\"\\u0061\"=false\n", true)]
    [InlineData("a=1\nb=2\n", "a=1b=2\n", false)]
    [InlineData("a=[1,2]\n", "a=[1,\n2]\n", true)]
    [InlineData("a=\"\"\"one\ntwo\"\"\"\nb=2\n", "a=\"one\"\nb=2\n", true)]
    public void Repaired_result_matches_cold_syntax_and_current_projection(string before, string after, bool valid)
    {
        using var document = new Document(before + Padding + "tail=9\n");
        using var session = NewSession();
        AssertComplete(Analyze(session, document.Snapshot));
        var edit = Apply(document, new(0, before.Length, after));
        var result = Analyze(session, document.Snapshot, [edit]);
        Assert.Equal(valid, result.Completeness == AnalysisCompleteness.Complete);
        Equivalent(Cold(document.Snapshot), result);
        // Independently check the shifted suffix viewport, not just the edited prefix.
        var suffix = new TextSpan(document.Snapshot.Length - 7, 7);
        Equivalent(Cold(document.Snapshot, suffix), Analyze(session, document.Snapshot, [], suffix));
        if (!valid) return;
        Assert.Equal(0, session.LastParsedCharacters);
        Assert.Equal(0, session.LastScannedCharacters);
    }

    /// <summary>Changed namespace effects must revisit an off-screen dependency, including latest array elements.</summary>
    [Theory]
    [InlineData("a=1\n", "b=1\n", "b=2\n", false)]
    [InlineData("\"b\"=1\n", "\"\\u0061\"=1\n", "a=2\n", false)]
    [InlineData("[a]\nx=1\n", "[b]\nx=1\n", "[b]\n", false)]
    [InlineData("[b]\nx=1\n", "a.x=1\n", "[a]\n", false)]
    [InlineData("[[a]]\nx=1\n", "[a]\nx=1\n", "[[a]]\nx=2\n", false)]
    [InlineData("[[a]]\nx=1\n", "[[a]]\ny=1\n", "[a.child]\nx=1\n[[a]]\nx=2\n", true)]
    [InlineData("[[a]]\nx=1\n", "[[a]]\ny=1\n", "[[a.child]]\nx=1\n[[a]]\nx=2\n", true)]
    public void Global_dependencies_are_replayed(string before, string after, string suffix, bool valid)
    {
        using var document = new Document(before + Padding + suffix);
        using var session = NewSession();
        AssertComplete(Analyze(session, document.Snapshot));
        var edit = Apply(document, new(0, before.Length, after));
        var actual = Analyze(session, document.Snapshot, [edit]);
        Equivalent(Cold(document.Snapshot), actual);
        Assert.Equal(valid, actual.Completeness == AnalysisCompleteness.Complete);
        Assert.True(session.LastOwnershipTransitions >= 2);
        if (valid) return;
        var diagnostic = Assert.Single(actual.Diagnostics);
        Assert.Equal("TOML_OWNERSHIP", diagnostic.Code);
        Assert.True(diagnostic.Span.Start >= after.Length + Padding.Length);
    }

    /// <summary>Value changes and different spellings of the same decoded key preserve namespace effects.</summary>
    [Theory]
    [InlineData("a=1\n", "a={b=[1,2]}\n")]
    [InlineData("a=1\n", "\"\\u0061\"=\"text\"\n")]
    [InlineData("a=1\n", "a=1 # comment\n")]
    [InlineData("[a]\nx=1\n", "[\"\\u0061\"]\nx=1\n")]
    public void Identical_effects_skip_ownership_but_validate_changed_syntax(string before, string after)
    {
        using var document = new Document(before + Padding + "tail=9\n");
        using var session = NewSession();
        AssertComplete(Analyze(session, document.Snapshot));
        var edit = Apply(document, new(0, before.Length, after));
        Equivalent(Cold(document.Snapshot), Analyze(session, document.Snapshot, [edit]));
        Assert.InRange(session.LastParsedCharacters, 1, 20_000);
        Assert.InRange(session.LastScannedCharacters, 1, 20_000);
        Assert.Equal(0, session.LastOwnershipTransitions);
    }

    /// <summary>A seam at the start of a statement still includes the preceding newline owner.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Newline_boundary_insert_and_delete_do_not_reuse_invalid_join(bool insert)
    {
        string prefix = "a=1\nb=2\n";
        using var document = new Document(prefix + Padding);
        using var session = NewSession();
        AssertComplete(Analyze(session, document.Snapshot));
        var edit = Apply(document, insert ? new(4, 0, "\n") : new(3, 1, ""));
        var actual = Analyze(session, document.Snapshot, [edit]);
        Equivalent(Cold(document.Snapshot), actual);
        Assert.Equal(insert, actual.Completeness == AnalysisCompleteness.Complete);
    }

    /// <summary>Small mutations inside a multiline owner can consume former statement seams.</summary>
    [Theory]
    [InlineData("a=\"\"\"one\"\"\"\nb=2\n", "\"\"\"\nb", "\"\"\nb", false)]
    [InlineData("a=[1,2]\nb=2\n", "]\nb", ",3]\nb", true)]
    [InlineData("a=[1,2]\nb=2\n", "]\nb", "\nb", false)]
    [InlineData("a=\"\"\"one\"\"\"\nb=2\n", "one", "one\ntwo", true)]
    [InlineData("a='''one'''\nb=2\n", "one", "one\ntwo", true)]
    public void Interior_edits_expand_to_certified_current_seams(string prefix, string oldText, string newText, bool valid)
    {
        using var document = new Document(prefix + Padding + "tail=9\n");
        using var session = NewSession();
        AssertComplete(Analyze(session, document.Snapshot));
        int offset = prefix.IndexOf(oldText, StringComparison.Ordinal);
        Assert.True(offset >= 0);
        var edit = Apply(document, new(offset, oldText.Length, newText));
        var actual = Analyze(session, document.Snapshot, [edit]);
        Equivalent(Cold(document.Snapshot), actual);
        Assert.Equal(valid, actual.Completeness == AnalysisCompleteness.Complete);
    }

    /// <summary>End-of-source insertions map an empty old suffix without inventing a newline seam.</summary>
    [Theory]
    [InlineData("a=1\n", "b=2\n", true)]
    [InlineData("a=1", "\nb=2\n", true)]
    [InlineData("a=1", "b=2\n", false)]
    [InlineData("a=1\n", "a=2\n", false)]
    public void Eof_insertions_preserve_syntax_and_ownership(string suffix, string insert, bool valid)
    {
        using var document = new Document(Padding + suffix);
        using var session = NewSession();
        AssertComplete(Analyze(session, document.Snapshot));
        var edit = Apply(document, new(document.Snapshot.Length, 0, insert));
        var viewport = new TextSpan(Padding.Length, document.Snapshot.Length - Padding.Length);
        var actual = Analyze(session, document.Snapshot, [edit], viewport);
        Equivalent(Cold(document.Snapshot, viewport), actual);
        Assert.Equal(valid, actual.Completeness == AnalysisCompleteness.Complete);
    }

    /// <summary>Removing an entire former owner still checks the namespace of the mapped suffix.</summary>
    [Fact]
    public void Deleted_header_changes_offscreen_assignment_scope()
    {
        const string prefix = "a=1\n[t]\n";
        using var document = new Document(prefix + Padding + "a=2\n");
        using var session = NewSession();
        AssertComplete(Analyze(session, document.Snapshot));
        var edit = Apply(document, new(4, 4, ""));
        var actual = Analyze(session, document.Snapshot, [edit]);
        Equivalent(Cold(document.Snapshot), actual);
        Assert.Equal(new TextSpan(4 + Padding.Length, 1), Assert.Single(actual.Diagnostics).Span);
        Assert.True(session.LastOwnershipTransitions >= 2);
    }

    /// <summary>Version identity never substitutes for document/source identity.</summary>
    [Fact]
    public void Same_version_other_source_requires_full_validation()
    {
        using var first = new Document("a=1\n" + Padding);
        using var other = new Document("a=1\na=2\n" + Padding);
        using var session = NewSession();
        AssertComplete(Analyze(session, first.Snapshot));
        Assert.Equal(first.Snapshot.Version, other.Snapshot.Version);
        var actual = Analyze(session, other.Snapshot);
        Equivalent(Cold(other.Snapshot), actual);
        Assert.Equal("TOML_OWNERSHIP", Assert.Single(actual.Diagnostics).Code);
        Assert.True(session.LastParsedCharacters > 0);
    }

    /// <summary>Missing edits, false payloads/offsets and unsupported multi-edit chains force cold analysis.</summary>
    [Theory]
    [InlineData("missing")]
    [InlineData("payload")]
    [InlineData("offset")]
    [InlineData("outside")]
    [InlineData("multiple")]
    public void Untrusted_history_cannot_certify_reuse(string kind)
    {
        using var document = new Document("a=1\nb=2\n" + Padding + "tail=9\n");
        using var session = NewSession();
        AssertComplete(Analyze(session, document.Snapshot));
        var first = Apply(document, new(2, 1, "3"));
        VersionedEdit[] history = kind switch
        {
            "missing" => [],
            "payload" => [first with { Change = new(2, 1, "4") }],
            "offset" => [first with { Change = new(6, 1, "3") }],
            _ => [first]
        };
        if (kind is "outside" or "multiple")
        {
            var second = Apply(document, new(6, 1, "4"));
            history = kind == "multiple" ? [first, second]
                : [new(first.BeforeVersion, second.AfterVersion, first.Change)];
        }
        var actual = Analyze(session, document.Snapshot, history);
        Equivalent(Cold(document.Snapshot), actual);
        Assert.True(session.LastParsedCharacters > 4 * 1024 * 1024);
    }

    /// <summary>The same immutable snapshot can reproject Complete without rescanning; cold Visible cannot.</summary>
    [Fact]
    public void Warm_visible_projection_is_complete_but_cold_visible_is_not()
    {
        using var document = new Document("a=1\n" + Padding + "tail=9\n");
        using var session = NewSession();
        var viewport = new TextSpan(document.Snapshot.Length - 7, 7);
        using var cold = NewSession();
        Assert.Equal(AnalysisCompleteness.Provisional,
            cold.Analyze(document.Snapshot, [], new(viewport, AnalysisScope.Visible)).Completeness);
        AssertComplete(Analyze(session, document.Snapshot));
        var actual = session.Analyze(document.Snapshot, [], new(viewport, AnalysisScope.Visible));
        Equivalent(Cold(document.Snapshot, viewport), actual);
        Assert.Equal(0, session.LastParsedCharacters);
        Assert.Equal(0, session.LastScannedCharacters);
        Assert.Equal(0, session.LastOwnershipTransitions);
    }

    /// <summary>Undo/redo history comes from engine events, including fresh monotonically increasing versions.</summary>
    [Fact]
    public void Undo_and_redo_match_fresh_current_coordinate_results()
    {
        using var document = new Document("a=1\n" + Padding + "tail=9\n");
        using var session = NewSession();
        VersionedEdit latest = default;
        document.Changed += (_, change) => latest = new(change.Before.Version, change.After.Version, change.Change);
        AssertComplete(Analyze(session, document.Snapshot));
        document.Apply(new(2, 1, "[1,2]"));
        Equivalent(Cold(document.Snapshot), Analyze(session, document.Snapshot, [latest]));
        Assert.True(document.Undo());
        Equivalent(Cold(document.Snapshot), Analyze(session, document.Snapshot, [latest]));
        Assert.True(document.Redo());
        Equivalent(Cold(document.Snapshot), Analyze(session, document.Snapshot, [latest]));
    }

    /// <summary>Cancellation or an exception before publication preserves the old committed certificate.</summary>
    [Theory]
    [InlineData("repair", false)]
    [InlineData("projection", false)]
    [InlineData("repair", true)]
    [InlineData("projection", true)]
    public void Interrupted_staging_preserves_previous_cache(string stage, bool throwException)
    {
        using var document = new Document("a=1\n" + Padding);
        using var session = NewSession();
        var previous = document.Snapshot;
        AssertComplete(Analyze(session, previous));
        var edit = Apply(document, new(2, 1, "2"));
        using var cancellation = new CancellationTokenSource();
        session.AnalysisHook = current =>
        {
            if (current != stage) return;
            if (throwException) throw new InvalidOperationException("test interruption");
            cancellation.Cancel();
        };
        if (throwException)
            Assert.Throws<InvalidOperationException>(() => Analyze(session, document.Snapshot, [edit], ct: cancellation.Token));
        else
            Assert.Throws<OperationCanceledException>(() => Analyze(session, document.Snapshot, [edit], ct: cancellation.Token));
        session.AnalysisHook = null;
        Equivalent(Cold(previous), Analyze(session, previous));
        Assert.Equal(0, session.LastParsedCharacters);
        Equivalent(Cold(document.Snapshot), Analyze(session, document.Snapshot, [edit]));
    }

    /// <summary>Cold rebuild interruption also leaves a prior unrelated-source certificate intact.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Interrupted_full_fallback_preserves_previous_cache(bool throwException)
    {
        using var previous = new Document("a=1\n" + Padding);
        using var next = new Document("b=1\n" + Padding);
        using var session = NewSession();
        AssertComplete(Analyze(session, previous.Snapshot));
        using var cancellation = new CancellationTokenSource();
        session.AnalysisHook = stage =>
        {
            if (stage != "full") return;
            if (throwException) throw new InvalidOperationException("test interruption");
            cancellation.Cancel();
        };
        if (throwException)
            Assert.Throws<InvalidOperationException>(() => Analyze(session, next.Snapshot, ct: cancellation.Token));
        else
            Assert.Throws<OperationCanceledException>(() => Analyze(session, next.Snapshot, ct: cancellation.Token));
        session.AnalysisHook = null;
        AssertComplete(Analyze(session, previous.Snapshot));
        Assert.Equal(0, session.LastParsedCharacters);
    }

    /// <summary>Failed syntax/ownership publication and a small-file switch retire a prior certificate.</summary>
    [Theory]
    [InlineData("syntax")]
    [InlineData("ownership")]
    [InlineData("small")]
    public void Published_noncomplete_or_small_result_clears_large_cache(string kind)
    {
        using var document = new Document("a=1\n" + Padding);
        using var session = NewSession();
        var previous = document.Snapshot;
        AssertComplete(Analyze(session, previous));
        TextChange change = kind switch
        {
            "syntax" => new(0, 4, "a=???\n"),
            "ownership" => new(0, 4, "a=1\na=2\n"),
            _ => new(4, Padding.Length, "")
        };
        var edit = Apply(document, change);
        Equivalent(Cold(document.Snapshot), Analyze(session, document.Snapshot, [edit]));
        AssertComplete(Analyze(session, previous));
        Assert.True(session.LastParsedCharacters > 4 * 1024 * 1024);
    }

    /// <summary>Reentry is rejected rather than mutating staged state; disposal is terminal.</summary>
    [Fact]
    public void Reentry_is_rejected_and_dispose_prevents_future_analysis()
    {
        using var document = new Document("a=1\n" + Padding);
        var session = NewSession();
        try
        {
            int callbacks = 0;
            session.AnalysisHook = stage =>
            {
                callbacks++;
                Assert.Throws<InvalidOperationException>(() => Analyze(session, document.Snapshot));
                Assert.Throws<InvalidOperationException>(() => session.Dispose());
            };
            AssertComplete(Analyze(session, document.Snapshot));
            Assert.True(callbacks >= 2);
            session.Dispose();
            Assert.Throws<ObjectDisposedException>(() => Analyze(session, document.Snapshot));
        }
        finally { session.Dispose(); }
    }

    /// <summary>Creates one isolated per-document session without public policy indirection.</summary>
    private static TomlIncrementalSession NewSession() => new(new TomlPolicy());

    /// <summary>Supplies an actual engine mutation and preserves its before/after coordinates.</summary>
    private static VersionedEdit Apply(Document document, TextChange change)
    {
        long before = document.Snapshot.Version;
        var after = document.Apply(change);
        return new(before, after.Version, change);
    }

    /// <summary>Requests a bounded viewport with authoritative whole-document semantic validation.</summary>
    private static DocumentAnalysis Analyze(TomlIncrementalSession session, TextSnapshot snapshot,
        IReadOnlyList<VersionedEdit>? edits = null, TextSpan? viewport = null, CancellationToken ct = default) =>
        session.Analyze(snapshot, edits ?? [], new(viewport ?? new(0, Math.Min(256, snapshot.Length)), AnalysisScope.Full), ct);

    /// <summary>The oracle has no cache or edit history; small-file lifetime checks use a fresh session.</summary>
    private static DocumentAnalysis Cold(TextSnapshot snapshot, TextSpan? viewport = null)
    {
        using var session = NewSession();
        return Analyze(session, snapshot, viewport: viewport);
    }

    /// <summary>Checks full certificate semantics, not merely an enum label.</summary>
    private static void AssertComplete(DocumentAnalysis result)
    {
        Assert.Equal(AnalysisCompleteness.Complete, result.Completeness);
        Assert.Equal(0, result.TotalDiagnosticCount);
        Assert.Empty(result.Diagnostics);
    }

    /// <summary>Compares every externally visible fact, including ordered nodes and absolute UTF-16 spans.</summary>
    private static void Equivalent(DocumentAnalysis expected, DocumentAnalysis actual)
    {
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.Coverage, actual.Coverage);
        Assert.Equal(expected.Completeness, actual.Completeness);
        Assert.Equal(expected.TotalDiagnosticCount, actual.TotalDiagnosticCount);
        Assert.Equal(expected.Diagnostics, actual.Diagnostics);
        Assert.Equal(expected.Tokens, actual.Tokens);
        EqualNode(expected.Root, actual.Root);
    }

    /// <summary>Semantic nodes intentionally have reference equality, so compare their contract recursively.</summary>
    private static void EqualNode(SemanticNode expected, SemanticNode actual)
    {
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.Span, actual.Span);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Value, actual.Value);
        Assert.Equal(expected.Children.Count, actual.Children.Count);
        for (int i = 0; i < expected.Children.Count; i++) EqualNode(expected.Children[i], actual.Children[i]);
    }
}
