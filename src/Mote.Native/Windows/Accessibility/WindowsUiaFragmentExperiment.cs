using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Mote.Native.Windows.Accessibility;

/// <summary>Four-double screen-space rectangle used by the UIA fragment ABI.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct UiaRect(double Left, double Top, double Width, double Height);

/// <summary>The IRawElementProviderFragment IUnknown vtable, independent of Simple.</summary>
[GeneratedComInterface]
[Guid("f7063da8-8359-439c-9297-bbc5299a7d87")]
internal partial interface IRawElementProviderFragmentAbi
{
    /// <summary>Navigates only within this fragment.</summary>
    [PreserveSig] int Navigate(int direction, out nint provider);
    /// <summary>Gets a stable fragment-local runtime ID.</summary>
    [PreserveSig] int GetRuntimeId(out nint id);
    /// <summary>Gets the element's screen-space rectangle.</summary>
    [PreserveSig] int GetBoundingRectangle(out UiaRect rectangle);
    /// <summary>Gets nested automation-framework roots, if any.</summary>
    [PreserveSig] int GetEmbeddedFragmentRoots(out nint roots);
    /// <summary>Focuses the physical text-input HWND.</summary>
    [PreserveSig] int SetFocus();
    /// <summary>Gets the canvas fragment root.</summary>
    [PreserveSig] int GetFragmentRoot(out nint root);
}

/// <summary>The IRawElementProviderFragmentRoot IUnknown vtable.</summary>
[GeneratedComInterface]
[Guid("620ce2a5-ab8f-40a9-86cb-de3c75599b58")]
internal partial interface IRawElementProviderFragmentRootAbi
{
    /// <summary>Gets the logical document at a screen point.</summary>
    [PreserveSig] int ElementProviderFromPoint(double x, double y, out nint provider);
    /// <summary>Maps physical RichEdit focus to the logical document.</summary>
    [PreserveSig] int GetFocus(out nint provider);
}

/// <summary>The IRawElementProviderHwndOverride IUnknown vtable.</summary>
[GeneratedComInterface]
[Guid("1d5df27c-8947-4425-b8d9-79787bb460b8")]
internal partial interface IRawElementProviderHwndOverrideAbi
{
    /// <summary>Repositions only the known input HWND within this fragment.</summary>
    [PreserveSig] int GetOverrideProviderForHwnd(nint hwnd, out nint provider);
}

/// <summary>Win32 window bounds with four native LONG members.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct UiaWin32Rect(int Left, int Top, int Right, int Bottom);

/// <summary>Native GUITHREADINFO for a focus query against the input HWND's own thread.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct UiaGuiThreadInfo
{
    /// <summary>Required native structure size.</summary>
    internal uint Size;
    /// <summary>GUI thread state flags.</summary>
    internal uint Flags;
    /// <summary>Active window on the target input thread.</summary>
    internal nint Active;
    /// <summary>Focused window on the target input thread.</summary>
    internal nint Focus;
    /// <summary>Mouse-capture window.</summary>
    internal nint Capture;
    /// <summary>Menu owner window.</summary>
    internal nint MenuOwner;
    /// <summary>Window being moved or sized.</summary>
    internal nint MoveSize;
    /// <summary>Native caret owner.</summary>
    internal nint Caret;
    /// <summary>Native caret rectangle.</summary>
    internal UiaWin32Rect CaretRect;
}

