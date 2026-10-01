using System.Globalization;
using System.Text;
using Mote.Native.Accessibility;

namespace Mote.Native.Windows.Accessibility;

/// <summary>
/// Transaction-local UIA navigation over canonical source coordinates. Logical
/// lines are canvas lines (there is no soft wrap); character units are .NET
/// extended grapheme clusters, including one CRLF cluster. No document mirror
/// or persistent per-character index is retained.
/// </summary>
/// <remarks>
/// Character navigation materializes at most 65,536 UTF-16 units per operation.
/// A long line uses certified local context of at most 4,096 units; a pathological
/// unbounded cluster or excessive walk fails before endpoints change.
/// Line/document navigation uses the rope line index regardless of file size.
/// Unsupported Format/Word units promote to Line, Paragraph/Page to Document,
/// following UIA's next-supported-unit rule. Input source coordinates remain
/// UTF-16, not grapheme ordinals.
/// </remarks>
internal sealed class WindowsTextRangeNavigation
{
    // Tiny-line walks need a separate structural budget: a text-byte budget
    // alone permits tens of thousands of rope queries on the owning UI thread.
    private const int MaxCharacterLines = 256;
    private readonly WindowsTextProviderCore _core;
    private readonly AccessibleRange _identity;
    private readonly AccessibleRange _document;
    private readonly Dictionary<int, CharacterSegment> _characters = new();
    private int _textBudget = AccessibleDocument.MaxTextRequest;
    private int _steps;

    /// <summary>Captures a checked identity for one atomic range operation.</summary>
    internal WindowsTextRangeNavigation(WindowsTextProviderCore core, AccessibleRange range)
    {
        _core = core;
        _identity = range;
        core.ValidateRange(range);
        _document = core.DocumentRange;
        EnsureIdentity(_document);
    }

    /// <summary>Promotes unsupported units without pretending to implement word/format layout.</summary>
    internal static int SupportedUnit(int unit) => unit switch
    {
        0 => 0,
        1 or 2 or 3 => 3,
        4 or 5 or 6 => 6,
        _ => throw new ArgumentOutOfRangeException(nameof(unit))
    };

    /// <summary>Returns a source interval enclosing the unit at the start endpoint.</summary>
    internal AccessibleRange Expand(AccessibleRange range, int unit)
    {
        unit = SupportedUnit(unit);
        if (unit == 6) return Checked(0, _document.End);
        if (_document.End == 0) return Checked(0, 0);
        // UIA explicitly preserves an exact quantity of units. This differs
        // from Move's normalization to exactly one unit at its starting point.
        if (range.Length != 0 && IsBoundary(range.Start, unit) && IsBoundary(range.End, unit))
            return Checked(range.Start, range.End);
        // At EOF a nonempty range belongs to the final actual unit. A caret at
        // EOF stays degenerate; moving it backwards can reach the final unit.
        var offset = range.Start;
        if (offset == _document.End) return Checked(offset, offset);
        var line = Line(offset);
        if (unit == 3) return Checked(line.Start, line.End);
        var boundaries = Characters(_core.LineFromOffset(offset), offset, 0);
        var index = Array.BinarySearch(boundaries, offset);
        if (index < 0) index = ~index - 1;
        return Checked(boundaries[index], boundaries[index + 1]);
    }

    /// <summary>Moves an endpoint to signed unit boundaries, reporting exact completed units.</summary>
    internal (int Offset, int Moved) MoveEndpoint(int offset, int unit, int count)
    {
        unit = SupportedUnit(unit);
        if (count == 0) return (offset, 0);
        if (unit == 6)
        {
            var next = count > 0 ? _document.End : 0;
            return (next, next == offset ? 0 : Math.Sign(count));
        }
        if (unit == 3) return MoveLine(offset, count);
        if (count is -1 or 1 && TryAsciiStep(offset, count, out var ascii))
            return ascii;
        var direction = Math.Sign(count);
        var remaining = Math.Abs((long)count);
        var moved = 0;
        while (remaining-- > 0)
        {
            if (offset == (direction > 0 ? _document.End : 0)) break;
            if (++_steps > AccessibleDocument.MaxTextRequest)
                throw new AccessibleRequestTooLargeException(_steps, AccessibleDocument.MaxTextRequest);
            var lineIndex = _core.LineFromOffset(offset);
            var line = Line(offset);
            if (direction < 0 && offset == line.Start && lineIndex > 0) lineIndex--;
            var boundaries = Characters(lineIndex, offset, direction);
            var index = Array.BinarySearch(boundaries, offset);
            index = direction > 0
                ? index >= 0 ? index + 1 : ~index
                : index >= 0 ? index - 1 : ~index - 1;
            offset = boundaries[index];
            moved += direction;
        }
        _core.ValidateRange(_identity);
        return (offset, moved);
    }

