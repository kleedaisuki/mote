using System.Text;
using Mote.Engine;

namespace Mote.Native;

/// <summary>
/// Maps one bounded canonical source page to a platform text control. In particular,
/// RichEdit's CRLF projection never changes the engine's original line-ending bytes.
/// </summary>
internal sealed class NativeTextProjection
{
    private readonly int[] _sourceToDisplay;
    private readonly int[] _displayToSourceFloor;
    private readonly int[] _displayToSourceCeiling;
    private readonly NativeLineEndingMode _mode;

    /// <summary>Creates a reversible coordinate map for one immutable source page.</summary>
    public NativeTextProjection(string source, NativeLineEndingMode mode)
    {
        Source = source;
        _mode = mode;
        if (mode == NativeLineEndingMode.Preserve)
        {
            Display = source;
            _sourceToDisplay = Identity(source.Length + 1);
            _displayToSourceFloor = _sourceToDisplay;
            _displayToSourceCeiling = _sourceToDisplay;
            return;
        }

        var output = new StringBuilder(source.Length);
        var sourceToDisplay = new int[source.Length + 1];
        var floor = new List<int>(source.Length + 1) { 0 };
        var ceiling = new List<int>(source.Length + 1) { 0 };
        var sourceOffset = 0;
        while (sourceOffset < source.Length)
        {
            var start = sourceOffset;
            var c = source[sourceOffset++];
            if (c == '\r' && sourceOffset < source.Length && source[sourceOffset] == '\n')
            {
                output.Append('\r');
                floor.Add(sourceOffset);
                ceiling.Add(sourceOffset);
                sourceToDisplay[sourceOffset] = output.Length;
                output.Append('\n');
                sourceOffset++;
            }
            else if (c is '\r' or '\n')
            {
                output.Append('\r');
                floor.Add(start);
                ceiling.Add(sourceOffset);
                output.Append('\n');
            }
            else
            {
                output.Append(c);
            }
            floor.Add(sourceOffset);
            ceiling.Add(sourceOffset);
            sourceToDisplay[sourceOffset] = output.Length;
        }
        Display = output.ToString();
        _sourceToDisplay = sourceToDisplay;
        _displayToSourceFloor = floor.ToArray();
        _displayToSourceCeiling = ceiling.ToArray();
    }

    /// <summary>The engine's exact page text.</summary>
    public string Source { get; }

    /// <summary>The bounded text passed to the native control.</summary>
    public string Display { get; }

    /// <summary>Converts a source-relative UTF-16 boundary to a display boundary.</summary>
    public int ToDisplay(int sourceOffset) => _sourceToDisplay[sourceOffset];

    /// <summary>
    /// Converts a display boundary to a source boundary. A position inside a synthesized
    /// CRLF maps before or after its original one-character delimiter as requested.
    /// </summary>
    public int ToSourceBoundary(int displayOffset, bool towardEnd = false)
    {
        if ((uint)displayOffset >= (uint)_displayToSourceFloor.Length)
            throw new ArgumentOutOfRangeException(nameof(displayOffset));
        return towardEnd ? _displayToSourceCeiling[displayOffset] :
            _displayToSourceFloor[displayOffset];
    }

    /// <summary>
    /// Computes a single engine replacement from an edited native page. Unchanged source
    /// outside the replacement keeps its exact original newline spelling. Replacement
    /// endpoints include whole surrogate pairs; inserted text is not Unicode-normalized
    /// or repaired, so malformed input remains subject to the engine's rejection contract.
    /// </summary>
    public TextChange? Difference(string editedDisplay)
    {
        ArgumentNullException.ThrowIfNull(editedDisplay);
        var (commonPrefix, oldEnd, newEnd) = ReplacementBounds(Display, editedDisplay);
        if (commonPrefix == Display.Length && commonPrefix == editedDisplay.Length) return null;

        var sourceStart = _displayToSourceFloor[commonPrefix];
        var sourceEnd = _displayToSourceCeiling[oldEnd];
        var prefix = Display.AsSpan(_sourceToDisplay[sourceStart], commonPrefix - _sourceToDisplay[sourceStart]);
        var suffix = Display.AsSpan(oldEnd, _sourceToDisplay[sourceEnd] - oldEnd);
        var insert = string.Concat(prefix, editedDisplay.AsSpan(commonPrefix, newEnd - commonPrefix), suffix);
        if (_mode == NativeLineEndingMode.CrLf)
            insert = NormalizeLineEndings(insert, PreferredLineEnding(sourceStart));
        var direct = new TextChange(sourceStart, sourceEnd - sourceStart, insert);
        if (ProjectsTo(direct, editedDisplay)) return direct;
        return DifferenceByAtoms(editedDisplay);
    }

