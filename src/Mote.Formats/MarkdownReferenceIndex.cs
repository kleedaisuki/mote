using Markdig;
using Markdig.Syntax;
using Mote.Engine;
using Markdig.Syntax.Inlines;
using System.Text;

namespace Mote.Formats;

/// <summary>Private immutable, budgeted source-owner certificate for large cross-block reference documents.</summary>
internal sealed class MarkdownReferenceIndex
{
    /// <summary>One ordered source owner, including real nonwinning declarations; no text or per-owner key strings.</summary>
    internal readonly record struct Row(int Start, int Length, int Template, int Geometry, int DefinitionKey = -1, long Identity = 0);
    /// <summary>Shared syntax/count facts; Text is one bounded cache key, never projected display text.</summary>
    internal sealed record Template(string Text, MarkdownReferenceCertificate? Certificate, string Kind, int ConstantWarnings);
    /// <summary>Only winning decoded payloads are retained; raw source is a declaration row span.</summary>
    internal sealed record Winner(int Row, long Identity, string Url, bool Unsafe);
    /// <summary>Private frozen state; consumers use the exact snapshot object, never just an equal numeric version.</summary>
    internal sealed record State(TextSnapshot Snapshot, Row[] Rows, Template[] Templates, int[][] Geometry,
        string[] Keys, Dictionary<string, int> KeyIds, Dictionary<int, Winner> Winners, int Warnings);
    /// <summary>Failure categories have different retry semantics; budget exhaustion is never a bad-source-line certificate.</summary>
    internal enum AdmissionFailureKind { None, UnsupportedGrammar, Budget, Canceled, MissingEditChain, StaleVersion }
    /// <summary>Publication-aware result, separating intrinsic grammar refusal from resource exhaustion.</summary>
    internal sealed record Attempt(bool Complete, long RequestedVersion, long? PublishedVersion, string? Failure, AdmissionFailureKind FailureKind,
        long RetainedCharge, long Work, int ParserCalls, long ParserUnits, long Allocated, int Rows, int Templates, int Geometry);
    /// <summary>All source-owner categories share these budgets after ordered merge.</summary>
    internal sealed record OutputLimits(int Nodes = 512, int Tokens = 512, int Diagnostics = 128,
        int ValueUnits = 131072, int ParsedSourceUnits = 262144);
    /// <summary>Only a successfully certified snapshot may be projected as complete.</summary>
    internal State? Current { get; private set; }
    /// <summary>Resource evidence is not a cacheable intrinsic bad-line conclusion.</summary>
    internal Attempt? LastAttempt { get; private set; }
    /// <summary>Production parser projection supplies payloads and warning classification; raw URL guesses are not used.</summary>
    private readonly MarkdownPolicy _policy = new();
    /// <summary>Limits are private format policy, not user-visible completeness configuration.</summary>
    private readonly MarkdownReferenceLimits _limits;
    /// <summary>Constructs the private builder with optional deterministic test limits.</summary>
    internal MarkdownReferenceIndex(MarkdownReferenceLimits? limits = null) => _limits = limits ?? new();

