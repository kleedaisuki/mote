using System.Text.Json;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using Mote.Engine;

namespace Mote.Formats;

/// <summary>
/// Source-backed JSON session with independently certified root-array owners.
/// Hard parser seams, not delimiter guesses, establish reuse after local edits.
/// </summary>
/// <remarks>
/// Full scans harvest source-sized root-array owners while output is viewport-bounded.
/// Local edits repair strict owners without trusting unchanged suffix hashes. Depth limits and error recovery that skip
/// unknown structure return Provisional rather than asserting an exact global count.
/// A small Full request preserves the legacy tree. No whole-snapshot string is materialized.
/// </remarks>
internal sealed class JsonIncrementalSession : IFormatSession
{
    private const int FullTreeLimit = 1024 * 1024;
    private const int MaxProjection = 256 * 1024;
    private bool _disposed;
    private bool _analyzing;
    private readonly ulong _hashSeed = NewHashSeed();
    private JsonArrayCertificate? _array;
    private WeakReference<TextSnapshot>? _currentSnapshot;

    /// <summary>Actual interactive parser/key-comparison visits; null for unbounded authoritative Full.</summary>
    internal long? LastVisitedUnits { get; private set; }
    /// <summary>Bounded metadata instrumentation; neither property retains source.</summary>
    internal int ArrayPageCount => _array?.Pages.Length ?? 0;
    /// <summary>Owners still requiring strict local repair.</summary>
    internal int ArrayDirtyPageCount => _array?.Pages.Count(page => page.Dirty) ?? 0;
    /// <summary>Version of the committed candidate, including provisional dirty candidates.</summary>
    internal long? ArrayCertificateVersion => _array?.Version;
    /// <summary>Test-only interval snapshot; normal analysis never calls this allocating accessor.</summary>
    internal IReadOnlyList<TextSpan> ArrayPageSpans => _array?.Pages.Select(page => page.Span).ToArray() ?? [];
    /// <summary>Deterministic internal validation seam; never configured by production callers.</summary>
    internal Action<string>? AnalysisHook { get; set; }

