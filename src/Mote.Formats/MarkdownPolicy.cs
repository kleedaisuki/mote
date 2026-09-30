using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System.Text;

namespace Mote.Formats;

/// <summary>
/// CommonMark document policy backed by Markdig's source-spanned AST. Raw HTML is parsed as
/// text, and unsafe link destinations are cleared before rendering. The semantic projection
/// preserves source positions rather than flattening Markdown to plain text.
/// </summary>
public sealed class MarkdownPolicy : IIncrementalDocumentPolicy
{
    /// <summary>
    /// Shared safe parser configuration for full and incremental projections. Precise
    /// locations are required for inline nodes: default Markdig offsets can otherwise
    /// point to the document head instead of a reference's actual UTF-16 source range.
    /// This changes coordinate tracking, not the admitted CommonMark grammar.
    /// </summary>
    internal static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml().UsePreciseSourceLocation().Build();

    /// <inheritdoc />
    public DocumentKind Kind => DocumentKind.Markdown;
    /// <inheritdoc />
    public string DisplayName => "Markdown";

    /// <inheritdoc />
    public IFormatSession CreateSession() => new MarkdownIncrementalSession();

    /// <inheritdoc />
    public FormatAnalysis Analyze(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();
        var document = Markdown.Parse(text, Pipeline);
        var diagnostics = new List<Diagnostic>();
        var tokens = new List<SemanticToken>();
        var children = new List<SemanticNode>();
        foreach (var block in document)
        {
            cancellationToken.ThrowIfCancellationRequested();
            children.Add(Project(block, text, tokens, diagnostics, cancellationToken));
        }
        return new FormatAnalysis(text, new SemanticNode("document", new TextSpan(0, text.Length), children: children), diagnostics, tokens);
    }

    /// <inheritdoc />
    public string Format(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var before = Analyze(text);
        var edits = new List<(int Start, int Length)>();
        CollectHeadingEdits(before.Root, text, edits);
        if (edits.Count == 0) return text;
        var output = new StringBuilder(text);
        foreach (var edit in edits.OrderByDescending(e => e.Start))
            output.Remove(edit.Start, edit.Length).Insert(edit.Start, " ");
        var candidate = output.ToString();
        // Heading spaces can influence extensions and inline text; rendered equivalence is
        // the final guard, not an assumption about Markdig's treatment of whitespace.
        return RenderHtml(before) == RenderHtml(Analyze(candidate)) ? candidate : text;
    }

    /// <summary>
    /// Locates only surplus spaces after ATX heading markers in source-spanned heading nodes.
    /// Setext headings, hard line breaks, indentation and trailing whitespace are untouched.
    /// </summary>
    private static void CollectHeadingEdits(SemanticNode node, string source, List<(int Start, int Length)> edits)
    {
        if (node.Kind == "heading")
        {
            var end = Math.Min(node.Span.End, source.Length);
            var i = node.Span.Start;
            var spaces = 0;
            while (i < end && spaces < 3 && source[i] == ' ') { i++; spaces++; }
            var markers = 0;
            while (i < end && markers < 6 && source[i] == '#') { i++; markers++; }
            if (markers > 0 && i < end && source[i] != '#')
            {
                var gapStart = i;
                while (i < end && source[i] == ' ') i++;
                if (i - gapStart > 1 && i < end && source[i] is not ('\r' or '\n'))
                    edits.Add((gapStart, i - gapStart));
            }
        }
        foreach (var child in node.Children) CollectHeadingEdits(child, source, edits);
    }

    /// <inheritdoc />
    public string RenderHtml(FormatAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        var document = Markdown.Parse(analysis.SourceText, Pipeline);
        foreach (var block in document) Sanitize(block);
        return Markdown.ToHtml(document, Pipeline);
    }

