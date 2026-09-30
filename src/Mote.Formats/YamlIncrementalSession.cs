using System.Text;
using Mote.Engine;
using SharpYaml;
using SharpYaml.Events;
using SharpYaml.Syntax;

namespace Mote.Formats;

/// <summary>
/// Per-document YAML analysis over engine snapshots. Small documents use the exact legacy
/// oracle; large full requests stream SharpYaml events from bounded rope reads and retain only
/// key/anchor summaries plus a viewport projection. Visible-only requests avoid a global parse
/// and are explicitly provisional. Edits trigger a fresh stream parse, not local semantic reuse.
/// A semantic memory limit downgrades, never fabricates Complete.
/// </summary>
internal sealed class YamlIncrementalSession : IFormatSession
{
    // The legacy syntax tree is disproportionately expensive for tiny-node-dense inputs:
    // 120k "- x" items below 1 MiB allocated ~242 MiB in a measured Release run.
    private const int SmallDocument = 256 * 1024;
    private const int VisibleWindow = 64 * 1024;
    private const int MaxVisibleNodes = 2048;
    private const int MaxDiagnostics = 4096;
    private const int MaxAnchorNames = 16_384;
    private const int MaxKeyChars = 2 * 1024 * 1024;
    private const int MaxScalarChars = 64 * 1024;
    private long? _version;
    private bool _disposed;

    /// <summary>A valid but too-deep stream cannot be fully projected by the bounded checker.</summary>
    private sealed class ProjectionDepthException(int offset) : Exception
    {
        internal int Offset { get; } = offset;
    }