    /// <summary>Seeds object-key hashing independently for each document session.</summary>
    private static ulong NewHashSeed()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToUInt64(bytes);
    }

    /// <inheritdoc />
    public DocumentAnalysis Analyze(TextSnapshot snapshot, IReadOnlyList<VersionedEdit> changesSinceCommittedState,
        AnalysisRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(changesSinceCommittedState);
        var range = request.VisibleRange;
        if (range.Start < 0 || range.Length < 0 || range.Start > snapshot.Length ||
            range.Length > snapshot.Length - range.Start)
            throw new ArgumentOutOfRangeException(nameof(request));
        if (request.Scope is not (AnalysisScope.Visible or AnalysisScope.Full))
            throw new ArgumentOutOfRangeException(nameof(request));

        if (_analyzing) throw new InvalidOperationException("JSON session calls must be serialized and non-reentrant.");
        _analyzing = true;
        try { return AnalyzeCore(snapshot, changesSinceCommittedState, request, cancellationToken); }
        finally { _analyzing = false; }
    }

    /// <summary>Stages all mapping, validation and projection before one cancellation-gated publication.</summary>
    private DocumentAnalysis AnalyzeCore(TextSnapshot snapshot, IReadOnlyList<VersionedEdit> edits,
        AnalysisRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var visible = new TextSpan(request.VisibleRange.Start, Math.Min(request.VisibleRange.Length, MaxProjection));
        var work = new SourceWork(request.Scope == AnalysisScope.Visible && snapshot.Length > FullTreeLimit
            ? 512 * 1024 : long.MaxValue, ct);
        JsonArrayCertificate? candidate = null;
        if (snapshot.Length > FullTreeLimit && _array is not null)
        {
            bool sameSource = _currentSnapshot?.TryGetTarget(out var previous) == true && ReferenceEquals(previous, snapshot);
            candidate = _array.Map(snapshot, edits, sameSource, ct);
            if (candidate is not null)
            {
                var projection = ProjectArray(snapshot, candidate, visible, work, ct, out candidate);
                if (projection.Completeness == AnalysisCompleteness.Complete || request.Scope == AnalysisScope.Visible)
                    return Publish(snapshot, candidate, projection, work, ct);
            }
        }
        // Small files and explicit Full keep the established authoritative recovery domain.
        // Cold large Visible cannot synchronously validate an arbitrarily large suffix.
        bool bounded = request.Scope == AnalysisScope.Visible && snapshot.Length > FullTreeLimit;
        work = new SourceWork(bounded ? MaxProjection : long.MaxValue, ct);
        var parser = new Parser(snapshot, visible, request.Scope == AnalysisScope.Full && snapshot.Length <= FullTreeLimit,
            _hashSeed, ct, work);
        var result = parser.Parse();
        if (bounded && work.Exhausted)
            result = Provisional(snapshot, visible, result.Root, result.Diagnostics, result.Tokens);
        candidate = parser.Certificate;
        return Publish(snapshot, candidate, result, work, ct);
    }

    /// <summary>Revalidates only dirty owners and projects independently certified intersecting owners.</summary>
    private DocumentAnalysis ProjectArray(TextSnapshot snapshot, JsonArrayCertificate mapped, TextSpan visible,
        SourceWork work, CancellationToken ct, out JsonArrayCertificate candidate)
    {
        var pages = (JsonArrayPage[])mapped.Pages.Clone();
        var parsed = new Dictionary<int, Parser.PageResult>();
        bool dirty = false;
        for (int i = 0; i < pages.Length; i++)
        {
            if (!pages[i].Dirty) continue;
            AnalysisHook?.Invoke("page");
            ct.ThrowIfCancellationRequested();
            var parser = new Parser(snapshot, visible, false, _hashSeed, ct, work, pages[i].Span);
            var local = parser.ParsePage(i == pages.Length - 1);
            parsed[i] = local;
            if (local.Valid)
                pages[i] = pages[i] with { DiagnosticCount = local.DiagnosticCount, ElementCount = local.ElementCount, Dirty = false };
            else dirty = true;
            if (work.Exhausted) break;
        }
        dirty |= pages.Any(page => page.Dirty);
        candidate = mapped with { Pages = pages };
        var children = new List<SemanticNode>();
        var diagnostics = new List<Diagnostic>();
        var tokens = new List<SemanticToken>();
        for (int i = 0; i < pages.Length; i++)
        {
            var page = pages[i];
            if (!Intersects(page.Span, visible)) continue;
            if (!parsed.TryGetValue(i, out var local))
            {
                if (work.Exhausted) break;
                AnalysisHook?.Invoke("page");
                ct.ThrowIfCancellationRequested();
                var parser = new Parser(snapshot, visible, false, _hashSeed, ct, work, page.Span);
                local = parser.ParsePage(i == pages.Length - 1);
                // Unchanged pages were proved at their exact mapped source. Refusal to
                // project a giant owner does not invalidate the global certificate.
            }
            children.AddRange(local.Children);
            diagnostics.AddRange(local.Diagnostics);
            tokens.AddRange(local.Tokens);
        }
        var arraySpan = new TextSpan(candidate.ArrayStart, candidate.ArrayEnd - candidate.ArrayStart);
        var array = new SemanticNode("array", arraySpan, children: children);
        var root = new SemanticNode("document", new TextSpan(0, snapshot.Length),
            children: Intersects(arraySpan, visible) ? [array] : []);
        if (dirty) return Provisional(snapshot, visible, root, diagnostics, tokens);
        return new DocumentAnalysis(snapshot.Version, new TextSpan(0, snapshot.Length), AnalysisCompleteness.Complete,
            root, diagnostics, tokens, pages.Sum(page => page.DiagnosticCount));
    }

    /// <summary>Uses the existing half-open projection convention, including zero-length cursor windows.</summary>
    private static bool Intersects(TextSpan owner, TextSpan visible) => owner.Length == 0
        ? owner.Start >= visible.Start && owner.Start <= visible.End
        : owner.Start < visible.End && owner.End > visible.Start;

    /// <summary>Partial output never carries a stale exact global diagnostic count.</summary>
    private static DocumentAnalysis Provisional(TextSnapshot snapshot, TextSpan visible, SemanticNode root,
        IReadOnlyList<Diagnostic> diagnostics, IReadOnlyList<SemanticToken> tokens) =>
        new(snapshot.Version, visible, AnalysisCompleteness.Provisional, root, diagnostics, tokens, null);

    /// <summary>A canceled stage cannot mutate committed owner spans, version, count or instrumentation.</summary>
    private DocumentAnalysis Publish(TextSnapshot snapshot, JsonArrayCertificate? candidate,
        DocumentAnalysis result, SourceWork work, CancellationToken ct)
    {
        AnalysisHook?.Invoke("before-commit");
        ct.ThrowIfCancellationRequested();
        _array = candidate;
        _currentSnapshot = new WeakReference<TextSnapshot>(snapshot);
        LastVisitedUnits = work.Limited ? work.Visited : null;
        return result;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        _array = null;
        _currentSnapshot = null;
        AnalysisHook = null;
    }

    /// <summary>Counts every grammar or exact-key source visit, with a hard interactive ceiling.</summary>
    private sealed class SourceWork(long limit, CancellationToken ct)
    {
        internal bool Limited => limit != long.MaxValue;
        internal long Visited { get; private set; }
        internal bool Exhausted { get; private set; }

        /// <summary>The common read path stays inline; grammar loops own periodic cancellation.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Visit(int count = 1)
        {
            if (count > limit - Visited) Refuse();
            Visited += count;
        }

        /// <summary>Cold refusal is kept out of the per-character source cursor.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Refuse()
        {
            ct.ThrowIfCancellationRequested();
            Exhausted = true;
            throw new SourceBudgetException();
        }
    }

    /// <summary>Internal control flow only; cancellation is never translated into syntax failure.</summary>
    private sealed class SourceBudgetException : Exception;

    /// <summary>Range-backed recursive descent, deliberately matching legacy recovery and diagnostic ordering.</summary>
    private sealed class Parser
    {
        private readonly TextSnapshot _snapshot;
        private readonly TextSpan _visible;
        private readonly bool _captureAll;
        private readonly ulong _hashSeed;
        private readonly CancellationToken _ct;
        private readonly IEnumerator<ReadOnlyMemory<char>> _chunks;
        private readonly SourceWork? _work;
        private readonly int _end;
        private bool _syntaxError;
        private readonly List<JsonArrayPage> _pages = [];
        private bool _indexAbandoned;
        private int _arrayStart = -1;
        private int _arrayEnd;
        internal JsonArrayCertificate? Certificate { get; private set; }
        private readonly List<Diagnostic> _diagnostics = [];
        private readonly List<SemanticToken> _tokens = [];
        private ReadOnlyMemory<char> _chunk;
        private int _chunkStart;
        private int _position;
        private int _depth;
        private int _diagnosticCount;
        private bool _uncertain;
        private ulong _lastKeyHash;
        private bool _lastKeyValid;
        private bool _lastKeyEscaped;

        internal Parser(TextSnapshot snapshot, TextSpan visible, bool captureAll, ulong hashSeed, CancellationToken ct, SourceWork work, TextSpan? sourceRange = null)
        {
            _snapshot = snapshot;
            _visible = visible;
            _captureAll = captureAll;
            _hashSeed = hashSeed;
            _ct = ct;
            _work = work.Limited ? work : null;
            _position = sourceRange?.Start ?? 0;
            _chunkStart = _position;
            _end = sourceRange?.End ?? snapshot.Length;
            _chunks = snapshot.GetChunks(_position, _end - _position).GetEnumerator();
        }

        private int Length => _end;

        /// <summary>Reads immutable rope chunks directly; grammar access is forward-only.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private char At(int position)
        {
            _work?.Visit();
            while (position >= _chunkStart + _chunk.Length)
            {
                _chunkStart += _chunk.Length;
                if (!_chunks.MoveNext()) throw new InvalidOperationException("JSON cursor passed snapshot end.");
                _chunk = _chunks.Current;
            }
            if (position < _chunkStart) throw new InvalidOperationException("JSON parser moved backward across chunks.");
            return _chunk.Span[position - _chunkStart];
        }

        private bool Hits(int start, int length) => _captureAll ||
            (length == 0 ? start >= _visible.Start && start <= _visible.End :
                start < _visible.End && start + length > _visible.Start);

        /// <summary>Projection decoding also visits source and shares the same interactive ceiling.</summary>
        private string Slice(int start, int length)
        {
            _work?.Visit(length);
            return _snapshot.GetText(start, length);
        }

        internal DocumentAnalysis Parse()
        {
            try
            {
                Space();
                var value = Value();
                Space();
                if (_position < Length)
                {
                    Error("JSON_TRAILING", "Unexpected content after the JSON value.", _position, Length - _position);
                    // The legacy parser diagnoses the suffix as one unit rather than
                    // inspecting its nested structure or duplicate bindings.
                    _uncertain = true;
                }
                if (!_syntaxError && !_uncertain && !_indexAbandoned && _arrayStart >= 0)
                    Certificate = new JsonArrayCertificate(_snapshot.Version, _snapshot.Length, _arrayStart, _arrayEnd, _pages.ToArray());
                var root = new SemanticNode("document", new TextSpan(0, Length), children: value is null ? [] : [value]);
                return new DocumentAnalysis(_snapshot.Version,
                    _uncertain ? _visible : new TextSpan(0, Length),
                    _uncertain ? AnalysisCompleteness.Provisional : AnalysisCompleteness.Complete,
                    root, _diagnostics, _tokens, _uncertain ? null : _diagnosticCount);
            }
            catch (SourceBudgetException)
            {
                return Provisional(_snapshot, _visible,
                    new SemanticNode("document", new TextSpan(0, _snapshot.Length)), _diagnostics, _tokens);
            }
            finally { _chunks.Dispose(); }
        }

        /// <summary>Parses the exact array-owner grammar with the same value/depth/key routines.</summary>
        internal PageResult ParsePage(bool final)
        {
            var children = new List<SemanticNode>();
            int elements = 0;
            _depth = 1; // The omitted root array still contributes one level.
            bool boundary = false;
            try
            {
                while (_position < Length)
                {
                    int before = _position;
                    var value = Value();
                    if (value is not null) children.Add(value);
                    elements++;
                    Space();
                    if (_position == Length) { boundary = final; break; }
                    if (At(_position) != ',' || _position == before) break;
                    _position++;
                    Space();
                    if (_position == Length) { boundary = !final; break; }
                }
                return new PageResult(boundary && !_syntaxError && !_uncertain && elements > 0,
                    elements, _diagnosticCount, children, _diagnostics, _tokens);
            }
            catch (SourceBudgetException)
            {
                return new PageResult(false, elements, _diagnosticCount, children, _diagnostics, _tokens);
            }
            finally { _chunks.Dispose(); }
        }

        /// <summary>Call-local output only; retained page metadata never stores this tree or key data.</summary>
        internal sealed record PageResult(bool Valid, int ElementCount, int DiagnosticCount,
            IReadOnlyList<SemanticNode> Children, IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<SemanticToken> Tokens);

        private SemanticNode? Value()
        {
            _ct.ThrowIfCancellationRequested();
            if (_depth > 256)
            {
                Error("JSON_DEPTH", "JSON nesting is too deep.", _position, 1);
                _uncertain = true;
                return null;
            }
            if (_position >= Length) { Error("JSON_VALUE", "Expected a JSON value.", _position, 0); return null; }
            return At(_position) switch
            {
                '{' => Container('}', "object"),
                '[' => Container(']', "array"),
                '"' => StringValue(),
                't' => Keyword("true", "boolean"),
                'f' => Keyword("false", "boolean"),
                'n' => Keyword("null", "null"),
                '-' or >= '0' and <= '9' => Number(),
                _ => UnexpectedValue()
            };
        }

        private SemanticNode? UnexpectedValue()
        {
            Error("JSON_VALUE", "Expected a JSON value.", _position, 1);
            _position++;
            return null;
        }

        private SemanticNode? Container(char close, string kind)
        {
            int start = _position++;
            _depth++;
            bool harvest = kind == "array" && _depth == 1 && _snapshot.Length > FullTreeLimit;
            if (harvest) _arrayStart = start;
            var children = new List<SemanticNode>();
            var names = kind == "object" ? new KeyTable(_snapshot, _ct, _work) : null;
            Space();
            int pageStart = _position, pageDiagnostics = _diagnosticCount, pageElements = 0;
            while (_position < Length && At(_position) != close)
            {
                _ct.ThrowIfCancellationRequested();
                int before = _position;
                var child = kind == "object" ? Property(names!) : Value();
                if (child is not null) children.Add(child);
                if (harvest) pageElements++;
                Space();
                if (_position < Length && At(_position) == ',')
                {
                    _position++;
                    Space();
                    if (_position < Length && At(_position) == close)
                        Error("JSON_TRAILING_COMMA", "Trailing comma is not valid JSON.", _position - 1, 1);
                }
                else if (_position < Length && At(_position) != close)
                {
                    Error("JSON_SEPARATOR", "Expected a comma or closing delimiter.", _position, 1);
                    Recover(close);
                }
                if (_position == before) _position++;
                if (harvest && !_indexAbandoned && _position - pageStart >= JsonArrayCertificate.TargetPageLength &&
                    _position < Length && At(_position) != close)
                {
                    AddPage(pageStart, _position, pageElements, _diagnosticCount - pageDiagnostics);
                    pageStart = _position;
                    pageDiagnostics = _diagnosticCount;
                    pageElements = 0;
                }
            }
            if (harvest && pageElements > 0 && !_indexAbandoned)
                AddPage(pageStart, _position, pageElements, _diagnosticCount - pageDiagnostics);
            if (_position < Length && At(_position) == close) _position++;
            else Error("JSON_UNCLOSED", $"Expected '{close}'.", _position, 0);
            _depth--;
            if (harvest) _arrayEnd = _position;
            return Hits(start, _position - start)
                ? new SemanticNode(kind, new TextSpan(start, _position - start), children: children)
                : null;
        }

        /// <summary>Source-sized owner admission; index refusal never weakens authoritative Full.</summary>
        private void AddPage(int start, int end, int elements, int diagnosticCount)
        {
            if (_pages.Count == JsonArrayCertificate.MaxPages)
            { _pages.Clear(); _indexAbandoned = true; return; }
            _pages.Add(new JsonArrayPage(start, end, elements, diagnosticCount, false));
        }

        private SemanticNode? Property(KeyTable names)
        {
            int start = _position;
            if (At(_position) != '"')
            {
                Error("JSON_KEY", "Object keys must be strings.", _position, 1);
                Recover('}');
                return null;
            }
            var key = StringValue("key");
            string? name = _lastStringValue;
            if (!_lastKeyValid) _uncertain = true;
            else
            {
                bool hasRaw = !_lastKeyEscaped && start >= _chunkStart &&
                    _position <= _chunkStart + _chunk.Length;
                ReadOnlySpan<char> raw = hasRaw
                    ? _chunk.Span.Slice(start + 1 - _chunkStart, _position - start - 2)
                    : default;
                var unique = names.Add(_lastKeyHash, new TextSpan(start, _position - start), raw, hasRaw);
                if (unique is null) _uncertain = true;
                else if (unique == false)
                {
                    _diagnosticCount++;
                    if (Hits(start, _position - start))
                    {
                        var label = name ?? ReadKeyLabel(start, _position - start);
                        if (!_captureAll && label.Length > 16384) label = label[..256] + "…";
                        _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "JSON_DUPLICATE_KEY",
                            $"Duplicate object key '{label}'.", new TextSpan(start, _position - start)));
                    }
                }
            }
            string? projectedName = !_captureAll && name?.Length > 16384 ? null : name;
            Space();
            if (_position >= Length || At(_position) != ':')
            {
                Error("JSON_COLON", "Expected ':' after object key.", _position, 0);
                Recover('}');
                return Hits(start, _position - start)
                    ? new SemanticNode("property", new TextSpan(start, _position - start), projectedName,
                        children: key is null ? [] : [key]) : null;
            }
            _position++;
            Space();
            var value = Value();
            if (!Hits(start, _position - start)) return null;
            var children = new List<SemanticNode>(2);
            if (key is not null) children.Add(key);
            if (value is not null) children.Add(value);
            return new SemanticNode("property", new TextSpan(start, _position - start), projectedName, children: children);
        }

        private string? _lastStringValue;

        /// <summary>Produces a bounded label for a visible duplicate without retaining a giant key.</summary>
        private string ReadKeyLabel(int start, int length)
        {
            var reader = new KeyReader(_snapshot, new TextSpan(start, length), _work, _ct);
            var chars = new char[Math.Min(256, length)];
            int count = 0;
            while (count < chars.Length && reader.Next(out var ch)) chars[count++] = ch;
            return new string(chars, 0, count) + (reader.Next(out _) ? "…" : "");
        }

        private SemanticNode? StringValue(string kind = "string")
        {
            int start = _position++;
            bool closed = false, escaped = false, invalid = false;
            bool pendingHigh = false, invalidSurrogate = false;
            ulong keyHash = 14695981039346656037UL ^ _hashSeed;
            int nextCancellation = _position;
            while (_position < Length)
            {
                // Escapes advance by several units and can skip every exact modulo
                // boundary; threshold checks remain periodic for arbitrary strings.
                if (_position >= nextCancellation)
                {
                    _ct.ThrowIfCancellationRequested();
                    nextCancellation = (int)Math.Min((long)_position + 4096, int.MaxValue);
                }
                char ch = At(_position++);
                if (ch == '"') { closed = true; break; }
                if (ch < 0x20) { Error("JSON_CONTROL", "Unescaped control character in string.", _position - 1, 1); invalid = true; }
                if (ch != '\\')
                {
                    if (pendingHigh) { invalidSurrogate = true; pendingHigh = false; }
                    if (kind == "key") keyHash = (keyHash ^ ch) * 1099511628211UL;
                    continue;
                }
                escaped = true;
                if (_position >= Length) break;
                char escape = At(_position++);
                if (escape == 'u')
                {
                    int scalar = 0;
                    bool complete = true;
                    for (int i = 0; i < 4; i++)
                    {
                        if (_position >= Length || !Uri.IsHexDigit(At(_position)))
                        {
                            Error("JSON_ESCAPE", "Expected four hexadecimal digits after \\u.", _position, 0);
                            invalid = true;
                            complete = false;
                            break;
                        }
                        scalar = (scalar << 4) | Hex(At(_position));
                        _position++;
                    }
                    if (complete)
                    {
                        bool high = scalar is >= 0xD800 and <= 0xDBFF;
                        bool low = scalar is >= 0xDC00 and <= 0xDFFF;
                        if (pendingHigh && !low) invalidSurrogate = true;
                        if (low && !pendingHigh) invalidSurrogate = true;
                        pendingHigh = high;
                        if (kind == "key") keyHash = (keyHash ^ (char)scalar) * 1099511628211UL;
                    }
                }
                else if (escape is not ('"' or '\\' or '/' or 'b' or 'f' or 'n' or 'r' or 't'))
                { Error("JSON_ESCAPE", "Invalid JSON string escape.", _position - 2, 2); invalid = true; }
                if (kind == "key" && escape != 'u')
                {
                    char decoded = escape switch
                    {
                        'b' => '\b', 'f' => '\f', 'n' => '\n', 'r' => '\r', 't' => '\t',
                        _ => escape
                    };
                    keyHash = (keyHash ^ decoded) * 1099511628211UL;
                }
                if (escape != 'u' && pendingHigh) { invalidSurrogate = true; pendingHigh = false; }
            }
            if (!closed) Error("JSON_STRING", "Unterminated JSON string.", start, _position - start);
            if (pendingHigh) invalidSurrogate = true;
            if (closed && !invalid && invalidSurrogate)
                Error("JSON_STRING", "Invalid JSON string.", start, _position - start);
            string? value = null;
            bool decodeFailed = false;
            var capture = Hits(start, _position - start);
            // Off-screen values need no decoded payload. Even a visible multi-MiB
            // scalar must not become a second whole-file allocation.
            if (closed && !invalid && !invalidSurrogate && (_captureAll || capture && _position - start <= 16384))
            {
                try
                {
                    if (!escaped) value = Slice(start + 1, _position - start - 2);
                    else { using var parsed = JsonDocument.Parse(Slice(start, _position - start)); value = parsed.RootElement.GetString(); }
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException)
                { Error("JSON_STRING", "Invalid JSON string.", start, _position - start); decodeFailed = true; }
            }
            if (kind == "key")
            {
                _lastStringValue = value;
                _lastKeyHash = Mix(keyHash);
                _lastKeyValid = closed && !invalid && !invalidSurrogate && !decodeFailed;
                _lastKeyEscaped = escaped;
            }
            var span = new TextSpan(start, _position - start);
            Token(kind, span);
            return capture ? new SemanticNode(kind, span,
                value: kind == "key" && !_captureAll && value?.Length > 16384 ? null : value) : null;
        }

        private static int Hex(char ch) => ch <= '9' ? ch - '0' : (ch | 0x20) - 'a' + 10;

        /// <summary>Avalanches FNV's weak low bits before power-of-two table indexing.</summary>
        private static ulong Mix(ulong value)
        {
            value ^= value >> 33;
            value *= 0xff51afd7ed558ccdUL;
            value ^= value >> 33;
            value *= 0xc4ceb9fe1a85ec53UL;
            return value ^ (value >> 33);
        }

        /// <summary>Reads a validated key as decoded UTF-16 without materializing its value.</summary>
        private sealed class KeyReader
        {
            private readonly TextSnapshot _snapshot;
            private readonly int _end;
            private readonly SourceWork? _work;
            private readonly CancellationToken _ct;
            private int _position;
            private string _window = string.Empty;
            private int _windowStart = -1;

            internal KeyReader(TextSnapshot snapshot, TextSpan span, SourceWork? work = null, CancellationToken ct = default)
            {
                _snapshot = snapshot;
                _work = work;
                _ct = ct;
                _position = span.Start + 1;
                _end = span.End - 1;
            }

            private char Read()
            {
                _work?.Visit();
                if (_position < _windowStart || _position >= _windowStart + _window.Length)
                {
                    _ct.ThrowIfCancellationRequested();
                    _windowStart = _position;
                    int count = Math.Min(4096, _end - _position);
                    _work?.Visit(count); // Charge bounded lookahead even when comparison stops early.
                    _window = _snapshot.GetText(_position, count);
                }
                return _window[_position++ - _windowStart];
            }

            internal bool Next(out char value)
            {
                if (_position >= _end) { value = default; return false; }
                value = Read();
                if (value != '\\') return true;
                char escape = Read();
                if (escape == 'u')
                {
                    int scalar = 0;
                    for (int i = 0; i < 4; i++) scalar = (scalar << 4) | Hex(Read());
                    value = (char)scalar;
                    return true;
                }
                value = escape switch
                {
                    'b' => '\b', 'f' => '\f', 'n' => '\n', 'r' => '\r', 't' => '\t',
                    _ => escape
                };
                return true;
            }
        }

        /// <summary>
        /// Exact per-object key set. Entries store source spans and randomized decoded hashes;
        /// equal hashes are always verified against decoded UTF-16, never trusted as identity.
        /// </summary>
        private sealed class KeyTable
        {
            private const int MaxChain = 128;
            private const int MaxEntries = 10_000_000;
            private const int PageSize = 1 << 16;
            private const int SmallPageTotal = PageSize - 4;
            private readonly TextSnapshot _snapshot;
            private readonly CancellationToken _ct;
            private readonly SourceWork? _work;
            private readonly List<KeyEntry[]> _pages = [];
            private int[] _buckets = new int[8];
            private int _capacity;
            private int _count;
            private bool _abandoned;
            private ulong _hotHash;
            private string? _hotKey;

            internal KeyTable(TextSnapshot snapshot, CancellationToken ct) : this(snapshot, ct, null) { }

            /// <summary>Shares the turn's visit budget across exact duplicate comparisons.</summary>
            internal KeyTable(TextSnapshot snapshot, CancellationToken ct, SourceWork? work)
            {
                _snapshot = snapshot;
                _ct = ct;
                _work = work;
            }

            /// <summary>Collision-verification entry point when no borrowed raw span exists.</summary>
            internal bool? Add(ulong hash, TextSpan span) => Add(hash, span, default, false);

            /// <returns>True if new, false if an exact duplicate, null if the budget prevents proof.</returns>
            internal bool? Add(ulong hash, TextSpan span, ReadOnlySpan<char> raw, bool hasRaw)
            {
                if (_abandoned) return null;
                // Repeated short raw keys are common in tabular JSON. A single exact
                // decoded hot key avoids two ranged source reads per duplicate.
                if (hasRaw && _hotKey is not null && hash == _hotHash)
                {
                    _work?.Visit(Math.Min(raw.Length, _hotKey.Length));
                    if (raw.SequenceEqual(_hotKey)) return false;
                }
                if (_count >= MaxEntries)
                { _abandoned = true; return null; }
                if (_count >= _buckets.Length * 2 && !Grow())
                { _abandoned = true; return null; }
                int bucket = (int)(hash & (uint)(_buckets.Length - 1));
                int current = _buckets[bucket];
                for (int probes = 0; current != 0; probes++)
                {
                    if (probes >= MaxChain) { _abandoned = true; return null; }
                    var entry = Entry(current - 1);
                    if (entry.Hash == hash && EqualsDecoded(new TextSpan(entry.Start, entry.Length), span))
                    {
                        if (hasRaw && raw.Length <= 64)
                        { _hotHash = hash; _hotKey = new string(raw); }
                        return false;
                    }
                    current = entry.Next;
                }
                if (_count == _capacity)
                {
                    int size = _pages.Count < 14 ? 4 << _pages.Count : PageSize;
                    _pages.Add(new KeyEntry[size]);
                    _capacity += size;
                }
                ref var slot = ref EntryRef(_count);
                slot =
                    new KeyEntry(hash, span.Start, span.Length, _buckets[bucket]);
                _buckets[bucket] = ++_count;
                return true;
            }

            /// <summary>Gets a previously inserted entry without moving its page.</summary>
            private KeyEntry Entry(int index) => EntryRef(index);

            private ref KeyEntry EntryRef(int index)
            {
                if (index >= SmallPageTotal)
                {
                    int tail = index - SmallPageTotal;
                    return ref _pages[14 + tail / PageSize][tail & (PageSize - 1)];
                }
                int page = 0, size = 4;
                while (index >= size) { index -= size; size <<= 1; page++; }
                return ref _pages[page][index];
            }

            private bool Grow()
            {
                if (_buckets.Length >= 1 << 23) return false;
                var grown = new int[_buckets.Length * 2];
                for (int i = 0; i < _count; i++)
                {
                    if ((i & 4095) == 0) _ct.ThrowIfCancellationRequested();
                    ref var entry = ref EntryRef(i);
                    int bucket = (int)(entry.Hash & (uint)(grown.Length - 1));
                    entry.Next = grown[bucket];
                    grown[bucket] = i + 1;
                }
                _buckets = grown;
                return true;
            }

            private bool EqualsDecoded(TextSpan left, TextSpan right)
            {
                var a = new KeyReader(_snapshot, left, _work, _ct);
                var b = new KeyReader(_snapshot, right, _work, _ct);
                int checkedChars = 0;
                while (true)
                {
                    if ((checkedChars++ & 4095) == 0) _ct.ThrowIfCancellationRequested();
                    bool hasA = a.Next(out var ca);
                    bool hasB = b.Next(out var cb);
                    if (hasA != hasB) return false;
                    if (!hasA) return true;
                    if (ca != cb) return false;
                }
            }

            private struct KeyEntry
            {
                internal KeyEntry(ulong hash, int start, int length, int next)
                { Hash = hash; Start = start; Length = length; Next = next; }

                internal readonly ulong Hash;
                internal readonly int Start;
                internal readonly int Length;
                internal int Next;
            }
        }

        private SemanticNode? Keyword(string expected, string kind)
        {
            int start = _position;
            int end = Math.Min(Length, start + expected.Length);
            _position = end;
            bool matches = end - start == expected.Length;
            for (int i = start; i < end; i++)
                if (At(i) != expected[i - start])
                { matches = false; break; }
            if (!matches) Error("JSON_LITERAL", $"Expected '{expected}'.", start, end - start);
            var span = new TextSpan(start, end - start);
            Token(kind, span);
            return Hits(start, span.Length) ? new SemanticNode(kind, span,
                value: _captureAll || span.Length <= 16384 ? Slice(start, span.Length) : null) : null;
        }

        private SemanticNode? Number()
        {
            int start = _position;
            if (At(_position) == '-') _position++;
            if (_position < Length && At(_position) == '0') _position++;
            else
            {
                int digits = _position;
                while (_position < Length && char.IsAsciiDigit(At(_position)))
                { if ((_position & 4095) == 0) _ct.ThrowIfCancellationRequested(); _position++; }
                if (_position == digits) Error("JSON_NUMBER", "Expected a digit.", start, _position - start);
            }
            if (_position < Length && At(_position) == '.')
            {
                _position++;
                int digits = _position;
                while (_position < Length && char.IsAsciiDigit(At(_position)))
                { if ((_position & 4095) == 0) _ct.ThrowIfCancellationRequested(); _position++; }
                if (_position == digits) Error("JSON_NUMBER", "Fraction requires digits.", start, _position - start);
            }
            if (_position < Length && At(_position) is 'e' or 'E')
            {
                _position++;
                if (_position < Length && At(_position) is '+' or '-') _position++;
                int digits = _position;
                while (_position < Length && char.IsAsciiDigit(At(_position)))
                { if ((_position & 4095) == 0) _ct.ThrowIfCancellationRequested(); _position++; }
                if (_position == digits) Error("JSON_NUMBER", "Exponent requires digits.", start, _position - start);
            }
            var span = new TextSpan(start, _position - start);
            Token("number", span);
            return Hits(start, span.Length) ? new SemanticNode("number", span,
                value: _captureAll || span.Length <= 16384 ? Slice(start, span.Length) : null) : null;
        }

        private void Space()
        {
            while (_position < Length && At(_position) is ' ' or '\t' or '\r' or '\n')
            {
                if ((_position & 4095) == 0) _ct.ThrowIfCancellationRequested();
                _position++;
            }
        }

        private void Recover(char close)
        {
            var start = _position;
            while (_position < Length && At(_position) != ',' && At(_position) != close)
            {
                if ((_position & 4095) == 0) _ct.ThrowIfCancellationRequested();
                _position++;
            }
            if (_position != start) _uncertain = true;
        }

        private void Error(string code, string message, int start, int length)
        {
            _syntaxError = true;
            _diagnosticCount++;
            if (Hits(start, length))
                _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, code, message, new TextSpan(start, length)));
        }

        private void Token(string kind, TextSpan span)
        {
            if (Hits(span.Start, span.Length)) _tokens.Add(new SemanticToken(kind, span));
        }
    }
}