    /// <summary>
    /// The dominant remote single-character operation needs only constant-size
    /// rope reads. Six ASCII neighbors certify reset context and CRLF seams;
    /// any Unicode neighbor delegates to bounded grapheme segmentation.
    /// </summary>
    private bool TryAsciiStep(int offset, int direction, out (int Offset, int Moved) result)
    {
        result = (offset, 0);
        if (offset == (direction > 0 ? _document.End : 0)) return true;
        var start = Math.Max(0, offset - 3);
        var end = (int)Math.Min(_document.End, (long)offset + 3);
        var text = _core.GetText(Checked(start, end), -1);
        foreach (var character in text)
            if (character > 0x7f) return false;
        var relative = offset - start;
        var next = offset + direction;
        if (direction > 0 && text[relative] == '\r' &&
            relative + 1 < text.Length && text[relative + 1] == '\n') next++;
        if (direction < 0 && text[relative - 1] == '\n' &&
            relative >= 2 && text[relative - 2] == '\r') next--;
        _core.ValidateRange(_identity);
        result = (next, direction);
        return true;
    }

    /// <summary>Normalizes and moves a range while preserving degenerate-caret semantics.</summary>
    internal (AccessibleRange Range, int Moved) Move(AccessibleRange range, int unit, int count)
    {
        unit = SupportedUnit(unit);
        if (count == 0) return (range, 0);
        if (range.Length == 0)
        {
            var caret = MoveEndpoint(range.Start, unit, count);
            return (Checked(caret.Offset, caret.Offset), caret.Moved);
        }
        var normalized = Expand(Checked(range.Start, range.Start), unit);
        var movement = MoveEndpoint(normalized.Start, unit, count);
        // A nondegenerate range cannot move beyond the last actual unit.
        if (movement.Offset == _document.End && _document.End != 0)
        {
            var previous = MoveEndpoint(movement.Offset, unit, -1);
            movement = (previous.Offset, movement.Moved - 1);
        }
        var result = Expand(Checked(movement.Offset, movement.Offset), unit);
        return (result, movement.Moved);
    }

    private (int Offset, int Moved) MoveLine(int offset, int count)
    {
        var lineIndex = _core.LineFromOffset(offset);
        var current = Line(offset);
        var last = _core.LineFromOffset(_document.End);
        // EOF is a boundary even when the last line has no delimiter. A trailing
        // empty line shares EOF and must not count as a second identical boundary.
        var lastRange = _core.RangeForLine(last);
        EnsureIdentity(lastRange);
        var finalBoundary = last + (lastRange.Length == 0 ? 0 : 1);
        var currentBoundary = offset == _document.End ? finalBoundary : lineIndex;
        var target = count > 0
            ? Math.Min((long)finalBoundary, (long)currentBoundary + count)
            : Math.Max(0L, (long)currentBoundary + count + (offset > current.Start && offset != _document.End ? 1 : 0));
        var next = target == finalBoundary ? _document.End : _core.RangeForLine((int)target).Start;
        var moved = count > 0 ? target - currentBoundary
            : target - currentBoundary - (offset > current.Start && offset != _document.End ? 1 : 0);
        _core.ValidateRange(_identity);
        return (next, (int)moved);
    }

    private AccessibleRange Line(int offset)
    {
        var line = _core.RangeForLine(_core.LineFromOffset(offset));
        EnsureIdentity(line);
        return line;
    }

    private bool IsBoundary(int offset, int unit)
    {
        if (offset == 0 || offset == _document.End) return true;
        var lineIndex = _core.LineFromOffset(offset);
        if (unit == 3) return Line(offset).Start == offset;
        return Array.BinarySearch(Characters(lineIndex, offset, 0), offset) >= 0;
    }

