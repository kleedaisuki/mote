using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace Mote.Native.Windows.Accessibility;

/// <summary>Native HWND group containing the bounded Table and real logical navigation controls.</summary>
[GeneratedComClass, SupportedOSPlatform("windows")]
internal sealed partial class WindowsGridUiaGroup : IRawElementProviderSimpleAbi
{
    private nint _hwnd;
    private readonly bool _statusOnly;
    private string _status = "CSV navigation; window not installed";
    /// <summary>Captures only the group HWND, never a source snapshot.</summary>
    internal WindowsGridUiaGroup(nint hwnd, bool statusOnly = false) { _hwnd = hwnd; _statusOnly = statusOnly; }
    /// <summary>Publishes proved extent and retained selection facts separately from Table counts.</summary>
    internal void Publish(string status) => Volatile.Write(ref _status, status);
    /// <summary>Invalidates retained group providers independently of source accessibility.</summary>
    internal void Detach() => _hwnd = 0;
    /// <summary>Returns this group while leaving native descendant HWND navigation intact.</summary>
    internal nint GetObject(nuint wParam, nint lParam)
    {
        var pointer = UiaComInterface.Pointer(this, typeof(IRawElementProviderSimpleAbi).GUID);
        try { return UiaReturnRawElementProvider(_hwnd, wParam, lParam, pointer); }
        finally { Marshal.Release(pointer); }
    }
    /// <inheritdoc />
    public int GetProviderOptions(out int options) { options = 2; return _hwnd == 0 ? unchecked((int)0x80040201) : 0; }
    /// <inheritdoc />
    public int GetPatternProvider(int patternId, out nint provider) { provider = 0; return _hwnd == 0 ? unchecked((int)0x80040201) : 0; }
    /// <inheritdoc />
    public int GetPropertyValue(int propertyId, out UiaVariant value)
    {
        value = default;
        if (_hwnd == 0) return unchecked((int)0x80040201);
        if (propertyId == 30003) { value.Type = 3; value.Integer = _statusOnly ? 50020 : 50026; }
        else if (propertyId is 30005 or 30011 or 30013)
        {
            value.Type = 8; value.Pointer = Marshal.StringToBSTR(propertyId switch
            { 30005 => _statusOnly ? "CSV grid status: " + Volatile.Read(ref _status) : "Mote CSV grid navigation", 30011 => _statusOnly ? "Mote.CsvGrid.Status" : "Mote.CsvGrid.Navigation", _ => Volatile.Read(ref _status) });
        }
        else if (propertyId is 30016 or 30017) { value.Type = 11; value.Integer = -1; }
        else if (propertyId is 30008 or 30009) { value.Type = 11; value.Integer = 0; }
        return 0;
    }
    /// <inheritdoc />
    public int GetHostRawElementProvider(out nint provider) { provider = 0; return _hwnd == 0 ? unchecked((int)0x80040201) : UiaHostProviderFromHwnd(_hwnd, out provider); }
    [LibraryImport("UIAutomationCore.dll")] private static partial int UiaHostProviderFromHwnd(nint hwnd, out nint provider);
    [LibraryImport("UIAutomationCore.dll")] private static partial nint UiaReturnRawElementProvider(nint hwnd, nuint wParam, nint lParam, nint provider);
}
