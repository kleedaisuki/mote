using System.Diagnostics;
using System.Runtime.InteropServices;
using Xunit.Abstractions;
using System.Runtime.Versioning;
using Mote.Formats;
using Mote.Native;
using Mote.Native.Windows;
using Mote.Themes;

namespace Mote.Tests;

/// <summary>Real hidden HWND logical-range and gesture-token acceptance, without desktop or clipboard changes.</summary>
public sealed class NativeGridLogicalWindowsTests(ITestOutputHelper output)
{
    /// <summary>Reports synchronous hidden-HWND install/readback cost, not rendering or physical paint latency.</summary>
    [Fact]
    public void Bounded_install_readback_observation()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var host = new Host();
        var frame = Frame();
        for (var index = 0; index < 10; index++) host.Grid.SetNavigation(frame);
        var measurements = new double[100];
        for (var index = 0; index < measurements.Length; index++)
        {
            var start = Stopwatch.GetTimestamp();
            host.Grid.SetNavigation(frame with { RequestSerial = index + 1 });
            measurements[index] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        Array.Sort(measurements);
        output.WriteLine($"Hidden HWND 16-row/8-column range install+readback: n=100 warmup=10 p50={measurements[49]:F4}ms p95={measurements[94]:F4}ms; {RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}; {RuntimeInformation.FrameworkDescription}");
    }

