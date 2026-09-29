using System.Buffers;
using System.Text;
using Mote.Engine;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace Mote.Formats;

/// <summary>
/// Per-document TOML analysis with validated whole-document semantics for ordinary files and
/// a bounded lexical viewport for large files. No parser tree or source copy survives a call.
/// </summary>
/// <remarks>
/// Tomlyn's lossless syntax parser validates cross-table key ownership but requires
/// whole-document input. Its TextReader event parser also copies the entire input before
/// parsing and does not report duplicate-key/table or scalar-prefix conflicts that the
/// validated syntax parser detects. A slice cannot establish table context or whether it
/// begins inside a multiline value. Therefore large-file output is explicitly provisional:
/// it makes no semantic or diagnostic completeness claim for a Visible request. A Full request
/// may become Complete only for the restricted language accepted by TryAnalyzeLarge: every
/// bounded logical statement is individually accepted by Tomlyn, statement boundaries occur
/// only at top-level newlines outside strings/collections, and the trie holds every key binding.
/// Scalar and inline-table bindings seal their path; only header-created implicit parents may
/// later become explicit tables. Array-of-tables documents remain Provisional because Tomlyn's
/// validated source-order behavior differs from the trie in nested re-entry cases.
/// </remarks>
internal sealed class TomlIncrementalSession : IFormatSession
{
    private const int CompleteLimit = 4 * 1024 * 1024;
    private const int VisibleLimit = 256 * 1024;
    private const int Context = 4096;
    private const int MaxValueProjection = 4096;
    private const int MaxStatement = 256 * 1024;
    private const int MaxStatementLines = 64;
    private const int MaxStatements = 120_000;
    private readonly TomlPolicy _policy;
    private long? _committedVersion;
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

        // The authoritative immutable snapshot is always reparsed; a missing or malformed
        // change chain cannot make this session publish stale facts. We record only version.
        var result = snapshot.Length <= CompleteLimit
            ? AnalyzeComplete(snapshot, request, cancellationToken)
            : request.Scope == AnalysisScope.Full && TryAnalyzeLarge(snapshot, request.VisibleRange, cancellationToken) is { } complete
                ? complete
                : AnalyzeVisible(snapshot, request.VisibleRange, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (_committedVersion is null || snapshot.Version >= _committedVersion)
            _committedVersion = snapshot.Version;
        return result;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _committedVersion = null;
        _disposed = true;
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
        int length = Math.Min(VisibleLimit, snapshot.Length - start);
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

    /// <summary>
    /// Certifies line-delimited TOML with independently valid logical statements and a bounded
    /// ownership trie. Unsupported, malformed, or oversized statements fall back to Provisional.
    /// A 64-line cap bounds repeated continuation scans even for many tiny physical lines.
    /// </summary>
    private static DocumentAnalysis? TryAnalyzeLarge(TextSnapshot snapshot, TextSpan visible, CancellationToken ct)
    {
        using var reader = new SnapshotTextReader(snapshot, 0, snapshot.Length, ct);
        var buffer = ArrayPool<char>.Shared.Rent(8192);
        var statement = new StringBuilder();
        var ownership = new TomlOwnershipIndex();
        var nodes = new List<SemanticNode>();
        int offset = 0, statementStart = 0, count = 0, statementLines = 0;
        try
        {
            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                for (int i = 0; i < read; i++)
                {
                    char ch = buffer[i];
                    statement.Append(ch);
                    offset++;
                    if (statement.Length > MaxStatement) return null;
                    if (ch != '\n') continue;
                    if (++statementLines > MaxStatementLines) return null;
                    string source = statement.ToString();
                    if (Continues(source)) continue;
                    if (++count > MaxStatements || !ProcessStatement(source, statementStart, visible, ownership, nodes)) return null;
                    statement.Clear();
                    statementStart = offset;
                    statementLines = 0;
                }
            }
            if (statement.Length > 0 &&
                (++count > MaxStatements || Continues(statement.ToString()) ||
                 !ProcessStatement(statement.ToString(), statementStart, visible, ownership, nodes))) return null;
            if (!ownership.IsExhaustive) return null;
            ct.ThrowIfCancellationRequested();
            var lexical = AnalyzeVisible(snapshot, visible, ct);
            return new DocumentAnalysis(snapshot.Version, new TextSpan(0, snapshot.Length),
                AnalysisCompleteness.Complete,
                new SemanticNode("document", new TextSpan(0, snapshot.Length), children: nodes),
                Array.Empty<Diagnostic>(), lexical.Tokens, 0);
        }
        finally { ArrayPool<char>.Shared.Return(buffer); }
    }