    /// <summary>Builds privately; a failed/canceled stage never changes Current, including over-budget edits.</summary>
    internal Attempt Build(TextSnapshot snapshot, CancellationToken ct = default, State? previous = null, VersionedEdit? edit = null, Action<string>? hook = null, bool allowRebuild = true)
    {
        var ledger = new MarkdownReferenceBudget(_limits);
        Stage? stage = null;
        void Check(string phase) { hook?.Invoke(phase); ct.ThrowIfCancellationRequested(); ledger.Poll(); }
        try
        {
            Check("begin"); ledger.Keep(16384);
            stage = new Stage(snapshot, ledger, Check, _policy, ct, previous);
            if (previous is not null && edit is { } transition && CanTransition(previous, snapshot, transition, out var changed))
            {
                if (!stage.Transition(previous, changed, transition.Change)) return Finish(false, "unsupported-grammar");
            }
            else
            {
                if (!allowRebuild) return Finish(false, "missing-or-unsupported-edit-chain");
                if (!Scan(snapshot, stage.Line, ledger, () => Check("scan-chunk"))) return Finish(false, "unsupported-grammar");
            }
            if (stage.InFence) return Finish(false, "unclosed-fence");
            if (!stage.HasReferences) return Finish(false, "no-reference-consumers");
            Check("resolve-winners"); stage.Resolve();
            Check("before-commit");
            var state = stage.Publish(); Check("publish-arrays");
            var result = new Attempt(true, snapshot.Version, snapshot.Version, null, AdmissionFailureKind.None, ledger.Retained, ledger.Work,
                ledger.Calls, ledger.ParsedUnits, ledger.Allocated, stage.Rows.Count, stage.Templates.Count, stage.Geometries.Count);
            ledger.Poll();
            // Hooks may reenter with a newer snapshot. No hook or external call is allowed between this gate and publication.
            if (Current is { } old && old.Snapshot.Version > snapshot.Version) return Finish(false, "stale-version");
            Current = state;
            return LastAttempt = result;
        }
        catch (MarkdownReferenceBudget.Refused failure) { return Finish(false, failure.Message); }
        catch (OperationCanceledException) { Finish(false, "canceled"); throw; }

        Attempt Finish(bool complete, string? failure)
        {
            return LastAttempt = new Attempt(complete, snapshot.Version, Current?.Snapshot.Version, failure, failure switch
                {
                    null => AdmissionFailureKind.None,
                    "canceled" => AdmissionFailureKind.Canceled,
                    "stale-version" => AdmissionFailureKind.StaleVersion,
                    "missing-or-unsupported-edit-chain" => AdmissionFailureKind.MissingEditChain,
                    "unsupported-grammar" or "unclosed-fence" or "no-reference-consumers" => AdmissionFailureKind.UnsupportedGrammar,
                    _ => AdmissionFailureKind.Budget
                },
                ledger.Retained, ledger.Work, ledger.Calls, ledger.ParsedUnits, ledger.Allocated,
                stage?.Rows.Count ?? 0, stage?.Templates.Count ?? 0, stage?.Geometries.Count ?? 0);
        }
    }

    /// <summary>Projects one ordered owner prefix under shared source, node, token, diagnostic and value allowances.</summary>
    internal DocumentAnalysis Project(TextSnapshot snapshot, AnalysisRequest request, CancellationToken ct = default)
    {
        var state = Current;
        if (state is null || !ReferenceEquals(snapshot, state.Snapshot))
            throw new InvalidOperationException("Only the exact certified snapshot may be projected.");
        var nodes = new List<SemanticNode>(); var tokens = new List<SemanticToken>(); var diagnostics = new List<Diagnostic>();
        var nodesUsed = 0; var valuesUsed = 0; var parsedUnits = 0; var cap = new OutputLimits();
        foreach (var slot in VisibleSlots(state, request.VisibleRange))
        {
            ct.ThrowIfCancellationRequested(); var row = state.Rows[slot];
            if (!TryParseRow(state, row, cap.ParsedSourceUnits - parsedUnits, out var ast, out var text)) break;
            parsedUnits += text.Length;
            var block = RealBlock(ast, row);
            var localTokens = new List<SemanticToken>(); var localDiagnostics = new List<Diagnostic>();
            var node = MarkdownPolicy.Project(block, text, localTokens, localDiagnostics, ct);
            var size = Size(node);
            if (size.Nodes > cap.Nodes - nodesUsed || size.ValueUnits > cap.ValueUnits - valuesUsed ||
                localTokens.Count > cap.Tokens - tokens.Count || localDiagnostics.Count > cap.Diagnostics - diagnostics.Count) break;
            nodes.Add(Shift(node, row.Start)); nodesUsed += size.Nodes; valuesUsed += size.ValueUnits;
            tokens.AddRange(localTokens.Select(t => t with { Span = new TextSpan(t.Span.Start + row.Start, t.Span.Length) }));
            diagnostics.AddRange(localDiagnostics.Select(d => d with { Span = new TextSpan(d.Span.Start + row.Start, d.Span.Length) }));
        }
        ct.ThrowIfCancellationRequested();
        return new DocumentAnalysis(snapshot.Version, new TextSpan(0, snapshot.Length), AnalysisCompleteness.Complete,
            new SemanticNode("document", new TextSpan(0, snapshot.Length), children: nodes), diagnostics, tokens, state.Warnings);
    }

