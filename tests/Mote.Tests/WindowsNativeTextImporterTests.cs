using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mote.Native.Windows;

// Only callback transport and pre-P/Invoke admission are exercised. No HWND,
// control, library load, GUI, input or Windows message is created by these tests.
#pragma warning disable CA1416

namespace Mote.Tests;

/// <summary>Portable tests of the actual Unicode stream callback and SDK ABI.</summary>
public sealed class WindowsNativeTextImporterTests
{
    /// <summary>Matches SDK DWORD CALLBACK(DWORD_PTR, LPBYTE, LONG, LONG*).</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint ReadCallback(nuint cookie, nint buffer, int capacity, nint copied);

    /// <summary>RichEdit packing differs from ordinary 64-bit pointer alignment.</summary>
    [Fact]
    public void EditStreamMatchesFourBytePackedSdk()
    {
        Assert.Equal(4, typeof(WindowsNativeTextImporter.EditStream).StructLayoutAttribute!.Pack);
        Assert.Equal(0, Marshal.OffsetOf<WindowsNativeTextImporter.EditStream>("Cookie").ToInt32());
        Assert.Equal(IntPtr.Size, Marshal.OffsetOf<WindowsNativeTextImporter.EditStream>("Error").ToInt32());
        Assert.Equal(IntPtr.Size + 4, Marshal.OffsetOf<WindowsNativeTextImporter.EditStream>("Callback").ToInt32());
        Assert.Equal(2 * IntPtr.Size + 4, Marshal.SizeOf<WindowsNativeTextImporter.EditStream>());
        Assert.Equal(typeof(nuint), typeof(WindowsNativeTextImporter.EditStream)
            .GetField("Cookie", BindingFlags.Instance | BindingFlags.NonPublic)!.FieldType);
    }

    /// <summary>AOT callback is static unmanaged Stdcall, with exact Windows-width scalar types.</summary>
    [Fact]
    public void CallbackMetadataMatchesSdkWithoutDelegateThunk()
    {
        var method = typeof(WindowsNativeTextImporter).GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic)!;
        var attribute = method.GetCustomAttribute<UnmanagedCallersOnlyAttribute>()!;
        Assert.Equal(new[] { typeof(CallConvStdcall) }, attribute.CallConvs);
        Assert.Equal(typeof(uint), method.ReturnType);
        Assert.Equal(new[] { typeof(nuint), typeof(byte).MakePointerType(), typeof(int), typeof(int).MakePointerType() },
            method.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.NotEqual(0, WindowsNativeTextImporter.CallbackAddress);
    }

    /// <summary>Even and odd callback capacities reconstruct literal code units, with no terminator or BOM insertion.</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(256)]
    [InlineData(4095)]
    public void ChunkedTransportPreservesExactUtf16Bytes(int capacity)
    {
        var text = "A中文😀e\u0301\r\nB\r\n\uFEFF\u0627\u05D0" + new string(['\uD800', 'x', '\uDC00']);
        var expected = LittleEndianBytes(text);
        using var lease = new CallbackLease(expected, capacity);
        var observed = new List<byte>();
        while (lease.Cursor.Position < lease.Cursor.ByteLength)
        {
            Assert.Equal(0u, lease.Read(capacity));
            var copied = Marshal.ReadInt32(lease.Count);
            Assert.InRange(copied, 2, capacity);
            Assert.Equal(0, copied & 1);
            var bytes = new byte[copied];
            Marshal.Copy(lease.Buffer, bytes, 0, copied);
            observed.AddRange(bytes);
            lease.AssertGuards();
        }
        Assert.Equal(expected, observed);
        Assert.Equal(expected.Length, lease.Cursor.Position);
        Assert.Equal(0u, lease.Read(capacity));
        Assert.Equal(0, Marshal.ReadInt32(lease.Count));
        Assert.Equal(expected.Length, lease.Cursor.Position);
    }

