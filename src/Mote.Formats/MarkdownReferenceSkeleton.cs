namespace Mote.Formats;

/// <summary>Allocation-free construction of the already-proved skeleton and original atom geometry in caller-owned scratch.</summary>
internal static class MarkdownReferenceSkeleton
{
    /// <summary>Returns active skeleton length/atom count, or rejects before any cache query; scratch tail is never meaningful.</summary>
    internal static (int Length, int Atoms)? Describe(ReadOnlySpan<char> source, Span<char> output, Span<int> starts,
        MarkdownReferenceBudget ledger)
    {
        if (source.Length is 0 or > 4096) return null;
        ledger.Visit(source.Length * 2L + 256);
        Span<char> keys = stackalloc char[256]; Span<int> keyLengths = stackalloc int[4];
        var keyCount = 0; var cursor = 0; var written = 0; var atoms = 0;
        if (source[0] == '#')
        {
            while (cursor < source.Length && source[cursor] == '#') cursor++;
            if (cursor > 6 || cursor == source.Length || source[cursor] != ' ') return null;
            source[..(cursor + 1)].CopyTo(output); written = ++cursor;
        }
        while (cursor < source.Length)
        {
            if (source[cursor] == ' ') { output[written++] = ' '; cursor++; continue; }
            if (char.IsAsciiLetterOrDigit(source[cursor]))
            {
                output[written++] = 'X';
                do { cursor++; } while (cursor < source.Length && char.IsAsciiLetterOrDigit(source[cursor]));
                continue;
            }
            if (source[cursor] != '[' || atoms == 32 || (atoms != 0 && source[cursor - 1] != ' ')) return null;
            var first = source[(cursor + 1)..].IndexOf(']'); if (first < 0) return null;
            var close = cursor + 1 + first;
            if (close + 1 >= source.Length || source[close + 1] != '[') return null;
            var second = source[(close + 2)..].IndexOf(']'); if (second < 0) return null;
            var end = close + 2 + second;
            if (end + 1 < source.Length && source[end + 1] != ' ') return null;
            if (!AddKey(source[(cursor + 1)..close], keys, keyLengths, ref keyCount, ledger) ||
                !AddKey(source[(close + 2)..end], keys, keyLengths, ref keyCount, ledger)) return null;
            starts[atoms++] = cursor;
            source.Slice(cursor, end + 1 - cursor).CopyTo(output[written..]); written += end + 1 - cursor; cursor = end + 1;
        }
        return (written, atoms);
    }

    /// <summary>Normalizes the same restricted label alphabet without allocating a key string on cache hits.</summary>
    private static bool AddKey(ReadOnlySpan<char> raw, Span<char> keys, Span<int> lengths, ref int count, MarkdownReferenceBudget ledger)
    {
        if (raw.Length is 0 or > 64) return false;
        ledger.Visit(6L * raw.Length + 4);
        Span<char> normalized = stackalloc char[64]; var length = 0; var gap = false;
        foreach (var c in raw)
        {
            if (c == ' ') { gap = length > 0; continue; }
            if (!char.IsAsciiLetterOrDigit(c)) return false;
            if (gap) normalized[length++] = ' ';
            normalized[length++] = char.ToLowerInvariant(c); gap = false;
        }
        if (length == 0) return false;
        for (var i = 0; i < count; i++) if (normalized[..length].SequenceEqual(keys.Slice(i * 64, lengths[i]))) return true;
        if (count == 4) return false;
        normalized[..length].CopyTo(keys.Slice(count * 64, 64)); lengths[count++] = length;
        return true;
    }

    /// <summary>Indexes caches only; every hit also requires exact active-prefix or geometry equality.</summary>
    internal static ulong Hash(ReadOnlySpan<char> source)
    {
        var hash = 14695981039346656037UL;
        foreach (var c in source) hash = unchecked((hash ^ c) * 1099511628211UL);
        return hash;
    }
}