    /// <summary>Projects parser AST nodes without changing their inclusive source ranges.</summary>
    internal static SemanticNode Project(MarkdownObject node, string text, List<SemanticToken> tokens,
        List<Diagnostic> diagnostics, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var span = Span(node, text.Length);
        List<SemanticNode>? children = null;
        if (node is ContainerBlock block)
            foreach (var child in block) (children ??= []).Add(Project(child, text, tokens, diagnostics, cancellationToken));
        if (node is LeafBlock { Inline: { } inline })
            for (var child = inline.FirstChild; child is not null; child = child.NextSibling)
                if (child is not LiteralInline)
                    (children ??= []).Add(Project(child, text, tokens, diagnostics, cancellationToken));
        if (node is ContainerInline container)
            for (var child = container.FirstChild; child is not null; child = child.NextSibling)
                if (child is not LiteralInline)
                    (children ??= []).Add(Project(child, text, tokens, diagnostics, cancellationToken));

        var kind = node switch
        {
            HeadingBlock => "heading",
            ParagraphBlock => "paragraph",
            ListBlock => "list",
            ListItemBlock => "list-item",
            FencedCodeBlock => "fenced-code",
            CodeBlock => "code-block",
            QuoteBlock => "quote",
            ThematicBreakBlock => "thematic-break",
            LinkInline link when link.IsImage => "image",
            LinkInline => "link",
            AutolinkInline => "link",
            EmphasisInline => "emphasis",
            CodeInline => "code",
            LiteralInline => "text",
            HtmlEntityInline => "text",
            LineBreakInline => "line-break",
            _ => "construct"
        };
        var name = node switch
        {
            HeadingBlock heading => heading.Level.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ListBlock list => list.IsOrdered ? "ordered" : "unordered",
            FencedCodeBlock fence => fence.Info?.ToString(),
            _ => null
        };
        var value = node switch
        {
            HeadingBlock heading => InlineText(heading.Inline),
            ParagraphBlock paragraph => InlineText(paragraph.Inline),
            CodeBlock codeBlock => codeBlock.Lines.ToString(),
            LinkInline link => link.Url,
            AutolinkInline autolink => autolink.Url,
            CodeInline code => code.Content,
            LiteralInline literal => literal.Content.ToString(),
            HtmlEntityInline entity => entity.Transcoded.ToString(),
            // SourceSpan is the source of truth for structural nodes. Copying every paragraph
            // and list's full text here multiplies peak memory on large documents.
            _ => null
        };
        if (node is LinkInline destination && !SafeUrl(destination.Url))
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "markdown.unsafe-link", "The link destination is not safe to render.", span));
        if (node is AutolinkInline autolinkDestination && !SafeUrl(autolinkDestination.Url))
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "markdown.unsafe-link", "The link destination is not safe to render.", span));
        if (kind is not ("text" or "construct") && span.Length > 0)
            tokens.Add(new SemanticToken(kind, span));
        return new SemanticNode(kind, span, name, value, children);
    }

    /// <summary>
    /// Extracts display text from parsed inlines instead of copying Markdown source markers.
    /// Inline semantic children remain available for links/emphasis, while native previews can
    /// use this block value without accidentally displaying raw heading/list syntax.
    /// </summary>
    private static string InlineText(ContainerInline? inline)
    {
        if (inline is null) return string.Empty;
        if (inline.FirstChild is LiteralInline literal && literal.NextSibling is null)
            return literal.Content.ToString();
        var text = new StringBuilder();
        for (var child = inline.FirstChild; child is not null; child = child.NextSibling)
            AppendInlineText(text, child);
        return text.ToString();
    }

    /// <summary>Flattens only rendered inline content; destinations and source delimiters are excluded.</summary>
    private static void AppendInlineText(StringBuilder text, Inline inline)
    {
        switch (inline)
        {
            case LiteralInline literal:
                text.Append(literal.Content.ToString());
                break;
            case CodeInline code:
                text.Append(code.Content);
                break;
            case HtmlEntityInline entity:
                text.Append(entity.Transcoded.ToString());
                break;
            case AutolinkInline autolink:
                text.Append(autolink.Url);
                break;
            case LineBreakInline:
                text.Append('\n');
                break;
            case ContainerInline container:
                for (var child = container.FirstChild; child is not null; child = child.NextSibling)
                    AppendInlineText(text, child);
                break;
        }
    }

    /// <summary>Clears unsafe destinations on the ephemeral render tree, never on cached analysis.</summary>
    private static void Sanitize(MarkdownObject node)
    {
        if (node is LinkInline link && !SafeUrl(link.Url)) link.Url = "";
        if (node is AutolinkInline autolink && !SafeUrl(autolink.Url)) autolink.Url = "";
        if (node is ContainerBlock block)
            foreach (var child in block) Sanitize(child);
        if (node is LeafBlock { Inline: { } inline })
            for (var child = inline.FirstChild; child is not null; child = child.NextSibling) Sanitize(child);
        if (node is ContainerInline container)
            for (var child = container.FirstChild; child is not null; child = child.NextSibling) Sanitize(child);
    }

    /// <summary>Converts Markdig's inclusive offsets to mote's half-open UTF-16 spans.</summary>
    private static TextSpan Span(MarkdownObject node, int length)
    {
        var start = Math.Clamp(node.Span.Start, 0, length);
        var end = Math.Clamp(node.Span.End + 1, start, length);
        return new TextSpan(start, end - start);
    }

    /// <summary>Allowlist for navigation attributes; HTML escaping alone cannot prevent script URLs.</summary>
    private static bool SafeUrl(string? url)
    {
        if (string.IsNullOrEmpty(url) || url.Any(char.IsControl) || url.Any(char.IsWhiteSpace)) return false;
        if (url.StartsWith("//", StringComparison.Ordinal) || url.StartsWith('\\')) return false;
        if (!Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri)) return false;
        return !uri.IsAbsoluteUri || uri.Scheme is "http" or "https" or "mailto";
    }
}
