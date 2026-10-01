using System.Text;
using Mote.Engine;
using Mote.Formats;

namespace Mote.Native;

/// <summary>A bounded, source-mapped native presentation, not an HTML web view.</summary>
internal sealed record NativePreview(string Text, IReadOnlyList<NativePreviewSpan> Spans,
    FlowRenderProjection? Flow = null, bool Truncated = false);

/// <summary>Resolves only explicit semantic preview runs, never inferred text positions.</summary>
internal static class NativePreviewNavigation
{
    /// <summary>
    /// Returns the originating source item's start for an exact displayed UTF-16
    /// offset. Decorative banners, delimiters, blank lines, and clipped-away
    /// text have no destination. The caller must also validate the view stamp.
    /// </summary>
    public static int? SourceStart(NativeAnalysisView view, int previewOffset, int sourceLength)
    {
        if (previewOffset < 0 || previewOffset >= view.PreviewText.Length) return null;
        if (view.PreviewSpans is null) return null;
        foreach (var span in view.PreviewSpans)
        {
            if (previewOffset < span.Start || previewOffset >= span.Start + span.Length)
                continue;
            if (!span.Navigable) continue;
            if (span.SourceSpan.Length <= 0 ||
                span.SourceSpan.Start < 0 || span.SourceSpan.End > sourceLength)
                return null;
            return span.SourceSpan.Start;
        }
        return null;
    }
}

/// <summary>
/// Projects policy semantic nodes into readable native text runs. The projection is
/// intentionally bounded; parsing and diagnostics still use the policy's full tree.
/// </summary>
internal static class NativePreviewBuilder
{
    private const int MaxCharacters = 16 * 1024;
    private const int MaxLines = 120;

    /// <summary>Adapts a policy-owned Flow without reconstructing syntax in Native.</summary>
    public static NativePreview FromFlow(FlowRenderProjection flow)
    {
        ArgumentNullException.ThrowIfNull(flow);
        var spans = flow.Runs.Select(run =>
        {
            var paragraph = NativeFlowPresentation.ContainingParagraph(flow, run);
            var heading = paragraph?.Kind == "heading";
            // The established reveal action targets a heading's marker, while
            // Flow keeps the independently precise literal origin for future actions.
            var source = heading ? paragraph!.Value.SourceRange : run.SourceRange;
            return new NativePreviewSpan(run.DisplayRange.Start, run.DisplayRange.Length,
                NativeFlowPresentation.Role(run, paragraph), source,
                heading || (run.Style & FlowInlineStyle.Strong) != 0, run.Navigable);
        }).ToArray();
        return new NativePreview(flow.Text, Array.AsReadOnly(spans), flow, flow.Truncated);
    }

    /// <summary>
    /// Gives existing source-backed semantic projections a typed native Flow.
    /// Markdown sessions use their private ordered inline renderer instead.
    /// CSV retains its compatibility Flow until its independent Grid phase.
    /// </summary>
    public static NativePreview BuildFlow(DocumentAnalysis analysis, DocumentKind kind,
        TextSnapshot snapshot, int pageStart, int pageLength)
    {
        var preview = Build(analysis, kind, snapshot, pageStart, pageLength);
        var runs = new List<FlowRun>();
        var end = 0;
        foreach (var span in preview.Spans.Where(span => span.Kind != "table-cell" || span.Navigable)
            .OrderBy(span => span.Start))
        {
            if (span.Start < end || span.Length <= 0) continue;
            var style = span.Emphasis ? FlowInlineStyle.Strong : FlowInlineStyle.None;
            if (span.Kind == "code") style |= FlowInlineStyle.Code;
            runs.Add(new FlowRun(new TextSpan(span.Start, span.Length), span.SourceSpan,
                span.Kind, style, RenderOriginPrecision.Item, span.Navigable));
            end = span.Start + span.Length;
        }
        var paragraphs = new List<FlowParagraph>();
        var start = 0;
        while (start < preview.Text.Length && paragraphs.Count < MaxLines)
        {
            var next = preview.Text.IndexOf('\n', start);
            if (next < 0) next = preview.Text.Length;
            var owner = runs.FirstOrDefault(run => run.DisplayRange.Start >= start &&
                run.DisplayRange.Start < next);
            paragraphs.Add(new FlowParagraph(new TextSpan(start, next - start),
                owner.SourceRange, string.IsNullOrEmpty(owner.Role) ? "paragraph" : owner.Role));
            start = next + 1;
        }
        var flow = new FlowRenderProjection(analysis.Version, preview.Text, runs, paragraphs,
            preview.Truncated, analysis.Completeness);
        return preview with { Flow = flow };
    }

    /// <summary>Builds a format-aware preview with global document source ranges.</summary>
    public static NativePreview Build(FormatAnalysis analysis, DocumentKind kind,
        int sourceBase, bool complete)
    {
        var output = new Builder(sourceBase);
        if (!complete) output.Add("Viewport sample — incomplete context", "comment",
            analysis.Root.Span, navigable: false);
        switch (kind)
        {
            case DocumentKind.Markdown:
                foreach (var node in analysis.Root.Children) output.Markdown(node);
                break;
            case DocumentKind.Csv:
                output.Csv(analysis.Root);
                break;
            case DocumentKind.PlainText:
                output.Plain(analysis.SourceText, analysis.Root.Span);
                break;
            default:
                output.Tree(analysis.Root);
                break;
        }
        return output.Finish();
    }

