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
/// Win32 UIA registration for the source-backed Canvas. The shell calls
/// HandleGetObject from WM_GETOBJECT and Close from WM_DESTROY. Legacy page
/// editing still uses the native control provider.
/// </summary>
/// <remarks>
/// This prototype sketches an AOT-compatible COM registration path and exact
/// source GetText/ScrollIntoView plumbing. Its text pattern is intentionally
/// incomplete (find, attributes, hit-test, geometry and events remain open).
/// Source range navigation and canonical selection are distinct from full
/// screen-reader acceptance. Test x64/arm64 ABI and COM lifetime independently.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsUiaBridgePrototype
{
    private readonly UiaEditorObject _provider;
    private readonly UiaFragmentRootObject? _fragmentRoot;

    /// <summary>Creates one provider object for one engine-backed editor.</summary>
    internal WindowsUiaBridgePrototype(AccessibleDocument document, IAccessibleViewport viewport,
        nint inputHwnd = 0, bool fragmentExperiment = false)
    {
        if (fragmentExperiment && inputHwnd == 0)
            throw new ArgumentException("The fragment experiment requires the input HWND.", nameof(inputHwnd));
        var core = new WindowsTextProviderCore(document, viewport);
        _provider = new UiaEditorObject(core);
        if (fragmentExperiment) _fragmentRoot = new UiaFragmentRootObject(core, inputHwnd);
    }

    /// <summary>Returns a WM_GETOBJECT result using a source-generated COM callable wrapper.</summary>
    internal nint HandleGetObject(nint hwnd, nuint wParam, nint lParam)
    {
        _provider.BindWindow(hwnd);
        _fragmentRoot?.BindWindow(hwnd);
        var visibleProvider = (object?)_fragmentRoot ?? _provider;
        var pointer = UiaComInterface.Pointer(visibleProvider, typeof(IRawElementProviderSimpleAbi).GUID);
        try { return UiaReturnRawElementProvider(hwnd, wParam, lParam, pointer); }
        finally { Marshal.Release(pointer); }
    }

    /// <summary>
    /// Publishes the physical source-body height in canvas client coordinates.
    /// Zero means the tiny window paints only the native input ribbon. The
    /// diagnostic fragment converts these units to UIA screen coordinates.
    /// </summary>
    internal void SetSourceBodyClientHeight(int height)
    {
        if (height < 0) throw new ArgumentOutOfRangeException(nameof(height));
        _fragmentRoot?.SetSourceBodyClientHeight(height);
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
        _fragmentRoot?.Detach();
        UiaReturnRawElementProvider(hwnd, 0, 0, 0);
        _provider.BindWindow(0);
        _fragmentRoot?.BindWindow(0);
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
    public int GetProviderOptions(out int options)
    {
        // UseComThreading preserves the non-agile generated provider's STA
        // affinity. Canonical selection never mutates the UI from an RPC worker.
        options = 2 | 0x20;
        return 0;
    }

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
    private AccessibleRange _range;
    private readonly object _rangeGate = new();

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
            var current = Current();
            range = UiaComInterface.Pointer(new UiaRangeObject(_core, current),
                typeof(ITextRangeProviderAbi).GUID);
            return 0;
        }
        catch (InvalidOperationException) { return WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE; }
        catch (OutOfMemoryException) { return WindowsTextResult.E_OUTOFMEMORY; }
    }

    /// <inheritdoc />
    public int Compare(nint other, out int equal)
    {
        equal = 0;
        try
        {
            var peer = Peer(other);
            var current = Current();
            _core.ValidateRange(peer);
            equal = current == peer ? 1 : 0;
            return 0;
        }
        catch (Exception error) { return RangeError(error); }
    }
    /// <inheritdoc />
    public int CompareEndpoints(int endpoint, nint other, int otherEndpoint, out int result)
    {
        result = 0;
        try
        {
            CheckEndpoint(endpoint);
            CheckEndpoint(otherEndpoint);
            var peer = Peer(other);
            var current = Current();
            _core.ValidateRange(peer);
            result = Endpoint(current, endpoint) - Endpoint(peer, otherEndpoint);
            return 0;
        }
        catch (Exception error) { return RangeError(error); }
    }
    /// <inheritdoc />
    public int ExpandToEnclosingUnit(int unit) => Change(range =>
        new WindowsTextRangeNavigation(_core, range).Expand(range, unit));
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
        WindowsTextResult result;
        try { result = _core.TryGetText(Current(), maxLength); }
        catch (Exception error) { return RangeError(error); }
        if (result.HResult != 0) return result.HResult;
        try { bstr = Marshal.StringToBSTR(result.Text!); return 0; }
        catch (OutOfMemoryException) { return WindowsTextResult.E_OUTOFMEMORY; }
    }

    /// <inheritdoc />
    public int Move(int unit, int count, out int moved)
    {
        moved = 0;
        try
        {
            lock (_rangeGate)
            {
                var result = new WindowsTextRangeNavigation(_core, _range).Move(_range, unit, count);
                _core.ValidateRange(result.Range);
                _range = result.Range;
                moved = result.Moved;
                return 0;
            }
        }
        catch (Exception error) { return RangeError(error); }
    }
    /// <inheritdoc />
    public int MoveEndpointByUnit(int endpoint, int unit, int count, out int moved)
    {
        moved = 0;
        try
        {
            CheckEndpoint(endpoint);
            lock (_rangeGate)
            {
                var result = new WindowsTextRangeNavigation(_core, _range)
                    .MoveEndpoint(Endpoint(_range, endpoint), unit, count);
                var next = WithEndpoint(_range, endpoint, result.Offset);
                _core.ValidateRange(next);
                _range = next;
                moved = result.Moved;
                return 0;
            }
        }
        catch (Exception error) { return RangeError(error); }
    }
    /// <inheritdoc />
    public int MoveEndpointByRange(int endpoint, nint target, int targetEndpoint)
    {
        try
        {
            CheckEndpoint(endpoint);
            CheckEndpoint(targetEndpoint);
            // Capture the peer before locking this range: reciprocal concurrent
            // calls must not acquire two mutable-range locks in opposite order.
            var peer = Peer(target);
            _core.ValidateRange(peer);
            return Change(range => WithEndpoint(range, endpoint, Endpoint(peer, targetEndpoint)));
        }
        catch (Exception error) { return RangeError(error); }
    }
    /// <inheritdoc />
    public int Select()
    {
        try { return _core.Select(Current()); }
        catch (Exception error) { return RangeError(error); }
    }
    /// <inheritdoc />
    public int AddToSelection() => E_NOTIMPL;
    /// <inheritdoc />
    public int RemoveFromSelection() => E_NOTIMPL;
    /// <inheritdoc />
    public int ScrollIntoView(int alignToTop) =>
        ScrollCurrentIntoView(alignToTop != 0);

    /// <summary>Captures one validated mutable interval without retaining source text.</summary>
    private AccessibleRange Current()
    {
        lock (_rangeGate)
        {
            _core.ValidateRange(_range);
            return _range;
        }
    }

    /// <summary>Resolves only this provider's own generated CCWs, never an arbitrary native pointer.</summary>
    private AccessibleRange Peer(nint pointer)
    {
        if (pointer == 0) throw new ArgumentException("A peer text range is required.");
        var id = new Guid("00000000-0000-0000-C000-000000000046");
        var hr = Marshal.QueryInterface(pointer, in id, out var unknown);
        if (hr < 0) throw new ArgumentException("The peer is not an IUnknown range.");
        try
        {
            if (!ComWrappers.TryGetObject(unknown, out var value) ||
                value is not UiaRangeObject peer || !ReferenceEquals(peer._core, _core))
                throw new ArgumentException("The peer belongs to another text provider.");
            return peer.Current();
        }
        finally { Marshal.Release(unknown); }
    }

    /// <summary>Commits computed endpoints once, only after current-source validation succeeds.</summary>
    private int Change(Func<AccessibleRange, AccessibleRange> change)
    {
        try
        {
            lock (_rangeGate)
            {
                _core.ValidateRange(_range);
                var next = change(_range);
                _core.ValidateRange(next);
                _range = next;
                return 0;
            }
        }
        catch (Exception error) { return RangeError(error); }
    }

    private static int Endpoint(AccessibleRange range, int endpoint) =>
        endpoint == 0 ? range.Start : range.End;

    private static void CheckEndpoint(int endpoint)
    {
        if (endpoint is not (0 or 1)) throw new ArgumentOutOfRangeException(nameof(endpoint));
    }

    /// <summary>Crossing an endpoint collapses the opposite endpoint, as required by UIA.</summary>
    private static AccessibleRange WithEndpoint(AccessibleRange range, int endpoint, int offset) =>
        endpoint == 0
            ? range with { Start = offset, End = Math.Max(offset, range.End) }
            : range with { Start = Math.Min(offset, range.Start), End = offset };

    private static int RangeError(Exception error) => error switch
    {
        AccessibleRequestTooLargeException => WindowsTextResult.UIA_E_INVALIDOPERATION,
        InvalidOperationException => WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE,
        ArgumentException => WindowsTextResult.E_INVALIDARG,
        OutOfMemoryException => WindowsTextResult.E_OUTOFMEMORY,
        _ => E_FAIL
    };
    private int ScrollCurrentIntoView(bool alignToTop)
    {
        try
        {
            return _core.ScrollIntoView(Current(), alignToTop) switch
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
