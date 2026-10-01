using System.Text;
using System.Text.Json;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mote.Engine;
using Mote.Formats;

namespace Mote.Tests;

/// <summary>Production root-array reuse contracts, with independent syntax and duplicate expectations.</summary>
public sealed class JsonArrayCertificateTests
{
    private const int Records = 2_400;

    /// <summary>Cold interactive requests cannot claim global knowledge outside their bounded scan.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cold_large_visible_is_provisional_even_when_requested_near_eof(bool nearEnd)
    {
        using var document = new Document(Corpus());
        using var session = NewSession();
        var start = nearEnd ? document.Snapshot.Length - 200 : 0;
        var result = Visible(session, document.Snapshot, [], start);
        Assert.Equal(AnalysisCompleteness.Provisional, result.Completeness);
        Assert.Null(result.TotalDiagnosticCount);
        Assert.Equal(document.Snapshot.Version, result.Version);
        Assert.InRange(Metric<long>(session, "LastVisitedUnits"), 0, 256 * 1024);
    }

    /// <summary>Decoded-equivalent keys collide within objects, never across root-array elements.</summary>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Full_then_contiguous_nested_edits_preserve_exact_offscreen_counts_and_absolute_spans(string newline)
    {
        using var document = new Document(Corpus(newline));
        using var syntax = JsonDocument.Parse(document.Snapshot.GetText());
        Assert.Equal(Records, syntax.RootElement.GetArrayLength());
        using var session = NewSession();
        var initial = Full(session, document.Snapshot, 0);
        AssertComplete(initial, document.Snapshot, Records);
        var at = document.Snapshot.GetText().IndexOf("\"nested\":", StringComparison.Ordinal) + 9;
        var edit = Apply(document, at, 1, "[true,{\"x\":1,\"\\u0078\":2}]");
        var changed = Visible(session, document.Snapshot, [edit], 0);
        AssertComplete(changed, document.Snapshot, Records + 1);
        Assert.InRange(Metric<long>(session, "LastVisitedUnits"), 0, 512 * 1024);
        AssertProjectionMatchesFull(changed, document.Snapshot, 0);

        // Each edit uses its own before-version coordinates; growth in the first edit shifts the second.
        var before = document.Snapshot;
        var keyAt = before.GetText().IndexOf("\\u0061", StringComparison.Ordinal);
        var first = Apply(document, keyAt, 6, "different");
        var secondAt = document.Snapshot.GetText().IndexOf("\"nested\":", StringComparison.Ordinal) + 9;
        var second = Apply(document, secondAt, 0, " ");
        var result = Visible(session, document.Snapshot, [first, second], 0);
        AssertComplete(result, document.Snapshot, Records);
        AssertProjectionMatchesFull(result, document.Snapshot, 0);
        var away = document.Snapshot.Length - 400;
        AssertProjectionMatchesFull(Visible(session, document.Snapshot, [], away), document.Snapshot, away);
        AssertProjectionMatchesFull(Visible(session, document.Snapshot, [], 0), document.Snapshot, 0);
    }

    /// <summary>A quote repair regains completeness without accepting an unterminated string across seams.</summary>
    [Fact]
    public void Invalid_quote_delete_then_repair_restores_exact_certificate()
    {
        using var document = new Document(Corpus());
        using var session = NewSession();
        AssertComplete(Full(session, document.Snapshot, 0), document.Snapshot, Records);
        var at = document.Snapshot.GetText().IndexOf("\"payload\":\"", StringComparison.Ordinal) + 10;
        var removed = Apply(document, at, 1, "");
        var invalid = Visible(session, document.Snapshot, [removed], 0);
        Assert.Equal(AnalysisCompleteness.Provisional, invalid.Completeness);
        Assert.Null(invalid.TotalDiagnosticCount);
        var repaired = Apply(document, at, 0, "\"");
        var result = Visible(session, document.Snapshot, [repaired], 0);
        AssertComplete(result, document.Snapshot, Records);
        AssertProjectionMatchesFull(result, document.Snapshot, 0);
    }

