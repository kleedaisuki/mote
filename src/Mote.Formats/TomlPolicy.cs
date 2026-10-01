using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace Mote.Formats;

/// <summary>Lossless TOML syntax with shared normative source-order ownership validation.</summary>
public sealed class TomlPolicy : IIncrementalDocumentPolicy
{
    /// <inheritdoc />
    public DocumentKind Kind => DocumentKind.Toml;
    /// <inheritdoc />
    public string DisplayName => "TOML";

    /// <inheritdoc />
    public IFormatSession CreateSession() => new TomlIncrementalSession(this);

    /// <inheritdoc />
    public FormatAnalysis Analyze(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();

        // Retain the lossless grammar tree, but do not delegate global array element identity
        // to Tomlyn's whole-file validator. The same normative ownership model serves all sizes.
        DocumentSyntax syntax = SyntaxParser.Parse(text, validate: false);
        var diagnostics = new List<Diagnostic>();
        foreach (var diagnostic in syntax.Diagnostics)
        {
            cancellationToken.ThrowIfCancellationRequested();
            diagnostics.Add(new Diagnostic(
                diagnostic.Kind == DiagnosticMessageKind.Error ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                "TOML_PARSE", diagnostic.Message, Span(diagnostic.Span, text.Length)));
        }
        // Local recovery cannot replace the whole grammar parser's independent witnesses.
        // Exact duplicate records are collapsed, never unrelated messages/spans.
        if (!TomlTreeCertification.TryCertify(syntax, cancellationToken))
        {
            var seen = new HashSet<Diagnostic>(diagnostics);
            foreach (var diagnostic in TomlDocumentValidation.Validate(text, cancellationToken))
                if (seen.Add(diagnostic)) diagnostics.Add(diagnostic);
        }
        diagnostics.Sort((left, right) => left.Span.Start.CompareTo(right.Span.Start));

        var tokens = new List<SemanticToken>();
        foreach (var token in syntax.Tokens(includeCommentsAndWhitespaces: true))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? kind = token switch
            {
                SyntaxToken syntaxToken => TokenKindName(syntaxToken.TokenKind),
                SyntaxTrivia trivia when trivia.Text?.StartsWith('#') == true => "comment",
                _ => null
            };
            if (kind is not null) tokens.Add(new SemanticToken(kind, Span(token.Span, text.Length)));
        }
        foreach (var node in syntax.Descendants())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node is KeySyntax key) tokens.Add(new SemanticToken("key", Span(key.Span, text.Length)));
        }
        tokens.Sort((a, b) => a.Span.Start.CompareTo(b.Span.Start));

        return new FormatAnalysis(text, Project(syntax, text.Length, cancellationToken), diagnostics, tokens);
    }

    /// <inheritdoc />
    public string Format(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var syntax = SyntaxParser.Parse(text, validate: false);
        if (!IsValid(syntax, text)) return text;

        // Only horizontal gaps directly adjacent to an assignment token are eligible. In
        // particular, this never rewrites a key/value token, a comment, or a newline.
        var edits = new List<GapEdit>();
        foreach (var node in syntax.Descendants())
        {
            if (node is not KeyValueSyntax pair || pair.Key is null || pair.EqualToken is null || pair.Value is null) continue;
            CollectGap(text, pair.Key.Span, pair.EqualToken.Span, edits);
            CollectGap(text, pair.EqualToken.Span, pair.Value.Span, edits);
        }
        if (edits.Count == 0) return text;
        edits.Sort((a, b) => b.Start.CompareTo(a.Start));
        string candidate = ApplyReverse(text, edits);
        var reparsed = SyntaxParser.Parse(candidate, validate: false);
        if (!IsValid(reparsed, candidate) ||
            !Equivalent(Project(syntax, text.Length), Project(reparsed, candidate.Length))) return text;
        return candidate;
    }

    /// <summary>Unknown certification retains the established full validation, not a formatting rejection.</summary>
    private static bool IsValid(DocumentSyntax syntax, string text) => !syntax.HasErrors &&
        (TomlTreeCertification.TryCertify(syntax) || TomlDocumentValidation.Validate(text, default).Count == 0);

    /// <inheritdoc />
    public string RenderHtml(FormatAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        return FormatHelpers.RenderTree(analysis, "mote-toml");
    }

    /// <summary>Projects the validated syntax tree without relying on physical line boundaries.</summary>
    private static SemanticNode Project(DocumentSyntax syntax, int length, CancellationToken cancellationToken = default)
    {
        var children = new List<SemanticNode>();
        foreach (var pair in syntax.KeyValues)
        {
            cancellationToken.ThrowIfCancellationRequested();
            children.Add(ConvertEntry(pair, length));
        }
        foreach (var table in syntax.Tables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entries = new List<SemanticNode>();
            foreach (var pair in table.Items) entries.Add(ConvertEntry(pair, length));
            children.Add(new SemanticNode(table is TableArraySyntax ? "array-table" : "table",
                Span(table.Span, length), table.Name?.ToString().Trim(), children: entries));
        }
        children.Sort((a, b) => a.Span.Start.CompareTo(b.Span.Start));
        return new SemanticNode("document", new TextSpan(0, length), children: children);
    }

    /// <summary>A horizontal whitespace range replaced by one ASCII space.</summary>
    private readonly record struct GapEdit(int Start, int Length);

    /// <summary>Adds only parser-delimited, horizontal-whitespace gaps to the edit set.</summary>
    private static void CollectGap(string text, SourceSpan left, SourceSpan right, List<GapEdit> edits)
    {
        int start = left.Offset + left.Length;
        int end = right.Offset;
        if (start < 0 || end < start || end > text.Length) return;
        if (end - start == 1 && text[start] == ' ') return;
        for (int i = start; i < end; i++)
            if (text[i] is not (' ' or '\t')) return;
        edits.Add(new GapEdit(start, end - start));
    }

    /// <summary>Applies source-anchored edits from right to left, avoiding offset drift.</summary>
    private static string ApplyReverse(string text, List<GapEdit> edits)
    {
        int outputLength = text.Length;
        foreach (var edit in edits) outputLength += 1 - edit.Length;
        var output = new char[outputLength];
        int readEnd = text.Length, writeEnd = outputLength;
        foreach (var edit in edits)
        {
            int tail = readEnd - edit.Start - edit.Length;
            if (tail < 0) return text;
            writeEnd -= tail;
            text.CopyTo(edit.Start + edit.Length, output, writeEnd, tail);
            output[--writeEnd] = ' ';
            readEnd = edit.Start;
        }
        text.CopyTo(0, output, 0, readEnd);
        return new string(output);
    }

    /// <summary>Requires the same semantic hierarchy after formatting; spans may change.</summary>
    private static bool Equivalent(SemanticNode before, SemanticNode after)
    {
        if (before.Kind != after.Kind || before.Name != after.Name || before.Value != after.Value ||
            before.Children.Count != after.Children.Count) return false;
        for (int i = 0; i < before.Children.Count; i++)
            if (!Equivalent(before.Children[i], after.Children[i])) return false;
        return true;
    }

    /// <summary>Projects one validated assignment while retaining the original source extent.</summary>
    private static SemanticNode ConvertEntry(KeyValueSyntax pair, int length)
    {
        var children = pair.Value is null ? Array.Empty<SemanticNode>() : new[] { ConvertValue(pair.Value, length) };
        return new SemanticNode("entry", Span(pair.Span, length), pair.Key?.ToString().Trim(), children: children);
    }

    /// <summary>Recursively projects arrays and inline tables into format-neutral semantic nodes.</summary>
    private static SemanticNode ConvertValue(ValueSyntax value, int length)
    {
        var span = Span(value.Span, length);
        if (value is ArraySyntax array)
        {
            var children = new List<SemanticNode>();
            foreach (var item in array.Items)
                if (item.Value is not null) children.Add(ConvertValue(item.Value, length));
            return new SemanticNode("array", span, children: children);
        }
        if (value is InlineTableSyntax inline)
        {
            var children = new List<SemanticNode>();
            foreach (var item in inline.Items)
                if (item.KeyValue is not null) children.Add(ConvertEntry(item.KeyValue, length));
            return new SemanticNode("inline-table", span, children: children);
        }
        string kind = value switch
        {
            StringValueSyntax => "string",
            IntegerValueSyntax or FloatValueSyntax => "number",
            BooleanValueSyntax => "boolean",
            DateTimeValueSyntax => "datetime",
            _ => "invalid"
        };
        string? normalized = value is StringValueSyntax str ? str.Value : value.ToString();
        return new SemanticNode(kind, span, value: normalized);
    }

    /// <summary>Clamps parser recovery spans to the current immutable source snapshot.</summary>
    private static TextSpan Span(SourceSpan source, int textLength)
    {
        int start = Math.Clamp(source.Offset, 0, textLength);
        return new TextSpan(start, Math.Clamp(source.Length, 0, textLength - start));
    }

    /// <summary>Maps lexical kinds to stable editor classifications without emitting trivia noise.</summary>
    internal static string? TokenKindName(TokenKind kind) => kind switch
    {
        TokenKind.String or TokenKind.StringMulti or TokenKind.StringLiteral or TokenKind.StringLiteralMulti => "string",
        TokenKind.Integer or TokenKind.IntegerHexa or TokenKind.IntegerOctal or TokenKind.IntegerBinary or
            TokenKind.Float or TokenKind.Infinite or TokenKind.PositiveInfinite or TokenKind.NegativeInfinite or
            TokenKind.Nan or TokenKind.PositiveNan or TokenKind.NegativeNan => "number",
        TokenKind.True or TokenKind.False => "boolean",
        TokenKind.OffsetDateTimeByZ or TokenKind.OffsetDateTimeByNumber or TokenKind.LocalDateTime or
            TokenKind.LocalDate or TokenKind.LocalTime => "datetime",
        TokenKind.Comment => "comment",
        TokenKind.OpenBracket or TokenKind.CloseBracket or TokenKind.OpenBracketDouble or TokenKind.CloseBracketDouble => "table",
        _ => null
    };
}
