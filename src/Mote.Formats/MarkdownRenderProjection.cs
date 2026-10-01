using System.Globalization;
using System.Text;
using Markdig.Helpers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Mote.Engine;

namespace Mote.Formats;

/// <summary>Bounded Flow projection of policy-private Markdown syntax, never HTML reparsing.</summary>
internal sealed class MarkdownRenderProjection(TextSnapshot snapshot, DocumentAnalysis analysis,
    CancellationToken cancellationToken)
{
    private const int NoticeReserve = 128;
    internal const int MaxSyntaxVisits = 8192;
    private readonly StringBuilder _text = new();
    private readonly List<FlowRun> _runs = [];
    private readonly List<FlowParagraph> _paragraphs = [];
    private bool _truncated;
    private int _syntaxVisits;

    /// <summary>Whether admitting more syntax could exceed the reserved-notice budget.</summary>
    public bool Full => _text.Length >= FlowRenderProjection.MaxTextLength - NoticeReserve ||
        _runs.Count >= FlowRenderProjection.MaxRuns - 2 || _paragraphs.Count >= FlowRenderProjection.MaxParagraphs - 2 ||
        _syntaxVisits >= MaxSyntaxVisits;

    /// <summary>Walks an authoritative block with its lazy source displacement.</summary>
    public void Add(Block block, int shift, int depth = 0, string? marker = null, bool quote = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Full || depth > 64) { _truncated = true; return; }
        _syntaxVisits++;
        if (block is LinkReferenceDefinitionGroup) return;
        if (block is ListBlock list)
        {
            _ = long.TryParse(list.OrderedStart, NumberStyles.None, CultureInfo.InvariantCulture, out var ordinal);
            for (var i = 0; i < list.Count && !Full; i++)
                Add(list[i], shift, depth + 1, list.IsOrdered ?
                    (ordinal + i).ToString(CultureInfo.InvariantCulture) + "." : "•", quote);
            if (Full) _truncated = true;
            return;
        }
        if (block is ContainerBlock container && block is ListItemBlock or QuoteBlock)
        {
            // The item owns its marker. A descendant leaf can share its paragraph;
            // a nested list owns different markers and cannot consume this one.
            if (block is ListItemBlock && !FirstPresentationIsLeaf(container))
            {
                var itemSource = Origin(block, shift);
                var itemStart = _text.Length;
                Append((marker ?? "•") + " ", itemSource, "list-marker", FlowInlineStyle.None, false, false);
                _paragraphs.Add(new FlowParagraph(new TextSpan(itemStart, _text.Length - itemStart),
                    itemSource, "list-item", Depth: depth, Marker: marker ?? "•"));
                _text.Append('\n');
                marker = null;
            }
            for (var i = 0; i < container.Count && !Full; i++)
            {
                var before = _text.Length;
                Add(container[i], shift, depth + (block is QuoteBlock ? 1 : 0), marker,
                    quote || block is QuoteBlock);
                if (_text.Length != before) marker = null;
            }
            if (Full) _truncated = true;
            return;
        }
        var source = Origin(block, shift);
        var start = _text.Length;
        if (marker is not null)
            Append(marker + " ", source, "list-marker", FlowInlineStyle.None, false, false);
        var kind = block switch { HeadingBlock => "heading", CodeBlock => "code", ThematicBreakBlock => "separator",
            ParagraphBlock => quote ? "quote" : marker is not null ? "list-item" : "paragraph", _ => "unsupported" };
        if (block is LeafBlock { Inline: not null } leaf)
            Inline(leaf.Inline, shift, FlowInlineStyle.None, "text", source, 0);
        else if (block is CodeBlock code)
        {
            for (var i = 0; i < code.Lines.Count && !Full; i++)
            {
                if (i > 0) Append("\n", source, "code", FlowInlineStyle.Code, false);
                Slice(code.Lines.Lines[i].Slice, source, "code", FlowInlineStyle.Code, false);
            }
            if (Full) _truncated = true;
        }
        else if (block is ThematicBreakBlock)
            Append("────────", source, "separator", FlowInlineStyle.None, false, false);
        else
        {
            _truncated = true;
            Append("[Unsupported Markdown block]", source, "notice", FlowInlineStyle.None, false, false);
        }
        _paragraphs.Add(new FlowParagraph(new TextSpan(start, _text.Length - start), source, kind,
            block is HeadingBlock heading ? heading.Level : 0, depth, marker));
        _text.Append('\n');
    }

    /// <summary>Finds the first visible structural owner without crossing a nested-list boundary.</summary>
    private bool FirstPresentationIsLeaf(ContainerBlock container)
    {
        var inspected = 0;
        return Find(container, 0) is LeafBlock;

        Block? Find(Block block, int depth)
        {
            if (Full || ++inspected > FlowRenderProjection.MaxRuns || depth > 64)
            {
                _truncated = true;
                return block;
            }
            _syntaxVisits++;
            if (block is LinkReferenceDefinitionGroup) return null;
            if (block is ListBlock || block is not ContainerBlock children) return block;
            foreach (var child in children)
            {
                var visible = Find(child, depth + 1);
                if (visible is not null) return visible;
            }
            return null;
        }
    }

    /// <summary>Emits a bounded source excerpt when a syntax block exceeds safe reparse limits.</summary>
    public void Excerpt(TextSpan source)
    {
        var start = _text.Length;
        var length = Math.Min(source.Length, FlowRenderProjection.MaxTextLength - NoticeReserve - start);
        if (length > 0)
            Append(snapshot.GetText(source.Start, length), source, "text", FlowInlineStyle.None, false);
        _paragraphs.Add(new FlowParagraph(new TextSpan(start, _text.Length - start), source, "source-excerpt"));
        _text.Append('\n');
        _truncated = true;
    }

    /// <summary>Finalizes independent semantic and presentation evidence with non-navigable notices.</summary>
    public FlowRenderProjection Finish(bool omitted)
    {
        _truncated |= omitted;
        if (_truncated) Notice("[Preview truncated: additional content omitted]");
        if (analysis.Completeness != AnalysisCompleteness.Complete && _paragraphs.Count < FlowRenderProjection.MaxParagraphs &&
            _runs.Count < FlowRenderProjection.MaxRuns)
            Notice(analysis.Completeness == AnalysisCompleteness.Provisional ?
                "[Markdown semantics provisional; full validation pending]" :
                "[Markdown semantics covered-region; full validation pending]");
        return new FlowRenderProjection(snapshot.Version, _text.ToString(), _runs, _paragraphs,
            _truncated, analysis.Completeness);
    }

    private void Inline(Inline inline, int shift, FlowInlineStyle style, string role, TextSpan item, int nesting)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Full || nesting > 64) { _truncated = true; return; }
        _syntaxVisits++;
        var source = Origin(inline, shift);
        switch (inline)
        {
            case LiteralInline literal:
                Slice(literal.Content, source, role, style, true);
                break;
            case HtmlEntityInline entity:
                Slice(entity.Transcoded, source, role, style, false);
                break;
            case CodeInline code:
                Append(code.Content, source, "code", style | FlowInlineStyle.Code, false);
                break;
            case AutolinkInline link:
                Append(link.Url, source, "link", style | FlowInlineStyle.Link, false);
                break;
            case LineBreakInline line:
                Append(line.IsHard ? "\n" : " ", source, role, style, false);
                break;
            case ContainerInline container:
                // Unresolved reference/open-bracket containers keep their literal
                // delimiter separate from children; traversing only children loses it.
                if (inline is LinkDelimiterInline && source.Length > 0)
                    Append(snapshot.GetText(source.Start, Math.Min(source.Length, 2)), source, role, style, true);
                if (inline is EmphasisDelimiterInline delimiter)
                    Slice(delimiter.Content, source, role, style, true);
                if (inline is EmphasisInline emphasis)
                {
                    if (emphasis.DelimiterChar is '*' or '_')
                        style |= emphasis.DelimiterCount >= 2 ? FlowInlineStyle.Strong : FlowInlineStyle.Emphasis;
                    else
                        Append("[Unsupported emphasis] ", source, "notice", FlowInlineStyle.None, false, false);
                }
                if (inline is LinkInline target)
                {
                    if (target.IsImage)
                        Append("[Image: ", source, "image-alt", style, false, false);
                    else { style |= FlowInlineStyle.Link; role = "link"; }
                }
                for (var child = container.FirstChild; child is not null && !Full; child = child.NextSibling)
                    Inline(child, shift, style, inline is LinkInline { IsImage: true } ? "image-alt" : role,
                        source.Length > 0 ? source : item, nesting + 1);
                if (inline is LinkInline { IsImage: true })
                    Append("]", source, "image-alt", style, false, false);
                if (Full) _truncated = true;
                break;
            default:
                _truncated = true;
                Append("[Unsupported inline]", source.Length > 0 ? source : item, "notice", style, false, false);
                break;
        }
    }

    private void Slice(StringSlice slice, TextSpan source, string role, FlowInlineStyle style, bool exact)
    {
        if (slice.Text is not null && slice.Length > 0)
            Append(slice.Text.AsSpan(slice.Start, slice.Length), source, role, style, exact);
    }

    private void Append(ReadOnlySpan<char> value, TextSpan source, string role, FlowInlineStyle style,
        bool exact, bool navigable = true)
    {
        if (Full) { _truncated = true; return; }
        var available = FlowRenderProjection.MaxTextLength - NoticeReserve - _text.Length;
        var length = Math.Min(value.Length, available);
        if (length > 0 && char.IsHighSurrogate(value[length - 1])) length--;
        if (length < value.Length) _truncated = true;
        if (length == 0) return;
        value = value[..length];
        var start = _text.Length;
        // Engine admits well-formed UTF-16; defensively normalize any parser-generated
        // malformed display slice rather than handing an invalid string to native text.
        var valid = true;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            { _text.Append(c); _text.Append(value[++i]); }
            else if (char.IsSurrogate(c)) { _text.Append('\uFFFD'); valid = false; }
            else _text.Append(c);
        }
        var affine = exact && valid && source.Length >= length && source.Start <= snapshot.Length - length &&
            snapshot.GetText(source.Start, length).AsSpan().SequenceEqual(value);
        _runs.Add(new FlowRun(new TextSpan(start, _text.Length - start),
            affine ? new TextSpan(source.Start, length) : source, role, style,
            affine ? RenderOriginPrecision.ExactText : RenderOriginPrecision.Item, navigable));
    }

    private TextSpan Origin(MarkdownObject node, int shift)
    {
        var start = Math.Clamp((long)node.Span.Start + shift, 0, snapshot.Length);
        var end = Math.Clamp((long)node.Span.End + 1 + shift, start, snapshot.Length);
        return new TextSpan((int)start, (int)(end - start));
    }

    private void Notice(string notice)
    {
        if (_paragraphs.Count >= FlowRenderProjection.MaxParagraphs || _runs.Count >= FlowRenderProjection.MaxRuns ||
            _text.Length + notice.Length + 1 > FlowRenderProjection.MaxTextLength) return;
        var range = new TextSpan(_text.Length, notice.Length);
        _text.Append(notice).Append('\n');
        _runs.Add(new FlowRun(range, new TextSpan(0, 0), "notice", FlowInlineStyle.None, RenderOriginPrecision.Item, false));
        _paragraphs.Add(new FlowParagraph(range, new TextSpan(0, 0), "notice"));
    }
}