    /// <summary>EOF accepts an empty source and does not dereference the absent source or output buffer.</summary>
    [Fact]
    public void EmptySourceReportsActualEof()
    {
        using var lease = new CallbackLease([], 2);
        lease.SetCursor(new() { Source = 0, ByteLength = 0, Position = 0 });
        Assert.Equal(0u, lease.Read(0, buffer: 0));
        Assert.Equal(0, Marshal.ReadInt32(lease.Count));
        Assert.Equal(0, lease.Cursor.Position);
        lease.AssertGuards();
    }

    /// <summary>Small/negative capacities fail before progress; they cannot fabricate healthy EOF.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void NonProgressRequestIsErrorWithZeroOutput(int capacity)
    {
        using var lease = new CallbackLease(LittleEndianBytes("abc"), 4);
        Assert.Equal(87u, lease.Read(capacity));
        Assert.Equal(0, Marshal.ReadInt32(lease.Count));
        Assert.Equal(0, lease.Cursor.Position);
        lease.AssertGuards();
    }

    /// <summary>Null callback arguments fail without consuming data or raising managed exceptions.</summary>
    [Fact]
    public void NullArgumentsFailWithoutConsumption()
    {
        using var lease = new CallbackLease(LittleEndianBytes("abc"), 4);
        Assert.Equal(87u, lease.Read(4, cookie: 0));
        Assert.Equal(0, Marshal.ReadInt32(lease.Count));
        Assert.Equal(87u, lease.Read(4, buffer: 0));
        Assert.Equal(0, Marshal.ReadInt32(lease.Count));
        Assert.Equal(87u, lease.Read(4, count: 0));
        Assert.Equal(0, lease.Cursor.Position);
        lease.SetCursor(new() { Source = 0, ByteLength = 6 });
        Assert.Equal(87u, lease.Read(4));
        Assert.Equal(0, Marshal.ReadInt32(lease.Count));
        lease.AssertGuards();
    }

    /// <summary>Invalid cursor bounds/alignment are refused before any pointer arithmetic or copy.</summary>
    [Theory]
    [InlineData(-2, 0)]
    [InlineData(6, -2)]
    [InlineData(6, 8)]
    [InlineData(5, 0)]
    [InlineData(6, 1)]
    public void InvalidCursorIsError(long length, long position)
    {
        using var lease = new CallbackLease(LittleEndianBytes("abc"), 4);
        var cursor = lease.Cursor;
        cursor.ByteLength = length;
        cursor.Position = position;
        lease.SetCursor(cursor);
        Assert.Equal(87u, lease.Read(4));
        Assert.Equal(0, Marshal.ReadInt32(lease.Count));
        Assert.Equal(position, lease.Cursor.Position);
        lease.AssertGuards();
    }

    /// <summary>Native error, incomplete transfer and invalid count can never certify installation.</summary>
    [Fact]
    public void CompletionRequiresErrorFreeCompleteTransport()
    {
        var cursor = new WindowsNativeTextImporter.Cursor { ByteLength = 6, Position = 6 };
        WindowsNativeTextImporter.VerifyCompletion(0, 2, cursor); // Paragraph count is not display length.
        WindowsNativeTextImporter.VerifyCompletion(0, 0, default); // Empty control is valid.
        Assert.Throws<InvalidOperationException>(() => WindowsNativeTextImporter.VerifyCompletion(87, 3, cursor));
        Assert.Throws<InvalidOperationException>(() => WindowsNativeTextImporter.VerifyCompletion(0, -1, cursor));
        cursor.Position = 4;
        Assert.Throws<InvalidOperationException>(() => WindowsNativeTextImporter.VerifyCompletion(0, 3, cursor));
        cursor.ByteLength = cursor.Position = 5;
        Assert.Throws<InvalidOperationException>(() => WindowsNativeTextImporter.VerifyCompletion(0, 3, cursor));
        cursor.ByteLength = cursor.Position = -2;
        Assert.Throws<InvalidOperationException>(() => WindowsNativeTextImporter.VerifyCompletion(0, 3, cursor));
        cursor.ByteLength = cursor.Position = 2L * int.MaxValue;
        WindowsNativeTextImporter.VerifyCompletion(0, int.MaxValue, cursor);
    }

