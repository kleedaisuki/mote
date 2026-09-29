using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using System.Windows.Automation.Text;

/// <summary>External Windows UI Automation client for the published Native AOT canvas.</summary>
internal static class Program
{
    private const int ExpectedBudgetHresult = unchecked((int)0x80131509);
    private const int StaleHresult = unchecked((int)0x80040201);
    private const uint WmGetTextLength = 0x000E;
    private const uint WmVScroll = 0x0115;
    private const uint WmCommand = 0x0111;
    private const uint WmClose = 0x0010;
    private const string TailMarker = "TAIL_AX_MARKER_世界😀";

    [STAThread]
    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("This probe requires Windows UI Automation.");
            return 2;
        }

        if (args.Length is < 3 or > 4 ||
            (args.Length == 4 && args[3] != "--uia-fragment-experimental"))
        {
            Console.Error.WriteLine("Usage: WindowsAxExternalProbe <published-mote.exe> <scratch-directory> <report.json> [--uia-fragment-experimental]");
            return 2;
        }

        var report = new ProbeReport();
        var fragmentExperiment = args.Length == 4;
        report.Mode = fragmentExperiment ? "fragment-experimental" : "canvas-baseline";
        var reportPath = Path.GetFullPath(args[2]);
        try
        {
            var exe = Path.GetFullPath(args[0]);
            var scratch = Path.GetFullPath(args[1]);
            if (!File.Exists(exe)) throw new FileNotFoundException("Published executable is missing", exe);
            Directory.CreateDirectory(scratch);
            report.ExecutableSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(exe)));
            Run(exe, scratch, report, fragmentExperiment);
        }
        catch (Exception exception)
        {
            report.Fail("probe-unhandled", "probe-or-host", exception);
        }
        finally
        {
            report.CompletedUtc = DateTimeOffset.UtcNow;
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                WriteIndented = true
            }), new UTF8Encoding(false));
            Console.WriteLine($"Windows external UIA report: {reportPath}; failures={report.Failures.Count}; release-blockers={report.ReleaseBlockers.Count}");
        }

        return report.Failures.Count == 0 && report.ReleaseBlockers.Count == 0 ? 0 : 1;
    }

    /// <summary>Runs all checks against the real published process, retaining the original UIA proxy across the oversized call.</summary>
    private static void Run(string exe, string scratch, ProbeReport report, bool fragmentExperiment)
    {
        var fixture = Path.Combine(scratch, "ax-many-lines.md");
        var source = string.Concat(Enumerable.Range(0, 9000).Select(index => $"row-{index:D6} hello\n"))
            + TailMarker + "\n";
        File.WriteAllText(fixture, source, new UTF8Encoding(false, true));
        report.SourceUtf16Length = source.Length;
        report.SourceSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fixture)));

        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("--canvas-experimental");
        if (fragmentExperiment) start.ArgumentList.Add("--uia-fragment-experimental");
        start.ArgumentList.Add(fixture);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Native editor did not start");
        report.ProcessId = process.Id;
        try
        {
            var main = WaitFor(() => FindOwnWindow("MoteNativeEditorWindow", process.Id), process);
            var canvas = WaitFor(() => FindWindowExW(main, 0, "MoteInteractiveCanvas", null), process);
            var input = WaitFor(() => FindWindowExW(canvas, 0, "RICHEDIT50W", null), process);
            WaitFor(() => Title(main).Contains("ax-many-lines", StringComparison.Ordinal) ? main : 0, process);

            var element = AutomationElement.FromHandle(canvas);
            var sourceElement = WaitForSourceElement(canvas, process, fragmentExperiment);
            var text = (TextPattern)sourceElement.GetCurrentPattern(TextPattern.Pattern);
            var originalRange = text.DocumentRange;
            var prefix = originalRange.GetText(128);
            report.Check("document-prefix-exact", prefix.Length == 128 && source.StartsWith(prefix, StringComparison.Ordinal),
                $"length={prefix.Length}");

            var hostLength = checked((int)SendMessageW(input, WmGetTextLength, 0, 0));
            report.HostUtf16Length = hostLength;
            report.Check("bounded-native-input-host", hostLength > 0 && hostLength <= 16 * 1024 && source.Length > 65_536,
                $"host={hostLength}; source={source.Length}");

            var hostElement = AutomationElement.FromHandle(input);
            if (hostElement.TryGetCurrentPattern(TextPattern.Pattern, out var hostPattern))
            {
                if (fragmentExperiment)
                {
                    var hostPrefix = ((TextPattern)hostPattern).DocumentRange.GetText(128);
                    report.HostPatternPrefixUtf16Length = hostPrefix.Length;
                    report.Check("input-hwnd-routes-to-source-document",
                        hostElement.Current.AutomationId == "mote.source.document" &&
                        hostPrefix.Length == 128 && source.StartsWith(hostPrefix, StringComparison.Ordinal),
                        $"id={hostElement.Current.AutomationId}; prefix={hostPrefix.Length}");
                }
                else
                {
                    report.HostPatternUtf16Length = ((TextPattern)hostPattern).DocumentRange.GetText(-1).Length;
                    report.Check("baseline-input-has-only-bounded-text",
                        report.HostPatternUtf16Length <= 16 * 1024 &&
                        report.HostPatternUtf16Length < source.Length,
                        $"host-uia={report.HostPatternUtf16Length}");
                }
            }
            else report.Check("input-hwnd-has-text-pattern", false, "RichEdit/input HWND had no UIA TextPattern");
            var rawChild = TreeWalker.RawViewWalker.GetFirstChild(element);
            var controlChild = TreeWalker.ControlViewWalker.GetFirstChild(element);
            var contentChild = TreeWalker.ContentViewWalker.GetFirstChild(element);
            var rawDocuments = DocumentsInView(element, TreeWalker.RawViewWalker);
            var controlDocuments = DocumentsInView(element, TreeWalker.ControlViewWalker);
            var contentDocuments = DocumentsInView(element, TreeWalker.ContentViewWalker);
            report.Tree = new TreeObservation(
                Describe(element), Describe(sourceElement), Describe(hostElement), Describe(rawChild), Describe(controlChild),
                Describe(contentChild), Describe(AutomationElement.FocusedElement),
                element.Current.HasKeyboardFocus, sourceElement.Current.HasKeyboardFocus,
                hostElement.Current.HasKeyboardFocus, rawDocuments, controlDocuments, contentDocuments);
            if (rawDocuments.Count != 1 || controlDocuments.Count != 1 || contentDocuments.Count != 1)
            {
                report.ReleaseBlockers.Add($"Expected one source-backed Document in each canvas subtree: Raw={rawDocuments.Count}, Control={controlDocuments.Count}, Content={contentDocuments.Count}; focused={report.Tree.Focused}. Single-editor accessibility is not established.");
            }

            var selectionBefore = text.GetSelection();
            report.Check("global-selection-before-budget-failure", selectionBefore.Length == 1,
                $"ranges={selectionBefore.Length}");
            var visibleBefore = text.GetVisibleRanges();
            report.Check("visible-range-before-budget-failure", visibleBefore.Length > 0,
                $"ranges={visibleBefore.Length}");

            // This must not report a successful truncated document. Capture the exact managed
            // exception and HRESULT because UIAutomationClient remaps some provider HRESULTs.
            try
            {
                var fullText = originalRange.GetText(-1);
                report.Oversize = new OversizeObservation("success", 0, fullText.Length, null);
                report.Failures.Add("oversized GetText(-1) returned success; exact full-text semantics and 64 Ki request budget were violated");
            }
            catch (Exception exception) when (exception is COMException or InvalidOperationException or ElementNotAvailableException)
            {
                report.Oversize = new OversizeObservation(exception.GetType().Name, exception.HResult, null, exception.Message);
                report.Check("oversize-exact-hresult", exception.HResult == ExpectedBudgetHresult,
                    $"type={exception.GetType().Name}; hresult=0x{exception.HResult:X8}; expected=0x{ExpectedBudgetHresult:X8}");
            }

            // Reuse exactly the same cached TextPattern, not a newly acquired pattern.
            var cachedHealthy = TryReadPattern(text, report, "cached-after-oversize");
            report.Check("cached-pattern-usable-after-budget-failure", cachedHealthy,
                cachedHealthy ? "selection and visible ranges returned" : "one or both cached calls failed");
            var freshText = (TextPattern)WaitForSourceElement(canvas, process, fragmentExperiment)
                .GetCurrentPattern(TextPattern.Pattern);
            report.Check("fresh-pattern-usable-after-budget-failure",
                TryReadPattern(freshText, report, "fresh-after-oversize"), "fresh proxy selection and visible ranges");

            // Continue with a fresh proxy if the cached one was poisoned: preserve both failures
            // independently instead of letting the first exception hide offscreen/lifetime checks.
            var scrollingPattern = cachedHealthy ? text : freshText;
            SendMessageW(canvas, WmVScroll, 7, 0); // SB_BOTTOM through the canvas scrollbar.
            Thread.Sleep(400);
            var visibleAfterScroll = scrollingPattern.GetVisibleRanges();
            var visibleText = string.Concat(visibleAfterScroll.Select(range => range.GetText(-1)));
            report.Check("offscreen-tail-after-scroll", visibleText.Contains(TailMarker, StringComparison.Ordinal),
                $"ranges={visibleAfterScroll.Length}; utf16-length={visibleText.Length}");

            SendMessageW(main, WmCommand, 201, 0); // New document, same HWND, new generation.
            WaitFor(() => Title(main).StartsWith("Untitled", StringComparison.Ordinal) ? main : 0, process);
            report.Check("same-hwnd-new-retains-window", FindOwnWindow("MoteNativeEditorWindow", process.Id) == main,
                "main HWND after New");
            report.Check("old-range-rejected-after-new", IsStale(originalRange, report, "after-new"),
                "old source range must not resolve against new document");
            var newRange = ((TextPattern)WaitForSourceElement(canvas, process, fragmentExperiment)
                .GetCurrentPattern(TextPattern.Pattern)).DocumentRange;
            report.Check("new-document-empty", newRange.GetText(-1).Length == 0, "fresh same-HWND range");

            SendMessageW(main, WmClose, 0, 0);
            report.Check("native-process-closed", process.WaitForExit(10_000), "WM_CLOSE exit within 10 seconds");
            report.Check("new-range-rejected-after-close", IsStale(newRange, report, "after-close"),
                "retained range must not expose closed source");
            if (process.HasExited)
                report.Check("native-exit-code", process.ExitCode == 0, $"exit={process.ExitCode}");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            if (process.HasExited && process.ExitCode != 0)
                report.NativeStderr = process.StandardError.ReadToEnd();
        }
    }

    /// <summary>Tests selection and visible ranges separately so one poisoned call cannot hide the other.</summary>
    private static bool TryReadPattern(TextPattern pattern, ProbeReport report, string stage)
    {
        var success = true;
        try
        {
            var count = pattern.GetSelection().Length;
            report.Check($"{stage}-selection", count == 1, $"ranges={count}");
            success &= count == 1;
        }
        catch (Exception exception)
        {
            report.Fail($"{stage}-selection", "UIA-client-or-provider", exception);
            success = false;
        }

        try
        {
            var count = pattern.GetVisibleRanges().Length;
            report.Check($"{stage}-visible", count > 0, $"ranges={count}");
            success &= count > 0;
        }
        catch (Exception exception)
        {
            report.Fail($"{stage}-visible", "UIA-client-or-provider", exception);
            success = false;
        }

        return success;
    }

    /// <summary>Recognizes an unavailable old range without silently accepting an unrelated exception.</summary>
    private static bool IsStale(TextPatternRange range, ProbeReport report, string stage)
    {
        try { _ = range.GetText(10); return false; }
        catch (Exception exception) when (exception is COMException or ElementNotAvailableException or InvalidOperationException)
        {
            report.Observations.Add(new Observation(stage, exception.GetType().Name, exception.HResult));
            return exception.HResult == StaleHresult;
        }
    }

    /// <summary>
    /// Finds the source Document by stable AutomationId in fragment mode, never by
    /// generic Document type: RichEdit is also a Document but holds only an island.
    /// </summary>
    private static AutomationElement WaitForSourceElement(nint canvas, Process process,
        bool fragmentExperiment)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 10_000)
        {
            try
            {
                var element = AutomationElement.FromHandle(canvas);
                var source = fragmentExperiment
                    ? element.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.AutomationIdProperty,
                            "mote.source.document"))
                    : element;
                if (source is not null &&
                    source.Current.ControlType == ControlType.Document &&
                    source.TryGetCurrentPattern(TextPattern.Pattern, out _))
                    return source;
            }
            catch (ElementNotAvailableException) { }
            if (process.HasExited) break;
            Thread.Sleep(50);
        }
        throw new InvalidOperationException(fragmentExperiment
            ? "Published fragment did not expose source Document AutomationId mote.source.document and TextPattern"
            : "Published canvas did not expose an external UIA TextPattern");
    }

    /// <summary>Enumerates every Document in one canvas subtree, including its root.</summary>
    private static List<string> DocumentsInView(AutomationElement root, TreeWalker walker)
    {
        var documents = new List<string>();
        var pending = new Queue<(AutomationElement Element, int Depth)>();
        pending.Enqueue((root, 0));
        var visited = 0;
        while (pending.Count > 0)
        {
            var (element, depth) = pending.Dequeue();
            if (++visited > 64) throw new InvalidOperationException("UIA canvas subtree exceeded 64 nodes");
            if (element.Current.ControlType == ControlType.Document) documents.Add(Describe(element));
            if (depth >= 4) continue;
            for (var child = walker.GetFirstChild(element); child is not null;
                child = walker.GetNextSibling(child))
                pending.Enqueue((child, depth + 1));
        }
        return documents;
    }

    /// <summary>Polls a real HWND without accidentally accepting another editor process.</summary>
    private static nint WaitFor(Func<nint> find, Process process)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 10_000)
        {
            var handle = find();
            if (handle != 0) return handle;
            if (process.HasExited) throw new InvalidOperationException($"Native editor exited early: {process.ExitCode}");
            Thread.Sleep(30);
        }
        throw new TimeoutException("Native editor did not expose its expected HWND");
    }

    /// <summary>Returns the named top-level window only when owned by the launched process.</summary>
    private static nint FindOwnWindow(string className, int pid)
    {
        var handle = FindWindowW(className, null);
        if (handle == 0) return 0;
        GetWindowThreadProcessId(handle, out var actual);
        return actual == pid ? handle : 0;
    }

    /// <summary>Reads the current top-level title used to await asynchronous open/New transitions.</summary>
    private static string Title(nint window)
    {
        var characters = new char[512];
        var length = GetWindowTextW(window, characters, characters.Length);
        return new string(characters, 0, length);
    }

    /// <summary>Records a UIA element's tree identity and semantic filtering properties.</summary>
    private static string Describe(AutomationElement? element) => element is null
        ? "none"
        : $"runtime={string.Join('.', element.GetRuntimeId())};hwnd={element.Current.NativeWindowHandle};" +
          $"type={element.Current.ControlType.ProgrammaticName};name={element.Current.Name};" +
          $"id={element.Current.AutomationId};control={element.Current.IsControlElement};" +
          $"content={element.Current.IsContentElement};focus={element.Current.HasKeyboardFocus};" +
          $"text={element.TryGetCurrentPattern(TextPattern.Pattern, out _)}";

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowW(string className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowExW(nint parent, nint after, string className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(nint window, char[] text, int capacity);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out int pid);
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint window, uint message, nuint wParam, nint lParam);
}

