using SharpYaml;
using SharpYaml.Events;
using SharpYaml.Syntax;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Mote.Formats;

/// <summary>
/// YAML 1.2 policy backed by SharpYaml's parser events and lossless syntax tokens. Semantic
/// nodes represent mapping, sequence, scalar and alias constructs, including flow style,
/// tags and multiple documents. Mapping key uniqueness follows YAML's tag-aware scalar
/// canonicalization and recursive collection equality. Cyclic aliases and custom scalar tags
/// without a known canonicalizer are warned rather than guessed. Malformed edits return
/// diagnostics instead of retaining a stale semantic tree.
/// </summary>
public sealed class YamlPolicy : IDocumentPolicy
{
    /// <inheritdoc />
    public DocumentKind Kind => DocumentKind.Yaml;
    /// <inheritdoc />
    public string DisplayName => "YAML";

    /// <inheritdoc />
    public FormatAnalysis Analyze(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        var diagnostics = new List<Diagnostic>();
        var tokens = new List<SemanticToken>();
        var documents = new List<SemanticNode>();
        try
        {
            // Syntax tokens retain punctuation and trivia that event-level semantic nodes omit.
            var syntax = YamlSyntaxTree.Parse(text);
            foreach (var token in syntax.Tokens)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var span = token.Span;
                var start = Math.Clamp(span.Start.Index, 0, text.Length);
                var end = Math.Clamp(span.End.Index, start, text.Length);
                if (end > start) tokens.Add(new SemanticToken(token.Kind.ToString(), new TextSpan(start, end - start)));
            }
            var parser = Parser.CreateParser(new StringReader(text));
            var projector = new Projector(parser, text.Length, diagnostics, tokens, cancellationToken);
            documents.AddRange(projector.ParseDocuments());
        }
        catch (YamlException error)
        {
            var start = Math.Clamp(error.Start.Index, 0, text.Length);
            var end = Math.Clamp(error.End.Index, start, text.Length);
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "yaml.syntax", error.Message, new TextSpan(start, end - start)));
        }
        catch (FormatException error)
        {
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "yaml.syntax", error.Message, new TextSpan(text.Length, 0)));
        }
        catch (YamlDepthException error)
        {
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "yaml.depth-limit", "YAML nesting exceeds the safe semantic projection limit.", new TextSpan(Math.Clamp(error.Offset, 0, text.Length), 0)));
        }
        return new FormatAnalysis(text, new SemanticNode("document", new TextSpan(0, text.Length), children: documents), diagnostics, tokens);
    }

    /// <inheritdoc />
    public string Format(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var before = Analyze(text);
        if (before.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) return text;
        var edits = new List<(int Start, int Length)>();
        if (!CollectSeparatorEdits(before.Root, text, edits) || edits.Count == 0) return text;
        var output = new StringBuilder(text);
        foreach (var edit in edits.OrderByDescending(e => e.Start))
            output.Remove(edit.Start, edit.Length).Insert(edit.Start, ": ");
        var candidate = output.ToString();
        var after = Analyze(candidate);
        return after.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) || !SameSemantics(before.Root, after.Root)
            ? text : candidate;
    }

    /// <summary>
    /// Only replace the literal gap between scalar key and scalar value when it is a colon
    /// followed solely by extra spaces on the same line. Complex keys and block scalars veto
    /// the whole operation; comments, indentation, and flow syntax are never rewritten.
    /// </summary>
    private static bool CollectSeparatorEdits(SemanticNode node, string source, List<(int Start, int Length)> edits)
    {
        if (node.Kind == "mapping" && node.Span.Start < source.Length && source[node.Span.Start] == '{')
            return false;
        if (node.Kind == "entry" && node.Children.Count == 2)
        {
            var key = node.Children[0];
            var value = node.Children[1];
            if (key.Kind != "scalar") return false;
            if (value.Kind == "scalar" && value.Span.Start < source.Length && source[value.Span.Start] is not ('|' or '>'))
            {
                var start = key.Span.End;
                var length = value.Span.Start - start;
                if (length > 2 && start >= 0 && start + length <= source.Length && source[start] == ':' &&
                    source.AsSpan(start + 1, length - 1).IndexOfAnyExcept(' ') < 0)
                    edits.Add((start, length));
            }
        }
        foreach (var child in node.Children)
            if (!CollectSeparatorEdits(child, source, edits)) return false;
        return true;
    }

    /// <summary>Compares semantic trees while ignoring offsets and alias binding offsets.</summary>
    private static bool SameSemantics(SemanticNode before, SemanticNode after)
    {
        if (before.Kind != after.Kind || before.Name != after.Name || before.Children.Count != after.Children.Count) return false;
        if (before.Kind != "alias" && before.Value != after.Value) return false;
        for (var i = 0; i < before.Children.Count; i++)
            if (!SameSemantics(before.Children[i], after.Children[i])) return false;
        return true;
    }

    /// <inheritdoc />
    public string RenderHtml(FormatAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        return FormatHelpers.RenderTree(analysis, "mote-yaml");
    }

    /// <summary>Separates a safe projection limit from malformed YAML syntax.</summary>
    private sealed class YamlDepthException(int offset) : Exception
    {
        public int Offset { get; } = offset;
    }

    /// <summary>
    /// Converts a forward-only event stream to source-spanned semantic nodes. Each ParseNode
    /// call consumes exactly one YAML node, leaving Current at its next sibling or end event.
    /// </summary>
    private sealed class Projector
    {
        private readonly IParser _parser;
        private readonly int _length;
        private readonly List<Diagnostic> _diagnostics;
        private readonly List<SemanticToken> _tokens;
        private readonly CancellationToken _cancellationToken;
        private readonly Dictionary<string, int> _anchors = new(StringComparer.Ordinal);
        private readonly Dictionary<int, SemanticNode> _anchorNodes = new();
        private readonly Dictionary<SemanticNode, (string Tag, string Value, bool Supported)> _scalarKeys = new(ReferenceEqualityComparer.Instance);

        public Projector(IParser parser, int length, List<Diagnostic> diagnostics,
            List<SemanticToken> tokens, CancellationToken cancellationToken)
        {
            _parser = parser;
            _length = length;
            _diagnostics = diagnostics;
            _tokens = tokens;
            _cancellationToken = cancellationToken;
        }

        public IReadOnlyList<SemanticNode> ParseDocuments()
        {
            var documents = new List<SemanticNode>();
            while (_parser.MoveNext())
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (_parser.Current is not DocumentStart start) continue;
                _anchors.Clear();
                _anchorNodes.Clear();
                if (!_parser.MoveNext()) break;
                var children = new List<SemanticNode>();
                if (_parser.Current is not DocumentEnd) children.Add(ParseNode());
                if (children.Count != 0) CheckMappingKeys(children[0]);
                var end = _parser.Current is DocumentEnd documentEnd ? documentEnd.End : start.End;
                documents.Add(new SemanticNode("yaml-document", Range(start.Start, end), children: children));
            }
            return documents;
        }

        private SemanticNode ParseNode(int depth = 0)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var current = _parser.Current;
            if (depth > 256) throw new YamlDepthException(current?.Start.Index ?? _length);
            if (current is Scalar scalar)
            {
                RegisterAnchor(scalar);
                var span = Range(scalar.Start, scalar.End);
                var identity = ScalarIdentity(scalar);
                var kind = ScalarKind(identity.Tag);
                _tokens.Add(new SemanticToken(kind, span));
                if (!string.IsNullOrEmpty(scalar.Tag) &&
                    ((!identity.Supported && IsCoreScalarTag(identity.Tag)) ||
                     identity.Tag is "tag:yaml.org,2002:seq" or "tag:yaml.org,2002:map"))
                    _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "yaml.invalid-tagged-scalar", $"Scalar is not valid for explicit tag '{scalar.Tag}'.", span));
                _parser.MoveNext();
                var node = new SemanticNode("scalar", span, scalar.Tag, scalar.Value);
                _scalarKeys[node] = identity;
                BindAnchor(scalar, node);
                return node;
            }
            if (current is AnchorAlias alias)
            {
                var span = Range(alias.Start, alias.End);
                _tokens.Add(new SemanticToken("alias", span));
                var bound = _anchors.TryGetValue(alias.Value, out var target);
                if (!bound)
                    _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "yaml.undefined-alias", $"Alias '*{alias.Value}' has no preceding anchor.", span));
                _parser.MoveNext();
                return new SemanticNode("alias", span, alias.Value, bound ? target.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
            }
            if (current is SequenceStart sequence)
            {
                RegisterAnchor(sequence);
                ValidateCollectionTag(sequence, "seq");
                _parser.MoveNext();
                var items = new List<SemanticNode>();
                while (_parser.Current is not SequenceEnd)
                {
                    if (_parser.Current is null) throw new FormatException("Unexpected end of YAML sequence.");
                    var value = ParseNode(depth + 1);
                    items.Add(new SemanticNode("item", value.Span, children: new[] { value }));
                }
                var span = Range(sequence.Start, _parser.Current.End);
                _parser.MoveNext();
                var node = new SemanticNode("sequence", span, sequence.Tag, children: items);
                BindAnchor(sequence, node);
                return node;
            }
            if (current is MappingStart mapping)
            {
                RegisterAnchor(mapping);
                ValidateCollectionTag(mapping, "map");
                _parser.MoveNext();
                var entries = new List<SemanticNode>();
                while (_parser.Current is not MappingEnd)
                {
                    if (_parser.Current is null) throw new FormatException("Unexpected end of YAML mapping.");
                    var key = ParseNode(depth + 1);
                    if (_parser.Current is null or MappingEnd) throw new FormatException("Mapping key has no value.");
                    var value = ParseNode(depth + 1);
                    var span = new TextSpan(key.Span.Start, Math.Max(0, value.Span.End - key.Span.Start));
                    entries.Add(new SemanticNode("entry", span, key.Value, children: new[] { key, value }));
                    _tokens.Add(new SemanticToken("key", key.Span));
                }
                var mapSpan = Range(mapping.Start, _parser.Current.End);
                _parser.MoveNext();
                var node = new SemanticNode("mapping", mapSpan, mapping.Tag, children: entries);
                BindAnchor(mapping, node);
                return node;
            }
            throw new FormatException($"Unexpected YAML event {current?.GetType().Name ?? "end of input"}.");
        }

        /// <summary>YAML aliases may only reference anchors introduced earlier in the stream.</summary>
        private void RegisterAnchor(NodeEvent node)
        {
            if (!string.IsNullOrEmpty(node.Anchor)) _anchors[node.Anchor] = node.Start.Index;
        }

        /// <summary>Preserves the anchored node behind its definition offset, even if names are reused.</summary>
        private void BindAnchor(NodeEvent source, SemanticNode target)
        {
            if (!string.IsNullOrEmpty(source.Anchor)) _anchorNodes[source.Start.Index] = target;
        }

        /// <summary>Core tags prescribe a node kind; custom collection tags remain application-defined.</summary>
        private void ValidateCollectionTag(NodeEvent node, string expected)
        {
            if (string.IsNullOrEmpty(node.Tag)) return;
            var tag = NormalizeTag(node.Tag, expected);
            if ((IsCoreScalarTag(tag) || tag is "tag:yaml.org,2002:seq" or "tag:yaml.org,2002:map") &&
                tag != "tag:yaml.org,2002:" + expected)
                _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "yaml.invalid-tagged-collection", $"Tag '{node.Tag}' cannot be applied to a {expected} node.", Range(node.Start, node.End)));
        }

        private TextSpan Range(Mark start, Mark end)
        {
            var offset = Math.Clamp(start.Index, 0, _length);
            return new TextSpan(offset, Math.Clamp(end.Index, offset, _length) - offset);
        }

        /// <summary>Checks all mapping keys after aliases have been bound to complete nodes.</summary>
        private void CheckMappingKeys(SemanticNode node)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (node.Kind == "mapping")
            {
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in node.Children)
                {
                    if (entry.Children.Count != 2) continue;
                    var key = entry.Children[0];
                    if (_diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error &&
                        d.Span.Start >= key.Span.Start && d.Span.End <= key.Span.End)) continue;
                    var canonical = Canonical(key, new HashSet<SemanticNode>(ReferenceEqualityComparer.Instance), out var reason);
                    if (canonical is null)
                        _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "yaml.key-equality-unsupported", $"Cannot verify this mapping key's uniqueness: {reason}.", key.Span));
                    else if (!keys.Add(canonical))
                        _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "yaml.duplicate-key", "Duplicate YAML mapping key after canonicalization.", key.Span));
                }
            }
            foreach (var child in node.Children) CheckMappingKeys(child);
        }

        /// <summary>
        /// Constructs an injective representation of an acyclic YAML node: sequence order is
        /// retained, mapping pairs are sorted, and aliases resolve to their original anchors.
        /// Cycles are implementation-defined by YAML and deliberately produce no guessed key.
        /// </summary>
        private string? Canonical(SemanticNode node, HashSet<SemanticNode> visiting, out string reason)
        {
            reason = "";
            if (visiting.Count > 256) { reason = "key nesting exceeds comparison limit"; return null; }
            if (!visiting.Add(node)) { reason = "cyclic alias graph"; return null; }
            try
            {
                if (node.Kind == "alias")
                {
                    if (!int.TryParse(node.Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var offset) ||
                        !_anchorNodes.TryGetValue(offset, out var target))
                    { reason = "unbound alias"; return null; }
                    return Canonical(target, visiting, out reason);
                }
                if (node.Kind == "scalar")
                {
                    if (!_scalarKeys.TryGetValue(node, out var scalar) || !scalar.Supported)
                    { reason = "unknown scalar tag canonicalization"; return null; }
                    return Pack("S", scalar.Tag, scalar.Value);
                }
                if (node.Kind == "sequence")
                {
                    var values = new List<string> { "Q", NormalizeTag(node.Name, "seq") };
                    foreach (var item in node.Children)
                    {
                        if (item.Children.Count != 1) { reason = "invalid sequence projection"; return null; }
                        var value = Canonical(item.Children[0], visiting, out reason);
                        if (value is null) return null;
                        values.Add(value);
                    }
                    return Pack(values.ToArray());
                }
                if (node.Kind == "mapping")
                {
                    var pairs = new List<string>();
                    foreach (var entry in node.Children)
                    {
                        if (entry.Children.Count != 2) { reason = "invalid mapping projection"; return null; }
                        var key = Canonical(entry.Children[0], visiting, out reason);
                        if (key is null) return null;
                        var value = Canonical(entry.Children[1], visiting, out reason);
                        if (value is null) return null;
                        pairs.Add(Pack(key, value));
                    }
                    pairs.Sort(StringComparer.Ordinal);
                    pairs.Insert(0, NormalizeTag(node.Name, "map"));
                    pairs.Insert(0, "M");
                    return Pack(pairs.ToArray());
                }
                reason = "unknown YAML node kind";
                return null;
            }
            finally { visiting.Remove(node); }
        }

        /// <summary>Length prefixes prevent delimiter collisions in nested canonical strings.</summary>
        private static string Pack(params string[] values)
        {
            var result = new StringBuilder();
            foreach (var value in values) result.Append(value.Length).Append(':').Append(value);
            return result.ToString();
        }

        private static string NormalizeTag(string? tag, string fallback)
        {
            if (string.IsNullOrEmpty(tag)) tag = "!!" + fallback;
            return tag.StartsWith("!!", StringComparison.Ordinal) ? "tag:yaml.org,2002:" + tag[2..] : tag;
        }

        private static bool IsCoreScalarTag(string tag) => tag is
            "tag:yaml.org,2002:null" or "tag:yaml.org,2002:bool" or
            "tag:yaml.org,2002:int" or "tag:yaml.org,2002:float" or "tag:yaml.org,2002:str";

        /// <summary>
        /// Resolves the YAML 1.2.2 Core Schema directly. SharpYaml's resolver accepts extra
        /// numeric forms (for example underscores and signed hex), so it cannot define editor
        /// key equality when the document claims strict YAML 1.2 semantics.
        /// </summary>
        private static (string Tag, string Value, bool Supported) ScalarIdentity(Scalar scalar)
        {
            var inferred = ResolveCoreTag(scalar.Value, scalar.Style);
            var tag = NormalizeTag(scalar.Tag, "str");
            if (string.IsNullOrEmpty(scalar.Tag)) tag = inferred;
            if (tag == "tag:yaml.org,2002:str") return (tag, scalar.Value, true);
            if (tag is not ("tag:yaml.org,2002:null" or "tag:yaml.org,2002:bool" or
                "tag:yaml.org,2002:int" or "tag:yaml.org,2002:float")) return (tag, scalar.Value, false);
            if (tag.EndsWith(":null", StringComparison.Ordinal))
                return (tag, "null", scalar.Value is "" or "null" or "Null" or "NULL" or "~");
            if (tag.EndsWith(":bool", StringComparison.Ordinal))
                return (tag, scalar.Value.ToLowerInvariant(), scalar.Value is "true" or "True" or "TRUE" or "false" or "False" or "FALSE");
            if (tag.EndsWith(":int", StringComparison.Ordinal) && TryCanonicalInteger(scalar.Value, out var integer)) return (tag, integer, true);
            if (tag.EndsWith(":float", StringComparison.Ordinal) && TryCanonicalFloat(scalar.Value, out var number)) return (tag, number, true);
            return (tag, scalar.Value, false);
        }

        /// <summary>Implements §10.3.2 regex precedence: null, boolean, integer, float, string.</summary>
        private static string ResolveCoreTag(string value, ScalarStyle style)
        {
            const string prefix = "tag:yaml.org,2002:";
            if (style != ScalarStyle.Plain) return prefix + "str";
            if (value is "" or "null" or "Null" or "NULL" or "~") return prefix + "null";
            if (value is "true" or "True" or "TRUE" or "false" or "False" or "FALSE") return prefix + "bool";
            if (TryCanonicalInteger(value, out _)) return prefix + "int";
            if (TryCanonicalFloat(value, out _)) return prefix + "float";
            return prefix + "str";
        }

        /// <summary>Canonicalizes arbitrary-precision Core Schema integers across bases.</summary>
        private static bool TryCanonicalInteger(string text, out string canonical)
        {
            canonical = "";
            var index = 0;
            var negative = false;
            if (text.StartsWith('+') || text.StartsWith('-')) { negative = text[0] == '-'; index++; }
            var radix = 10;
            if (index + 1 < text.Length && text[index] == '0')
            {
                radix = text[index + 1] switch { 'x' => 16, 'o' => 8, _ => 10 };
                if (radix != 10)
                {
                    if (index != 0) return false;
                    index += 2;
                }
            }
            var value = BigInteger.Zero;
            var digits = 0;
            for (; index < text.Length; index++)
            {
                var ch = text[index];
                var digit = ch is >= '0' and <= '9' ? ch - '0' : ch is >= 'a' and <= 'f' ? ch - 'a' + 10 : ch is >= 'A' and <= 'F' ? ch - 'A' + 10 : -1;
                if (digit < 0 || digit >= radix) return false;
                value = value * radix + digit;
                digits++;
            }
            if (digits == 0) return false;
            canonical = (negative ? -value : value).ToString(CultureInfo.InvariantCulture);
            return true;
        }

        /// <summary>Canonicalizes finite floats as exact decimal coefficient and exponent.</summary>
        private static bool TryCanonicalFloat(string text, out string canonical)
        {
            canonical = "";
            if (text is ".nan" or ".NaN" or ".NAN") { canonical = "nan"; return true; }
            if (text is ".inf" or ".Inf" or ".INF" or "+.inf" or "+.Inf" or "+.INF") { canonical = "+inf"; return true; }
            if (text is "-.inf" or "-.Inf" or "-.INF") { canonical = "-inf"; return true; }
            var lower = text.ToLowerInvariant();
            var negative = lower.StartsWith('-');
            var unsigned = lower.StartsWith('+') || negative ? lower[1..] : lower;
            var exponentAt = unsigned.IndexOf('e');
            var mantissa = exponentAt < 0 ? unsigned : unsigned[..exponentAt];
            var exponentText = exponentAt < 0 ? "0" : unsigned[(exponentAt + 1)..];
            if (!BigInteger.TryParse(exponentText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var exponent)) return false;
            var dot = mantissa.IndexOf('.');
            if (dot >= 0 && mantissa.IndexOf('.', dot + 1) >= 0) return false;
            var fractionalDigits = dot < 0 ? 0 : mantissa.Length - dot - 1;
            var digits = mantissa.Replace(".", "", StringComparison.Ordinal);
            if (digits.Length == 0 || digits.Any(ch => ch is < '0' or > '9')) return false;
            if (dot == 0 && fractionalDigits == 0) return false;
            digits = digits.TrimStart('0');
            if (digits.Length == 0) { canonical = "0"; return true; }
            exponent -= fractionalDigits;
            var trimmed = digits.TrimEnd('0');
            exponent += digits.Length - trimmed.Length;
            canonical = (negative ? "-" : "") + trimmed + "e" + exponent.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        private static string ScalarKind(string tag)
        {
            if (tag.EndsWith(":null", StringComparison.Ordinal)) return "null";
            if (tag.EndsWith(":bool", StringComparison.Ordinal)) return "boolean";
            if (tag.EndsWith(":int", StringComparison.Ordinal) || tag.EndsWith(":float", StringComparison.Ordinal)) return "number";
            return "string";
        }
    }
}
