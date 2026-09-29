using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;
using Mote.Native.Accessibility;

namespace Mote.Native.Windows.Accessibility;

/// <summary>Blittable VARIANT storage for the x64/arm64 Windows UIA ABI.</summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct UiaVariant
{
    /// <summary>VARTYPE discriminant, such as VT_I4 or VT_BSTR.</summary>
    [FieldOffset(0)] internal ushort Type;
    /// <summary>32-bit value of a VT_I4 or VT_BOOL variant.</summary>
    [FieldOffset(8)] internal int Integer;
    /// <summary>BSTR payload of a VT_BSTR variant.</summary>
    [FieldOffset(8)] internal nint Pointer;
}

/// <summary>UIA screen-space point with the native two-double layout.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct UiaPoint(double X, double Y);

/// <summary>The exact IRawElementProviderSimple IUnknown vtable.</summary>
[GeneratedComInterface]
[Guid("d6dd68d1-86fd-4332-8666-9abedea2d24c")]
internal partial interface IRawElementProviderSimpleAbi
{
    /// <summary>Gets provider category flags.</summary>
    [PreserveSig] int GetProviderOptions(out int options);
    /// <summary>Gets a pattern interface pointer with an owned COM reference.</summary>
    [PreserveSig] int GetPatternProvider(int patternId, out nint provider);
    /// <summary>Gets a native VARIANT property value.</summary>
    [PreserveSig] int GetPropertyValue(int propertyId, out UiaVariant value);
    /// <summary>Gets an optional host provider pointer.</summary>
    [PreserveSig] int GetHostRawElementProvider(out nint provider);
}

/// <summary>The exact ITextProvider IUnknown vtable, including UIA range arrays.</summary>
[GeneratedComInterface]
[Guid("3589c92c-63f3-4367-99bb-ada653b77cf2")]
internal partial interface ITextProviderAbi
{
    /// <summary>Gets one or more selected text ranges.</summary>
    [PreserveSig] int GetSelection(out nint ranges);
    /// <summary>Gets contiguous painted source ranges.</summary>
    [PreserveSig] int GetVisibleRanges(out nint ranges);
    /// <summary>Maps a child element to source text, unsupported in this probe.</summary>
    [PreserveSig] int RangeFromChild(nint child, out nint range);
    /// <summary>Maps a screen-space point to source text, pending canvas hit testing.</summary>
    [PreserveSig] int RangeFromPoint(UiaPoint point, out nint range);
    /// <summary>Gets a range spanning the entire canonical engine snapshot.</summary>
    [PreserveSig] int GetDocumentRange(out nint range);
    /// <summary>Reports one global selection rather than island-local selection.</summary>
    [PreserveSig] int GetSupportedTextSelection(out int selection);
}

/// <summary>The exact ITextRangeProvider vtable; unimplemented slots fail explicitly.</summary>
[GeneratedComInterface]
[Guid("5347ad7b-c355-46f8-aff5-909033582f63")]
internal partial interface ITextRangeProviderAbi
{
    /// <summary>Clones a range.</summary>
    [PreserveSig] int Clone(out nint range);
    /// <summary>Compares ranges.</summary>
    [PreserveSig] int Compare(nint other, out int equal);
    /// <summary>Compares endpoint positions.</summary>
    [PreserveSig] int CompareEndpoints(int endpoint, nint other, int otherEndpoint, out int result);
    /// <summary>Expands to a text unit.</summary>
    [PreserveSig] int ExpandToEnclosingUnit(int unit);
    /// <summary>Finds a text attribute.</summary>
    [PreserveSig] int FindAttribute(int attributeId, UiaVariant value, int backward, out nint range);
    /// <summary>Finds text.</summary>
    [PreserveSig] int FindText(nint text, int backward, int ignoreCase, out nint range);
    /// <summary>Gets a text attribute.</summary>
    [PreserveSig] int GetAttributeValue(int attributeId, out UiaVariant value);
    /// <summary>Gets visible bounding rectangles.</summary>
    [PreserveSig] int GetBoundingRectangles(out nint rectangles);
    /// <summary>Gets the enclosing editor element.</summary>
    [PreserveSig] int GetEnclosingElement(out nint element);
    /// <summary>Reads source text exactly or fails on over-budget requests.</summary>
    [PreserveSig] int GetText(int maxLength, out nint bstr);
    /// <summary>Moves a range by text units.</summary>
    [PreserveSig] int Move(int unit, int count, out int moved);
    /// <summary>Moves an endpoint by text units.</summary>
    [PreserveSig] int MoveEndpointByUnit(int endpoint, int unit, int count, out int moved);
    /// <summary>Moves an endpoint to another range.</summary>
    [PreserveSig] int MoveEndpointByRange(int endpoint, nint target, int targetEndpoint);
    /// <summary>Selects the range.</summary>
    [PreserveSig] int Select();
    /// <summary>Adds a disjoint selection, unsupported by the single-selection editor.</summary>
    [PreserveSig] int AddToSelection();
    /// <summary>Removes a selection.</summary>
    [PreserveSig] int RemoveFromSelection();
    /// <summary>Requests source-viewport reveal on the controller's UI thread.</summary>
    [PreserveSig] int ScrollIntoView(int alignToTop);
    /// <summary>Gets nested text children.</summary>
    [PreserveSig] int GetChildren(out nint children);
}