    /// <summary>Builds a preview from a versioned session result with absolute source spans.</summary>
    public static NativePreview Build(DocumentAnalysis analysis, DocumentKind kind,
        TextSnapshot snapshot, int pageStart, int pageLength)
    {
        var output = new Builder(0);
        if (analysis.Completeness != AnalysisCompleteness.Complete)
            output.Add($"{analysis.Completeness} preview — incomplete context", "comment",
                analysis.Coverage, navigable: false);
        switch (kind)
        {
            case DocumentKind.Markdown:
                foreach (var node in analysis.Root.Children) output.Markdown(node);
                break;
            case DocumentKind.Csv:
                output.Csv(analysis.Root);
                break;
            case DocumentKind.PlainText:
                output.Plain(snapshot.GetText(pageStart, pageLength),
                    new TextSpan(pageStart, pageLength));
                break;
            default:
                output.Tree(analysis.Root);
                break;
        }
        return output.Finish();
    }

    private sealed class Builder(int sourceBase)
    {
        private readonly StringBuilder _text = new();
        private readonly List<NativePreviewSpan> _spans = [];
        private int _lines;
        private bool _truncated;

        public NativePreview Finish()
        {
            if (_text.Length == 0)
                Add("(empty document)", "comment", new TextSpan(0, 0), navigable: false);
            if (_truncated || _text.Length >= MaxCharacters || _lines >= MaxLines)
            {
                _truncated = true;
                const string marker = "… preview truncated …\n";
                var keep = Math.Min(_text.Length, MaxCharacters - marker.Length);
                var breaks = 0;
                for (var i = 0; i < keep; ++i)
                    if (_text[i] == '\n' && ++breaks == MaxLines - 1)
                    { keep = i + 1; break; }
                if (keep > 0 && char.IsHighSurrogate(_text[keep - 1])) --keep;
                _text.Length = keep;
                _spans.RemoveAll(span => span.Start >= keep);
                for (var i = 0; i < _spans.Count; ++i)
                    if (_spans[i].Start + _spans[i].Length > keep)
                        _spans[i] = _spans[i] with { Length = keep - _spans[i].Start };
                _text.Append(marker);
            }
            return new NativePreview(_text.ToString(), _spans.AsReadOnly(), Truncated: _truncated);
        }

        public void Add(string value, string kind, TextSpan source, int indent = 0,
            bool emphasis = false, bool blankAfter = false, bool navigable = true)
        {
            if (_lines >= MaxLines || _text.Length >= MaxCharacters) { _truncated = true; return; }
            if (indent > 0) _text.Append(' ', Math.Min(indent, 8) * 2);
            var available = MaxCharacters - _text.Length;
            if (available <= 0) return;
            if (value.Length > available)
            {
                _truncated = true;
                if (available > 0 && char.IsHighSurrogate(value[available - 1])) --available;
                value = value[..available];
            }
            var start = _text.Length;
            _text.Append(value);
            if (value.Length > 0)
                _spans.Add(new NativePreviewSpan(start, value.Length, kind,
                    new TextSpan(sourceBase + source.Start, source.Length), emphasis,
                    navigable && source.Length > 0));
            _text.Append('\n');
            _lines++;
            if (blankAfter && _lines < MaxLines && _text.Length < MaxCharacters)
            {
                _text.Append('\n');
                _lines++;
            }
        }

        public void Plain(string source, TextSpan span)
        {
            if (source.Length == 0) return;
            var start = 0;
            while (start < source.Length && _lines < MaxLines && _text.Length < MaxCharacters)
            {
                var end = start;
                while (end < source.Length && source[end] is not ('\r' or '\n')) end++;
                Add(source[start..end], "paragraph", new TextSpan(span.Start + start, end - start));
                start = end;
                if (start < source.Length && source[start] == '\r') start++;
                if (start < source.Length && source[start] == '\n') start++;
            }
        }