    /// <summary>Renders real parser syntax with actual winners; skeleton text never reaches native display or navigation.</summary>
    internal FlowRenderProjection Render(TextSnapshot snapshot, DocumentAnalysis analysis, AnalysisRequest request, CancellationToken ct)
    {
        var state = Current;
        if (state is null || !ReferenceEquals(state.Snapshot, snapshot))
            throw new InvalidOperationException("Rendering requires the exact certified snapshot.");
        var builder = new MarkdownRenderProjection(snapshot, analysis, ct);
        var remaining = new OutputLimits().ParsedSourceUnits; var omitted = false;
        foreach (var slot in VisibleSlots(state, request.VisibleRange))
        {
            ct.ThrowIfCancellationRequested(); var row = state.Rows[slot];
            if (row.DefinitionKey >= 0) continue;
            if (builder.Full || !TryParseRow(state, row, remaining, out var ast, out var text)) { omitted = true; break; }
            remaining -= text.Length;
            builder.Add(RealBlock(ast, row), row.Start);
        }
        ct.ThrowIfCancellationRequested(); return builder.Finish(omitted);
    }

    /// <summary>Lookup work depends on the requested owner prefix, not on all document rows.</summary>
    private static IEnumerable<int> VisibleSlots(State state, TextSpan visible)
    {
        if (visible.Length == 0) yield break;
        for (var slot = First(state.Rows, visible.Start); slot < state.Rows.Length && state.Rows[slot].Start < visible.End; slot++)
            yield return slot;
    }

    /// <summary>Constructs bounded real-owner context using original source declarations, never synthesized decoded URLs.</summary>
    private static bool TryParseRow(State state, Row row, int remaining, out MarkdownDocument ast, out string text)
    {
        ast = null!; text = string.Empty;
        var certificate = row.Template >= 0 ? state.Templates[row.Template].Certificate : null;
        var units = row.Length;
        if (certificate is not null)
            foreach (var key in certificate.Keys)
                if (state.Winners.TryGetValue(state.KeyIds[key], out var winner)) units += 2 + state.Rows[winner.Row].Length;
        if (units > remaining) return false;
        var context = new StringBuilder(units); context.Append(state.Snapshot.GetText(row.Start, row.Length));
        if (certificate is not null)
            foreach (var key in certificate.Keys)
                if (state.Winners.TryGetValue(state.KeyIds[key], out var winner))
                {
                    var definition = state.Rows[winner.Row];
                    context.Append("\n\n").Append(state.Snapshot.GetText(definition.Start, definition.Length));
                }
        text = context.ToString(); ast = Markdown.Parse(text, MarkdownPolicy.Pipeline);
        if (certificate is not null)
        {
            var leaf = RealBlock(ast, row) as LeafBlock;
            var links = MarkdownReferenceCertificate.AllLinks(leaf?.Inline).ToArray();
            var offsets = state.Geometry[row.Geometry]; var linkSlot = 0;
            for (var atomSlot = 0; atomSlot < certificate.Atoms.Length; atomSlot++)
            {
                var atom = certificate.Atoms[atomSlot];
                if (!state.Winners.TryGetValue(state.KeyIds[atom.TargetKey], out var winner)) continue;
                if (linkSlot >= links.Length) throw new InvalidOperationException("A certified reference was not resolved.");
                var link = links[linkSlot++];
                if (link.IsImage || link.IsShortcut || link.Label is null || MarkdownReferenceCertificate.Key(link.Label) != atom.TargetKey ||
                    link.Url != winner.Url || link.Span.Start != offsets[atomSlot] || link.Span.Length != atom.Length)
                    throw new InvalidOperationException("Reference payload or source provenance violated its certificate.");
            }
            if (linkSlot != links.Length) throw new InvalidOperationException("Unexpected resolved reference in a certified owner.");
        }
        return true;
    }

