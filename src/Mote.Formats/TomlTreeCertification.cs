using Tomlyn.Syntax;

namespace Mote.Formats;

/// <summary>
/// Certifies a grammar-clean lossless tree without reparsing or changing its ownership.
/// A false result publishes no partial findings: callers retain full source validation.
/// </summary>
internal static class TomlTreeCertification
{
    /// <summary>
    /// Checks required structure, local value namespaces and source-order global ownership.
    /// Scalar spelling/decoding remains the whole parser's responsibility. The tree must
    /// originate from that parser and remain exclusively read-only during this call.
    /// </summary>
    internal static bool TryCertify(DocumentSyntax syntax, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(syntax);
        cancellationToken.ThrowIfCancellationRequested();
        if (syntax.Diagnostics.Count != 0) return false;
        var ownership = new TomlOwnershipIndex(int.MaxValue);
        var pending = new Stack<ValueSyntax>();
        int position = -1;
        foreach (var pair in syntax.KeyValues)
            if (!Ordered(pair, ref position) || !Pair(pair, ownership, pending, cancellationToken)) return false;
        foreach (var table in syntax.Tables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Ordered(table, ref position) || table.OpenBracket is null || table.CloseBracket is null ||
                (table.EndOfLineToken is null && table.Items.ChildrenCount != 0) ||
                !Key(table.Name, cancellationToken)) return false;
            if (table is not (TableSyntax or TableArraySyntax) ||
                ownership.AddHeader(table.Name!, table is TableArraySyntax, 0) is not null) return false;
            foreach (var pair in table.Items)
                if (!Ordered(pair, ref position) || !Pair(pair, ownership, pending, cancellationToken)) return false;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return ownership.IsExhaustive && ownership.IsCertifiable;
    }

    /// <summary>Rejects reordered/recovered units rather than guessing their namespace context.</summary>
    private static bool Ordered(SyntaxNode node, ref int position)
    {
        if (node.Span.Offset < position) return false;
        position = node.Span.Offset;
        return true;
    }

    /// <summary>Seals the external binding only after every nested local value is certified.</summary>
    private static bool Pair(KeyValueSyntax pair, TomlOwnershipIndex ownership,
        Stack<ValueSyntax> pending, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (pair.EqualToken is null || !Key(pair.Key, cancellationToken) || pair.Value is null) return false;
        pending.Push(pair.Value);
        if (!Values(pending, cancellationToken)) return false;
        return ownership.AddAssignment(pair.Key!, pair.Value, 0) is null && ownership.IsExhaustive;
    }

    /// <summary>
    /// Uses an explicit stack, not CLR recursion. Each inline table owns a fresh trie;
    /// arrays and separately nested values never share an inline namespace.
    /// </summary>
    private static bool Values(Stack<ValueSyntax> pending, CancellationToken cancellationToken)
    {
        while (pending.TryPop(out var value))
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (value)
            {
                case ArraySyntax array:
                    if (!Array(array, pending, cancellationToken)) return false;
                    break;
                case InlineTableSyntax inline:
                    if (!Inline(inline, pending, cancellationToken)) return false;
                    break;
                case StringValueSyntax text when text.Token is not null:
                case IntegerValueSyntax integer when integer.Token is not null:
                case FloatValueSyntax number when number.Token is not null:
                case BooleanValueSyntax boolean when boolean.Token is not null:
                    break;
                case DateTimeValueSyntax date when date.Token is not null && date.Kind is
                    SyntaxKind.OffsetDateTimeByZ or SyntaxKind.OffsetDateTimeByNumber or
                    SyntaxKind.LocalDateTime or SyntaxKind.LocalDate or SyntaxKind.LocalTime:
                    break;
                default:
                    return false;
            }
        }
        return true;
    }

    /// <summary>Checks all item values and separators without imposing homogeneous array types.</summary>
    private static bool Array(ArraySyntax array, Stack<ValueSyntax> pending, CancellationToken cancellationToken)
    {
        if (array.OpenBracket is null || array.CloseBracket is null) return false;
        int count = array.Items.ChildrenCount;
        for (int i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = array.Items.GetChild(i)!;
            if (item.Value is null || (i + 1 < count && item.Comma is null)) return false;
            pending.Push(item.Value);
        }
        return true;
    }

    /// <summary>Certifies dotted/quoted key identity and sealing independently in each inline table.</summary>
    private static bool Inline(InlineTableSyntax inline, Stack<ValueSyntax> pending, CancellationToken cancellationToken)
    {
        if (inline.OpenBrace is null || inline.CloseBrace is null) return false;
        var ownership = new TomlOwnershipIndex(int.MaxValue);
        int count = inline.Items.ChildrenCount;
        for (int i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = inline.Items.GetChild(i)!;
            var pair = item.KeyValue;
            if (pair?.Value is null || pair.EqualToken is null || !Key(pair.Key, cancellationToken) ||
                (i + 1 < count && item.Comma is null)) return false;
            if (ownership.AddAssignment(pair.Key!, pair.Value, 0) is not null || !ownership.IsExhaustive) return false;
            pending.Push(pair.Value);
        }
        return true;
    }

    /// <summary>Never silently drops a missing dotted component or substitutes an empty unknown key.</summary>
    private static bool Key(KeySyntax? key, CancellationToken cancellationToken)
    {
        if (key is null || !KeyPart(key.Key)) return false;
        foreach (var part in key.DotKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (part.Dot is null || !KeyPart(part.Key)) return false;
        }
        return true;
    }

    /// <summary>Empty quoted names are valid; absent tokens or decoded names are not.</summary>
    private static bool KeyPart(BareKeyOrStringValueSyntax? part) => part switch
    {
        BareKeySyntax bare => bare.Key?.Text is not null,
        StringValueSyntax quoted => quoted.Token is not null && quoted.Value is not null,
        _ => false
    };
}
