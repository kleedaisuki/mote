using System.Text;

namespace Mote.Engine;

/// <summary>Explicit source codecs; no locale lookup, guessing, or implicit conversion.</summary>
public enum DocumentTextEncoding
{
    /// <summary>Strict UTF-8, retaining a matching source BOM if present.</summary>
    Utf8,
    /// <summary>Strict little-endian UTF-16.</summary>
    Utf16LittleEndian,
    /// <summary>Strict big-endian UTF-16.</summary>
    Utf16BigEndian,
    /// <summary>Strict little-endian UTF-32.</summary>
    Utf32LittleEndian,
    /// <summary>Strict big-endian UTF-32.</summary>
    Utf32BigEndian,
    /// <summary>The .NET Windows code-page-936 mapping, without best-fit fallback.</summary>
    Gbk,
    /// <summary>The .NET code-page-54936 mapping; not a claim of every GB18030 revision.</summary>
    Gb18030,
    /// <summary>The .NET Windows code-page-950 mapping, without best-fit fallback.</summary>
    Big5,
}

/// <summary>A chosen source codec disagrees with a recognized Unicode BOM.</summary>
public sealed class DocumentEncodingConflictException : IOException
{
    /// <summary>Creates a content-free conflict; no file bytes or path are retained.</summary>
    public DocumentEncodingConflictException(DocumentTextEncoding selected, DocumentTextEncoding detected)
        : base($"The selected encoding ({selected}) conflicts with the file's byte-order mark ({detected}).")
    {
        SelectedEncoding = selected;
        DetectedEncoding = detected;
    }

    /// <summary>The codec explicitly selected by the caller.</summary>
    public DocumentTextEncoding SelectedEncoding { get; }
    /// <summary>The codec unambiguously indicated by the recognized BOM.</summary>
    public DocumentTextEncoding DetectedEncoding { get; }
}

/// <summary>Decoded legacy text cannot reproduce the exact original source bytes.</summary>
public sealed class DocumentEncodingRoundTripException : IOException
{
    /// <summary>Creates a content-free rejection of a non-invertible source mapping.</summary>
    public DocumentEncodingRoundTripException(DocumentTextEncoding selected)
        : base($"The selected encoding ({selected}) cannot reproduce the original bytes. The file has not been opened for editing.") =>
        SelectedEncoding = selected;

    /// <summary>The codec selected for the rejected editable open.</summary>
    public DocumentTextEncoding SelectedEncoding { get; }
}

/// <summary>Creates isolated strict codecs without altering process-wide provider registration.</summary>
internal static class DocumentEncodingPolicy
{
    /// <summary>Returns a fresh strict codec; unknown enum values never become UTF-8.</summary>
    internal static Encoding Create(DocumentTextEncoding selected) => selected switch
    {
        DocumentTextEncoding.Utf8 => new UTF8Encoding(false, true),
        DocumentTextEncoding.Utf16LittleEndian => new UnicodeEncoding(false, false, true),
        DocumentTextEncoding.Utf16BigEndian => new UnicodeEncoding(true, false, true),
        DocumentTextEncoding.Utf32LittleEndian => new UTF32Encoding(false, false, true),
        DocumentTextEncoding.Utf32BigEndian => new UTF32Encoding(true, false, true),
        DocumentTextEncoding.Gbk => Legacy(936),
        DocumentTextEncoding.Gb18030 => Legacy(54936),
        DocumentTextEncoding.Big5 => Legacy(950),
        _ => throw new ArgumentOutOfRangeException(nameof(selected)),
    };

    /// <summary>Only fixed legacy mappings need admission-time inverse-byte verification.</summary>
    internal static bool IsLegacy(DocumentTextEncoding selected) =>
        selected is DocumentTextEncoding.Gbk or DocumentTextEncoding.Gb18030 or DocumentTextEncoding.Big5;

    /// <summary>Maps the existing BOM detector's closed Unicode set to explicit choices.</summary>
    internal static DocumentTextEncoding FromUnicodeCodePage(int codePage) => codePage switch
    {
        65001 => DocumentTextEncoding.Utf8,
        1200 => DocumentTextEncoding.Utf16LittleEndian,
        1201 => DocumentTextEncoding.Utf16BigEndian,
        12000 => DocumentTextEncoding.Utf32LittleEndian,
        12001 => DocumentTextEncoding.Utf32BigEndian,
        _ => throw new InvalidOperationException("The BOM detector returned an unsupported codec."),
    };

    /// <summary>A direct provider lookup avoids registration's code-page-zero / OS-locale side effects.</summary>
    private static Encoding Legacy(int codePage) =>
        CodePagesEncodingProvider.Instance.GetEncoding(codePage,
            EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback) ??
        throw new NotSupportedException("The selected source encoding is unavailable.");
}
