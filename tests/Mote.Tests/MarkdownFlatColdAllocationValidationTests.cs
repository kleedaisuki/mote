using System.Reflection;
using System.Text;
using Mote.Engine;
using Mote.Formats;

namespace Mote.Tests;

/// <summary>Independent whole-parser and lifecycle checks for bounded cold flat certification.</summary>
public sealed class MarkdownFlatColdAllocationValidationTests
{
    /// <summary>Forces certification beyond whole-document sparse parser admission.</summary>
    private const int MinimumLength = 16 * 1024 * 1024 + 4096;

    /// <summary>Repeated certificates and unique Unicode owners retain exact recursive semantics across rope seams.</summary>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Mixed_repeated_unique_unicode_blocks_match_whole_parser(string newline)
    {
        var source = Corpus(newline);
        using var document = new Document(source);
        using var session = new MarkdownPolicy().CreateSession();
        var oracle = new MarkdownPolicy().Analyze(source);
        var seams = new List<int>();
        var cursor = 0;
        foreach (var chunk in document.Snapshot.GetChunks())
        {
            cursor += chunk.Length;
            if (cursor < source.Length) seams.Add(cursor);
        }
        Assert.NotEmpty(seams);
        var probes = new[] { 5, seams[0] - 1, seams[1] - 1, source.Length / 2, source.Length - 64 };
        foreach (var offset in probes)
        {
            var range = new TextSpan(offset, 32);
            var actual = session.Analyze(document.Snapshot, [], new(range, AnalysisScope.Full));
            Complete(document.Snapshot, actual);
            Match(oracle, actual, range);
        }
        if (newline == "\r\n")
            Assert.Contains(seams, seam => source[seam - 1] == '\r' && source[seam] == '\n');
    }

    /// <summary>Buffer growth preserves the admitted 64 Ki body edge and refuses its first extra code unit.</summary>
    [Theory]
    [InlineData("\n", 65_536, true)]
    [InlineData("\r\n", 65_536, true)]
    [InlineData("\n", 65_537, false)]
    [InlineData("\r\n", 65_537, false)]
    public void Maximum_line_body_and_unterminated_final_owner(string newline, int length, bool admitted)
    {
        var prefix = Corpus(newline);
        var source = prefix + "Alpha " + new string('z', length - 6);
        using var document = new Document(source);
        using var session = new MarkdownPolicy().CreateSession();
        var range = new TextSpan(prefix.Length + 1, 8);
        var actual = session.Analyze(document.Snapshot, [], new(range, AnalysisScope.Full));
        if (!admitted) { Provisional(actual); return; }
        Complete(document.Snapshot, actual);
        Match(new MarkdownPolicy().Analyze(source), actual, range);
    }
    /// <summary>A repeated paragraph certificate cannot bypass its current owner's adjacency condition.</summary>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Identical_adjacent_paragraph_owners_are_not_certified(string newline)
    {
        var source = Corpus(newline);
        var paragraph = "Alpha 中文 Élan Ωμέγα １２３ " + new string('a', 32_600);
        source += paragraph + newline + paragraph + newline;
        using var document = new Document(source);
        using var session = new MarkdownPolicy().CreateSession();
        Provisional(session.Analyze(document.Snapshot, [], new(new(0, 32), AnalysisScope.Full)));
    }

    /// <summary>Fence contents remain opaque even when they resemble active references and hostile syntax.</summary>
    [Fact]
    public void Opaque_closed_fence_matches_whole_parser()
    {
        const string fence = "```json\n[id]: javascript:hidden\n[x][id]\n- hostile *markup*\n```\n\n";
        var source = fence + Corpus("\n");
        using var document = new Document(source);
        using var session = new MarkdownPolicy().CreateSession();
        var range = new TextSpan(0, fence.Length - 2);
        var actual = session.Analyze(document.Snapshot, [], new(range, AnalysisScope.Full));
        Complete(document.Snapshot, actual);
        Match(new MarkdownPolicy().Analyze(source), actual, range);
    }

    /// <summary>Cached simple sources cannot turn an unsupported intermediate into complete semantics.</summary>
    [Theory]
    [InlineData("Alpha *emphasis*\n\n")]
    [InlineData("```json\nunclosed\n")]
    [InlineData("Alpha\rBeta\r")]
    [InlineData("Alpha\nAlpha\n\n")]
    public void Hostile_offscreen_intermediates_remain_provisional(string suffix)
    {
        using var document = new Document(Corpus("\n") + suffix);
        using var session = new MarkdownPolicy().CreateSession();
        Provisional(session.Analyze(document.Snapshot, [], new(new(0, 32), AnalysisScope.Full)));
    }