/// <summary>
/// Opt-in Win32 UIA registration probe. The experimental canvas shell calls
/// HandleGetObject from WM_GETOBJECT and Close from WM_DESTROY; default page
/// editing does not use this incomplete provider.
/// </summary>
/// <remarks>
/// This prototype sketches an AOT-compatible COM registration path and exact
/// source GetText/ScrollIntoView plumbing. Its text pattern is intentionally
/// incomplete (word/character movement, hit-test, geometry and events return
/// E_NOTIMPL), so it must not be enabled in the default editor or counted as a
/// screen-reader pass. Test x64/arm64 ABI and COM lifetime before integration.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsUiaBridgePrototype
{
    private readonly UiaEditorObject _provider;

    /// <summary>Creates one provider object for one engine-backed editor.</summary>
    internal WindowsUiaBridgePrototype(AccessibleDocument document, IAccessibleViewport viewport)
    {
        _provider = new UiaEditorObject(new WindowsTextProviderCore(document, viewport));
    }

    /// <summary>Returns a WM_GETOBJECT result using a source-generated COM callable wrapper.</summary>
    internal nint HandleGetObject(nint hwnd, nuint wParam, nint lParam)
    {
        _provider.BindWindow(hwnd);
        var pointer = UiaComInterface.Pointer(_provider, typeof(IRawElementProviderSimpleAbi).GUID);
        try { return UiaReturnRawElementProvider(hwnd, wParam, lParam, pointer); }
        finally { Marshal.Release(pointer); }
    }

    /// <summary>Releases UIA's HWND-to-provider event map when the window is destroyed.</summary>
    internal void Close(nint hwnd)
    {
        DetachRegistration(hwnd);
    }

    /// <summary>
    /// Removes this HWND's UIA registration after a provider fault without
    /// invalidating the controller-shared document or disabling native input.
    /// Retained COM ranges from this bridge become unavailable immediately.
    /// </summary>
    internal void DetachRegistration(nint hwnd)
    {
        _provider.Detach();
        UiaReturnRawElementProvider(hwnd, 0, 0, 0);
        _provider.BindWindow(0);
    }

    [LibraryImport("Uiautomationcore.dll")]
    private static partial nint UiaReturnRawElementProvider(nint hwnd, nuint wParam,
        nint lParam, nint provider);
}

/// <summary>Source-generated COM pointer and SAFEARRAY helpers, avoiding runtime RCWs.</summary>
internal static partial class UiaComInterface
{
    private static readonly StrategyBasedComWrappers Wrappers = new();
    private const int E_OUTOFMEMORY = unchecked((int)0x8007000E);
    private const int E_FAIL = unchecked((int)0x80004005);

    /// <summary>Returns an owned pointer to a generated COM interface.</summary>
    internal static nint Pointer(object value, Guid interfaceId)
    {
        var unknown = Wrappers.GetOrCreateComInterfaceForObject(value, CreateComInterfaceFlags.None);
        try
        {
            var id = interfaceId;
            var hr = Marshal.QueryInterface(unknown, in id, out var pointer);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
            return pointer;
        }
        finally { Marshal.Release(unknown); }
    }

