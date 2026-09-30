using System.Reflection;
using Mote.Engine;
using Mote.Formats;

namespace Mote.Tests;

/// <summary>Format-private certificate tests without enlarging the shipped policy surface for test access.</summary>
public sealed class MarkdownReferenceCertificateTests
{
    /// <summary>All restricted key-presence assignments retain exact current source provenance and real payloads.</summary>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void Masks_and_source_mapped_ballast_match_real_policy(string newline)
    {
        var owners = new[] { "Alpha [a][b] [c][d] End", "## " + new string('X', 4000) + " [a][b] End" };
        foreach (var owner in owners)
        for (var mask = 0; mask < 16; mask++)
        {
            var lines = new List<string> { owner };
            for (var k = 0; k < 4; k++) if ((mask & (1 << k)) != 0)
                lines.Add($"[{(char)('a' + k)}]: {(k % 2 == 0 ? "https://safe.test" : "javascript&#58;bad")}");
            using var document = new Document(string.Join(newline + newline, lines));
            var index = NewIndex(); Assert.True(Complete(Build(index, document.Snapshot)));
            var actual = Project(index, document.Snapshot);
            var oracle = new MarkdownPolicy().Analyze(document.Snapshot.GetText());
            Assert.Equal(oracle.Diagnostics.Count, actual.TotalDiagnosticCount);
            Assert.Equal(oracle.Diagnostics.Select(d => (d.Code, d.Span)), actual.Diagnostics.Select(d => (d.Code, d.Span)));
            Assert.Equal(oracle.Tokens.Select(t => (t.Kind, t.Span)), actual.Tokens.Select(t => (t.Kind, t.Span)));
            Assert.Equal(Signature(oracle.Root.Children.First()), Signature(actual.Root.Children.First()));
        }
    }

    /// <summary>Unsupported shape refusals never publish an approximate whole-document certificate.</summary>
    [Theory]
    [InlineData("Alpha [a][b][c][d] End\n\n[b]: /url")]
    [InlineData("Alpha [a] End\n\n[a]: /url")]
    [InlineData("Alpha ![a][b] End\n\n[b]: /url")]
    [InlineData("Alpha [cafe][id] End\n\n[café]: /url")]
    [InlineData("Alpha [a][b] End\n\n[b]: /url 'title'")]
    [InlineData("Alpha [a][b] End\n\n[b]:\n /url")]
    [InlineData("Alpha [a][b] End\n[b]: /url")]
    [InlineData("Alpha [a][b] End\n\n- List")]
    [InlineData("Alpha [a][b] *bold* End\n\n[b]: /url")]
    public void Restricted_domain_fails_closed(string source)
    {
        using var document = new Document(source); var index = NewIndex();
        var result = Build(index, document.Snapshot);
        Assert.False(Complete(result)); Assert.Null(State(index));
        Assert.Equal("UnsupportedGrammar", Property(result, "FailureKind")!.ToString());
    }

    /// <summary>Each resource axis refuses independently; no axis creates an intrinsic bad-line conclusion.</summary>
    [Theory]
    [InlineData(17000L, 33554432L, 1073741824L, 4096, 4194304L, "retained-accounting")]
    [InlineData(8388608L, 0L, 1073741824L, 4096, 4194304L, "thread-allocation")]
    [InlineData(8388608L, 33554432L, 1L, 4096, 4194304L, "admission-work")]
    [InlineData(8388608L, 33554432L, 1073741824L, 1, 4194304L, "parser-work")]
    [InlineData(8388608L, 33554432L, 1073741824L, 4096, 1L, "parser-work")]
    public void Unified_budget_refuses_before_publication(long retained, long allocated, long work, int calls, long units, string reason)
    {
        using var document = new Document("Alpha [one][guide] End\n\n[guide]: javascript:bad");
        var index = NewIndex(retained, allocated, work, calls, units);
        var attempt = Build(index, document.Snapshot);
        Assert.False(Complete(attempt)); Assert.Equal(reason, Property(attempt, "Failure"));
        Assert.Equal("Budget", Property(attempt, "FailureKind")!.ToString()); Assert.Null(State(index));
    }

    /// <summary>Cancellation at every substantive stage leaves the previously certified snapshot untouched.</summary>
    [Theory]
    [InlineData("scan-line")]
    [InlineData("presence-mask")]
    [InlineData("winner-payload")]
    [InlineData("aggregate-owner")]
    [InlineData("before-commit")]
    [InlineData("publish-arrays")]
    public void Cancellation_preserves_committed_certificate_and_retry(string phase)
    {
        using var document = new Document("Alpha [a][b] End\n\n[b]: /safe"); var index = NewIndex();
        Assert.True(Complete(Build(index, document.Snapshot))); var committed = State(index);
        var after = document.Apply(new TextChange(0, 0, "Beta [e][b] End\n\n"));
        using var cancellation = new CancellationTokenSource();
        var observed = false;
        Action<string> hook = value => { if (value == phase) { observed = true; cancellation.Cancel(); } };
        var failure = Assert.Throws<TargetInvocationException>(() => Build(index, after, cancellation.Token, hook: hook));
        Assert.IsAssignableFrom<OperationCanceledException>(failure.InnerException); Assert.True(observed);
        Assert.Same(committed, State(index));
        Assert.True(Complete(Build(index, after))); Assert.Equal(after.Version, Project(index, after).Version);
    }

    /// <summary>A local destination edit does not rerun consumer masks; removing the winner promotes the real duplicate.</summary>
    [Fact]
    public void Local_winner_change_and_removal_reuse_certified_consumers()
    {
        using var document = new Document("[b]: /safe\n\nAlpha [a][b] End\n\n[B]: javascript:duplicate");
        var index = NewIndex(); Assert.True(Complete(Build(index, document.Snapshot)));
        var before = document.Snapshot; var state = State(index);
        var edit = new TextChange(5, 5, "javascript&#58;changed"); var after = document.Apply(edit);
        var changed = NewIndex(); var attempt = Build(changed, after, previous: state, edit: new(before.Version, after.Version, edit));
        Assert.True(Complete(attempt)); Assert.InRange((int)Property(attempt, "ParserCalls")!, 1, 2);
        Assert.Equal(1, Project(changed, after).TotalDiagnosticCount);
        before = after; state = State(changed); edit = new TextChange(0, after.GetLine(0).Length, ""); after = document.Apply(edit);
        var promoted = NewIndex(); attempt = Build(promoted, after, previous: state, edit: new(before.Version, after.Version, edit));
        Assert.True(Complete(attempt)); Assert.Equal(1, Project(promoted, after).TotalDiagnosticCount);
        var link = Project(promoted, after).Root.Children.Single(n => n.Kind == "paragraph").Children.Single(n => n.Kind == "link");
        Assert.Equal("javascript:duplicate", link.Value);
    }

    /// <summary>Equal numeric versions from distinct documents cannot authorize source maps or presentation.</summary>
    [Fact]
    public void Snapshot_identity_not_numeric_version_is_the_projection_generation()
    {
        using var a = new Document("Alpha [a][b] End\n\n[b]: /safe"); using var b = new Document("Other [a][b] End\n\n[b]: /else");
        var index = NewIndex(); Assert.True(Complete(Build(index, a.Snapshot)));
        Assert.Equal(a.Snapshot.Version, b.Snapshot.Version);
        var failure = Assert.Throws<TargetInvocationException>(() => Project(index, b.Snapshot));
        Assert.IsType<InvalidOperationException>(failure.InnerException);
    }

    /// <summary>A newer reentrant private stage wins; the old stage cannot replace its snapshot or maps.</summary>
    [Fact]
    public void Reentrant_private_stage_cannot_publish_an_older_version()
    {
        using var document = new Document("Alpha [a][b] End\n\n[b]: /safe");
        var older = document.Snapshot;
        var newer = document.Apply(new TextChange(older.Length, 0, "\n\nBeta [a][b] End"));
        var index = NewIndex(); var entered = false;
        Action<string> hook = phase =>
        {
            if (phase != "before-commit" || entered) return;
            entered = true; Assert.True(Complete(Build(index, newer)));
        };
        var outer = Build(index, older, hook: hook);
        Assert.False(Complete(outer)); Assert.Equal("StaleVersion", Property(outer, "FailureKind")!.ToString());
        Assert.True(entered); Assert.Equal(newer.Version, Project(index, newer).Version);
    }

    /// <summary>Refusal memoization must not retain an obsolete giant rope after a current exact commit.</summary>
    [Fact]
    public void Successful_exact_commit_drops_old_refusal_snapshot()
    {
        using var document = new Document(new string('!', 17 * 1024 * 1024));
        using var session = new MarkdownPolicy().CreateSession();
        var rejected = document.Snapshot;
        var result = session.Analyze(rejected, [], new(new TextSpan(0, 8), AnalysisScope.Full));
        Assert.Equal(AnalysisCompleteness.Provisional, result.Completeness);
        var memo = session.GetType().GetField("_referenceRejectedSnapshot", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.Same(rejected, memo.GetValue(session));
        var edit = new TextChange(0, rejected.Length, "small"); var current = document.Apply(edit);
        result = session.Analyze(current, [new(rejected.Version, current.Version, edit)], new(new TextSpan(0, 5), AnalysisScope.Full));
        Assert.Equal(AnalysisCompleteness.Complete, result.Completeness); Assert.Null(memo.GetValue(session));
    }

    /// <summary>Resolves a private type by exact assembly identity; this helper never changes production visibility.</summary>
    private static Type Type(string name) => typeof(MarkdownPolicy).Assembly.GetType("Mote.Formats." + name, true)!;
    /// <summary>Creates deterministic independent resource limits with normal production defaults.</summary>
    private static object NewIndex(long retained = 8388608, long allocated = 33554432, long work = 1073741824,
        int calls = 4096, long units = 4194304)
    {
        var limits = Activator.CreateInstance(Type("MarkdownReferenceLimits"), retained, allocated, work, calls, units)!;
        return Activator.CreateInstance(Type("MarkdownReferenceIndex"), BindingFlags.NonPublic | BindingFlags.Instance, null, [limits], null)!;
    }
    /// <summary>Invokes a complete private stage, with optional real transition and deterministic cancellation hook.</summary>
    private static object Build(object index, TextSnapshot snapshot, CancellationToken ct = default, object? previous = null,
        VersionedEdit? edit = null, Action<string>? hook = null) => index.GetType().GetMethod("Build", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(index, [snapshot, ct, previous, edit, hook, true])!;
    /// <summary>Reads immutable evidence from the private implementation.</summary>
    private static object? Property(object value, string name) => value.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(value);
    /// <summary>Reads the atomically published state, not its last refused attempt.</summary>
    private static object? State(object index) => Property(index, "Current");
    /// <summary>Reads the stage success marker.</summary>
    private static bool Complete(object attempt) => (bool)Property(attempt, "Complete")!;
    /// <summary>Projects all source under the same bounded shared owner budget.</summary>
    private static DocumentAnalysis Project(object index, TextSnapshot snapshot) => (DocumentAnalysis)index.GetType().GetMethod("Project", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(index, [snapshot, new AnalysisRequest(new TextSpan(0, snapshot.Length), AnalysisScope.Full), CancellationToken.None])!;
    /// <summary>Recursively compares externally meaningful semantics without relying on object reference equality.</summary>
    private static string Signature(SemanticNode node) => $"{node.Kind}:{node.Span}:{node.Name}:{node.Value}({string.Join('|', node.Children.Select(Signature))})";
}
