using System.Runtime.InteropServices;
using Mote.Engine;
using Mote.Native;
using Mote.Native.Accessibility;
using Mote.Native.Viewport;
using Mote.Native.Windows.Accessibility;

namespace Mote.Tests;

/// <summary>Exercises source-generated UIA COM vtables on a real Windows runtime.</summary>
public sealed class WindowsUiaBridgePrototypeTests
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDocumentRangeDelegate(nint self, out nint range);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetTextDelegate(nint self, int maxLength, out nint bstr);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetRangesDelegate(nint self, out nint array);

    /// <summary>Generated COM pointers expose an offscreen source range and bounded BSTR.</summary>
    [Fact]
    public void Generated_com_vtable_reads_offscreen_engine_text()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var document = new Document("first\n" + new string('x', 80_000) + "\nEND😀");
        var snapshot = document.Snapshot;
        var frame = new CanvasFrame(snapshot.Version, new ViewportAnchor(0, 0), 0,
            [new ViewportSlice(0, 0, 5, 0, 16, false, false)], 0, 0);
        var binding = new NativeCanvasBinding(1, snapshot.Version, 1, snapshot, frame,
            0, "", 0, 0, "Test", "", false);
        var core = new WindowsTextProviderCore(new AccessibleDocument(binding), new ViewportStub());
        var editor = new UiaEditorObject(core);
        var provider = UiaComInterface.Pointer(editor, typeof(ITextProviderAbi).GUID);
        try
        {
            var getRange = Marshal.GetDelegateForFunctionPointer<GetDocumentRangeDelegate>(
                Slot(provider, 7));
            var getSelection = Marshal.GetDelegateForFunctionPointer<GetRangesDelegate>(Slot(provider, 3));
            Assert.Equal(0, getSelection(provider, out var selection));
            try
            {
                Assert.Equal(0, SafeArrayGetLBound(selection, 1, out var first));
                Assert.Equal(0, SafeArrayGetUBound(selection, 1, out var last));
                Assert.Equal(0, first);
                Assert.Equal(0, last);
                Assert.Equal(0, SafeArrayGetElement(selection, ref first, out var selectedRange));
                Marshal.Release(selectedRange);
            }
            finally { SafeArrayDestroy(selection); }
            Assert.Equal(0, getRange(provider, out var range));
            Assert.NotEqual(0, range);
            try
            {
                var getText = Marshal.GetDelegateForFunctionPointer<GetTextDelegate>(Slot(range, 12));
                Assert.Equal(WindowsTextResult.E_OUTOFMEMORY,
                    getText(range, -1, out var oversized));
                Assert.Equal(0, oversized);
                Assert.Equal(0, getText(range, 10, out var bstr));
                try { Assert.Equal("first\nxxxx", Marshal.PtrToStringBSTR(bstr)); }
                finally { Marshal.FreeBSTR(bstr); }
                core.Invalidate();
                Assert.Equal(WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE,
                    getText(range, 10, out var closedText));
                Assert.Equal(0, closedText);
            }
            finally { Marshal.Release(range); }
        }
        finally { Marshal.Release(provider); }
    }

    private static nint Slot(nint instance, int index) =>
        Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), index * IntPtr.Size);

    [DllImport("oleaut32.dll")]
    private static extern int SafeArrayGetLBound(nint array, uint dimension, out int bound);
    [DllImport("oleaut32.dll")]
    private static extern int SafeArrayGetUBound(nint array, uint dimension, out int bound);
    [DllImport("oleaut32.dll")]
    private static extern int SafeArrayGetElement(nint array, ref int index, out nint element);
    [DllImport("oleaut32.dll")]
    private static extern int SafeArrayDestroy(nint array);

    private sealed class ViewportStub : IAccessibleViewport
    {
        public bool TryScrollIntoView(AccessibleRange range, bool alignToTop) => true;
    }
}
