using System.Text.Json;
using Mote.Engine;

namespace Mote.Formats;

/// <summary>
/// Streaming JSON semantic session. The authoritative snapshot is reparsed on every call;
/// no edit checkpoint is assumed safe across an unterminated string or changed delimiter.
/// </summary>
/// <remarks>
/// The whole snapshot is scanned while large-document trees, tokens, and diagnostics
/// are projected to the requested range. Depth limits and error recovery that skip
/// unknown structure return Provisional rather than asserting an exact global count.
/// A small Full request preserves the legacy tree. No whole-snapshot string is materialized.
/// </remarks>
internal sealed class JsonIncrementalSession : IFormatSession
{
    private const int FullTreeLimit = 1024 * 1024;
    private const int MaxProjection = 256 * 1024;
    private bool _disposed;

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

        var captureAll = request.Scope == AnalysisScope.Full && snapshot.Length <= FullTreeLimit;
        // A caller may request the entire 100 MiB file as its "viewport". Keep the
        // semantic scan global, but bound retained nodes/tokens to the leading window.
        var projectedRange = new TextSpan(range.Start, Math.Min(range.Length, MaxProjection));
        var parser = new Parser(snapshot, projectedRange, captureAll, cancellationToken);
        var result = parser.Parse();
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    /// <inheritdoc />
    public void Dispose() => _disposed = true;

    /// <summary>Range-backed recursive descent, deliberately matching legacy recovery and diagnostic ordering.</summary>
    private sealed class Parser
    {
        private readonly TextSnapshot _snapshot;
        private readonly TextSpan _visible;
        private readonly bool _captureAll;
        private readonly CancellationToken _ct;
        private readonly IEnumerator<ReadOnlyMemory<char>> _chunks;
        private readonly List<Diagnostic> _diagnostics = [];
        private readonly List<SemanticToken> _tokens = [];
        private ReadOnlyMemory<char> _chunk;
        private int _chunkStart;
        private int _position;
        private int _depth;
        private int _diagnosticCount;
        private bool _uncertain;

        internal Parser(TextSnapshot snapshot, TextSpan visible, bool captureAll, CancellationToken ct)
        {
            _snapshot = snapshot;
            _visible = visible;
            _captureAll = captureAll;
            _ct = ct;
            _chunks = snapshot.GetChunks().GetEnumerator();
        }

        private int Length => _snapshot.Length;

        /// <summary>Reads immutable rope chunks directly; grammar access is forward-only.</summary>
        private char At(int position)
        {
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

        private string Slice(int start, int length) => _snapshot.GetText(start, length);

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
                var root = new SemanticNode("document", new TextSpan(0, Length), children: value is null ? [] : [value]);
                return new DocumentAnalysis(_snapshot.Version,
                    _uncertain ? _visible : new TextSpan(0, Length),
                    _uncertain ? AnalysisCompleteness.Provisional : AnalysisCompleteness.Complete,
                    root, _diagnostics, _tokens, _uncertain ? null : _diagnosticCount);
            }
            finally { _chunks.Dispose(); }
        }

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
            var children = new List<SemanticNode>();
            var names = kind == "object" ? new HashSet<string>(StringComparer.Ordinal) : null;
            Space();
            while (_position < Length && At(_position) != close)
            {
                _ct.ThrowIfCancellationRequested();
                int before = _position;
                var child = kind == "object" ? Property(names!) : Value();
                if (child is not null) children.Add(child);
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
            }
            if (_position < Length && At(_position) == close) _position++;
            else Error("JSON_UNCLOSED", $"Expected '{close}'.", _position, 0);
            _depth--;
            return Hits(start, _position - start)
                ? new SemanticNode(kind, new TextSpan(start, _position - start), children: children)
                : null;
        }

        private SemanticNode? Property(HashSet<string> names)
        {
            int start = _position;
            if (At(_position) != '"')
            {
                Error("JSON_KEY", "Object keys must be strings.", _position, 1);
                Recover('}');
                return null;
            }
            var key = StringValue("key");
            // An invalid escape/surrogate has no decoded key identity. Treating all such
            // keys as the empty string invents duplicates and a false global count.
            string? name = _lastStringValue;
            if (name is null) _uncertain = true;
            else if (!names.Add(name))
            {
                var label = !_captureAll && name.Length > 16384 ? name[..256] + "…" : name;
                Error("JSON_DUPLICATE_KEY", $"Duplicate object key '{label}'.", start, _position - start);
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

        private SemanticNode? StringValue(string kind = "string")
        {
            int start = _position++;
            bool closed = false, escaped = false, invalid = false;
            bool pendingHigh = false, invalidSurrogate = false;
            while (_position < Length)
            {
                if ((_position & 4095) == 0) _ct.ThrowIfCancellationRequested();
                char ch = At(_position++);
                if (ch == '"') { closed = true; break; }
                if (ch < 0x20) { Error("JSON_CONTROL", "Unescaped control character in string.", _position - 1, 1); invalid = true; }
                if (ch != '\\')
                {
                    if (pendingHigh) { invalidSurrogate = true; pendingHigh = false; }
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
                    }
                }
                else if (escape is not ('"' or '\\' or '/' or 'b' or 'f' or 'n' or 'r' or 't'))
                { Error("JSON_ESCAPE", "Invalid JSON string escape.", _position - 2, 2); invalid = true; }
                if (escape != 'u' && pendingHigh) { invalidSurrogate = true; pendingHigh = false; }
            }
            if (!closed) Error("JSON_STRING", "Unterminated JSON string.", start, _position - start);
            if (pendingHigh) invalidSurrogate = true;
            if (closed && !invalid && invalidSurrogate)
                Error("JSON_STRING", "Invalid JSON string.", start, _position - start);
            string? value = null;
            var capture = Hits(start, _position - start);
            // Off-screen values need no decoded payload. Even a visible multi-MiB
            // scalar must not become a second whole-file allocation.
            if (closed && !invalid && !invalidSurrogate && (kind == "key" || capture && (_captureAll || _position - start <= 16384)))
            {
                try
                {
                    if (!escaped) value = Slice(start + 1, _position - start - 2);
                    else { using var parsed = JsonDocument.Parse(Slice(start, _position - start)); value = parsed.RootElement.GetString(); }
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException)
                { Error("JSON_STRING", "Invalid JSON string.", start, _position - start); }
            }
            if (kind == "key") _lastStringValue = value;
            var span = new TextSpan(start, _position - start);
            Token(kind, span);
            return capture ? new SemanticNode(kind, span,
                value: kind == "key" && !_captureAll && value?.Length > 16384 ? null : value) : null;
        }

        private static int Hex(char ch) => ch <= '9' ? ch - '0' : (ch | 0x20) - 'a' + 10;

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
