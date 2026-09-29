using System.Text;

namespace Mote.Formats;

/// <summary>Plain text has no imposed syntax or semantic validity rules.</summary>
public sealed class PlainTextPolicy : IIncrementalDocumentPolicy
{
    /// <inheritdoc />
    public DocumentKind Kind => DocumentKind.PlainText;
    /// <inheritdoc />
    public string DisplayName => "Plain text";

    /// <inheritdoc />
    public IFormatSession CreateSession() => new PlainIncrementalSession();

    /// <inheritdoc />
    public FormatAnalysis Analyze(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();
        return new FormatAnalysis(text, new SemanticNode("document", new TextSpan(0, text.Length)),
            Array.Empty<Diagnostic>(), Array.Empty<SemanticToken>());
    }

    /// <inheritdoc />
    public string Format(string text) => text ?? throw new ArgumentNullException(nameof(text));

    /// <inheritdoc />
    public string RenderHtml(FormatAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        return new StringBuilder("<pre class=\"mote-plain\">")
            .Append(FormatHelpers.HtmlEncode(analysis.SourceText)).Append("</pre>").ToString();
    }
}
