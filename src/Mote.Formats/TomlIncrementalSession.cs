using Mote.Engine;
using Tomlyn.Parsing;

namespace Mote.Formats;

/// <summary>
/// Per-document TOML analysis with validated whole-document semantics for ordinary files and
/// a bounded lexical viewport for large files. Compact validated statement IR supports edit
/// reuse; no parser tree, full source copy or value contents survive a call.
/// </summary>
/// <remarks>
/// Tomlyn's lossless syntax parser validates cross-table key ownership but requires
/// whole-document input. Its TextReader event parser also copies the entire input before
/// parsing and does not report duplicate-key/table or scalar-prefix conflicts that the
/// validated syntax parser detects. A slice cannot establish table context or whether it
/// begins inside a multiline value. Therefore large-file output is explicitly provisional:
/// a cold Visible request makes no global completeness claim. A validated cached snapshot
/// can also provide Complete Visible output. A Full request may become Complete only when every
/// bounded logical statement is individually accepted by Tomlyn, statement boundaries occur
/// only at top-level newlines outside strings/collections, and the trie holds every key binding.
/// Scalar and inline-table bindings seal their path. Header-created implicit parents may
/// become explicit tables or be defined by a dotted assignment, but never both. Nested
/// array-table headers always bind to the latest independently owned parent element,
/// including parent re-entry after nested headers. The normative ownership index, not
/// Tomlyn's whole-file array index, resolves those source-order transitions.
/// </remarks>
internal sealed class TomlIncrementalSession : IFormatSession
{
    private const int CompleteLimit = 4 * 1024 * 1024;
    private const int VisibleLimit = 256 * 1024;
    private const int Context = 4096;
    private const int MaxValueProjection = 4096;
    private readonly TomlPolicy _policy;
    /// <summary>One successfully committed Complete snapshot; never a history of parser roots.</summary>
    private TomlStatementCache? _cache;
    /// <summary>Rejects reentrant analysis/disposal; callers must serialize this document session.</summary>
    private bool _analyzing;

    /// <summary>Actual characters supplied to standalone syntax parsers in the latest large call.</summary>
    internal long LastParsedCharacters { get; private set; }
    /// <summary>Actual logical-boundary character visits in the latest large call.</summary>
    internal long LastScannedCharacters { get; private set; }
    /// <summary>External namespace actions replayed in the latest large call; trivia is excluded.</summary>
    internal int LastOwnershipTransitions { get; private set; }
    /// <summary>Deterministic cancellation/reentrancy seam for retained tests, never a public setting.</summary>
    internal Action<string>? AnalysisHook { get; set; }
    private bool _disposed;

    /// <summary>Creates isolated state for one document lifetime.</summary>
    internal TomlIncrementalSession(TomlPolicy policy) => _policy = policy;

    /// <inheritdoc />
    public DocumentAnalysis Analyze(TextSnapshot snapshot, IReadOnlyList<VersionedEdit> changesSinceCommittedState,
        AnalysisRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(changesSinceCommittedState);
        Validate(snapshot, request);
        cancellationToken.ThrowIfCancellationRequested();

        if (_analyzing) throw new InvalidOperationException("TOML session calls must be serialized and non-reentrant.");
        _analyzing = true;
        LastParsedCharacters = LastScannedCharacters = 0;
        LastOwnershipTransitions = 0;
        try
        {
            if (snapshot.Length <= CompleteLimit)
            {
                var small = AnalyzeComplete(snapshot, request, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                _cache = null;
                return small;
            }
            return AnalyzeWithCache(snapshot, changesSinceCommittedState, request, cancellationToken);
        }
        finally { _analyzing = false; }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_analyzing) throw new InvalidOperationException("TOML session calls must be serialized and non-reentrant.");
        _cache = null;
        AnalysisHook = null;
        _disposed = true;
    }

