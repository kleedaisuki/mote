using System.Text;
using Mote.Formats;

namespace Mote.Native;

/// <summary>A bounded, source-mapped native presentation, not an HTML web view.</summary>
internal sealed record NativePreview(string Text, IReadOnlyList<NativePreviewSpan> Spans);

/// <summary>
/// Projects policy semantic nodes into readable native text runs. The projection is
/// intentionally bounded; parsing and diagnostics still use the policy's full tree.
/// </summary>
internal static class NativePreviewBuilder
{
    private const int MaxCharacters = 16 * 1024;
    private const int MaxLines = 120;

    /// <summary>Builds a format-aware preview with global document source ranges.</summary>
    public static NativePreview Build(FormatAnalysis analysis, DocumentKind kind,
        int sourceBase, bool complete)
    {
        var output = new Builder(sourceBase);
        if (!complete) output.Add("Viewport sample — incomplete context", "comment", analysis.Root.Span);
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

    private sealed class Builder(int sourceBase)
    {
        private readonly StringBuilder _text = new();
        private readonly List<NativePreviewSpan> _spans = [];
        private int _lines;

        public NativePreview Finish()
        {
            if (_text.Length == 0) Add("(empty document)", "comment", new TextSpan(0, 0));
            if (_text.Length >= MaxCharacters || _lines >= MaxLines)
                _text.Append("… preview truncated …\n");
            return new NativePreview(_text.ToString(), _spans);
        }

        public void Add(string value, string kind, TextSpan source, int indent = 0,
            bool emphasis = false, bool blankAfter = false)
        {
            if (_lines >= MaxLines || _text.Length >= MaxCharacters) return;
            if (indent > 0) _text.Append(' ', Math.Min(indent, 8) * 2);
            var available = MaxCharacters - _text.Length;
            if (available <= 0) return;
            if (value.Length > available) value = value[..available];
            var start = _text.Length;
            _text.Append(value);
            if (value.Length > 0)
                _spans.Add(new NativePreviewSpan(start, value.Length, kind,
                    new TextSpan(sourceBase + source.Start, source.Length), emphasis));
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
            var lines = source.Split(['\r', '\n'], StringSplitOptions.None);
            foreach (var line in lines)
            {
                if (_lines >= MaxLines || _text.Length >= MaxCharacters) break;
                Add(line, "paragraph", span);
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
                    foreach (var line in (node.Value ?? "").Split('\n'))
                        Add(line.TrimEnd('\r'), "code", node.Span, depth + 1);
                    Add("", "code", node.Span);
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
                    Add("", "paragraph", node.Span);
                    break;
                case "quote":
                    foreach (var child in node.Children)
                    {
                        if (child.Kind == "paragraph")
                            Add("│ " + (child.Value ?? ""), "quote", child.Span, depth);
                        else Markdown(child, depth + 1);
                    }
                    Add("", "quote", node.Span);
                    break;
                case "thematic-break":
                    Add("────────────────────", "comment", node.Span, blankAfter: true);
                    break;
                default:
                    foreach (var child in node.Children) Markdown(child, depth);
                    break;
            }
        }

        public void Csv(SemanticNode root)
        {
            var rows = root.Children.Take(MaxLines).ToArray();
            if (rows.Length == 0) return;
            var columns = Math.Min(8, rows.Max(row => row.Children.Count));
            var widths = new int[columns];
            foreach (var row in rows)
                for (var col = 0; col < Math.Min(columns, row.Children.Count); col++)
                    widths[col] = Math.Max(widths[col], Math.Min(24, (row.Children[col].Value ?? "").Length));
            for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
            {
                var row = rows[rowIndex];
                var cells = new string[columns];
                for (var col = 0; col < columns; col++)
                {
                    var value = col < row.Children.Count ? row.Children[col].Value ?? "" : "";
                    value = value.Replace('\r', ' ').Replace('\n', ' ');
                    if (value.Length > 24) value = value[..23] + "…";
                    cells[col] = value.PadRight(widths[col]);
                }
                var rowText = $"{rowIndex + 1,4}  │ " + string.Join(" │ ", cells);
                if (row.Children.Count > columns) rowText += " │ …";
                Add(rowText, "table-cell", row.Span);
            }
        }

        public void Tree(SemanticNode root)
        {
            foreach (var child in root.Children) TreeNode(child, 0);
        }

        private void TreeNode(SemanticNode node, int depth)
        {
            if (_lines >= MaxLines || _text.Length >= MaxCharacters || depth > 8) return;
            var kind = node.Kind switch
            {
                "property" or "entry" or "table" or "array-table" => "key",
                "string" or "scalar" => "string",
                "number" => "number",
                "boolean" or "null" or "alias" => "keyword",
                _ => "paragraph"
            };
            var label = node.Name is { Length: > 0 } name ? name : node.Kind;
            if (node.Value is { Length: > 0 } value)
                label = label == node.Kind ? value : $"{label}: {value}";
            if (label.Length > 120) label = label[..119] + "…";
            Add(label, kind, node.Span, depth, emphasis: node.Kind is "table" or "array-table");
            foreach (var child in node.Children) TreeNode(child, depth + 1);
        }

        private static int ParseLevel(string? level) =>
            int.TryParse(level, out var parsed) ? Math.Clamp(parsed, 1, 6) : 1;
    }
}