    /// <summary>A lost version chain is not permission to use stale counts or shifted source spans.</summary>
    [Fact]
    public void Missing_edit_chain_revokes_visible_reuse_until_authoritative_full()
    {
        using var document = new Document(Corpus());
        using var session = NewSession();
        Full(session, document.Snapshot, 0);
        var at = document.Snapshot.GetText().IndexOf("\"nested\":", StringComparison.Ordinal) + 9;
        Apply(document, at, 0, " ");
        var result = Visible(session, document.Snapshot, [], 0);
        Assert.Equal(AnalysisCompleteness.Provisional, result.Completeness);
        Assert.Null(result.TotalDiagnosticCount);
        AssertComplete(Full(session, document.Snapshot, 0), document.Snapshot, Records);
    }

    /// <summary>Shell and cross-owner edits require a rebuild even if the final text happens to be valid.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Shell_or_cross_page_edit_revokes_visible_reuse(bool crossPage)
    {
        using var document = new Document(Corpus());
        using var session = NewSession();
        Full(session, document.Snapshot, 0);
        var edit = crossPage
            ? Apply(document, 1, 140_000, document.Snapshot.GetText(1, 140_000))
            : Apply(document, 0, 0, " ");
        var result = Visible(session, document.Snapshot, [edit], 0);
        Assert.Equal(AnalysisCompleteness.Provisional, result.Completeness);
        Assert.Null(result.TotalDiagnosticCount);
        AssertComplete(Full(session, document.Snapshot, 0), document.Snapshot, Records);
    }

    /// <summary>Full retains its established authoritative domain outside reusable root arrays.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Other_large_roots_remain_authoritative_under_explicit_full(bool objectRoot)
    {
        var payload = new string('x', 1_100_000);
        var source = objectRoot ? "{\"a\":1,\"\\u0061\":2,\"payload\":\"" + payload + "\"}" : "\"" + payload + "\"";
        using var document = new Document(source);
        using var session = NewSession();
        AssertComplete(Full(session, document.Snapshot, 0), document.Snapshot, objectRoot ? 1 : 0);
    }

    /// <summary>A giant owner may be fully validated but cannot be synchronously repaired interactively.</summary>
    [Fact]
    public void Oversized_element_edit_is_provisional_and_full_can_rebuild()
    {
        using var document = new Document("[\"" + new string('x', 1_100_000) + "\",{\"a\":1,\"a\":2}]");
        using var session = NewSession();
        AssertComplete(Full(session, document.Snapshot, 0), document.Snapshot, 1);
        var edit = Apply(document, 100, 1, "y");
        var result = Visible(session, document.Snapshot, [edit], 0);
        Assert.Equal(AnalysisCompleteness.Provisional, result.Completeness);
        Assert.Null(result.TotalDiagnosticCount);
        Assert.InRange(Metric<long>(session, "LastVisitedUnits"), 0, 512 * 1024);
        AssertComplete(Full(session, document.Snapshot, 0), document.Snapshot, 1);
    }

    /// <summary>Exact seams are not treated as ordinary local ownership, even for harmless whitespace.</summary>
    [Fact]
    public void Exact_page_seam_insertion_revokes_reuse()
    {
        using var document = new Document(Corpus());
        using var session = NewSession();
        Full(session, document.Snapshot, 0);
        var pages = Metric<IReadOnlyList<TextSpan>>(session, "ArrayPageSpans");
        Assert.True(pages.Count > 1);
        Assert.InRange(pages.Count, 2, 64);
        var edit = Apply(document, pages[1].Start, 0, " ");
        var result = Visible(session, document.Snapshot, [edit], pages[1].Start);
        Assert.Equal(AnalysisCompleteness.Provisional, result.Completeness);
        Assert.Null(result.TotalDiagnosticCount);
        AssertComplete(Full(session, document.Snapshot, 0), document.Snapshot, Records);
    }

