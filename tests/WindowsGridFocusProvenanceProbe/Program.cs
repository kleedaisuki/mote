using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;

/// <summary>Hosted-only independent client graph; native identities never leave process memory.</summary>
internal static class Program
{
    /// <summary>One owned process and one sequential operation attempt; failures retain a censored report.</summary>
    [MTAThread]
    private static int Main(string[] args)
    {
        var report = new Report();
        Process? child = null;
        NativeIdentity? identity = null;
        string? fixture = null;
        string? reportPath = null;
        try
        {
            if (args.Length != 3) throw new InvalidOperationException();
            var exe = Path.GetFullPath(args[0]);
            var scratch = RequireArtifactPath(args[1]);
            var candidateReportPath = RequireArtifactPath(args[2]);
            Require(!File.Exists(candidateReportPath) && !Directory.Exists(candidateReportPath));
            reportPath = candidateReportPath;
            if (Directory.Exists(scratch) && Directory.EnumerateFileSystemEntries(scratch).Any())
                throw new InvalidOperationException();
            Directory.CreateDirectory(scratch);
            fixture = Path.Combine(scratch, "bounded.csv");
            File.WriteAllText(fixture, string.Concat(Enumerable.Range(0, 1100).Select(row =>
                string.Join(',', Enumerable.Range(0, 32).Select(col => $"R{row + 1}C{col + 1}")) + "\r\n")));
            report.BinarySha256 = Hash(exe);
            report.FixtureSha256 = Hash(fixture);
            var start = new ProcessStartInfo(exe) { UseShellExecute = false, ArgumentList = { fixture } };
            start.Environment["MOTE_HOME"] = Path.Combine(scratch, "mote-home");
            start.Environment["MOTE_TRACE"] = "1";
            start.Environment["MOTE_NATIVE_GRID_ACCESSIBILITY"] = "1";
            child = Process.Start(start) ?? throw new InvalidOperationException();
            Observe(report, "discover", () => identity, () => identity = Discover(child));
            report.IdentityCertified = true;
            RunSequence(report, identity!);
            report.Classification = ClassificationAfterSequence(report);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            report.Classification = "incomplete";
            report.Exception = ExceptionKind(error);
            report.HResult = error.HResult;
        }
        finally
        {
            if (child is not null)
            {
                try
                {
                    if (!child.HasExited)
                    {
                        report.CloseRequested = child.CloseMainWindow();
                        if (!child.WaitForExit(5000))
                        {
                            report.ForcedCleanup = true;
                            child.Kill();
                            child.WaitForExit(5000);
                        }
                    }
                    if (child.HasExited) report.EditorExitCode = child.ExitCode;
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    report.CleanupException = ExceptionKind(error);
                    report.CleanupHResult = error.HResult;
                    try
                    {
                        if (!child.HasExited)
                        {
                            report.ForcedCleanup = true;
                            child.Kill();
                            child.WaitForExit(5000);
                        }
                        if (child.HasExited) report.EditorExitCode = child.ExitCode;
                    }
                    catch (Exception cleanupError) when (cleanupError is not OutOfMemoryException)
                    { report.CleanupException = ExceptionKind(cleanupError); report.CleanupHResult = cleanupError.HResult; }
                }
                child.Dispose();
            }
            report.Boundary = report.ForcedCleanup || report.EditorExitCode is null ? "censored" :
                report.EditorExitCode == 0 ? "normal-exit-observed" : "nonzero-exit-observed";
            try
            {
                if (fixture is not null && File.Exists(fixture))
                {
                    report.FixtureAfterSha256 = Hash(fixture);
                    report.FixtureUnchanged = report.FixtureAfterSha256 == report.FixtureSha256;
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                report.CleanupException ??= ExceptionKind(error);
                report.CleanupHResult ??= error.HResult;
            }
            if (report.CleanupException is not null) report.Classification = "incomplete";
            if (reportPath is not null)
            {
                try
                {
                    RequireArtifactPath(reportPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
                    using var output = new FileStream(reportPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    JsonSerializer.Serialize(output, report, new JsonSerializerOptions { WriteIndented = true });
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    report.Classification = "incomplete";
                    report.CleanupException ??= ExceptionKind(error);
                    report.CleanupHResult ??= error.HResult;
                }
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(report));
        return report.Classification == "observed" && report.EditorExitCode == 0 && !report.ForcedCleanup && report.CleanupException is null && report.FixtureUnchanged ? 0 : 1;
    }

    /// <summary>Observation completeness is independent of an action returning or throwing as recorded.</summary>
    private static string ClassificationAfterSequence(Report report) =>
        report.Operations.Any(entry => entry.ObservationException is not null) ? "incomplete" : "observed";

    /// <summary>Mirrors the existing bounded read/selection workload, without its misidentified source assignment.</summary>
    private static void RunSequence(Report report, NativeIdentity identity)
    {
        AutomationElement? group = null;
        AutomationElement? table = null;
        GridPattern? grid = null;
        SelectionPattern? selection = null;
        AutomationElement? first = null;
        AutomationElement? second = null;
        Cell? firstCell = null;
        Cell[]? expected = null;
        Observe(report, "initial_uia", () => identity, () =>
        {
            group = AutomationElement.FromHandle(identity.Group);
            table = group.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "Mote.CsvGrid.Window")) ?? throw new InvalidOperationException();
            Require(table.Current.NativeWindowHandle == identity.Table);
            grid = (GridPattern)table.GetCurrentPattern(GridPattern.Pattern);
            selection = (SelectionPattern)table.GetCurrentPattern(SelectionPattern.Pattern);
            Require(grid.Current.RowCount == 64 && grid.Current.ColumnCount == 16);
            first = grid.GetItem(0, 0);
            Require(first.Current.Name.StartsWith("Row 1, Column 1;", StringComparison.Ordinal));
            var item = ((GridItemPattern)first.GetCurrentPattern(GridItemPattern.Pattern)).Current;
            Require(item.Row == 0 && item.Column == 0 && item.RowSpan == 1 && item.ColumnSpan == 1);
            firstCell = ReadCell(first);
            Require(firstCell.RowHeader == "Row 1" && firstCell.ColumnHeader == "Column 1" && firstCell.Value == "R1C1");
            Require(!first.TryGetCurrentPattern(InvokePattern.Pattern, out _));
            Require(((ValuePattern)first.GetCurrentPattern(ValuePattern.Pattern)).Current.IsReadOnly);
        });
        Observe(report, "query_1000", () => identity, () =>
        {
            for (var query = 0; query < 1000; query++)
            {
                var watch = Stopwatch.StartNew();
                _ = grid!.GetItem(0, 0).Current.Name;
                Require(watch.Elapsed.TotalMilliseconds < 1000);
            }
        });
        Observe(report, "select_first", () => identity, () =>
        {
            ((SelectionItemPattern)first!.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
            CheckSelection(selection!, [firstCell!]);
        });
        Observe(report, "add_second", () => identity, () =>
        {
            second = grid!.GetItem(0, 1);
            var cell = ReadCell(second);
            Require(cell.Row == 0 && cell.Column == 1 && cell.RowHeader == "Row 1" && cell.ColumnHeader == "Column 2" && cell.Value == "R1C2");
            expected = [firstCell!, cell];
            ((SelectionItemPattern)second.GetCurrentPattern(SelectionItemPattern.Pattern)).AddToSelection();
            CheckSelection(selection!, expected);
        });
        Observe(report, "refuse_sparse", () => identity, () =>
        {
            var refused = false;
            try { ((SelectionItemPattern)grid!.GetItem(1, 1).GetCurrentPattern(SelectionItemPattern.Pattern)).AddToSelection(); }
            catch (InvalidOperationException) { refused = true; }
            Require(refused);
            CheckSelection(selection!, expected!);
        });
        HashSet<string>? baselineIds = null;
        HashSet<string>? baselineRuntimeIds = null;
        foreach (var view in new[] { ("tree_raw", TreeWalker.RawViewWalker), ("tree_control", TreeWalker.ControlViewWalker), ("tree_content", TreeWalker.ContentViewWalker) })
        {
            Observe(report, view.Item1, () => identity, () =>
            {
                var ids = new HashSet<string>(StringComparer.Ordinal);
                var runtimeIds = new HashSet<string>(StringComparer.Ordinal);
                var count = 0;
                for (var element = view.Item2.GetFirstChild(table); element is not null; element = view.Item2.GetNextSibling(element))
                {
                    Require(++count <= 1104);
                    var id = element.Current.AutomationId;
                    Require(id.Length > 0 && ids.Add(id));
                    Require(runtimeIds.Add(string.Join(',', element.GetRuntimeId())));
                    var kind = element.Current.ControlType;
                    Require((kind == ControlType.HeaderItem || kind == ControlType.DataItem) && id.StartsWith("Mote.CsvGrid.", StringComparison.Ordinal));
                }
                Require(count == 1104);
                if (baselineIds is not null) Require(ids.SetEquals(baselineIds) && runtimeIds.SetEquals(baselineRuntimeIds!));
                baselineIds ??= ids;
                baselineRuntimeIds ??= runtimeIds;
            });
        }
        Observe(report, "navigation_reads", () => identity, () =>
        {
            foreach (var handle in new[] { identity.Rows, identity.Columns })
            {
                var element = AutomationElement.FromHandle(handle);
                Require(element.TryGetCurrentPattern(RangeValuePattern.Pattern, out var pattern));
                Require(((RangeValuePattern)pattern).Current.IsReadOnly);
            }
            _ = table!.Current.BoundingRectangle;
            _ = group!.Current.HelpText;
        });
        Observe(report, "f6_once", () => identity, () =>
        {
            var focus = identity.FocusHandle();
            var before = identity.Classify(focus);
            Require(before is "source" or "table" or "row_scroller" or "column_scroller" or "coordinate");
            Require(Native.PostMessageW(focus, 0x0100, 0x75, 0));
            var expectedPane = before switch { "source" => "table", "table" => "row_scroller", "row_scroller" => "column_scroller", "column_scroller" => "coordinate", _ => "source" };
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 1000 && identity.Classify(identity.FocusHandle()) != expectedPane) Thread.Sleep(10);
            report.F6SuccessorRelation = identity.Classify(identity.FocusHandle()) == expectedPane ? "matches" : "differs";
        });
        Observe(report, "goto_once", () => identity, () =>
        {
            var go = AutomationElement.FromHandle(identity.GoTo);
            ((InvokePattern)go.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            var prompt = Poll(() => FindPrompt(identity), 5000);
            Require(prompt != 0);
            var inputHandle = Native.GetDlgItem(prompt, 301);
            var acceptHandle = Native.GetDlgItem(prompt, 302);
            identity.RequireWindow(inputHandle, prompt, 301, "EDIT");
            identity.RequireWindow(acceptHandle, prompt, 302, "BUTTON");
            var input = AutomationElement.FromHandle(inputHandle);
            ((ValuePattern)input.GetCurrentPattern(ValuePattern.Pattern)).SetValue("1001:17");
            Require(((ValuePattern)input.GetCurrentPattern(ValuePattern.Pattern)).Current.Value == "1001:17");
            Native.SendMessageW(prompt, 0x0111, 302, acceptHandle);
        });
        AutomationElement? distant = null;
        Observe(report, "distant_read", () => identity, () =>
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 10000 && distant is null)
            {
                Thread.Sleep(50);
                try
                {
                    table = group!.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "Mote.CsvGrid.Window"));
                    if (table is null || !table.TryGetCurrentPattern(GridPattern.Pattern, out var pattern)) continue;
                    grid = (GridPattern)pattern;
                    var candidate = grid.GetItem(0, 0);
                    if (candidate.Current.Name.StartsWith("Row 1001, Column 17; presentation value: R1001C17", StringComparison.Ordinal)) distant = candidate;
                }
                catch (ElementNotAvailableException) { }
            }
            Require(distant is not null);
            var item = ((GridItemPattern)distant!.GetCurrentPattern(GridItemPattern.Pattern)).Current;
            Require(item.Row == 0 && item.Column == 0);
            Require(((RangeValuePattern)AutomationElement.FromHandle(identity.Rows).GetCurrentPattern(RangeValuePattern.Pattern)).Current.Value == 1000);
            Require(((RangeValuePattern)AutomationElement.FromHandle(identity.Columns).GetCurrentPattern(RangeValuePattern.Pattern)).Current.Value == 16);
        });
        // A refusal is an observed outcome, never a manufactured success or assumed callback thread.
        Observe(report, "cell_focus_once", () => identity, () => distant!.SetFocus(), containOperationException: true);
    }

    /// <summary>Pairs explicit client receipts; intervals are not joined to server spans by time or count.</summary>
    private static void Observe(Report report, string operation, Func<NativeIdentity?> identity, Action action, bool containOperationException = false)
    {
        var entry = new Operation { Receipt = report.Operations.Count + 1, Label = operation, BeginUtc = DateTimeOffset.UtcNow };
        report.Operations.Add(entry);
        try
        {
            ObserveBefore(entry, identity);
            // This witnesses entry to the client body, not OS or server delivery.
            entry.ActionAttempted = true;
            try { action(); entry.Outcome = "returned"; }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                entry.Outcome = "threw";
                entry.Exception = ExceptionKind(error);
                entry.HResult = error.HResult;
                if (!containOperationException) throw;
            }
        }
        finally
        {
            ObserveAfter(entry, identity);
            entry.EndUtc = DateTimeOffset.UtcNow;
            entry.TerminalParentReceipt = entry.Receipt;
        }
    }

    /// <summary>A failed pre-action observation is not an action throw; unsafe identity stops even an allowed Focus refusal.</summary>
    private static void ObserveBefore(Operation entry, Func<NativeIdentity?> identity)
    {
        try { entry.OwnerQueuePaneBefore = identity()?.Snapshot() ?? "unavailable"; }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            entry.Outcome = "not_attempted";
            entry.ObservationException = ExceptionKind(error);
            entry.ObservationHResult = error.HResult;
            throw;
        }
    }

    /// <summary>Post-action query failure marks only observation unknown and preserves the actual body return or throw.</summary>
    private static void ObserveAfter(Operation entry, Func<NativeIdentity?> identity)
    {
        try { entry.OwnerQueuePaneAfter = identity()?.Snapshot() ?? "unavailable"; }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            entry.OwnerQueuePaneAfter = "unavailable";
            entry.ObservationException ??= ExceptionKind(error);
            entry.ObservationHResult ??= error.HResult;
        }
    }

    /// <summary>Certifies native structure before any UIA read; absent canvas is not a legacy fallback.</summary>
    private static NativeIdentity Discover(Process child)
    {
        NativeIdentity? identity = null;
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 20000)
        {
            Thread.Sleep(100);
            child.Refresh();
            Require(!child.HasExited);
            var main = child.MainWindowHandle;
            if (main == 0) continue;
            identity = NativeIdentity.TryCreate(main, (uint)child.Id);
            if (identity is not null) break;
        }
        Require(identity is not null);
        return identity!;
    }

    /// <summary>Finds only the owned thread's exact modal class; no global title lookup or foreign metadata.</summary>
    private static nint FindPrompt(NativeIdentity identity)
    {
        nint found = 0;
        Exception? callbackError = null;
        Native.EnumThreadWindows(identity.Thread, (window, _) =>
        {
            try
            {
                if (Native.GetWindowThreadProcessId(window, out var pid) == identity.Thread && pid == identity.Pid &&
                    Native.GetParent(window) == identity.Main && Native.ClassIs(window, "MoteNativeTextPrompt") && Native.IsWindowVisible(window))
                    found = SelectUniquePrompt(found, window);
                return true;
            }
            catch (Exception error) when (error is not OutOfMemoryException) { callbackError = error; return false; }
        }, 0);
        if (callbackError is not null) throw callbackError;
        return found;
    }

    /// <summary>Ambiguous eligible prompts are refused, never resolved by native enumeration order.</summary>
    private static nint SelectUniquePrompt(nint previous, nint candidate)
    {
        Require(previous == 0 && candidate != 0);
        return candidate;
    }

    /// <summary>Bounded read polling is allowed; no input, retrying actions, or focus repair occurs here.</summary>
    private static nint Poll(Func<nint> read, int milliseconds)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds)
        {
            var result = read();
            if (result != 0) return result;
            Thread.Sleep(50);
        }
        return 0;
    }

    /// <summary>Restricts owned artifacts to this checkout's root .cache or .temp trees.</summary>
    private static string RequireArtifactPath(string value)
    {
        var path = Path.GetFullPath(value);
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git"))) directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException();
        Require(new[] { ".cache", ".temp" }.Any(name => path.StartsWith(Path.Combine(directory.FullName, name) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)));
        // Existing ancestors must not redirect an approved lexical path outside the checkout.
        var cursor = path;
        while (true)
        {
            try { Require((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) == 0); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            var parent = Path.GetDirectoryName(cursor);
            if (parent is null || parent == cursor) break;
            cursor = parent;
        }
        return path;
    }

    /// <summary>Hash values are allowed provenance, unlike paths or fixture text.</summary>
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    /// <summary>Closed exception categories never serialize messages, stack traces, or type names supplied by a provider.</summary>
    private static string ExceptionKind(Exception error) => error switch
    {
        ElementNotAvailableException => "element_unavailable", InvalidOperationException => "invalid_operation",
        COMException => "com", ArgumentException => "argument", IOException => "io", UnauthorizedAccessException => "access",
        _ => "other"
    };
    /// <summary>Unexpected fixture or ownership facts fail immediately.</summary>
    private static void Require(bool condition) { if (!condition) throw new InvalidOperationException(); }
    /// <summary>UIA identities and presentation values are transient comparison data, never serialized.</summary>
    private sealed record Cell(string Id, string Runtime, int Row, int Column, string RowHeader, string ColumnHeader, string Value);
    /// <summary>Reads exact synthetic selection identity locally.</summary>
    private static Cell ReadCell(AutomationElement element)
    {
        var item = ((GridItemPattern)element.GetCurrentPattern(GridItemPattern.Pattern)).Current;
        var header = ((TableItemPattern)element.GetCurrentPattern(TableItemPattern.Pattern)).Current;
        var rows = header.GetRowHeaderItems(); var columns = header.GetColumnHeaderItems();
        Require(rows.Length == 1 && columns.Length == 1);
        return new(element.Current.AutomationId, string.Join(',', element.GetRuntimeId()), item.Row, item.Column,
            rows[0].Current.Name, columns[0].Current.Name, ((ValuePattern)element.GetCurrentPattern(ValuePattern.Pattern)).Current.Value);
    }
    /// <summary>Preserves the original exact set and cardinality check without writing identities to report.</summary>
    private static void CheckSelection(SelectionPattern selection, Cell[] expected)
    {
        var actual = selection.Current.GetSelection().Select(ReadCell).ToArray();
        Require(actual.Length == expected.Length && actual.ToHashSet().Count == actual.Length && actual.ToHashSet().SetEquals(expected));
    }

    /// <summary>Closed report schema with independent client-only edges; no raw native identities or content.</summary>
    private sealed class Report
    {
        public int Schema { get; } = 1;
        public string Architecture { get; } = RuntimeInformation.ProcessArchitecture.ToString();
        public string CrossProcessEdge { get; } = "unjoined";
        public string FocusBoundary { get; } = "owner_gui_queue";
        public bool AbsenceCertified { get; } = false;
        public string Classification { get; set; } = "incomplete";
        public string Boundary { get; set; } = "open";
        public string? BinarySha256 { get; set; }
        public string? FixtureSha256 { get; set; }
        public string? FixtureAfterSha256 { get; set; }
        public bool FixtureUnchanged { get; set; }
        public bool IdentityCertified { get; set; }
        public string? F6SuccessorRelation { get; set; }
        public List<Operation> Operations { get; } = [];
        public string? Exception { get; set; }
        public int? HResult { get; set; }
        public bool CloseRequested { get; set; }
        public bool ForcedCleanup { get; set; }
        public int? EditorExitCode { get; set; }
        public string? CleanupException { get; set; }
        public int? CleanupHResult { get; set; }
    }
    /// <summary>One begin/terminal pair in the client graph; UTC is descriptive, not cross-process attribution.</summary>
    private sealed class Operation
    {
        public int Receipt { get; set; }
        public int? TerminalParentReceipt { get; set; }
        public required string Label { get; init; }
        public DateTimeOffset BeginUtc { get; init; }
        public DateTimeOffset? EndUtc { get; set; }
        public string OwnerQueuePaneBefore { get; set; } = "unavailable";
        public string OwnerQueuePaneAfter { get; set; } = "unavailable";
        /// <summary>Client body-entry witness only; false pre-query failure does not pretend the action threw.</summary>
        public bool ActionAttempted { get; set; }
        public string Outcome { get; set; } = "entered";
        public string? Exception { get; set; }
        public int? HResult { get; set; }
        public string? ObservationException { get; set; }
        public int? ObservationHResult { get; set; }
    }

    /// <summary>All handles, process and GUI thread identities stay private in memory and are rechecked for every sample.</summary>
    private sealed class NativeIdentity(nint main, uint pid, uint thread, nint canvas, nint source, nint group, nint table, nint rows, nint columns, nint goTo)
    {
        internal nint Main { get; } = main;
        internal uint Pid { get; } = pid;
        internal uint Thread { get; } = thread;
        internal nint Canvas { get; } = canvas;
        internal nint Source { get; } = source;
        internal nint Group { get; } = group;
        internal nint Table { get; } = table;
        internal nint Rows { get; } = rows;
        internal nint Columns { get; } = columns;
        internal nint GoTo { get; } = goTo;

        /// <summary>Discovers exactly one direct canvas child, its input island and the fixed native Grid structure.</summary>
        internal static NativeIdentity? TryCreate(nint main, uint pid)
        {
            var thread = Native.GetWindowThreadProcessId(main, out var actualPid);
            if (actualPid != pid || thread == 0 || !Native.ClassIs(main, "MoteNativeEditorWindow") || !Native.IsWindowVisible(main)) return null;
            var canvases = new List<nint>();
            Exception? callbackError = null;
            Native.EnumChildWindows(main, (window, _) =>
            {
                try
                {
                    if (Native.GetParent(window) == main && Native.GetWindowThreadProcessId(window, out var childPid) == thread && childPid == pid && Native.ClassIs(window, "MoteInteractiveCanvas")) canvases.Add(window);
                    return true;
                }
                catch (Exception error) when (error is not OutOfMemoryException) { callbackError = error; return false; }
            }, 0);
            if (callbackError is not null) throw callbackError;
            if (canvases.Count != 1) return null;
            var canvas = canvases[0];
            var group = Native.GetDlgItem(main, 1204);
            var identity = new NativeIdentity(main, pid, thread, canvas, Native.GetDlgItem(canvas, 301), group,
                Native.GetDlgItem(group, 104), Native.GetDlgItem(group, 1104), Native.GetDlgItem(group, 1105), Native.GetDlgItem(group, 1205));
            try { identity.Validate(); return identity; }
            catch (InvalidOperationException) { return null; }
        }
        /// <summary>Known parent, control, class, visibility, process and creating-thread facts must all agree.</summary>
        internal void RequireWindow(nint window, nint parent, int id, string cls)
        {
            Require(window != 0 && Native.IsWindow(window) && Native.GetParent(window) == parent && Native.GetDlgCtrlID(window) == id && Native.ClassIs(window, cls) && Native.IsWindowVisible(window));
            Require(Native.GetWindowThreadProcessId(window, out var pid) == Thread && pid == Pid);
        }
        /// <summary>Revalidates owned native structure without querying global/foreign UIA focus.</summary>
        private void Validate()
        {
            Require(Native.IsWindow(Main) && Native.IsWindowVisible(Main) && Native.ClassIs(Main, "MoteNativeEditorWindow") && Native.GetParent(Main) == 0);
            Require(Native.GetWindowThreadProcessId(Main, out var pid) == Thread && pid == Pid);
            RequireWindow(Canvas, Main, 0, "MoteInteractiveCanvas");
            RequireWindow(Source, Canvas, 301, "RICHEDIT50W");
            RequireWindow(Group, Main, 1204, "STATIC");
            RequireWindow(Table, Group, 104, "SysListView32");
            RequireWindow(Rows, Group, 1104, "SCROLLBAR");
            RequireWindow(Columns, Group, 1105, "SCROLLBAR");
            RequireWindow(GoTo, Group, 1205, "BUTTON");
        }
        /// <summary>Queries only the certified GUI queue; null focus is none, not unavailable or global focus.</summary>
        internal nint FocusHandle()
        {
            Validate();
            var info = new Native.GuiThreadInfo { Size = (uint)Marshal.SizeOf<Native.GuiThreadInfo>() };
            Require(Native.GetGUIThreadInfo(Thread, ref info));
            Validate();
            ValidateActive(info.Active);
            return info.Focus;
        }
        /// <summary>Requires active-window lifetime to remain within the certified main/modal owner tree.</summary>
        private void ValidateActive(nint active)
        {
            if (active == 0) return;
            Require(Native.IsWindow(active));
            Require(Native.GetWindowThreadProcessId(active, out var pid) == Thread && pid == Pid);
            if (active == Main) return;
            Require(Native.GetWindow(active, 4) == Main && Native.ClassIs(active, "MoteNativeTextPrompt"));
        }
        /// <summary>Maps raw HWND only to a fixed owned pane classification.</summary>
        internal string Classify(nint focus)
        {
            if (focus == 0) return "none";
            if (Native.GetWindowThreadProcessId(focus, out var pid) != Thread || pid != Pid) return "outside";
            if (focus == Source) return "source";
            if (focus == Table) return "table";
            if (focus == Rows) return "row_scroller";
            if (focus == Columns) return "column_scroller";
            if (focus == GoTo) return "coordinate";
            return "owned_other";
        }
        /// <summary>Samples only certified owner GUI-queue focus, not global foreground/keyboard focus.</summary>
        internal string Snapshot() => Classify(FocusHandle());
    }

    /// <summary>Typed read-only Win32 interop except the one owned F6 post and owned prompt submission.</summary>
    private static class Native
    {
        /// <summary>Platform GUI thread snapshot; opaque HWND fields are never serialized.</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct GuiThreadInfo { internal uint Size, Flags; internal nint Active, Focus, Capture, MenuOwner, MoveSize, Caret; internal int Left, Top, Right, Bottom; }
        /// <summary>Callback lifetime is synchronous for native enumeration.</summary>
        internal delegate bool EnumWindow(nint window, nint parameter);
        [DllImport("user32.dll")] internal static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint pid);
        [DllImport("user32.dll")] internal static extern nint GetParent(nint window);
        [DllImport("user32.dll")] internal static extern nint GetWindow(nint window, uint command);
        [DllImport("user32.dll")] internal static extern nint GetDlgItem(nint parent, int id);
        [DllImport("user32.dll")] internal static extern int GetDlgCtrlID(nint window);
        [DllImport("user32.dll")] internal static extern bool IsWindow(nint window);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint window);
        [DllImport("user32.dll")] internal static extern bool EnumChildWindows(nint parent, EnumWindow callback, nint parameter);
        [DllImport("user32.dll")] internal static extern bool EnumThreadWindows(uint thread, EnumWindow callback, nint parameter);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint window, StringBuilder value, int capacity);
        [DllImport("user32.dll")] internal static extern bool PostMessageW(nint window, uint message, nuint parameter, nint data);
        [DllImport("user32.dll")] internal static extern nint SendMessageW(nint window, uint message, nuint parameter, nint data);
        /// <summary>Native class name is kept local for fixed identity comparisons.</summary>
        internal static bool ClassIs(nint window, string expected) => string.Equals(Class(window), expected, StringComparison.OrdinalIgnoreCase);
        /// <summary>Native class names are compared case-insensitively against fixed class contracts.</summary>
        internal static string Class(nint window)
        {
            var value = new StringBuilder(256);
            Require(GetClassNameW(window, value, value.Capacity) > 0);
            return value.ToString();
        }
    }
}
