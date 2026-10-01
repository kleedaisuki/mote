using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mote.Formats;
using Mote.Native;
using Mote.Native.Windows;
using Mote.Native.Windows.Accessibility;
using Mote.Themes;

namespace Mote.Tests;

/// <summary>Owned offscreen HWND traversal; no global key injection, clipboard or source edits.</summary>
public sealed class WindowsGridPaneFocusTests
{
    /// <summary>F6 cycles actual enabled panes bidirectionally and does not route Tab through dialog navigation.</summary>
    [Fact]
    public void Pane_cycle_reads_back_focus_prunes_unavailable_and_preserves_source_text()
    {
        if (!OperatingSystem.IsWindows()) return;
        var parent = Win32.CreateWindowExW(0x08000080, "STATIC", "owned offscreen pane test", // NOACTIVATE | TOOLWINDOW.
            Win32.WS_OVERLAPPEDWINDOW | 0x10000000, -32000, -32000, 600, 400, 0, 0, Win32.GetModuleHandleW(null), 0);
        var source = Win32.CreateWindowExW(0, "EDIT", "source text", Win32.WS_CHILD | 0x10000000 | Win32.WS_TABSTOP,
            0, 0, 100, 40, parent, 0, Win32.GetModuleHandleW(null), 0);
        Assert.NotEqual(0, parent); Assert.NotEqual(0, source);
        using var grid = new WindowsCsvGrid(parent, 104, ThemePolicies.Get(ThemePolicies.DefaultId), accessibilityEnabled: true);
        var shell = new WindowsEditorShell();
        Field(shell, "_window", parent); Field(shell, "_editor", source); Field(shell, "_grid", grid);
        try
        {
            var identity = new NativePresentationId(new(4, 0), 1);
            var row = new GridRow(0, new(0, 1), new(1, 0), 1,
                [new(0, new(0, 1), new(0, 1), GridValueState.Complete, false)]);
            var projection = new GridRenderProjection(0, 1, new(1, 1, 1), AnalysisCompleteness.Complete,
                [new(0, 1)], 0, new(0, 1), new(0, 1), "a", [row], [], false, false, false, false);
            grid.Install(projection, identity);
            var navigation = new NativeGridScrollFrame(new(identity.Document, 1), identity,
                NativeGridScrollAxis.Create(NativeGridExtentKind.Exact, 1, 1, 0),
                NativeGridScrollAxis.Create(NativeGridExtentKind.Exact, 1, 1, 0),
                new(0, 1), new(0, 1), 1, false, "ready");
            grid.SetNavigation(navigation); grid.Resize(120, 0, 450, 330); grid.Show(true);
            Win32.SetFocus(source);
            foreach (var target in new[] { grid.Handle, grid.RowScroller, grid.ColumnScroller, grid.CoordinateControl, source })
            {
                Assert.True(shell.CyclePaneFocus(false)); Assert.Equal(target, WindowsGridInterop.GetFocus());
                AssertProviderFocus(grid, target);
            }
            foreach (var target in new[] { grid.CoordinateControl, grid.ColumnScroller, grid.RowScroller, grid.Handle, source })
            {
                Assert.True(shell.CyclePaneFocus(true)); Assert.Equal(target, WindowsGridInterop.GetFocus());
                AssertProviderFocus(grid, target);
            }
            Assert.True(shell.CyclePaneFocus(true)); Assert.Equal(grid.CoordinateControl, WindowsGridInterop.GetFocus());
            AssertProviderFocus(grid, grid.CoordinateControl);
            var sourceCuts = 0; var sourceCopies = 0; var gridCopies = 0;
            shell.CutRequested += () => sourceCuts++;
            shell.CopyRequested += () => sourceCopies++;
            grid.IntentRequested += intent => { if (intent.Kind == NativeGridIntentKind.CopyValue) gridCopies++; };
            Command(shell, 216); // Cut remains refused throughout the read-only Grid navigation group.
            Command(shell, 212); // Copy retains the Grid selection; it cannot fall through to hidden source.
            Assert.Equal(0, sourceCuts); Assert.Equal(0, sourceCopies); Assert.Equal(1, gridCopies);
            Win32.SetFocus(source);
            grid.SetNavigation(navigation with
            {
                Rows = NativeGridScrollAxis.Create(NativeGridExtentKind.Unavailable, 0, 1, 0),
                Columns = NativeGridScrollAxis.Create(NativeGridExtentKind.Unavailable, 0, 1, 0)
            });
            Assert.True(shell.CyclePaneFocus(false)); Assert.Equal(grid.Handle, WindowsGridInterop.GetFocus());
            Assert.True(shell.CyclePaneFocus(false)); Assert.Equal(grid.CoordinateControl, WindowsGridInterop.GetFocus());
            grid.Show(false); Win32.SetFocus(source);
            Assert.True(shell.CyclePaneFocus(false)); Assert.Equal(source, WindowsGridInterop.GetFocus());
            Field(shell, "_imeComposing", true);
            Assert.False(shell.CyclePaneFocus(false)); Assert.Equal(source, WindowsGridInterop.GetFocus());
            var buffer = new char[32];
            var length = Win32.GetWindowTextW(source, buffer, buffer.Length);
            Assert.Equal("source text", new string(buffer, 0, length));
        }
        finally { grid.Dispose(); Win32.DestroyWindow(parent); }
    }