    /// <inheritdoc />
    public DocumentAnalysis Analyze(TextSnapshot snapshot, IReadOnlyList<VersionedEdit> changesSinceCommittedState,
        AnalysisRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(changesSinceCommittedState);
        Validate(snapshot, request);
        cancellationToken.ThrowIfCancellationRequested();

        DocumentAnalysis result;
        if (snapshot.Length <= SmallDocument)
            result = AnalyzeSmall(snapshot, request, cancellationToken);
        else if (request.Scope == AnalysisScope.Visible)
            result = AnalyzeVisible(snapshot, request.VisibleRange, cancellationToken);
        else if (RequiresBoundedFallback(snapshot, cancellationToken))
            result = AnalyzeBoundedFallback(snapshot, request.VisibleRange, cancellationToken);
        else
            result = AnalyzeStream(snapshot, request.VisibleRange, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        // This implementation rebuilds from the authoritative immutable snapshot. A history
        // gap therefore cannot reuse stale facts, and an obsolete snapshot cannot rewind state.
        if (_version is null || snapshot.Version >= _version) _version = snapshot.Version;
        return result;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        _version = null;
    }

    /// <summary>The exact small-document oracle, with bounded projection after parsing.</summary>
    private static DocumentAnalysis AnalyzeSmall(TextSnapshot snapshot, AnalysisRequest request, CancellationToken ct)
    {
        var analysis = new YamlPolicy().Analyze(snapshot.GetText(), ct);
        // The legacy parser stops at its first syntax exception, so later aliases and
        // duplicate keys were never checked; an error is not proof of a complete scan.
        var trustworthy = !analysis.Diagnostics.Any(d => d.Code is
            "yaml.key-equality-unsupported" or "yaml.depth-limit" or "yaml.syntax");
        var visible = request.VisibleRange;
        var children = analysis.Root.Children.Where(n => Intersects(n.Span, visible)).Take(MaxVisibleNodes).ToArray();
        var root = new SemanticNode("document", new TextSpan(0, snapshot.Length), children: children);
        var diagnostics = analysis.Diagnostics.Where(d => Intersects(d.Span, visible) || request.Scope == AnalysisScope.Full).Take(MaxDiagnostics).ToArray();
        var tokens = analysis.Tokens.Where(t => Intersects(t.Span, visible)).Take(MaxVisibleNodes).ToArray();
        var complete = trustworthy && analysis.Diagnostics.Count <= MaxDiagnostics;
        return new DocumentAnalysis(snapshot.Version, complete ? new TextSpan(0, snapshot.Length) : visible,
            complete ? AnalysisCompleteness.Complete : AnalysisCompleteness.Provisional,
            root, diagnostics, tokens, complete ? analysis.Diagnostics.Count : null);
    }

    /// <summary>Bounds an isolated viewport sample and never presents it as global semantics.</summary>
    private static DocumentAnalysis AnalyzeVisible(TextSnapshot snapshot, TextSpan visible, CancellationToken ct)
    {
        var start = Math.Max(0, visible.Start - 1024);
        var length = Math.Min(VisibleWindow, snapshot.Length - start);
        var tokens = new List<SemanticToken>();
        try
        {
            var syntax = YamlSyntaxTree.Parse(snapshot.GetText(start, length));
            foreach (var token in syntax.Tokens)
            {
                ct.ThrowIfCancellationRequested();
                if (tokens.Count >= MaxVisibleNodes) break;
                var begin = Math.Clamp(token.Span.Start.Index, 0, length);
                var end = Math.Clamp(token.Span.End.Index, begin, length);
                if (end > begin) tokens.Add(new SemanticToken(token.Kind.ToString(), new TextSpan(start + begin, end - begin)));
            }
        }
        catch (YamlException) { /* A context-free fragment can be invalid even when the document is valid. */ }
        return new DocumentAnalysis(snapshot.Version, new TextSpan(start, length), AnalysisCompleteness.Provisional,
            new SemanticNode("document", new TextSpan(0, snapshot.Length)), [], tokens, null);
    }

    /// <summary>
    /// SharpYaml events own decoded scalar strings. A single giant line or multiline scalar
    /// could therefore allocate most of a 100 MiB file even with a streaming TextReader. A
    /// cheap rope scan conservatively routes such inputs to bounded provisional projection.
    /// </summary>
    private static bool RequiresBoundedFallback(TextSnapshot snapshot, CancellationToken ct)
    {
        const int MaxScalarBody = 1024 * 1024;
        // Event parsing of many tiny nodes remains allocation-heavy even without a giant
        // scalar. These conservative source-shape caps route pathological density to a
        // bounded provisional viewport before event objects are created.
        const int MaxShortSemanticLines = 600_000;
        const int MaxStructuralMarks = 1_250_000;
        var shortSemanticLines = 0;
        var structuralMarks = 0;
        var anchorMarkers = 0;
        var lineLength = 0;
        var lineIndent = 0;
        var structural = false;
        var blockCandidate = false;
        var blockActive = false;
        var blockIndent = 0;
        var blockChars = 0;
        var plainActive = false;
        var plainIndent = 0;
        var plainChars = 0;
        var mappingEntry = false;
        var sequenceEntry = false;
        var pendingColon = false;
        var pendingDash = false;
        var valueSeen = false;
        var comment = false;
        var commentOnly = false;
        var seen = false;
        var singleQuote = false;
        var doubleQuote = false;
        var escaped = false;
        var lastSignificant = '\0';
        var lastChar = '\0';
        var prefixToken = false;
        var afterPrefix = false;
        var justClosedSingle = false;

        bool FinishLine()
        {
            var body = blockActive && (!seen || lineIndent > blockIndent);
            if (body) blockChars += lineLength;
            else blockActive = false;
            var plainBody = !body && plainActive && (!seen || lineIndent > plainIndent && !mappingEntry && !sequenceEntry);
            if (plainBody) plainChars += lineLength;
            else plainActive = false;
            if (!body && !plainBody && seen && !commentOnly && lineLength <= 32)
                shortSemanticLines++;
            var unsafeLine = blockChars > MaxScalarBody || plainChars > MaxScalarBody ||
                !body && !plainBody && (singleQuote || doubleQuote) ||
                shortSemanticLines > MaxShortSemanticLines || structuralMarks > MaxStructuralMarks;
            if (!body && blockCandidate)
            {
                blockActive = true;
                blockIndent = lineIndent;
                blockChars = 0;
            }
            if (!body && !plainBody && !blockCandidate && (mappingEntry || sequenceEntry) && valueSeen)
            {
                plainActive = true;
                plainIndent = lineIndent;
                plainChars = 0;
            }
            lineLength = lineIndent = 0;
            structural = blockCandidate = mappingEntry = sequenceEntry = pendingColon = pendingDash =
                valueSeen = comment = seen = singleQuote = doubleQuote = escaped = prefixToken = afterPrefix =
                    justClosedSingle = false;
            lastSignificant = lastChar = '\0';
            commentOnly = false;
            return unsafeLine;
        }

        foreach (var chunk in snapshot.GetChunks())
        {
            ct.ThrowIfCancellationRequested();
            foreach (var ch in chunk.Span)
            {
                if (ch == '\n')
                {
                    if (FinishLine()) return true;
                    continue;
                }
                if (++lineLength > 1024 * 1024) return true;
                var previousChar = lastChar;
                lastChar = ch;
                if (ch == '\r' || comment) continue;
                if (!seen && ch is ' ' or '\t') { lineIndent++; continue; }
                if (!seen && !char.IsWhiteSpace(ch))
                {
                    seen = true;
                    if (ch == '#') { comment = commentOnly = true; continue; }
                    if (ch == '-') pendingDash = true;
                }
                else if (pendingDash) { if (char.IsWhiteSpace(ch)) { sequenceEntry = true; structuralMarks++; } pendingDash = false; }
                if (pendingColon) { if (char.IsWhiteSpace(ch)) mappingEntry = true; pendingColon = false; }
                if (char.IsWhiteSpace(ch) && prefixToken) { prefixToken = false; afterPrefix = true; }
                if (doubleQuote)
                {
                    if (ch == '"' && !escaped) { doubleQuote = false; lastSignificant = '"'; }
                    escaped = ch == '\\' && !escaped;
                    continue;
                }
                if (singleQuote)
                {
                    justClosedSingle = ch == '\'';
                    if (justClosedSingle) { singleQuote = false; lastSignificant = '\''; }
                    continue;
                }
                if (ch is '"' or '\'')
                {
                    var startsQuoted = lastSignificant is '\0' or ':' or '[' or '{' or ',' or '?' ||
                        lastSignificant == '-' && sequenceEntry || afterPrefix ||
                        ch == '\'' && justClosedSingle && previousChar == '\'';
                    if (startsQuoted)
                    {
                        if (ch == '"') doubleQuote = true;
                        else singleQuote = true;
                        lastSignificant = ch;
                        afterPrefix = false;
                        justClosedSingle = false;
                        if (mappingEntry || sequenceEntry) valueSeen = true;
                        continue;
                    }
                }
                justClosedSingle = false;
                if (ch == '#') { comment = true; continue; }
                if (afterPrefix && !char.IsWhiteSpace(ch) && ch is not ('!' or '&')) afterPrefix = false;
                if (ch is '!' or '&' && (lastSignificant is '\0' or ':' or '[' or '{' or ',' or '?' || afterPrefix ||
                    lastSignificant == '-' && sequenceEntry)) prefixToken = true;
                if (ch == '&' && ++anchorMarkers > MaxAnchorNames) return true;
                if (ch is ':' or ',' or '[' or ']' or '{' or '}') structuralMarks++;
                if (structuralMarks > MaxStructuralMarks) return true;
                if (ch is ':' or '-') structural = true;
                if (ch == ':') pendingColon = true;
                if ((mappingEntry || sequenceEntry) && !char.IsWhiteSpace(ch) && ch is not (':' or '-')) valueSeen = true;
                if (structural && ch is '|' or '>') blockCandidate = true;
                else if (blockCandidate && !char.IsWhiteSpace(ch) && !char.IsAsciiDigit(ch) && ch is not ('+' or '-')) blockCandidate = false;
                if (!char.IsWhiteSpace(ch)) lastSignificant = ch;
            }
        }
        return lineLength > 1024 * 1024 || lineLength > 0 && FinishLine();
    }

    private static DocumentAnalysis AnalyzeBoundedFallback(TextSnapshot snapshot, TextSpan visible, CancellationToken ct)
    {
        var partial = AnalyzeVisible(snapshot, visible, ct);
        var warning = new Diagnostic(DiagnosticSeverity.Warning, "yaml.streaming-limit",
            "Full YAML analysis exceeded the safe scalar or structural-density budget; showing a provisional viewport instead.",
            new TextSpan(visible.Start, 0));
        return new DocumentAnalysis(snapshot.Version, partial.Coverage, AnalysisCompleteness.Provisional,
            partial.Root, new[] { warning }, partial.Tokens, null);
    }

    /// <summary>Streams one full snapshot without materializing its source or a whole AST.</summary>
    private static DocumentAnalysis AnalyzeStream(TextSnapshot snapshot, TextSpan visible, CancellationToken ct)
    {
        var diagnostics = new List<Diagnostic>();
        var tokens = new List<SemanticToken>();
        var nodes = new List<SemanticNode>();
        var projector = new StreamProjector(snapshot.Length, visible, diagnostics, tokens, nodes, ct);
        try
        {
            using var reader = new SnapshotTextReader(snapshot, 0, snapshot.Length, ct);
            var parser = Parser.CreateParser(reader);
            projector.Parse(parser);
        }
        catch (YamlException error)
        {
            projector.Downgrade();
            var start = Math.Clamp(error.Start.Index, 0, snapshot.Length);
            var end = Math.Clamp(error.End.Index, start, snapshot.Length);
            if (diagnostics.Count < MaxDiagnostics)
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "yaml.syntax", error.Message, new TextSpan(start, end - start)));
        }
        catch (FormatException error)
        {
            projector.Downgrade();
            if (diagnostics.Count < MaxDiagnostics)
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "yaml.syntax", error.Message, new TextSpan(snapshot.Length, 0)));
        }
        catch (ProjectionDepthException error)
        {
            projector.Downgrade();
            if (diagnostics.Count < MaxDiagnostics)
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "yaml.depth-limit",
                    "YAML nesting exceeds safe streaming depth.", new TextSpan(Math.Clamp(error.Offset, 0, snapshot.Length), 0)));
        }
        var complete = projector.Complete;
        return new DocumentAnalysis(snapshot.Version, complete ? new TextSpan(0, snapshot.Length) : visible,
            complete ? AnalysisCompleteness.Complete : AnalysisCompleteness.Provisional,
            new SemanticNode("document", new TextSpan(0, snapshot.Length), children: nodes), diagnostics, tokens,
            complete ? projector.TotalDiagnostics : null);
    }

    /// <summary>Includes zero-length diagnostics at a viewport boundary.</summary>
    private static bool Intersects(TextSpan span, TextSpan range) =>
        span.Start < range.End && span.End > range.Start || span.Length == 0 && span.Start >= range.Start && span.Start <= range.End;

    /// <summary>Rejects invalid viewport coordinates before any source read.</summary>
    private static void Validate(TextSnapshot snapshot, AnalysisRequest request)
    {
        var range = request.VisibleRange;
        if (range.Start < 0 || range.Length < 0 || range.Start > snapshot.Length || range.Length > snapshot.Length - range.Start)
            throw new ArgumentOutOfRangeException(nameof(request), "Visible range exceeds the snapshot.");
        if (request.Scope is not (AnalysisScope.Visible or AnalysisScope.Full))
            throw new ArgumentOutOfRangeException(nameof(request), "Unknown analysis scope.");
    }

    /// <summary>
    /// Forward-only semantic checker. Canonical strings are built only for mapping keys and
    /// anchored nodes; ordinary values are discarded as soon as their events are consumed.
    /// </summary>
    private sealed class StreamProjector
    {
        private readonly int _length;
        private readonly TextSpan _visible;
        private readonly List<Diagnostic> _diagnostics;
        private readonly List<SemanticToken> _tokens;
        private readonly List<SemanticNode> _nodes;
        private readonly CancellationToken _ct;
        private readonly Dictionary<string, Anchor> _anchors = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _shortScalarKeys = new(StringComparer.Ordinal);
        private int _anchorNameChars;
        private bool _anchorIndexIncomplete;
        private int _keyChars;
        private int _anchorChars;
        private bool _complete = true;
        private bool _budgetExceeded;
        private IParser _parser = null!;
        private Mark _lastNodeEnd;

        private sealed class Anchor
        {
            public string? Canonical;
            public bool Ready;
        }

        internal StreamProjector(int length, TextSpan visible, List<Diagnostic> diagnostics,
            List<SemanticToken> tokens, List<SemanticNode> nodes, CancellationToken ct)
        {
            _length = length;
            _visible = visible;
            _diagnostics = diagnostics;
            _tokens = tokens;
            _nodes = nodes;
            _ct = ct;
        }

        internal bool Complete => _complete;
        internal int TotalDiagnostics => _diagnostics.Count;
        internal void Downgrade() => _complete = false;

        /// <summary>Consumes all documents while discarding ordinary value bodies immediately.</summary>
        internal void Parse(IParser parser)
        {
            _parser = parser;
            while (_parser.MoveNext())
            {
                _ct.ThrowIfCancellationRequested();
                if (_parser.Current is not DocumentStart) continue;
                _anchors.Clear();
                _anchorChars = 0;
                _anchorNameChars = 0;
                _anchorIndexIncomplete = !_complete;
                if (!_parser.MoveNext()) throw new FormatException("Unexpected end of YAML document.");
                if (_parser.Current is not DocumentEnd) ParseNode(false, 0);
                if (_parser.Current is not DocumentEnd) throw new FormatException("Expected end of YAML document.");
            }
        }

        /// <summary>Returns a canonical key shape only when a key or anchor needs one.</summary>
        private string? ParseNode(bool needsCanonical, int depth)
        {
            _ct.ThrowIfCancellationRequested();
            if (depth > 256) throw new ProjectionDepthException(_parser.Current?.Start.Index ?? _length);
            var current = _parser.Current;
            if (!_complete) DropAnchorIndex();
            if (current is AnchorAlias alias)
            {
                AddVisible("alias", alias.Start, alias.End, alias.Value);
                _lastNodeEnd = alias.End;
                _parser.MoveNext();
                // Once the index is incomplete, absence no longer proves an undefined alias.
                if (_anchorIndexIncomplete) return null;
                if (!_anchors.TryGetValue(alias.Value, out var target))
                {
                    AddDiagnostic(DiagnosticSeverity.Error, "yaml.undefined-alias", $"Alias '*{alias.Value}' has no preceding anchor.", alias.Start, alias.End);
                    // A missing alias used inside a mapping key also makes equality with
                    // later keys undecidable. The syntax error alone cannot certify that
                    // every duplicate was found, even though the stream can continue.
                    if (needsCanonical) Unsupported(alias.Start, alias.End, "unbound alias in canonical key or anchor");
                    return null;
                }
                if (!needsCanonical) return null;
                if (!target.Ready || target.Canonical is null) { Unsupported(alias.Start, alias.End, "cyclic or unavailable alias target"); return null; }
                return target.Canonical;
            }
            if (current is not NodeEvent node) throw new FormatException("Unexpected YAML event while reading a node.");
            Anchor? anchor = null;
            if (!string.IsNullOrEmpty(node.Anchor) && !_anchorIndexIncomplete)
            {
                var newName = !_anchors.ContainsKey(node.Anchor);
                if ((newName && _anchors.Count >= MaxAnchorNames) ||
                    (newName && _anchorNameChars + node.Anchor.Length > MaxKeyChars))
                {
                    Unsupported(node.Start, node.End, "anchor name/count budget exceeded");
                    DropAnchorIndex();
                }
                else
                {
                    anchor = new Anchor();
                    _anchors[node.Anchor] = anchor;
                    if (newName) _anchorNameChars += node.Anchor.Length;
                    needsCanonical = true;
                }
            }
            string? canonical;
            if (node is Scalar scalar) canonical = ParseScalar(scalar, needsCanonical);
            else if (node is SequenceStart sequence) canonical = ParseSequence(sequence, needsCanonical, depth);
            else if (node is MappingStart mapping) canonical = ParseMapping(mapping, needsCanonical, depth);
            else throw new FormatException("Unknown YAML node event.");
            if (anchor is not null)
            {
                if (canonical is not null && _anchorChars + canonical.Length > MaxKeyChars)
                {
                    Unsupported(node.Start, node.End, "anchor summary budget exceeded");
                    canonical = null;
                }
                anchor.Canonical = canonical;
                anchor.Ready = true;
                _anchorChars += canonical?.Length ?? 0;
            }
            if (!_complete) DropAnchorIndex();
            return canonical;
        }

        /// <summary>Stops retaining anchors once absence can no longer be certified.</summary>
        private void DropAnchorIndex()
        {
            if (_anchorIndexIncomplete) return;
            _anchorIndexIncomplete = true;
            _anchors.Clear();
            _anchorNameChars = 0;
            _anchorChars = 0;
        }

        /// <summary>Validates explicit core tags and skips irrelevant ordinary values.</summary>
        private string? ParseScalar(Scalar scalar, bool needed)
        {
            var span = Range(scalar.Start, scalar.End);
            if (!needed && !Intersects(span, _visible) && string.IsNullOrEmpty(scalar.Tag))
            {
                // Values that are neither keys nor anchors have no effect on key equality.
                // The parser already validated their syntax; avoid allocating a typed identity.
                _lastNodeEnd = scalar.End;
                _parser.MoveNext();
                return null;
            }
            var oversized = scalar.Value.Length > MaxScalarChars;
            var identity = !oversized
                ? YamlPolicy.ResolveScalarIdentity(scalar) : (Tag: "", Value: "", Supported: false);
            var kind = identity.Tag.EndsWith(":bool", StringComparison.Ordinal) ? "boolean" :
                identity.Tag.EndsWith(":null", StringComparison.Ordinal) ? "null" :
                identity.Tag.EndsWith(":int", StringComparison.Ordinal) || identity.Tag.EndsWith(":float", StringComparison.Ordinal) ? "number" : "scalar";
            AddVisible(kind, scalar.Start, scalar.End, scalar.Value.Length <= 256 ? scalar.Value : null);
            if (!oversized && !identity.Supported && IsCoreScalarTag(NormalizeTag(scalar.Tag, "str")))
                AddDiagnostic(DiagnosticSeverity.Error, "yaml.invalid-tagged-scalar", "Invalid explicit YAML core scalar.", scalar.Start, scalar.End);
            if (oversized && !needed && !string.IsNullOrEmpty(scalar.Tag) && IsCoreScalarTag(NormalizeTag(scalar.Tag, "str")))
                _complete = false;
            _lastNodeEnd = scalar.End;
            _parser.MoveNext();
            if (!needed) return null;
            if (!identity.Supported) { Unsupported(scalar.Start, scalar.End, "scalar canonicalization unavailable"); return null; }
            // Repeated field names dominate config files. A tiny per-analysis cache avoids
            // rebuilding the same canonical string for every record; no global interning.
            if (identity.Tag == "tag:yaml.org,2002:str" && scalar.Style == ScalarStyle.Plain &&
                string.IsNullOrEmpty(scalar.Tag) && scalar.Value.Length <= 32)
            {
                if (_shortScalarKeys.TryGetValue(scalar.Value, out var cached)) return Retain(cached, span);
                var created = Retain(Pack("S", identity.Tag, identity.Value), span);
                if (created is not null && _shortScalarKeys.Count < 64) _shortScalarKeys.Add(scalar.Value, created);
                return created;
            }
            return Retain(Pack("S", identity.Tag, identity.Value), span);
        }

        /// <summary>Retains ordered children only for key/anchor identity.</summary>
        private string? ParseSequence(SequenceStart start, bool needed, int depth)
        {
            ValidateCollectionTag(start, "seq");
            AddVisible("sequence", start.Start, start.End, null);
            _parser.MoveNext();
            var children = needed ? new List<string>() : null;
            var shapeChars = 0;
            while (_parser.Current is not SequenceEnd)
            {
                if (_parser.Current is null) throw new FormatException("Unexpected end of YAML sequence.");
                var value = ParseNode(needed, depth + 1);
                if (needed && (value is null || shapeChars + value.Length > MaxScalarChars))
                {
                    if (value is not null) Unsupported(start.Start, start.End, "collection key is too large");
                    needed = false;
                    children?.Clear();
                }
                if (needed) { children!.Add(value!); shapeChars += value!.Length; }
            }
            var end = _parser.Current.End;
            _lastNodeEnd = end;
            _parser.MoveNext();
            if (!needed || children is null) return null;
            children.Insert(0, NormalizeTag(start.Tag, "seq"));
            children.Insert(0, "Q");
            return Retain(Pack(children.ToArray()), Range(start.Start, end));
        }

        /// <summary>Checks local key uniqueness and optionally retains unordered pairs.</summary>
        private string? ParseMapping(MappingStart start, bool needed, int depth)
        {
            ValidateCollectionTag(start, "map");
            AddVisible("mapping", start.Start, start.End, null);
            _parser.MoveNext();
            // Most YAML records have only a few fields. Keep their keys in locals rather
            // than allocating a HashSet and backing array for every tiny mapping.
            string? key0 = null, key1 = null, key2 = null, key3 = null;
            var keyCount = 0;
            HashSet<string>? keys = null;
            var pairs = needed ? new List<string>() : null;
            var shapeChars = 0;
            var localKeyChars = 0;
            while (_parser.Current is not MappingEnd)
            {
                if (_parser.Current is null) throw new FormatException("Unexpected end of YAML mapping.");
                var keyStart = _parser.Current.Start;
                // Once global uniqueness is explicitly provisional, do not keep allocating
                // canonical keys that cannot contribute to a truthful Complete result.
                var key = ParseNode(!_budgetExceeded, depth + 1);
                var keyEnd = _lastNodeEnd;
                if (_parser.Current is null or MappingEnd) throw new FormatException("Mapping key has no value.");
                var value = ParseNode(needed, depth + 1);
                if (!_complete) _budgetExceeded = true;
                if (_budgetExceeded && keyCount != 0)
                {
                    keys?.Clear();
                    keys = null;
                    key0 = key1 = key2 = key3 = null;
                    keyCount = 0;
                    _keyChars -= localKeyChars;
                    localKeyChars = 0;
                }
                if (!_budgetExceeded && key is not null)
                {
                    var added = keys is not null ? keys.Add(key) :
                        key != key0 && key != key1 && key != key2 && key != key3;
                    if (!added)
                        AddDiagnostic(DiagnosticSeverity.Error, "yaml.duplicate-key", "Duplicate YAML mapping key after canonicalization.", keyStart, keyEnd);
                    else
                    {
                        if (keys is null)
                        {
                            switch (keyCount)
                            {
                                case 0: key0 = key; break;
                                case 1: key1 = key; break;
                                case 2: key2 = key; break;
                                case 3: key3 = key; break;
                                default:
                                    keys = new HashSet<string>(StringComparer.Ordinal) { key0!, key1!, key2!, key3!, key };
                                    key0 = key1 = key2 = key3 = null;
                                    break;
                            }
                        }
                        keyCount++;
                        if (_complete) { _keyChars += key.Length; localKeyChars += key.Length; }
                    }
                }
                if (_keyChars > MaxKeyChars)
                {
                    _complete = false;
                    _budgetExceeded = true;
                    keys?.Clear();
                    keys = null;
                    key0 = key1 = key2 = key3 = null;
                    keyCount = 0;
                    _keyChars -= localKeyChars;
                    localKeyChars = 0;
                }
                if (needed && (key is null || value is null || shapeChars + key.Length + value.Length > MaxScalarChars))
                {
                    if (key is not null && value is not null) Unsupported(start.Start, start.End, "collection key is too large");
                    needed = false;
                    pairs?.Clear();
                }
                if (needed)
                {
                    var pair = Pack(key!, value!);
                    pairs!.Add(pair);
                    shapeChars += pair.Length;
                }
            }
            var end = _parser.Current.End;
            _lastNodeEnd = end;
            _parser.MoveNext();
            _keyChars -= localKeyChars;
            if (!needed || pairs is null) return null;
            pairs.Sort(StringComparer.Ordinal);
            pairs.Insert(0, NormalizeTag(start.Tag, "map"));
            pairs.Insert(0, "M");
            return Retain(Pack(pairs.ToArray()), Range(start.Start, end));
        }

        /// <summary>Core collection tags must match the actual node kind.</summary>
        private void ValidateCollectionTag(NodeEvent node, string expected)
        {
            if (string.IsNullOrEmpty(node.Tag)) return;
            var tag = NormalizeTag(node.Tag, expected);
            if ((IsCoreScalarTag(tag) || tag is "tag:yaml.org,2002:map" or "tag:yaml.org,2002:seq") &&
                tag != "tag:yaml.org,2002:" + expected)
                AddDiagnostic(DiagnosticSeverity.Error, "yaml.invalid-tagged-collection", "YAML core tag conflicts with collection kind.", node.Start, node.End);
        }

        /// <summary>Enforces the per-key and total retained-summary budgets.</summary>
        private string? Retain(string canonical, TextSpan span)
        {
            if (canonical.Length > MaxScalarChars || _keyChars + canonical.Length > MaxKeyChars)
            {
                _complete = false;
                if (_keyChars + canonical.Length > MaxKeyChars) _budgetExceeded = true;
                if (_diagnostics.Count < MaxDiagnostics)
                    _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "yaml.key-equality-unsupported",
                        "Semantic key memory budget exceeded.", span));
                return null;
            }
            return canonical;
        }

        /// <summary>Downgrades global certainty without pretending an unsupported key is valid.</summary>
        private void Unsupported(Mark start, Mark end, string reason)
        {
            _complete = false;
            AddDiagnostic(DiagnosticSeverity.Warning, "yaml.key-equality-unsupported", reason, start, end);
        }

        /// <summary>Publishes only viewport-overlapping, absolute source spans.</summary>
        private void AddVisible(string kind, Mark start, Mark end, string? value)
        {
            var span = Range(start, end);
            if (!Intersects(span, _visible) || _nodes.Count >= MaxVisibleNodes) return;
            _nodes.Add(new SemanticNode(kind, span, value: value));
            _tokens.Add(new SemanticToken(kind, span));
        }

        /// <summary>Caps retained diagnostics and downgrades when truncation occurs.</summary>
        private void AddDiagnostic(DiagnosticSeverity severity, string code, string message, Mark start, Mark end)
        {
            if (_diagnostics.Count >= MaxDiagnostics) { _complete = false; return; }
            _diagnostics.Add(new Diagnostic(severity, code, message, Range(start, end)));
        }

        /// <summary>Converts parser UTF-16 marks into bounded half-open spans.</summary>
        private TextSpan Range(Mark start, Mark end)
        {
            var offset = Math.Clamp(start.Index, 0, _length);
            return new TextSpan(offset, Math.Clamp(end.Index, offset, _length) - offset);
        }

        /// <summary>Expands standard YAML tag handles for structural comparison.</summary>
        private static string NormalizeTag(string? tag, string fallback)
        {
            if (string.IsNullOrEmpty(tag)) tag = "!!" + fallback;
            return tag.StartsWith("!!", StringComparison.Ordinal) ? "tag:yaml.org,2002:" + tag[2..] : tag;
        }

        /// <summary>Recognizes only tags whose canonicalizer is implemented locally.</summary>
        private static bool IsCoreScalarTag(string tag) => tag is
            "tag:yaml.org,2002:str" or "tag:yaml.org,2002:int" or "tag:yaml.org,2002:float" or
            "tag:yaml.org,2002:bool" or "tag:yaml.org,2002:null";

        /// <summary>Length prefixes make nested string encodings unambiguous.</summary>
        private static string Pack(params string[] values)
        {
            var output = new StringBuilder();
            foreach (var value in values) output.Append(value.Length).Append(':').Append(value);
            return output.ToString();
        }
    }
}