        public void Markdown(SemanticNode node, int depth = 0)
        {
            if (_lines >= MaxLines || _text.Length >= MaxCharacters) return;
            switch (node.Kind)
            {
                case "heading":
                    Add(node.Value ?? "", "heading", node.Span, Math.Max(0, ParseLevel(node.Name) - 1),
                        emphasis: true, blankAfter: true);
                    break;
                case "paragraph":
                    Add(node.Value ?? "", "paragraph", node.Span, depth,
                        blankAfter: depth == 0);
                    break;
                case "fenced-code" or "code-block":
                    CodeLines(node.Value ?? "", node.Span, depth + 1);
                    Add("", "code", node.Span, navigable: false);
                    break;
                case "list":
                    var number = 1;
                    foreach (var item in node.Children)
                    {
                        var marker = node.Name == "ordered" ? $"{number++}. " : "• ";
                        var body = item.Children.FirstOrDefault(n => n.Kind == "paragraph")?.Value ??
                            item.Value ?? "";
                        Add(marker + body, "list-marker", item.Span, depth);
                        foreach (var nested in item.Children.Where(n => n.Kind == "list"))
                            Markdown(nested, depth + 1);
                    }
                    Add("", "paragraph", node.Span, navigable: false);
                    break;
                case "quote":
                    foreach (var child in node.Children)
                    {
                        if (child.Kind == "paragraph")
                            Add("│ " + (child.Value ?? ""), "quote", child.Span, depth);
                        else Markdown(child, depth + 1);
                    }
                    Add("", "quote", node.Span, navigable: false);
                    break;
                case "thematic-break":
                    Add("────────────────────", "comment", node.Span, blankAfter: true);
                    break;
                default:
                    foreach (var child in node.Children) Markdown(child, depth);
                    break;
            }
        }

        private void CodeLines(string value, TextSpan source, int depth)
        {
            var start = 0;
            while (start < value.Length && _lines < MaxLines && _text.Length < MaxCharacters)
            {
                var end = value.IndexOf('\n', start);
                if (end < 0) end = value.Length;
                var visibleEnd = Math.Min(end, start + MaxCharacters - _text.Length);
                if (visibleEnd > start && char.IsHighSurrogate(value[visibleEnd - 1])) --visibleEnd;
                Add(value[start..visibleEnd].TrimEnd('\r'), "code", source, depth);
                start = end + 1;
            }
        }

        public void Csv(SemanticNode root)
        {
            _truncated |= root.Children.Count > MaxLines;
            var rows = root.Children.Take(MaxLines).ToArray();
            if (rows.Length == 0) return;
            var columns = Math.Min(8, rows.Max(row => row.Children.Count));
            var widths = new int[columns];
            foreach (var row in rows)
                for (var col = 0; col < Math.Min(columns, row.Children.Count); col++)
                    widths[col] = Math.Max(widths[col], Math.Min(24, (row.Children[col].Value ?? "").Length));
            for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
            {
                if (_lines >= MaxLines || _text.Length >= MaxCharacters) break;
                var row = rows[rowIndex];
                var cells = new string[columns];
                for (var col = 0; col < columns; col++)
                {
                    var value = col < row.Children.Count ? row.Children[col].Value ?? "" : "";
                    // Clip before normalization: a giant decoded cell must not
                    // be duplicated just to display its bounded prefix.
                    value = Clip(value, 24).Replace('\r', ' ').Replace('\n', ' ');
                    cells[col] = value.PadRight(widths[col]);
                }
                var rowText = $"{rowIndex + 1,4}  │ " + string.Join(" │ ", cells);
                if (row.Children.Count > columns)
                {
                    rowText += " │ …";
                    _truncated = true;
                }
                var rowStart = _text.Length;
                Add(rowText, "table-cell", row.Span, navigable: false);
                var displayed = Math.Min(rowText.Length, Math.Max(0, MaxCharacters - rowStart));
                var columnStart = 8; // Four-digit row ordinal plus the native column separator.
                for (var col = 0; col < columns && columnStart < displayed; col++)
                {
                    if (col < row.Children.Count && row.Children[col].Span.Length > 0)
                    {
                        var visible = Math.Min(cells[col].TrimEnd().Length, displayed - columnStart);
                        if (visible > 0)
                            _spans.Add(new NativePreviewSpan(rowStart + columnStart, visible,
                                "table-cell", new TextSpan(sourceBase + row.Children[col].Span.Start,
                                    row.Children[col].Span.Length)));
                    }
                    columnStart += cells[col].Length + 3;
                }
            }
        }

        public void Tree(SemanticNode root)
        {
            foreach (var child in root.Children) TreeNode(child, 0);
        }

        private void TreeNode(SemanticNode node, int depth)
        {
            if (_lines >= MaxLines || _text.Length >= MaxCharacters || depth > 8)
            { _truncated = true; return; }
            var kind = node.Kind switch
            {
                "property" or "entry" or "table" or "array-table" => "key",
                "string" or "scalar" => "string",
                "number" => "number",
                "boolean" or "null" or "alias" => "keyword",
                _ => "paragraph"
            };
            var label = Clip(node.Name is { Length: > 0 } name ? name : node.Kind, 120);
            if (node.Value is { Length: > 0 } value)
                label = label == node.Kind ? Clip(value, 120) :
                    Clip($"{label}: {Clip(value, 120)}", 120);
            Add(label, kind, node.Span, depth, emphasis: node.Kind is "table" or "array-table");
            foreach (var child in node.Children) TreeNode(child, depth + 1);
        }

        private static int ParseLevel(string? level) =>
            int.TryParse(level, out var parsed) ? Math.Clamp(parsed, 1, 6) : 1;

        private string Clip(string value, int limit)
        {
            if (value.Length <= limit) return value;
            _truncated = true;
            var length = Math.Max(0, limit - 1);
            if (length > 0 && char.IsHighSurrogate(value[length - 1])) --length;
            return value[..length] + "…";
        }
    }
}
