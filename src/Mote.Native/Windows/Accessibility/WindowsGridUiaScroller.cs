using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace Mote.Native.Windows.Accessibility;

/// <summary>SDK RangeValue vtable with a deliberately read-only admitted navigation domain.</summary>
[GeneratedComInterface, Guid("36dc7aef-33e6-4691-afe1-2be7274b3d33")]
internal partial interface IGridRangeValueProviderAbi
{
    /// <summary>Automation writes are not admitted by this first experimental stage.</summary>
    [PreserveSig] int SetValue(double value);
    /// <summary>Actual admitted viewport origin, not a pending guessed cell.</summary>
    [PreserveSig] int GetValue(out double value);
    /// <summary>True while external navigation writes remain gated.</summary>
    [PreserveSig] int GetIsReadOnly(out int readOnly);
    /// <summary>Last legal origin in the certified or prefix domain.</summary>
    [PreserveSig] int GetMaximum(out double value);
    /// <summary>Zero-based origin minimum.</summary>
    [PreserveSig] int GetMinimum(out double value);
    /// <summary>Admitted native page step.</summary>
    [PreserveSig] int GetLargeChange(out double value);
    /// <summary>One row or column ordinal step.</summary>
    [PreserveSig] int GetSmallChange(out double value);
}

/// <summary>Names a real native scroller and exposes complete read-only range facts, not fake host fallback.</summary>
[GeneratedComClass, SupportedOSPlatform("windows")]
internal sealed partial class WindowsGridUiaScroller : IRawElementProviderSimpleAbi, IGridRangeValueProviderAbi
{
    private const int Unavailable = unchecked((int)0x80040201), Unsupported = unchecked((int)0x80131509);
    private nint _hwnd;
    private readonly bool _vertical;
    private State? _state;
    /// <summary>One atomic publication contains navigation and actual native focus together.</summary>
    private sealed record State(NativeGridScrollFrame? Frame, bool HasFocus);
    /// <summary>Captures the real HWND and axis, never source data or a controller command.</summary>
    internal WindowsGridUiaScroller(nint hwnd, bool vertical) { _hwnd = hwnd; _vertical = vertical; }
    /// <summary>Publishes only after the native range/position readback has been validated.</summary>
    internal void Publish(NativeGridScrollFrame? frame, bool hasFocus) => Volatile.Write(ref _state, new(frame, hasFocus));
    /// <summary>Invalidates retained providers independently of source or another Grid attachment.</summary>
    internal void Detach() { _hwnd = 0; Volatile.Write(ref _state, null); }
    private NativeGridScrollAxis? Axis(State? state) => _vertical ? state?.Frame?.Rows : state?.Frame?.Columns;
    /// <summary>Registers this Simple provider while native HWND focus and keyboard behavior remain intact.</summary>
    internal nint GetObject(nuint wParam, nint lParam)
    {
        var pointer = UiaComInterface.Pointer(this, typeof(IRawElementProviderSimpleAbi).GUID);
        try { return UiaReturnRawElementProvider(_hwnd, wParam, lParam, pointer); }
        finally { Marshal.Release(pointer); }
    }
    /// <inheritdoc />
    public int GetProviderOptions(out int options) { options = 2 | 32; return _hwnd == 0 ? Unavailable : 0; }
    /// <inheritdoc />
    public int GetPatternProvider(int patternId, out nint provider)
    {
        provider = 0; if (_hwnd == 0) return Unavailable;
        if (patternId == 10003 && Axis(Volatile.Read(ref _state)) is { Kind: not NativeGridExtentKind.Unavailable })
            provider = UiaComInterface.Pointer(this, typeof(IGridRangeValueProviderAbi).GUID);
        return 0;
    }
    /// <inheritdoc />
    public int GetPropertyValue(int propertyId, out UiaVariant value)
    {
        value = default; if (_hwnd == 0) return Unavailable;
        var state = Volatile.Read(ref _state); var axis = Axis(state);
        if (propertyId == 30003) { value.Type = 3; value.Integer = 50014; }
        else if (propertyId is 30005 or 30011 or 30013)
        {
            var name = axis is not { Kind: not NativeGridExtentKind.Unavailable } ? _vertical ? "CSV rows unavailable" : "CSV columns unavailable" :
                _vertical ? axis?.Kind == NativeGridExtentKind.Exact ? "File rows" : "Indexed prefix rows" :
                axis?.Kind == NativeGridExtentKind.Exact ? "File columns" : "Known columns";
            var scope = axis?.Kind switch { NativeGridExtentKind.Exact => "Exact file navigation domain", NativeGridExtentKind.Prefix => _vertical ? "Admitted indexed row prefix; file row total unknown" : "Admitted known-column domain; file maximum width unknown", _ => "Navigation extent unavailable; no range pattern" };
            var first = axis is { Kind: not NativeGridExtentKind.Unavailable, Count: > 0 } admitted ?
                "current first CSV " + (_vertical ? "row " : "column ") + ((long)admitted.First + 1) : "no admitted first CSV coordinate";
            var text = propertyId == 30005 ? name : propertyId == 30011 ? _vertical ? "Mote.CsvGrid.Rows" : "Mote.CsvGrid.Columns" :
                scope + "; zero-based window-origin positions, maximum is the last legal window origin, not the last data ordinal; " + first + "; automation RangeValue is read-only. Native arrows/Page keys remain available; use Go to CSV cell for exact external navigation.";
            value.Type = 8; value.Pointer = Marshal.StringToBSTR(text);
        }
        else if (propertyId is 30008 or 30009 or 30010 or 30016 or 30017)
        {
            var yes = propertyId switch { 30008 => state?.HasFocus == true, 30009 or 30010 => axis is { Kind: not NativeGridExtentKind.Unavailable, Count: > 0 }, _ => true };
            value.Type = 11; value.Integer = yes ? -1 : 0;
        }
        return 0;
    }
    /// <inheritdoc />
    public int GetHostRawElementProvider(out nint provider) { provider = 0; return _hwnd == 0 ? Unavailable : UiaHostProviderFromHwnd(_hwnd, out provider); }
    private int Read(int member, out double value)
    {
        value = 0; if (_hwnd == 0) return Unavailable;
        var axis = Axis(Volatile.Read(ref _state));
        if (axis is not { Kind: not NativeGridExtentKind.Unavailable } admitted) return Unsupported;
        value = member switch { 0 => admitted.First, 1 => admitted.Last, 2 => 0, 3 => Math.Min(admitted.Page, admitted.Count), _ => 1 }; return 0;
    }
    /// <inheritdoc />
    public int SetValue(double value) => _hwnd == 0 ? Unavailable : Unsupported;
    /// <inheritdoc />
    public int GetValue(out double value) => Read(0, out value);
    /// <inheritdoc />
    public int GetIsReadOnly(out int readOnly) { readOnly = 1; return _hwnd == 0 ? Unavailable : Axis(Volatile.Read(ref _state)) is { Kind: not NativeGridExtentKind.Unavailable } ? 0 : Unsupported; }
    /// <inheritdoc />
    public int GetMaximum(out double value) => Read(1, out value);
    /// <inheritdoc />
    public int GetMinimum(out double value) => Read(2, out value);
    /// <inheritdoc />
    public int GetLargeChange(out double value) => Read(3, out value);
    /// <inheritdoc />
    public int GetSmallChange(out double value) => Read(4, out value);
    [LibraryImport("UIAutomationCore.dll")] private static partial int UiaHostProviderFromHwnd(nint hwnd, out nint provider);
    [LibraryImport("UIAutomationCore.dll")] private static partial nint UiaReturnRawElementProvider(nint hwnd, nuint wParam, nint lParam, nint provider);
}