    /// <summary>Many primitive elements retain source-sized pages, not one entry per value.</summary>
    [Fact]
    public void Compact_primitive_array_has_bounded_page_count_and_warm_work()
    {
        using var document = new Document("[" + string.Join(',', Enumerable.Repeat("0", 600_000)) + "]");
        using var session = NewSession();
        AssertComplete(Full(session, document.Snapshot, 0), document.Snapshot, 0);
        Assert.InRange(Metric<int>(session, "ArrayPageCount"), 2, 32);
        var edit = Apply(document, 101, 1, "[true,null,{\"a\":1,\"a\":2}]");
        AssertComplete(Visible(session, document.Snapshot, [edit], 80), document.Snapshot, 1);
        Assert.InRange(Metric<long>(session, "LastVisitedUnits"), 1, 512 * 1024);
    }

    /// <summary>Undo and Redo are new contiguous versions, not a return to an old certificate identity.</summary>
    [Fact]
    public void Undo_redo_restore_exact_counts_using_new_versions()
    {
        using var document = new Document(Corpus());
        using var session = NewSession();
        Full(session, document.Snapshot, 0);
        var at = document.Snapshot.GetText().IndexOf("\\u0061", StringComparison.Ordinal);
        var edit = Apply(document, at, 6, "other");
        AssertComplete(Visible(session, document.Snapshot, [edit], 0), document.Snapshot, Records - 1);
        var beforeUndo = document.Snapshot.Version;
        Assert.True(document.Undo());
        var undo = new VersionedEdit(beforeUndo, document.Snapshot.Version, new TextChange(at, 5, "\\u0061"));
        AssertComplete(Visible(session, document.Snapshot, [undo], 0), document.Snapshot, Records);
        var beforeRedo = document.Snapshot.Version;
        Assert.True(document.Redo());
        var redo = new VersionedEdit(beforeRedo, document.Snapshot.Version, new TextChange(at, 6, "other"));
        AssertComplete(Visible(session, document.Snapshot, [redo], 0), document.Snapshot, Records - 1);
    }

    /// <summary>Deterministic cancellation before page repair or publication cannot mutate committed state.</summary>
    [Theory]
    [InlineData("page")]
    [InlineData("before-commit")]
    public void Mid_analysis_cancellation_keeps_certificate_atomic(string cancelStage)
    {
        using var document = new Document(Corpus());
        using var session = NewSession();
        Full(session, document.Snapshot, 0);
        var committedVersion = Metric<long?>(session, "ArrayCertificateVersion");
        var at = document.Snapshot.GetText().IndexOf("\\u0061", StringComparison.Ordinal);
        var edit = Apply(document, at, 6, "other");
        using var canceled = new CancellationTokenSource();
        var hit = false;
        var hook = session.GetType().GetProperty("AnalysisHook", BindingFlags.Instance | BindingFlags.NonPublic)!;
        hook.SetValue(session, (Action<string>)(stage =>
        {
            if (stage != cancelStage) return;
            hit = true;
            canceled.Cancel();
        }));
        try
        {
            Assert.Throws<OperationCanceledException>(() => session.Analyze(document.Snapshot, [edit],
                new AnalysisRequest(new TextSpan(0, 200), AnalysisScope.Visible), canceled.Token));
        }
        finally { hook.SetValue(session, null); }
        Assert.True(hit, $"Cancellation stage {cancelStage} must actually be exercised.");
        Assert.Equal(committedVersion, Metric<long?>(session, "ArrayCertificateVersion"));
        Assert.Equal(0, Metric<int>(session, "ArrayDirtyPageCount"));
        AssertComplete(Visible(session, document.Snapshot, [edit], 0), document.Snapshot, Records - 1);
    }