    /// <summary>Definition-group wrappers are normalized to their one real source owner; synthetic suffixes are never projected.</summary>
    private static Markdig.Syntax.Block RealBlock(MarkdownDocument ast, Row row)
    {
        if (row.DefinitionKey >= 0 && ast.Count == 1 && ast[0] is LinkReferenceDefinitionGroup { Count: 1 } group)
            return group[0];
        var block = ast.FirstOrDefault(b => b is not LinkReferenceDefinitionGroup && b.Span.Start == 0);
        if (block is null || block.Span.Length != row.Length)
            throw new InvalidOperationException("Actual Markdown owner violated its boundary certificate.");
        return block;
    }

    /// <summary>Private builder never mutates committed objects; hash collisions require exact cache-key equality.</summary>
    private sealed class Stage(TextSnapshot snapshot, MarkdownReferenceBudget ledger, Action<string> check, MarkdownPolicy policy, CancellationToken ct, State? previous)
    {
        /// <summary>Physical source order is the common projection and duplicate-winner order.</summary>
        internal readonly List<Row> Rows = [];
        /// <summary>Each entry is one immutable interpretation of an exact category-scoped cache key.</summary>
        internal readonly List<Template> Templates = [];
        /// <summary>Original atom offsets are shared only after complete exact equality.</summary>
        internal readonly List<int[]> Geometries = [];
        /// <summary>Category separation prevents a synthetic fence key from colliding with real prose.</summary>
        private readonly Dictionary<(int Kind, ulong Hash), List<int>> _templates = [];
        /// <summary>Hashing narrows candidates but never substitutes for source-geometry equality.</summary>
        private readonly Dictionary<(int, ulong), List<int>> _geometry = [];
        /// <summary>Canonical labels belong to the closed ASCII normalization domain.</summary>
        private readonly Dictionary<string, int> _keys = new(StringComparer.Ordinal);
        /// <summary>Dense key IDs permit payload lookup without repeated dictionary scans.</summary>
        private readonly List<string> _keyNames = [];
        /// <summary>First physical declaration wins; every duplicate remains in Rows.</summary>
        private readonly Dictionary<int, int> _firstDeclarations = [];
        /// <summary>Decoded semantic payloads are retained only for actual effective winners.</summary>
        private readonly Dictionary<int, Winner> _winners = [];
        /// <summary>Only its active prefix is meaningful; no caller may retain a scratch span.</summary>
        private readonly char[] _scratch = new char[4096];
        /// <summary>Requires a real blank separator between independent owners.</summary>
        private bool _previous;
        /// <summary>An open exact fence makes all interior definition-looking text opaque.</summary>
        private int _fenceStart = -1;
        /// <summary>Private aggregate count becomes externally usable only at successful publication.</summary>
        private int _warnings;
        /// <summary>Changed owners receive a fresh identity independent of source displacement.</summary>
        private long _nextIdentity = (previous?.Rows.Select(r => r.Identity).DefaultIfEmpty(0).Max() ?? 0) + 1;
        /// <summary>An unclosed fence refuses the entire document certificate.</summary>
        internal bool InFence => _fenceStart >= 0;
        /// <summary>At least one real explicit-reference atom is required to select this additional semantic path.</summary>
        internal bool HasReferences => Rows.Any(r => r.Template >= 0 && Templates[r.Template].Certificate is { Atoms.Length: > 0 });

        /// <summary>Re-admits one bounded owner, preserves exact unchanged facts, and lazily shifts their source coordinates.</summary>
        internal bool Transition(State old, int changed, TextChange change)
        {
            foreach (var key in old.Keys) { check("reuse-key"); Key(key); }
            foreach (var template in old.Templates)
            {
                check("reuse-template");
                ledger.Keep(128 + 2L * template.Text.Length);
                if (template.Certificate is { } certificate)
                    ledger.Keep(1024 + certificate.Atoms.Length * 96L + certificate.Keys.Sum(k => 128L + 4L * k.Length));
                AddTemplate(template);
            }
            foreach (var geometry in old.Geometry)
            {
                check("reuse-geometry");
                ledger.Keep(256 + 4L * geometry.Length);
                Geometries.Add(geometry);
            }
            // Rebuild geometry lookup using the template from each prior source owner.
            foreach (var row in old.Rows)
                if (row.Geometry >= 0) { check("reuse-geometry-index"); RegisterGeometry(row.Template, row.Geometry); }
            var delta = change.InsertText.Length - change.DeleteLength;
            for (var i = 0; i < old.Rows.Length; i++)
            {
                check("reuse-owner"); var row = old.Rows[i];
                if (i == changed)
                {
                    var length = row.Length + delta;
                    if (length != 0 && !Line(row.Start, snapshot.GetText(row.Start, length))) return false;
                    _previous = false;
                    continue;
                }
                var shifted = row with { Start = row.Start + (i > changed ? delta : 0) };
                AddRow(shifted);
                if (shifted.DefinitionKey >= 0) _firstDeclarations.TryAdd(shifted.DefinitionKey, Rows.Count - 1);
            }
            return true;
        }

