using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mote.Formats;
using Mote.Native;
using Mote.Native.Windows;
using Mote.Native.Windows.Accessibility;
using Mote.Themes;

namespace Mote.Tests;

/// <summary>Bounded generated UIA ABI, retained epoch, and actual adapter selection contracts.</summary>
public sealed class WindowsGridAccessibilityTests
{
    /// <summary>All advertised container/cell pattern pointers expose their SDK GUID and correct slots.</summary>
    [Fact]
    public void Pattern_vtables_headers_local_indices_and_pending_value_are_coherent()
    {
        if (!OperatingSystem.IsWindows()) return;
        var frame = Frame();
        var actions = new Actions();
        var bridge = new WindowsGridUiaBridge(0, actions, (_, _, _) => new(1, 2, 3, 4));
        bridge.Publish(frame);
        var root = bridge.Node(frame.Id, 0, 0, 0);
        Assert.Equal(0, root.GetRuntimeId(out var rootId));
        Assert.Equal(0, rootId);
        foreach (var entry in new[] { (10006, typeof(IGridProviderAbi)), (10012, typeof(ITableProviderAbi)), (10001, typeof(ISelectionProviderAbi)) })
        {
            Assert.Equal(0, root.GetPatternProvider(entry.Item1, out var pointer));
            Assert.NotEqual(0, pointer);
            var guid = entry.Item2.GUID;
            Assert.Equal(0, Marshal.QueryInterface(pointer, in guid, out var same));
            Marshal.Release(same); Marshal.Release(pointer);
        }
        Assert.Equal(0, root.GetPatternProvider(10006, out var grid));
        try
        {
            var rowCount = Marshal.GetDelegateForFunctionPointer<Count>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(grid), 4 * IntPtr.Size));
            Assert.Equal(0, rowCount(grid, out var count)); Assert.Equal(3, count);
            var getItem = Marshal.GetDelegateForFunctionPointer<Item>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(grid), 3 * IntPtr.Size));
            Assert.Equal(0, getItem(grid, 2, 1, out var cell)); Marshal.Release(cell);
            Assert.Equal(unchecked((int)0x80070057), getItem(grid, 1000, 16, out cell)); Assert.Equal(0, cell);
        }
        finally { Marshal.Release(grid); }
        var header = bridge.Node(frame.Id, 2, 0, 0);
        Assert.Equal(unchecked((int)0x80131509), header.Select());
        Assert.Equal(0, actions.Mutations);
        Assert.Equal(unchecked((int)0x80131509), header.GetItem(0, 0, out _));
        var item = bridge.Node(frame.Id, 1, 2, 1);
        Assert.Equal(0, item.GetRow(out var row)); Assert.Equal(2, row);
        Assert.Equal(0, item.GetColumn(out var column)); Assert.Equal(1, column);
        Assert.Equal(0, item.GetPropertyValue(30005, out var name));
        try { Assert.StartsWith("Row 1003, Column 18; Pending", Marshal.PtrToStringBSTR(name.Pointer)); }
        finally { Marshal.FreeBSTR(name.Pointer); }
        Assert.Equal(0, item.GetPatternProvider(10002, out var value)); Assert.Equal(0, value);
        Assert.Equal(0, item.GetPropertyValue(30009, out var focusable)); Assert.Equal(0, focusable.Integer);
        Assert.Equal(0, root.GetRowHeaders(out var headers)); Assert.Equal(3, ArrayLength(headers)); SafeArrayDestroy(headers);
        Assert.Equal(0, item.GetColumnHeaderItems(out headers)); Assert.Equal(1, ArrayLength(headers)); SafeArrayDestroy(headers);
        Assert.Equal(0, root.GetSelection(out var selection)); Assert.Equal(4, ArrayLength(selection)); SafeArrayDestroy(selection);
        bridge.Detach();
    }

    /// <summary>A retained old node cannot acquire a new coordinate or retain historical projections.</summary>
    [Fact]
    public void Retained_nodes_and_old_epoch_requests_are_unavailable_after_replace_clear_and_detach()
    {
        if (!OperatingSystem.IsWindows()) return;
        var frame = Frame();
        var actions = new Actions();
        var bridge = new WindowsGridUiaBridge(0, actions, (_, _, _) => default);
        bridge.Publish(frame);
        var old = bridge.Node(frame.Id, 1, 0, 0);
        bridge.Publish(frame with { Id = frame.Id with { WindowSerial = 2 }, Rows = new(2000, 3) });
        Assert.Equal(unchecked((int)0x80040201), old.GetPropertyValue(30005, out _));
        Assert.Equal(unchecked((int)0x80040201), old.Select());
        var requestedOld = bridge.Node(frame.Id, 1, 1, 1);
        Assert.Equal(unchecked((int)0x80040201), requestedOld.GetRow(out _));
        bridge.Clear();
        Assert.Equal(unchecked((int)0x80040201), requestedOld.AddToSelection());
        Assert.Equal(0, actions.Mutations);
        bridge.Detach();
        foreach (var field in new[] { "_actions", "_bounds" })
            Assert.Null(typeof(WindowsGridUiaBridge).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(bridge));
    }

    /// <summary>Native clear removes painted membership and Copy does not resurrect hidden endpoints.</summary>
    [Fact]
    public void Adapter_clear_reject_hole_and_composition_leave_source_intents_untouched()
    {
        if (!OperatingSystem.IsWindows()) return;
        var parent = Win32.CreateWindowExW(0, "STATIC", "accessibility test", Win32.WS_OVERLAPPEDWINDOW,
            0, 0, 700, 400, 0, 0, Win32.GetModuleHandleW(null), 0);
        var composing = false;
        using var grid = new WindowsCsvGrid(parent, 104, ThemePolicies.Get(ThemePolicies.DefaultId), () => composing);
        try
        {
            grid.Install(Projection(), new(new(1, 0), 1));
            var intents = 0; grid.IntentRequested += _ => intents++;
            var frame = ReadFrame(grid);
            var rectangle = new GridAccessibleSelection(new(1000, 16), new(1001, 17), false);
            Assert.Equal(GridAccessibilityResult.Applied, grid.MutateSelection(frame.Id, new GridSelectionMutation.ReplaceRectangle(rectangle)));
            Assert.Equal(GridAccessibilityResult.Unsupported, grid.MutateSelection(frame.Id, new GridSelectionMutation.RemoveCell(new(1000, 16))));
            Assert.Equal(rectangle, ReadFrame(grid).Selection);
            var selectedBackground = DrawBackground(grid);
            Assert.Equal(GridAccessibilityResult.Applied, grid.MutateSelection(frame.Id, new GridSelectionMutation.Clear()));
            Assert.Null(ReadFrame(grid).Selection);
            Assert.NotEqual(selectedBackground, DrawBackground(grid));
            grid.Copy(); Assert.Null(ReadFrame(grid).Selection);
            composing = true;
            Assert.Equal(GridAccessibilityResult.CompositionBlocked, grid.MutateSelection(frame.Id, new GridSelectionMutation.AddCell(new(1000, 16))));
            Assert.Null(ReadFrame(grid).Selection);
            // Successful accessibility mutation dispatches selection-only cancellation plus explicit Copy.
            Assert.Equal(2, intents);
        }
        finally { grid.Dispose(); Win32.DestroyWindow(parent); }
    }

    /// <summary>Semantic cell focus commands target that cell while Copy retains the independent selection.</summary>
    [Fact]
    public void Focused_cell_keyboard_actions_and_arrow_origin_do_not_use_old_selection()
    {
        if (!OperatingSystem.IsWindows()) return;
        var parent = Win32.CreateWindowExW(0, "STATIC", "focus grid test", Win32.WS_OVERLAPPEDWINDOW,
            0, 0, 700, 400, 0, 0, Win32.GetModuleHandleW(null), 0);
        using var grid = new WindowsCsvGrid(parent, 104, ThemePolicies.Get(ThemePolicies.DefaultId));
        try
        {
            grid.Install(Projection(), new(new(1, 0), 1)); grid.Resize(0, 0, 600, 300);
            Win32.ShowWindow(parent, 5); grid.Show(true);
            var intents = new List<NativeGridIntent>(); grid.IntentRequested += intents.Add;
            var frame = ReadFrame(grid); var selected = new GridCoordinate(1000, 17); var focused = new GridCoordinate(1000, 16);
            Assert.Equal(GridAccessibilityResult.Applied, grid.MutateSelection(frame.Id,
                new GridSelectionMutation.ReplaceRectangle(new(selected, selected, false))));
            var beforePendingFocus = WindowsGridInterop.GetFocus();
            Assert.Equal(GridAccessibilityResult.NotReady, grid.Focus(frame.Id, selected));
            Assert.Equal(beforePendingFocus, WindowsGridInterop.GetFocus());
            Assert.Equal(selected, ReadFrame(grid).Selection!.Value.Active);
            Assert.Equal(GridAccessibilityResult.Applied, grid.Focus(frame.Id, focused));
            Assert.Equal(selected, ReadFrame(grid).Selection!.Value.Active);
            Win32.SendMessageW(grid.Handle, Win32.WM_KEYDOWN, 13, 0);
            Assert.Equal((NativeGridIntentKind.Reveal, 1000, 16), (intents[^1].Kind, intents[^1].Row, intents[^1].Column));
            Win32.SendMessageW(grid.Handle, Win32.WM_KEYDOWN, 0x71, 0);
            Assert.Equal((NativeGridIntentKind.Replace, 1000, 16), (intents[^1].Kind, intents[^1].Row, intents[^1].Column));
            grid.Copy(); Assert.Equal(17, intents[^1].Column); Assert.Equal(focused, ReadFrame(grid).FocusedCell);
            Win32.SendMessageW(grid.Handle, Win32.WM_KEYDOWN, 0x27, 0);
            Assert.Equal((NativeGridIntentKind.Select, 1000, 17), (intents[^1].Kind, intents[^1].Row, intents[^1].Column));
            grid.Install(Projection(), new(new(2, 0), 1));
            Assert.Null(typeof(WindowsCsvGrid).GetField("_accessibleFocusedCell", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(grid));
        }
        finally { grid.Dispose(); Win32.DestroyWindow(parent); }
    }

    /// <summary>A stalled UI admission times out without a late mutation; concurrent commands stay bounded.</summary>
    [Fact]
    public void External_selection_timeout_throw_and_focus_refusal_have_no_late_effects()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); using var ready = new ManualResetEventSlim();
        WindowsCsvGrid? grid = null; uint threadId = 0; var mode = 0; Thread? caller = null;
        var owner = new Thread(() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            threadId = GetCurrentThreadId();
            var parent = Win32.CreateWindowExW(0, "STATIC", "thread grid test", Win32.WS_OVERLAPPEDWINDOW,
                0, 0, 700, 400, 0, 0, Win32.GetModuleHandleW(null), 0);
            try
            {
                grid = new WindowsCsvGrid(parent, 104, ThemePolicies.Get(ThemePolicies.DefaultId), () =>
                {
                    if (Volatile.Read(ref mode) == 1) { entered.Set(); release.Wait(); }
                    if (Volatile.Read(ref mode) == 2) throw new InvalidOperationException("synthetic admission fault");
                    return false;
                });
                grid.Install(Projection(), new(new(1, 0), 1)); ready.Set();
                while (Win32.GetMessageW(out var message, 0, 0, 0) > 0) { Win32.TranslateMessage(ref message); Win32.DispatchMessageW(ref message); }
            }
            finally { grid?.Dispose(); Win32.DestroyWindow(parent); }
        });
        owner.SetApartmentState(ApartmentState.STA); owner.Start();
        try
        {
            Assert.True(ready.Wait(5000)); var frame = ReadFrame(grid!);
            Volatile.Write(ref mode, 1);
            var result = GridAccessibilityResult.Unsupported;
            System.Runtime.ExceptionServices.ExceptionDispatchInfo? failure = null;
            // This caller must reach the blocked HWND owner independently of the test runner's
            // pool. Task.Run can remain queued while other tests synchronously occupy its workers.
            caller = new Thread(() =>
            {
                if (!OperatingSystem.IsWindows()) return;
                try { result = grid!.MutateSelection(frame.Id, new GridSelectionMutation.Clear()); }
                catch (Exception error) { failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error); }
            });
            caller.Start();
            Assert.True(entered.Wait(5000));
            Assert.Equal(GridAccessibilityResult.Unsupported, grid!.MutateSelection(frame.Id, new GridSelectionMutation.Clear()));
            Assert.True(caller.Join(5000));
            failure?.Throw();
            Assert.Equal(GridAccessibilityResult.Unavailable, result);
            release.Set(); Volatile.Write(ref mode, 0);
            // A synchronous no-change request waits behind the expired message, proving it has resumed.
            Assert.Equal(GridAccessibilityResult.NoChange, grid.MutateSelection(frame.Id,
                new GridSelectionMutation.ReplaceRectangle(frame.Selection!.Value)));
            Assert.Equal(frame.Selection, ReadFrame(grid).Selection);
            Volatile.Write(ref mode, 2);
            Assert.Equal(GridAccessibilityResult.Unavailable, grid.MutateSelection(frame.Id, new GridSelectionMutation.Clear()));
            Assert.Equal(frame.Selection, ReadFrame(grid).Selection);
            Assert.Equal(GridAccessibilityResult.Unsupported, grid.Focus(frame.Id, new(1000, 16)));
        }
        finally
        {
            release.Set();
            try { if (caller is not null) Assert.True(caller.Join(5000)); }
            finally { PostThreadMessageW(threadId, 0x0012, 0, 0); Assert.True(owner.Join(5000)); }
        }
    }

    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool PostThreadMessageW(uint threadId, uint message, nuint parameter, nint data);

    /// <summary>Exercises the actual custom-draw callback, not an independent membership helper.</summary>
    [SupportedOSPlatform("windows")]
    private static uint DrawBackground(WindowsCsvGrid grid)
    {
        var draw = new WindowsGridInterop.Draw { Header = new() { Window = grid.Handle, Code = WindowsGridInterop.CustomDraw }, Stage = 0x30001, Item = 0, Column = 1 };
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<WindowsGridInterop.Draw>());
        try
        {
            Marshal.StructureToPtr(draw, memory, false); Assert.True(grid.HandleNotify(memory, out _));
            return Marshal.PtrToStructure<WindowsGridInterop.Draw>(memory).Background;
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    /// <summary>Worst-case 8192-slot enumeration remains bounded and retirement drops the current wrapper map.</summary>
    [Fact]
    public void Maximum_window_enumeration_and_retained_wrappers_do_not_keep_history()
    {
        if (!OperatingSystem.IsWindows()) return;
        var frame = NativeGridAccessibility.Create(new(new(1, 0), 1), null, null, null,
            new(0, 128), new(0, 64), new(new(0, 0), new(127, 63), false), null, false);
        var bridge = new WindowsGridUiaBridge(0, new Actions(), (_, _, _) => default); bridge.Publish(frame);
        var root = bridge.Node(frame.Id, 0, 0, 0);
        var allocated = GC.GetAllocatedBytesForCurrentThread(); var timer = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal(0, root.GetSelection(out var selected)); Assert.Equal(8192, ArrayLength(selected));
        var allocation = GC.GetAllocatedBytesForCurrentThread() - allocated; SafeArrayDestroy(selected);
        var retained = new WindowsGridUiaNode[64];
        for (var column = 0; column < retained.Length; column++) retained[column] = bridge.Node(frame.Id, 1, 127, column);
        bridge.Clear(); Assert.Null(bridge.CurrentFrame);
        foreach (var node in retained) Assert.Equal(unchecked((int)0x80040201), node.GetRow(out _));
        var nodes = typeof(WindowsGridUiaBridge).GetField("_nodes", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(bridge)!;
        Assert.Equal(0, (int)nodes.GetType().GetProperty("Count")!.GetValue(nodes)!);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory.Parent is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "Mote.Native"))) directory = directory.Parent;
        var reports = Path.Combine(directory.FullName, ".cache", "windows-grid-accessibility"); Directory.CreateDirectory(reports);
        File.WriteAllText(Path.Combine(reports, "worst-case-enumeration.json"), System.Text.Json.JsonSerializer.Serialize(new
        { Slots = 8192, RetainedWrappers = retained.Length, ManagedAllocatedBytes = allocation, ElapsedMs = timer.Elapsed.TotalMilliseconds,
            Scope = "one in-process generated COM enumeration; not RSS, AOT, or 100 MiB fixture acceptance" }));
        bridge.Detach();
    }

    /// <summary>Read-only range facts are certified window origins; unknown and empty axes do not invent row one.</summary>
    [Fact]
    public void Read_only_scroller_patterns_distinguish_prefix_unavailable_and_exact_empty()
    {
        if (!OperatingSystem.IsWindows()) return;
        var hwnd = Win32.CreateWindowExW(0, "STATIC", "range test", Win32.WS_OVERLAPPEDWINDOW,
            0, 0, 100, 100, 0, 0, Win32.GetModuleHandleW(null), 0);
        var scroller = new WindowsGridUiaScroller(hwnd, true);
        var nav = new NativeGridScrollFrame(new(new(1, 0), 1), null,
            NativeGridScrollAxis.Create(NativeGridExtentKind.Prefix, 1003, 3, 1000),
            NativeGridScrollAxis.Create(NativeGridExtentKind.Prefix, 18, 2, 16), new(1000, 3), new(16, 2), 1, true, "pending");
        try
        {
            scroller.Publish(nav, false);
            Assert.Equal(0, scroller.GetValue(out var value)); Assert.Equal(1000, value);
            Assert.Equal(0, scroller.GetMaximum(out value)); Assert.Equal(1000, value);
            Assert.Equal(0, scroller.GetLargeChange(out value)); Assert.Equal(3, value);
            Assert.Equal(0, scroller.GetIsReadOnly(out var readOnly)); Assert.Equal(1, readOnly);
            Assert.Equal(unchecked((int)0x80131509), scroller.SetValue(double.NaN));
            Assert.Equal(0, scroller.GetPatternProvider(10003, out var pattern)); Assert.NotEqual(0, pattern); Marshal.Release(pattern);
            Assert.Equal(0, scroller.GetPropertyValue(30013, out var help));
            try { Assert.Contains("file row total unknown", Marshal.PtrToStringBSTR(help.Pointer)); }
            finally { Marshal.FreeBSTR(help.Pointer); }
            scroller.Publish(nav with { Rows = NativeGridScrollAxis.Create(NativeGridExtentKind.Unavailable, 0, 1, 0) }, false);
            Assert.Equal(0, scroller.GetPatternProvider(10003, out pattern)); Assert.Equal(0, pattern);
            Assert.Equal(unchecked((int)0x80131509), scroller.GetMaximum(out _));
            Assert.Equal(0, scroller.GetPropertyValue(30013, out help));
            try { Assert.Contains("no admitted first CSV coordinate", Marshal.PtrToStringBSTR(help.Pointer)); }
            finally { Marshal.FreeBSTR(help.Pointer); }
            scroller.Publish(null, false);
            Assert.Equal(0, scroller.GetPatternProvider(10003, out pattern)); Assert.Equal(0, pattern);
            Assert.Equal(unchecked((int)0x80131509), scroller.GetValue(out _));
            scroller.Publish(nav with { Rows = NativeGridScrollAxis.Create(NativeGridExtentKind.Exact, 0, 1, 0) }, false);
            Assert.Equal(0, scroller.GetMaximum(out value)); Assert.Equal(0, value);
            Assert.Equal(0, scroller.GetLargeChange(out value)); Assert.Equal(0, value);
            Assert.Equal(0, scroller.GetPropertyValue(30013, out help));
            try { Assert.Contains("no admitted first CSV coordinate", Marshal.PtrToStringBSTR(help.Pointer)); }
            finally { Marshal.FreeBSTR(help.Pointer); }
        }
        finally { scroller.Detach(); Win32.DestroyWindow(hwnd); }
    }

    /// <summary>A native retained COM node cannot root the disposed attachment's indirect owner closures.</summary>
    [Fact]
    public void Detached_retained_com_node_releases_action_and_geometry_owner()
    {
        if (!OperatingSystem.IsWindows()) return;
        var retained = DetachedOwner();
        try
        {
            GC.Collect(2, GCCollectionMode.Forced, true, true); GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            Assert.False(retained.Owner.IsAlive);
            Assert.Equal(unchecked((int)0x80040201), retained.Node.GetRow(out _));
            GC.KeepAlive(retained.Node);
        }
        finally { Marshal.Release(retained.Pointer); }
    }

    /// <summary>A separate non-inlined scope prevents JIT local lifetime from becoming the owner-lifetime oracle.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    [SupportedOSPlatform("windows")]
    private static (WindowsGridUiaNode Node, WeakReference Owner, nint Pointer) DetachedOwner()
    {
        var owner = new OwnerActions(); var frame = Frame();
        var bridge = new WindowsGridUiaBridge(0, owner, owner.Bounds); bridge.Publish(frame);
        var node = bridge.Node(frame.Id, 1, 0, 0);
        var weak = new WeakReference(owner);
        var pointer = UiaComInterface.Pointer(node, typeof(IRawElementProviderSimpleAbi).GUID);
        bridge.Detach(); return (node, weak, pointer);
    }

    /// <summary>Represents an indirect adapter/controller owner, without a real document or large fixture.</summary>
    private sealed class OwnerActions : IGridAccessibilityActions
    {
        /// <summary>Instance geometry intentionally captures the same owner as the actions reference.</summary>
        internal UiaRect Bounds(int kind, int row, int column) => default;
        /// <inheritdoc />
        public GridAccessibilityResult MutateSelection(GridAccessibilityId id, GridSelectionMutation mutation) => GridAccessibilityResult.Applied;
        /// <inheritdoc />
        public GridAccessibilityResult Focus(GridAccessibilityId id, GridCoordinate? cell) => GridAccessibilityResult.Applied;
    }

    /// <summary>Reflection inspects the adapter publication, not a second provider-owned selection.</summary>
    private static GridAccessibilityFrame ReadFrame(WindowsCsvGrid grid) => (GridAccessibilityFrame)typeof(WindowsCsvGrid)
        .GetField("_accessibleFrame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(grid)!;
    private static GridRenderProjection Projection()
    {
        var row = new GridRow(1000, new(0, 1), new(1, 0), 18, [new(16, new(0, 1), new(0, 1), GridValueState.Complete, false)]);
        return new(0, 1, new(1003, null, null), AnalysisCompleteness.CoveredRegion, [new(0, 1)], null,
            new(1000, 3), new(16, 2), "a", [row], [], true, false, false, false);
    }
    private static GridAccessibilityFrame Frame() => NativeGridAccessibility.Create(new(new(1, 0), 1), null,
        new(new(1, 0), 1), Projection(), new(1000, 3), new(16, 2), new(new(999, 15), new(1001, 17), false), null, false);
    private static int ArrayLength(nint array) { Assert.Equal(0, SafeArrayGetUBound(array, 1, out var last)); return last + 1; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Count(nint self, out int count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Item(nint self, int row, int column, out nint provider);
    [DllImport("oleaut32.dll")] private static extern int SafeArrayGetUBound(nint array, uint dimension, out int bound);
    [DllImport("oleaut32.dll")] private static extern int SafeArrayDestroy(nint array);
    /// <summary>Records only actual attempted owner calls.</summary>
    private sealed class Actions : IGridAccessibilityActions
    {
        internal int Mutations;
        public GridAccessibilityResult MutateSelection(GridAccessibilityId id, GridSelectionMutation mutation) { Mutations++; return GridAccessibilityResult.Applied; }
        public GridAccessibilityResult Focus(GridAccessibilityId id, GridCoordinate? cell) => GridAccessibilityResult.Applied;
    }
}
