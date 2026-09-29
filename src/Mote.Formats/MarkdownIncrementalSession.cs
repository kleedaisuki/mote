using Markdig;
using Markdig.Syntax;
using Mote.Engine;

namespace Mote.Formats;

/// <summary>
/// A per-document Markdown analyzer with conservative, source-spanned block reuse.
/// Small documents use Markdig's complete AST. Large files can be certified
/// complete only when every bounded block belongs to an independent flat
/// heading/paragraph grammar; otherwise the visible result is Provisional.
/// Lists, fences, references and malformed intermediates never inherit stale
/// semantics from an unrelated block.
/// </summary>
internal sealed class MarkdownIncrementalSession : IFormatSession
{
    private const int VisibleContext = 4096;
    private const int MaxVisibleParse = 512 * 1024;
    private const int MaxFlatProjectionLength = 256 * 1024;
    private const int MaxFlatProjectionBlocks = 2048;
    private const int AlwaysExactLimit = 4 * 1024 * 1024;
    private const int SparseExactLimit = 16 * 1024 * 1024;
    private const int MaxExactLines = 100_000;
    private const int MaxExactLineLength = 64 * 1024;
    private const int MaxExactMarkers = 100_000;
    private const int MaxFlatBlocks = 100_000;
    private List<Run> _runs = [];
    private List<FlatRun> _flatRuns = [];
    private long? _version;
    private int _length;
    private bool _complete;
    private bool _flatComplete;
    private TextSpan? _uncertifiedLine;
    private bool _disposed;

