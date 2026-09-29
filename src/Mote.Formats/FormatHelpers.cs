using System.Text;

namespace Mote.Formats;

/// <summary>Small, allocation-conscious helpers shared by statically linked policies.</summary>
internal static class FormatHelpers
{
    /// <summary>Escapes text for HTML element content and attribute values.</summary>
    internal static string HtmlEncode(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var result = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '&': result.Append("&amp;"); break;
                case '<': result.Append("&lt;"); break;
                case '>': result.Append("&gt;"); break;
                case '"': result.Append("&quot;"); break;
                case '\'': result.Append("&#39;"); break;
                default: result.Append(c); break;
            }
        }
        return result.ToString();
    }

    /// <summary>Returns true if any diagnostic makes source-preserving formatting necessary.</summary>
    internal static bool HasErrors(FormatAnalysis analysis) =>
        analysis.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>Renders a semantic tree without interpreting any source text as markup.</summary>
    internal static string RenderTree(FormatAnalysis analysis, string cssClass)
    {
        var html = new StringBuilder("<div class=\"mote-structure ");
        html.Append(HtmlEncode(cssClass)).Append("\"><ul>");
        foreach (var child in analysis.Root.Children) AppendNode(html, child, 0);
        return html.Append("</ul></div>").ToString();
    }

    /// <summary>Bounds recursive HTML projection independently of parser nesting limits.</summary>
    private static void AppendNode(StringBuilder html, SemanticNode node, int depth)
    {
        html.Append("<li data-start=\"").Append(node.Span.Start)
            .Append("\" data-length=\"").Append(node.Span.Length).Append("\">")
            .Append("<span class=\"mote-kind\">").Append(HtmlEncode(node.Kind)).Append("</span>");
        if (node.Name is not null)
            html.Append(" <span class=\"mote-name\">").Append(HtmlEncode(node.Name)).Append("</span>");
        if (node.Value is not null)
            html.Append(" <span class=\"mote-value\">").Append(HtmlEncode(node.Value)).Append("</span>");
        if (node.Children.Count > 0)
        {
            if (depth >= 512) html.Append(" <span class=\"mote-truncated\">…</span>");
            else
            {
                html.Append("<ul>");
                foreach (var child in node.Children) AppendNode(html, child, depth + 1);
                html.Append("</ul>");
            }
        }
        html.Append("</li>");
    }
}
