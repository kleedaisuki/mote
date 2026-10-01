using System.Runtime.InteropServices;
using Mote.Engine;
using Mote.Native.Accessibility;
using Mote.Native.Viewport;
using Mote.Native.Windows.Accessibility;

namespace Mote.Tests;

/// <summary>Checks the diagnostic UIA fragment's actual generated COM vtables.</summary>
public sealed class WindowsUiaFragmentExperimentTests
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int NavigateDelegate(nint self, int direction, out nint provider);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetPointerDelegate(nint self, out nint pointer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int HwndOverrideDelegate(nint self, nint hwnd, out nint provider);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int PatternDelegate(nint self, int pattern, out nint provider);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int PropertyDelegate(nint self, int property, out UiaVariant value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetTextDelegate(nint self, int maxLength, out nint bstr);

    /// <summary>
    /// Root navigation and HWND override must identify the same child with a
    /// source-backed TextPattern; retained pointers fail after local detach.
    /// </summary>
    [Fact]
    public void Generated_fragment_vtables_have_one_reachable_source_document()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var source = new Document("head\nTAIL😀");
        var snapshot = source.Snapshot;
        var frame = new CanvasFrame(snapshot.Version, new ViewportAnchor(0, 0), 0,
            [new ViewportSlice(0, 0, 4, 0, 16, false, false)], 0, 0);
        var core = new WindowsTextProviderCore(
            new AccessibleDocument(new AccessibleCanvasState(7, snapshot, frame)),
            new ViewportStub());
        var root = new UiaFragmentRootObject(core, (nint)1234);
        var rootFragment = UiaComInterface.Pointer(root, typeof(IRawElementProviderFragmentAbi).GUID);
        var rootOverride = UiaComInterface.Pointer(root,
            typeof(IRawElementProviderHwndOverrideAbi).GUID);
        try
        {
            var navigate = Marshal.GetDelegateForFunctionPointer<NavigateDelegate>(Slot(rootFragment, 3));
            Assert.Equal(0, navigate(rootFragment, 3, out var childFragment));
            Assert.NotEqual(0, childFragment);
            try
            {
                var overrideForHwnd = Marshal.GetDelegateForFunctionPointer<HwndOverrideDelegate>(
                    Slot(rootOverride, 3));
                Assert.Equal(0, overrideForHwnd(rootOverride, (nint)42, out var other));
                Assert.Equal(0, other);
                Assert.Equal(0, overrideForHwnd(rootOverride, (nint)1234, out var childSimple));
                Assert.NotEqual(0, childSimple);
                try
                {
                    var iid = typeof(IRawElementProviderFragmentAbi).GUID;
                    Assert.Equal(0, Marshal.QueryInterface(childSimple, in iid, out var overrideFragment));
                    try { Assert.Equal(childFragment, overrideFragment); }
                    finally { Marshal.Release(overrideFragment); }

                    var navigateChild = Marshal.GetDelegateForFunctionPointer<NavigateDelegate>(
                        Slot(childFragment, 3));
                    Assert.Equal(0, navigateChild(childFragment, 0, out var parentFragment));
                    try { Assert.Equal(rootFragment, parentFragment); }
                    finally { Marshal.Release(parentFragment); }

                    AssertSourceTextAndIdentity(childSimple, core);
                }
                finally { Marshal.Release(childSimple); }
            }
            finally { Marshal.Release(childFragment); }
        }
        finally
        {
            Marshal.Release(rootOverride);
            Marshal.Release(rootFragment);
        }
    }

    /// <summary>An off-UI-thread focus request must not pretend to move Win32 focus.</summary>
    [Fact]
    public async Task Fragment_set_focus_fails_honestly_off_window_thread()
    {
        if (!OperatingSystem.IsWindows()) return;
        const uint WsChild = 0x40000000;
        using var release = new ManualResetEventSlim(false);
        var ready = new TaskCompletionSource<nint>(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = Task.Run(() =>
        {
            var hwnd = CreateWindowEx(0, "STATIC", "mote-ax-focus-probe", WsChild,
                0, 0, 1, 1, (nint)(-3), 0, 0, 0);
            ready.SetResult(hwnd);
            release.Wait();
            if (hwnd != 0) Assert.True(DestroyWindow(hwnd));
        });
        try
        {
            var hwnd = await ready.Task;
            Assert.NotEqual(0, hwnd);
            using var source = new Document("focus");
            var frame = new CanvasFrame(source.Snapshot.Version, new ViewportAnchor(0, 0), 0,
                [new ViewportSlice(0, 0, 5, 0, 16, false, false)], 0, 0);
            var core = new WindowsTextProviderCore(
                new AccessibleDocument(new AccessibleCanvasState(9, source.Snapshot, frame)),
                new ViewportStub());
            var root = new UiaFragmentRootObject(core, hwnd);
            root.BindWindow(hwnd);
            root.SetSourceBodyClientHeight(0);
            Assert.Equal(0, root.SourceBodyBounds.Height);
            root.SetSourceBodyClientHeight(1);
            Assert.Equal(root.CanvasBounds.Height, root.SourceBodyBounds.Height);
            var ownerThread = GetWindowThreadProcessId(hwnd, 0);
            Assert.NotEqual(0u, ownerThread);
            Assert.NotEqual(ownerThread, GetCurrentThreadId());
            Assert.True(UiaFragmentRootObject.FocusMatchesForeground(
                ownerThread, ownerThread, hwnd, hwnd));
            Assert.False(UiaFragmentRootObject.FocusMatchesForeground(
                ownerThread, GetCurrentThreadId(), hwnd, hwnd));
            Assert.False(UiaFragmentRootObject.FocusMatchesForeground(
                ownerThread, ownerThread, hwnd, 0));
            Assert.Equal(WindowsTextResult.UIA_E_INVALIDOPERATION, root.SetFocus());
            // A real message-only HWND cannot own the foreground desktop.
            Assert.False(root.InputHasFocus);
            AssertFocusProperties(root, isKeyboardFocusable: false);
            AssertFocusProperties(root.Document, isKeyboardFocusable: true);
        }
        finally { release.Set(); await owner; }
    }

    private static void AssertFocusProperties(object provider, bool isKeyboardFocusable)
    {
        var simple = UiaComInterface.Pointer(provider, typeof(IRawElementProviderSimpleAbi).GUID);
        try
        {
            var property = Marshal.GetDelegateForFunctionPointer<PropertyDelegate>(Slot(simple, 5));
            Assert.Equal(0, property(simple, 30008, out var focused));
            Assert.Equal((ushort)11, focused.Type); // UIA_HasKeyboardFocusPropertyId, VT_BOOL.
            Assert.Equal(0, focused.Integer);
            Assert.Equal(0, property(simple, 30009, out var focusable));
            Assert.Equal((ushort)11, focusable.Type); // UIA_IsKeyboardFocusablePropertyId.
            Assert.Equal(isKeyboardFocusable ? -1 : 0, focusable.Integer);
        }
        finally { Marshal.Release(simple); }
    }

    private static void AssertSourceTextAndIdentity(nint childSimple, WindowsTextProviderCore core)
    {
        var property = Marshal.GetDelegateForFunctionPointer<PropertyDelegate>(Slot(childSimple, 5));
        Assert.Equal(0, property(childSimple, 30011, out var id));
        try { Assert.Equal("mote.source.document", Marshal.PtrToStringBSTR(id.Pointer)); }
        finally { Marshal.FreeBSTR(id.Pointer); }

        var pattern = Marshal.GetDelegateForFunctionPointer<PatternDelegate>(Slot(childSimple, 4));
        Assert.Equal(0, pattern(childSimple, 10014, out var text));
        Assert.NotEqual(0, text);
        try
        {
            var getRange = Marshal.GetDelegateForFunctionPointer<GetPointerDelegate>(Slot(text, 7));
            Assert.Equal(0, getRange(text, out var range));
            try
            {
                var getText = Marshal.GetDelegateForFunctionPointer<GetTextDelegate>(Slot(range, 12));
                Assert.Equal(0, getText(range, 32, out var bstr));
                try { Assert.Equal("head\nTAIL😀", Marshal.PtrToStringBSTR(bstr)); }
                finally { Marshal.FreeBSTR(bstr); }
                core.Detach();
                Assert.Equal(WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE,
                    getText(range, 32, out var stale));
                Assert.Equal(0, stale);
                Assert.Equal(WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE,
                    property(childSimple, 30011, out _));
            }
            finally { Marshal.Release(range); }
        }
        finally { Marshal.Release(text); }
    }

    private static nint Slot(nint instance, int index) =>
        Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), index * IntPtr.Size);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")]
    private static extern nint CreateWindowEx(uint exStyle, string className, string name,
        uint style, int x, int y, int width, int height, nint parent, nint menu,
        nint instance, nint parameter);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, nint processId);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private sealed class ViewportStub : IAccessibleViewport
    {
        /// <inheritdoc />
        public AccessibleRevealResult TryReveal(AccessibleRange range, bool alignToTop) =>
            AccessibleRevealResult.Revealed;
    }
}