    /// <inheritdoc />
    public DocumentAnalysis Analyze(TextSnapshot snapshot, IReadOnlyList<VersionedEdit> changesSinceCommittedState,
        AnalysisRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(changesSinceCommittedState);
        Validate(snapshot, request);
        cancellationToken.ThrowIfCancellationRequested();

        if (_flatComplete && _version == snapshot.Version && changesSinceCommittedState.Count == 0)
            return ProjectFlat(snapshot, _flatRuns, request, cancellationToken);
        if (_flatComplete && changesSinceCommittedState.Count == 1 &&
            TryFlatIncremental(snapshot, changesSinceCommittedState[0], cancellationToken, out var editedFlat))
        {
            var projection = ProjectFlat(snapshot, editedFlat, request, cancellationToken);
            CommitFlat(snapshot, editedFlat);
            return projection;
        }

        List<Run> next;
        if (_complete && _version == snapshot.Version && changesSinceCommittedState.Count == 0)
            next = _runs;
        else if (_complete && changesSinceCommittedState.Count == 1 &&
            TryIncremental(snapshot, changesSinceCommittedState[0], cancellationToken, out next))
        {
            // A local edit cannot affect reference definitions: the old and new
            // block are delimiter-free, isolated, and keep the same block kind.
        }
        else if (!CanExactParse(snapshot, cancellationToken))
        {
            var skipCertification = TryMapUncertifiedLine(snapshot, changesSinceCommittedState, out var badLine);
            if (!skipCertification &&
                TryCertifyFlat(snapshot, cancellationToken, out var certifiedFlat, out badLine))
            {
                var projection = ProjectFlat(snapshot, certifiedFlat, request, cancellationToken);
                CommitFlat(snapshot, certifiedFlat);
                return projection;
            }
            // Markdig builds a whole-document AST from one string and cannot be
            // canceled inside Parse. Even an explicit Full request must stay
            // bounded for dense or enormous inputs: references can resolve
            // across any block, so local parsing is Provisional, not Complete.
            var partial = AnalyzeVisible(snapshot, request.VisibleRange, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _runs = [];
            _flatRuns = [];
            _version = snapshot.Version;
            _length = snapshot.Length;
            _complete = false;
            _flatComplete = false;
            _uncertifiedLine = badLine;
            return partial;
        }
        else
            next = ParseAll(snapshot, cancellationToken);

        var result = Project(snapshot.Version, snapshot.Length, next, request, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _runs = next;
        _flatRuns = [];
        _version = snapshot.Version;
        _length = snapshot.Length;
        _complete = true;
        _flatComplete = false;
        _uncertifiedLine = null;
        return result;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _runs = [];
        _flatRuns = [];
        _version = null;
        _complete = false;
        _flatComplete = false;
        _uncertifiedLine = null;
        _disposed = true;
    }

    /// <summary>
    /// Certifies a deliberately small, dependency-free CommonMark subset from
    /// bounded physical-line reads. Exact blank separators make each heading or
    /// paragraph an independent Markdig block; no references or inline markup
    /// are admitted. One failure rejects the whole certification.
    /// </summary>
    private static bool TryCertifyFlat(TextSnapshot snapshot, CancellationToken ct,
        out List<FlatRun> runs, out TextSpan? badLine)
    {
        runs = [];
        badLine = null;
        if (snapshot.LineCount > MaxFlatBlocks * 3 + 1) return false;
        var blocks = new List<FlatBlock>();
        var separated = true;
        for (var line = 0; line < snapshot.LineCount; line++)
        {
            ct.ThrowIfCancellationRequested();
            var start = snapshot.GetLineStartOffset(line);
            var end = line + 1 < snapshot.LineCount ? snapshot.GetLineStartOffset(line + 1) : snapshot.Length;
            var rawLength = end - start;
            if (rawLength > MaxExactLineLength + 2)
            {
                badLine = new TextSpan(start, rawLength);
                return false;
            }
            var raw = snapshot.GetText(start, rawLength);
            var bodyLength = rawLength;
            if (line + 1 < snapshot.LineCount)
            {
                if (raw.EndsWith("\r\n", StringComparison.Ordinal)) bodyLength -= 2;
                else if (raw.EndsWith('\n')) bodyLength--;
                else { badLine = new TextSpan(start, rawLength); return false; }
            }
            if (bodyLength == 0) { separated = true; continue; }
            if (!separated)
            {
                // Missing separation depends on both physical lines. Editing
                // the earlier line can create a blank even if this line stays
                // byte-for-byte unchanged.
                var previous = blocks[^1].Start;
                badLine = new TextSpan(previous, end - previous);
                return false;
            }
            // A count-budget failure is not an intrinsic defect of this line:
            // deleting earlier blocks may make the same line admissible.
            if (blocks.Count == MaxFlatBlocks) return false;
            if (!IsVerifiedFlatBlock(raw[..bodyLength], out var level))
            {
                badLine = new TextSpan(start, rawLength);
                return false;
            }
            blocks.Add(new FlatBlock(start, bodyLength, level));
            separated = false;
        }
        ct.ThrowIfCancellationRequested();
        if (blocks.Count > 0)
            runs.Add(new FlatRun(blocks.ToArray(), 0, blocks.Count, 0));
        return true;
    }

    /// <summary>
    /// A single known invalid line remains invalid after an unrelated one-edit
    /// chain. Mapping its location avoids rescanning a huge unsupported file on
    /// every keystroke; edits touching it retry certification.
    /// </summary>
    private bool TryMapUncertifiedLine(TextSnapshot snapshot, IReadOnlyList<VersionedEdit> edits,
        out TextSpan? mapped)
    {
        mapped = null;
        if (_uncertifiedLine is not { } bad || _version is null) return false;
        if (edits.Count == 0 && snapshot.Version == _version)
        {
            mapped = bad;
            return true;
        }
        if (edits.Count != 1 || edits[0].BeforeVersion != _version ||
            edits[0].AfterVersion != snapshot.Version) return false;
        var change = edits[0].Change;
        if (change.InsertText is null || change.Start < 0 || change.DeleteLength < 0 ||
            change.Start > _length || change.DeleteLength > _length - change.Start ||
            (long)_length - change.DeleteLength + change.InsertText.Length != snapshot.Length)
            return false;
        if ((long)change.Start + change.DeleteLength < bad.Start)
        {
            mapped = ShiftSpan(bad, change.InsertText.Length - change.DeleteLength);
            return true;
        }
        if (change.Start > bad.End)
        {
            mapped = bad;
            return true;
        }
        return false;
    }

    /// <summary>Checks one single-line block without accepting Markdown-active syntax.</summary>
    private static bool IsFlatBlock(ReadOnlySpan<char> text, out int headingLevel)
    {
        headingLevel = 0;
        if (text.IsEmpty || text.Length > MaxExactLineLength) return false;
        var body = text;
        if (body[0] == '#')
        {
            while (headingLevel < body.Length && body[headingLevel] == '#') headingLevel++;
            if (headingLevel is < 1 or > 6 || headingLevel >= body.Length || body[headingLevel] != ' ')
                return false;
            body = body[(headingLevel + 1)..];
        }
        // A leading decimal digit can begin a CommonMark ordered list (`1. x`
        // or `1) x`). Reject every digit-leading line rather than duplicate
        // Markdig's marker and Unicode rules in this restricted grammar.
        if (body.IsEmpty || !char.IsLetter(body[0]) || body[^1] == ' ') return false;
        foreach (var c in body)
            if (!char.IsLetterOrDigit(c) && c != ' ' && c is not ('.' or ',' or '!' or '?' or ';' or ':' or '\'' or '"' or '(' or ')'))
                return false;
        return true;
    }

    /// <summary>Uses Markdig to check every certified block, even outside the viewport.</summary>
    private static bool IsVerifiedFlatBlock(string source, out int level)
    {
        if (!IsFlatBlock(source, out level)) return false;
        var parsed = Markdown.Parse(source, MarkdownPolicy.Pipeline);
        return parsed.Count == 1 && parsed[0].Span.Start == 0 &&
            parsed[0].Span.End + 1 == source.Length &&
            (level == 0 ? parsed[0] is ParagraphBlock :
                parsed[0] is HeadingBlock heading && heading.Level == level);
    }

    private bool TryFlatIncremental(TextSnapshot snapshot, VersionedEdit edit, CancellationToken ct,
        out List<FlatRun> next)
    {
        next = [];
        var change = edit.Change;
        if (_version != edit.BeforeVersion || snapshot.Version != edit.AfterVersion ||
            change.InsertText is null || change.Start < 0 || change.DeleteLength < 0 ||
            change.Start > _length || change.DeleteLength > _length - change.Start ||
            (long)_length - change.DeleteLength + change.InsertText.Length != snapshot.Length)
            return false;
        var found = FindFlatBlock(change.Start, change.DeleteLength);
        if (found is null) return false;
        var (runIndex, blockIndex) = found.Value;
        var run = _flatRuns[runIndex];
        var block = run.Blocks[blockIndex];
        var start = block.Start + run.Shift;
        var newLength = (long)block.Length - change.DeleteLength + change.InsertText.Length;
        if (newLength is < 1 or > MaxExactLineLength) return false;
        var source = snapshot.GetText(start, (int)newLength);
        if (!IsVerifiedFlatBlock(source, out var level)) return false;
        var delta = change.InsertText.Length - change.DeleteLength;
        var updated = new FlatBlock(0, (int)newLength, level);
        for (var i = 0; i < _flatRuns.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var current = _flatRuns[i];
            if (i != runIndex)
            {
                next.Add(i < runIndex ? current : current with { Shift = current.Shift + delta });
                continue;
            }
            if (blockIndex > current.First)
                next.Add(current with { Count = blockIndex - current.First });
            next.Add(new FlatRun([updated], 0, 1, start));
            var suffixFirst = blockIndex + 1;
            var suffixCount = current.First + current.Count - suffixFirst;
            if (suffixCount > 0)
                next.Add(current with { First = suffixFirst, Count = suffixCount, Shift = current.Shift + delta });
        }
        return true;
    }

    private (int Run, int Block)? FindFlatBlock(int start, int deleteLength)
    {
        for (var r = 0; r < _flatRuns.Count; r++)
        {
            var run = _flatRuns[r];
            var last = run.Blocks[run.First + run.Count - 1];
            if (start > last.Start + last.Length + run.Shift) continue;
            if (start < run.Blocks[run.First].Start + run.Shift) return null;
            var lo = run.First;
            var hi = run.First + run.Count;
            while (lo < hi)
            {
                var mid = lo + (hi - lo) / 2;
                var block = run.Blocks[mid];
                if (block.Start + block.Length + run.Shift < start) lo = mid + 1;
                else hi = mid;
            }
            if (lo == run.First + run.Count) return null;
            var candidate = run.Blocks[lo];
            return start >= candidate.Start + run.Shift &&
                (long)start + deleteLength <= candidate.Start + candidate.Length + run.Shift
                ? (r, lo) : null;
        }
        return null;
    }

    private static DocumentAnalysis ProjectFlat(TextSnapshot snapshot, List<FlatRun> runs,
        AnalysisRequest request, CancellationToken ct)
    {
        // Completeness is a whole-document certification; materializing every
        // block node would defeat its bounded-memory purpose. The public
        // contract permits a viewport-only projection even for Full requests.
        var children = new List<SemanticNode>();
        var tokens = new List<SemanticToken>();
        var diagnostics = new List<Diagnostic>();
        var visible = request.VisibleRange;
        // Include a whole block that intersects the bounded window: clipping
        // its node would corrupt the IR. MaxExactLineLength bounds spillover
        // beyond the 256 Ki start window to at most another 64 Ki.
        var projectionEnd = (int)Math.Min(visible.End,
            Math.Min(snapshot.Length, (long)visible.Start + MaxFlatProjectionLength));
        var reachedEnd = false;
        foreach (var run in runs)
        {
            ct.ThrowIfCancellationRequested();
            for (var i = run.First; i < run.First + run.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (children.Count == MaxFlatProjectionBlocks) break;
                var block = run.Blocks[i];
                var start = block.Start + run.Shift;
                if (start >= projectionEnd)
                {
                    reachedEnd = true;
                    break;
                }
                if (start + block.Length < visible.Start) continue;
                var source = snapshot.GetText(start, block.Length);
                var parsed = Markdown.Parse(source, MarkdownPolicy.Pipeline);
                if (parsed.Count != 1)
                    throw new InvalidOperationException("Certified Markdown block failed local projection.");
                var localTokens = new List<SemanticToken>();
                var localDiagnostics = new List<Diagnostic>();
                var node = MarkdownPolicy.Project(parsed[0], source, localTokens, localDiagnostics, ct);
                if (node.Kind != (block.HeadingLevel == 0 ? "paragraph" : "heading") ||
                    node.Span != new TextSpan(0, block.Length) || localDiagnostics.Count != 0)
                    throw new InvalidOperationException("Certified Markdown block violated its grammar invariant.");
                children.Add(ShiftNode(node, start));
                foreach (var token in localTokens)
                    tokens.Add(token with { Span = ShiftSpan(token.Span, start) });
            }
            if (children.Count == MaxFlatProjectionBlocks || reachedEnd) break;
        }
        ct.ThrowIfCancellationRequested();
        return new DocumentAnalysis(snapshot.Version, new TextSpan(0, snapshot.Length),
            AnalysisCompleteness.Complete,
            new SemanticNode("document", new TextSpan(0, snapshot.Length), children: children),
            diagnostics, tokens, 0);
    }

    private void CommitFlat(TextSnapshot snapshot, List<FlatRun> runs)
    {
        _runs = [];
        _flatRuns = runs;
        _version = snapshot.Version;
        _length = snapshot.Length;
        _complete = false;
        _flatComplete = true;
        _uncertifiedLine = null;
    }

    private bool TryIncremental(TextSnapshot snapshot, VersionedEdit edit, CancellationToken ct, out List<Run> next)
    {
        next = [];
        var change = edit.Change;
        if (_version != edit.BeforeVersion || snapshot.Version != edit.AfterVersion ||
            change.InsertText is null || change.Start < 0 || change.DeleteLength < 0 ||
            change.Start > _length || change.DeleteLength > _length - change.Start ||
            (long)_length - change.DeleteLength + change.InsertText.Length != snapshot.Length)
            return false;
        if (_runs.Count == 0) return false;

        var found = FindBlock(change.Start, change.DeleteLength);
        if (found is null) return false;
        var (runIndex, blockIndex) = found.Value;
        var run = _runs[runIndex];
        var block = run.Blocks[blockIndex];
        var oldStart = block.Start + run.Shift;
        var oldEnd = block.End + run.Shift;
        if (block.SimpleText is null || change.Start < oldStart ||
            (long)change.Start + change.DeleteLength > oldEnd) return false;
        if ((long)block.SimpleText.Length - change.DeleteLength + change.InsertText.Length > MaxExactLineLength)
            return false;
        var local = change.Start - oldStart;
        var replacement = block.SimpleText.Remove(local, change.DeleteLength).Insert(local, change.InsertText);
        if (!IsSimple(block.Kind, replacement)) return false;
        var newLength = replacement.Length;
        if (oldStart + newLength > snapshot.Length ||
            snapshot.GetText(oldStart, newLength) != replacement) return false;

        // Reparse exactly one independently delimited block with the same pipeline.
        // No reference syntax is permitted in this fast path, including in edits.
        var parsed = Markdown.Parse(replacement, MarkdownPolicy.Pipeline);
        if (parsed.Count != 1) return false;
        var diagnostics = new List<Diagnostic>();
        var tokens = new List<SemanticToken>();
        var node = MarkdownPolicy.Project(parsed[0], replacement, tokens, diagnostics, ct);
        if (node.Kind != block.Kind || node.Span.Start != 0 || node.Span.End != newLength)
            return false;
        var updated = new Block(0, newLength, node.Kind, node, diagnostics.ToArray(), tokens.ToArray(), replacement);
        var delta = change.InsertText.Length - change.DeleteLength;
        for (var i = 0; i < _runs.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var current = _runs[i];
            if (i != runIndex)
            {
                next.Add(i < runIndex ? current : current with { Shift = current.Shift + delta });
                continue;
            }
            if (blockIndex > current.First)
                next.Add(current with { Count = blockIndex - current.First });
            next.Add(new Run([updated], 0, 1, oldStart));
            var suffixFirst = blockIndex + 1;
            var suffixCount = current.First + current.Count - suffixFirst;
            if (suffixCount > 0)
                next.Add(current with { First = suffixFirst, Count = suffixCount, Shift = current.Shift + delta });
        }
        return true;
    }

    private (int Run, int Block)? FindBlock(int start, int deleteLength)
    {
        for (var r = 0; r < _runs.Count; r++)
        {
            var run = _runs[r];
            if (start > run.Blocks[run.First + run.Count - 1].End + run.Shift) continue;
            if (start < run.Blocks[run.First].Start + run.Shift) return null;
            var lo = run.First;
            var hi = run.First + run.Count;
            while (lo < hi)
            {
                var mid = lo + (hi - lo) / 2;
                if (run.Blocks[mid].End + run.Shift < start) lo = mid + 1;
                else hi = mid;
            }
            if (lo == run.First + run.Count) return null;
            var block = run.Blocks[lo];
            return start >= block.Start + run.Shift &&
                (long)start + deleteLength <= block.End + run.Shift ? (r, lo) : null;
        }
        return null;
    }

    private static List<Run> ParseAll(TextSnapshot snapshot, CancellationToken ct)
    {
        // Markdig's reference resolution and block parsing require a whole document.
        // The resulting source string is discarded immediately; the session retains
        // only compact semantic blocks and their bounded simple-text fast-path keys.
        var text = snapshot.GetText();
        var document = Markdown.Parse(text, MarkdownPolicy.Pipeline);
        var blocks = new Block[document.Count];
        for (var i = 0; i < document.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var diagnostics = new List<Diagnostic>();
            var tokens = new List<SemanticToken>();
            var node = MarkdownPolicy.Project(document[i], text, tokens, diagnostics, ct);
            var start = node.Span.Start;
            var length = node.Span.Length;
            var source = length <= MaxExactLineLength ? text.Substring(start, length) : null;
            var simple = source is not null && IsSimple(node.Kind, source) && IsIsolated(text, start, start + length)
                ? source : null;
            blocks[i] = new Block(start, start + length, node.Kind, ShiftNode(node, -start),
                diagnostics.Select(d => d with { Span = ShiftSpan(d.Span, -start) }).ToArray(),
                tokens.Select(t => t with { Span = ShiftSpan(t.Span, -start) }).ToArray(), simple);
        }
        return blocks.Length == 0 ? [] : [new Run(blocks, 0, blocks.Length, 0)];
    }

    private static bool IsSimple(string kind, string text)
    {
        if (kind == "heading")
        {
            var i = 0;
            while (i < text.Length && text[i] == '#') i++;
            if (i is < 1 or > 6 || i >= text.Length || text[i] != ' ') return false;
            text = text[(i + 1)..];
        }
        else if (kind != "paragraph") return false;
        if (text.Length == 0 || text.IndexOfAny(['\r', '\n', '`', '\\']) >= 0) return false;
        // Markdig's recovery for unmatched inline delimiters can assign spans
        // outside the enclosing block. Reparse those through the full oracle.
        if ((text.Count(c => c == '*') & 1) != 0 || (text.Count(c => c == '_') & 1) != 0)
            return false;
        // An inline link is self-contained. A reference use or definition can
        // change other blocks, including links before a definition, and is not.
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '[') continue;
            var closeLabel = text.IndexOf(']', i + 1);
            if (closeLabel < 0 || closeLabel + 1 >= text.Length || text[closeLabel + 1] != '(')
                return false;
            var closeUrl = text.IndexOf(')', closeLabel + 2);
            if (closeUrl < 0 || text.AsSpan(i + 1, closeLabel - i - 1).IndexOfAny(['[', ']']) >= 0 ||
                text.AsSpan(closeLabel + 2, closeUrl - closeLabel - 2).IndexOfAny(['(', '[', ']']) >= 0)
                return false;
            i = closeUrl;
        }
        return true;
    }

