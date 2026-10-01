using Mote.Engine;
using Mote.Formats;

namespace Mote.Tests;

/// <summary>Versioned session contracts independent of the legacy whole-string API.</summary>
public sealed class IncrementalPolicyTests
{
    /// <summary>Plain text can assert complete semantics without copying any source string.</summary>
    [Fact]
    public void Plain_session_reports_complete_absolute_coverage_for_visible_request()
    {
        using var document = new Document("a😀\r\nb");
        var policy = Assert.IsAssignableFrom<IIncrementalDocumentPolicy>(
            DocumentPolicies.ForKind(DocumentKind.PlainText));
        using var session = policy.CreateSession();
        var snapshot = document.Snapshot;
        var result = session.Analyze(snapshot, [],
            new AnalysisRequest(new TextSpan(2, 1), AnalysisScope.Visible));
        Assert.Equal(snapshot.Version, result.Version);
        Assert.Equal(new TextSpan(0, snapshot.Length), result.Coverage);
        Assert.Equal(AnalysisCompleteness.Complete, result.Completeness);
        Assert.Equal(0, result.TotalDiagnosticCount);
        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.Tokens);
        Assert.Equal(snapshot.Length, result.Root.Span.Length);
    }

    /// <summary>Cancellation leaves the session usable; a history gap rebuilds from authority.</summary>
    [Fact]
    public void Plain_session_cancellation_and_edit_gap_do_not_publish_stale_version()
    {
        using var document = new Document("abc");
        using var session = ((IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.PlainText)).CreateSession();
        var original = document.Snapshot;
        var request = new AnalysisRequest(new TextSpan(0, 1), AnalysisScope.Visible);
        var first = session.Analyze(original, [], request);
        document.Apply(new TextChange(3, 0, "d"));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => session.Analyze(document.Snapshot, [], request, canceled.Token));
        document.Apply(new TextChange(4, 0, "e"));
        var rebuilt = session.Analyze(document.Snapshot, [], request);
        Assert.Equal(document.Snapshot.Version, rebuilt.Version);
        Assert.True(rebuilt.Version > first.Version);
        Assert.Equal(document.Snapshot.Length, rebuilt.Coverage.Length);
        Assert.Equal(AnalysisCompleteness.Complete, rebuilt.Completeness);
    }

    /// <summary>CSV record indexes agree with a fresh whole-document parser after structural edits.</summary>
    [Fact]
    public void Csv_session_matches_oracle_after_width_and_multiline_edits()
    {
        using var document = new Document("a,b\r\n\"x\r\ny\",2\r\nc,3\r\n");
        var policy = Assert.IsAssignableFrom<IIncrementalDocumentPolicy>(
            DocumentPolicies.ForKind(DocumentKind.Csv));
        using var session = policy.CreateSession();
        AssertEquivalent(policy.Analyze(document.Snapshot.GetText()), Full(session, document.Snapshot, []));

        var before = document.Snapshot;
        var width = new TextChange(0, 3, "a,b,c");
        var after = document.Apply(width);
        var widened = Full(session, after, [new VersionedEdit(before.Version, after.Version, width)]);
        AssertEquivalent(policy.Analyze(after.GetText()), widened);
        Assert.Equal(2, widened.TotalDiagnosticCount);

        before = after;
        var y = before.GetText().IndexOf('y');
        var content = new TextChange(y, 1, "Y");
        after = document.Apply(content);
        AssertEquivalent(policy.Analyze(after.GetText()),
            Full(session, after, [new VersionedEdit(before.Version, after.Version, content)]));

        // An omitted edit history is a gap, not permission to reuse stale row offsets.
        document.Apply(new TextChange(document.Snapshot.Length, 0, "d,4\n"));
        AssertEquivalent(policy.Analyze(document.Snapshot.GetText()), Full(session, document.Snapshot, []));
    }

    /// <summary>CSV completeness counts offscreen ragged rows even when projection is bounded.</summary>
    [Fact]
    public void Csv_large_visible_projection_keeps_exact_global_diagnostic_count()
    {
        var source = "a,b\n" + string.Concat(Enumerable.Repeat("1,2\n", 5_000));
        using var document = new Document(source);
        using var session = ((IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Csv)).CreateSession();
        var visible = new AnalysisRequest(new TextSpan(0, 4), AnalysisScope.Full);
        var initial = session.Analyze(document.Snapshot, [], visible);
        Assert.Equal(AnalysisCompleteness.Complete, initial.Completeness);
        Assert.Equal(0, initial.TotalDiagnosticCount);
        Assert.True(initial.Root.Children.Count < 5_001);

        var before = document.Snapshot;
        var change = new TextChange(0, 3, "a,b,c");
        var after = document.Apply(change);
        var changed = session.Analyze(after,
            [new VersionedEdit(before.Version, after.Version, change)], visible);
        Assert.Equal(AnalysisCompleteness.Complete, changed.Completeness);
        Assert.Equal(5_000, changed.TotalDiagnosticCount);
        Assert.Equal(new TextSpan(0, after.Length), changed.Coverage);
    }

    /// <summary>CSV cancellation does not advance the committed index version.</summary>
    [Fact]
    public void Csv_session_cancellation_allows_same_edit_chain_retry()
    {
        using var document = new Document("a,b\n1,2\n");
        var policy = (IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Csv);
        using var session = policy.CreateSession();
        Full(session, document.Snapshot, []);
        var before = document.Snapshot;
        var change = new TextChange(4, 1, "3");
        var after = document.Apply(change);
        var edit = new VersionedEdit(before.Version, after.Version, change);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            Full(session, after, [edit], canceled.Token));
        AssertEquivalent(policy.Analyze(after.GetText()), Full(session, after, [edit]));
    }

    /// <summary>Seeded malformed and structural CSV edits stay equivalent to a fresh parse.</summary>
    [Fact]
    public void Csv_session_random_edits_match_whole_document_oracle()
    {
        using var document = new Document("a,b\r\n\"x\r\ny\",2\r\n3,4\n");
        var policy = (IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Csv);
        using var session = policy.CreateSession();
        Full(session, document.Snapshot, []);
        var random = new Random(0x435356);
        var insertions = new[] { "", "x", ",", "\"", "\r", "\n", "\r\n" };
        for (var i = 0; i < 250; i++)
        {
            var before = document.Snapshot;
            var start = random.Next(before.Length + 1);
            var remove = random.Next(Math.Min(3, before.Length - start) + 1);
            var change = new TextChange(start, remove, insertions[random.Next(insertions.Length)]);
            var after = document.Apply(change);
            var actual = Full(session, after, [new VersionedEdit(before.Version, after.Version, change)]);
            AssertEquivalent(policy.Analyze(after.GetText()), actual);
        }
    }

    /// <summary>Local Markdown inline edits preserve whole-document semantic equivalence.</summary>
    [Fact]
    public void Markdown_session_matches_oracle_after_local_heading_and_inline_edits()
    {
        using var document = new Document("Intro *one* and [link](https://example.com)\n\n# Heading\n\nTail\n");
        var policy = Assert.IsAssignableFrom<IIncrementalDocumentPolicy>(
            DocumentPolicies.ForKind(DocumentKind.Markdown));
        using var session = policy.CreateSession();
        AssertEquivalent(policy.Analyze(document.Snapshot.GetText()), Full(session, document.Snapshot, []));
        var before = document.Snapshot;
        var change = new TextChange(before.GetText().IndexOf("one", StringComparison.Ordinal), 3, "two");
        var after = document.Apply(change);
        AssertEquivalent(policy.Analyze(after.GetText()),
            Full(session, after, [new VersionedEdit(before.Version, after.Version, change)]));
        before = after;
        change = new TextChange(before.GetText().IndexOf("Heading", StringComparison.Ordinal), 7, "Title");
        after = document.Apply(change);
        AssertEquivalent(policy.Analyze(after.GetText()),
            Full(session, after, [new VersionedEdit(before.Version, after.Version, change)]));
    }

    /// <summary>Markdown global references and fences force safe results, not stale local facts.</summary>
    [Theory]
    [InlineData("[foo][ref]\n\n[ref]: https://old.example\n", "old.example", "new.example")]
    [InlineData("```json\n{\"x\":1}\n```\n", "1", "2")]
    [InlineData("Text [link](https://example.com)\n", "]", "")]
    public void Markdown_session_matches_oracle_after_nonlocal_or_malformed_edit(
        string source, string oldText, string newText)
    {
        using var document = new Document(source);
        var policy = (IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Markdown);
        using var session = policy.CreateSession();
        Full(session, document.Snapshot, []);
        var before = document.Snapshot;
        var change = new TextChange(source.IndexOf(oldText, StringComparison.Ordinal), oldText.Length, newText);
        var after = document.Apply(change);
        AssertEquivalent(policy.Analyze(after.GetText()),
            Full(session, after, [new VersionedEdit(before.Version, after.Version, change)]));
    }

    /// <summary>Large viewport Markdown results cannot claim complete semantics without a full parse.</summary>
    [Fact]
    public void Markdown_large_visible_analysis_is_explicitly_provisional()
    {
        using var document = new Document(new string('x', 5 * 1024 * 1024));
        using var session = ((IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Markdown)).CreateSession();
        var result = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(2 * 1024 * 1024, 100), AnalysisScope.Visible));
        Assert.Equal(AnalysisCompleteness.Provisional, result.Completeness);
        Assert.Null(result.TotalDiagnosticCount);
        Assert.True(result.Coverage.Length <= 512 * 1024);
        Assert.Equal(document.Snapshot.Version, result.Version);
    }

    /// <summary>Markdown canceled analysis cannot consume an edit chain or publish its version.</summary>
    [Fact]
    public void Markdown_session_cancellation_allows_retry()
    {
        using var document = new Document("# Heading\n\nParagraph\n");
        var policy = (IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Markdown);
        using var session = policy.CreateSession();
        Full(session, document.Snapshot, []);
        var before = document.Snapshot;
        var change = new TextChange(2, 7, "Title");
        var after = document.Apply(change);
        var edit = new VersionedEdit(before.Version, after.Version, change);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => Full(session, after, [edit], canceled.Token));
        AssertEquivalent(policy.Analyze(after.GetText()), Full(session, after, [edit]));
    }

    /// <summary>JSON sessions preserve escaped-key semantics and recover from invalid Unicode escapes.</summary>
    [Theory]
    [InlineData("{\"a\":1,\"\\u0061\":2}", "JSON_DUPLICATE_KEY")]
    [InlineData("{\"x\":\"\\uD800\"}", "JSON_STRING")]
    [InlineData("{\"x\":\"\\uDC00\"}", "JSON_STRING")]
    public void Json_small_full_session_matches_oracle_and_reports_semantic_error(string source, string code)
    {
        using var document = new Document(source);
        var policy = (IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Json);
        using var session = policy.CreateSession();
        var actual = Full(session, document.Snapshot, []);
        AssertEquivalent(policy.Analyze(source), actual);
        Assert.Contains(actual.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    /// <summary>A valid UTF-16 surrogate pair in JSON escapes is one decoded scalar, not an error.</summary>
    [Fact]
    public void Json_session_accepts_valid_escaped_surrogate_pair()
    {
        const string source = "{\"x\":\"\\uD83D\\uDE00\"}";
        using var document = new Document(source);
        var policy = (IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Json);
        using var session = policy.CreateSession();
        var actual = Full(session, document.Snapshot, []);
        AssertEquivalent(policy.Analyze(source), actual);
        Assert.Empty(actual.Diagnostics);
        Assert.Contains(Shape(actual.Root), node => node.Contains("😀", StringComparison.Ordinal));
    }

    /// <summary>Off-screen malformed JSON remains globally diagnosed without a full tree projection.</summary>
    [Fact]
    public void Json_large_full_session_counts_offscreen_invalid_surrogate()
    {
        var source = "{\"pad\":\"" + new string('x', 1024 * 1024 + 16) + "\",\"bad\":\"\\uD800\"}";
        using var document = new Document(source);
        using var session = ((IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Json)).CreateSession();
        var actual = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(0, 32), AnalysisScope.Full));
        Assert.Equal(AnalysisCompleteness.Complete, actual.Completeness);
        Assert.Equal(1, actual.TotalDiagnosticCount);
        Assert.Equal(new TextSpan(0, source.Length), actual.Coverage);
        Assert.Empty(actual.Diagnostics);
        Assert.True(actual.Root.Children.Count < 4);
    }

    /// <summary>Two undecodable JSON keys must not collapse to a spurious empty-key duplicate.</summary>
    [Fact]
    public void Json_invalid_surrogate_keys_do_not_invent_duplicate_or_complete_claim()
    {
        const string source = "{\"\\uD800\":1,\"\\uD801\":2}";
        using var document = new Document(source);
        var policy = (IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Json);
        using var session = policy.CreateSession();
        var legacy = policy.Analyze(source);
        var actual = Full(session, document.Snapshot, []);
        Assert.Equal(AnalysisCompleteness.Provisional, actual.Completeness);
        Assert.Null(actual.TotalDiagnosticCount);
        Assert.Contains(legacy.Diagnostics, diagnostic => diagnostic.Code == "JSON_STRING");
        Assert.DoesNotContain(legacy.Diagnostics, diagnostic => diagnostic.Code == "JSON_DUPLICATE_KEY");
        Assert.Contains(actual.Diagnostics, diagnostic => diagnostic.Code == "JSON_STRING");
        Assert.DoesNotContain(actual.Diagnostics, diagnostic => diagnostic.Code == "JSON_DUPLICATE_KEY");
    }

    /// <summary>Small YAML sessions agree with the source-anchored oracle on canonical keys and aliases.</summary>
    [Theory]
    [InlineData("0xB: a\n11: b\n", "yaml.duplicate-key")]
    [InlineData("a: &anchor 1\nb: *anchor\n", null)]
    public void Yaml_small_full_session_matches_oracle(string source, string? diagnosticCode)
    {
        using var document = new Document(source);
        var policy = (IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Yaml);
        using var session = policy.CreateSession();
        var actual = Full(session, document.Snapshot, []);
        AssertEquivalent(policy.Analyze(source), actual);
        if (diagnosticCode is not null)
            Assert.Contains(actual.Diagnostics, diagnostic => diagnostic.Code == diagnosticCode);
    }

    /// <summary>An early YAML syntax abort cannot certify unseen aliases or key equality.</summary>
    [Theory]
    [InlineData("value: [a, b\nlater: *missing\n")]
    [InlineData("value: [a, b\n0xB: x\n11: y\n")]
    public void Yaml_small_syntax_abort_does_not_claim_global_completeness(string source)
    {
        using var document = new Document(source);
        var policy = (IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Yaml);
        using var session = policy.CreateSession();
        var legacy = policy.Analyze(source);
        Assert.Contains(legacy.Diagnostics, diagnostic => diagnostic.Code == "yaml.syntax");
        var actual = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(0, 8), AnalysisScope.Full));
        Assert.Equal(AnalysisCompleteness.Provisional, actual.Completeness);
        Assert.Null(actual.TotalDiagnosticCount);
        Assert.Contains(actual.Diagnostics, diagnostic => diagnostic.Code == "yaml.syntax");
        Assert.True(actual.Coverage.Length < source.Length);
    }

    /// <summary>Streaming YAML finds duplicate keys and undefined aliases beyond the viewport.</summary>
    [Fact]
    public void Yaml_large_full_session_reports_offscreen_semantics_at_absolute_offsets()
    {
        var source = "0xB: a\n" + string.Concat(Enumerable.Repeat("# padding\n", 110_000)) +
            "11: b\nmissing: *absent\n";
        using var document = new Document(source);
        using var session = ((IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Yaml)).CreateSession();
        var actual = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(0, 32), AnalysisScope.Full));
        Assert.Equal(AnalysisCompleteness.Complete, actual.Completeness);
        Assert.Equal(2, actual.TotalDiagnosticCount);
        Assert.Equal(new TextSpan(0, source.Length), actual.Coverage);
        Assert.Contains(actual.Diagnostics, diagnostic => diagnostic.Code == "yaml.duplicate-key" &&
            diagnostic.Span.Start == source.LastIndexOf("11: b", StringComparison.Ordinal));
        Assert.Contains(actual.Diagnostics, diagnostic => diagnostic.Code == "yaml.undefined-alias" &&
            diagnostic.Span.Start == source.LastIndexOf("*absent", StringComparison.Ordinal));
    }

    /// <summary>Large YAML syntax errors and giant scalar lines must not claim global completeness.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Yaml_large_session_downgrades_unverifiable_full_analysis(bool giantScalar)
    {
        var source = giantScalar
            ? "value: " + new string('x', 1024 * 1024 + 1) + "\n"
            : string.Concat(Enumerable.Repeat("# padding\n", 110_000)) + "value: [a, b\n";
        using var document = new Document(source);
        using var session = ((IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Yaml)).CreateSession();
        var actual = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(0, 16), AnalysisScope.Full));
        Assert.Equal(AnalysisCompleteness.Provisional, actual.Completeness);
        Assert.Null(actual.TotalDiagnosticCount);
        if (giantScalar)
            Assert.Contains(actual.Diagnostics, diagnostic => diagnostic.Code == "yaml.streaming-limit");
        else
            Assert.Contains(actual.Diagnostics, diagnostic => diagnostic.Code == "yaml.syntax");
    }

    /// <summary>Large YAML viewport-only analysis stays explicitly provisional.</summary>
    [Fact]
    public void Yaml_large_visible_session_does_not_assert_document_validity()
    {
        var source = string.Concat(Enumerable.Repeat("# padding\n", 110_000)) + "bad: *missing\n";
        using var document = new Document(source);
        using var session = ((IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Yaml)).CreateSession();
        var actual = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(0, 32), AnalysisScope.Visible));
        Assert.Equal(AnalysisCompleteness.Provisional, actual.Completeness);
        Assert.Null(actual.TotalDiagnosticCount);
        Assert.True(actual.Coverage.Length < source.Length);
    }

    /// <summary>Small TOML sessions retain the exact semantic tree through Unicode and multiline syntax.</summary>
    [Theory]
    [InlineData("title = '猫😀'\n[section]\nvalues = [1, 2]\n", null)]
    [InlineData("description = \"\"\"hello\nworld\"\"\"\n[[items]]\nname = 'one'\n[[items]]\nname = 'two'\n", null)]
    public void Toml_small_full_session_matches_oracle(string source, string? diagnosticCode)
    {
        using var document = new Document(source);
        var policy = (IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Toml);
        using var session = policy.CreateSession();
        var actual = Full(session, document.Snapshot, []);
        AssertEquivalent(policy.Analyze(source), actual);
        Assert.Empty(actual.Diagnostics);
        if (diagnosticCode is not null)
            Assert.Contains(actual.Diagnostics, diagnostic => diagnostic.Code == diagnosticCode);
    }

    /// <summary>Tomlyn may stop at an early error, so later ownership cannot be certified.</summary>
    [Theory]
    [InlineData("x = ???\nb = 1\nb = 2\n")]
    [InlineData("a = 1\na = 2\n")]
    public void Toml_small_invalid_document_does_not_claim_complete_ownership(string source)
    {
        using var document = new Document(source);
        var policy = (IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Toml);
        using var session = policy.CreateSession();
        var actual = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(0, Math.Min(8, source.Length)), AnalysisScope.Full));
        Assert.Equal(AnalysisCompleteness.Provisional, actual.Completeness);
        Assert.Null(actual.TotalDiagnosticCount);
        Assert.Contains(actual.Diagnostics, diagnostic => diagnostic.Code == "TOML_PARSE");
    }

    /// <summary>Only a bounded, independently verified large TOML subset may claim Complete.</summary>
    [Fact]
    public void Toml_large_full_session_certifies_bounded_valid_assignments()
    {
        var value = new string('x', 100);
        var source = string.Concat(Enumerable.Range(0, 40_000).Select(index => $"k{index:D5} = \"{value}\"\n"));
        Assert.True(source.Length > 4 * 1024 * 1024);
        using var document = new Document(source);
        using var session = ((IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Toml)).CreateSession();
        var actual = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(0, 32), AnalysisScope.Full));
        Assert.Equal(AnalysisCompleteness.Complete, actual.Completeness);
        Assert.Equal(0, actual.TotalDiagnosticCount);
        Assert.Equal(new TextSpan(0, source.Length), actual.Coverage);
        Assert.True(actual.Root.Children.Count < 40_000);
    }

    /// <summary>Offscreen ownership conflicts cannot be silently certified.</summary>
    [Theory]
    [InlineData("k00000 = 0\n")]
    public void Toml_large_full_session_downgrades_uncertain_ownership(string suffix)
    {
        var value = new string('x', 100);
        var source = string.Concat(Enumerable.Range(0, 40_000).Select(index => $"k{index:D5} = \"{value}\"\n")) + suffix;
        using var document = new Document(source);
        using var session = ((IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Toml)).CreateSession();
        var actual = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(0, 32), AnalysisScope.Full));
        Assert.Equal(AnalysisCompleteness.Provisional, actual.Completeness);
        Assert.Null(actual.TotalDiagnosticCount);
        Assert.True(actual.Coverage.Length < source.Length);
    }

    /// <summary>Repeated root array-table elements have independent key namespaces.</summary>
    [Fact]
    public void Toml_large_root_array_tables_with_per_element_repeated_keys_are_complete()
    {
        var source = RootArrayTableCorpus();
        using var document = new Document(source);
        using var session = ((IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Toml)).CreateSession();
        var actual = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(0, 32), AnalysisScope.Full));
        Assert.Equal(AnalysisCompleteness.Complete, actual.Completeness);
        Assert.Equal(0, actual.TotalDiagnosticCount);
        Assert.Equal(new TextSpan(0, source.Length), actual.Coverage);
        Assert.True(actual.Root.Children.Count < 5_000);
    }

    /// <summary>A duplicate in the latest array element cannot be certified.</summary>
    [Theory]
    [InlineData("v = 'again'\n")]
    public void Toml_large_array_table_conflicts_or_nested_scopes_downgrade(string suffix)
    {
        var source = RootArrayTableCorpus() + suffix;
        using var document = new Document(source);
        using var session = ((IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Toml)).CreateSession();
        var actual = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(0, 32), AnalysisScope.Full));
        Assert.Equal(AnalysisCompleteness.Provisional, actual.Completeness);
        Assert.Null(actual.TotalDiagnosticCount);
        Assert.True(actual.Coverage.Length < source.Length);
    }

    /// <summary>Nested headers bind to the latest element; parent re-entry creates a fresh independent scope.</summary>
    [Theory]
    [InlineData("[item.child]\ny = 1\n")]
    [InlineData("[[item.child]]\ny = 1\n[[item.child]]\ny = 2\n")]
    [InlineData("[item.child]\ny = 1\n[[item]]\nv = 'new'\n")]
    [InlineData("[[item.child]]\ny = 1\n[[item]]\nv = 'new'\n")]
    public void Toml_large_nested_array_tables_without_parent_reentry_are_complete(string suffix)
    {
        var source = RootArrayTableCorpus() + suffix;
        using var document = new Document(source);
        using var session = ((IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Toml)).CreateSession();
        var actual = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(source.Length - suffix.Length, suffix.Length), AnalysisScope.Full));
        Assert.Equal(AnalysisCompleteness.Complete, actual.Completeness);
        Assert.Equal(0, actual.TotalDiagnosticCount);
        Assert.Equal(new TextSpan(0, source.Length), actual.Coverage);
        Assert.Contains(actual.Root.Children, node => node.Kind is "table" or "array-table");
    }

    /// <summary>
    /// An ordinary or array-table header creates a still-open implicit parent: after its
    /// outer parent is opened, a dotted sibling may define that implicit namespace.
    /// </summary>
    [Theory]
    [InlineData("[a.x.y]\n[a]\nx.z = 3\n")]
    [InlineData("[[a.x.y]]\n[a]\nx.z = 3\n")]
    [InlineData("[[a.\"x\".y]]\n[a]\nx.z = 3\n")]
    public void Toml_large_implicit_header_parent_allows_dotted_sibling(string suffix)
    {
        Assert.Empty(DocumentPolicies.ForKind(DocumentKind.Toml).Analyze(suffix).Diagnostics);
        var source = RootArrayTableCorpus() + suffix;
        using var document = new Document(source);
        using var session = ((IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Toml)).CreateSession();
        var actual = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(source.Length - suffix.Length, suffix.Length), AnalysisScope.Full));
        Assert.Equal(AnalysisCompleteness.Complete, actual.Completeness);
        Assert.Equal(0, actual.TotalDiagnosticCount);
        Assert.Equal(new TextSpan(0, source.Length), actual.Coverage);
        var entry = Assert.Single(actual.Root.Children,
            node => node.Kind == "entry" && node.Name == "x.z");
        Assert.Equal("x.z = 3", source.Substring(entry.Span.Start, entry.Span.Length).TrimEnd());
    }

    /// <summary>An explicitly declared table cannot be traversed by a dotted sibling.</summary>
    [Fact]
    public void Toml_large_explicit_header_parent_stays_provisional()
    {
        const string suffix = "[a.x]\n[a]\nx.z = 3\n";
        var source = RootArrayTableCorpus() + suffix;
        using var document = new Document(source);
        using var session = ((IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Toml)).CreateSession();
        var actual = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(source.Length - suffix.Length, suffix.Length), AnalysisScope.Full));
        Assert.Equal(AnalysisCompleteness.Provisional, actual.Completeness);
        Assert.Null(actual.TotalDiagnosticCount);
    }

    /// <summary>
    /// Once a dotted assignment traverses a header-created implicit parent, that parent
    /// cannot be explicitly reopened; duplicate dotted descendants also remain errors.
    /// </summary>
    [Theory]
    [InlineData("[a.x.y]\n[a]\nx.z = 3\n[a.x]\n")]
    [InlineData("[[a.x.y]]\n[a]\nx.z = 3\n[a.x]\n")]
    [InlineData("[[a.x.y]]\n[a]\nx.z = 3\nx.z = 4\n")]
    public void Toml_large_dotted_header_parent_seals_and_detects_duplicates(string suffix)
    {
        var source = RootArrayTableCorpus() + suffix;
        using var document = new Document(source);
        using var session = ((IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Toml)).CreateSession();
        var actual = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(source.Length - suffix.Length, suffix.Length), AnalysisScope.Full));
        Assert.Equal(AnalysisCompleteness.Provisional, actual.Completeness);
        Assert.Null(actual.TotalDiagnosticCount);
    }

    /// <summary>Builds a bounded >4 MiB TOML array-table corpus with one valid key per element.</summary>
    private static string RootArrayTableCorpus()
    {
        var value = new string('x', 900);
        var source = string.Concat(Enumerable.Repeat($"[[item]]\nv = '{value}'\n", 5_000));
        Assert.True(source.Length > 4 * 1024 * 1024);
        return source;
    }

    /// <summary>Cancellation and missing edit history must not cause any structural session to reuse stale facts.</summary>
    [Theory]
    [InlineData(DocumentKind.Json, "{\"value\":1}")]
    [InlineData(DocumentKind.Toml, "value = 1\n")]
    [InlineData(DocumentKind.Yaml, "value: 1\n")]
    public void Structural_session_cancellation_and_gap_rebuild_from_snapshot(DocumentKind kind, string source)
    {
        using var document = new Document(source);
        var policy = (IIncrementalDocumentPolicy)DocumentPolicies.ForKind(kind);
        using var session = policy.CreateSession();
        AssertEquivalent(policy.Analyze(source), Full(session, document.Snapshot, []));
        var before = document.Snapshot;
        var at = source.IndexOf('1');
        var change = new TextChange(at, 1, "2");
        var after = document.Apply(change);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => Full(session, after,
            [new VersionedEdit(before.Version, after.Version, change)], canceled.Token));
        AssertEquivalent(policy.Analyze(after.GetText()), Full(session, after,
            [new VersionedEdit(before.Version, after.Version, change)]));
        document.Apply(new TextChange(at, 1, "3"));
        AssertEquivalent(policy.Analyze(document.Snapshot.GetText()), Full(session, document.Snapshot, []));
    }

    /// <summary>Requests all semantics and projection for a small authoritative snapshot.</summary>
    private static DocumentAnalysis Full(IFormatSession session, TextSnapshot snapshot,
        IReadOnlyList<VersionedEdit> changes, CancellationToken cancellationToken = default) =>
        session.Analyze(snapshot, changes,
            new AnalysisRequest(new TextSpan(0, snapshot.Length), AnalysisScope.Full), cancellationToken);

    /// <summary>Compares source-anchored IR and diagnostics with the independent legacy parser.</summary>
    private static void AssertEquivalent(FormatAnalysis expected, DocumentAnalysis actual)
    {
        Assert.Equal(AnalysisCompleteness.Complete, actual.Completeness);
        Assert.Equal(expected.SourceText.Length, actual.Coverage.Length);
        Assert.Equal(expected.Diagnostics.Count, actual.TotalDiagnosticCount);
        Assert.Equal(Shape(expected.Root), Shape(actual.Root));
        Assert.Equal(expected.Diagnostics, actual.Diagnostics);
        Assert.Equal(expected.Tokens, actual.Tokens);
    }

    /// <summary>Serializes a semantic tree including absolute source spans.</summary>
    private static string[] Shape(SemanticNode node) =>
        new[] { $"{node.Kind}|{node.Name}|{node.Value}|{node.Span.Start}|{node.Span.Length}" }
            .Concat(node.Children.SelectMany(Shape)).ToArray();
}