    /// <summary>Builds a SAFEARRAY(VT_UNKNOWN) of source range COM pointers.</summary>
    internal static int Ranges(WindowsTextProviderCore core,
        IReadOnlyList<AccessibleRange> ranges, out nint array)
    {
        array = SafeArrayCreateVector(13, 0, (uint)ranges.Count);
        if (array == 0) return E_OUTOFMEMORY;
        for (var i = 0; i < ranges.Count; i++)
        {
            nint pointer = 0;
            try
            {
                pointer = Pointer(new UiaRangeObject(core, ranges[i]),
                    typeof(ITextRangeProviderAbi).GUID);
                var index = i;
                var hr = SafeArrayPutElement(array, ref index, pointer);
                if (hr >= 0) continue;
                SafeArrayDestroy(array);
                array = 0;
                return hr;
            }
            catch (OutOfMemoryException)
            {
                SafeArrayDestroy(array);
                array = 0;
                return E_OUTOFMEMORY;
            }
            catch
            {
                SafeArrayDestroy(array);
                array = 0;
                return E_FAIL;
            }
            finally { if (pointer != 0) Marshal.Release(pointer); }
        }
        return 0;
    }

    [LibraryImport("oleaut32.dll")]
    private static partial nint SafeArrayCreateVector(ushort variantType, int lowerBound, uint elements);
    [LibraryImport("oleaut32.dll")]
    private static partial int SafeArrayPutElement(nint array, ref int index, nint element);
    [LibraryImport("oleaut32.dll")]
    private static partial int SafeArrayDestroy(nint array);
}

/// <summary>COM-visible editor element exposing a source-backed Text pattern.</summary>
[GeneratedComClass]
internal sealed partial class UiaEditorObject : IRawElementProviderSimpleAbi, ITextProviderAbi
{
    private const int E_INVALIDARG = unchecked((int)0x80070057);
    private const int E_NOTIMPL = unchecked((int)0x80004001);
    private readonly WindowsTextProviderCore _core;
    private nint _window;

    /// <summary>Captures the source provider, not the bounded input island.</summary>
    internal UiaEditorObject(WindowsTextProviderCore core) => _core = core;

    /// <summary>Rejects retained COM range reads from this detached bridge.</summary>
    internal void Detach() => _core.Detach();

    /// <summary>Binds the HWND whose default provider supplies native window metadata.</summary>
    internal void BindWindow(nint window) => _window = window;

    /// <inheritdoc />
    public int GetProviderOptions(out int options) { options = 2; return 0; }

    /// <inheritdoc />
    public int GetPatternProvider(int patternId, out nint provider)
    {
        if (!_core.IsAttached)
        {
            provider = 0;
            return WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE;
        }
        provider = patternId == 10014
            ? UiaComInterface.Pointer(this, typeof(ITextProviderAbi).GUID) : 0;
        return 0;
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
        else if (propertyId is 30016 or 30017 or 30009)
        {
            value.Type = 11;
            value.Integer = -1;
        }
        return 0;
    }

    /// <inheritdoc />
    public int GetHostRawElementProvider(out nint provider)
    {
        if (!_core.IsAttached)
        {
            provider = 0;
            return WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE;
        }
        if (_window == 0) { provider = 0; return 0; }
        return UiaHostProviderFromHwnd(_window, out provider);
    }

    [LibraryImport("Uiautomationcore.dll")]
    private static partial int UiaHostProviderFromHwnd(nint hwnd, out nint provider);

    /// <inheritdoc />
    public int GetSelection(out nint ranges)
    {
        ranges = 0;
        try { return UiaComInterface.Ranges(_core, [_core.Selection], out ranges); }
        catch (InvalidOperationException) { return WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE; }
    }

    /// <inheritdoc />
    public int GetVisibleRanges(out nint ranges)
    {
        ranges = 0;
        try { return UiaComInterface.Ranges(_core, _core.GetVisibleRanges(), out ranges); }
        catch (InvalidOperationException) { return WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE; }
    }

    /// <inheritdoc />
    public int RangeFromChild(nint child, out nint range)
    {
        range = 0;
        return E_INVALIDARG;
    }

    /// <inheritdoc />
    public int RangeFromPoint(UiaPoint point, out nint range)
    {
        range = 0;
        return E_NOTIMPL;
    }

    /// <inheritdoc />
    public int GetDocumentRange(out nint range)
    {
        range = 0;
        try
        {
            range = UiaComInterface.Pointer(new UiaRangeObject(_core, _core.DocumentRange),
                typeof(ITextRangeProviderAbi).GUID);
            return 0;
        }
        catch (InvalidOperationException) { return WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE; }
        catch (OutOfMemoryException) { return WindowsTextResult.E_OUTOFMEMORY; }
    }

