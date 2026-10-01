using System.Collections.ObjectModel;

namespace Mote.Formats;

/// <summary>The built-in document languages. Policies are selected at compile time.</summary>
public enum DocumentKind { PlainText, Markdown, Toml, Json, Yaml, Csv }

/// <summary>A half-open UTF-16 source range, matching .NET string and editor offsets.</summary>
public readonly record struct TextSpan(int Start, int Length)
{
    /// <summary>The exclusive end offset.</summary>
    public int End => Start + Length;
}

/// <summary>Severity of a format diagnostic.</summary>
public enum DiagnosticSeverity { Information, Warning, Error }

/// <summary>A source-anchored parse or semantic problem.</summary>
public sealed record Diagnostic(DiagnosticSeverity Severity, string Code, string Message, TextSpan Span);

/// <summary>A semantic construct. Children preserve source order.</summary>
public sealed class SemanticNode
{
    /// <summary>Constructs a node with source span and optional semantic value.</summary>
    public SemanticNode(string kind, TextSpan span, string? name = null, string? value = null,
        IReadOnlyList<SemanticNode>? children = null)
    {
        Kind = kind;
        Span = span;
        Name = name;
        Value = value;
        Children = children is null
            ? Array.Empty<SemanticNode>()
            : new ReadOnlyCollection<SemanticNode>(children.ToArray());
    }

    /// <summary>Language-neutral construct name, such as object, key, heading or row.</summary>
    public string Kind { get; }
    /// <summary>Full source span of the construct.</summary>
    public TextSpan Span { get; }
    /// <summary>Optional binding or field name.</summary>
    public string? Name { get; }
    /// <summary>Optional normalized scalar value.</summary>
    public string? Value { get; }
    /// <summary>Nested semantic constructs.</summary>
    public IReadOnlyList<SemanticNode> Children { get; }
}

/// <summary>A source-anchored semantic classification for highlighting.</summary>
public sealed record SemanticToken(string Kind, TextSpan Span);

/// <summary>An immutable analysis snapshot, safe to cache alongside a document version.</summary>
public sealed class FormatAnalysis
{
    /// <summary>Constructs an analysis snapshot. All offsets refer to <paramref name="sourceText"/>.</summary>
    public FormatAnalysis(string sourceText, SemanticNode root,
        IReadOnlyList<Diagnostic> diagnostics, IReadOnlyList<SemanticToken> tokens)
    {
        SourceText = sourceText;
        Root = root;
        Diagnostics = new ReadOnlyCollection<Diagnostic>(diagnostics.ToArray());
        Tokens = new ReadOnlyCollection<SemanticToken>(tokens.ToArray());
    }

    /// <summary>The exact text analyzed; never normalized implicitly.</summary>
    public string SourceText { get; }
    /// <summary>The root semantic construct.</summary>
    public SemanticNode Root { get; }
    /// <summary>Syntax and semantic diagnostics.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    /// <summary>Semantic highlighting spans.</summary>
    public IReadOnlyList<SemanticToken> Tokens { get; }
}

/// <summary>Format-specific policy. Implementations must be statically reachable for Native AOT.</summary>
public interface IDocumentPolicy
{
    /// <summary>The policy's document kind.</summary>
    DocumentKind Kind { get; }
    /// <summary>A user-facing format label.</summary>
    string DisplayName { get; }
    /// <summary>Parses text into semantic constructs and diagnostics. Offsets are UTF-16.</summary>
    FormatAnalysis Analyze(string text, CancellationToken cancellationToken = default);
    /// <summary>Returns a conservative, idempotent normalized form. Invalid input is returned unchanged.</summary>
    string Format(string text);
    /// <summary>Renders safe HTML from an analysis snapshot; user-controlled text is escaped.</summary>
    string RenderHtml(FormatAnalysis analysis);
}

/// <summary>Static built-in policy registry; no reflection or runtime plugin loading.</summary>
public static class DocumentPolicies
{
    private static readonly IDocumentPolicy Plain = new PlainTextPolicy();
    private static readonly IDocumentPolicy Markdown = new MarkdownPolicy();
    private static readonly IDocumentPolicy Toml = new TomlPolicy();
    private static readonly IDocumentPolicy Json = new JsonPolicy();
    private static readonly IDocumentPolicy Yaml = new YamlPolicy();
    private static readonly IDocumentPolicy Csv = new CsvPolicy();

    /// <summary>Gets a policy for a document kind.</summary>
    public static IDocumentPolicy ForKind(DocumentKind kind) => kind switch
    {
        DocumentKind.Markdown => Markdown,
        DocumentKind.Toml => Toml,
        DocumentKind.Json => Json,
        DocumentKind.Yaml => Yaml,
        DocumentKind.Csv => Csv,
        DocumentKind.PlainText => Plain,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown document kind.")
    };

    /// <summary>Gets a policy from a filename extension, falling back to plain text.</summary>
    public static IDocumentPolicy ForPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".md" or ".markdown" or ".mdown" => Markdown,
            ".toml" => Toml,
            ".json" => Json,
            ".yaml" or ".yml" => Yaml,
            ".csv" => Csv,
            _ => Plain
        };
    }
}
