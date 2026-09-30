using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Automation;

/// <summary>Independent cross-process UIA falsifiers for the actual native CSV bounded table.</summary>
internal static class Program
{
    [MTAThread]
    private static int Main(string[] args)
    {
        var results = new Dictionary<string, object?> { ["OS"] = Environment.OSVersion.ToString(), ["Architecture"] = RuntimeInformation.ProcessArchitecture.ToString() };
        var overall = Stopwatch.StartNew();
        Process? process = null;
        var errors = new List<string>(); var inconclusive = new List<string>(); var semanticFocusBlocked = false;
        try
        {
            var exe = Path.GetFullPath(args[0]); var scratch = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(scratch);
            var fixture = Path.Combine(scratch, "bounded.csv");
            File.WriteAllText(fixture, string.Concat(Enumerable.Range(0, 1100).Select(row => string.Join(',', Enumerable.Range(0, 32).Select(col => $"R{row + 1}C{col + 1}")) + "\r\n")));
            results["BinarySha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(exe)));
            results["FixtureSha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fixture)));
            var start = new ProcessStartInfo(exe) { UseShellExecute = false, ArgumentList = { fixture } };
            start.Environment["MOTE_HOME"] = Path.Combine(scratch, "mote-home");
            start.Environment.Remove("MOTE_TRACE");
            start.Environment["MOTE_NATIVE_GRID_ACCESSIBILITY"] = "1";
            process = Process.Start(start)!;
            results["PID"] = process.Id;
            AutomationElement? group = null;
            for (var i = 0; i < 200 && group is null; i++)
            {
                Thread.Sleep(100); process.Refresh();
                if (process.HasExited) throw new Exception("Host exited " + process.ExitCode);
                if (process.MainWindowHandle == 0) continue;
                var main = AutomationElement.FromHandle(process.MainWindowHandle);
                group = main.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "Mote.CsvGrid.Navigation"));
            }
            if (group is null) throw new Exception("Navigation group unavailable");
            Thread.Sleep(2000);
            var table = group.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "Mote.CsvGrid.Window")) ?? throw new Exception("Table missing under navigation group");
            var grid = (GridPattern)table.GetCurrentPattern(GridPattern.Pattern);
            results["InitialCounts"] = new[] { grid.Current.RowCount, grid.Current.ColumnCount };
            var selection = (SelectionPattern)table.GetCurrentPattern(SelectionPattern.Pattern);
            var first = grid.GetItem(0, 0);
            results["InitialCellName"] = first.Current.Name;
            var queries = Stopwatch.StartNew(); var maximumMs = 0.0;
            for (var query = 0; query < 1000; query++)
            {
                var one = Stopwatch.StartNew(); _ = grid.GetItem(0, 0).Current.Name;
                maximumMs = Math.Max(maximumMs, one.Elapsed.TotalMilliseconds);
            }
            results["Bounded1000QueryMs"] = queries.Elapsed.TotalMilliseconds;
            results["MaximumQueryMs"] = maximumMs;
            Check(maximumMs < 1000, "bounded query avoids one-second hangs", errors);
            Check(first.Current.Name.StartsWith("Row 1, Column 1;"), "initial ordinal", errors);
            var item = (GridItemPattern)first.GetCurrentPattern(GridItemPattern.Pattern);
            Check(item.Current.Row == 0 && item.Current.Column == 0 && item.Current.RowSpan == 1 && item.Current.ColumnSpan == 1, "local item indices", errors);
            var headers = (TableItemPattern)first.GetCurrentPattern(TableItemPattern.Pattern);
            Check(headers.Current.GetRowHeaderItems()[0].Current.Name == "Row 1" && headers.Current.GetColumnHeaderItems()[0].Current.Name == "Column 1", "header relationships", errors);
            Check(!first.TryGetCurrentPattern(InvokePattern.Pattern, out _), "source Invoke omitted", errors);
            var value = (ValuePattern)first.GetCurrentPattern(ValuePattern.Pattern);
            Check(value.Current.IsReadOnly && value.Current.Value == "R1C1", "read-only presentation value", errors);
            var firstSelection = ReadSelectedCell(first);
            ((SelectionItemPattern)first.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
            AssertSelection(selection, [firstSelection], "AfterSelect", results, errors);
            var second = grid.GetItem(0, 1);
            var expectedSelection = new[] { firstSelection, ReadSelectedCell(second) };
            Check(expectedSelection.Select(cell => (cell.Row, cell.Column)).ToHashSet().SetEquals(new[] { (0, 0), (0, 1) }) &&
                expectedSelection.All(cell => cell.RowHeader == "Row 1") &&
                expectedSelection.Single(cell => cell.Column == 0).ColumnHeader == "Column 1" &&
                expectedSelection.Single(cell => cell.Column == 1).ColumnHeader == "Column 2" &&
                expectedSelection.Single(cell => cell.Column == 0).Value == "R1C1" &&
                expectedSelection.Single(cell => cell.Column == 1).Value == "R1C2",
                "selection reference local indices and absolute headers", errors);
            ((SelectionItemPattern)second.GetCurrentPattern(SelectionItemPattern.Pattern)).AddToSelection();
            AssertSelection(selection, expectedSelection, "AfterRectangularAdd", results, errors);
            var rejected = false;
            try { ((SelectionItemPattern)grid.GetItem(1, 1).GetCurrentPattern(SelectionItemPattern.Pattern)).AddToSelection(); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "impossible sparse union rejected", errors);
            AssertSelection(selection, expectedSelection, "AfterRejectedSparseUnion", results, errors);
            HashSet<string>? firstViewIds = null;
            HashSet<string>? firstViewRuntimeIds = null;
            foreach (var view in new[] { ("Raw", TreeWalker.RawViewWalker), ("Control", TreeWalker.ControlViewWalker), ("Content", TreeWalker.ContentViewWalker) })
            {
                var children = new List<string>(); var ids = new HashSet<string>(StringComparer.Ordinal);
                var runtimeIds = new HashSet<string>(StringComparer.Ordinal);
                var duplicateIds = new List<string>(); var duplicateRuntimeIds = new List<string>(); var count = 0;
                for (var child = view.Item2.GetFirstChild(table); child is not null; child = view.Item2.GetNextSibling(child))
                {
                    if (++count > 1104)
                    {
                        errors.Add(view.Item1 + " exceeds synthetic bounded-window child limit");
                        throw new Exception("Table exceeds synthetic bounded-window child limit");
                    }
                    var id = child.Current.AutomationId; var runtimeId = RuntimeIdentity(child);
                    children.Add(child.Current.ControlType.ProgrammaticName + ":" + id);
                    if (!ids.Add(id)) duplicateIds.Add(id);
                    if (!runtimeIds.Add(runtimeId)) duplicateRuntimeIds.Add(runtimeId);
                }
                results[view.Item1 + "UniqueAutomationIdCount"] = ids.Count;
                results[view.Item1 + "UniqueRuntimeIdCount"] = runtimeIds.Count;
                results[view.Item1 + "DuplicateAutomationIds"] = duplicateIds;
                results[view.Item1 + "DuplicateRuntimeIds"] = duplicateRuntimeIds;
                Check(count == 1104, view.Item1 + " exact 64x16 cells plus 64 row and 16 column headers", errors);
                Check(ids.Count == count && !ids.Contains(string.Empty), view.Item1 + " unique nonempty AutomationIds", errors);
                Check(runtimeIds.Count == count && !runtimeIds.Contains(string.Empty), view.Item1 + " unique nonempty runtime identities", errors);
                if (firstViewIds is not null)
                    Check(ids.SetEquals(firstViewIds) && runtimeIds.SetEquals(firstViewRuntimeIds!), view.Item1 + " same child identities as Raw regardless of order", errors);
                firstViewIds ??= ids; firstViewRuntimeIds ??= runtimeIds;
                results[view.Item1 + "ChildCount"] = count;
                results[view.Item1 + "UnexpectedChildren"] = children.Where(child => !child.StartsWith("ControlType.HeaderItem:Mote.CsvGrid.") && !child.StartsWith("ControlType.DataItem:Mote.CsvGrid.")).ToArray();
                Check(children.All(child => child.StartsWith("ControlType.HeaderItem:Mote.CsvGrid.") || child.StartsWith("ControlType.DataItem:Mote.CsvGrid.")), view.Item1 + " no default ListView proxy children", errors);
            }
            results["TableBounds"] = table.Current.BoundingRectangle.ToString();
            var windowThread = GetWindowThreadProcessId(process.MainWindowHandle, out _);
            var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
            if (!GetGUIThreadInfo(windowThread, ref info) || info.Focus == 0) throw new Exception("Owned source thread focus unavailable");
            var sourceFocus = info.Focus;
            var rowsControl = AutomationElement.FromHandle(GetDlgItem((nint)group.Current.NativeWindowHandle, 1104));
            var columnsControl = AutomationElement.FromHandle(GetDlgItem((nint)group.Current.NativeWindowHandle, 1105));
            results["NativeRowMetadata"] = new { rowsControl.Current.Name, rowsControl.Current.AutomationId, rowsControl.Current.ClassName, ControlType = rowsControl.Current.ControlType.ProgrammaticName };
            results["NativeColumnMetadata"] = new { columnsControl.Current.Name, columnsControl.Current.AutomationId, columnsControl.Current.ClassName, ControlType = columnsControl.Current.ControlType.ProgrammaticName };
            Check(rowsControl is not null && rowsControl.TryGetCurrentPattern(RangeValuePattern.Pattern, out _), "row admitted read-only RangeValue pattern", errors);
            Check(rowsControl!.Current.Name == "File rows", "native row navigation name", errors);
            Check(columnsControl is not null && columnsControl.TryGetCurrentPattern(RangeValuePattern.Pattern, out _), "column admitted read-only RangeValue pattern", errors);
            Check(columnsControl!.Current.Name == "File columns", "native column navigation name", errors);
            if (rowsControl is not null && rowsControl.TryGetCurrentPattern(RangeValuePattern.Pattern, out var rowRange))
            {
                var native = ((RangeValuePattern)rowRange).Current;
                Check(native.IsReadOnly, "row automation writes explicitly read-only", errors);
                results["InitialRowsRange"] = new { native.Minimum, native.Maximum, native.Value, native.IsReadOnly, native.SmallChange, native.LargeChange };
            }
            if (columnsControl is not null && columnsControl.TryGetCurrentPattern(RangeValuePattern.Pattern, out var columnRange))
            {
                var native = ((RangeValuePattern)columnRange).Current;
                Check(native.IsReadOnly, "column automation writes explicitly read-only", errors);
                results["InitialColumnsRange"] = new { native.Minimum, native.Maximum, native.Value, native.IsReadOnly, native.SmallChange, native.LargeChange };
            }
            var coordinateControl = group.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Go to CSV cell…"));
            var cycle = new[] { (nint)table.Current.NativeWindowHandle, (nint)rowsControl!.Current.NativeWindowHandle,
                (nint)columnsControl!.Current.NativeWindowHandle, (nint)coordinateControl!.Current.NativeWindowHandle, sourceFocus };
            var observed = new List<long>();
            var focusFacts = new List<object>();
            foreach (var target in cycle)
            {
                var previousFocus = info.Focus;
                if (!PostMessageW(info.Focus, 0x0100, 0x75, 0)) throw new Exception("Own-process F6 post failed");
                for (var attempt = 0; attempt < 100; attempt++)
                {
                    Thread.Sleep(10); info.Size = (uint)Marshal.SizeOf<GuiThreadInfo>();
                    GetGUIThreadInfo(windowThread, ref info); if (info.Focus == target) break;
                }
                var currentElement = AutomationElement.FromHandle(target);
                var previousElement = AutomationElement.FromHandle(previousFocus);
                var semantic = AutomationElement.FocusedElement;
                var semanticOwned = semantic.Current.ProcessId == process.Id;
                if (!semanticOwned) semanticFocusBlocked = true;
                // Ownership is checked before reading another application's semantic metadata.
                focusFacts.Add(new { PhysicalHwnd = (long)target, currentElement.Current.HasKeyboardFocus,
                    PreviousHasKeyboardFocus = previousElement.Current.HasKeyboardFocus, SemanticOwnedByTarget = semanticOwned,
                    SemanticHasKeyboardFocus = semanticOwned ? (bool?)semantic.Current.HasKeyboardFocus : null,
                    SemanticType = semanticOwned ? semantic.Current.ControlType.ProgrammaticName : "blocked: outside exact target PID",
                    SemanticName = semanticOwned ? semantic.Current.Name[..Math.Min(120, semantic.Current.Name.Length)] : null });
                if (target == (nint)rowsControl.Current.NativeWindowHandle || target == (nint)columnsControl.Current.NativeWindowHandle)
                    Check(currentElement.Current.HasKeyboardFocus && !previousElement.Current.HasKeyboardFocus, "coherent scroller focus publication", errors);
                observed.Add((long)info.Focus); Check(info.Focus == target, "synthetic owned F6 cycle target " + target, errors);
            }
            results["SyntheticF6Expected"] = cycle.Select(handle => (long)handle).ToArray();
            results["SyntheticF6Observed"] = observed;
            results["SyntheticF6FocusFacts"] = focusFacts;
            results["GlobalSemanticFocusClassification"] = semanticFocusBlocked ? "blocked: foreground focus not owned by exact target process; no foreign semantic metadata inspected" : "pass: actual global focus belongs to target process";
            var go = group.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Go to CSV cell…")) ?? throw new Exception("Go-to missing");
            ((InvokePattern)go.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            nint prompt = 0;
            for (var i = 0; i < 100 && prompt == 0; i++) { Thread.Sleep(50); prompt = FindWindowW("MoteNativeTextPrompt", "Go to CSV cell"); }
            if (prompt == 0) throw new Exception("Go-to dialog did not open");
            GetWindowThreadProcessId(prompt, out var promptPid);
            if (promptPid != process.Id) throw new Exception("Prompt does not belong to launched process");
            var input = AutomationElement.FromHandle(GetDlgItem(prompt, 301));
            ((ValuePattern)input.GetCurrentPattern(ValuePattern.Pattern)).SetValue("1001:17");
            results["GoToInputReadback"] = ((ValuePattern)input.GetCurrentPattern(ValuePattern.Pattern)).Current.Value;
            SendMessageW(prompt, 0x0111, 302, GetDlgItem(prompt, 302));
            AutomationElement? distant = null;
            for (var i = 0; i < 200 && distant is null; i++)
            {
                Thread.Sleep(50);
                try { table = group.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "Mote.CsvGrid.Window")); if (table is null || !table.TryGetCurrentPattern(GridPattern.Pattern, out var currentGrid)) continue; grid = (GridPattern)currentGrid; var candidate = grid.GetItem(0, 0); if (candidate.Current.Name.StartsWith("Row 1001, Column 17; presentation value: R1001C17")) distant = candidate; }
                catch (ElementNotAvailableException) { }
            }
            results["AfterGoToFirstCell"] = grid.GetItem(0, 0).Current.Name;
            results["AfterGoToStatus"] = group.Current.HelpText;
            var afterRows = (RangeValuePattern)rowsControl!.GetCurrentPattern(RangeValuePattern.Pattern);
            var afterColumns = (RangeValuePattern)columnsControl!.GetCurrentPattern(RangeValuePattern.Pattern);
            results["AfterRowsRange"] = new { afterRows.Current.Minimum, afterRows.Current.Maximum, afterRows.Current.Value, afterRows.Current.IsReadOnly };
            results["AfterColumnsRange"] = new { afterColumns.Current.Minimum, afterColumns.Current.Maximum, afterColumns.Current.Value, afterColumns.Current.IsReadOnly };
            Check(afterRows.Current.Value == 1000 && afterColumns.Current.Value == 16, "native RangeValue reflects distant logical origins", errors);
            Check(distant is not null, "distant window absolute coordinates/value", errors);
            if (distant is not null)
            {
                var distantItem = (GridItemPattern)distant.GetCurrentPattern(GridItemPattern.Pattern);
                Check(distantItem.Current.Row == 0 && distantItem.Current.Column == 0, "distant local zero indices", errors);
                results["DistantName"] = distant.Current.Name;
                var focusedRefused = false;
                try { distant.SetFocus(); } catch (InvalidOperationException) { focusedRefused = true; }
                Check(focusedRefused, "off-owner cell Focus fails safely pending acceptance gate", errors);
                results["ExternalCellFocus"] = "blocked: generated COM callback is off owner thread; no native focus attempt";
            }
            var stale = false;
            try { _ = first.Current.Name; } catch (ElementNotAvailableException) { stale = true; }
            Check(stale, "retained cell unavailable after rebase", errors);
        }
        catch (Exception exception) { inconclusive.Add(exception.ToString()); }
        finally
        {
            if (process is { HasExited: false }) { process.CloseMainWindow(); if (!process.WaitForExit(5000)) process.Kill(); }
            results["ElapsedMs"] = overall.Elapsed.TotalMilliseconds;
            results["Scope"] = "opt-in bounded reads, rectangular selection, readonly range facts, synthetic owned F6, native coordinate navigation; not external focus/write/reader acceptance";
            results["RemainingGates"] = new[] { "off-owner cell Focus", "RangeValue writes", "real reader speech", "physical IME", "cross-RID parity beyond this one run", "100 MiB memory teardown", "multi-monitor scaling" };
            results["Errors"] = errors; results["Inconclusive"] = inconclusive;
            results["Classification"] = errors.Count > 0 ? "product-fail" : inconclusive.Count > 0 ? "inconclusive" : "pass";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
            File.WriteAllText(args[2], JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine(JsonSerializer.Serialize(results));
        return errors.Count == 0 && inconclusive.Count == 0 ? 0 : 1;
    }
    /// <summary>Snapshots synthetic cell identity, local coordinates and absolute ordinal headers.</summary>
    private sealed record SelectedCell(string AutomationId, string RuntimeId, int Row, int Column,
        string RowHeader, string ColumnHeader, string Value);

    /// <summary>Uses UIA runtime identity rather than wrapper reference equality across COM reads.</summary>
    private static string RuntimeIdentity(AutomationElement element) => string.Join(",", element.GetRuntimeId());

    /// <summary>Reads independent GridItem/TableItem/Value facts for a returned selected element.</summary>
    private static SelectedCell ReadSelectedCell(AutomationElement element)
    {
        var item = ((GridItemPattern)element.GetCurrentPattern(GridItemPattern.Pattern)).Current;
        var headers = ((TableItemPattern)element.GetCurrentPattern(TableItemPattern.Pattern)).Current;
        var rows = headers.GetRowHeaderItems(); var columns = headers.GetColumnHeaderItems();
        if (rows.Length != 1 || columns.Length != 1) throw new InvalidOperationException("Selected cell requires one row and column header");
        return new(element.Current.AutomationId, RuntimeIdentity(element), item.Row, item.Column,
            rows[0].Current.Name, columns[0].Current.Name, ((ValuePattern)element.GetCurrentPattern(ValuePattern.Pattern)).Current.Value);
    }

    /// <summary>Requires exact set membership and cardinality; duplicate or reordered COM wrappers cannot hide a wrong cell.</summary>
    private static void AssertSelection(SelectionPattern selection, SelectedCell[] expected, string stage,
        Dictionary<string, object?> results, List<string> errors)
    {
        var actual = selection.Current.GetSelection().Select(ReadSelectedCell).ToArray();
        results[stage + "SelectedCells"] = actual;
        Check(actual.Length == expected.Length && actual.ToHashSet().Count == actual.Length &&
            actual.ToHashSet().SetEquals(expected), stage + " exact selected identities, local coordinates and absolute headers", errors);
    }

    /// <summary>Collects all independent falsifiers before final classification.</summary>
    private static void Check(bool ok, string label, List<string> errors) { if (!ok) errors.Add(label); }
    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo { internal uint Size, Flags; internal nint Active, Focus, Capture, MenuOwner, MoveSize, Caret; internal int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);
    [DllImport("user32.dll")] private static extern bool PostMessageW(nint window, uint message, nuint parameter, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowW(string cls, string title);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll")] private static extern nint GetDlgItem(nint parent, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetWindowTextW(nint hwnd, string text);
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint hwnd, uint message, nuint wParam, nint lParam);
}