/// <summary>
/// Opt-in UIA fragment experiment: a canvas Pane root and one source-backed
/// Document child. The override never intercepts RichEdit WM_GETOBJECT or IME.
/// </summary>
[GeneratedComClass]
internal sealed partial class UiaFragmentRootObject : IRawElementProviderSimpleAbi,
    IRawElementProviderFragmentAbi, IRawElementProviderFragmentRootAbi,
    IRawElementProviderHwndOverrideAbi
{
    private const int E_FAIL = unchecked((int)0x80004005);
    private readonly WindowsTextProviderCore _core;
    private readonly nint _inputHwnd;
    private readonly UiaFragmentDocumentObject _document;
    private nint _canvasHwnd;

    /// <summary>Creates a stable two-node fragment for one canvas/input pair.</summary>
    internal UiaFragmentRootObject(WindowsTextProviderCore core, nint inputHwnd)
    {
        _core = core;
        _inputHwnd = inputHwnd;
        _document = new UiaFragmentDocumentObject(this, core, inputHwnd);
    }

    /// <summary>Exposes the child for focused vtable tests without exposing it to the shell.</summary>
    internal UiaFragmentDocumentObject Document => _document;

    /// <summary>Associates the fragment with its actual canvas HWND.</summary>
    internal void BindWindow(nint hwnd) => _canvasHwnd = hwnd;

    /// <summary>Invalidates retained COM objects while the shared source stays publishable.</summary>
    internal void Detach() => _core.Detach();

    /// <summary>
    /// Checks the input HWND's GUI thread, not the possibly different COM
    /// callback thread. A failed query must not claim logical focus.
    /// </summary>
    internal bool InputHasFocus
    {
        get
        {
            if (_inputHwnd == 0) return false;
            var thread = GetWindowThreadProcessId(_inputHwnd, 0);
            if (thread == 0) return false;
            var info = new UiaGuiThreadInfo { Size = (uint)Marshal.SizeOf<UiaGuiThreadInfo>() };
            return GetGUIThreadInfo(thread, ref info) && info.Focus == _inputHwnd;
        }
    }

    /// <summary>Gets the canvas rectangle for both the Pane and logical Document.</summary>
    internal UiaRect CanvasBounds
    {
        get
        {
            if (_canvasHwnd == 0 || !GetWindowRect(_canvasHwnd, out var native)) return default;
            return new UiaRect(native.Left, native.Top,
                Math.Max(0, native.Right - native.Left), Math.Max(0, native.Bottom - native.Top));
        }
    }

    /// <inheritdoc />
    public int GetProviderOptions(out int options) { options = 2; return 0; }

    /// <inheritdoc />
    public int GetPatternProvider(int patternId, out nint provider)
    {
        provider = 0;
        return _core.IsAttached ? 0 : WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE;
    }

    /// <inheritdoc />
    public int GetPropertyValue(int propertyId, out UiaVariant value)
    {
        value = default;
        if (!_core.IsAttached) return WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE;
        if (propertyId == 30003) { value.Type = 3; value.Integer = 50033; }
        else if (propertyId == 30005)
        {
            value.Type = 8;
            value.Pointer = Marshal.StringToBSTR("Mote canvas");
        }
        else if (propertyId is 30016 or 30009)
        {
            value.Type = 11;
            value.Integer = propertyId == 30016 ? -1 : 0;
        }
        else if (propertyId == 30017) value.Type = 11;
        return 0;
    }

    /// <inheritdoc />
    public int GetHostRawElementProvider(out nint provider)
    {
        provider = 0;
        if (!_core.IsAttached) return WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE;
        return _canvasHwnd == 0 ? 0 : UiaHostProviderFromHwnd(_canvasHwnd, out provider);
    }

    /// <inheritdoc />
    public int Navigate(int direction, out nint provider)
    {
        provider = 0;
        if (!_core.IsAttached) return WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE;
        if (direction is not (3 or 4)) return 0;
        return UiaFragmentPointers.Get(_document, typeof(IRawElementProviderFragmentAbi).GUID,
            out provider);
    }

    /// <inheritdoc />
    public int GetRuntimeId(out nint id)
    {
        id = 0; // HWND-backed fragment roots inherit the window runtime ID.
        return _core.IsAttached ? 0 : WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE;
    }

    /// <inheritdoc />
    public int GetBoundingRectangle(out UiaRect rectangle)
    {
        rectangle = default;
        if (!_core.IsAttached) return WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE;
        rectangle = CanvasBounds;
        return 0;
    }

    /// <inheritdoc />
    public int GetEmbeddedFragmentRoots(out nint roots)
    {
        roots = 0; // No nested automation framework is hosted by this Pane.
        return _core.IsAttached ? 0 : WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE;
    }

    /// <inheritdoc />
    public int SetFocus()
    {
        if (!_core.IsAttached) return WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE;
        if (_inputHwnd == 0) return E_FAIL;
        if (InputHasFocus) return 0;
        // SetFocus only accepts a window attached to the caller's input queue.
        // Never pretend a COM worker thread changed native focus; a later
        // experiment can add a bounded, explicitly reviewed UI-thread dispatch.
        if (GetWindowThreadProcessId(_inputHwnd, 0) != GetCurrentThreadId())
            return WindowsTextResult.UIA_E_INVALIDOPERATION;
        SetFocusHwnd(_inputHwnd);
        return InputHasFocus ? 0 : E_FAIL;
    }

    /// <inheritdoc />
    public int GetFragmentRoot(out nint root) =>
        UiaFragmentPointers.GetAttached(this, _core,
            typeof(IRawElementProviderFragmentRootAbi).GUID, out root);

    /// <inheritdoc />
    public int ElementProviderFromPoint(double x, double y, out nint provider)
    {
        provider = 0;
        if (!_core.IsAttached) return WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE;
        var bounds = CanvasBounds;
        if (x < bounds.Left || y < bounds.Top || x >= bounds.Left + bounds.Width ||
            y >= bounds.Top + bounds.Height) return 0;
        return UiaFragmentPointers.Get(_document, typeof(IRawElementProviderFragmentAbi).GUID,
            out provider);
    }

    /// <inheritdoc />
    public int GetFocus(out nint provider)
    {
        provider = 0;
        if (!_core.IsAttached) return WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE;
        if (!InputHasFocus) return 0;
        return UiaFragmentPointers.Get(_document, typeof(IRawElementProviderFragmentAbi).GUID,
            out provider);
    }

    /// <inheritdoc />
    public int GetOverrideProviderForHwnd(nint hwnd, out nint provider)
    {
        provider = 0;
        if (!_core.IsAttached) return WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE;
        if (hwnd != _inputHwnd || hwnd == 0) return 0;
        return UiaFragmentPointers.Get(_document, typeof(IRawElementProviderSimpleAbi).GUID,
            out provider);
    }

    [LibraryImport("Uiautomationcore.dll")]
    private static partial int UiaHostProviderFromHwnd(nint hwnd, out nint provider);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hwnd, out UiaWin32Rect rectangle);
    [LibraryImport("user32.dll", EntryPoint = "SetFocus")]
    private static partial nint SetFocusHwnd(nint hwnd);
    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hwnd, nint processId);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetGUIThreadInfo(uint threadId, ref UiaGuiThreadInfo info);
    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();
}

