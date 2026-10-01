using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace Mote.Formats;

/// <summary>The externally observable namespace action of one validated logical statement.</summary>
internal enum TomlStatementAction { Trivia, Assignment, Table, ArrayTable }

/// <summary>
/// Compact syntax/ownership IR. Spans are statement-local; only decoded key names and the
/// displayed key spelling survive parsing. No value contents, source copy or syntax tree survives.
/// </summary>
/// <param name="Length">Whole logical-unit length, including preserved trivia/newline.</param>
/// <param name="Action">The validated external namespace transition.</param>
/// <param name="Keys">Ordinal decoded components; owned immutable after construction.</param>
/// <param name="KeySpan">Statement-local ownership-diagnostic anchor.</param>
/// <param name="NodeSpan">Statement-local projected entry/header extent.</param>
/// <param name="ValueSpan">Statement-local value extent, without retaining its contents.</param>
/// <param name="Name">Displayed key/header spelling, absent for trivia.</param>
/// <param name="ValueKind">Existing semantic category, absent for headers/trivia.</param>
internal sealed record TomlStatementSummary(int Length, TomlStatementAction Action,
    IReadOnlyList<string> Keys, TextSpan KeySpan, TextSpan NodeSpan, TextSpan ValueSpan,
    string? Name, string? ValueKind)
{
    /// <summary>Validates grammar and inline ownership before extracting external namespace effects.</summary>
    internal static TomlStatementSummary? Parse(string source)
        => Parse(source, out _);

    /// <summary>Preserves local parser diagnostics for unbounded public-policy validation.</summary>
    internal static TomlStatementSummary? Parse(string source, out IReadOnlyList<Diagnostic> diagnostics)
        => Parse(source, out diagnostics, out _);

    /// <summary>Retains recovery header identity without treating invalid leading trivia as valid syntax.</summary>
    internal static TomlStatementSummary? Parse(string source, out IReadOnlyList<Diagnostic> diagnostics,
        out bool headerUnit)
    {
        var syntax = SyntaxParser.Parse(source, validate: true);
        headerUnit = syntax.Tables.Any() || HasHeaderPrefix(source);
        diagnostics = syntax.Diagnostics.Count == 0 ? Array.Empty<Diagnostic>() : syntax.Diagnostics.Select(error =>
            new Diagnostic(error.Kind == DiagnosticMessageKind.Error ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                "TOML_PARSE", error.Message, DiagnosticSpan(error.Span, source.Length))).ToArray();
        if (diagnostics.Count != 0) return null;
        var pairs = syntax.KeyValues.ToArray();
        var tables = syntax.Tables.ToArray();
        if (pairs.Length + tables.Length == 0)
            return new(source.Length, TomlStatementAction.Trivia, [], default, default, default, null, null);
        if (pairs.Length + tables.Length != 1) return null;
        if (pairs.Length == 1) return FromPair(source.Length, pairs[0]);
        var table = tables[0];
        if (table.Name is null || table.Items.Any()) return null;
        return new(source.Length, table is TableArraySyntax ? TomlStatementAction.ArrayTable : TomlStatementAction.Table,
            TomlOwnershipIndex.Parts(table.Name), Span(table.Name.Span), Span(table.Span), default,
            table.Name.ToString().Trim(), null);
    }

    /// <summary>
    /// Conservative recovery-only classification. Broad whitespace/control skipping never
    /// certifies source; the parser still rejects all non-TOML leading characters.
    /// </summary>
    private static bool HasHeaderPrefix(string source)
    {
        foreach (char ch in source)
        {
            if (char.IsWhiteSpace(ch) || char.IsControl(ch)) continue;
            return ch == '[';
        }
        return false;
    }

    /// <summary>All validated values seal their external path, independent of their semantic category.</summary>
    private static TomlStatementSummary? FromPair(int length, KeyValueSyntax pair)
    {
        if (pair.Key is null || pair.Value is null) return null;
        return new(length, TomlStatementAction.Assignment, TomlOwnershipIndex.Parts(pair.Key),
            Span(pair.Key.Span), Span(pair.Span), Span(pair.Value.Span), pair.Key.ToString().Trim(), Kind(pair.Value));
    }

    /// <summary>Replays this action at its current absolute source position.</summary>
    internal Diagnostic? Apply(TomlOwnershipIndex ownership, int start) => Action switch
    {
        TomlStatementAction.Assignment => ownership.AddAssignment(Keys, Shift(KeySpan, start)),
        TomlStatementAction.Table => ownership.AddHeader(Keys, false, Shift(KeySpan, start)),
        TomlStatementAction.ArrayTable => ownership.AddHeader(Keys, true, Shift(KeySpan, start)),
        _ => null
    };

    /// <summary>Exact dependency equality: syntax/value changes cannot affect unchanged namespace actions.</summary>
    internal bool HasSameEffect(TomlStatementSummary other) =>
        Action == other.Action && Keys.SequenceEqual(other.Keys, StringComparer.Ordinal);

    /// <summary>Projects current coordinates without materializing the source or the value.</summary>
    internal SemanticNode? Project(int start, TextSpan visible)
    {
        if (Action == TomlStatementAction.Trivia) return null;
        var span = Shift(NodeSpan, start);
        if (span.Start > visible.End || span.End < visible.Start) return null;
        return Action == TomlStatementAction.Assignment
            ? new SemanticNode("entry", span, Name, children: [new SemanticNode(ValueKind!, Shift(ValueSpan, start))])
            : new SemanticNode(Action == TomlStatementAction.ArrayTable ? "array-table" : "table", span, Name);
    }

    /// <summary>Preserves the established semantic categories of the large-file projection.</summary>
    private static string Kind(ValueSyntax value) => value switch
    {
        StringValueSyntax => "string",
        IntegerValueSyntax or FloatValueSyntax => "number",
        BooleanValueSyntax => "boolean",
        DateTimeValueSyntax => "datetime",
        ArraySyntax => "array",
        InlineTableSyntax => "inline-table",
        _ => "invalid"
    };

    /// <summary>Copies coordinates, never parser ownership or source storage.</summary>
    private static TextSpan Span(SourceSpan span) => new(span.Offset, span.Length);
    /// <summary>Parser EOF sentinels are zero-width inside the actual statement, never one unit beyond it.</summary>
    private static TextSpan DiagnosticSpan(SourceSpan span, int length)
    {
        int start = Math.Clamp(span.Offset, 0, length);
        return new(start, Math.Clamp(span.Length, 0, length - start));
    }
    /// <summary>Maps statement-local coordinates into the current immutable snapshot.</summary>
    private static TextSpan Shift(TextSpan span, int start) => new(start + span.Start, span.Length);
}

/// <summary>A positioned IR summary; edits shift positions while sharing unchanged summaries.</summary>
/// <param name="Start">Absolute current UTF-16 position of the certified statement seam.</param>
/// <param name="Summary">Immutable validated syntax and namespace effect.</param>
internal readonly record struct TomlStatement(int Start, TomlStatementSummary Summary)
{
    /// <summary>Exclusive current source seam, including the preserved newline.</summary>
    internal int End => Start + Summary.Length;
}