        /// <summary>Exact immutable geometry arrays can be shared; mutable lookup buckets belong to the new stage.</summary>
        private void RegisterGeometry(int template, int id)
        {
            var offsets = Geometries[id]; ledger.Visit(16L * offsets.Length);
            var hash = 14695981039346656037UL;
            foreach (var offset in offsets) hash = unchecked((hash ^ (uint)offset) * 1099511628211UL);
            if (!_geometry.TryGetValue((template, hash), out var bucket))
            { ledger.Keep(256); _geometry[(template, hash)] = bucket = []; }
            if (!bucket.Contains(id)) { ledger.Keep(16); bucket.Add(id); }
        }

        /// <summary>Consumes an ephemeral physical line; raw definitions and owner source never escape the snapshot.</summary>
        internal bool Line(int start, ReadOnlySpan<char> body)
        {
            check("scan-line");
            if (InFence) return FenceLine(start, body);
            if (body.Length == 0) { _previous = false; return true; }
            if (_previous) return false;
            _previous = true;
            if (body.SequenceEqual("```json")) { _fenceStart = start; return true; }
            if (body[0] == '[') return Declaration(start, body);
            Span<int> original = stackalloc int[32];
            var description = MarkdownReferenceSkeleton.Describe(body, _scratch, original, ledger);
            if (description is { } described)
            {
                var active = _scratch.AsSpan(0, described.Length);
                var template = Find(active, 1);
                if (template < 0)
                {
                    ledger.Keep(128 + 2L * active.Length); var text = active.ToString(); ledger.Poll();
                    var parses = 0; var certificate = MarkdownReferenceCertificate.Admit(text, check, ref parses, ledger: ledger);
                    if (certificate is null) return false;
                    ledger.Keep(1024 + certificate.Atoms.Length * 96L + certificate.Keys.Sum(k => 128L + 4L * k.Length));
                    certificate = Canonical(certificate);
                    template = AddTemplate(new Template(text, certificate, text.StartsWith('#') ? "heading" : "paragraph", 0));
                }
                var geometry = Geometry(template, original[..described.Atoms]);
                AddRow(new Row(start, body.Length, template, geometry));
                return true;
            }
            // A paragraph/heading with no '[' has no document-reference reads; actual local parse checks its block kind/count.
            if (body.Contains('[')) return false;
            return Independent(start, body);
        }

        /// <summary>Preserves all declarations by span, including unused unsafe duplicates; only first winners retain decoded payloads.</summary>
        private bool Declaration(int start, ReadOnlySpan<char> body)
        {
            var close = body.IndexOf(']');
            if (close < 2 || close + 3 >= body.Length || !body.Slice(close + 1, 2).SequenceEqual(": ")) return false;
            var label = body.Slice(1, close - 1); var destination = body[(close + 3)..];
            if (!MarkdownReferenceCertificate.IsLabel(label.ToString()) || destination.Length > 2048) return false;
            foreach (var c in destination) if (c is < '!' or > '~' or '"' or '\'' or '<' or '>' or '\\') return false;
            ledger.Visit(body.Length); var raw = body.ToString(); ledger.Poll();
            var ast = ledger.Parse(raw.Length, () => Markdown.Parse(raw, MarkdownReferenceCertificate.Pipeline));
            if (ast.Count != 1 || ast[0] is not LinkReferenceDefinitionGroup { Count: 1 } group || group[0] is not LinkReferenceDefinition reference || reference.Title is not null || reference.CreateLinkInline is not null) return false;
            var key = MarkdownReferenceCertificate.Key(label.ToString());
            if (reference.Label is null || MarkdownReferenceCertificate.Key(reference.Label) != key) return false;
            var id = Key(key); var slot = Rows.Count;
            AddRow(new Row(start, body.Length, -1, -1, id)); _firstDeclarations.TryAdd(id, slot);
            ledger.Poll(); return true;
        }

