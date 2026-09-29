using System.Text;
using System.Text.Json;

namespace Mote.Formats;

/// <summary>Strict JSON policy with source-positioned syntax trees and duplicate-member diagnostics.</summary>
public sealed class JsonPolicy : IDocumentPolicy
{
    /// <inheritdoc />
    public DocumentKind Kind => DocumentKind.Json;
    /// <inheritdoc />
    public string DisplayName => "JSON";

    /// <inheritdoc />
    public FormatAnalysis Analyze(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new Parser(text, cancellationToken).Parse();
    }

    /// <inheritdoc />
    public string Format(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var analysis = Analyze(text);
        if (analysis.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) return text;
        try
        {
            using var document = JsonDocument.Parse(text);
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
                document.WriteTo(writer);
            return Encoding.UTF8.GetString(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
        }
        catch (JsonException) { return text; }
    }

    /// <inheritdoc />
    public string RenderHtml(FormatAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        return FormatHelpers.RenderTree(analysis, "mote-json");
    }

    /// <summary>Recursive-descent parser whose cursor and every range use UTF-16 editor offsets.</summary>
    private sealed class Parser
    {
        private readonly string _text;
        private readonly CancellationToken _cancellationToken;
        private readonly List<Diagnostic> _diagnostics = [];
        private readonly List<SemanticToken> _tokens = [];
        private int _position;
        private int _depth;

        internal Parser(string text, CancellationToken cancellationToken)
        {
            _text = text;
            _cancellationToken = cancellationToken;
        }

        internal FormatAnalysis Parse()
        {
            Space();
            var value = Value();
            Space();
            if (_position < _text.Length) Error("JSON_TRAILING", "Unexpected content after the JSON value.", _position, _text.Length - _position);
            var root = new SemanticNode("document", new TextSpan(0, _text.Length), children: value is null ? [] : [value]);
            return new FormatAnalysis(_text, root, _diagnostics, _tokens);
        }

        /// <summary>Parses one recursive JSON value, limiting depth to bound editor work.</summary>
        private SemanticNode? Value()
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (_depth > 256) { Error("JSON_DEPTH", "JSON nesting is too deep.", _position, 1); return null; }
            if (_position >= _text.Length) { Error("JSON_VALUE", "Expected a JSON value.", _position, 0); return null; }
            return _text[_position] switch
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

        /// <summary>Consumes one unexpected character so recovery always advances.</summary>
        private SemanticNode? UnexpectedValue()
        {
            Error("JSON_VALUE", "Expected a JSON value.", _position, 1);
            _position++;
            return null;
        }

        /// <summary>Parses array/object children while recovering at comma or the matching close.</summary>
        private SemanticNode Container(char close, string kind)
        {
            int start = _position++;
            _depth++;
            var children = new List<SemanticNode>();
            var names = kind == "object" ? new HashSet<string>(StringComparer.Ordinal) : null;
            Space();
            while (_position < _text.Length && _text[_position] != close)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                int before = _position;
                SemanticNode? child = kind == "object" ? Property(names!) : Value();
                if (child is not null) children.Add(child);
                Space();
                if (_position < _text.Length && _text[_position] == ',')
                {
                    _position++;
                    Space();
                    if (_position < _text.Length && _text[_position] == close) Error("JSON_TRAILING_COMMA", "Trailing comma is not valid JSON.", _position - 1, 1);
                }
                else if (_position < _text.Length && _text[_position] != close)
                {
                    Error("JSON_SEPARATOR", "Expected a comma or closing delimiter.", _position, 1);
                    Recover(close);
                }
                if (_position == before) _position++;
            }
            if (_position < _text.Length && _text[_position] == close) _position++;
            else Error("JSON_UNCLOSED", $"Expected '{close}'.", _position, 0);
            _depth--;
            return new SemanticNode(kind, new TextSpan(start, _position - start), children: children);
        }

        /// <summary>Checks decoded property names, not source spellings, for duplicate bindings.</summary>
        private SemanticNode? Property(HashSet<string> names)
        {
            int start = _position;
            if (_text[_position] != '"')
            {
                Error("JSON_KEY", "Object keys must be strings.", _position, 1);
                Recover('}');
                return null;
            }
            var key = StringValue("key");
            string name = key.Value ?? string.Empty;
            if (!names.Add(name)) Error("JSON_DUPLICATE_KEY", $"Duplicate object key '{name}'.", key.Span.Start, key.Span.Length);
            Space();
            if (_position >= _text.Length || _text[_position] != ':')
            {
                Error("JSON_COLON", "Expected ':' after object key.", _position, 0);
                Recover('}');
                return new SemanticNode("property", new TextSpan(start, _position - start), name, children: [key]);
            }
            _position++;
            Space();
            var value = Value();
            return new SemanticNode("property", new TextSpan(start, _position - start), name,
                children: value is null ? [key] : [key, value]);
        }

        /// <summary>Validates escapes and decodes strings only when semantic values are needed.</summary>
        private SemanticNode StringValue(string kind = "string")
        {
            int start = _position++;
            bool closed = false;
            bool escaped = false;
            bool invalid = false;
            while (_position < _text.Length)
            {
                char ch = _text[_position++];
                if (ch == '"') { closed = true; break; }
                if (ch < 0x20) { Error("JSON_CONTROL", "Unescaped control character in string.", _position - 1, 1); invalid = true; }
                if (ch != '\\') continue;
                escaped = true;
                if (_position >= _text.Length) break;
                char escape = _text[_position++];
                if (escape == 'u')
                {
                    for (int i = 0; i < 4; i++)
                    {
                        if (_position >= _text.Length || !Uri.IsHexDigit(_text[_position]))
                        {
                            Error("JSON_ESCAPE", "Expected four hexadecimal digits after \\u.", _position, 0);
                            invalid = true;
                            break;
                        }
                        _position++;
                    }
                }
                else if (escape is not ('"' or '\\' or '/' or 'b' or 'f' or 'n' or 'r' or 't'))
                { Error("JSON_ESCAPE", "Invalid JSON string escape.", _position - 2, 2); invalid = true; }
            }
            if (!closed) Error("JSON_STRING", "Unterminated JSON string.", start, _position - start);
            string? value = null;
            if (closed && !invalid)
            {
                try
                {
                    if (!escaped) value = _text.Substring(start + 1, _position - start - 2);
                    else { using var parsed = JsonDocument.Parse(_text.Substring(start, _position - start)); value = parsed.RootElement.GetString(); }
                }
                catch (JsonException) { Error("JSON_STRING", "Invalid JSON string.", start, _position - start); }
            }
            var span = new TextSpan(start, _position - start);
            _tokens.Add(new SemanticToken(kind, span));
            return new SemanticNode(kind, span, value: value);
        }

        /// <summary>Parses one fixed JSON literal without allocating a token string.</summary>
        private SemanticNode Keyword(string expected, string kind)
        {
            int start = _position;
            int end = Math.Min(_text.Length, start + expected.Length);
            _position = end;
            if (!_text.AsSpan(start, end - start).SequenceEqual(expected)) Error("JSON_LITERAL", $"Expected '{expected}'.", start, end - start);
            var span = new TextSpan(start, end - start);
            _tokens.Add(new SemanticToken(kind, span));
            return new SemanticNode(kind, span, value: _text[start..end]);
        }

        /// <summary>Recognizes JSON's decimal grammar without accepting host-language extensions.</summary>
        private SemanticNode Number()
        {
            int start = _position;
            if (_text[_position] == '-') _position++;
            if (_position < _text.Length && _text[_position] == '0') _position++;
            else
            {
                int digits = _position;
                while (_position < _text.Length && char.IsAsciiDigit(_text[_position])) _position++;
                if (_position == digits) Error("JSON_NUMBER", "Expected a digit.", start, _position - start);
            }
            if (_position < _text.Length && _text[_position] == '.')
            {
                _position++;
                int digits = _position;
                while (_position < _text.Length && char.IsAsciiDigit(_text[_position])) _position++;
                if (_position == digits) Error("JSON_NUMBER", "Fraction requires digits.", start, _position - start);
            }
            if (_position < _text.Length && _text[_position] is 'e' or 'E')
            {
                _position++;
                if (_position < _text.Length && _text[_position] is '+' or '-') _position++;
                int digits = _position;
                while (_position < _text.Length && char.IsAsciiDigit(_text[_position])) _position++;
                if (_position == digits) Error("JSON_NUMBER", "Exponent requires digits.", start, _position - start);
            }
            var span = new TextSpan(start, _position - start);
            _tokens.Add(new SemanticToken("number", span));
            return new SemanticNode("number", span, value: _text[start.._position]);
        }

        private void Space()
        {
            while (_position < _text.Length && _text[_position] is ' ' or '\t' or '\r' or '\n') _position++;
        }

        private void Recover(char close)
        {
            while (_position < _text.Length && _text[_position] != ',' && _text[_position] != close) _position++;
        }

        private void Error(string code, string message, int start, int length) =>
            _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, code, message, new TextSpan(start, length)));
    }
}
