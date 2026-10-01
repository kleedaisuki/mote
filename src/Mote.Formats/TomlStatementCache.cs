using Mote.Engine;

namespace Mote.Formats;

/// <summary>One committed Complete snapshot and its immutable source-order statement summaries.</summary>
/// <param name="Snapshot">The sole retained committed source identity; later commits retire this reference.</param>
/// <param name="Statements">Exhaustive source-order units with current absolute positions.</param>
internal sealed record TomlStatementCache(TextSnapshot Snapshot, TomlStatement[] Statements)
{
    /// <summary>
    /// Repairs a single actual edit, expanding through multiline owners to an exact mapped seam.
    /// Version claims, edit payload and unchanged prefix/suffix must all agree with source.
    /// Unsupported history returns null; the caller performs the authoritative Full fallback.
    /// </summary>
    internal TomlRepair? Repair(TextSnapshot snapshot, IReadOnlyList<VersionedEdit> edits, CancellationToken ct)
    {
        if (edits.Count != 1 || edits[0].BeforeVersion != Snapshot.Version ||
            edits[0].AfterVersion != snapshot.Version || edits[0].AfterVersion <= edits[0].BeforeVersion ||
            Statements.Length == 0) return null;
        var change = edits[0].Change;
        if (!Matches(snapshot, change, ct)) return null;
        int first = FirstOwner(change.Start);
        int delta = change.InsertText.Length - change.DeleteLength;
        int minimum = change.Start + change.InsertText.Length;
        int last = Statements.Length;
        bool Stop(int end)
        {
            if (end < minimum) return false;
            int seam = FindSeam(end - delta);
            if (seam < first || Statements[seam].End < change.Start + change.DeleteLength) return false;
            last = seam + 1;
            return true;
        }
        var scan = TomlStatementReader.Read(snapshot, Statements[first].Start, _ => true, Stop, ct);
        if (!scan.Valid) return new(null, false, scan.ParsedCharacters, scan.ScannedCharacters);
        int count = first + scan.Statements.Count + Statements.Length - last;
        if (count > TomlStatementReader.MaxStatements)
            return new(null, false, scan.ParsedCharacters, scan.ScannedCharacters);
        var mapped = new TomlStatement[count];
        Array.Copy(Statements, mapped, first);
        for (int i = 0; i < scan.Statements.Count; i++) mapped[first + i] = scan.Statements[i];
        int target = first + scan.Statements.Count;
        for (int i = last; i < Statements.Length; i++)
            mapped[target++] = Statements[i] with { Start = Statements[i].Start + delta };
        bool equal = EffectsEqual(Statements.AsSpan(first, last - first), scan.Statements);
        return new(new(snapshot, mapped), equal, scan.ParsedCharacters, scan.ScannedCharacters);
    }

    /// <summary>Finds a preceding owner, including its newline when an edit begins at a seam.</summary>
    private int FirstOwner(int start)
    {
        int lo = 0, hi = Statements.Length - 1;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (Statements[mid].End < start) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    /// <summary>Only an exact old end, never a nearby newline, may certify suffix reuse.</summary>
    private int FindSeam(int end)
    {
        int lo = 0, hi = Statements.Length - 1;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            int value = Statements[mid].End;
            if (value == end) return mid;
            if (value < end) lo = mid + 1;
            else hi = mid - 1;
        }
        return -1;
    }

    /// <summary>Verifies the complete edit against actual source, not caller-supplied version numbers.</summary>
    private bool Matches(TextSnapshot snapshot, TextChange change, CancellationToken ct)
    {
        if (change.Start < 0 || change.DeleteLength < 0 || change.Start > Snapshot.Length ||
            change.DeleteLength > Snapshot.Length - change.Start || change.InsertText is null) return false;
        long expected = (long)Snapshot.Length - change.DeleteLength + change.InsertText.Length;
        if (expected != snapshot.Length) return false;
        return Equal(Snapshot, 0, snapshot, 0, change.Start, ct) &&
            InsertMatches(snapshot, change, ct) &&
            Equal(Snapshot, change.Start + change.DeleteLength, snapshot,
                change.Start + change.InsertText.Length, Snapshot.Length - change.Start - change.DeleteLength, ct);
    }

    /// <summary>Checks declared inserted characters without materializing a new snapshot range.</summary>
    private static bool InsertMatches(TextSnapshot snapshot, TextChange change, CancellationToken ct)
    {
        int position = 0;
        foreach (var chunk in snapshot.GetChunks(change.Start, change.InsertText.Length))
        {
            ct.ThrowIfCancellationRequested();
            if (!chunk.Span.SequenceEqual(change.InsertText.AsSpan(position, chunk.Length))) return false;
            position += chunk.Length;
        }
        return true;
    }

    /// <summary>
    /// Shared immutable slice identity proves equality cheaply; different storage is compared
    /// exactly. No probabilistic hash, document version or unrelated snapshot identity suffices.
    /// </summary>
    private static bool Equal(TextSnapshot before, int a, TextSnapshot after, int b, int length, CancellationToken ct)
    {
        using var left = before.GetChunks(a, length).GetEnumerator();
        using var right = after.GetChunks(b, length).GetEnumerator();
        bool hasLeft = left.MoveNext(), hasRight = right.MoveNext();
        int li = 0, ri = 0;
        while (hasLeft && hasRight)
        {
            ct.ThrowIfCancellationRequested();
            int count = Math.Min(left.Current.Length - li, right.Current.Length - ri);
            var l = left.Current.Slice(li, count);
            var r = right.Current.Slice(ri, count);
            if (!l.Equals(r) && !l.Span.SequenceEqual(r.Span)) return false;
            li += count;
            ri += count;
            if (li == left.Current.Length) { hasLeft = left.MoveNext(); li = 0; }
            if (ri == right.Current.Length) { hasRight = right.MoveNext(); ri = 0; }
        }
        return !hasLeft && !hasRight;
    }

    /// <summary>Trivia has no namespace dependency; all remaining actions compare decoded keys exactly.</summary>
    private static bool EffectsEqual(ReadOnlySpan<TomlStatement> before, IReadOnlyList<TomlStatement> after)
    {
        int a = 0, b = 0;
        while (true)
        {
            while (a < before.Length && before[a].Summary.Action == TomlStatementAction.Trivia) a++;
            while (b < after.Count && after[b].Summary.Action == TomlStatementAction.Trivia) b++;
            if (a == before.Length || b == after.Count) return a == before.Length && b == after.Count;
            if (!before[a++].Summary.HasSameEffect(after[b++].Summary)) return false;
        }
    }
}

/// <summary>A staged repair plus its exact namespace dependency equivalence and observed syntax work.</summary>
/// <param name="Cache">Validated mapped source, or null when syntax/resource repair was refused.</param>
/// <param name="SameEffects">Whether all nontrivia namespace actions and decoded keys are unchanged.</param>
/// <param name="ParsedCharacters">Actual standalone parser input, including a refused attempt.</param>
/// <param name="ScannedCharacters">Actual logical-boundary visits, excluding rope equality checks.</param>
internal sealed record TomlRepair(TomlStatementCache? Cache, bool SameEffects,
    int ParsedCharacters, int ScannedCharacters);