    /// <summary>One certified local segmentation, never a whole long-line index.</summary>
    private readonly record struct CharacterSegment(int Start, int End, int[] Boundaries);

    private int[] Characters(int lineIndex, int offset, int direction)
    {
        if (_characters.TryGetValue(lineIndex, out var cached) &&
            offset >= cached.Start && offset <= cached.End &&
            (direction <= 0 || offset < cached.End) && (direction >= 0 || offset > cached.Start))
            return cached.Boundaries;
        if (_characters.Count == MaxCharacterLines)
            throw new AccessibleRequestTooLargeException(_characters.Count + 1, MaxCharacterLines);
        var line = _core.RangeForLine(lineIndex);
        EnsureIdentity(line);
        var source = line.Length <= 4096 ? line : line with
        {
            Start = Math.Max(line.Start, offset - 2048),
            End = (int)Math.Min(line.End, (long)offset + 2048)
        };
        if (source.Length > _textBudget)
            throw new AccessibleRequestTooLargeException(source.Length, _textBudget);
        _textBudget -= source.Length;
        var text = _core.GetText(source, -1);
        var start = source.Start == line.Start ? 0 : CertifiedStart(text, offset - source.Start);
        var end = source.End == line.End ? text.Length : CertifiedEnd(text, offset - source.Start);
        if (start < 0 || end < 0 || start >= end ||
            direction < 0 && source.Start + start >= offset ||
            direction > 0 && source.Start + end <= offset)
            throw new AccessibleRequestTooLargeException(line.Length, 4096);
        var relative = StringInfo.ParseCombiningCharacters(text[start..end]);
        var result = new int[relative.Length + 1];
        for (var i = 0; i < relative.Length; i++) result[i] = source.Start + start + relative[i];
        result[^1] = source.Start + end;
        _characters[lineIndex] = new CharacterSegment(source.Start + start, source.Start + end, result);
        return result;
    }

    /// <summary>Finds a real reset boundary before the requested position within bounded context.</summary>
    private static int CertifiedStart(string text, int offset)
    {
        for (var boundary = 1; boundary < offset; boundary++)
            if (CertifiedBreak(text, boundary)) return boundary;
        return -1;
    }

    /// <summary>Finds a reset boundary after the requested position, preserving lookahead.</summary>
    private static int CertifiedEnd(string text, int offset)
    {
        for (var boundary = text.Length - 1; boundary > offset; boundary--)
            if (CertifiedBreak(text, boundary)) return boundary;
        return -1;
    }

    /// <summary>
    /// Uses the input-island selector's conservative reset rule: never truncate
    /// earlier RI parity, extending marks, joiners, or linker context. A local
    /// StringInfo break is certified only after a non-context-dependent scalar.
    /// A pathological cluster with no such reset fails without endpoint mutation.
    /// </summary>
    private static bool CertifiedBreak(string text, int boundary)
    {
        if (char.IsHighSurrogate(text[boundary - 1]) && char.IsLowSurrogate(text[boundary]))
            return false;
        var previous = boundary - 1;
        if (previous > 0 && char.IsLowSurrogate(text[previous]) && char.IsHighSurrogate(text[previous - 1]))
            previous--;
        if (!Rune.TryGetRuneAt(text, previous, out var rune) ||
            rune.Value is >= 0x1F1E6 and <= 0x1F1FF ||
            Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or
                UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format)
            return false;
        var nextEnd = boundary + 1;
        if (nextEnd < text.Length && char.IsHighSurrogate(text[boundary]) && char.IsLowSurrogate(text[nextEnd]))
            nextEnd++;
        var pair = text[previous..nextEnd];
        var boundaries = StringInfo.ParseCombiningCharacters(pair);
        return boundaries.Length >= 2 && boundaries[1] == boundary - previous;
    }
    private AccessibleRange Checked(int start, int end)
    {
        var result = _core.MakeRange(start, end);
        EnsureIdentity(result);
        return result;
    }

    private void EnsureIdentity(AccessibleRange range)
    {
        if (range.Generation != _identity.Generation || range.Version != _identity.Version)
            throw new InvalidOperationException("Accessibility source changed during range navigation.");
    }
}
