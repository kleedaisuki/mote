using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Mote.Native.Windows.Accessibility;

/// <summary>Owns one successful COM STA initialization on the native window thread.</summary>
/// <remarks>
/// Create before HWNDs and generated COM providers, and dispose on the same thread after
/// the message loop and providers end. A conflicting host apartment is observed, not
/// changed or uninitialized; selection callbacks retain their existing owner-thread guard.
/// STA initialization alone does not prove that a generated COM wrapper is non-agile.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsUiaApartment : IDisposable
{
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private bool _ownsInitialization;

    /// <summary>Exact CoInitializeEx result; only S_OK and S_FALSE acquire a balance obligation.</summary>
    internal int InitializationResult { get; }

    /// <summary>Exact CoGetApartmentType result, captured before any provider is created.</summary>
    internal int QueryResult { get; }

    /// <summary>Native APTTYPE value, or -1 when the query failed.</summary>
    internal int ApartmentType { get; }

    /// <summary>Native APTTYPEQUALIFIER value, or -1 when the query failed.</summary>
    internal int ApartmentQualifier { get; }

    /// <summary>Whether COM accepted the requested STA initialization on this owner thread.</summary>
    internal bool IsStaInitialized => InitializationResult is 0 or 1;

    /// <summary>Requests STA without changing a previously selected incompatible apartment.</summary>
    internal WindowsUiaApartment()
    {
        InitializationResult = CoInitializeEx(0, 2);
        _ownsInitialization = IsStaInitialized;
        QueryResult = CoGetApartmentType(out var type, out var qualifier);
        ApartmentType = QueryResult >= 0 ? type : -1;
        ApartmentQualifier = QueryResult >= 0 ? qualifier : -1;
    }

    /// <summary>Balances this scope once; never balances failed or incompatible initialization.</summary>
    public void Dispose()
    {
        if (!_ownsInitialization) return;
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("COM apartment scopes must end on their owner thread.");
        _ownsInitialization = false;
        CoUninitialize();
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint flags);

    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    private static partial int CoGetApartmentType(out int type, out int qualifier);
}