    /// <summary>Unsafe control/source inputs are rejected before sending any message.</summary>
    [Fact]
    public void ImportAdmissionDoesNotReachNativeForInvalidInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowsNativeTextImporter.Install(0, "text"));
        Assert.Throws<ArgumentNullException>(() => WindowsNativeTextImporter.Install(1, null!));
        Assert.Throws<ArgumentException>(() => WindowsNativeTextImporter.Install(1, "a\0b"));
    }

    /// <summary>Builds literal UTF-16LE bytes without decoder replacement of malformed code units.</summary>
    private static byte[] LittleEndianBytes(string text) =>
        text.SelectMany(character => new[] { (byte)character, (byte)(character >> 8) }).ToArray();

    /// <summary>Owns private unmanaged test memory, never an operating-system text control.</summary>
    private sealed class CallbackLease : IDisposable
    {
        /// <summary>Static production entry point invoked through its actual unmanaged ABI.</summary>
        private readonly ReadCallback _callback = Marshal.GetDelegateForFunctionPointer<ReadCallback>(WindowsNativeTextImporter.CallbackAddress);
        /// <summary>Owned allocations remain live until all synchronous callback calls finish.</summary>
        private readonly nint _source, _bufferAllocation;
        /// <summary>Requested output buffer capacity excluding two guard bytes.</summary>
        private readonly int _capacity;
        /// <summary>Cookie points to a privately owned mutable production cursor.</summary>
        internal nint Cookie { get; }
        /// <summary>Transferred-byte output begins with a nonzero sentinel before every call.</summary>
        internal nint Count { get; }
        /// <summary>Buffer has one untouched guard byte at each side.</summary>
        internal nint Buffer => _bufferAllocation + 1;
        /// <summary>Reads the cursor actually mutated by the production callback.</summary>
        internal WindowsNativeTextImporter.Cursor Cursor => Marshal.PtrToStructure<WindowsNativeTextImporter.Cursor>(Cookie);

        /// <summary>Allocates literal source and guarded destination for one pure transport scenario.</summary>
        internal CallbackLease(byte[] source, int capacity)
        {
            _capacity = capacity;
            _source = Marshal.AllocHGlobal(Math.Max(source.Length, 1));
            _bufferAllocation = Marshal.AllocHGlobal(capacity + 2);
            Cookie = Marshal.AllocHGlobal(Marshal.SizeOf<WindowsNativeTextImporter.Cursor>());
            Count = Marshal.AllocHGlobal(sizeof(int));
            Marshal.Copy(source, 0, _source, source.Length);
            Marshal.WriteByte(_bufferAllocation, 0xA5);
            Marshal.WriteByte(_bufferAllocation + capacity + 1, 0x5A);
            SetCursor(new() { Source = _source, ByteLength = source.Length });
        }

        /// <summary>Writes an intentionally valid or invalid cursor without introducing a different callback implementation.</summary>
        internal void SetCursor(WindowsNativeTextImporter.Cursor cursor) => Marshal.StructureToPtr(cursor, Cookie, false);

        /// <summary>Invokes the actual callback with optional null-pointer failure cases.</summary>
        internal uint Read(int capacity, nint? cookie = null, nint? buffer = null, nint? count = null)
        {
            Marshal.WriteInt32(Count, 999);
            return _callback((nuint)(cookie ?? Cookie), buffer ?? Buffer, capacity, count ?? Count);
        }

        /// <summary>Detects writes outside the capacity granted by the simulated RichEdit caller.</summary>
        internal void AssertGuards()
        {
            Assert.Equal(0xA5, Marshal.ReadByte(_bufferAllocation));
            Assert.Equal(0x5A, Marshal.ReadByte(_bufferAllocation + _capacity + 1));
        }

        /// <summary>Releases only allocations made by this test lease.</summary>
        public void Dispose()
        {
            Marshal.FreeHGlobal(Count);
            Marshal.FreeHGlobal(Cookie);
            Marshal.FreeHGlobal(_bufferAllocation);
            Marshal.FreeHGlobal(_source);
        }
    }
}