/// <summary>One logical source-backed Document child of the experimental Pane.</summary>
[GeneratedComClass]
internal sealed partial class UiaFragmentDocumentObject : IRawElementProviderSimpleAbi,
    IRawElementProviderFragmentAbi, ITextProviderAbi
{
    private readonly UiaFragmentRootObject _root;
    private readonly WindowsTextProviderCore _core;
    private readonly UiaEditorObject _text;
    private readonly nint _inputHwnd;

    /// <summary>Shares the canonical source and bridge-local lifetime with its root.</summary>
    internal UiaFragmentDocumentObject(UiaFragmentRootObject root,
        WindowsTextProviderCore core, nint inputHwnd)
    {
        _root = root;
        _core = core;
        _text = new UiaEditorObject(core);
        _inputHwnd = inputHwnd;
    }

    /// <inheritdoc />
    public int GetProviderOptions(out int options) { options = 2 | 8 | 16; return 0; }

    /// <inheritdoc />
    public int GetPatternProvider(int patternId, out nint provider)
    {
        provider = 0;
        if (!_core.IsAttached) return WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE;
        return patternId == 10014
            ? UiaFragmentPointers.Get(this, typeof(ITextProviderAbi).GUID, out provider) : 0;
    }

    /// <inheritdoc />
    public int GetPropertyValue(int propertyId, out UiaVariant value)
    {
        value = default;
        if (!_core.IsAttached) return WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE;
        if (propertyId == 30003) { value.Type = 3; value.Integer = 50030; }
        else if (propertyId == 30005)
        {
            value.Type = 8;
            value.Pointer = Marshal.StringToBSTR("Mote editor");
        }
        else if (propertyId == 30011)
        {
            value.Type = 8;
            value.Pointer = Marshal.StringToBSTR("mote.source.document");
        }
        else if (propertyId is 30016 or 30017 or 30009)
        {
            value.Type = 11;
            value.Integer = propertyId == 30009 ? (_root.InputHasFocus ? -1 : 0) : -1;
        }
        return 0;
    }

    /// <inheritdoc />
    public int GetHostRawElementProvider(out nint provider)
    {
        provider = 0;
        if (!_core.IsAttached) return WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE;
        return UiaHostProviderFromHwnd(_inputHwnd, out provider);
    }

    /// <inheritdoc />
    public int Navigate(int direction, out nint provider)
    {
        provider = 0;
        if (!_core.IsAttached) return WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE;
        return direction == 0
            ? UiaFragmentPointers.Get(_root, typeof(IRawElementProviderFragmentAbi).GUID,
                out provider) : 0;
    }

    /// <inheritdoc />
    public int GetRuntimeId(out nint id) =>
        _core.IsAttached ? UiaFragmentPointers.RuntimeId(out id)
            : UiaFragmentPointers.Unavailable(out id);

    /// <inheritdoc />
    public int GetBoundingRectangle(out UiaRect rectangle)
    {
        rectangle = default;
        if (!_core.IsAttached) return WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE;
        rectangle = _root.CanvasBounds;
        return 0;
    }

    /// <inheritdoc />
    public int GetEmbeddedFragmentRoots(out nint roots)
    {
        roots = 0;
        return _core.IsAttached ? 0 : WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE;
    }

    /// <inheritdoc />
    public int SetFocus() => _root.SetFocus();

    /// <inheritdoc />
    public int GetFragmentRoot(out nint root) =>
        UiaFragmentPointers.GetAttached(_root, _core,
            typeof(IRawElementProviderFragmentRootAbi).GUID, out root);

    /// <inheritdoc />
    public int GetSelection(out nint ranges) => _text.GetSelection(out ranges);
    /// <inheritdoc />
    public int GetVisibleRanges(out nint ranges) => _text.GetVisibleRanges(out ranges);
    /// <inheritdoc />
    public int RangeFromChild(nint child, out nint range) => _text.RangeFromChild(child, out range);
    /// <inheritdoc />
    public int RangeFromPoint(UiaPoint point, out nint range) => _text.RangeFromPoint(point, out range);
    /// <inheritdoc />
    public int GetDocumentRange(out nint range) => _text.GetDocumentRange(out range);
    /// <inheritdoc />
    public int GetSupportedTextSelection(out int selection) => _text.GetSupportedTextSelection(out selection);

    [LibraryImport("Uiautomationcore.dll")]
    private static partial int UiaHostProviderFromHwnd(nint hwnd, out nint provider);
}