    private bool ProjectsTo(TextChange change, string expectedDisplay)
    {
        var expectedOffset = 0;
        if (_mode == NativeLineEndingMode.Preserve)
            return CompareRaw(Source.AsSpan(0, change.Start), expectedDisplay, ref expectedOffset) &&
                CompareRaw(change.InsertText, expectedDisplay, ref expectedOffset) &&
                CompareRaw(Source.AsSpan(change.Start + change.DeleteLength), expectedDisplay,
                    ref expectedOffset) && expectedOffset == expectedDisplay.Length;

        var pendingCr = false;
        if (!CompareProjected(Source.AsSpan(0, change.Start), expectedDisplay,
                ref expectedOffset, ref pendingCr) ||
            !CompareProjected(change.InsertText, expectedDisplay, ref expectedOffset, ref pendingCr) ||
            !CompareProjected(Source.AsSpan(change.Start + change.DeleteLength), expectedDisplay,
                ref expectedOffset, ref pendingCr)) return false;
        return (!pendingCr || MatchNewline(expectedDisplay, ref expectedOffset)) &&
            expectedOffset == expectedDisplay.Length;
    }

    private static bool CompareRaw(ReadOnlySpan<char> source, string expected, ref int offset)
    {
        if (source.Length > expected.Length - offset ||
            !source.SequenceEqual(expected.AsSpan(offset, source.Length))) return false;
        offset += source.Length;
        return true;
    }

    private static bool CompareProjected(ReadOnlySpan<char> source, string expected,
        ref int offset, ref bool pendingCr)
    {
        foreach (var c in source)
        {
            if (pendingCr)
            {
                if (!MatchNewline(expected, ref offset)) return false;
                pendingCr = false;
                if (c == '\n') continue;
            }
            if (c == '\r') { pendingCr = true; continue; }
            if (c == '\n')
            {
                if (!MatchNewline(expected, ref offset)) return false;
            }
            else if (offset >= expected.Length || expected[offset++] != c) return false;
        }
        return true;
    }

    private static bool MatchNewline(string expected, ref int offset)
    {
        if (offset + 1 >= expected.Length || expected[offset] != '\r' ||
            expected[offset + 1] != '\n') return false;
        offset += 2;
        return true;
    }

    /// <summary>
    /// A source CR and LF separated by deleted text can otherwise merge into one CRLF.
    /// Retokenize only on this exceptional path, preserving unchanged newline atoms and
    /// widening the replacement when an adjacent newline spelling must change to keep
    /// two visible lines. The changed spelling is part of the explicit engine edit.
    /// </summary>
    private TextChange DifferenceByAtoms(string editedDisplay)
    {
        var before = SourceAtoms(Source);
        var after = DisplayAtoms(editedDisplay);
        var prefix = 0;
        while (prefix < before.Count && prefix < after.Count &&
               Same(before[prefix], after[prefix], editedDisplay)) prefix++;
        var suffix = 0;
        while (suffix < before.Count - prefix && suffix < after.Count - prefix &&
               Same(before[before.Count - suffix - 1], after[after.Count - suffix - 1], editedDisplay))
            suffix++;

        var rebuilt = new StringBuilder(editedDisplay.Length);
        var priorWasLoneCr = false;
        void Append(ReadOnlySpan<char> atom)
        {
            if (priorWasLoneCr && atom.SequenceEqual("\n"))
            {
                // CR + LF would become one delimiter. Preserve the left CR and
                // spell the right delimiter CRLF instead.
                rebuilt.Append("\r\n");
                priorWasLoneCr = false;
                return;
            }
            rebuilt.Append(atom);
            priorWasLoneCr = atom.SequenceEqual("\r");
        }

        for (var i = 0; i < prefix; i++)
        {
            var atom = before[i];
            Append(Source.AsSpan(atom.Start, atom.Length));
        }
        var newline = PreferredLineEnding(prefix < before.Count ? before[prefix].Start : Source.Length);
        for (var i = prefix; i < after.Count - suffix; i++)
        {
            var atom = after[i];
            Append(atom.IsNewline ? newline.AsSpan() : editedDisplay.AsSpan(atom.Start, atom.Length));
        }
        for (var i = before.Count - suffix; i < before.Count; i++)
        {
            var atom = before[i];
            Append(Source.AsSpan(atom.Start, atom.Length));
        }

        var desired = rebuilt.ToString();
        if (!string.Equals(new NativeTextProjection(desired, _mode).Display,
            editedDisplay, StringComparison.Ordinal))
            throw new InvalidOperationException("The native edit cannot be mapped to canonical text without loss.");
        var (start, oldEnd, newEnd) = ReplacementBounds(Source, desired);
        return new TextChange(start, oldEnd - start, desired[start..newEnd]);
    }

