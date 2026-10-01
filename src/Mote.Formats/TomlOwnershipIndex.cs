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
    /// <summary>Large sessions bound memory; legacy whole-source validation may explicitly opt out.</summary>
    private readonly int _bindingLimit;
    private readonly Scope _root = new();
    private Scope? _current;
    private int _bindings;
    /// <summary>Opt-in public-policy recovery journal; normal large sessions allocate no journal.</summary>
    private readonly List<Mutation>? _journal;

    /// <summary>Starts at the root table with no user-defined bindings.</summary>
    internal TomlOwnershipIndex(int bindingLimit = MaxBindings, bool recover = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bindingLimit);
        _bindingLimit = bindingLimit;
        _journal = recover ? new List<Mutation>() : null;
        _current = _root;
    }

    /// <summary>Whether the trie has kept every binding needed for duplicate detection.</summary>
    internal bool IsExhaustive { get; private set; } = true;

    /// <summary>
    /// Whether source-order ownership is fully represented. Syntax validation belongs to
    /// the caller; every supported table/key transition is normative TOML ownership.
    /// </summary>
    internal bool IsCertifiable { get; private set; } = true;

    /// <summary>Opens a table or creates one new array-of-tables element.</summary>
    internal Diagnostic? AddHeader(KeySyntax key, bool array, int sourceOffset)
        => AddHeader(Parts(key), array, new TextSpan(sourceOffset + key.Span.Offset, key.Span.Length));

    /// <summary>Replays decoded statement semantics without retaining a parser syntax tree.</summary>
    internal Diagnostic? AddHeader(IReadOnlyList<string> parts, bool array, TextSpan keySpan)
    {
        var problem = AddHeaderCore(parts, array, keySpan);
        if (problem is not null && _journal is not null) { Rollback(); _current = null; }
        _journal?.Clear();
        return problem;
    }

    /// <summary>A failed header leaves its following assignments unowned until a valid header resumes.</summary>
    internal void InvalidateCurrentScope() => _current = null;

    /// <summary>Applies one header with optional reversible mutations for diagnostic recovery.</summary>
    private Diagnostic? AddHeaderCore(IReadOnlyList<string> parts, bool array, TextSpan keySpan)
    {
        if (parts.Count == 0) return Conflict("Invalid table path.", keySpan);
        var parent = ResolveParent(_root, parts, Origin.ImplicitHeader, keySpan, out var problem);
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
            // References can only reach the latest element. Earlier element namespaces
            // cannot be revisited, even when they contain nested table/array headers.
            SetScope(binding, new Scope());
            _current = binding.Scope;
            return null;
        }
        if (!array && binding.Origin == Origin.ImplicitHeader)
        {
            SetOrigin(binding, Origin.ExplicitTable);
            _current = binding.Scope!;
            return null;
        }
        return Conflict($"Table '{string.Join('.', parts)}' is already defined.", keySpan);
    }

    /// <summary>Registers one assignment in the current table, including dotted-key parents.</summary>
    internal Diagnostic? AddAssignment(KeySyntax key, ValueSyntax value, int sourceOffset)
        => AddAssignment(Parts(key), new TextSpan(sourceOffset + key.Span.Offset, key.Span.Length));

    /// <summary>All validated assignment values seal the external namespace, regardless of category.</summary>
    internal Diagnostic? AddAssignment(IReadOnlyList<string> parts, TextSpan keySpan)
    {
        // Syntax errors still surface independently. Unknown header context must not
        // manufacture ownership conflicts against the preceding valid table.
        if (_current is null) return null;
        var problem = AddAssignmentCore(parts, keySpan);
        if (problem is not null && _journal is not null) Rollback();
        _journal?.Clear();
        return problem;
    }

    /// <summary>Applies one assignment; failed dotted-parent state changes are reversible.</summary>
    private Diagnostic? AddAssignmentCore(IReadOnlyList<string> parts, TextSpan keySpan)
    {
        if (parts.Count == 0) return Conflict("Invalid key.", keySpan);
        var parent = ResolveParent(_current!, parts, Origin.Dotted, keySpan, out var problem);
        if (problem is not null) return problem;
        if (parent is null) return null;
        var name = parts[^1];
        if (parent.Children.ContainsKey(name))
            return Conflict($"Key '{string.Join('.', parts)}' is already defined.", keySpan);
        NewBinding(parent, name, Origin.Value);
        return null;
    }

    /// <summary>Traverses parents, preserving the latest element of every array table.</summary>
    private Scope? ResolveParent(Scope start, IReadOnlyList<string> parts, Origin newParentOrigin,
        TextSpan keySpan, out Diagnostic? problem)
    {
        problem = null;
        var scope = start;
        for (int i = 0; i < parts.Count - 1; i++)
        {
            if (!scope.Children.TryGetValue(parts[i], out var binding))
            {
                binding = NewBinding(scope, parts[i], newParentOrigin);
                if (binding is null) return null;
            }
            if (binding.Origin is Origin.Value or Origin.InlineTable)
            {
                problem = Conflict($"Key '{parts[i]}' cannot contain another key or table.", keySpan);
                return null;
            }
            // A header-created parent remains implicit until a dotted assignment
            // traverses it. That assignment defines the table, sealing later [table]
            // redeclaration while still allowing another dotted sibling.
            if (newParentOrigin == Origin.Dotted)
            {
                if (binding.Origin == Origin.ImplicitHeader) SetOrigin(binding, Origin.Dotted);
                else if (binding.Origin is Origin.ExplicitTable or Origin.ArrayTable)
                {
                    problem = Conflict($"Key '{parts[i]}' cannot redefine an explicitly defined table.", keySpan);
                    return null;
                }
            }
            scope = binding.Scope!;
        }
        return scope;
    }

    /// <summary>Limits memory for documents with unbounded unique-key cardinality.</summary>
    private Binding? NewBinding(Scope parent, string name, Origin origin)
    {
        if (_bindings == _bindingLimit) { IsExhaustive = false; return null; }
        _bindings++;
        var binding = new Binding(origin, origin is Origin.Value or Origin.InlineTable ? null : new Scope());
        parent.Children.Add(name, binding);
        _journal?.Add(new(MutationKind.Added, parent, name, binding, default, null));
        return binding;
    }

    /// <summary>Records only changed namespace state, never cloning the whole ownership index.</summary>
    private void SetOrigin(Binding binding, Origin origin)
    {
        _journal?.Add(new(MutationKind.Origin, null, null, binding, binding.Origin, null));
        binding.Origin = origin;
    }

    /// <summary>Latest-element replacement is reversible when recovering an invalid header.</summary>
    private void SetScope(Binding binding, Scope scope)
    {
        _journal?.Add(new(MutationKind.Scope, null, null, binding, default, binding.Scope));
        binding.Scope = scope;
    }

    /// <summary>Restores the exact certified prefix after a failed ownership transition.</summary>
    private void Rollback()
    {
        for (int i = _journal!.Count - 1; i >= 0; i--)
        {
            var mutation = _journal[i];
            if (mutation.Kind == MutationKind.Added)
            { mutation.Parent!.Children.Remove(mutation.Name!); _bindings--; }
            else if (mutation.Kind == MutationKind.Origin) mutation.Binding.Origin = mutation.Origin;
            else mutation.Binding.Scope = mutation.Scope;
        }
    }

    /// <summary>Uses decoded Tomlyn key components, so escaped-equivalent names collide.</summary>
    internal static List<string> Parts(KeySyntax key)
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
    private static Diagnostic Conflict(string message, TextSpan keySpan) =>
        new(DiagnosticSeverity.Error, "TOML_OWNERSHIP", message,
            keySpan);

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

    /// <summary>The three reversible namespace mutations produced by an ownership operation.</summary>
    private enum MutationKind { Added, Origin, Scope }
    /// <summary>One undo entry owned only for the duration of the current recoverable operation.</summary>
    private readonly record struct Mutation(MutationKind Kind, Scope? Parent, string? Name,
        Binding Binding, Origin Origin, Scope? Scope);
}
