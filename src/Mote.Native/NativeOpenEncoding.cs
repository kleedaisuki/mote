using Mote.Engine;

namespace Mote.Native;

/// <summary>Optional explicit-encoding UI; ordinary shells and existing test adapters need not implement it.</summary>
internal interface INativeOpenEncodingShell
{
    /// <summary>Requests an explicit open without changing the ordinary Open command or shortcut.</summary>
    event Action? OpenWithEncodingRequested;

    /// <summary>
    /// Returns a deliberately selected codec, or null on cancellation. The chooser
    /// does not read files, mutate documents, guess encodings, or emit content telemetry.
    /// </summary>
    DocumentTextEncoding? ChooseOpenEncoding();
}

/// <summary>One closed, stable chooser order shared by OS-native dialogs.</summary>
internal static class NativeOpenEncodingChoices
{
    /// <summary>Display labels identify the actual .NET mappings, not guessed file metadata.</summary>
    internal static IReadOnlyList<(DocumentTextEncoding Encoding, string Label)> All { get; } =
        Array.AsReadOnly(new[]
        {
            (DocumentTextEncoding.Utf8, "UTF-8"),
            (DocumentTextEncoding.Utf16LittleEndian, "UTF-16 Little Endian"),
            (DocumentTextEncoding.Utf16BigEndian, "UTF-16 Big Endian"),
            (DocumentTextEncoding.Utf32LittleEndian, "UTF-32 Little Endian"),
            (DocumentTextEncoding.Utf32BigEndian, "UTF-32 Big Endian"),
            (DocumentTextEncoding.Gbk, "GBK (.NET code page 936)"),
            (DocumentTextEncoding.Gb18030, "GB18030 (.NET code page 54936)"),
            (DocumentTextEncoding.Big5, "Big5 (.NET code page 950)"),
        });
}