    /// <summary>Initial and repeated ready Grid publication must not steal source HWND focus.</summary>
    [Fact]
    public void Source_focus_survives_initial_install_reinstall_selection_and_provider_reads()
    {
        if (!OperatingSystem.IsWindows()) return;
        var parent = Win32.CreateWindowExW(0x08000080, "STATIC", "owned initial focus test",
            Win32.WS_OVERLAPPEDWINDOW | 0x10000000, -32000, -32000, 600, 400, 0, 0, Win32.GetModuleHandleW(null), 0);
        Assert.NotEqual(0, parent);
        try
        {
            var source = Win32.CreateWindowExW(0, "EDIT", "source text", Win32.WS_CHILD | 0x10000000 | Win32.WS_TABSTOP,
                0, 0, 100, 40, parent, 0, Win32.GetModuleHandleW(null), 0);
            Assert.NotEqual(0, source);
            Win32.SetFocus(source);
            Assert.Equal(source, WindowsGridInterop.GetFocus());
            using var grid = new WindowsCsvGrid(parent, 104, ThemePolicies.Get(ThemePolicies.DefaultId), accessibilityEnabled: true);
            Assert.Equal(source, WindowsGridInterop.GetFocus());
            var identity = new NativePresentationId(new(4, 0), 1);
            var projection = Projection();
            var navigation = Navigation(identity);
            // No focus reset after any operation: the first differing readback identifies the boundary.
            grid.Install(projection, identity); AssertSourceFocus(grid, source, "Install");
            grid.SetNavigation(navigation); AssertSourceFocus(grid, source, "SetNavigation");
            grid.Resize(120, 0, 450, 330); AssertSourceFocus(grid, source, "Resize");
            grid.Show(true); AssertSourceFocus(grid, source, "Show");
            grid.Install(projection, identity); AssertSourceFocus(grid, source, "same-version Install");
            grid.SetNavigation(navigation); AssertSourceFocus(grid, source, "same-version SetNavigation");
            grid.Install(Projection(), identity); AssertSourceFocus(grid, source, "replacement projection Install");
            AssertSelectionPreservesFocus(grid, source);
            var buffer = new char[32];
            var length = Win32.GetWindowTextW(source, buffer, buffer.Length);
            Assert.Equal("source text", new string(buffer, 0, length));
        }
        finally { Win32.DestroyWindow(parent); }
    }

    /// <summary>Selection-only accessibility calls leave the source HWND focused, including no-change calls.</summary>
    [SupportedOSPlatform("windows")]
    private static void AssertSelectionPreservesFocus(WindowsCsvGrid grid, nint source)
    {
        var frame = Read<GridAccessibilityFrame>(grid, "_accessibleFrame");
        Assert.Equal(GridAccessibilityResult.Applied, grid.MutateSelection(frame.Id, new GridSelectionMutation.Clear()));
        AssertSourceFocus(grid, source, "Clear selection");
        Assert.Equal(GridAccessibilityResult.NoChange, grid.MutateSelection(frame.Id, new GridSelectionMutation.Clear()));
        AssertSourceFocus(grid, source, "Clear unchanged selection");
        Assert.Equal(GridAccessibilityResult.Applied, grid.MutateSelection(frame.Id,
            new GridSelectionMutation.ReplaceRectangle(new(new(0, 0), new(0, 0), false))));
        AssertSourceFocus(grid, source, "Replace selection");
    }