    /// <summary>The explicit reference certificate and dependency-free flat certificate coexist per session.</summary>
    [Fact]
    public void Reference_then_no_reference_routes_match_whole_parser()
    {
        var flat = string.Concat(Enumerable.Repeat("Alpha " + new string('a', 3_000) + "\n\n", 5_600));
        const string reference = "Alpha [use][id]\n\n[id]: https://safe.example\n\n";
        using var document = new Document(flat + reference);
        using var session = new MarkdownPolicy().CreateSession();
        var range = new TextSpan(flat.Length + 3, 8);
        var actual = session.Analyze(document.Snapshot, [], new(range, AnalysisScope.Full));
        Complete(document.Snapshot, actual);
        Match(new MarkdownPolicy().Analyze(flat + reference), actual, range);
        var before = document.Snapshot;
        var change = new TextChange(flat.Length, reference.Length, "");
        var after = document.Apply(change);
        actual = session.Analyze(after, [new(before.Version, after.Version, change)], new(new(0, 32), AnalysisScope.Full));
        Complete(after, actual);
        Match(new MarkdownPolicy().Analyze(flat), actual, new(0, 32));
    }

    /// <summary>Cancellation before entry and during an active cold rebuild leave the old published version intact.</summary>
    [Fact]
    public async Task Cancellation_preserves_old_complete_state_and_allows_retry()
    {
        using var document = new Document("# Old\n\nAlpha old\n");
        using var session = new MarkdownPolicy().CreateSession();
        var old = document.Snapshot;
        Complete(old, session.Analyze(old, [], new(new(0, 8), AnalysisScope.Full)));
        var change = new TextChange(0, old.Length, Corpus("\n"));
        var next = document.Apply(change);
        var edits = new[] { new VersionedEdit(old.Version, next.Version, change) };
        var request = new AnalysisRequest(new(0, 32), AnalysisScope.Full);
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            Assert.Throws<OperationCanceledException>(() => session.Analyze(next, edits, request, canceled.Token));
        }
        var version = session.GetType().GetField("_version", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.Equal(old.Version, version.GetValue(session));
        using var cancellation = new CancellationTokenSource();
        using var entered = new ManualResetEventSlim();
        var active = session.GetType().GetField("_analyzing", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var task = Task.Run(() =>
        {
            entered.Set();
            return session.Analyze(next, edits, request, cancellation.Token);
        });
        entered.Wait();
        // Observe active Analyze rather than depending on an elapsed-time threshold.
        while (!task.IsCompleted && !(bool)active.GetValue(session)!) Thread.Yield();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await task);
        Assert.Equal(old.Version, version.GetValue(session));
        Complete(old, session.Analyze(old, [], new(new(0, 8), AnalysisScope.Full)));
        Complete(next, session.Analyze(next, edits, request));
    }

    /// <summary>Constructs bounded mixed owners with a CRLF deliberately split at the first 16 Ki rope seam.</summary>
    private static string Corpus(string newline)
    {
        var builder = new StringBuilder(MinimumLength + 100_000);
        builder.Append("# ").Append('H', 16_381).Append(newline).Append(newline);
        var repeated = "Alpha 中文 Élan Ωμέγα １２３ " + new string('a', 32_600);
        var index = 0;
        while (builder.Length < MinimumLength)
        {
            builder.Append("# Heading 中文").Append(newline);
            builder.Append(index % 4 == 0 ? $"Unique Élan 中文 {index} " + new string('b', 32_600) : repeated);
            builder.Append(newline).Append(newline);
            index++;
        }
        return builder.ToString();
    }

    /// <summary>Uses the independent whole-document Markdig-backed policy as the semantic oracle.</summary>
    private static void Match(FormatAnalysis oracle, DocumentAnalysis actual, TextSpan range)
    {
        Assert.Equal(oracle.Root.Children.Where(node => node.Span.Start < range.End && node.Span.End >= range.Start)
            .Select(Signature), actual.Root.Children.Select(Signature));
        Assert.Equal(oracle.Tokens.Where(token => token.Span.Start < range.End && token.Span.End >= range.Start)
            .Select(token => (token.Kind, token.Span)), actual.Tokens.Select(token => (token.Kind, token.Span)));
    }

    /// <summary>Includes recursively all semantic fields relevant to rendering and navigation.</summary>
    private static string Signature(SemanticNode node) =>
        $"{node.Kind}:{node.Span.Start}:{node.Span.Length}:{node.Name}:{node.Value}:[{string.Join(',', node.Children.Select(Signature))}]";

    /// <summary>Asserts the public whole-document certificate, not the number of visible nodes.</summary>
    private static void Complete(TextSnapshot snapshot, DocumentAnalysis analysis)
    {
        Assert.Equal(snapshot.Version, analysis.Version);
        Assert.Equal(AnalysisCompleteness.Complete, analysis.Completeness);
        Assert.Equal(new TextSpan(0, snapshot.Length), analysis.Coverage);
        Assert.Equal(0, analysis.TotalDiagnosticCount);
    }

    /// <summary>Unsupported sources must not claim a known whole-file diagnostic total.</summary>
    private static void Provisional(DocumentAnalysis analysis)
    {
        Assert.Equal(AnalysisCompleteness.Provisional, analysis.Completeness);
        Assert.Null(analysis.TotalDiagnosticCount);
    }
}
