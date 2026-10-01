using Mote.Engine;
using Mote.Formats;

namespace Mote.Tests;

/// <summary>Requires exact UTF-16 source coverage for explicitly delimited YAML collections.</summary>
public sealed class YamlFlowCollectionSpanTests
{
    /// <summary>Shares exact source fixtures between ordinary and streamed diagnostic checks.</summary>
    public static IEnumerable<object[]> Keys() => new[]
    {
        "[猫, 犬]", "[猫, 😀]", "[]", "{}", "{猫: 😀}",
        "[[猫], {犬: 😀}]", "{猫: [😀], 犬: {a: b}}",
        "[']', \"}\", 'a''b']", "[猫, # ] is a comment\n  😀]",
        "{猫: 😀, # } is a comment\n 犬: x}", "[a: b]", "!!seq [猫, 😀]"
    }.Select(key => new object[] { key });

    /// <summary>Nested punctuation, quoted delimiters, comments and Unicode are source, not end heuristics.</summary>
    [Theory]
    [MemberData(nameof(Keys))]
    public void Duplicate_key_and_key_token_cover_complete_collection(string key)
    {
        var source = $"# 😀 prefix\r\n? {key}\n: first\n? {key}\n: second # suffix\n";
        var result = new YamlPolicy().Analyze(source);
        var duplicate = Assert.Single(result.Diagnostics, d => d.Code == "yaml.duplicate-key");
        Assert.Single(result.Diagnostics);
        var expected = new TextSpan(source.LastIndexOf(key, StringComparison.Ordinal), key.Length);
        Assert.Equal(expected, duplicate.Span);
        Assert.Equal(key, source.Substring(duplicate.Span.Start, duplicate.Span.Length));
        Assert.Contains(result.Tokens, token => token.Kind == "key" && token.Span == expected);
        var mapping = Assert.Single(Assert.Single(result.Root.Children).Children);
        Assert.Equal(expected, mapping.Children[1].Children[0].Span);
        Assert.Equal(source, new YamlPolicy().Format(source));
    }

    /// <summary>Streamed duplicate diagnostics must cover the same exact source as ordinary analysis.</summary>
    [Theory]
    [MemberData(nameof(Keys))]
    public void Streamed_collection_key_has_exact_full_span(string key)
    {
        var source = "# " + new string('p', 300 * 1024) + $"😀\r\n? {key}\n: first\n? {key}\n: second\n";
        using var document = new Document(source);
        using var session = new YamlPolicy().CreateSession();
        var result = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(0, source.Length), AnalysisScope.Full));
        Assert.Equal(AnalysisCompleteness.Complete, result.Completeness);
        Assert.Equal(1, result.TotalDiagnosticCount);
        var duplicate = Assert.Single(result.Diagnostics);
        Assert.Equal("yaml.duplicate-key", duplicate.Code);
        Assert.Equal(new TextSpan(source.LastIndexOf(key, StringComparison.Ordinal), key.Length), duplicate.Span);
    }

    /// <summary>Collection values and enclosing entries end after the delimiter, not after trailing trivia.</summary>
    [Theory]
    [InlineData("[😀]")]
    [InlineData("{猫: 😀}")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void Collection_value_and_entry_exclude_trailing_comment(string value)
    {
        var source = $"猫: {value} # trailing ] }}\r\n";
        var result = new YamlPolicy().Analyze(source);
        Assert.Empty(result.Diagnostics);
        var entry = Assert.Single(Assert.Single(Assert.Single(result.Root.Children).Children).Children);
        Assert.Equal(new TextSpan(3, value.Length), entry.Children[1].Span);
        Assert.Equal(new TextSpan(0, 3 + value.Length), entry.Span);
    }

    /// <summary>An implicit mapping inside a sequence owns no closing delimiter of its own.</summary>
    [Fact]
    public void Implicit_flow_mapping_does_not_take_sequence_closer()
    {
        const string source = "[a: b]";
        var result = new YamlPolicy().Analyze(source);
        Assert.Empty(result.Diagnostics);
        var sequence = Assert.Single(Assert.Single(result.Root.Children).Children);
        var mapping = Assert.Single(Assert.Single(sequence.Children).Children);
        // SharpYaml includes the opening '[' in this implicit mapping's start mark.
        // Preserve that existing range, but never include the sequence's closing ']'.
        Assert.Equal(new TextSpan(0, 5), mapping.Span);
    }
}