    /// <summary>Large ordinals use native 32-bit ranges while retained item storage remains bounded.</summary>
    [Fact]
    public void Logical_ranges_exceed_16_bits_and_pending_slots_retire_commands()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var host = new Host();
        var frame = Frame();
        host.Grid.SetNavigation(frame);
        var info = new WindowsGridInterop.Scroll { Size = (uint)Marshal.SizeOf<WindowsGridInterop.Scroll>(), Mask = 7 };
        Assert.True(WindowsGridInterop.GetScrollInfo(host.Grid.RowScroller, 2, ref info));
        Assert.Equal(999_999, info.Maximum);
        Assert.Equal(700_000, info.Position);
        Assert.Equal(10u, info.Page);
        Assert.Equal(16, (int)Win32.SendMessageW(host.Grid.Handle, WindowsGridInterop.GetItemCount, 0, 0));
        var intents = new List<NativeGridIntent>();
        host.Grid.IntentRequested += intents.Add;
        host.Grid.Copy();
        Assert.Empty(intents);
        host.Grid.SetNavigation(frame with { Rows = NativeGridScrollAxis.Create(NativeGridExtentKind.Exact, 0, 10, 0),
            RequestedRows = new(0, 16) });
        Assert.Equal(0, (int)Win32.SendMessageW(host.Grid.Handle, WindowsGridInterop.GetItemCount, 0, 0));
    }

    /// <summary>Tracking survives same-navigation installs, consumes once, and cancellation retains the original token.</summary>
    [Fact]
    public void Frozen_gesture_tokens_survive_install_and_cancel_without_duplicate_terminal()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var host = new Host();
        var frame = Frame();
        host.Grid.SetNavigation(frame);
        var begins = new List<NativeGridGestureBegin>();
        var actions = new List<NativeGridGestureAction>();
        long sequence = 0;
        host.Grid.GestureBeginning += begin =>
        {
            begins.Add(begin);
            return new(new(begin.Frame.Navigation, ++sequence), begin.Frame);
        };
        host.Grid.GestureRequested += actions.Add;
        Assert.True(host.Grid.HandleScroll(0x0115, 5, host.Grid.RowScroller));
        host.Grid.SetNavigation(frame with { RequestSerial = 2, Status = "new pending installation" });
        Assert.True(host.Grid.HandleScroll(0x0115, 5, host.Grid.RowScroller));
        Assert.Single(begins);
        Assert.Equal(actions[0].Id, actions[1].Id);
        Win32.SendMessageW(host.Grid.RowScroller, 0x001F, 0, 0);
        Assert.Equal(NativeGridGesturePhase.Cancel, actions[^1].Phase);
        Assert.Equal(actions[0].Id, actions[^1].Id);
        var count = actions.Count;
        host.Grid.HandleScroll(0x0115, 8, host.Grid.RowScroller);
        Assert.Equal(count, actions.Count);
        host.Grid.HandleScroll(0x0115, 1, host.Grid.RowScroller);
        Assert.Equal(700_001, actions[^1].Row);
        Assert.Equal(NativeGridGesturePhase.Commit, actions[^1].Phase);
        Assert.Equal(2, begins.Count);
        Assert.InRange(begins[^1].VisibleRows, 1, 256);
        Assert.InRange(begins[^1].VisibleColumns, 1, 64);
        Assert.False(host.Grid.HandleScroll(0x0115, 1, host.Grid.Handle));
    }

    /// <summary>End and retry remain symbolic; column actions use absolute column units.</summary>
    [Fact]
    public void Symbolic_commands_and_column_steps_keep_navigation_domain()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var host = new Host();
        host.Grid.SetNavigation(Frame());
        var actions = new List<NativeGridGestureAction>();
        long sequence = 0;
        host.Grid.GestureBeginning += begin => new(new(begin.Frame.Navigation, ++sequence), begin.Frame);
        host.Grid.GestureRequested += actions.Add;
        host.Grid.HandleScroll(0x0114, 1, host.Grid.ColumnScroller);
        Assert.Equal(701, actions[^1].Column);
        Win32.SendMessageW(host.Grid.Handle, Win32.WM_KEYDOWN, 0x23, 0);
        Assert.Equal(NativeGridTargetKind.End, actions[^1].Kind);
        Win32.SendMessageW(host.Grid.Handle, Win32.WM_KEYDOWN, 0x74, 0);
        Assert.Equal(NativeGridTargetKind.Retry, actions[^1].Kind);
        Win32.SendMessageW(host.Grid.Handle, Win32.WM_KEYDOWN, 0x22, 0);
        Assert.Equal(700_010, actions[^1].Row);
        Assert.Equal(0, (int)Win32.SendMessageW(host.Grid.Handle, WindowsGridInterop.First + 39, 0, 0));
        host.Grid.SetNavigation(null);
        var count = actions.Count;
        host.Grid.HandleScroll(0x0115, 1, host.Grid.RowScroller);
        Assert.Equal(count, actions.Count);
    }

    /// <summary>Sparse delivery retains a pending ordinal slot and exact file width clips cache columns.</summary>
    [Fact]
    public void Sparse_ready_rows_do_not_compact_gaps_or_display_columns_beyond_extent()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var host = new Host();
        var identity = new NativePresentationId(new(8, 1), 1);
        var first = new GridRow(0, new(0, 3), new(3, 1), 2,
            [new(0, new(0, 1), new(0, 1), GridValueState.Complete, false)]);
        var third = new GridRow(2, new(4, 3), new(7, 1), 2,
            [new(0, new(4, 1), new(1, 1), GridValueState.Complete, false)]);
        var grid = new GridRenderProjection(1, 8, new(0, 3, 2), AnalysisCompleteness.CoveredRegion,
            [new(0, 8)], null, new(0, 3), new(0, 8), "ac", [first, third], [], false, false, false, false);
        host.Grid.Install(grid, identity);
        var frame = Frame() with { Ready = identity, Pending = false,
            Rows = NativeGridScrollAxis.Create(NativeGridExtentKind.Exact, 3, 3, 0),
            Columns = NativeGridScrollAxis.Create(NativeGridExtentKind.Exact, 2, 2, 0),
            RequestedRows = new(0, 3), RequestedColumns = new(0, 2) };
        host.Grid.SetNavigation(frame);
        var slots = (GridRow?[])typeof(WindowsCsvGrid).GetField("_slots",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(host.Grid)!;
        Assert.Equal(3, slots.Length);
        Assert.Equal(0, slots[0]!.Ordinal);
        Assert.Null(slots[1]);
        Assert.Equal(2, slots[2]!.Ordinal);
        Assert.Equal(3, (int)Win32.SendMessageW(host.Grid.Handle, WindowsGridInterop.GetItemCount, 0, 0));
        var header = Win32.SendMessageW(host.Grid.Handle, WindowsGridInterop.First + 31, 0, 0);
        Assert.Equal(3, (int)Win32.SendMessageW(header, 0x1200, 0, 0)); // Row gutter plus two file columns.
    }

    /// <summary>Geometry changes can synchronously reinstall ranges without recursive repeated notifications.</summary>
    [Fact]
    public void Measured_geometry_change_is_deduplicated_under_reentrant_install()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var host = new Host();
        var frame = Frame();
        host.Grid.SetNavigation(frame);
        var notifications = 0;
        host.Grid.GeometryChanged += (rows, columns) =>
        {
            notifications++;
            frame = frame with { Rows = NativeGridScrollAxis.Create(frame.Rows.Kind, frame.Rows.Count, rows, frame.Rows.First),
                Columns = NativeGridScrollAxis.Create(frame.Columns.Kind, frame.Columns.Count, columns, frame.Columns.First) };
            if (OperatingSystem.IsWindows()) host.Grid.SetNavigation(frame);
        };
        host.Grid.Resize(0, 0, 350, 150);
        Assert.Equal(1, notifications);
        host.Grid.Resize(0, 0, 350, 150);
        Assert.Equal(1, notifications);
    }

    /// <summary>A newer native installation owns ranges and slots after a nested SetItemCount callback.</summary>
    [Fact]
    public void Nested_range_installation_survives_obsolete_outer_native_messages()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var host = new Host();
        var original = Frame();
        var newer = original with { RequestSerial = 3, RequestedRows = new(800_000, 8),
            Rows = NativeGridScrollAxis.Create(NativeGridExtentKind.Exact, 1_000_000, 10, 800_000) };
        host.Grid.SetNavigation(original);
        var nested = false;
        Win32.SubclassProcedure callback = (window, message, parameter, data, id, reference) =>
        {
            var result = Win32.DefSubclassProc(window, message, parameter, data);
            if (OperatingSystem.IsWindows() && message == WindowsGridInterop.SetItemCount && !nested)
            {
                nested = true;
                host.Grid.SetNavigation(newer);
            }
            return result;
        };
        Assert.True(Win32.SetWindowSubclass(host.Grid.Handle, callback, 93, 0));
        try
        {
            host.Grid.SetNavigation(original with { RequestSerial = 2 });
            Assert.True(nested);
            Assert.True(host.Grid.HasNavigation(newer));
            Assert.Equal(8, (int)Win32.SendMessageW(host.Grid.Handle, WindowsGridInterop.GetItemCount, 0, 0));
            var info = new WindowsGridInterop.Scroll { Size = (uint)Marshal.SizeOf<WindowsGridInterop.Scroll>(), Mask = 7 };
            Assert.True(WindowsGridInterop.GetScrollInfo(host.Grid.RowScroller, 2, ref info));
            Assert.Equal(800_000, info.Position);
        }
        finally { Win32.RemoveWindowSubclass(host.Grid.Handle, callback, 93); GC.KeepAlive(callback); }
    }

    private static NativeGridScrollFrame Frame() => new(new(new(8, 1), 5), null,
        NativeGridScrollAxis.Create(NativeGridExtentKind.Exact, 1_000_000, 10, 700_000),
        NativeGridScrollAxis.Create(NativeGridExtentKind.Prefix, 1_000, 3, 700),
        new(700_000, 16), new(700, 8), 1, true, "pending");

    /// <summary>All HWNDs are hidden and destroyed before managed callback roots retire.</summary>
    [SupportedOSPlatform("windows")]
    private sealed class Host : IDisposable
    {
        private readonly nint _parent;
        internal WindowsCsvGrid Grid { get; }
        internal Host()
        {
            _parent = Win32.CreateWindowExW(0, "STATIC", "logical grid test", Win32.WS_OVERLAPPEDWINDOW,
                0, 0, 800, 500, 0, 0, Win32.GetModuleHandleW(null), 0);
            Assert.NotEqual(0, _parent);
            Grid = new(_parent, 104, ThemePolicies.Get(ThemePolicies.DefaultId));
            Grid.Resize(0, 0, 700, 400);
        }
        public void Dispose() { Grid.Dispose(); Win32.DestroyWindow(_parent); }
    }
}