    /// <summary>
    /// Bounds the uninterruptible Markdig parse by both source size and a cheap
    /// structural-density proxy. This is an admission heuristic, not a Markdown
    /// validator: rejected documents receive honest bounded semantics.
    /// </summary>
    private static bool CanExactParse(TextSnapshot snapshot, CancellationToken ct)
    {
        if (snapshot.Length <= AlwaysExactLimit) return true;
        if (snapshot.Length > SparseExactLimit || snapshot.LineCount > MaxExactLines) return false;

        var lineLength = 0;
        var markers = 0;
        foreach (var chunk in snapshot.GetChunks())
        {
            ct.ThrowIfCancellationRequested();
            var span = chunk.Span;
            for (var i = 0; i < span.Length; i++)
            {
                var c = span[i];
                if (c is '\r' or '\n') lineLength = 0;
                else if (++lineLength > MaxExactLineLength) return false;
                if (c is '*' or '_' or '[' or ']' or '<' or '>' or '`' or '~' &&
                    ++markers > MaxExactMarkers) return false;
            }
        }
        return true;
    }

    private static bool IsIsolated(string text, int start, int end)
    {
        var before = start == 0 || start >= 2 && text[start - 1] == '\n' && text[start - 2] == '\n';
        var after = end == text.Length || end + 1 < text.Length && text[end] == '\n' && text[end + 1] == '\n';
        return before && after;
    }