    /// <summary>Stages repair, global dependencies and projection before one cancellation-gated commit.</summary>
    private DocumentAnalysis AnalyzeWithCache(TextSnapshot snapshot, IReadOnlyList<VersionedEdit> edits,
        AnalysisRequest request, CancellationToken ct)
    {
        TomlStatementCache? candidate = null;
        Diagnostic? error = null;
        bool repaired = false;
        if (_cache is not null && ReferenceEquals(_cache.Snapshot, snapshot)) candidate = _cache;
        else if (_cache?.Repair(snapshot, edits, ct) is { } repair)
        {
            LastParsedCharacters = repair.ParsedCharacters;
            LastScannedCharacters = repair.ScannedCharacters;
            AnalysisHook?.Invoke("repair");
            if (repair.Cache is { } mapped)
            {
                repaired = true;
                if (repair.SameEffects || CheckOwnership(mapped.Statements, ct, out error)) candidate = mapped;
            }
        }
        if (candidate is null && !repaired && request.Scope == AnalysisScope.Full)
        {
            AnalysisHook?.Invoke("full");
            var outcome = TryAnalyzeLarge(snapshot, request.VisibleRange, ct);
            LastParsedCharacters += outcome.ParsedCharacters;
            LastScannedCharacters += outcome.ScannedCharacters;
            LastOwnershipTransitions += outcome.OwnershipTransitions;
            candidate = outcome.Cache;
            error = outcome.KnownError;
        }
        var lexical = AnalyzeVisible(snapshot, request.VisibleRange, ct);
        var result = candidate is not null ? ProjectCache(candidate, request.VisibleRange, lexical, ct)
            : error is not null ? new DocumentAnalysis(lexical.Version, lexical.Coverage, lexical.Completeness,
                lexical.Root, [error], lexical.Tokens, null) : lexical;
        AnalysisHook?.Invoke("projection");
        ct.ThrowIfCancellationRequested();
        _cache = candidate;
        return result;
    }

    /// <summary>Changed namespace effects invalidate all following off-screen dependencies.</summary>
    private bool CheckOwnership(IReadOnlyList<TomlStatement> statements, CancellationToken ct, out Diagnostic? error)
    {
        var index = new TomlOwnershipIndex();
        foreach (var statement in statements)
        {
            ct.ThrowIfCancellationRequested();
            if (statement.Summary.Action != TomlStatementAction.Trivia) LastOwnershipTransitions++;
            error = statement.Summary.Apply(index, statement.Start);
            if (error is not null || !index.IsExhaustive) return false;
        }
        error = null;
        return true;
    }

    /// <summary>A Complete projection can remain viewport-bounded without retaining whole-file nodes.</summary>
    private static DocumentAnalysis ProjectCache(TomlStatementCache cache, TextSpan visible,
        DocumentAnalysis lexical, CancellationToken ct)
    {
        var nodes = new List<SemanticNode>();
        foreach (var statement in cache.Statements)
        {
            ct.ThrowIfCancellationRequested();
            var node = statement.Summary.Project(statement.Start, visible);
            if (node is not null) nodes.Add(node);
        }
        return new DocumentAnalysis(cache.Snapshot.Version, new TextSpan(0, cache.Snapshot.Length),
            AnalysisCompleteness.Complete,
            new SemanticNode("document", new TextSpan(0, cache.Snapshot.Length), children: nodes),
            Array.Empty<Diagnostic>(), lexical.Tokens, 0);
    }

    /// <summary>Runs Tomlyn's whole-document syntax and semantic validation once.</summary>
    private DocumentAnalysis AnalyzeComplete(TextSnapshot snapshot, AnalysisRequest request, CancellationToken ct)
    {
        var source = snapshot.GetText();
        var analysis = _policy.Analyze(source, ct);
        ct.ThrowIfCancellationRequested();
        bool all = request.Scope == AnalysisScope.Full;
        var visible = request.VisibleRange;
        var children = new List<SemanticNode>();
        foreach (var child in analysis.Root.Children)
        {
            ct.ThrowIfCancellationRequested();
            var projected = Project(child, visible, all);
            if (projected is not null) children.Add(projected);
        }
        var diagnostics = all ? analysis.Diagnostics : analysis.Diagnostics.Where(d => Intersects(d.Span, visible)).ToArray();
        var tokens = all ? analysis.Tokens : analysis.Tokens.Where(t => Intersects(t.Span, visible)).ToArray();
        // Tomlyn may stop cross-key validation after a syntax error. Its diagnostic list
        // remains useful locally but cannot certify the unseen suffix or an exact total.
        var complete = analysis.Diagnostics.Count == 0;
        return new DocumentAnalysis(snapshot.Version, complete ? new TextSpan(0, snapshot.Length) : visible,
            complete ? AnalysisCompleteness.Complete : AnalysisCompleteness.Provisional,
            new SemanticNode("document", new TextSpan(0, snapshot.Length), children: children),
            diagnostics, tokens, complete ? analysis.Diagnostics.Count : null);
    }

