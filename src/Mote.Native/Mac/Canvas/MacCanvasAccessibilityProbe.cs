using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Mote.Configuration;
using Mote.Engine;
using Mote.Themes;

namespace Mote.Native.Mac.Canvas;

/// <summary>
/// Published-Mach-O AppKit AX tree and selector probe for the opt-in canvas.
/// It does not substitute for an external AXUIElement client or VoiceOver.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacCanvasAccessibilityProbe
{
    private const string Runtime = "/usr/lib/libobjc.A.dylib";
    private const string Marker = "AX_OFFSCREEN_MARKER";
    private const string UnavailableStatus = "Accessibility provider unavailable";

    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    private static extern ObjC.Range RangeForLine(nint receiver, nint selector, nint line);

    /// <summary>
    /// Opens a mixed-newline fixture, discovers the attached AX element through
    /// the real AppKit tree, queries offscreen text, then tests New/fault/close.
    /// </summary>
    internal static int Run(string input)
    {
        Workflow? workflow = null;
        try
        {
            var path = Path.GetFullPath(input);
            var root = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".temp"));
            var file = new FileInfo(path);
            if (!string.Equals(Path.GetDirectoryName(path), root, StringComparison.Ordinal) ||
                !file.Exists || file.LinkTarget is not null || file.Length > 512 * 1024)
                return 2;
            using var source = Document.OpenAsync(path).GetAwaiter().GetResult();
            var original = source.Snapshot.GetText();
            var markerAt = original.IndexOf(Marker, StringComparison.Ordinal);
            if (markerAt <= 64 * 1024 || original.IndexOf("\r\n", StringComparison.Ordinal) < 0 ||
                original.IndexOf("😀", StringComparison.Ordinal) < 0 ||
                original.IndexOf('\r') < 0 ||
                original.IndexOf('\n') < 0)
                return 2;
            var before = SHA256.HashData(File.ReadAllBytes(path));
            var shell = new MacEditorShell(experimentalCanvas: true);
            var config = MoteConfigLoader.Load();
            var theme = ThemePolicies.Resolve(config.ThemeId, shell.PrefersDark);
            using var controller = new NativeEditorController(shell, config, theme, path);
            workflow = new Workflow(shell, path, source.Snapshot, markerAt);
            shell.Shown += workflow.Start;
            controller.Run();
            workflow.WriteMetrics();
            if (!workflow.Succeeded || !workflow.CheckDetachedAfterRun() ||
                !SHA256.HashData(File.ReadAllBytes(path)).AsSpan().SequenceEqual(before))
                return 1;
            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"Mac canvas AX probe failed: {error.GetType().Name}.");
            return 1;
        }
        finally { workflow?.ReleaseElement(); }
    }

    private static bool Responds(nint value, string selector) => value != 0 &&
        ObjC.Send(value, ObjC.Sel("respondsToSelector:"), ObjC.Sel(selector)) != 0;

    private static IEnumerable<nint> Items(nint array)
    {
        if (array == 0) yield break;
        var count = Math.Min(512, checked((int)ObjC.Send(array, ObjC.Sel("count"))));
        for (var index = 0; index < count; index++)
            yield return ObjC.Send(array, ObjC.Sel("objectAtIndex:"), index);
    }

    private static nint FindSourceElement(nint window)
    {
        var queue = new Queue<nint>();
        var seen = new HashSet<nint>();
        queue.Enqueue(window);
        var content = ObjC.Send(window, ObjC.Sel("contentView"));
        if (content != 0) queue.Enqueue(content);
        nint found = 0;
        while (queue.Count > 0 && seen.Count < 512)
        {
            var node = queue.Dequeue();
            if (node == 0 || !seen.Add(node)) continue;
            if (Responds(node, "accessibilityRole") &&
                ObjC.ManagedString(ObjC.Send(node, ObjC.Sel("accessibilityRole"))) == "AXTextArea" &&
                Responds(node, "accessibilityLabel") &&
                ObjC.ManagedString(ObjC.Send(node, ObjC.Sel("accessibilityLabel"))) == "Mote editor")
            {
                if (found != 0) throw new InvalidOperationException("Multiple source AX text areas exist.");
                found = node;
            }
            if (Responds(node, "accessibilityChildren"))
                foreach (var child in Items(ObjC.Send(node, ObjC.Sel("accessibilityChildren"))))
                    queue.Enqueue(child);
            if (Responds(node, "subviews"))
                foreach (var child in Items(ObjC.Send(node, ObjC.Sel("subviews"))))
                    queue.Enqueue(child);
        }
        if (found == 0) throw new InvalidOperationException("Source AX element was not in AppKit's tree.");
        ObjC.Send(found, ObjC.Sel("retain"));
        return found;
    }

    /// <summary>
    /// Finds exactly one separately labeled, read-only preview in AppKit's
    /// accessibility children without using its rendered document text.
    /// </summary>
    private static void CheckPreviewElement(nint window, nint source, nint preview)
    {
        if (preview == 0 || preview == source ||
            ObjC.Send(preview, ObjC.Sel("isEditable")) != 0 ||
            ObjC.ManagedString(ObjC.Send(preview, ObjC.Sel("accessibilityRole"))) != "AXTextArea" ||
            ObjC.ManagedString(ObjC.Send(preview, ObjC.Sel("accessibilityLabel"))) != "Mote preview")
            throw new InvalidOperationException("Preview AX role, label, or read-only state is wrong.");
        var queue = new Queue<nint>();
        var seen = new HashSet<nint>();
        queue.Enqueue(window);
        var matches = 0;
        var previewNodes = 0;
        while (queue.Count > 0 && seen.Count < 512)
        {
            var node = queue.Dequeue();
            if (node == 0 || !seen.Add(node)) continue;
            if (node == preview) ++previewNodes;
            if (Responds(node, "accessibilityRole") &&
                ObjC.ManagedString(ObjC.Send(node, ObjC.Sel("accessibilityRole"))) == "AXTextArea" &&
                Responds(node, "accessibilityLabel") &&
                ObjC.ManagedString(ObjC.Send(node, ObjC.Sel("accessibilityLabel"))) == "Mote preview")
                ++matches;
            if (Responds(node, "accessibilityChildren"))
                foreach (var child in Items(ObjC.Send(node, ObjC.Sel("accessibilityChildren"))))
                    queue.Enqueue(child);
        }
        if (queue.Count != 0 || matches != 1 || previewNodes != 1)
            throw new InvalidOperationException("Preview AX child is absent, duplicated, or tree traversal was truncated.");
    }

    private sealed class Workflow
    {
        private readonly MacEditorShell _shell;
        private readonly string _path;
        private readonly TextSnapshot _source;
        private readonly int _markerAt;
        private readonly DateTime _deadline = DateTime.UtcNow.AddSeconds(90);
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly List<string> _metrics = [];
        private nint _element;
        private int _stage;
        private bool _done;
        private bool _faultRequestReturned;
        private NativeDocumentStamp? _faultSourceStamp;
        private int _faultSourceLength;
        private byte[]? _faultSourceHash;
        private NativeDocumentStamp? _lastHashedStamp;
        private int _lastHashedLength = -1;
        private bool _lastHashEquality;
        private string? _lastFaultState;
        /// <summary>Caps changed polling records without dropping forced request/deadline evidence.</summary>
        private const int MaxFaultPollRecords = 32;
        /// <summary>Number of changed-state polls retained across stages 3 and 5.</summary>
        private int _faultPollRecords;
        /// <summary>Binding identity immediately before the second New request, after editing N.</summary>
        private NativeDocumentStamp? _beforeNewStamp;
        /// <summary>True after the second menu request returns, not proof that New completed.</summary>
        private bool _newRequestReturned;

        internal Workflow(MacEditorShell shell, string path, TextSnapshot source, int markerAt)
        {
            _shell = shell;
            _path = path;
            _source = source;
            _markerAt = markerAt;
        }

        internal bool Succeeded { get; private set; }

        internal void Start() => Schedule();

        private void Schedule()
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(45).ConfigureAwait(false);
                if (!_done) _shell.Post(Tick);
            });
        }

        private void Tick()
        {
            if (_done) return;
            if (DateTime.UtcNow >= _deadline)
            {
                try
                {
                    if (_stage is 3 or 5) RecordFaultState("deadline", force: true);
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    Console.Error.WriteLine($"Mac canvas AX deadline diagnostic: {error.GetType().Name}.");
                }
                finally { Finish(false); }
                return;
            }
            try
            {
                var before = _stage;
                switch (_stage)
                {
                    case 0 when _shell.ProbeTitle.Contains(Path.GetFileName(_path),
                        StringComparison.Ordinal) &&
                        _shell.ProbeCanvasSnapshot?.Length == _source.Length &&
                        _shell.ProbeCanvasAccessibilityAttached:
                        _element = FindSourceElement(_shell.ProbeWindow);
                        CheckSourceSelectors();
                        CheckPreviewElement(_shell.ProbeWindow, _element,
                            _shell.ProbePreviewView);
                        _metrics.Add("preview_ax_role=AXTextArea preview_label=Mote preview " +
                            "preview_read_only=True source_label=Mote editor distinct=True");
                        if (!_shell.ProbeCanvasInputEditable ||
                            !_shell.ProbeCanvasInputFocused ||
                            _shell.ProbeCanvasInputAccessible)
                            throw new InvalidOperationException("Input host AX/focus ownership is wrong.");
                        ObjC.Send(_shell.ProbeWindow, ObjC.Sel("setContentSize:"),
                            new ObjC.Size(1200, 820));
                        var canvas = ObjC.Send(_element, ObjC.Sel("accessibilityParent"));
                        ObjC.Send(canvas, ObjC.Sel("setNeedsDisplay:"), 1);
                        ObjC.Send(canvas, ObjC.Sel("displayIfNeeded"));
                        _stage = 1;
                        break;
                    case 1:
                        var frame = MacOnScreenCanvasNative.GetRect(_element,
                            ObjC.Sel("accessibilityFrameInParentSpace"));
                        if (frame.Size.Width <= 0 || frame.Size.Height <= 0)
                            throw new InvalidOperationException("AX frame is invalid after resize.");
                        _shell.ProbeInvokeMenu("moteNew:");
                        _stage = 2;
                        break;
                    case 2 when _shell.ProbeCanvasSnapshot?.Length == 0:
                        if (ObjC.Send(_element,
                                ObjC.Sel("accessibilityNumberOfCharacters")) != 0 ||
                            ObjC.Send(_element, ObjC.Sel("accessibilityStringForRange:"),
                                new ObjC.Range((nuint)_markerAt, (nuint)Marker.Length)) != 0)
                            throw new InvalidOperationException("Held AX element returned stale file content.");
                        CaptureFaultSource();
                        _shell.ProbeCanvasAccessibilityFault();
                        _faultRequestReturned = true;
                        _stage = 3;
                        RecordFaultState("request-returned", force: true);
                        break;
                    case 3 when !_shell.ProbeCanvasAccessibilityAttached &&
                        StatusMatches():
                        if (!_shell.ProbeCanvasInputEditable || !_shell.ProbeCanvasInputFocused)
                            throw new InvalidOperationException("AX fault disabled text editing.");
                        _shell.ProbeInsertAtEnd("N");
                        _stage = 4;
                        break;
                    case 4 when _shell.ProbeCanvasSnapshot?.GetText() == "N" &&
                        StatusMatches():
                        _beforeNewStamp = _shell.ProbeCanvasStamp;
                        _shell.ProbeApproveDiscardOnce();
                        _shell.ProbeInvokeMenu("moteNew:");
                        _newRequestReturned = true;
                        _stage = 5;
                        RecordFaultState("new-request-returned", force: true);
                        break;
                    case 5 when _shell.ProbeCanvasSnapshot?.Length == 0 &&
                        StatusMatches():
                        _shell.ProbeInsertAtEnd("M");
                        _stage = 6;
                        break;
                    case 6 when _shell.ProbeCanvasSnapshot?.GetText() == "M" &&
                        !_shell.ProbeCanvasAccessibilityAttached &&
                        _shell.ProbeCanvasInputEditable:
                        _shell.ProbeApproveDiscardOnce();
                        Succeeded = true;
                        Finish(true);
                        return;
                }
                if (_stage is 3 or 5) RecordFaultState("poll");
                if (_stage != before) Record($"stage-{before}-to-{_stage}");
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                Console.Error.WriteLine($"Mac canvas AX stage {_stage}: {error.GetType().Name}.");
                Finish(false);
                return;
            }
            Schedule();
        }

        /// <summary>Captures the post-New canonical identity before injecting an AX-only fault.</summary>
        private void CaptureFaultSource()
        {
            var snapshot = _shell.ProbeCanvasSnapshot ??
                throw new InvalidOperationException("Canvas source was absent before AX fault.");
            _faultSourceStamp = _shell.ProbeCanvasStamp;
            _faultSourceLength = snapshot.Length;
            _faultSourceHash = SHA256.HashData(Encoding.Unicode.GetBytes(snapshot.GetText()));
        }

        private bool StatusMatches() => _shell.ProbeCanvasStatus.Contains(
            UnavailableStatus, StringComparison.Ordinal);

        /// <summary>
        /// Records only source identity, status-token presence, provider state and input
        /// liveness. Request return is not evidence that AppKit ran the detach selector.
        /// Equality fields use the pre-fault stamp; before-New fields separately identify
        /// replacement after editing N. Changed polls are capped; deadlines are retained.
        /// </summary>
        private void RecordFaultState(string point, bool force = false)
        {
            var snapshot = _shell.ProbeCanvasSnapshot;
            var stamp = _shell.ProbeCanvasStamp;
            var generationMatches = stamp.HasValue && _faultSourceStamp.HasValue &&
                stamp.Value.Generation == _faultSourceStamp.Value.Generation;
            var versionMatches = stamp.HasValue && _faultSourceStamp.HasValue &&
                stamp.Value.Version == _faultSourceStamp.Value.Version;
            var lengthMatches = snapshot?.Length == _faultSourceLength;
            // Snapshots are immutable. Rehash at injection/deadline or after a
            // changed stamp/length, never on every 45 ms stage-3 poll.
            var hashEquality = false;
            if (snapshot is not null && lengthMatches && _faultSourceHash is not null)
            {
                if (force || stamp != _lastHashedStamp || snapshot.Length != _lastHashedLength)
                {
                    _lastHashEquality = SHA256.HashData(Encoding.Unicode.GetBytes(snapshot.GetText()))
                        .AsSpan().SequenceEqual(_faultSourceHash);
                    _lastHashedStamp = stamp;
                    _lastHashedLength = snapshot.Length;
                }
                hashEquality = _lastHashEquality;
            }
            var state = $"stage={_stage} fault_request_returned={_faultRequestReturned} " +
                $"new_request_returned={_newRequestReturned} " +
                $"provider_attached={_shell.ProbeCanvasAccessibilityAttached} " +
                $"status_match={StatusMatches()} " +
                $"status_exact_suffix={_shell.ProbeCanvasStatus.EndsWith(" · " + UnavailableStatus, StringComparison.Ordinal)} " +
                $"source_stamp_present={stamp.HasValue} " +
                $"source_snapshot_present={snapshot is not null} " +
                $"source_empty={snapshot is { Length: 0 }} " +
                $"source_generation={stamp?.Generation ?? -1} " +
                $"source_version={stamp?.Version ?? -1} " +
                $"snapshot_version={snapshot?.Version ?? -1} " +
                $"snapshot_version_matches_stamp={snapshot is not null && stamp.HasValue && snapshot.Version == stamp.Value.Version} " +
                $"before_new_stamp_present={_beforeNewStamp.HasValue} " +
                $"before_new_generation_equal={stamp.HasValue && _beforeNewStamp.HasValue && stamp.Value.Generation == _beforeNewStamp.Value.Generation} " +
                $"before_new_version_equal={stamp.HasValue && _beforeNewStamp.HasValue && stamp.Value.Version == _beforeNewStamp.Value.Version} " +
                $"source_length={snapshot?.Length ?? -1} " +
                $"source_generation_equal={generationMatches} " +
                $"source_version_equal={versionMatches} " +
                $"source_length_equal={lengthMatches} " +
                $"source_hash_equal={hashEquality} " +
                $"input_editable={_shell.ProbeCanvasInputEditable} " +
                $"input_focused={_shell.ProbeCanvasInputFocused}";
            if (!force && state == _lastFaultState) return;
            if (!force && _faultPollRecords >= MaxFaultPollRecords) return;
            if (!force) ++_faultPollRecords;
            _lastFaultState = state;
            _metrics.Add($"ax-fault-{point} elapsed_ms={_clock.ElapsedMilliseconds} " +
                $"poll_records={_faultPollRecords} poll_limit={MaxFaultPollRecords} {state}");
        }

        private void CheckSourceSelectors()
        {
            var count = ObjC.Send(_element, ObjC.Sel("accessibilityNumberOfCharacters"));
            var selected = ObjC.SendRange(_element, ObjC.Sel("accessibilitySelectedTextRange"));
            var visible = ObjC.SendRange(_element, ObjC.Sel("accessibilityVisibleCharacterRange"));
            if (count != _source.Length || selected.Location > (nuint)_source.Length ||
                selected.Length > (nuint)_source.Length - selected.Location ||
                visible.Length == 0 || visible.Location > (nuint)_source.Length ||
                visible.Length > (nuint)_source.Length - visible.Location)
                throw new InvalidOperationException("Source-backed AX ranges are invalid.");
            var found = ObjC.ManagedString(ObjC.Send(_element,
                ObjC.Sel("accessibilityStringForRange:"),
                new ObjC.Range((nuint)_markerAt, (nuint)Marker.Length)));
            if (found != Marker)
                throw new InvalidOperationException("Offscreen AX text differs from source.");
            var line = ObjC.Send(_element, ObjC.Sel("accessibilityLineForIndex:"),
                _markerAt);
            var expectedLine = _source.GetLineIndexFromOffset(_markerAt);
            var lineRange = RangeForLine(_element, ObjC.Sel("accessibilityRangeForLine:"), line);
            var start = _source.GetLineStartOffset(expectedLine);
            var end = expectedLine + 1 < _source.LineCount
                ? _source.GetLineStartOffset(expectedLine + 1) : _source.Length;
            if (line != expectedLine || lineRange.Location != (nuint)start ||
                lineRange.Length != (nuint)(end - start))
                throw new InvalidOperationException("AX line and NSRange ABI differ from source.");
            var frame = MacOnScreenCanvasNative.GetRect(_element,
                ObjC.Sel("accessibilityFrameInParentSpace"));
            if (frame.Size.Width <= 0 || frame.Size.Height <= 0)
                throw new InvalidOperationException("Source AX element has no parent-space frame.");
            _metrics.Add($"source_length={_source.Length} marker_offset={_markerAt} " +
                $"selected={selected.Location},{selected.Length} " +
                $"visible={visible.Location},{visible.Length} " +
                $"marker_line={line} line_range={lineRange.Location},{lineRange.Length} " +
                $"frame={frame.Origin.X:R},{frame.Origin.Y:R}," +
                $"{frame.Size.Width:R},{frame.Size.Height:R}");
        }

        internal bool CheckDetachedAfterRun()
        {
            if (_element == 0) return false;
            ObjC.ApplicationLoad();
            var pool = ObjC.New("NSAutoreleasePool");
            try
            {
                return ObjC.Send(_element, ObjC.Sel("accessibilityNumberOfCharacters")) == 0 &&
                    ObjC.Send(_element, ObjC.Sel("accessibilityStringForRange:"),
                        new ObjC.Range((nuint)_markerAt, (nuint)Marker.Length)) == 0;
            }
            finally { ObjC.Send(pool, ObjC.Sel("release")); }
        }

        internal void ReleaseElement()
        {
            if (_element == 0) return;
            ObjC.Send(_element, ObjC.Sel("release"));
            _element = 0;
        }

        internal void WriteMetrics()
        {
            var rid = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "osx-arm64" : "osx-x64";
            var output = Path.Combine(Environment.CurrentDirectory, ".cache",
                "ci-inventory", rid);
            Directory.CreateDirectory(output);
            File.WriteAllLines(Path.Combine(output, "mac-canvas-ax-metrics.txt"), _metrics);
        }

        private void Record(string stage)
        {
            using var process = Process.GetCurrentProcess();
            _metrics.Add($"{stage} elapsed_ms={_clock.ElapsedMilliseconds} " +
                $"peak_working_set_bytes={process.PeakWorkingSet64}");
        }

        private void Finish(bool success)
        {
            _done = true;
            Succeeded = success;
            Record(success ? "completed" : "failed");
            if (!success) Console.Error.WriteLine($"Mac canvas AX stage {_stage} failed.");
            _shell.ProbeApproveDiscardOnce();
            _shell.Close();
        }
    }
}