    /// <summary>A canceled candidate must not advance committed version or consume its edit chain.</summary>
    [Fact]
    public void Canceled_edit_does_not_publish_state_and_same_chain_can_be_retried()
    {
        using var document = new Document(Corpus());
        using var session = NewSession();
        Full(session, document.Snapshot, 0);
        var at = document.Snapshot.GetText().IndexOf("\\u0061", StringComparison.Ordinal);
        var edit = Apply(document, at, 6, "other");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => session.Analyze(document.Snapshot, [edit],
            new AnalysisRequest(new TextSpan(0, 200), AnalysisScope.Visible), canceled.Token));
        var result = Visible(session, document.Snapshot, [edit], 0);
        AssertComplete(result, document.Snapshot, Records - 1);
        AssertProjectionMatchesFull(result, document.Snapshot, 0);
    }

    /// <summary>Dispersed edits exercise mapping in different owners and compare every Complete to authority.</summary>
    [Fact]
    public void One_hundred_seeded_distributed_edits_match_fresh_full_projection()
    {
        using var document = new Document(Corpus("\r\n"));
        using var session = NewSession();
        Full(session, document.Snapshot, 0);
        var source = document.Snapshot.GetText();
        var positions = new List<int>();
        for (var at = 0; (at = source.IndexOf("\"nested\":0", at, StringComparison.Ordinal)) >= 0; at += 10)
            positions.Add(at + 9);
        var random = new Random(64517);
        for (var i = 0; i < 100; i++)
        {
            var at = positions[random.Next(positions.Count)];
            var old = document.Snapshot.GetText(at, 1);
            var edit = Apply(document, at, 1, old == "0" ? "1" : "0");
            var start = Math.Max(0, at - 30);
            var actual = Visible(session, document.Snapshot, [edit], start);
            AssertComplete(actual, document.Snapshot, Records);
            Assert.InRange(Metric<long>(session, "LastVisitedUnits"), 1, 512 * 1024);
            AssertProjectionMatchesFull(actual, document.Snapshot, start);
        }
        using var syntax = JsonDocument.Parse(document.Snapshot.GetText());
        Assert.Equal(Records, syntax.RootElement.GetArrayLength());
    }

    /// <summary>Equal version numbers from unrelated documents cannot authorize source-summary reuse.</summary>
    [Fact]
    public void Same_version_different_snapshot_identity_revokes_certificate()
    {
        using var original = new Document(Corpus());
        using var replacement = new Document(Corpus().Replace("\\u0061", "another", StringComparison.Ordinal));
        using var session = NewSession();
        Full(session, original.Snapshot, 0);
        Assert.Equal(original.Snapshot.Version, replacement.Snapshot.Version);
        var actual = Visible(session, replacement.Snapshot, [], 0);
        Assert.Equal(AnalysisCompleteness.Provisional, actual.Completeness);
        Assert.Null(actual.TotalDiagnosticCount);
        AssertComplete(Full(session, replacement.Snapshot, 0), replacement.Snapshot, 0);
    }

    /// <summary>Two invalid owners retain separate dirty seams and regain exact truth only when both repair.</summary>
    [Fact]
    public void Multiple_dirty_pages_require_all_repairs_before_complete()
    {
        using var document = new Document(Corpus());
        using var session = NewSession();
        Full(session, document.Snapshot, 0);
        var source = document.Snapshot.GetText();
        var first = source.IndexOf("\"payload\":\"", StringComparison.Ordinal) + 10;
        var second = source.IndexOf("\"payload\":\"", 700_000, StringComparison.Ordinal) + 10;
        var removeFirst = Apply(document, first, 1, "");
        Assert.Equal(AnalysisCompleteness.Provisional, Visible(session, document.Snapshot, [removeFirst], 0).Completeness);
        var removeSecond = Apply(document, second - 1, 1, "");
        Assert.Equal(AnalysisCompleteness.Provisional, Visible(session, document.Snapshot, [removeSecond], second - 100).Completeness);
        Assert.Equal(2, Metric<int>(session, "ArrayDirtyPageCount"));
        var restoreFirst = Apply(document, first, 0, "\"");
        var stillDirty = Visible(session, document.Snapshot, [restoreFirst], 0);
        Assert.Equal(AnalysisCompleteness.Provisional, stillDirty.Completeness);
        Assert.Null(stillDirty.TotalDiagnosticCount);
        Assert.Equal(1, Metric<int>(session, "ArrayDirtyPageCount"));
        var restoreSecond = Apply(document, second, 0, "\"");
        var actual = Visible(session, document.Snapshot, [restoreSecond], second - 100);
        AssertComplete(actual, document.Snapshot, Records);
        AssertProjectionMatchesFull(actual, document.Snapshot, second - 100);
        using var syntax = JsonDocument.Parse(document.Snapshot.GetText());
        Assert.Equal(Records, syntax.RootElement.GetArrayLength());
    }

    /// <summary>Local replacement may change the number of root elements without changing outer page seams.</summary>
    [Fact]
    public void Whole_element_replacement_with_multiple_values_updates_local_counts()
    {
        using var document = new Document(Corpus());
        using var session = NewSession();
        Full(session, document.Snapshot, 0);
        var source = document.Snapshot.GetText();
        var start = source.IndexOf('{');
        var end = source.IndexOf("\"}\n,", StringComparison.Ordinal);
        // The LF corpus separates values with comma before the following newline.
        if (end < 0) end = source.IndexOf("\"},\n", StringComparison.Ordinal);
        Assert.True(end > start);
        var edit = Apply(document, start, end + 2 - start, "true,{\"q\":1,\"\\u0071\":2},[null,\"\\uD83D\\uDE00\"]");
        var actual = Visible(session, document.Snapshot, [edit], 0);
        // Editing the first page's exact start is deliberately a seam, so choose an interior record below instead.
        Assert.Equal(AnalysisCompleteness.Provisional, actual.Completeness);
        AssertComplete(Full(session, document.Snapshot, 0), document.Snapshot, Records);
        var secondStart = document.Snapshot.GetText().IndexOf("{\"a\":", StringComparison.Ordinal);
        var secondEnd = document.Snapshot.GetText().IndexOf("\"},\n", secondStart, StringComparison.Ordinal);
        var interior = Apply(document, secondStart, secondEnd + 2 - secondStart, "true,{\"q\":1,\"q\":2},false");
        var result = Visible(session, document.Snapshot, [interior], Math.Max(0, secondStart - 10));
        AssertComplete(result, document.Snapshot, Records);
        AssertProjectionMatchesFull(result, document.Snapshot, Math.Max(0, secondStart - 10));
        using var syntax = JsonDocument.Parse(document.Snapshot.GetText());
        Assert.Equal(Records + 4, syntax.RootElement.GetArrayLength());
    }

    /// <summary>The final-owner grammar never admits a trailing comma as an exact reusable array.</summary>
    [Fact]
    public void Final_page_trailing_comma_is_provisional_until_removed()
    {
        using var document = new Document(Corpus());
        using var session = NewSession();
        Full(session, document.Snapshot, 0);
        var at = document.Snapshot.Length - 2;
        var add = Apply(document, at, 0, ",");
        var invalid = Visible(session, document.Snapshot, [add], at - 100);
        Assert.Equal(AnalysisCompleteness.Provisional, invalid.Completeness);
        Assert.Null(invalid.TotalDiagnosticCount);
        Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(document.Snapshot.GetText()));
        var repair = Apply(document, at, 1, "");
        AssertComplete(Visible(session, document.Snapshot, [repair], at - 100), document.Snapshot, Records);
    }

    /// <summary>One hundred actual seam/shell attacks must revoke proof, including valid no-op replacements.</summary>
    [Fact]
    public void One_hundred_directed_boundary_edits_never_reuse_invalid_seams()
    {
        using var document = new Document(Corpus("\r\n"));
        using var session = NewSession();
        for (var i = 0; i < 100; i++)
        {
            Full(session, document.Snapshot, 0);
            var pages = Metric<IReadOnlyList<TextSpan>>(session, "ArrayPageSpans");
            var at = i % 4 == 0 ? 0 : pages[1 + i % (pages.Count - 1)].Start;
            var mode = i % 4;
            var edit = Apply(document, at, mode >= 2 ? 1 : 0, mode == 2 ? "}" : mode == 3 ? "{" : " ");
            var actual = Visible(session, document.Snapshot, [edit], Math.Max(0, at - 20));
            Assert.Equal(AnalysisCompleteness.Provisional, actual.Completeness);
            Assert.Null(actual.TotalDiagnosticCount);
            if (mode == 2)
                Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(document.Snapshot.GetText()));
            else
            {
                using var syntax = JsonDocument.Parse(document.Snapshot.GetText());
                Assert.Equal(Records, syntax.RootElement.GetArrayLength());
            }
            var before = document.Snapshot.Version;
            Assert.True(document.Undo());
            // An omitted undo chain must not resurrect the retired certificate either.
            Assert.True(document.Snapshot.Version > before);
        }
    }

    /// <summary>A thousand seeded grammar variants validate both syntax and exact incremental projection.</summary>
    [Fact]
    public void Thousand_seeded_nested_value_edits_match_syntax_and_full_oracles()
    {
        const int width = 80;
        var source = Corpus("\r\n").Replace("\"nested\":0", "\"nested\":" + "0".PadRight(width), StringComparison.Ordinal);
        using var document = new Document(source);
        using var session = NewSession();
        Full(session, document.Snapshot, 0);
        var positions = new List<int>();
        for (var at = 0; (at = source.IndexOf("\"nested\":", at, StringComparison.Ordinal)) >= 0; at += 10)
            positions.Add(at + 9);
        string[] values = ["0", "-12.5e+2", "true", "null", "[]", "[1,false,{\"k\":2}]",
            "\"\\uD83D\\uDE00 ,]} \\\" \\\\\"", "{\"q\":1,\"\\u0071\":2}"];
        var extraCounts = new int[Records];
        var random = new Random(64517);
        for (var i = 0; i < 1_000; i++)
        {
            var record = random.Next(Records);
            var value = random.Next(values.Length);
            extraCounts[record] = value == values.Length - 1 ? 1 : 0;
            var at = positions[record];
            var edit = Apply(document, at, width, values[value].PadRight(width));
            var start = Math.Max(0, at - 20);
            var actual = Visible(session, document.Snapshot, [edit], start);
            AssertComplete(actual, document.Snapshot, Records + extraCounts.Sum());
            Assert.InRange(Metric<long>(session, "LastVisitedUnits"), 1, 512 * 1024);
            AssertProjectionMatchesFull(actual, document.Snapshot, start);
            using var syntax = JsonDocument.Parse(document.Snapshot.GetText());
            Assert.Equal(Records, syntax.RootElement.GetArrayLength());
        }
    }

    /// <summary>A live certified session does not keep historic snapshot objects alive solely for key reuse.</summary>
    [Fact]
    public void Certified_session_does_not_strongly_retain_historic_snapshots()
    {
        using var session = NewSession();
        var references = AnalyzeDisposableHistory(session);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.All(references, reference => Assert.False(reference.TryGetTarget(out _)));
        Assert.True(Metric<int>(session, "ArrayPageCount") > 0);
        GC.KeepAlive(session);
    }

    /// <summary>Owner cuts follow complete grammar tokens, not the nominal 64-Ki source coordinate.</summary>
    [Theory]
    [InlineData("quote")]
    [InlineData("surrogate")]
    [InlineData("number")]
    public void Escapes_and_numbers_straddling_nominal_page_cut_preserve_exact_semantics(string kind)
    {
        string prefix;
        string replacement;
        int at;
        int length;
        if (kind == "number")
        {
            prefix = "[\"" + new string('x', 65_515) + "\",1234567890123456789012345678901234567890,";
            at = 65_536;
            length = 1;
            replacement = "8";
        }
        else
        {
            var escape = kind == "quote" ? "\\\"" : "\\uD83D\\uDE00";
            prefix = "[\"" + new string('x', 65_533) + escape + "tail\",";
            at = 65_535;
            length = escape.Length;
            replacement = kind == "quote" ? "\\\\" : "\\u0061\\u0062";
        }
        var ordinary = Corpus();
        using var document = new Document(prefix + ordinary[1..]);
        using var session = NewSession();
        Full(session, document.Snapshot, at - 20);
        var edit = Apply(document, at, length, replacement);
        var actual = Visible(session, document.Snapshot, [edit], at - 20);
        AssertComplete(actual, document.Snapshot, Records);
        AssertProjectionMatchesFull(actual, document.Snapshot, at - 20);
        using var syntax = JsonDocument.Parse(document.Snapshot.GetText());
        Assert.Equal(Records + (kind == "number" ? 2 : 1), syntax.RootElement.GetArrayLength());
    }

    /// <summary>A non-inlined frame isolates snapshot lifetimes from JIT local-variable retention.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<TextSnapshot>[] AnalyzeDisposableHistory(IFormatSession session)
    {
        using var document = new Document(Corpus());
        var initial = document.Snapshot;
        Full(session, initial, 0);
        var at = initial.GetText().IndexOf("\"nested\":", StringComparison.Ordinal) + 9;
        var edit = Apply(document, at, 1, "1");
        var current = document.Snapshot;
        AssertComplete(Visible(session, current, [edit], 0), current, Records);
        return [new WeakReference<TextSnapshot>(initial), new WeakReference<TextSnapshot>(current)];
    }

    /// <summary>Creates complete records containing nested structure and string delimiters unrelated to seams.</summary>
    private static string Corpus(string newline = "\n")
    {
        var record = "{\"a\":1,\"\\u0061\":2,\"nested\":0,\"payload\":\"escaped \\\" ,]} 中文 😀 " + new string('x', 480) + "\"}";
        var builder = new StringBuilder("[" + newline);
        for (var i = 0; i < Records; i++)
        {
            if (i != 0) builder.Append(',').Append(newline);
            builder.Append(record);
        }
        builder.Append(newline).Append(']');
        Assert.True(builder.Length > 1024 * 1024);
        return builder.ToString();
    }

    /// <summary>Uses the public production policy, not a test parser or bracket-wrapped page.</summary>
    private static IFormatSession NewSession() =>
        ((IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Json)).CreateSession();

    /// <summary>Reads test-only instrumentation without adding a public policy contract.</summary>
    private static T Metric<T>(IFormatSession session, string name) =>
        (T)session.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;

    /// <summary>Captures the exact before/after version relation of a source edit.</summary>
    private static VersionedEdit Apply(Document document, int start, int delete, string insert)
    {
        var before = document.Snapshot.Version;
        var change = new TextChange(start, delete, insert);
        var after = document.Apply(change);
        return new VersionedEdit(before, after.Version, change);
    }

    /// <summary>Requests a small absolute viewport so global ownership is separate from retained projection.</summary>
    private static DocumentAnalysis Visible(IFormatSession session, TextSnapshot snapshot,
        IReadOnlyList<VersionedEdit> edits, int start) => session.Analyze(snapshot, edits,
            new AnalysisRequest(new TextSpan(start, Math.Min(200, snapshot.Length - start)), AnalysisScope.Visible));

    /// <summary>Requests authoritative validation using the same projection bounds as interactive calls.</summary>
    private static DocumentAnalysis Full(IFormatSession session, TextSnapshot snapshot, int start) =>
        session.Analyze(snapshot, [], new AnalysisRequest(new TextSpan(start, Math.Min(200, snapshot.Length - start)), AnalysisScope.Full));

    /// <summary>Checks independently counted global errors and current full-document coverage.</summary>
    private static void AssertComplete(DocumentAnalysis result, TextSnapshot snapshot, int count)
    {
        Assert.Equal(AnalysisCompleteness.Complete, result.Completeness);
        Assert.Equal(count, result.TotalDiagnosticCount);
        Assert.Equal(snapshot.Version, result.Version);
        Assert.Equal(new TextSpan(0, snapshot.Length), result.Coverage);
        Assert.Equal(new TextSpan(0, snapshot.Length), result.Root.Span);
    }

    /// <summary>Compares incremental publication against a fresh authoritative production scan.</summary>
    private static void AssertProjectionMatchesFull(DocumentAnalysis actual, TextSnapshot snapshot, int start)
    {
        using var fresh = NewSession();
        var expected = Full(fresh, snapshot, start);
        Assert.Equal(expected.Completeness, actual.Completeness);
        Assert.Equal(expected.TotalDiagnosticCount, actual.TotalDiagnosticCount);
        Assert.Equal(expected.Diagnostics, actual.Diagnostics);
        Assert.Equal(expected.Tokens, actual.Tokens);
        Assert.Equal(Shape(expected.Root), Shape(actual.Root));
    }

    /// <summary>Includes values and absolute UTF-16 spans, catching stale mapping and wrong depth context.</summary>
    private static string[] Shape(SemanticNode node) =>
        new[] { $"{node.Kind}|{node.Name}|{node.Value}|{node.Span.Start}|{node.Span.Length}" }
            .Concat(node.Children.SelectMany(Shape)).ToArray();
}