    /// <summary>Classifies bounded source without asserting table context or global validity.</summary>
    private static DocumentAnalysis AnalyzeVisible(TextSnapshot snapshot, TextSpan visible, CancellationToken ct)
    {
        int start = Math.Max(0, visible.Start - Context);
        int wanted = (int)Math.Min(int.MaxValue, (long)visible.End - start + Context);
        int length = Math.Min(VisibleLimit, Math.Min(wanted, snapshot.Length - start));
        var source = snapshot.GetText(start, length);
        var lexer = TomlLexer.Create(source);
        var tokens = new List<SemanticToken>();
        while (lexer.MoveNext())
        {
            ct.ThrowIfCancellationRequested();
            string? kind = TomlPolicy.TokenKindName(lexer.Current.Kind);
            if (kind is null) continue;
            var span = lexer.CurrentSpan;
            int localStart = Math.Clamp(span.Offset, 0, source.Length);
            int localLength = Math.Clamp(span.Length, 0, source.Length - localStart);
            tokens.Add(new SemanticToken(kind, new TextSpan(start + localStart, localLength)));
        }
        return new DocumentAnalysis(snapshot.Version, new TextSpan(start, length),
            AnalysisCompleteness.Provisional,
            new SemanticNode("document", new TextSpan(0, snapshot.Length)),
            Array.Empty<Diagnostic>(), tokens, null);
    }

    /// <summary>Combines bounded lexical output with either certification or one proved error.</summary>
    internal static DocumentAnalysis AnalyzeLarge(TextSnapshot snapshot, TextSpan visible, CancellationToken ct)
    {
        var outcome = TryAnalyzeLarge(snapshot, visible, ct);
        var lexical = AnalyzeVisible(snapshot, visible, ct);
        if (!outcome.Complete)
            return outcome.KnownError is { } error
                ? new DocumentAnalysis(lexical.Version, lexical.Coverage, lexical.Completeness,
                    lexical.Root, new[] { error }, lexical.Tokens, null)
                : lexical;
        return ProjectCache(outcome.Cache!, visible, lexical, ct);
    }

    /// <summary>Validates streamed syntax plus normative ownership, retaining compact immutable IR only.</summary>
    private static LargeAnalysisResult TryAnalyzeLarge(TextSnapshot snapshot, TextSpan visible, CancellationToken ct)
    {
        var ownership = new TomlOwnershipIndex();
        Diagnostic? error = null;
        int transitions = 0;
        bool Accept(TomlStatement statement)
        {
            if (statement.Summary.Action != TomlStatementAction.Trivia) transitions++;
            error = statement.Summary.Apply(ownership, statement.Start);
            return error is null && ownership.IsExhaustive;
        }
        var scan = TomlStatementReader.Read(snapshot, 0, Accept, null, ct);
        if (!scan.Valid || !ownership.IsExhaustive)
            return new(false, ownership.IsExhaustive ? error : null, null,
                scan.ParsedCharacters, scan.ScannedCharacters, transitions);
        var statements = scan.Statements.ToArray();
        var cache = new TomlStatementCache(snapshot, statements);
        return new(true, null, cache, scan.ParsedCharacters, scan.ScannedCharacters, transitions);
    }

    /// <summary>Separates an exact valid certificate from a bounded first-error witness and work counts.</summary>
    private readonly record struct LargeAnalysisResult(bool Complete, Diagnostic? KnownError,
        TomlStatementCache? Cache, int ParsedCharacters, int ScannedCharacters,
        int OwnershipTransitions);

    /// <summary>Trims a complete semantic tree to the requested viewport without changing spans.</summary>
    private static SemanticNode? Project(SemanticNode node, TextSpan visible, bool all)
    {
        if (!all && !Intersects(node.Span, visible)) return null;
        var children = new List<SemanticNode>();
        foreach (var child in node.Children)
        {
            var projected = Project(child, visible, all);
            if (projected is not null) children.Add(projected);
        }
        string? value = node.Value is { Length: <= MaxValueProjection } ? node.Value : null;
        return new SemanticNode(node.Kind, node.Span, node.Name, value, children);
    }

    /// <summary>Includes zero-width diagnostics at the viewport boundary.</summary>
    private static bool Intersects(TextSpan candidate, TextSpan visible) =>
        candidate.Start <= visible.End && candidate.End >= visible.Start;

    /// <summary>Checks request coordinates before accessing any rope slice.</summary>
    private static void Validate(TextSnapshot snapshot, AnalysisRequest request)
    {
        var visible = request.VisibleRange;
        if (visible.Start < 0 || visible.Length < 0 || visible.Start > snapshot.Length ||
            visible.Length > snapshot.Length - visible.Start)
            throw new ArgumentOutOfRangeException(nameof(request));
        if (request.Scope is not (AnalysisScope.Visible or AnalysisScope.Full))
            throw new ArgumentOutOfRangeException(nameof(request));
    }
}
