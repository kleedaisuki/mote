using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mote.Formats;
using Mote.Native;
using Mote.Native.Windows;
using Mote.Themes;

namespace Mote.Tests;

/// <summary>Actual hidden owner-data HWND contracts; no desktop input or clipboard mutation is used.</summary>
public sealed class NativeCsvGridWindowsTests
{
    /// <summary>Native text requests use delivered coordinates, a row gutter and scalar-safe callback buffers.</summary>
    [Fact]
    public void Owner_data_callbacks_return_ready_cells_and_never_split_surrogates()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var window = new Window();
        window.Grid.Install(Projection(), new(new(8, 1), 1));
        Assert.Equal(2, (int)Win32.SendMessageW(window.Grid.Handle, WindowsGridInterop.GetItemCount, 0, 0));
        Assert.Equal("1", window.Label(0, 0));
        Assert.Equal("😀", window.Label(0, 1));
        Assert.Equal("b", window.Label(0, 2));
        Assert.Equal("", window.Label(0, 1, 2)); // Capacity includes NUL; cannot return half of emoji.
        Assert.True(window.Callbacks > 0);
        Assert.Empty(window.Errors);
        window.Grid.Install(null, default);
        Assert.Equal(0, (int)Win32.SendMessageW(window.Grid.Handle, WindowsGridInterop.GetItemCount, 0, 0));
    }

    /// <summary>Real hit-test, key, Copy, read-only clipboard and same-version installs retain exact identities.</summary>
    [Fact]
    public void Pointer_keyboard_and_same_version_install_preserve_cell_selection()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var window = new Window();
        var identity = new NativePresentationId(new(8, 1), 1);
        window.Grid.Install(Projection(), identity);
        var intents = new List<NativeGridIntent>();
        window.Grid.IntentRequested += intents.Add;
        window.Click(1, 2);
        Assert.Equal(new NativeGridIntent(identity, NativeGridIntentKind.Select, 1, 1, 1, 1), intents[^1]);
        Win32.SendMessageW(window.Grid.Handle, Win32.WM_KEYDOWN, 13, 0);
        Assert.Equal(NativeGridIntentKind.Reveal, intents[^1].Kind);
        Win32.SendMessageW(window.Grid.Handle, Win32.WM_KEYDOWN, 0x71, 0);
        Assert.Equal(NativeGridIntentKind.Replace, intents[^1].Kind);
        window.Grid.Install(Projection(), identity with { Sequence = 2 });
        window.Grid.Copy();
        Assert.Equal(new NativeGridIntent(identity with { Sequence = 2 }, NativeGridIntentKind.CopyValue, 1, 1), intents[^1]);
        window.Click(0, 1, shift: true);
        window.Grid.Copy();
        Assert.Equal(NativeGridIntentKind.CopyTsv, intents[^1].Kind);
        Assert.Equal((1, 1, 0, 0), (intents[^1].Row, intents[^1].Column, intents[^1].EndRow, intents[^1].EndColumn));
        Win32.SendMessageW(window.Grid.Handle, Win32.WM_KEYDOWN, 13, 0);
        Assert.Equal(new NativeGridIntent(identity with { Sequence = 2 }, NativeGridIntentKind.Reveal, 0, 0), intents[^1]);
        window.Click(0, 0);
        window.Grid.Copy();
        Assert.True(intents[^1].WholeRows);
        Assert.Equal(NativeGridIntentKind.CopyRows, intents[^1].Kind);
        var count = intents.Count;
        Win32.SendMessageW(window.Grid.Handle, (int)Win32.WM_CUT, 0, 0);
        Win32.SendMessageW(window.Grid.Handle, (int)Win32.WM_PASTE, 0, 0);
        Assert.Equal(count, intents.Count);
        Assert.Equal("😀", window.Label(0, 1));
        window.Grid.Install(null, default);
        window.Grid.Copy();
        Assert.Equal(count, intents.Count);
        Assert.Empty(window.Errors);
    }

    /// <summary>Repeated edge input coalesces requests and off-window selections retain real logical coordinates.</summary>
    [Fact]
    public void Edge_navigation_rebases_bounded_window_without_fake_extent()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var window = new Window();
        var identity = new NativePresentationId(new(8, 1), 1);
        window.Grid.Install(Projection(exactCount: 10), identity);
        var requests = new List<NativeGridWindowRequest>();
        var intents = new List<NativeGridIntent>();
        window.Grid.WindowRequested += requests.Add;
        window.Grid.IntentRequested += intents.Add;
        window.Click(1, 1);
        for (var index = 0; index < 10; index++) Win32.SendMessageW(window.Grid.Handle, Win32.WM_KEYDOWN, 0x28, 0);
        Assert.Single(requests);
        Assert.Equal(2, requests[0].Row);
        Assert.Equal(identity, requests[0].Identity);
        Assert.InRange(requests[0].RowLimit, 1, 256);
        window.Grid.Install(Projection(firstRow: 2, exactCount: 10), identity with { Sequence = 2 });
        window.Grid.Copy();
        Assert.Equal(2, intents[^1].Row);
        Assert.Equal(2, intents[^1].Identity.Sequence);
        window.Grid.Install(Projection(firstRow: 4, exactCount: 10), identity with { Sequence = 3 });
        window.Grid.Copy();
        Assert.Equal(2, intents[^1].Row); // Off-window anchor is not silently reassigned to row 4.
        Assert.Equal(2, (int)Win32.SendMessageW(window.Grid.Handle, WindowsGridInterop.GetItemCount, 0, 0));
        Assert.Empty(window.Errors);
    }

    /// <summary>Grid-focused shell Cut cannot delete the prior source selection; Copy/Select All stay table-local.</summary>
    [Fact]
    public void Shell_accelerators_cannot_cut_hidden_source_selection()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var window = new Window();
        window.Grid.Install(Projection(), new(new(8, 1), 1));
        Win32.SetFocus(window.Grid.Handle);
        Assert.True(window.Grid.HasFocus);
        var shell = new WindowsEditorShell();
        typeof(WindowsEditorShell).GetField("_grid", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(shell, window.Grid);
        var cuts = 0; var copies = 0; var selects = 0;
        shell.CutRequested += () => cuts++;
        shell.CopyRequested += () => copies++;
        shell.SelectAllRequested += () => selects++;
        var intents = new List<NativeGridIntent>();
        window.Grid.IntentRequested += intents.Add;
        var command = typeof(WindowsEditorShell).GetMethod("HandleCommand", BindingFlags.NonPublic | BindingFlags.Instance)!;
        command.Invoke(shell, [216]); // Cut
        command.Invoke(shell, [212]); // Copy
        command.Invoke(shell, [211]); // SelectAll
        Assert.Equal((0, 0, 0), (cuts, copies, selects));
        Assert.Equal(NativeGridIntentKind.CopyValue, intents[0].Kind);
        Assert.Equal(NativeGridIntentKind.Select, intents[1].Kind);
        Assert.Equal(1, intents[1].EndRow);
        Assert.Equal(1, intents[1].EndColumn);
    }

    /// <summary>The production multiline input retains mixed line endings and values longer than old 4Ki prompts.</summary>
    [Fact]
    public void Multiline_replacement_input_roundtrips_literal_initial_value()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var window = new Window();
        var value = "a\r\nb\nc\rd😀" + new string('x', 5000);
        var input = Win32TextPrompt.CreateInput(window.Parent, value, multiline: true);
        Assert.NotEqual(0, input);
        var buffer = new char[value.Length + 1];
        var count = Win32.GetWindowTextW(input, buffer, buffer.Length);
        Assert.Equal(value, new string(buffer, 0, count));
        Win32.DestroyWindow(input);
    }

    /// <summary>Logical navigation is bounded and retains the identity captured before a modal UI loop.</summary>
    [Fact]
    public void Coordinate_navigation_keeps_frozen_identity_and_rejects_invalid_coordinates()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var window = new Window();
        var old = new NativePresentationId(new(8, 1), 1);
        window.Grid.Install(Projection(), old);
        window.Grid.Install(Projection(), old with { Sequence = 2 });
        var requests = new List<NativeGridWindowRequest>();
        window.Grid.WindowRequested += requests.Add;
        Assert.True(window.Grid.RequestCoordinate("2147483647:2147483647", old));
        Assert.Equal(old, requests[^1].Identity);
        Assert.Equal(int.MaxValue - 1, requests[^1].Row);
        Assert.Equal(new GridRange(int.MaxValue - 1, 1), requests[^1].Columns);
        Assert.False(window.Grid.RequestCoordinate("0:1", old));
        Assert.False(window.Grid.RequestCoordinate("1:-1", old));
        Assert.False(window.Grid.RequestCoordinate("1:1:1", old));
        Assert.Single(requests);
    }

    /// <summary>Real bounded native scrollbars coalesce edge requests in both axes without a whole-file item count.</summary>
    [Fact]
    public void Native_scrollbar_edges_request_overlapping_windows()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var window = new Window();
        window.Grid.Install(Projection(exactCount: 10), new(new(8, 1), 1));
        Win32.MoveWindow(window.Grid.Handle, 0, 0, 150, 45, false);
        var requests = new List<NativeGridWindowRequest>();
        window.Grid.WindowRequested += requests.Add;
        Win32.SendMessageW(window.Grid.Handle, 0x0115, 7, 0); // SB_BOTTOM
        Win32.SendMessageW(window.Grid.Handle, 0x0115, 7, 0);
        Assert.Single(requests);
        Assert.Equal(1, requests[0].Row);
        Win32.SendMessageW(window.Grid.Handle, 0x0114, 7, 0); // SB_RIGHT
        Assert.Equal(2, requests.Count);
        Assert.Equal(1, requests[^1].Columns.Start);
        Assert.InRange(requests[^1].Columns.Count, 1, 64);
        Assert.Equal(2, (int)Win32.SendMessageW(window.Grid.Handle, WindowsGridInterop.GetItemCount, 0, 0));
        Assert.Empty(window.Errors);
    }

    /// <summary>A known EOF/last-column boundary refuses invalid native coordinate navigation before emission.</summary>
    [Fact]
    public void Coordinate_navigation_refuses_known_extent_and_retains_frozen_extent_after_modal_rebase()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var window = new Window();
        var identity = new NativePresentationId(new(8, 1), 1);
        var grid = Projection();
        window.Grid.Install(grid, identity);
        var requests = new List<NativeGridWindowRequest>();
        window.Grid.WindowRequested += requests.Add;
        Assert.False(window.Grid.RequestCoordinate("3:1", identity));
        Assert.False(window.Grid.RequestCoordinate("1:3", identity));
        Assert.True(window.Grid.RequestCoordinate("2:2", identity));
        Assert.Single(requests);
        window.Grid.Install(Projection(exactCount: 10), identity with { Sequence = 2 });
        Assert.False(window.Grid.RequestCoordinate("3:1", identity, grid.Extent));
        Assert.Single(requests);
    }

    /// <summary>Grid visibility notifications cannot let an outer obsolete analysis clear a newer native table.</summary>
    [Fact]
    public void Grid_visibility_nested_analysis_keeps_newer_ready_table()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var window = new Window();
        var library = Win32.LoadLibraryW("msftedit.dll");
        Assert.NotEqual(0, library);
        var editor = Win32.CreateWindowExW(0, "RICHEDIT50W", "", Win32.WS_CHILD | Win32.ES_MULTILINE,
            0, 0, 100, 100, window.Parent, 0, Win32.GetModuleHandleW(null), 0);
        var preview = Win32.CreateWindowExW(0, "RICHEDIT50W", "", Win32.WS_CHILD | Win32.ES_MULTILINE | Win32.ES_READONLY,
            0, 0, 100, 100, window.Parent, 0, Win32.GetModuleHandleW(null), 0);
        Assert.NotEqual(0, editor);
        Assert.NotEqual(0, preview);
        var shell = new WindowsEditorShell();
        var stamp = new NativeDocumentStamp(8, 1);
        var fields = BindingFlags.NonPublic | BindingFlags.Instance;
        foreach (var (name, value) in new (string, object)[]
        {
            ("_window", window.Parent), ("_editor", editor), ("_preview", preview), ("_grid", window.Grid),
            ("_document", new NativeDocumentView("", "", 0, 0, false, "", stamp))
        }) typeof(WindowsEditorShell).GetField(name, fields)!.SetValue(shell, value);
        var older = new NativeAnalysisView([], "", "obsolete", "", stamp, PresentationSequence: 1, ShowPreview: false);
        var newer = older with { Grid = Projection(), ShowPreview = true, PresentationSequence = 2 };
        var nested = false;
        Exception? error = null;
        Win32.SubclassProcedure callback = (handle, message, parameter, data, id, reference) =>
        {
            if (message == 0x0018 && !nested)
            {
                nested = true;
                try { if (OperatingSystem.IsWindows()) shell.SetAnalysis(newer); }
                catch (Exception failure) { error = failure; }
            }
            return Win32.DefSubclassProc(handle, message, parameter, data);
        };
        Assert.True(Win32.SetWindowSubclass(window.Grid.Handle, callback, 91, 0));
        try
        {
            shell.SetAnalysis(older);
            Assert.True(nested);
            Assert.Null(error);
            Assert.Same(newer, typeof(WindowsEditorShell).GetField("_analysis", fields)!.GetValue(shell));
            Assert.Equal(newer.Identity, typeof(WindowsCsvGrid).GetField("_identity", fields)!.GetValue(window.Grid));
            Assert.Equal(2, (int)Win32.SendMessageW(window.Grid.Handle, WindowsGridInterop.GetItemCount, 0, 0));
            Assert.Equal("😀", window.Label(0, 1));
        }
        finally
        {
            Win32.RemoveWindowSubclass(window.Grid.Handle, callback, 91);
            Win32.DestroyWindow(editor);
            Win32.DestroyWindow(preview);
            FreeLibrary(library);
            GC.KeepAlive(callback);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(nint module);

    private static GridRenderProjection Projection(int firstRow = 0, int exactCount = 2)
    {
        var row0 = new GridRow(firstRow, new(0, 4), new(4, 1), 2,
            [new(0, new(0, 2), new(0, 2), GridValueState.Complete, false), new(1, new(3, 1), new(2, 1), GridValueState.Complete, false)]);
        var row1 = new GridRow(firstRow + 1, new(5, 3), new(8, 1), 2,
            [new(0, new(5, 1), new(3, 1), GridValueState.Complete, false), new(1, new(7, 1), new(4, 1), GridValueState.Complete, false)]);
        return new(1, 9, new(0, exactCount, 2), AnalysisCompleteness.CoveredRegion, [new(0, 9)], null,
            new(firstRow, 2), new(0, 2), "😀bcd", [row0, row1], [], false, false, false, false);
    }

    /// <summary>Hidden parent forwards real native notifications; all callback roots retire before HWND teardown.</summary>
    [SupportedOSPlatform("windows")]
    private sealed class Window : IDisposable
    {
        private readonly Win32.SubclassProcedure _procedure;
        internal readonly WindowsCsvGrid Grid;
        internal readonly nint Parent;
        internal readonly List<Exception> Errors = [];
        internal int Callbacks;

        internal Window()
        {
            Parent = Win32.CreateWindowExW(0, "STATIC", "Grid test", Win32.WS_OVERLAPPEDWINDOW,
                0, 0, 800, 500, 0, 0, Win32.GetModuleHandleW(null), 0);
            Assert.NotEqual(0, Parent);
            _procedure = Notify;
            Assert.True(Win32.SetWindowSubclass(Parent, _procedure, 88, 0));
            Grid = new WindowsCsvGrid(Parent, 104, ThemePolicies.Get(ThemePolicies.DefaultId));
            Grid.Faulted += Errors.Add;
            Win32.MoveWindow(Grid.Handle, 0, 0, 700, 400, false);
            Win32.ShowWindow(Grid.Handle, 5);
        }

        private nint Notify(nint window, uint message, nuint parameter, nint data, nuint id, nuint reference)
        {
            if (message == Win32.WM_NOTIFY && Grid is not null)
            {
                Callbacks++;
                if (Grid.HandleNotify(data, out var result)) return result;
            }
            return Win32.DefSubclassProc(window, message, parameter, data);
        }

        internal string Label(int row, int column, int capacity = 32)
        {
            var buffer = Marshal.AllocHGlobal((capacity + 1) * sizeof(char));
            try
            {
                for (var index = 0; index <= capacity; index++) Marshal.WriteInt16(buffer, index * sizeof(char), '!');
                var item = new WindowsGridInterop.Item { Mask = 1, Row = row, Column = column, Text = buffer, TextCapacity = capacity };
                Assert.NotEqual(0, Send(WindowsGridInterop.GetItem, (nuint)0, item));
                Assert.Equal('!', (char)Marshal.ReadInt16(buffer, capacity * sizeof(char)));
                return Marshal.PtrToStringUni(buffer)!;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        internal void Click(int row, int column, bool shift = false)
        {
            var bounds = new Win32.Rect { Top = column, Left = 0 };
            var memory = Marshal.AllocHGlobal(Marshal.SizeOf<Win32.Rect>());
            try
            {
                Marshal.StructureToPtr(bounds, memory, false);
                Assert.NotEqual(0, Win32.SendMessageW(Grid.Handle, WindowsGridInterop.First + 56, (nuint)row, memory));
                bounds = Marshal.PtrToStructure<Win32.Rect>(memory);
            }
            finally { Marshal.FreeHGlobal(memory); }
            var x = column == 0 ? 5 : bounds.Left + 5;
            var y = bounds.Top + 5;
            Win32.SendMessageW(Grid.Handle, (int)Win32.WM_LBUTTONDOWN, shift ? 4u : 0u, (nint)((y << 16) | x));
        }

        private nint Send<T>(int message, nuint parameter, T value) where T : struct
        {
            var memory = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
            try
            {
                Marshal.StructureToPtr(value, memory, false);
                return Win32.SendMessageW(Grid.Handle, message, parameter, memory);
            }
            finally { Marshal.FreeHGlobal(memory); }
        }

        public void Dispose()
        {
            Grid.Dispose();
            Win32.RemoveWindowSubclass(Parent, _procedure, 88);
            Win32.DestroyWindow(Parent);
        }
    }
}