        /// <summary>Independent rich blocks remain actual-parser checked; no unsupported container or hidden definition is admitted.</summary>
        private bool Independent(int start, ReadOnlySpan<char> body)
        {
            var template = Find(body, 0);
            if (template < 0)
            {
                ledger.Keep(256 + body.Length * 2L); var text = body.ToString(); ledger.Poll();
                var ast = ledger.Parse(text.Length, () => Markdown.Parse(text, MarkdownReferenceCertificate.Pipeline));
                if (ast.Count != 1 || ast[0] is not (ParagraphBlock or HeadingBlock) ||
                    ast[0].Span.Start != 0 || ast[0].Span.Length != text.Length || ast.GetLinkReferenceDefinitions(false) is not null) return false;
                var diagnostics = new List<Diagnostic>(); var tokens = new List<SemanticToken>();
                var node = MarkdownPolicy.Project(ast[0], text, tokens, diagnostics, ct);
                ledger.Poll();
                template = AddTemplate(new Template(text, null, node.Kind, diagnostics.Count));
            }
            AddRow(new Row(start, body.Length, template, -1)); return true;
        }

        /// <summary>Bounded exact json fences are opaque to reference definitions; parser validates the real closing boundary.</summary>
        private bool FenceLine(int start, ReadOnlySpan<char> body)
        {
            if (start + body.Length - _fenceStart > 65536) return false;
            if (!body.SequenceEqual("```"))
            {
                var trimmed = body.TrimStart(' ');
                if (trimmed.StartsWith("```")) return false;
                return true;
            }
            var length = start + body.Length - _fenceStart;
            var text = snapshot.GetText(_fenceStart, length); ledger.Poll();
            var ast = ledger.Parse(text.Length, () => Markdown.Parse(text, MarkdownReferenceCertificate.Pipeline));
            if (ast.Count != 1 || ast[0] is not FencedCodeBlock || ast[0].Span.Start != 0 || ast[0].Span.Length != length) return false;
            var template = Find("opaque-fence-json", 2);
            if (template < 0) { ledger.Keep(256); template = AddTemplate(new Template("opaque-fence-json", null, "fenced-code", 0)); }
            AddRow(new Row(_fenceStart, length, template, -1)); _fenceStart = -1;
            return true;
        }

        /// <summary>Resolves exact first-source winners only after all real declarations have passed admission.</summary>
        internal void Resolve()
        {
            foreach (var pair in _firstDeclarations)
            {
                check("winner-payload"); var row = Rows[pair.Value]; var raw = snapshot.GetText(row.Start, row.Length);
                if (previous is not null && previous.Winners.TryGetValue(pair.Key, out var unchanged) && unchanged.Identity == row.Identity)
                {
                    ledger.Keep(256 + 2L * unchanged.Url.Length);
                    _winners.Add(pair.Key, unchanged with { Row = pair.Value });
                    continue;
                }
                var source = $"Alpha [payload][{_keyNames[pair.Key]}] Omega\n\n{raw}"; ledger.Poll();
                var analysis = ledger.Parse(source.Length, () => policy.Analyze(source, ct));
                var link = analysis.Root.Children.Single(n => n.Kind == "paragraph").Children.Single(n => n.Kind == "link");
                ledger.Keep(256 + 2L * (link.Value?.Length ?? 0)); _winners.Add(pair.Key, new Winner(pair.Value, row.Identity, link.Value!, analysis.Diagnostics.Count != 0));
            }
            foreach (var row in Rows)
            {
                check("aggregate-owner"); ledger.Visit(32);
                if (row.Template < 0) continue;
                var template = Templates[row.Template]; _warnings = checked(_warnings + template.ConstantWarnings);
                if (template.Certificate is not { } certificate) continue;
                foreach (var pair in certificate.Multiplicities)
                    if (_winners.TryGetValue(Key(pair.Key), out var winner) && winner.Unsafe) _warnings = checked(_warnings + pair.Value);
            }
        }

