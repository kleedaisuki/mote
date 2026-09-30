using System.Reflection;
using Mote.Formats;
using Mote.Native;
using Mote.Native.Windows;
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
        using var grid = new WindowsCsvGrid(parent, 104, ThemePolicies.Get(ThemePolicies.DefaultId));
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
            { Assert.True(shell.CyclePaneFocus(false)); Assert.Equal(target, WindowsGridInterop.GetFocus()); }
            Assert.True(shell.CyclePaneFocus(true)); Assert.Equal(grid.CoordinateControl, WindowsGridInterop.GetFocus());
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

    /// <summary>Sets only the owned test HWND seams, never controller/document state.</summary>
    private static void Field(WindowsEditorShell shell, string name, object value) => typeof(WindowsEditorShell)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shell, value);

    /// <summary>Invokes the real shell accelerator handler for the owned synthetic pane focus.</summary>
    private static void Command(WindowsEditorShell shell, int id) => typeof(WindowsEditorShell)
        .GetMethod("HandleCommand", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, [id]);
}
