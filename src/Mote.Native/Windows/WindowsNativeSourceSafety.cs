namespace Mote.Native.Windows;

/// <summary>Length-aware RichEdit readback and display-only representation of unsupported source NUL.</summary>
internal static class WindowsNativeSourceSafety
{
    /// <summary>NUL cannot be preserved by RichEdit import; its marker is legal only in a read-only host.</summary>
    internal static string ReadOnlyDisplay(string source) => source.Replace('\0', '\u2400');

    /// <summary>Reads explicit returned UTF-16 length; uninitialized native suffixes can never enter Engine text.</summary>
    internal static unsafe string Read(nint control)
    {
        var length = (int)Win32.SendMessageW(control, Win32.WM_GETTEXTLENGTH, 0, 0);
        if (length < 0) throw new InvalidOperationException("Invalid native source length.");
        // Native paragraphs may expand to CRLF. A zero-initialized managed buffer
        // ensures even a lying/truncated native count cannot expose uninitialized memory.
        var capacity = checked((length + 1) * 2 + 16);
        var buffer = new char[capacity];
        var request = new Win32.GetTextEx
        {
            ByteCapacity = checked((uint)(capacity * sizeof(char))),
            Flags = Win32.GT_USECRLF,
            CodePage = Win32.CP_UNICODE
        };
        fixed (char* pointer = buffer)
        {
            var count = (int)Win32.SendMessageW(control, Win32.EM_GETTEXTEX, ref request, (nint)pointer);
            if (count < 0 || count >= capacity || buffer.AsSpan(0, count).Contains('\0'))
                throw new InvalidOperationException("Native text readback did not deliver a valid complete buffer.");
            return new string(buffer, 0, count);
        }
    }
}