        /// <summary>Allocations made for frozen arrays pass the same observed-allocation publication gate.</summary>
        internal State Publish() => new(snapshot, Rows.ToArray(), Templates.ToArray(), Geometries.ToArray(),
            _keyNames.ToArray(), _keys, _winners, _warnings);

        /// <summary>Interns semantic label values under the same retained and key-count policy.</summary>
        private int Key(string key)
        {
            if (_keys.TryGetValue(key, out var id)) return id;
            if (_keys.Count >= 4096) throw new MarkdownReferenceBudget.Refused("key-count");
            ledger.Keep(256 + key.Length * 2L); id = _keys.Count; _keys.Add(key, id); _keyNames.Add(key); return id;
        }
        /// <summary>Shares equal normalized key strings without changing any source span or multiplicity.</summary>
        private MarkdownReferenceCertificate Canonical(MarkdownReferenceCertificate certificate)
        {
            var keys = certificate.Keys.Select(k => _keyNames[Key(k)]).ToArray();
            string Name(string key) => keys[Array.IndexOf(keys, key)];
            var atoms = certificate.Atoms.Select(a => new MarkdownReferenceCertificate.Atom(a.Start, a.Length, Name(a.TextKey), Name(a.TargetKey))).ToArray();
            var multiplicities = certificate.Multiplicities.ToDictionary(p => Name(p.Key), p => p.Value, StringComparer.Ordinal);
            ledger.Poll(); return new MarkdownReferenceCertificate(keys, atoms, multiplicities);
        }
        /// <summary>Looks up an exact active scratch/source key; stale scratch tails never participate.</summary>
        private int Find(ReadOnlySpan<char> text, int kind)
        {
            ledger.Visit(text.Length); var hash = MarkdownReferenceSkeleton.Hash(text);
            if (!_templates.TryGetValue((kind, hash), out var bucket)) return -1;
            foreach (var id in bucket)
            {
                ledger.Visit(text.Length);
                if (text.SequenceEqual(Templates[id].Text)) return id;
            }
            return -1;
        }
        /// <summary>Installs only an already admitted template and accounts for its cache containers.</summary>
        private int AddTemplate(Template template)
        {
            ledger.Keep(256); ledger.Visit(template.Text.Length);
            var hash = MarkdownReferenceSkeleton.Hash(template.Text); var id = Templates.Count; Templates.Add(template);
            var kind = template.Certificate is not null ? 1 : template.Kind == "fenced-code" ? 2 : 0;
            if (!_templates.TryGetValue((kind, hash), out var bucket)) _templates[(kind, hash)] = bucket = [];
            bucket.Add(id);
            if (template.Certificate is { } certificate) foreach (var key in certificate.Keys) Key(key);
            ledger.Poll(); return id;
        }
        /// <summary>Interns a complete original-offset sequence in its template's interpretation.</summary>
        private int Geometry(int template, ReadOnlySpan<int> offsets)
        {
            ledger.Visit(offsets.Length * 16L); var hash = 14695981039346656037UL;
            foreach (var offset in offsets) hash = unchecked((hash ^ (uint)offset) * 1099511628211UL);
            if (_geometry.TryGetValue((template, hash), out var bucket))
                foreach (var id in bucket)
                {
                    ledger.Visit(offsets.Length);
                    if (offsets.SequenceEqual(Geometries[id])) return id;
                }
            ledger.Keep(256 + 4L * offsets.Length); var result = Geometries.Count; Geometries.Add(offsets.ToArray());
            if (bucket is null) _geometry[(template, hash)] = bucket = [];
            bucket.Add(result); ledger.Poll(); return result;
        }
        /// <summary>Charges before container growth; no raw owner or declaration source is retained.</summary>
        private void AddRow(Row row)
        {
            ledger.Keep(96); ledger.Visit(32); Rows.Add(row with { Identity = row.Identity == 0 ? _nextIdentity++ : row.Identity }); ledger.Poll();
        }
    }