/// <summary>Machine-readable diagnostic result; failures and release blockers are intentionally non-gating in CI.</summary>
internal sealed class ProbeReport
{
    public string Schema { get; } = "mote-windows-ax-external-v1";
    public string Mode { get; set; } = "canvas-baseline";
    public DateTimeOffset StartedUtc { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CompletedUtc { get; set; }
    public string OsVersion { get; } = RuntimeInformation.OSDescription;
    public string ClientArchitecture { get; } = RuntimeInformation.ProcessArchitecture.ToString();
    public string ClientRuntime { get; } = RuntimeInformation.FrameworkDescription;
    public string? ExecutableSha256 { get; set; }
    public string? SourceSha256 { get; set; }
    public int SourceUtf16Length { get; set; }
    public int HostUtf16Length { get; set; }
    public int? HostPatternUtf16Length { get; set; }
    public int? HostPatternPrefixUtf16Length { get; set; }
    public int ProcessId { get; set; }
    public OversizeObservation? Oversize { get; set; }
    public TreeObservation? Tree { get; set; }
    public string? NativeStderr { get; set; }
    public List<CheckResult> Checks { get; } = [];
    public List<Observation> Observations { get; } = [];
    public List<string> Failures { get; } = [];
    public List<string> ReleaseBlockers { get; } = [];

    /// <summary>Adds an assertion without discarding later independent evidence.</summary>
    public void Check(string name, bool passed, string detail)
    {
        Checks.Add(new CheckResult(name, passed, detail));
        if (!passed) Failures.Add($"{name}: {detail}");
    }

    /// <summary>Records an exception with its exact HRESULT and likely boundary, not a presumed root cause.</summary>
    public void Fail(string name, string boundary, Exception exception)
    {
        Observations.Add(new Observation(name, exception.GetType().Name, exception.HResult));
        Failures.Add($"{name} [{boundary}]: {exception.GetType().Name} HRESULT=0x{exception.HResult:X8}: {exception.Message}");
    }
}

/// <summary>One externally observed assertion.</summary>
internal sealed record CheckResult(string Name, bool Passed, string Detail);

/// <summary>Exact exception/HRESULT observation.</summary>
internal sealed record Observation(string Stage, string Type, int HResult);

/// <summary>Oversized unbounded request result; a successful partial value is never accepted.</summary>
internal sealed record OversizeObservation(string Type, int HResult, int? ReturnedUtf16Length, string? Message);

/// <summary>Raw/Control/Content tree and focus identities for duplicate-Document diagnosis.</summary>
internal sealed record TreeObservation(string Canvas, string SourceDocument, string Host,
    string RawChild, string ControlChild, string ContentChild, string Focused,
    bool CanvasHasKeyboardFocus, bool SourceHasKeyboardFocus, bool HostHasKeyboardFocus,
    IReadOnlyList<string> RawDocuments, IReadOnlyList<string> ControlDocuments,
    IReadOnlyList<string> ContentDocuments);