    /// <summary>Reads independent native and UIA facts without repairing the focus under test.</summary>
    [SupportedOSPlatform("windows")]
    private static void AssertSourceFocus(WindowsCsvGrid grid, nint source, string boundary)
    {
        Assert.True(WindowsGridInterop.GetFocus() == source, $"Source focus changed at {boundary}.");
        AssertProviderFocus(grid, source);
        Assert.True(WindowsGridInterop.GetFocus() == source, $"Provider reads changed source focus at {boundary}.");
    }

    /// <summary>Both scrollers and the table/cell must agree with the exact focused native pane.</summary>
    [SupportedOSPlatform("windows")]
    private static void AssertProviderFocus(WindowsCsvGrid grid, nint focused)
    {
        var frame = Read<GridAccessibilityFrame>(grid, "_accessibleFrame");
        var bridge = Read<WindowsGridUiaBridge>(grid, "_uia");
        var row = Read<WindowsGridUiaScroller>(grid, "_accessibleRowScroller");
        var column = Read<WindowsGridUiaScroller>(grid, "_accessibleColumnScroller");
        var root = bridge.Node(frame.Id, 0, 0, 0);
        var cell = bridge.Node(frame.Id, 1, 0, 0);
        AssertFocusProperty(row, focused == grid.RowScroller);
        AssertFocusProperty(column, focused == grid.ColumnScroller);
        AssertFocusProperty(root, focused == grid.Handle && frame.FocusedCell is null);
        AssertFocusProperty(cell, focused == grid.Handle && frame.FocusedCell == new GridCoordinate(0, 0));
        Assert.Equal(focused == grid.Handle, frame.HasTableFocus);
        Assert.Equal(0, cell.GetPropertyValue(30005, out var name));
        try { Assert.NotNull(Marshal.PtrToStringBSTR(name.Pointer)); }
        finally { Marshal.FreeBSTR(name.Pointer); }
        Assert.Equal(focused, WindowsGridInterop.GetFocus());
    }

    /// <summary>UIA HasKeyboardFocus is a VT_BOOL, not a guessed enabled or selected state.</summary>
    private static void AssertFocusProperty(IRawElementProviderSimpleAbi provider, bool expected)
    {
        Assert.Equal(0, provider.GetPropertyValue(30008, out var value));
        Assert.Equal(11, value.Type);
        Assert.Equal(expected ? -1 : 0, value.Integer);
    }

    /// <summary>Reads only the retained owned adapter facts used by its real UIA providers.</summary>
    [SupportedOSPlatform("windows")]
    private static T Read<T>(WindowsCsvGrid grid, string name) => (T)typeof(WindowsCsvGrid)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(grid)!;

    /// <summary>A single complete cell makes item focus and source focus distinguishable.</summary>
    private static GridRenderProjection Projection()
    {
        var row = new GridRow(0, new(0, 1), new(1, 0), 1,
            [new(0, new(0, 1), new(0, 1), GridValueState.Complete, false)]);
        return new(0, 1, new(1, 1, 1), AnalysisCompleteness.Complete,
            [new(0, 1)], 0, new(0, 1), new(0, 1), "a", [row], [], false, false, false, false);
    }

    /// <summary>Both exact nonempty navigation axes remain enabled in the shell cycle.</summary>
    private static NativeGridScrollFrame Navigation(NativePresentationId identity) => new(new(identity.Document, 1), identity,
        NativeGridScrollAxis.Create(NativeGridExtentKind.Exact, 1, 1, 0),
        NativeGridScrollAxis.Create(NativeGridExtentKind.Exact, 1, 1, 0),
        new(0, 1), new(0, 1), 1, false, "ready");

    /// <summary>Sets only the owned test HWND seams, never controller/document state.</summary>
    private static void Field(WindowsEditorShell shell, string name, object value) => typeof(WindowsEditorShell)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shell, value);

    /// <summary>Invokes the real shell accelerator handler for the owned synthetic pane focus.</summary>
    private static void Command(WindowsEditorShell shell, int id) => typeof(WindowsEditorShell)
        .GetMethod("HandleCommand", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, [id]);
}
