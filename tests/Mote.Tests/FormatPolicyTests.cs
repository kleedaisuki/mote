using Mote.Formats;

namespace Mote.Tests;

/// <summary>Contract tests for static, source-anchored format policies.</summary>
public sealed class FormatPolicyTests
{
    /// <summary>Each supported extension resolves to its built-in policy without runtime discovery.</summary>
    [Theory]
    [InlineData("note.md", DocumentKind.Markdown)]
    [InlineData("config.toml", DocumentKind.Toml)]
    [InlineData("data.json", DocumentKind.Json)]
    [InlineData("data.jsonc", DocumentKind.PlainText)]
    [InlineData("data.yaml", DocumentKind.Yaml)]
    [InlineData("data.yml", DocumentKind.Yaml)]
    [InlineData("table.csv", DocumentKind.Csv)]
    [InlineData("unknown.bin", DocumentKind.PlainText)]
    public void Registry_selects_by_extension(string path, DocumentKind expected)
    {
        Assert.Equal(expected, DocumentPolicies.ForPath(path).Kind);
    }

    /// <summary>Analyses retain exact input and all source ranges are valid UTF-16 ranges.</summary>
    [Theory]
    [InlineData(DocumentKind.PlainText, "a😀\r\nb")]
    [InlineData(DocumentKind.Markdown, "# 😀 heading\n\ntext")]
    [InlineData(DocumentKind.Toml, "title = '😀'\n")]
    [InlineData(DocumentKind.Json, "{\"emoji\":\"😀\"}")]
    [InlineData(DocumentKind.Yaml, "emoji: 😀\n")]
    [InlineData(DocumentKind.Csv, "emoji,name\n😀,a\n")]
    public void Analysis_preserves_source_and_bounds(DocumentKind kind, string source)
    {
        var analysis = DocumentPolicies.ForKind(kind).Analyze(source);
        Assert.Equal(source, analysis.SourceText);
        AssertRange(analysis.Root.Span, source.Length);
        foreach (var node in Descendants(analysis.Root)) AssertRange(node.Span, source.Length);
        foreach (var token in analysis.Tokens) AssertRange(token.Span, source.Length);
        foreach (var diagnostic in analysis.Diagnostics) AssertRange(diagnostic.Span, source.Length);
    }

