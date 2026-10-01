using System.Runtime.InteropServices;
using Mote.Native.Windows.Accessibility;
using Mote.Telemetry;

namespace Mote.Native.Windows;

/// <summary>Validated process-local capabilities; no native identity is passed to telemetry.</summary>
internal readonly record struct WindowsGridFocusRoles(nint Source, nint Table, nint Rows, nint Columns, nint Coordinate);

/// <summary>Pure owner GUI-queue reduction over validated capabilities, independent of global keyboard focus.</summary>
internal static class WindowsGridFocusClassifier
{
    /// <summary>Zero means no queue focus; absent role capabilities never label an arbitrary HWND as source.</summary>
    internal static TelemetryFocusPane Classify(nint focus, WindowsGridFocusRoles roles, bool owned)
    {
        if (focus == 0) return TelemetryFocusPane.None;
        if (!owned) return TelemetryFocusPane.Outside;
        if (focus == roles.Source) return TelemetryFocusPane.Source;
        if (focus == roles.Table) return TelemetryFocusPane.Table;
        if (focus == roles.Rows) return TelemetryFocusPane.RowScroller;
        if (focus == roles.Columns) return TelemetryFocusPane.ColumnScroller;
        if (focus == roles.Coordinate) return TelemetryFocusPane.Coordinate;
        return TelemetryFocusPane.OwnedOther;
    }
}

/// <summary>Enabled-only native focus readback for the actual adapter attempt; never transfers focus.</summary>
internal sealed partial class WindowsCsvGrid
{
    /// <summary>Checks live native identities and keeps OS thread affinity separate from managed admission.</summary>
    TelemetryFocusSample IWindowsGridFocusEvidence.CaptureFocusEvidence()
    {
        var managed = Environment.CurrentManagedThreadId == _uiThread
            ? TelemetryFocusThreadRelation.Owner : TelemetryFocusThreadRelation.NonOwner;
        var table = Handle;
        var source = _sourceHandle;
        var group = _groupHandle;
        var installation = _installation;
        var thread = FocusWindowThread(table, out var process);
        // Relative HWND-owner agreement is not editor ownership after handle reuse.
        // Reject a foreign process before reading any of its native class metadata.
        if (thread == 0 || process != (uint)Environment.ProcessId || !FocusMainMatches(_parent, thread, process) ||
            !FocusRoleMatches(group, _parent, _controlId + 1100, "STATIC", thread, process) ||
            !FocusRoleMatches(table, group, _controlId, "SysListView32", thread, process))
            return new(TelemetryFocusThreadRelation.Unknown, managed, TelemetryFocusPane.Unavailable);
        var native = FocusCurrentThread() == thread ? TelemetryFocusThreadRelation.Owner : TelemetryFocusThreadRelation.NonOwner;
        var info = new UiaGuiThreadInfo { Size = (uint)Marshal.SizeOf<UiaGuiThreadInfo>() };
        // The queue may retain focus while another application is foreground. Never
        // turn this owner-thread sample into global keyboard or foreground evidence.
        if (!FocusThreadInfo(thread, ref info) || info.Active != _parent)
            return new(native, managed, TelemetryFocusPane.Unavailable);
        var roles = new WindowsGridFocusRoles(
            FocusSourceMatches(source, thread, process) ? source : 0, table,
            FocusRoleMatches(RowScroller, group, _controlId + 1000, "ScrollBar", thread, process) ? RowScroller : 0,
            FocusRoleMatches(ColumnScroller, group, _controlId + 1001, "ScrollBar", thread, process) ? ColumnScroller : 0,
            FocusRoleMatches(_goToHandle, group, _controlId + 1101, "Button", thread, process) ? _goToHandle : 0);
        uint focusProcess = 0;
        if (info.Focus != 0 && FocusWindowThread(info.Focus, out focusProcess) == 0)
            return new(native, managed, TelemetryFocusPane.Unavailable);
        if (installation != _installation || table != Handle || source != _sourceHandle || group != _groupHandle ||
            !FocusMainMatches(_parent, thread, process) ||
            !FocusRoleMatches(table, group, _controlId, "SysListView32", thread, process))
            return new(native, managed, TelemetryFocusPane.Unavailable);
        return new(native, managed, WindowsGridFocusClassifier.Classify(info.Focus, roles, focusProcess == process));
    }

    /// <summary>Canvas input absence never falls back to the hidden legacy source control.</summary>
    private bool FocusSourceMatches(nint source, uint thread, uint process)
    {
        if (source == 0 || !WindowsGridInterop.IsWindowVisible(source)) return false;
        if (_sourceControlId == 101)
            return FocusRoleMatches(source, _parent, 101, "RICHEDIT50W", thread, process);
        if (_sourceControlId != 301) return false;
        var canvas = FocusParent(source);
        return FocusRoleMatches(canvas, _parent, 0, "MoteInteractiveCanvas", thread, process) &&
            FocusRoleMatches(source, canvas, 301, "RICHEDIT50W", thread, process);
    }

    /// <summary>Top-level windows have no valid control ID; validate their lifetime and owner without GetDlgCtrlID.</summary>
    private static bool FocusMainMatches(nint window, uint thread, uint process) =>
        window != 0 && WindowsGridInterop.IsWindowVisible(window) && FocusOwnerMatches(window, thread, process) &&
        FocusParent(window) == 0 && FocusClassMatches(window, "MoteNativeEditorWindow");

    /// <summary>Validates one live role against its actual parent, control identity, class and owner.</summary>
    private static bool FocusRoleMatches(nint window, nint parent, int id, string name, uint thread, uint process) =>
        window != 0 && WindowsGridInterop.IsWindowVisible(window) && FocusOwnerMatches(window, thread, process) && FocusParent(window) == parent &&
        FocusControlId(window) == id && FocusClassMatches(window, name);

    /// <summary>Queries a known capability without inspecting foreign semantic data.</summary>
    private static bool FocusOwnerMatches(nint window, uint thread, uint process) =>
        window != 0 && FocusWindowThread(window, out var actualProcess) == thread && actualProcess == process;

    /// <summary>Class names stay in stack storage and are compared only to shipped native classes.</summary>
    private static unsafe bool FocusClassMatches(nint window, string expected)
    {
        char* name = stackalloc char[64];
        var length = FocusClassName(window, name, 64);
        return length > 0 && length < 63 && new ReadOnlySpan<char>(name, length).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Native callback identity, never serialized.</summary>
    [DllImport("kernel32.dll", EntryPoint = "GetCurrentThreadId")]
    private static extern uint FocusCurrentThread();
    /// <summary>Live window owner identity, never serialized.</summary>
    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
    private static extern uint FocusWindowThread(nint window, out uint process);
    /// <summary>Reads the explicit owner GUI thread instead of adopting global foreground focus.</summary>
    [DllImport("user32.dll", EntryPoint = "GetGUIThreadInfo")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FocusThreadInfo(uint thread, ref UiaGuiThreadInfo info);
    /// <summary>Checks the live direct parent of a supplied role capability.</summary>
    [DllImport("user32.dll", EntryPoint = "GetParent")]
    private static extern nint FocusParent(nint window);
    /// <summary>Checks the actual role's native control identity.</summary>
    [DllImport("user32.dll", EntryPoint = "GetDlgCtrlID")]
    private static extern int FocusControlId(nint window);
    /// <summary>Reads only the native registered class, never window text.</summary>
    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern unsafe int FocusClassName(nint window, char* name, int capacity);
}
