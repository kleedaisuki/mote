using Markdig;
using Markdig.Syntax;
using Mote.Engine;

namespace Mote.Formats;

/// <summary>
/// A per-document Markdown parser with conservative, source-spanned block reuse.
/// Only an isolated, single-line paragraph or ATX heading without reference
/// dependencies may be reparsed locally. Other edits rebuild with Markdig, so lists, fences and
/// malformed intermediates never inherit stale semantics.
/// </summary>
internal sealed class MarkdownIncrementalSession : IFormatSession
{
    private const int VisibleContext = 4096;
    private const int MaxVisibleParse = 512 * 1024;
    private const int LargeDocument = 4 * 1024 * 1024;
    private List<Run> _runs = [];
    private long? _version;
    private int _length;
    private bool _complete;
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

        List<Run> next;
        if (_complete && _version == snapshot.Version && changesSinceCommittedState.Count == 0)
            next = _runs;
        else if (_complete && changesSinceCommittedState.Count == 1 &&
            TryIncremental(snapshot, changesSinceCommittedState[0], cancellationToken, out next))
        {
            // A local edit cannot affect reference definitions: the old and new
            // block are delimiter-free, isolated, and keep the same block kind.
        }
        else if (snapshot.Length > LargeDocument && request.Scope == AnalysisScope.Visible)
        {
            var partial = AnalyzeVisible(snapshot, request.VisibleRange, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _runs = [];
            _version = snapshot.Version;
            _length = snapshot.Length;
            _complete = false;
            return partial;
        }
        else
            next = ParseAll(snapshot, cancellationToken);

        var result = Project(snapshot.Version, snapshot.Length, next, request, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _runs = next;
        _version = snapshot.Version;
        _length = snapshot.Length;
        _complete = true;
        return result;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _runs = [];
        _version = null;
        _complete = false;
        _disposed = true;
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
            var source = length <= 4096 ? text.Substring(start, length) : null;
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
}
