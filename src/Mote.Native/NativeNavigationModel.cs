using Mote.Engine;

namespace Mote.Native;

/// <summary>A control-relative UTF-16 selection obtained by clipping a global selection to one source page.</summary>
internal readonly record struct NativeProjectedSelection(int Anchor, int Active);

/// <summary>
/// Holds canonical document coordinates independently of the bounded native text control.
/// Anchor and Active are UTF-16 boundaries in the engine snapshot, not display coordinates.
/// The active endpoint is the caret; reversing a selection preserves its direction.
/// </summary>
internal sealed class NativeNavigationModel
{
    /// <summary>The fixed endpoint of the global selection.</summary>
    public int Anchor { get; private set; }

    /// <summary>The movable endpoint, or caret, of the global selection.</summary>
    public int Active { get; private set; }

    /// <summary>The first selected source boundary, inclusive.</summary>
    public int SelectionStart => Math.Min(Anchor, Active);

    /// <summary>The number of selected UTF-16 code units.</summary>
    public int SelectionLength => Math.Abs(Active - Anchor);

    /// <summary>Places the caret at an engine offset and collapses the selection.</summary>
    public void MoveCaret(TextSnapshot snapshot, int offset) => SetSelection(snapshot, offset, offset);

    /// <summary>Sets the global selection, retaining the direction chosen by the user.</summary>
    public void SetSelection(TextSnapshot snapshot, int anchor, int active)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if ((uint)anchor > (uint)snapshot.Length) throw new ArgumentOutOfRangeException(nameof(anchor));
        if ((uint)active > (uint)snapshot.Length) throw new ArgumentOutOfRangeException(nameof(active));
        Anchor = anchor;
        Active = active;
    }

    /// <summary>Selects the complete source file, including portions outside the native viewport.</summary>
    public void SelectAll(TextSnapshot snapshot) => SetSelection(snapshot, 0, snapshot.Length);

    /// <summary>Clamps saved coordinates after replacing or shrinking the document.</summary>
    public void Clamp(TextSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Anchor = Math.Min(Anchor, snapshot.Length);
        Active = Math.Min(Active, snapshot.Length);
    }

    /// <summary>
    /// Transforms both endpoints through an engine replacement. An endpoint at or inside
    /// the replaced range follows the inserted text; later endpoints shift by the length delta.
    /// </summary>
    public void ApplyChange(TextChange change, TextSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(after);
        Anchor = Transform(Anchor, change);
        Active = Transform(Active, change);
        Clamp(after);
    }

    /// <summary>Moves the caret to a one-based logical line using the rope's indexed line lookup.</summary>
    public void GoToLine(TextSnapshot snapshot, int lineNumber)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (lineNumber < 1 || lineNumber > snapshot.LineCount)
            throw new ArgumentOutOfRangeException(nameof(lineNumber));
        MoveCaret(snapshot, snapshot.GetLineStartOffset(lineNumber - 1));
    }

    /// <summary>
    /// Finds the next ordinal, case-sensitive occurrence after the selection's end.
    /// The search traverses immutable rope chunks without materializing the document;
    /// when requested, one additional pass wraps to matches starting before the origin.
    /// </summary>
    /// <returns>True when a match was selected; false leaves the selection unchanged.</returns>
    public bool FindNext(TextSnapshot snapshot, string needle, bool wrap = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(needle);
        if (needle.Length == 0) return false;
        Clamp(snapshot);
        var origin = Math.Min(snapshot.Length, Math.Max(Anchor, Active));
        var prefix = PrefixTable(needle);
        var found = Find(snapshot, needle, prefix, origin, snapshot.Length, cancellationToken);
        if (found < 0 && wrap && origin > 0)
            found = Find(snapshot, needle, prefix, 0, origin, cancellationToken);
        if (found < 0) return false;
        SetSelection(snapshot, found, found + needle.Length);
        return true;
    }

    /// <summary>
    /// Clips the global selection to a bounded source page, then maps it through the
    /// platform line-ending projection. Returns null when even the caret is off-page.
    /// </summary>
    public NativeProjectedSelection? Project(int pageStart, NativeTextProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentOutOfRangeException.ThrowIfNegative(pageStart);
        var pageEnd = checked(pageStart + projection.Source.Length);
        if (SelectionLength == 0)
        {
            if (Active < pageStart || Active > pageEnd) return null;
        }
        else if (SelectionStart >= pageEnd || SelectionStart + SelectionLength <= pageStart)
            return null;

        var anchor = Math.Clamp(Anchor - pageStart, 0, projection.Source.Length);
        var active = Math.Clamp(Active - pageStart, 0, projection.Source.Length);
        return new NativeProjectedSelection(projection.ToDisplay(anchor), projection.ToDisplay(active));
    }

    /// <summary>
    /// Streams the globally selected source range into a writer. This preserves original
    /// newline spelling and bounds temporary memory even when the selection spans pages.
    /// </summary>
    public void WriteSelection(TextSnapshot snapshot, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(writer);
        Clamp(snapshot);
        var start = SelectionStart;
        var end = start + SelectionLength;
        var offset = 0;
        foreach (var chunk in snapshot.GetChunks())
        {
            var chunkEnd = offset + chunk.Length;
            if (chunkEnd > start && offset < end)
            {
                var from = Math.Max(start, offset) - offset;
                var to = Math.Min(end, chunkEnd) - offset;
                writer.Write(chunk.Span[from..to]);
            }
            offset = chunkEnd;
            if (offset >= end) break;
        }
    }

    private static int Transform(int offset, TextChange change)
    {
        if (offset < change.Start) return offset;
        var oldEnd = checked(change.Start + change.DeleteLength);
        if (offset <= oldEnd) return checked(change.Start + change.InsertText.Length);
        return checked(offset + change.InsertText.Length - change.DeleteLength);
    }

    private static int[] PrefixTable(string needle)
    {
        var prefix = new int[needle.Length];
        for (int i = 1, matched = 0; i < needle.Length; i++)
        {
            while (matched > 0 && needle[i] != needle[matched]) matched = prefix[matched - 1];
            if (needle[i] == needle[matched]) matched++;
            prefix[i] = matched;
        }
        return prefix;
    }

    private static int Find(TextSnapshot snapshot, string needle, int[] prefix,
        int minStart, int maxStart, CancellationToken cancellationToken)
    {
        var offset = 0;
        var matched = 0;
        foreach (var chunk in snapshot.GetChunks())
        {
            if (offset + chunk.Length <= minStart)
            {
                offset += chunk.Length;
                continue;
            }
            var first = Math.Max(0, minStart - offset);
            offset += first;
            for (var i = first; i < chunk.Length; i++)
            {
                var current = chunk.Span[i];
                if ((offset & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                while (matched > 0 && current != needle[matched]) matched = prefix[matched - 1];
                if (current == needle[matched]) matched++;
                offset++;
                if (matched != needle.Length) continue;
                var start = offset - matched;
                if (start >= minStart && start < maxStart) return start;
                matched = prefix[matched - 1];
            }
            // No later character can complete a match whose start precedes maxStart.
            if ((long)offset >= (long)maxStart + needle.Length - 1) break;
        }
        return -1;
    }
}
