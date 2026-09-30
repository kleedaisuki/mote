using Mote.Engine;

namespace Mote.Formats;

/// <summary>One complete root-array owner, without retained source, AST or decoded keys.</summary>
/// <param name="Start">First value token; the preceding separator belongs to the previous page.</param>
/// <param name="End">Next value token, or the closing root bracket for the final page.</param>
/// <param name="ElementCount">Exactly validated root-array values inside this owner.</param>
/// <param name="DiagnosticCount">Object-local duplicate diagnostics counted once per owner.</param>
/// <param name="Dirty">Mapped candidate whose old summary must not be asserted as current truth.</param>
internal readonly record struct JsonArrayPage(int Start, int End, int ElementCount, int DiagnosticCount, bool Dirty)
{
    /// <summary>Absolute half-open UTF-16 owner interval.</summary>
    internal TextSpan Span => new(Start, End - Start);
}

/// <summary>Immutable, version-tagged compositional proof; stores only source-sized owner summaries.</summary>
/// <remarks>
/// The shell and every separator were accepted by the production parser. Edits are admitted
/// strictly inside one owner, never at its fixed outer seam or in the shell. Successful
/// strict range parsing restores independence after malformed intermediate typing.
/// </remarks>
internal sealed record JsonArrayCertificate(long Version, int Length, int ArrayStart, int ArrayEnd, JsonArrayPage[] Pages)
{
    /// <summary>Target owner size; a large indivisible value may exceed it.</summary>
    internal const int TargetPageLength = 64 * 1024;
    /// <summary>Admission cap, independent of element count; refusal leaves Full streaming intact.</summary>
    internal const int MaxPages = 16_384;

    /// <summary>Maps each edit in its own BeforeVersion coordinates, never a collapsed final extent.</summary>
    internal JsonArrayCertificate? Map(TextSnapshot snapshot, IReadOnlyList<VersionedEdit> edits,
        bool sameSource, CancellationToken ct)
    {
        if (snapshot.Version == Version)
            return sameSource && edits.Count == 0 && snapshot.Length == Length ? this : null;
        if (edits.Count == 0) return null;
        var pages = (JsonArrayPage[])Pages.Clone();
        long version = Version;
        int length = Length, arrayEnd = ArrayEnd;
        foreach (var edit in edits)
        {
            ct.ThrowIfCancellationRequested();
            var change = edit.Change;
            if (edit.BeforeVersion != version || edit.AfterVersion <= version || change.InsertText is null ||
                change.Start < 0 || change.DeleteLength < 0 || change.Start > length ||
                change.DeleteLength > length - change.Start) return null;
            int end = change.Start + change.DeleteLength;
            int owner = FindOwner(pages, change.Start);
            if (owner < 0 || change.Start <= pages[owner].Start || end >= pages[owner].End) return null;
            long delta = (long)change.InsertText.Length - change.DeleteLength;
            if (length + delta > int.MaxValue || length + delta < 0) return null;
            int shift = (int)delta;
            pages[owner] = pages[owner] with { End = pages[owner].End + shift, Dirty = true };
            for (int i = owner + 1; i < pages.Length; i++)
                pages[i] = pages[i] with { Start = pages[i].Start + shift, End = pages[i].End + shift };
            length += shift;
            arrayEnd += shift;
            version = edit.AfterVersion;
        }
        return version == snapshot.Version && length == snapshot.Length
            ? new JsonArrayCertificate(version, length, ArrayStart, arrayEnd, pages) : null;
    }

    /// <summary>Finds an interval candidate; admission separately rejects exact seams.</summary>
    private static int FindOwner(JsonArrayPage[] pages, int position)
    {
        int low = 0, high = pages.Length - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            var page = pages[middle];
            if (position < page.Start) high = middle - 1;
            else if (position >= page.End) low = middle + 1;
            else return middle;
        }
        return -1;
    }
}
