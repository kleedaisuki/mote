using Mote.Themes;

namespace Mote.Native.Mac;

/// <summary>Pure range planning for foreground-only native diagnostic publication.</summary>
internal static class MacNativeForegroundPublication
{
    /// <summary>
    /// Returns the complement of all nonempty styles after validating every range.
    /// Only coverage is sorted: callers still apply original styles in input order,
    /// so arbitrary overlaps retain last-style-wins semantics. Coordinates remain
    /// display UTF-16 units; no scalar or newline boundary is rewritten.
    /// </summary>
    internal static NativeSourceDiagnosticRange[] UncoveredRanges(int length,
        IReadOnlyList<NativeSourceDiagnosticStyle> styles)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentNullException.ThrowIfNull(styles);
        foreach (var style in styles)
            if (style.Start < 0 || style.Length < 0 || style.Start > length ||
                style.Length > length - style.Start)
                throw new ArgumentOutOfRangeException(nameof(styles));

        var covered = styles.Where(style => style.Length != 0).OrderBy(style => style.Start);
        var gaps = new List<NativeSourceDiagnosticRange>();
        var end = 0;
        foreach (var style in covered)
        {
            if (style.Start > end) gaps.Add(new(end, style.Start - end));
            end = Math.Max(end, style.Start + style.Length);
        }
        if (end < length) gaps.Add(new(end, length - end));
        return gaps.ToArray();
    }

    /// <summary>
    /// Checks a native effective range and clips its forward boundary to one
    /// requested span. A short valid range is normal; absent attributes still
    /// require a valid range. Invalid, empty or non-progressing results fail.
    /// </summary>
    internal static int EffectiveEnd(int length, int position, int requestedEnd,
        nuint location, nuint count)
    {
        if (length < 0 || position < 0 || position >= requestedEnd || requestedEnd > length ||
            location > (nuint)position || location > (nuint)length ||
            count > (nuint)length - location || count == 0 ||
            location + count <= (nuint)position)
            throw new InvalidOperationException("Native foreground range is invalid or does not advance.");
        return (int)Math.Min(location + count, (nuint)requestedEnd);
    }

    /// <summary>Returns at most 21 representative actual-readback locations, never a complete style certificate.</summary>
    internal static int[] SampleLocations(int length, IReadOnlyList<NativeSourceDiagnosticStyle> styles,
        IReadOnlyList<NativeSourceDiagnosticRange> gaps)
    {
        var samples = new HashSet<int>();
        Add(0, length);
        var count = styles.Count(style => style.Length != 0);
        var ordinal = 0;
        foreach (var style in styles)
        {
            if (style.Length == 0) continue;
            if (ordinal == 0 || ordinal == count / 2 || ordinal == count - 1)
                Add(style.Start, style.Length);
            ordinal++;
        }
        foreach (var index in RepresentativeIndices(gaps.Count))
            Add(gaps[index].Start, gaps[index].Length);
        return samples.Order().ToArray();

        void Add(int start, int count)
        {
            if (count == 0) return;
            samples.Add(start);
            samples.Add(start + count / 2);
            samples.Add(start + count - 1);
        }
    }

    /// <summary>Resolves the exact original overlay order for a bounded readback location.</summary>
    internal static ThemeColor ExpectedColor(int position, ThemeColor foreground,
        IReadOnlyList<NativeSourceDiagnosticStyle> styles)
    {
        for (var index = styles.Count - 1; index >= 0; index--)
        {
            var style = styles[index];
            if (position >= style.Start && position - style.Start < style.Length)
                return style.Foreground;
        }
        return foreground;
    }

    /// <summary>Samples first, middle and last entries without depending on sorted style order.</summary>
    private static IEnumerable<int> RepresentativeIndices(int count)
    {
        if (count == 0) yield break;
        yield return 0;
        if (count > 2) yield return count / 2;
        if (count > 1) yield return count - 1;
    }
}