    private static DocumentAnalysis Project(long version, int length, List<Run> runs,
        AnalysisRequest request, CancellationToken ct)
    {
        var children = new List<SemanticNode>();
        var diagnostics = new List<Diagnostic>();
        var tokens = new List<SemanticToken>();
        var total = 0;
        var visible = request.VisibleRange;
        var all = request.Scope == AnalysisScope.Full;
        foreach (var run in runs)
        {
            ct.ThrowIfCancellationRequested();
            for (var i = run.First; i < run.First + run.Count; i++)
            {
                var block = run.Blocks[i];
                total += block.Diagnostics.Length;
                if (!all && (block.End + run.Shift < visible.Start ||
                    block.Start + run.Shift > visible.End)) continue;
                children.Add(ShiftNode(block.Node, block.Start + run.Shift));
                foreach (var diagnostic in block.Diagnostics)
                    diagnostics.Add(diagnostic with { Span = ShiftSpan(diagnostic.Span, block.Start + run.Shift) });
                foreach (var token in block.Tokens)
                    tokens.Add(token with { Span = ShiftSpan(token.Span, block.Start + run.Shift) });
            }
        }
        return new DocumentAnalysis(version, new TextSpan(0, length),
            AnalysisCompleteness.Complete,
            new SemanticNode("document", new TextSpan(0, length), children: children),
            diagnostics, tokens, total);
    }

