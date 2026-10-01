using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Mote.Engine;
using Mote.Native.Accessibility;
using Mote.Native.Viewport;
using Mote.Native.Windows.Accessibility;
using Xunit.Abstractions;

namespace Mote.Tests;

/// <summary>Exercises real per-thread COM lifetime without changing the test runner apartment.</summary>
public sealed class WindowsUiaApartmentTests(ITestOutputHelper output)
{
    /// <summary>Nested STA scopes balance S_FALSE and leave the outer native apartment intact.</summary>
    [Fact]
    public void Nested_sta_scopes_balance_and_dispose_idempotently()
    {
        OnThread(() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            using var outer = new WindowsUiaApartment();
            Assert.True(outer.IsStaInitialized);
            Assert.Equal(0, outer.QueryResult);
            Assert.True(outer.ApartmentType is 0 or 3);
            using var inner = new WindowsUiaApartment();
            Assert.Equal(1, inner.InitializationResult);
            inner.Dispose();
            inner.Dispose();
            Assert.Equal(0, CoGetApartmentType(out var type, out _));
            Assert.Equal(outer.ApartmentType, type);
        });
    }

    /// <summary>Failed STA initialization never uninitializes an existing MTA selected by a host.</summary>
    [Fact]
    public void Changed_mode_preserves_host_mta()
    {
        OnThread(() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var result = CoInitializeEx(0, 0);
            Assert.True(result is 0 or 1);
            try
            {
                using var scope = new WindowsUiaApartment();
                Assert.Equal(unchecked((int)0x80010106), scope.InitializationResult);
                Assert.False(scope.IsStaInitialized);
                Assert.Equal(1, scope.ApartmentType);
                scope.Dispose();
                Assert.Equal(0, CoGetApartmentType(out var type, out _));
                Assert.Equal(1, type);
            }
            finally { CoUninitialize(); }
        }, ApartmentState.MTA);
    }

    /// <summary>Records actual generated-wrapper agility instead of inferring it from STA creation.</summary>
    [Fact]
    public void Generated_source_provider_marshaling_interfaces_are_observed()
    {
        OnThread(() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            using var apartment = new WindowsUiaApartment();
            Assert.True(apartment.IsStaInitialized);
            using var source = new Document("hello");
            var snapshot = source.Snapshot;
            var frame = new CanvasFrame(snapshot.Version, new ViewportAnchor(0, 0), 0, [], 0, 0);
            var accessible = new AccessibleDocument(new AccessibleCanvasState(1, snapshot, frame));
            var core = new WindowsTextProviderCore(accessible, new ViewportStub());
            var provider = UiaComInterface.Pointer(new UiaEditorObject(core),
                typeof(IRawElementProviderSimpleAbi).GUID);
            try
            {
                Observe(provider, "IAgileObject", new Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90"));
                Observe(provider, "IMarshal", new Guid("00000003-0000-0000-C000-000000000046"));
            }
            finally { Marshal.Release(provider); }
        });
    }

    /// <summary>Reports supported interfaces and balances every returned reference.</summary>
    private void Observe(nint provider, string name, Guid id)
    {
        var hr = Marshal.QueryInterface(provider, in id, out var pointer);
        output.WriteLine($"{name}: HRESULT 0x{hr:X8}; present={pointer != 0}");
        try { Assert.True(hr == 0 || hr == unchecked((int)0x80004002)); }
        finally { if (pointer != 0) Marshal.Release(pointer); }
    }

    /// <summary>Contains apartment changes on a disposable thread and propagates assertions.</summary>
    private static void OnThread(Action action, ApartmentState apartment = ApartmentState.STA)
    {
        if (!OperatingSystem.IsWindows()) return;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(apartment);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "COM apartment test did not complete.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    /// <summary>Pure source reveal sink; the wrapper probe never opens a window or changes focus.</summary>
    private sealed class ViewportStub : IAccessibleViewport
    {
        public AccessibleRevealResult TryReveal(AccessibleRange range, bool alignToTop) => AccessibleRevealResult.Revealed;
    }

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();
    [DllImport("ole32.dll")]
    private static extern int CoGetApartmentType(out int type, out int qualifier);
}