    /// <inheritdoc />
    public int GetSupportedTextSelection(out int selection) { selection = 1; return 0; }
}

/// <summary>A COM text-range probe with exact offscreen reads and source scrolling.</summary>
[GeneratedComClass]
internal sealed partial class UiaRangeObject : ITextRangeProviderAbi
{
    private const int E_NOTIMPL = unchecked((int)0x80004001);
    private const int E_FAIL = unchecked((int)0x80004005);
    private readonly WindowsTextProviderCore _core;
    private readonly AccessibleRange _range;

    /// <summary>Captures one source interval without copying document text.</summary>
    internal UiaRangeObject(WindowsTextProviderCore core, AccessibleRange range)
    {
        _core = core;
        _range = range;
    }

    /// <inheritdoc />
    public int Clone(out nint range)
    {
        range = 0;
        try
        {
            _core.ValidateRange(_range);
            range = UiaComInterface.Pointer(new UiaRangeObject(_core, _range),
                typeof(ITextRangeProviderAbi).GUID);
            return 0;
        }
        catch (InvalidOperationException) { return WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE; }
        catch (OutOfMemoryException) { return WindowsTextResult.E_OUTOFMEMORY; }
    }

    /// <inheritdoc />
    public int Compare(nint other, out int equal) { equal = 0; return E_NOTIMPL; }
    /// <inheritdoc />
    public int CompareEndpoints(int endpoint, nint other, int otherEndpoint, out int result)
    { result = 0; return E_NOTIMPL; }
    /// <inheritdoc />
    public int ExpandToEnclosingUnit(int unit) => E_NOTIMPL;
    /// <inheritdoc />
    public int FindAttribute(int attributeId, UiaVariant value, int backward, out nint range)
    { range = 0; return E_NOTIMPL; }
    /// <inheritdoc />
    public int FindText(nint text, int backward, int ignoreCase, out nint range)
    { range = 0; return E_NOTIMPL; }
    /// <inheritdoc />
    public int GetAttributeValue(int attributeId, out UiaVariant value)
    { value = default; return E_NOTIMPL; }
    /// <inheritdoc />
    public int GetBoundingRectangles(out nint rectangles)
    { rectangles = 0; return E_NOTIMPL; }
    /// <inheritdoc />
    public int GetEnclosingElement(out nint element)
    { element = 0; return E_NOTIMPL; }

    /// <inheritdoc />
    public int GetText(int maxLength, out nint bstr)
    {
        bstr = 0;
        var result = _core.TryGetText(_range, maxLength);
        if (result.HResult != 0) return result.HResult;
        try { bstr = Marshal.StringToBSTR(result.Text!); return 0; }
        catch (OutOfMemoryException) { return WindowsTextResult.E_OUTOFMEMORY; }
    }

    /// <inheritdoc />
    public int Move(int unit, int count, out int moved) { moved = 0; return E_NOTIMPL; }
    /// <inheritdoc />
    public int MoveEndpointByUnit(int endpoint, int unit, int count, out int moved)
    { moved = 0; return E_NOTIMPL; }
    /// <inheritdoc />
    public int MoveEndpointByRange(int endpoint, nint target, int targetEndpoint) => E_NOTIMPL;
    /// <inheritdoc />
    public int Select() => E_NOTIMPL;
    /// <inheritdoc />
    public int AddToSelection() => E_NOTIMPL;
    /// <inheritdoc />
    public int RemoveFromSelection() => E_NOTIMPL;
    /// <inheritdoc />
    public int ScrollIntoView(int alignToTop) =>
        ScrollCurrentIntoView(alignToTop != 0);

    private int ScrollCurrentIntoView(bool alignToTop)
    {
        try
        {
            return _core.ScrollIntoView(_range, alignToTop) switch
            {
                AccessibleRevealResult.Revealed => 0,
                AccessibleRevealResult.StaleRange => WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE,
                AccessibleRevealResult.CompositionBlocked or AccessibleRevealResult.NotVisible or
                    AccessibleRevealResult.WrongThread =>
                    WindowsTextResult.UIA_E_INVALIDOPERATION, // Never claim false success.
                _ => E_FAIL
            };
        }
        catch (InvalidOperationException) { return WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE; }
        catch (ArgumentOutOfRangeException) { return WindowsTextResult.E_INVALIDARG; }
    }
    /// <inheritdoc />
    public int GetChildren(out nint children)
    { children = 0; return E_NOTIMPL; }
}