    /// <summary>
    /// Finds one replacement whose endpoints do not bisect a Unicode scalar in either
    /// string. Equal code units inside a surrogate pair are included in the replacement;
    /// malformed input remains unchanged for the engine's validation to reject.
    /// </summary>
    private static (int Start, int OldEnd, int NewEnd) ReplacementBounds(string before, string after)
    {
        var start = 0;
        var limit = Math.Min(before.Length, after.Length);
        while (start < limit && before[start] == after[start]) start++;
        if (SplitsScalar(before, start) || SplitsScalar(after, start)) start--;

        var oldEnd = before.Length;
        var newEnd = after.Length;
        while (oldEnd > start && newEnd > start && before[oldEnd - 1] == after[newEnd - 1])
        {
            oldEnd--;
            newEnd--;
        }
        if (SplitsScalar(before, oldEnd) || SplitsScalar(after, newEnd))
        {
            oldEnd++;
            newEnd++;
        }
        return (start, oldEnd, newEnd);
    }

    /// <summary>Whether a UTF-16 boundary lies between a paired high and low surrogate.</summary>
    private static bool SplitsScalar(string value, int offset) =>
        offset > 0 && offset < value.Length && char.IsHighSurrogate(value[offset - 1]) &&
        char.IsLowSurrogate(value[offset]);

    private bool Same(Atom source, Atom display, string edited) =>
        source.IsNewline == display.IsNewline &&
        (source.IsNewline || Source.AsSpan(source.Start, source.Length)
            .SequenceEqual(edited.AsSpan(display.Start, display.Length)));

    private static List<Atom> SourceAtoms(string value)
    {
        var atoms = new List<Atom>();
        for (var i = 0; i < value.Length;)
        {
            var start = i++;
            var newline = value[start] is '\r' or '\n';
            if (value[start] == '\r' && i < value.Length && value[i] == '\n') i++;
            else if (char.IsHighSurrogate(value[start]) && i < value.Length &&
                     char.IsLowSurrogate(value[i])) i++;
            atoms.Add(new Atom(start, i - start, newline));
        }
        return atoms;
    }

    private static List<Atom> DisplayAtoms(string value)
    {
        var atoms = new List<Atom>();
        for (var i = 0; i < value.Length;)
        {
            var start = i++;
            var newline = value[start] is '\r' or '\n';
            if (value[start] == '\r' && i < value.Length && value[i] == '\n') i++;
            else if (char.IsHighSurrogate(value[start]) && i < value.Length &&
                     char.IsLowSurrogate(value[i])) i++;
            atoms.Add(new Atom(start, i - start, newline));
        }
        return atoms;
    }

    private readonly record struct Atom(int Start, int Length, bool IsNewline);

    private string PreferredLineEnding(int sourceOffset)
    {
        for (var i = Math.Min(sourceOffset, Source.Length) - 1; i >= 0; i--)
        {
            if (Source[i] == '\n') return i > 0 && Source[i - 1] == '\r' ? "\r\n" : "\n";
            if (Source[i] == '\r') return "\r";
        }
        for (var i = sourceOffset; i < Source.Length; i++)
        {
            if (Source[i] == '\r') return i + 1 < Source.Length && Source[i + 1] == '\n' ? "\r\n" : "\r";
            if (Source[i] == '\n') return "\n";
        }
        return Environment.NewLine;
    }

    private static string NormalizeLineEndings(string value, string newline)
    {
        var result = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\r' && i + 1 < value.Length && value[i + 1] == '\n')
            {
                result.Append(newline);
                i++;
            }
            else if (value[i] is '\r' or '\n') result.Append(newline);
            else result.Append(value[i]);
        }
        return result.ToString();
    }

    private static int[] Identity(int count)
    {
        var values = new int[count];
        for (var i = 0; i < count; i++) values[i] = i;
        return values;
    }
}