    /// <summary>Validates one standalone statement, then updates global key ownership.</summary>
    private static bool ProcessStatement(string source, int start, TextSpan visible,
        TomlOwnershipIndex ownership, List<SemanticNode> nodes)
    {
        if (string.IsNullOrWhiteSpace(source) || source.TrimStart().StartsWith('#')) return true;
        var syntax = SyntaxParser.Parse(source, validate: true);
        if (syntax.Diagnostics.Count != 0) return false;
        var pairs = syntax.KeyValues.ToArray();
        var tables = syntax.Tables.ToArray();
        if (pairs.Length + tables.Length != 1) return false;
        if (pairs.Length == 1)
        {
            var pair = pairs[0];
            if (pair.Key is null || pair.Value is null || ownership.AddAssignment(pair.Key, pair.Value, start) is not null) return false;
            var span = Shift(pair.Span, start);
            if (Intersects(span, visible))
                nodes.Add(new SemanticNode("entry", span, pair.Key.ToString().Trim(),
                    children: [new SemanticNode(ValueKind(pair.Value), Shift(pair.Value.Span, start))]));
            return true;
        }
        var table = tables[0];
        // Array-table re-entry can change the meaning of later nested headers. Differential
        // probes found source-order cases where local ownership and Tomlyn disagree, so the
        // streaming path never certifies an array-table document.
        if (table.Name is null || table.Items.Any() ||
            table is TableArraySyntax ||
            ownership.AddHeader(table.Name, table is TableArraySyntax, start) is not null) return false;
        var tableSpan = Shift(table.Span, start);
        if (Intersects(tableSpan, visible))
            nodes.Add(new SemanticNode(table is TableArraySyntax ? "array-table" : "table",
                tableSpan, table.Name.ToString().Trim()));
        return true;
    }

    /// <summary>Finds continuation through arrays, inline tables, and triple-quoted strings.</summary>
    private static bool Continues(string source)
    {
        char quote = '\0';
        bool triple = false, comment = false;
        int depth = 0;
        for (int i = 0; i < source.Length; i++)
        {
            char ch = source[i];
            if (comment) { if (ch == '\n') comment = false; continue; }
            if (quote != '\0')
            {
                if (ch == '\\' && quote == '"') { i++; continue; }
                if (ch != quote) continue;
                if (!triple) { quote = '\0'; continue; }
                if (i + 2 < source.Length && source[i + 1] == quote && source[i + 2] == quote)
                { quote = '\0'; triple = false; i += 2; }
                continue;
            }
            if (ch == '#') { comment = true; continue; }
            if (ch is '"' or '\'')
            {
                quote = ch;
                triple = i + 2 < source.Length && source[i + 1] == ch && source[i + 2] == ch;
                if (triple) i += 2;
            }
            else if (ch is '[' or '{') depth++;
            else if (ch is ']' or '}') depth--;
        }
        return triple || depth > 0;
    }

    /// <summary>Shifts a statement-local Tomlyn span into snapshot coordinates.</summary>
    private static TextSpan Shift(SourceSpan span, int offset) => new(offset + span.Offset, span.Length);

    /// <summary>Projects only the value's semantic category on the large certified path.</summary>
    private static string ValueKind(ValueSyntax value) => value switch
    {
        StringValueSyntax => "string",
        IntegerValueSyntax or FloatValueSyntax => "number",
        BooleanValueSyntax => "boolean",
        DateTimeValueSyntax => "datetime",
        ArraySyntax => "array",
        InlineTableSyntax => "inline-table",
        _ => "invalid"
    };

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
