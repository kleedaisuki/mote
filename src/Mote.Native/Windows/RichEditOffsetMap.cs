namespace Mote.Native.Windows;

/// <summary>
/// Converts between RichEdit's paragraph offsets (one CR) and the CRLF text exposed by
/// <c>EM_GETTEXTEX</c> with <c>GT_USECRLF</c>. Selection and formatting messages use the
/// former, while the controller's bounded text projection and semantic spans use the latter.
/// </summary>
internal sealed class RichEditOffsetMap
{
    private readonly int[] _displayLf;
    private readonly int[] _nativeCr;
    private readonly int _displayLength;

    /// <summary>Number of paragraph boundaries expanded by CRLF projection.</summary>
    public int NewlineCount => _displayLf.Length;

    /// <summary>Indexes CRLF pairs in one bounded display page.</summary>
    public RichEditOffsetMap(string displayText)
    {
        ArgumentNullException.ThrowIfNull(displayText);
        _displayLength = displayText.Length;
        var displayLf = new List<int>();
        var nativeCr = new List<int>();
        for (var i = 1; i < displayText.Length; i++)
        {
            if (displayText[i - 1] != '\r' || displayText[i] != '\n') continue;
            displayLf.Add(i);
            nativeCr.Add(i - displayLf.Count);
        }
        _displayLf = displayLf.ToArray();
        _nativeCr = nativeCr.ToArray();
    }

    /// <summary>Maps a display UTF-16 boundary to its RichEdit internal boundary.</summary>
    public int ToNative(int displayOffset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(displayOffset);
        if (displayOffset > _displayLength) throw new ArgumentOutOfRangeException(nameof(displayOffset));
        return displayOffset - CountLessThan(_displayLf, displayOffset);
    }

    /// <summary>Maps a RichEdit internal UTF-16 boundary back to the CRLF display boundary.</summary>
    public int ToDisplay(int nativeOffset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nativeOffset);
        if (nativeOffset > _displayLength - _displayLf.Length)
            throw new ArgumentOutOfRangeException(nameof(nativeOffset));
        return nativeOffset + CountLessThan(_nativeCr, nativeOffset);
    }

    private static int CountLessThan(int[] sorted, int value)
    {
        var index = Array.BinarySearch(sorted, value);
        if (index < 0) return ~index;
        while (index > 0 && sorted[index - 1] == value) index--;
        return index;
    }
}
