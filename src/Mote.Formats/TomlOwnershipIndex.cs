using Tomlyn.Syntax;

namespace Mote.Formats;

/// <summary>
/// Bounded table/key ownership trie for TOML logical statements. It distinguishes implicit
/// parents created by table headers from those created by dotted assignments, and gives each
/// array-of-tables element an independent namespace.
/// </summary>
/// <remarks>
/// This index is not a TOML syntax parser. Callers must validate each statement before adding
/// it; an incomplete statement stream must never turn this index into a Complete analysis.
/// </remarks>
internal sealed class TomlOwnershipIndex
{
    private const int MaxBindings = 200_000;
    private readonly Scope _root = new();
    private Scope _current;
    private int _bindings;

    /// <summary>Starts at the root table with no user-defined bindings.</summary>
    internal TomlOwnershipIndex() => _current = _root;

    /// <summary>Whether the trie has kept every binding needed for duplicate detection.</summary>
    internal bool IsExhaustive { get; private set; } = true;

    /// <summary>
    /// Whether table-array use stayed in the source-order subset that matches Tomlyn's
    /// whole-document validator: repeated root array tables without nested table headers.
    /// </summary>
    internal bool IsCertifiable { get; private set; } = true;

    /// <summary>Opens a table or creates one new array-of-tables element.</summary>
    internal Diagnostic? AddHeader(KeySyntax key, bool array, int sourceOffset)
    {
        var parts = Parts(key);
        if (parts.Count == 0) return Conflict("Invalid table path.", key, sourceOffset);
        if (array && parts.Count != 1) IsCertifiable = false;
        var parent = ResolveParent(_root, parts, true, key, sourceOffset, out var problem);
        if (problem is not null) return problem;
        if (parent is null) return null;
        var name = parts[^1];
        if (!parent.Children.TryGetValue(name, out var binding))
        {
            binding = NewBinding(parent, name, array ? Origin.ArrayTable : Origin.ExplicitTable);
            if (binding is null) return null;
            _current = binding.Scope!;
            return null;
        }
        if (array && binding.Origin == Origin.ArrayTable)
        {
            binding.Scope = new Scope();
            _current = binding.Scope;
            return null;
        }
        if (!array && binding.Origin == Origin.ImplicitHeader)
        {
            binding.Origin = Origin.ExplicitTable;
            _current = binding.Scope!;
            return null;
        }
        return Conflict($"Table '{string.Join('.', parts)}' is already defined.", key, sourceOffset);
    }

    /// <summary>Registers one assignment in the current table, including dotted-key parents.</summary>
    internal Diagnostic? AddAssignment(KeySyntax key, ValueSyntax value, int sourceOffset)
    {
        var parts = Parts(key);
        if (parts.Count == 0) return Conflict("Invalid key.", key, sourceOffset);
        var parent = ResolveParent(_current, parts, false, key, sourceOffset, out var problem);
        if (problem is not null) return problem;
        if (parent is null) return null;
        var name = parts[^1];
        if (parent.Children.ContainsKey(name))
            return Conflict($"Key '{string.Join('.', parts)}' is already defined.", key, sourceOffset);
        NewBinding(parent, name, value is InlineTableSyntax ? Origin.InlineTable : Origin.Value);
        return null;
    }

    /// <summary>Traverses parents, preserving the latest element of every array table.</summary>
    private Scope? ResolveParent(Scope start, IReadOnlyList<string> parts, bool headerParents,
        KeySyntax key, int sourceOffset, out Diagnostic? problem)
    {
        problem = null;
        var scope = start;
        for (int i = 0; i < parts.Count - 1; i++)
        {
            if (!scope.Children.TryGetValue(parts[i], out var binding))
            {
                binding = NewBinding(scope, parts[i], headerParents ? Origin.ImplicitHeader : Origin.Dotted);
                if (binding is null) return null;
            }
            if (binding.Origin is Origin.Value or Origin.InlineTable)
            {
                problem = Conflict($"Key '{parts[i]}' cannot contain another key or table.", key, sourceOffset);
                return null;
            }
            if (headerParents && binding.Origin == Origin.ArrayTable)
                IsCertifiable = false;
            scope = binding.Scope!;
        }
        return scope;
    }

    /// <summary>Limits memory for documents with unbounded unique-key cardinality.</summary>
    private Binding? NewBinding(Scope parent, string name, Origin origin)
    {
        if (_bindings == MaxBindings) { IsExhaustive = false; return null; }
        _bindings++;
        var binding = new Binding(origin, origin is Origin.Value or Origin.InlineTable ? null : new Scope());
        parent.Children.Add(name, binding);
        return binding;
    }

    /// <summary>Uses decoded Tomlyn key components, so escaped-equivalent names collide.</summary>
    private static List<string> Parts(KeySyntax key)
    {
        var parts = new List<string>();
        if (key.Key is not null) parts.Add(Name(key.Key));
        foreach (var item in key.DotKeys)
            if (item.Key is not null) parts.Add(Name(item.Key));
        return parts;
    }

    /// <summary>Extracts the semantic key name rather than its quoted source spelling.</summary>
    private static string Name(BareKeyOrStringValueSyntax key) => key switch
    {
        BareKeySyntax bare => bare.Key?.Text ?? string.Empty,
        StringValueSyntax quoted => quoted.Value ?? string.Empty,
        _ => string.Empty
    };

    /// <summary>Anchors ownership conflicts at the statement's actual key.</summary>
    private static Diagnostic Conflict(string message, KeySyntax key, int sourceOffset) =>
        new(DiagnosticSeverity.Error, "TOML_OWNERSHIP", message,
            new TextSpan(sourceOffset + key.Span.Offset, key.Span.Length));

    /// <summary>One table namespace, independent for each array-table element.</summary>
    private sealed class Scope
    {
        internal Dictionary<string, Binding> Children { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>Ownership state of one path component.</summary>
    private sealed class Binding(Origin origin, Scope? scope)
    {
        internal Origin Origin = origin;
        internal Scope? Scope = scope;
    }

    /// <summary>Only implicit header parents may later become explicit tables.</summary>
    private enum Origin { ImplicitHeader, Dotted, ExplicitTable, ArrayTable, Value, InlineTable }
}