/// <summary>COM pointer and fragment-local runtime-ID helpers with owned results.</summary>
internal static partial class UiaFragmentPointers
{
    /// <summary>Returns an owned interface pointer or a failing HRESULT.</summary>
    internal static int Get(object value, Guid iid, out nint pointer)
    {
        pointer = 0;
        try { pointer = UiaComInterface.Pointer(value, iid); return 0; }
        catch (OutOfMemoryException) { return WindowsTextResult.E_OUTOFMEMORY; }
        catch { return unchecked((int)0x80004005); }
    }

    /// <summary>Rejects calls against a detached provider before issuing a pointer.</summary>
    internal static int GetAttached(object value, WindowsTextProviderCore core, Guid iid,
        out nint pointer) => core.IsAttached ? Get(value, iid, out pointer) : Unavailable(out pointer);

    /// <summary>Sets a null out-pointer for a detached element.</summary>
    internal static int Unavailable(out nint pointer)
    {
        pointer = 0;
        return WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE;
    }

    /// <summary>Creates [UiaAppendRuntimeId, 1] for the only document child.</summary>
    internal static int RuntimeId(out nint array)
    {
        array = SafeArrayCreateVector(3, 0, 2);
        if (array == 0) return WindowsTextResult.E_OUTOFMEMORY;
        for (var i = 0; i < 2; i++)
        {
            var value = i == 0 ? 3 : 1;
            var index = i;
            var hr = SafeArrayPutElement(array, ref index, ref value);
            if (hr >= 0) continue;
            SafeArrayDestroy(array);
            array = 0;
            return hr;
        }
        return 0;
    }

    [LibraryImport("oleaut32.dll")]
    private static partial nint SafeArrayCreateVector(ushort variantType, int lowerBound, uint elements);
    [LibraryImport("oleaut32.dll")]
    private static partial int SafeArrayPutElement(nint array, ref int index, ref int element);
    [LibraryImport("oleaut32.dll")]
    private static partial int SafeArrayDestroy(nint array);
}