    private static DocumentAnalysis AnalyzeVisible(TextSnapshot snapshot, TextSpan visible, CancellationToken ct)
    {
        var start = Math.Max(0, visible.Start - VisibleContext);
        var end = Math.Min(snapshot.Length, visible.End + VisibleContext);
        var first = snapshot.GetLineIndexFromOffset(start);
        var last = snapshot.GetLineIndexFromOffset(end);
        start = snapshot.GetLineStartOffset(first);
        end = last + 1 < snapshot.LineCount ? snapshot.GetLineStartOffset(last + 1) : snapshot.Length;
        // One enormous physical line must not turn a viewport request into a
        // whole-file materialization. A clipped line is explicitly provisional.
        if (end - start > MaxVisibleParse)
        {
            start = Math.Max(start, visible.Start - VisibleContext);
            end = Math.Min(end, start + MaxVisibleParse);
        }
        var source = snapshot.GetText(start, end - start);
        var document = Markdown.Parse(source, MarkdownPolicy.Pipeline);
        var children = new List<SemanticNode>();
        var diagnostics = new List<Diagnostic>();
        var tokens = new List<SemanticToken>();
        foreach (var block in document)
            children.Add(ShiftNode(MarkdownPolicy.Project(block, source, tokens, diagnostics, ct), start));
        ct.ThrowIfCancellationRequested();
        return new DocumentAnalysis(snapshot.Version, new TextSpan(start, end - start),
            AnalysisCompleteness.Provisional,
            new SemanticNode("document", new TextSpan(0, snapshot.Length), children: children),
            diagnostics.Select(d => d with { Span = ShiftSpan(d.Span, start) }).ToArray(),
            tokens.Select(t => t with { Span = ShiftSpan(t.Span, start) }).ToArray(), null);
    }

