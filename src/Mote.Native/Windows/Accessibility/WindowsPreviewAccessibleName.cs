using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Mote.Native.Windows.Accessibility;

/// <summary>
/// Gives the separate read-only RichEdit preview a stable, content-free accessible name.
/// The editor's source-backed UIA Document is not altered by this annotation.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class WindowsPreviewAccessibleName : IDisposable
{
    private const uint ClsctxInprocServer = 1;
    private const uint CoinitApartmentThreaded = 2;
    private const uint ObjidClient = unchecked((uint)-4);
    private const uint ChildidSelf = 0;
    private const int RpcEChangedMode = unchecked((int)0x80010106);
    private static readonly Guid ClassId = new("b5f8350b-0548-48b1-a6ee-88bd00b4a5e7");
    private static readonly Guid InterfaceId = new("6e26e776-04f0-495d-80e4-3330352e3169");
    private static readonly Guid NamePropertyId = new("c3a6921b-4a99-44f1-bca6-61187052c431");
    private nint _services;
    private readonly nint _preview;
    private readonly bool _uninitialize;

    private WindowsPreviewAccessibleName(nint preview, nint services, bool uninitialize)
    {
        _preview = preview;
        _services = services;
        _uninitialize = uninitialize;
    }

    /// <summary>
    /// Annotates only the preview's MSAA client object. Failure never prevents text editing;
    /// a target-host UIA probe must still verify that the name reaches clients.
    /// </summary>
    internal static WindowsPreviewAccessibleName? TryCreate(nint preview)
    {
        if (preview == 0) return null;
        var initialized = CoInitializeEx(0, CoinitApartmentThreaded);
        if (initialized < 0 && initialized != RpcEChangedMode) return null;
        var uninitialize = initialized >= 0;
        nint services = 0;
        var transferred = false;
        try
        {
            var classId = ClassId;
            var interfaceId = InterfaceId;
            if (CoCreateInstance(in classId, 0, ClsctxInprocServer, in interfaceId,
                    out services) < 0 || services == 0)
                return null;

            // oleacc.h: IUnknown slots 0..2, SetHwndPropStr slot 7. MSAAPROPID is GUID by value.
            var vtable = *(nint**)services;
            var setName = (delegate* unmanaged[Stdcall]<nint, nint, uint, uint, Guid, char*, int>)vtable[7];
            fixed (char* name = "Mote preview")
            {
                if (setName(services, preview, ObjidClient, ChildidSelf, NamePropertyId, name) < 0)
                    return null;
            }
            var annotation = new WindowsPreviewAccessibleName(preview, services, uninitialize);
            transferred = true;
            return annotation;
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            if (!transferred)
            {
                if (services != 0) Marshal.Release(services);
                if (uninitialize) CoUninitialize();
            }
        }
    }

    /// <summary>Clears the HWND annotation before RichEdit destruction and balances COM ownership.</summary>
    public void Dispose()
    {
        var services = _services;
        if (services == 0) return;
        _services = 0;
        try
        {
            var vtable = *(nint**)services;
            // oleacc.h: ClearHwndProps slot 9; one Name property is removed, not all annotations.
            var clear = (delegate* unmanaged[Stdcall]<nint, nint, uint, uint, Guid*, int, int>)vtable[9];
            var property = NamePropertyId;
            clear(services, _preview, ObjidClient, ChildidSelf, &property, 1);
        }
        finally
        {
            Marshal.Release(services);
            if (_uninitialize) CoUninitialize();
        }
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoInitializeEx(nint reserved, uint flags);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(in Guid classId, nint outer, uint context,
        in Guid interfaceId, out nint instance);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoUninitialize();
}
