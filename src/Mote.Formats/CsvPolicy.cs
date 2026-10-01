using System.Text;

namespace Mote.Formats;

/// <summary>
/// RFC 4180-style comma-separated records. The first row is not assumed to be a header:
/// CSV itself has no universal header convention, and inventing one would be false semantics.
/// </summary>
public sealed class CsvPolicy : IIncrementalDocumentPolicy
{
    /// <inheritdoc />
    public DocumentKind Kind => DocumentKind.Csv;
    /// <inheritdoc />
    public string DisplayName => "CSV";

    /// <inheritdoc />
    public IFormatSession CreateSession() => new CsvIncrementalSession();

    /// <inheritdoc />
    public FormatAnalysis Analyze(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        var rows = new List<SemanticNode>();
        var diagnostics = new List<Diagnostic>();
        var tokens = new List<SemanticToken>();
        if (text.Length == 0)
            return new FormatAnalysis(text, new SemanticNode("table", new TextSpan(0, 0), children: rows), diagnostics, tokens);

        var offset = 0;
        var expectedColumns = -1;
        while (offset < text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rowStart = offset;
            var cells = new List<SemanticNode>();
            var endedRecord = false;
            while (!endedRecord)
            {
                var start = offset;
                string value;
                if (offset < text.Length && text[offset] == '"')
                {
                    offset++;
                    var decoded = new StringBuilder();
                    var closed = false;
                    while (offset < text.Length)
                    {
                        if ((offset & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                        var c = text[offset++];
                        if (c != '"') { decoded.Append(c); continue; }
                        if (offset < text.Length && text[offset] == '"')
                        {
                            decoded.Append('"'); offset++; continue;
                        }
                        closed = true;
                        break;
                    }
                    if (!closed)
                        diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "CSV001", "Unterminated quoted field.",
                            new TextSpan(start, offset - start)));
                    if (closed && offset < text.Length && text[offset] is not (',' or '\r' or '\n'))
                    {
                        var invalidStart = offset;
                        while (offset < text.Length && text[offset] is not (',' or '\r' or '\n')) offset++;
                        diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "CSV002", "Characters after a closing quote are not valid in a CSV field.",
                            new TextSpan(invalidStart, offset - invalidStart)));
                    }
                    value = decoded.ToString();
                    tokens.Add(new SemanticToken("string", new TextSpan(start, offset - start)));
                }
                else
                {
                    while (offset < text.Length && text[offset] is not (',' or '\r' or '\n'))
                    {
                        if ((offset & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                        if (text[offset] == '"')
                            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "CSV003", "Quotes must enclose an entire field.", new TextSpan(offset, 1)));
                        offset++;
                    }
                    value = text[start..offset];
                }
                cells.Add(new SemanticNode("cell", new TextSpan(start, offset - start), value: value));
                if (offset < text.Length && text[offset] == ',') { offset++; continue; }
                endedRecord = true;
            }
            var rowEnd = offset;
            rows.Add(new SemanticNode("row", new TextSpan(rowStart, rowEnd - rowStart), children: cells));
            if (expectedColumns < 0) expectedColumns = cells.Count;
            else if (cells.Count != expectedColumns)
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "CSV004",
                    $"Row has {cells.Count} columns; the first row has {expectedColumns}.", new TextSpan(rowStart, rowEnd - rowStart)));
            if (offset < text.Length)
            {
                if (text[offset] == '\r' && offset + 1 < text.Length && text[offset + 1] == '\n') offset += 2;
                else offset++;
            }
        }
        return new FormatAnalysis(text, new SemanticNode("table", new TextSpan(0, text.Length), children: rows), diagnostics, tokens);
    }

    /// <inheritdoc />
    public string Format(string text)
    {
        var analysis = Analyze(text);
        if (FormatHelpers.HasErrors(analysis)) return text;
        if (analysis.Root.Children.Count == 0) return text;
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var result = new StringBuilder(text.Length);
        for (var i = 0; i < analysis.Root.Children.Count; i++)
        {
            if (i > 0) result.Append(newline);
            var cells = analysis.Root.Children[i].Children;
            for (var j = 0; j < cells.Count; j++)
            {
                if (j > 0) result.Append(',');
                var value = cells[j].Value ?? string.Empty;
                var quote = value.IndexOfAny([',', '"', '\r', '\n']) >= 0;
                if (quote) result.Append('"');
                foreach (var c in value)
                {
                    if (c == '"') result.Append('"');
                    result.Append(c);
                }
                if (quote) result.Append('"');
            }
        }
        if (text.EndsWith("\r\n", StringComparison.Ordinal) || text.EndsWith('\n') || text.EndsWith('\r')) result.Append(newline);
        return result.ToString();
    }

    /// <inheritdoc />
    public string RenderHtml(FormatAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        var result = new StringBuilder("<table class=\"mote-csv\"><tbody>");
        foreach (var row in analysis.Root.Children)
        {
            result.Append("<tr>");
            foreach (var cell in row.Children)
                result.Append("<td>").Append(FormatHelpers.HtmlEncode(cell.Value)).Append("</td>");
            result.Append("</tr>");
        }
        return result.Append("</tbody></table>").ToString();
    }
}