    private static SemanticNode ShiftNode(SemanticNode node, int delta) =>
        new(node.Kind, ShiftSpan(node.Span, delta), node.Name, node.Value,
            node.Children.Select(child => ShiftNode(child, delta)).ToArray());

    private static TextSpan ShiftSpan(TextSpan span, int delta) => new(span.Start + delta, span.Length);

    private static void Validate(TextSnapshot snapshot, AnalysisRequest request)
    {
        var visible = request.VisibleRange;
        if (visible.Start < 0 || visible.Length < 0 || visible.Start > snapshot.Length ||
            visible.Length > snapshot.Length - visible.Start)
            throw new ArgumentOutOfRangeException(nameof(request));
        if (request.Scope is not (AnalysisScope.Visible or AnalysisScope.Full))
            throw new ArgumentOutOfRangeException(nameof(request));
    }

    /// <summary>A source-spanned top-level block stored in base coordinates.</summary>
    private sealed record Block(int Start, int End, string Kind, SemanticNode Node,
        Diagnostic[] Diagnostics, SemanticToken[] Tokens, string? SimpleText);

    /// <summary>A slice of immutable blocks sharing one lazy UTF-16 displacement.</summary>
    private sealed record Run(Block[] Blocks, int First, int Count, int Shift);

    /// <summary>A certified independent block; no full paragraph text is retained.</summary>
    private readonly record struct FlatBlock(int Start, int Length, int HeadingLevel);

    /// <summary>Compact certified block offsets sharing one lazy edit displacement.</summary>
    private sealed record FlatRun(FlatBlock[] Blocks, int First, int Count, int Shift);
}