    /// <summary>Valid nested JSON yields semantic depth rather than a flat token stream.</summary>
    [Fact]
    public void Json_exposes_nested_structure()
    {
        const string source = "{\"outer\":{\"items\":[1,true,null]}}";
        var analysis = DocumentPolicies.ForKind(DocumentKind.Json).Analyze(source);
        Assert.DoesNotContain(analysis.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.True(MaxDepth(analysis.Root) >= 3, "Expected nested semantic nodes for object and array values.");
        Assert.NotEmpty(analysis.Tokens);
    }

    /// <summary>Duplicate keys and malformed JSON must not silently look valid.</summary>
    [Theory]
    [InlineData("{\"x\":1,\"x\":2}")]
    [InlineData("{\"x\":")]
    public void Json_reports_invalid_or_ambiguous_input(string source)
    {
        var analysis = DocumentPolicies.ForKind(DocumentKind.Json).Analyze(source);
        Assert.Contains(analysis.Diagnostics, d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning);
        Assert.Equal(source, DocumentPolicies.ForKind(DocumentKind.Json).Format(source));
    }

    /// <summary>JSON key equality is based on decoded strings, not spelling of escapes.</summary>
    [Fact]
    public void Json_reports_escaped_equivalent_duplicate_keys()
    {
        var analysis = DocumentPolicies.ForKind(DocumentKind.Json).Analyze("{\"a\":1,\"\\u0061\":2}");
        Assert.Contains(analysis.Diagnostics, d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning);
    }

    /// <summary>TOML table and key/value constructs are represented semantically.</summary>
    [Fact]
    public void Toml_exposes_tables_and_reports_duplicate_keys()
    {
        var policy = DocumentPolicies.ForKind(DocumentKind.Toml);
        var valid = policy.Analyze("[server]\nport = 8080\nactive = true\n");
        Assert.DoesNotContain(valid.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.True(MaxDepth(valid.Root) >= 2);
        var duplicate = policy.Analyze("[server]\nport = 8080\nport = 9090\n");
        Assert.Contains(duplicate.Diagnostics, d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning);
    }

    /// <summary>TOML multiline values and repeated array tables are valid structural input.</summary>
    [Fact]
    public void Toml_accepts_multiline_values_and_array_tables()
    {
        const string source = "title = \"\"\"first\nsecond\"\"\"\nvalues = [1, 2, 3]\n[[items]]\nname = \"a\"\n[[items]]\nname = \"b\"\n";
        var analysis = DocumentPolicies.ForKind(DocumentKind.Toml).Analyze(source);
        Assert.DoesNotContain(analysis.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.True(MaxDepth(analysis.Root) >= 2);
    }

    /// <summary>TOML rejects ambiguous scalar/table and dotted-key ownership.</summary>
    [Theory]
    [InlineData("a = 1\n[a]\nb = 2\n")]
    [InlineData("a.b = 1\na.b = 2\n")]
    public void Toml_reports_key_scope_conflicts(string source)
    {
        var analysis = DocumentPolicies.ForKind(DocumentKind.Toml).Analyze(source);
        Assert.Contains(analysis.Diagnostics, d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning);
    }

    /// <summary>YAML nesting and duplicate mapping keys need syntax-aware analysis.</summary>
    [Fact]
    public void Yaml_exposes_nested_collection_and_duplicate_keys()
    {
        var policy = DocumentPolicies.ForKind(DocumentKind.Yaml);
        var valid = policy.Analyze("items:\n  - name: first\n  - name: second\n");
        Assert.DoesNotContain(valid.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.True(MaxDepth(valid.Root) >= 2);
        var duplicate = policy.Analyze("name: first\nname: second\n");
        Assert.Contains(duplicate.Diagnostics, d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning);
    }

    /// <summary>Unresolved aliases and unknown-tag key equality limits are surfaced to users.</summary>
    [Theory]
    [InlineData("item: *missing\n", "yaml.undefined-alias")]
    [InlineData("? !custom thing\n: value\n", "yaml.key-equality-unsupported")]
    public void Yaml_reports_semantically_problematic_constructs(string source, string expectedCode)
    {
        var analysis = DocumentPolicies.ForKind(DocumentKind.Yaml).Analyze(source);
        Assert.Contains(analysis.Diagnostics, d => d.Code == expectedCode &&
            d.Severity is (DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
    }

    /// <summary>YAML key equality uses tag and canonical value, including structural collections.</summary>
    [Theory]
    [InlineData("0xB: a\n11: b\n")]
    [InlineData("0o13: a\n0xB: b\n")]
    [InlineData("!!int 11: a\n0xB: b\n")]
    [InlineData("? [a, b]\n: first\n? [a, b]\n: second\n")]
    [InlineData("? {a: 1, b: 2}\n: first\n? {b: 2, a: 1}\n: second\n")]
    public void Yaml_reports_canonically_equal_duplicate_keys(string source)
    {
        var analysis = DocumentPolicies.ForKind(DocumentKind.Yaml).Analyze(source);
        Assert.Contains(analysis.Diagnostics, d => d.Code == "yaml.duplicate-key");
    }

    /// <summary>A single valid complex key is supported, not diagnosed as uncomparable.</summary>
    [Fact]
    public void Yaml_accepts_supported_complex_key_without_equality_warning()
    {
        var analysis = DocumentPolicies.ForKind(DocumentKind.Yaml).Analyze("? [a, b]\n: value\n");
        Assert.DoesNotContain(analysis.Diagnostics, d => d.Code == "yaml.key-equality-unsupported");
    }

    /// <summary>Distinct YAML tags prevent a numeric key and a quoted string key from colliding.</summary>
    [Fact]
    public void Yaml_does_not_conflate_quoted_and_numeric_keys()
    {
        var analysis = DocumentPolicies.ForKind(DocumentKind.Yaml).Analyze("11: number\n\"11\": string\n");
        Assert.DoesNotContain(analysis.Diagnostics, d => d.Code == "yaml.duplicate-key");
    }

    /// <summary>YAML 1.2 Core leaves underscored integers and signed hex as strings unless explicitly tagged.</summary>
    [Theory]
    [InlineData("1_000: extended\n1000: core\n")]
    [InlineData("-0xB: extended\n-11: core\n")]
    [InlineData(".iNf: extended\n.inf: core\n")]
    [InlineData(".nAn: extended\n.nan: core\n")]
    public void Yaml_core_does_not_apply_extended_integer_implicit_tags(string source)
    {
        var analysis = DocumentPolicies.ForKind(DocumentKind.Yaml).Analyze(source);
        Assert.DoesNotContain(analysis.Diagnostics, d => d.Code == "yaml.duplicate-key");
    }

    /// <summary>An explicit core tag whose scalar/node kind is invalid must be diagnosed.</summary>
    [Theory]
    [InlineData("value: !!int nope\n", "yaml.invalid-tagged-scalar")]
    [InlineData("value: !!str [a]\n", "yaml.invalid-tagged-collection")]
    [InlineData("value: !!seq foo\n", "yaml.invalid-tagged-scalar")]
    [InlineData("value: !!map foo\n", "yaml.invalid-tagged-scalar")]
    public void Yaml_reports_invalid_explicit_core_tag_value(string source, string expectedCode)
    {
        var analysis = DocumentPolicies.ForKind(DocumentKind.Yaml).Analyze(source);
        Assert.Contains(analysis.Diagnostics, d => d.Code == expectedCode &&
            d.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>Self-referential alias graphs cannot make analysis, formatting, or rendering recurse forever.</summary>
    [Fact]
    public void Yaml_self_alias_is_bounded_or_diagnosed()
    {
        const string source = "value: &a [*a]\n";
        var policy = DocumentPolicies.ForKind(DocumentKind.Yaml);
        var analysis = policy.Analyze(source);
        Assert.Equal(source, analysis.SourceText);
        Assert.NotNull(policy.Format(source));
        Assert.NotNull(policy.RenderHtml(analysis));
        Assert.All(analysis.Diagnostics, d => AssertRange(d.Span, source.Length));
    }

    /// <summary>Quoted CSV newlines remain inside a record and ragged records are diagnosed.</summary>
    [Fact]
    public void Csv_parses_multiline_fields_and_reports_ragged_records()
    {
        var policy = DocumentPolicies.ForKind(DocumentKind.Csv);
        var valid = policy.Analyze("name,note\nA,\"line 1\nline 2\"\nB,\"a \"\"quote\"\"\"\n");
        Assert.DoesNotContain(valid.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.True(MaxDepth(valid.Root) >= 2);
        Assert.Equal(3, valid.Root.Children.Count);
        Assert.Equal("line 1\nline 2", valid.Root.Children[1].Children[1].Value);
        Assert.Equal("a \"quote\"", valid.Root.Children[2].Children[1].Value);
        var ragged = policy.Analyze("a,b\n1\n");
        Assert.Contains(ragged.Diagnostics, d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning);
    }

    /// <summary>CSV row coordinates include embedded newlines in a quoted cell.</summary>
    [Fact]
    public void Csv_multiline_row_span_uses_source_offsets()
    {
        const string source = "a,b\r\n\"x\r\ny\",z\r\n";
        var rows = DocumentPolicies.ForKind(DocumentKind.Csv).Analyze(source).Root.Children;
        Assert.Equal(2, rows.Count);
        Assert.Equal(source.IndexOf('"'), rows[1].Span.Start);
        Assert.Equal("\"x\r\ny\",z", source.Substring(rows[1].Span.Start, rows[1].Span.Length));
    }

    /// <summary>Markdown blocks parse, including a valid CommonMark fence closed by EOF.</summary>
    [Fact]
    public void Markdown_exposes_blocks_and_unclosed_fence()
    {
        var policy = DocumentPolicies.ForKind(DocumentKind.Markdown);
        var valid = policy.Analyze("# Heading\n\n- item\n\n[link](https://example.com)\n\n```cs\ncode\n```\n");
        Assert.DoesNotContain(valid.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.True(Descendants(valid.Root).Count() >= 4);
        var eofClosed = policy.Analyze("```cs\ncode\n");
        Assert.DoesNotContain(eofClosed.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.NotEmpty(eofClosed.Root.Children);
    }

    /// <summary>Markdown semantic values carry readable preview text alongside inline structure.</summary>
    [Fact]
    public void Markdown_ir_keeps_heading_list_and_link_display_values()
    {
        const string source = "#   Heading\n\n- item\n\nSee [link](https://example.com).\n";
        var analysis = DocumentPolicies.ForKind(DocumentKind.Markdown).Analyze(source);
        var nodes = new[] { analysis.Root }.Concat(Descendants(analysis.Root)).ToArray();
        Assert.Contains(nodes, n => n.Kind == "heading" && n.Value == "Heading");
        Assert.Contains(nodes, n => n.Kind == "paragraph" && n.Value == "item");
        Assert.Contains(nodes, n => n.Kind == "paragraph" && n.Value is not null && n.Value.Contains("See link."));
        Assert.Contains(nodes, n => n.Kind == "link" && n.Value == "https://example.com");
        foreach (var node in nodes) AssertRange(node.Span, source.Length);
    }

    /// <summary>Entities and autolinks remain readable in heading IR without creating unsafe links.</summary>
    [Fact]
    public void Markdown_ir_decodes_entity_and_autolink_display_text()
    {
        var policy = DocumentPolicies.ForKind(DocumentKind.Markdown);
        var valid = policy.Analyze("# A &amp; B <https://example.com>\n");
        Assert.Contains(valid.Root.Children, node => node.Kind == "heading" &&
            node.Value == "A & B https://example.com");
        var unsafeLink = policy.Analyze("<javascript:alert(1)>\n");
        Assert.Contains(unsafeLink.Diagnostics, d => d.Code == "markdown.unsafe-link");
        Assert.DoesNotContain("href=\"javascript:", policy.RenderHtml(unsafeLink),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Fallback plain text accepts arbitrary source without fabricated format errors.</summary>
    [Fact]
    public void Plain_text_does_not_report_format_errors()
    {
        var analysis = DocumentPolicies.ForKind(DocumentKind.PlainText).Analyze("{not: [a valid document\n😀\0");
        Assert.Empty(analysis.Diagnostics);
    }

    /// <summary>Formatters are conservative and stable after one pass.</summary>
    [Theory]
    [InlineData(DocumentKind.Json, "{\"a\":1,\"b\":[true,false]}")]
    [InlineData(DocumentKind.Toml, "a = 1\n")]
    [InlineData(DocumentKind.Yaml, "a: 1\n")]
    [InlineData(DocumentKind.Csv, "a,b\n1,2\n")]
    [InlineData(DocumentKind.Markdown, "# title\n")]
    public void Formatting_is_idempotent(DocumentKind kind, string source)
    {
        var policy = DocumentPolicies.ForKind(kind);
        var once = policy.Format(source);
        Assert.Equal(once, policy.Format(once));
    }

    /// <summary>Common whitespace normalization changes presentation but not semantic nodes.</summary>
    [Theory]
    [InlineData(DocumentKind.Toml, "a=1\n", "a = 1\n")]
    [InlineData(DocumentKind.Yaml, "a:    1\n", "a: 1\n")]
    [InlineData(DocumentKind.Markdown, "#   Heading\n", "# Heading\n")]
    public void Formatting_changes_common_input_without_changing_semantics(DocumentKind kind, string source, string expected)
    {
        var policy = DocumentPolicies.ForKind(kind);
        var formatted = policy.Format(source);
        Assert.NotEqual(source, formatted);
        Assert.Equal(expected, formatted);
        Assert.Equal(formatted, policy.Format(formatted));
        var before = policy.Analyze(source);
        var after = policy.Analyze(formatted);
        Assert.DoesNotContain(after.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        if (kind == DocumentKind.Markdown)
            Assert.Equal(policy.RenderHtml(before), policy.RenderHtml(after));
        else
            Assert.Equal(SemanticShape(before.Root), SemanticShape(after.Root));
    }

    /// <summary>Formatting cannot rewrite content-bearing multiline or hard-break whitespace.</summary>
    [Theory]
    [InlineData(DocumentKind.Toml, "a=\"\"\"first  \nsecond\"\"\"\n", "first  \nsecond")]
    [InlineData(DocumentKind.Yaml, "a: |\n  first  \n  second\n", "first  \n  second")]
    [InlineData(DocumentKind.Markdown, "hello  \nworld\n", "hello  \nworld")]
    public void Formatting_preserves_content_whitespace(DocumentKind kind, string source, string protectedSubstring)
    {
        var formatted = DocumentPolicies.ForKind(kind).Format(source);
        Assert.Contains(protectedSubstring, formatted, StringComparison.Ordinal);
    }

    /// <summary>Unsafe recovery-based formatting leaves malformed input byte-for-byte unchanged.</summary>
    [Theory]
    [InlineData(DocumentKind.Toml, "a = [\n")]
    [InlineData(DocumentKind.Yaml, "a: *missing\n")]
    public void Formatting_leaves_invalid_input_unchanged(DocumentKind kind, string source)
    {
        var policy = DocumentPolicies.ForKind(kind);
        Assert.Contains(policy.Analyze(source).Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(source, policy.Format(source));
    }

    /// <summary>Rendering user content as HTML must never create an executable element.</summary>
    [Theory]
    [InlineData(DocumentKind.Markdown, "<script>alert(1)</script>")]
    [InlineData(DocumentKind.Json, "{\"x\":\"<script>alert(1)</script>\"}")]
    [InlineData(DocumentKind.Csv, "x\n<script>alert(1)</script>\n")]
    public void Rendering_escapes_user_html(DocumentKind kind, string source)
    {
        var policy = DocumentPolicies.ForKind(kind);
        var html = policy.RenderHtml(policy.Analyze(source));
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Markdown links must not turn a javascript scheme into an executable href.</summary>
    [Fact]
    public void Markdown_rendering_rejects_javascript_link_target()
    {
        var policy = DocumentPolicies.ForKind(DocumentKind.Markdown);
        var html = policy.RenderHtml(policy.Analyze("[click](javascript:alert(1))"));
        Assert.DoesNotContain("href=\"javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href='javascript:", html, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Traverses a semantic tree without relying on parser-specific node names.</summary>
    private static IEnumerable<SemanticNode> Descendants(SemanticNode root)
    {
        foreach (var child in root.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    /// <summary>Computes semantic nesting depth independently of node labels.</summary>
    private static int MaxDepth(SemanticNode node) =>
        node.Children.Count == 0 ? 0 : 1 + node.Children.Max(MaxDepth);

    /// <summary>Compares structural meaning while intentionally ignoring source spans.</summary>
    private static string[] SemanticShape(SemanticNode root) =>
        new[] { $"{root.Kind}|{root.Name}|{root.Value}" }
            .Concat(root.Children.SelectMany(SemanticShape)).ToArray();

    /// <summary>Checks half-open source ranges against exact input length.</summary>
    private static void AssertRange(TextSpan span, int length)
    {
        Assert.InRange(span.Start, 0, length);
        Assert.InRange(span.Length, 0, length - span.Start);
    }
}