    /// <summary>Only an exact one-owner contiguous chain with untouched separators can reuse the old boundary certificate.</summary>
    private static bool CanTransition(State old, TextSnapshot snapshot, VersionedEdit edit, out int rowIndex)
    {
        rowIndex = -1; var change = edit.Change;
        if (edit.BeforeVersion != old.Snapshot.Version || edit.AfterVersion != snapshot.Version ||
            change.InsertText is null || change.Start < 0 || change.DeleteLength < 0 ||
            change.Start > old.Snapshot.Length || change.DeleteLength > old.Snapshot.Length - change.Start ||
            (long)old.Snapshot.Length - change.DeleteLength + change.InsertText.Length != snapshot.Length ||
            change.InsertText.AsSpan().ContainsAny('\r', '\n')) return false;
        var slot = First(old.Rows, change.Start);
        // At a row's end, insertion still belongs to that owner rather than a blank separator.
        if (slot > 0 && old.Rows[slot - 1].Start + old.Rows[slot - 1].Length == change.Start) slot--;
        if (slot >= old.Rows.Length) return false;
        var row = old.Rows[slot];
        if (change.Start < row.Start || (long)change.Start + change.DeleteLength > row.Start + row.Length ||
            row.Template >= 0 && old.Templates[row.Template].Kind == "fenced-code") return false;
        var length = row.Length + change.InsertText.Length - change.DeleteLength;
        if (length > 4096) return false;
        var original = old.Snapshot.GetText(row.Start, row.Length);
        var expected = original.Remove(change.Start - row.Start, change.DeleteLength).Insert(change.Start - row.Start, change.InsertText);
        if (snapshot.GetText(row.Start, length) != expected) return false;
        rowIndex = slot; return true;
    }

    /// <summary>Streaming physical lines have fixed scratch, CRLF seam handling and bounded cancellation polling.</summary>
    private delegate bool LineVisitor(int start, ReadOnlySpan<char> line);
    /// <summary>One fixed line buffer scans rope chunks without per-line strings, preserving CRLF seams.</summary>
    private static bool Scan(TextSnapshot snapshot, LineVisitor visitor, MarkdownReferenceBudget ledger, Action check)
    {
        var buffer = new char[4096]; var used = 0; var offset = 0; var lineStart = 0; var cr = false;
        foreach (var chunk in snapshot.GetChunks())
        {
            check(); ledger.Visit(chunk.Length);
            foreach (var c in chunk.Span)
            {
                if (cr && c == '\n') { cr = false; lineStart = ++offset; continue; }
                cr = false;
                if (c is '\r' or '\n')
                {
                    if (!visitor(lineStart, buffer.AsSpan(0, used))) return false;
                    used = 0; lineStart = offset + 1; cr = c == '\r';
                }
                else
                {
                    if (used == buffer.Length) return false;
                    buffer[used++] = c;
                }
                offset++;
            }
        }
        return visitor(lineStart, buffer.AsSpan(0, used));
    }
    /// <summary>Finds the first real disjoint owner intersecting a nonempty source viewport.</summary>
    private static int First(Row[] rows, int start)
    {
        var low = 0; var high = rows.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (rows[middle].Start + rows[middle].Length <= start) low = middle + 1;
            else high = middle;
        }
        return low;
    }
    /// <summary>Converts actual local-parser coordinates to the matching immutable snapshot.</summary>
    private static SemanticNode Shift(SemanticNode node, int offset) => new(node.Kind, new TextSpan(node.Span.Start + offset, node.Span.Length), node.Name, node.Value, node.Children.Select(c => Shift(c, offset)).ToArray());
    /// <summary>All descendant nodes and displayed values consume the same owner-atomic output allowance.</summary>
    private static (int Nodes, int ValueUnits) Size(SemanticNode node)
    {
        var nodes = 1; var values = node.Value?.Length ?? 0;
        foreach (var child in node.Children) { var size = Size(child); nodes += size.Nodes; values += size.ValueUnits; }
        return (nodes, values);
    }
}
