using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Mote.Native.Windows;

/// <summary>
/// Synchronous whole-control Unicode import for the full-source capability
/// experiment. This is never an ordinary per-keystroke replacement path.
/// </summary>
internal static unsafe class WindowsNativeTextImporter
{
    /// <summary>EM_STREAMIN and plain Unicode flags from four-byte-packed Richedit.h.</summary>
    private const int StreamIn = 0x0400 + 73;
    private const nuint UnicodeText = 0x0001 | 0x0010;
    /// <summary>ERROR_INVALID_PARAMETER; callback errors never escape as managed exceptions.</summary>
    private const uint InvalidParameter = 87;

    /// <summary>
    /// Replaces the complete control with admitted NUL-free display text. The
    /// owner must separately verify native readback and retain Engine history.
    /// </summary>
    /// <remarks>
    /// Caller owns the HWND on the UI thread and admits CRLF display semantics.
    /// Neither the pinned string nor stack cursor survives SendMessageW. A
    /// failed import may leave a partial native replica; do not publish it.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    internal static void Install(nint control, string display)
    {
        if (control == 0) throw new ArgumentOutOfRangeException(nameof(control));
        ArgumentNullException.ThrowIfNull(display);
        if (!BitConverter.IsLittleEndian)
            throw new PlatformNotSupportedException("RichEdit Unicode streams require little-endian UTF-16.");
        if (display.Contains('\0'))
            throw new ArgumentException("RichEdit source import requires NUL-free text.", nameof(display));

        fixed (char* source = display)
        {
            var cursor = new Cursor { Source = (nint)source, ByteLength = (long)display.Length * 2 };
            var stream = new EditStream { Cookie = (nuint)(&cursor), Callback = CallbackAddress };
            var characters = Win32.SendMessageW(control, StreamIn, UnicodeText, (nint)(&stream));
            VerifyCompletion(stream.Error, characters, cursor);
        }
    }

    /// <summary>SDK EDITSTREAM is packed to four bytes even on 64-bit Windows.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct EditStream
    {
        /// <summary>Borrowed stack cursor address, valid only during synchronous SendMessageW.</summary>
        internal nuint Cookie;
        /// <summary>Native or callback DWORD error output; zero is necessary, not full-content proof.</summary>
        internal uint Error;
        /// <summary>Static AOT-compatible Stdcall function pointer, not a generated delegate thunk.</summary>
        internal nint Callback;
    }

    /// <summary>Private transfer state; long byte counts avoid length-times-two int overflow.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Cursor
    {
        /// <summary>Borrowed pinned UTF-16 source, never a terminator or encoded-copy allocation.</summary>
        internal nint Source;
        /// <summary>Exact even source byte count excluding the string terminator.</summary>
        internal long ByteLength;
        /// <summary>Even consumed byte count; monotonically advances only after successful copying.</summary>
        internal long Position;
    }

    /// <summary>Actual ABI entry point, also exercised by portable unmanaged callback tests.</summary>
    internal static nint CallbackAddress =>
        (nint)(delegate* unmanaged[Stdcall]<nuint, byte*, int, int*, uint>)&Read;

    /// <summary>
    /// Copies complete UTF-16 code units without allocation or normalization.
    /// All nonnull pointers belong to our synchronous stream transaction; this
    /// is not a validator for arbitrary untrusted addresses.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint Read(nuint cookie, byte* buffer, int capacity, int* copied)
    {
        if (copied == null) return InvalidParameter;
        *copied = 0;
        if (cookie == 0 || capacity < 0) return InvalidParameter;
        var cursor = (Cursor*)cookie;
        if (cursor->ByteLength < 0 || cursor->Position < 0 ||
            cursor->Position > cursor->ByteLength ||
            ((cursor->ByteLength | cursor->Position) & 1) != 0)
            return InvalidParameter;
        if (cursor->Position == cursor->ByteLength) return 0;
        if (buffer == null || cursor->Source == 0 || capacity < 2) return InvalidParameter;
        // Odd capacities leave one unused byte. Transport chunks may split a
        // surrogate pair, but never a code unit, and no bytes are substituted.
        var count = (int)Math.Min(cursor->ByteLength - cursor->Position, capacity & ~1);
        Buffer.MemoryCopy((byte*)cursor->Source + cursor->Position, buffer, capacity, count);
        cursor->Position += count;
        *copied = count;
        return 0;
    }

    /// <summary>Checks necessary transport success; exact text is certified by later native readback.</summary>
    internal static void VerifyCompletion(uint error, nint characters, Cursor cursor)
    {
        if (error != 0 || characters < 0 || cursor.ByteLength < 0 ||
            (cursor.ByteLength & 1) != 0 || cursor.Position != cursor.ByteLength)
            throw new InvalidOperationException($"RichEdit Unicode import failed (stream error {error}).");
    }
}
