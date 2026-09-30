using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mote.Configuration;
using Mote.Themes;

namespace Mote.Native.Mac.Canvas;

/// <summary>
/// Published-Mach-O diagnostic for a window-local appearance change while
/// AppKit's real NSTextView owns synthetic marked text. It is not a physical
/// keyboard, input-method conversion, or CJK candidate-window test.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacCompositionThemeProbe
{
    private const string Heading = "MOTE_THEME_HEADING";
    private const string Body = "MOTE_THEME_BODY";
    private const string Candidate = "候";
    private const string CancelCandidate = "消";
    private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";

    /// <summary>
    /// Exercises one isolated mode with a direct repository .temp fixture and
    /// an empty direct .cache output child; never reads the user's ~/.mote.
    /// </summary>
    internal static int Run(string input, string outputDirectory, string mode)
    {
        if (mode is not ("default" or "canvas")) return 2;
        try
        {
            var path = Fixture(input);
            var output = Output(outputDirectory);
            var before = SHA256.HashData(File.ReadAllBytes(path));
            var temp = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".temp"));
            var config = MoteConfigLoader.Load(new MoteConfigLoadOptions
            {
                MoteHomeOverride = Path.Combine(temp,
                    $"composition-theme-config-{mode}-{Environment.ProcessId}"),
                UseEnvironmentOverride = false
            }) with { ThemeId = ThemePolicies.SystemId, TraceEnabled = false };
            var shell = new MacEditorShell(experimentalCanvas: mode == "canvas");
            var theme = ThemePolicies.Resolve(config.ThemeId, shell.PrefersDark);
            using var controller = new NativeEditorController(shell, config, theme, path);
            var workflow = new Workflow(shell, path, output, mode, before);
            shell.Shown += workflow.Start;
            controller.Run();
            var unchanged = SHA256.HashData(File.ReadAllBytes(path))
                .AsSpan().SequenceEqual(before);
            workflow.WriteResult(unchanged);
            return workflow.Succeeded && unchanged ? 0 : 1;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"Mac composition-theme probe failed: {error.GetType().Name}.");
            return 1;
        }
    }

    private static string Fixture(string path)
    {
        var full = Path.GetFullPath(path);
        var temp = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".temp"));
        var file = new FileInfo(full);
        if (!string.Equals(Path.GetDirectoryName(full), temp, StringComparison.Ordinal) ||
            !string.Equals(file.Extension, ".md", StringComparison.OrdinalIgnoreCase) ||
            !file.Exists || file.LinkTarget is not null || file.Length is < 32 or > 65536)
            throw new ArgumentException("Composition-theme fixture must be bounded Markdown in .temp.");
        var text = File.ReadAllText(full);
        if (!text.StartsWith($"# {Heading}", StringComparison.Ordinal) ||
            !text.Contains(Body, StringComparison.Ordinal))
            throw new ArgumentException("Composition-theme fixture markers are absent.");
        return full;
    }

    private static string Output(string path)
    {
        var full = Path.GetFullPath(path);
        var cache = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".cache"));
        if (!string.Equals(Path.GetDirectoryName(full), cache, StringComparison.Ordinal) ||
            Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any())
            throw new ArgumentException("Composition-theme output must be an empty .cache child.");
        Directory.CreateDirectory(full);
        if (new DirectoryInfo(full).LinkTarget is not null)
            throw new ArgumentException("Composition-theme output cannot be a symlink.");
        return full;
    }

    private sealed class Workflow
    {
        private readonly MacEditorShell _shell;
        private readonly string _input;
        private readonly string _output;
        private readonly string _mode;
        private readonly byte[] _originalFileHash;
        private readonly DateTime _deadline = DateTime.UtcNow.AddSeconds(90);
        private readonly List<Phase> _phases = [];
        private int _stage;
        private int _appearanceCallbacks;
        private int _settledCallbacks;
        private int _beforeAppearance;
        private int _beforeSettled;
        private long _baseVersion;
        private long _committedVersion;
        private long _undoVersion;
        private NativeDocumentStamp _baseStamp;
        private string _initialSource = string.Empty;
        private string _committedSource = string.Empty;
        private string _hostBeforeCancellation = string.Empty;
        private int _insertionOffset;
        private (int Anchor, int Active) _initialSelection;
        private (int Anchor, int Active) _undoSelection;
        private (int Anchor, int Active) _redoSelection;
        private ObjC.Range _markedSelection;
        private bool _preeditIsolated;
        private bool _policyDeferred;
        private bool _cancelIsolated;
        private bool _cancelVersionUnchanged;
        private bool _cancelSelectionPreserved;
        private bool _undoRestored;
        private bool _redoRestored;
        private bool _done;
        private string _check = "startup";

        internal Workflow(MacEditorShell shell, string input, string output,
            string mode, byte[] originalFileHash)
        {
            _shell = shell;
            _input = input;
            _output = output;
            _mode = mode;
            _originalFileHash = originalFileHash;
            shell.AppearanceChanged += () => ++_appearanceCallbacks;
            shell.CompositionSettled += () => ++_settledCallbacks;
        }

        internal bool Succeeded { get; private set; }

        internal void Start() => Schedule();

        private void Schedule()
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(55).ConfigureAwait(false);
                if (!_done) _shell.Post(Tick);
            });
        }

        private void Tick()
        {
            if (_done) return;
            if (DateTime.UtcNow >= _deadline)
            {
                Console.Error.WriteLine("Mac composition-theme readiness: " +
                    $"stage={_stage};marked={_shell.ProbeHasMarkedText};" +
                    $"composing={_shell.IsTextComposing};" +
                    $"os-dark={_shell.PrefersDark};" +
                    $"policy={_shell.ProbeThemeId ?? "unavailable"};" +
                    $"version-advanced={CurrentStamp().Version > _baseVersion};" +
                    $"source-original={CurrentSource() == _initialSource};" +
                    $"source-committed={CurrentSource() == _committedSource};" +
                    $"analysis-ready={AnalysisReady()};" +
                    $"appearance-callbacks={_appearanceCallbacks};" +
                    $"settled-callbacks={_settledCallbacks};");
                Fail("deadline");
                return;
            }
            try
            {
                switch (_stage)
                {
                    case 0 when Ready():
                        _check = "initial-dark";
                        _shell.ProbeSetWindowAppearance(true);
                        _stage = 1;
                        break;
                    case 1 when _shell.PrefersDark &&
                        _shell.ProbeThemeId == ThemePolicies.DarkId && Ready():
                        _check = "begin-marked";
                        _initialSource = CurrentSource();
                        _baseStamp = CurrentStamp();
                        _baseVersion = _baseStamp.Version;
                        _insertionOffset = _mode == "canvas"
                            ? checked(_shell.ProbeCanvasInputStart + _shell.ProbeNativeText.Length)
                            : _initialSource.Length;
                        if (_insertionOffset < 0 || _insertionOffset > _initialSource.Length)
                            throw new InvalidOperationException("Marked insertion offset is outside source.");
                        _committedSource = _initialSource.Insert(_insertionOffset, Candidate);
                        _initialSelection = Selection();
                        if (_initialSelection.Anchor != _initialSelection.Active)
                            throw new InvalidOperationException("Composition fixture started with a selection.");
                        Capture("dark-before", ThemePolicies.DarkId);
                        _beforeAppearance = _appearanceCallbacks;
                        _beforeSettled = _settledCallbacks;
                        _shell.ProbeSetMarkedAtEnd(Candidate);
                        _stage = 2;
                        break;
                    case 2 when _shell.ProbeHasMarkedText && _shell.IsTextComposing:
                        _check = "marked-before-appearance";
                        CheckPreeditIsolation();
                        CheckMarkedSelection(_mode == "canvas"
                            ? _initialSelection.Anchor : _insertionOffset + Candidate.Length);
                        _markedSelection = ObjC.SendRange(_shell.ProbeEditorView,
                            ObjC.Sel("selectedRange"));
                        _shell.ProbeSetWindowAppearance(false);
                        _stage = 3;
                        break;
                    case 3 when !_shell.PrefersDark &&
                        _appearanceCallbacks > _beforeAppearance:
                        _check = "deferred-light";
                        if (!_shell.ProbeHasMarkedText || !_shell.IsTextComposing ||
                            _shell.ProbeThemeId != ThemePolicies.DarkId ||
                            ObjC.SendRange(_shell.ProbeEditorView,
                                ObjC.Sel("selectedRange")) != _markedSelection)
                            throw new InvalidOperationException("Native marked state or palette changed early.");
                        CheckPreeditIsolation();
                        CheckMarkedSelection(_mode == "canvas"
                            ? _initialSelection.Anchor : _insertionOffset + Candidate.Length);
                        _policyDeferred = true;
                        Capture("light-marked", ThemePolicies.DarkId);
                        // Apple specifies unmarkText accepts the current marked
                        // string; this tests a synthetic commit, not IME choice.
                        ObjC.Send(_shell.ProbeEditorView, ObjC.Sel("unmarkText"));
                        _stage = 4;
                        break;
                    case 4 when !_shell.ProbeHasMarkedText && !_shell.IsTextComposing &&
                        CurrentStamp().Version > _baseVersion &&
                        CurrentSource() == _committedSource &&
                        _shell.ProbeThemeId == ThemePolicies.LightId &&
                        _settledCallbacks > _beforeSettled && AnalysisReady():
                        _check = "committed-light";
                        CheckSelection(_insertionOffset + Candidate.Length);
                        _committedVersion = CurrentStamp().Version;
                        Capture("light-settled", ThemePolicies.LightId);
                        _hostBeforeCancellation = _shell.ProbeNativeText;
                        _beforeAppearance = _appearanceCallbacks;
                        _beforeSettled = _settledCallbacks;
                        _shell.ProbeSetMarkedAtEnd(CancelCandidate);
                        _stage = 5;
                        break;
                    case 5 when _shell.ProbeHasMarkedText && _shell.IsTextComposing:
                        _check = "cancel-marked-before-appearance";
                        CheckCancellationIsolation();
                        if (_shell.ProbeNativeText !=
                            _hostBeforeCancellation + CancelCandidate)
                            throw new InvalidOperationException("Synthetic cancel mark is absent from native host.");
                        CheckMarkedSelection(_mode == "canvas"
                            ? _insertionOffset + Candidate.Length
                            : _insertionOffset + Candidate.Length + CancelCandidate.Length);
                        _markedSelection = ObjC.SendRange(_shell.ProbeEditorView,
                            ObjC.Sel("selectedRange"));
                        _shell.ProbeSetWindowAppearance(true);
                        _stage = 6;
                        break;
                    case 6 when _shell.PrefersDark &&
                        _appearanceCallbacks > _beforeAppearance:
                        _check = "deferred-dark";
                        if (!_shell.ProbeHasMarkedText || !_shell.IsTextComposing ||
                            _shell.ProbeThemeId != ThemePolicies.LightId ||
                            ObjC.SendRange(_shell.ProbeEditorView,
                                ObjC.Sel("selectedRange")) != _markedSelection)
                            throw new InvalidOperationException("Native cancellation mark or palette changed early.");
                        CheckCancellationIsolation();
                        if (_shell.ProbeNativeText !=
                            _hostBeforeCancellation + CancelCandidate)
                            throw new InvalidOperationException("Native cancel mark changed during appearance switch.");
                        CheckMarkedSelection(_mode == "canvas"
                            ? _insertionOffset + Candidate.Length
                            : _insertionOffset + Candidate.Length + CancelCandidate.Length);
                        Capture("dark-marked", ThemePolicies.LightId);
                        ClearSyntheticMarkedText();
                        _stage = 7;
                        break;
                    case 7 when !_shell.ProbeHasMarkedText && !_shell.IsTextComposing &&
                        _shell.ProbeThemeId == ThemePolicies.DarkId &&
                        _settledCallbacks > _beforeSettled && AnalysisReady():
                        _check = "cancelled-dark-canonical";
                        CheckCancellationIsolation();
                        _check = "cancelled-dark-native-host";
                        if (_shell.ProbeNativeText != _hostBeforeCancellation)
                            throw new InvalidOperationException("Cancelled candidate remains in native host.");
                        _check = "cancelled-dark-selection";
                        CheckSelection(_insertionOffset + Candidate.Length);
                        _cancelVersionUnchanged = CurrentStamp().Version == _committedVersion;
                        _cancelSelectionPreserved = true;
                        _cancelIsolated = true;
                        _check = "cancelled-dark-preview";
                        Capture("dark-cancelled", ThemePolicies.DarkId);
                        _shell.ProbeInvokeMenu("moteUndo:");
                        _stage = 8;
                        break;
                    case 8 when CurrentStamp().Version > _committedVersion &&
                        CurrentSource() == _initialSource:
                        _check = "undo";
                        CheckSelection(_insertionOffset);
                        _undoSelection = Selection();
                        _undoVersion = CurrentStamp().Version;
                        _undoRestored = true;
                        _shell.ProbeInvokeMenu("moteRedo:");
                        _stage = 9;
                        break;
                    case 9 when CurrentStamp().Version > _undoVersion &&
                        CurrentSource() == _committedSource:
                        _check = "redo";
                        CheckSelection(_insertionOffset + Candidate.Length);
                        _redoSelection = Selection();
                        _redoRestored = true;
                        if (!SameInput())
                            throw new InvalidOperationException("Probe changed its input file.");
                        Finish(true);
                        return;
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                Console.Error.WriteLine($"Mac composition-theme stage {_stage} check {_check}: " +
                    $"{error.GetType().Name}.");
                if (_stage == 7) ReportCancellationState();
                Finish(false);
                return;
            }
            Schedule();
        }

        private bool Ready() => CurrentStampNullable() is not null && AnalysisReady() &&
            CurrentSource().Contains(Heading, StringComparison.Ordinal) &&
            CurrentSource().Contains(Body, StringComparison.Ordinal);

        private bool AnalysisReady() => _shell.ProbeAnalysis is { } analysis &&
            analysis.Stamp == CurrentStamp() &&
            analysis.PreviewSpans?.Any(static span => span.Kind == "heading") == true;

        private NativeDocumentStamp? CurrentStampNullable() => _mode == "canvas"
            ? _shell.ProbeCanvasStamp : _shell.ProbeDocumentStamp;

        private NativeDocumentStamp CurrentStamp() => CurrentStampNullable() ?? default;

        private string CurrentSource() => _mode == "canvas"
            ? _shell.ProbeCanvasSnapshot?.GetText() ?? string.Empty
            : _shell.ProbeProjectedText;

        private bool SameInput() => SHA256.HashData(File.ReadAllBytes(_input))
            .AsSpan().SequenceEqual(_originalFileHash);

        private void CheckPreeditIsolation()
        {
            if (CurrentStamp() != _baseStamp || CurrentSource() != _initialSource ||
                !SameInput())
                throw new InvalidOperationException("Marked text escaped into canonical source.");
            _preeditIsolated = true;
        }

        private void CheckMarkedSelection(int expected)
        {
            if (Selection() != (expected, expected))
                throw new InvalidOperationException("Marked-text selection moved outside its expected state.");
        }

        private void CheckCancellationIsolation()
        {
            if (CurrentStamp().Generation != _baseStamp.Generation ||
                CurrentStamp().Version != _committedVersion ||
                CurrentSource() != _committedSource || !SameInput())
                throw new InvalidOperationException("Cancelled text escaped into canonical source.");
        }

        /// <summary>Reports only bounded equality and length evidence for a failed synthetic cancel.</summary>
        private void ReportCancellationState()
        {
            try
            {
                var source = CurrentSource();
                var host = _shell.ProbeNativeText;
                var before = _hostBeforeCancellation;
                var stamp = CurrentStamp();
                var selection = Selection();
                var caret = _insertionOffset + Candidate.Length;
                var prefix = 0;
                while (prefix < Math.Min(host.Length, before.Length) &&
                    host[prefix] == before[prefix]) ++prefix;
                var suffix = 0;
                while (suffix < Math.Min(host.Length, before.Length) - prefix &&
                    host[host.Length - suffix - 1] == before[before.Length - suffix - 1]) ++suffix;
                var nativeSelection = ObjC.SendRange(_shell.ProbeEditorView,
                    ObjC.Sel("selectedRange"));
                Console.Error.WriteLine("Mac composition-theme cancel state: " +
                    $"generation-eq={stamp.Generation == _baseStamp.Generation};" +
                    $"version-eq={stamp.Version == _committedVersion};" +
                    $"source-eq={source == _committedSource};source-length={source.Length};" +
                    $"input-eq={SameInput()};host-eq={host == _hostBeforeCancellation};" +
                    $"host-length={host.Length};expected-host-length={before.Length};" +
                    $"first-mismatch={prefix};matching-suffix={suffix};" +
                    $"absolute-range-shift={before.Length > 0 && host == before[1..] + CancelCandidate};" +
                    $"tail-replaced={before.Length > 0 && host == before[..^1] + CancelCandidate};" +
                    $"native-selection={nativeSelection.Location},{nativeSelection.Length};" +
                    $"selection-eq={selection == (caret, caret)};analysis-ready={AnalysisReady()};");
            }
            catch (Exception) { /* A diagnostic cannot replace the original failure. */ }
        }

        /// <summary>
        /// Clears the client's provisional range before asking the input context
        /// to discard its conversion session. AppKit's unmarkText is deliberately
        /// excluded because it accepts rather than cancels marked text.
        /// </summary>
        private void ClearSyntheticMarkedText()
        {
            var editor = _shell.ProbeEditorView;
            var marked = ObjC.SendRange(editor, ObjC.Sel("markedRange"));
            if (marked.Location == nuint.MaxValue ||
                marked.Length != (nuint)CancelCandidate.Length)
                throw new InvalidOperationException("Synthetic cancellation range is invalid.");
            ObjC.Send(editor, ObjC.Sel("setMarkedText:selectedRange:replacementRange:"),
                ObjC.String(string.Empty), new ObjC.Range(0, 0),
                new ObjC.Range(0, marked.Length));
            if (_shell.ProbeHasMarkedText)
                throw new InvalidOperationException("AppKit did not clear synthetic marked text.");
            var context = ObjC.Send(editor, ObjC.Sel("inputContext"));
            if (context == 0)
                throw new InvalidOperationException("AppKit input context is unavailable for discard.");
            ObjC.Send(context, ObjC.Sel("discardMarkedText"));
        }

        private void CheckSelection(int expected)
        {
            if (Selection() != (expected, expected))
                throw new InvalidOperationException("Composition edit changed the expected source caret.");
        }

        private void Capture(string step, string appliedThemeId)
        {
            if (_shell.ProbeAnalysis?.PreviewSpans?.FirstOrDefault(
                static span => span.Kind == "heading") is not { } heading)
                throw new InvalidOperationException("Marked-theme preview heading is unavailable.");
            var expected = ThemePolicies.Get(appliedThemeId).Palette.Accent;
            var actual = PreviewColor(heading.Start);
            if (Math.Abs(actual.Red - expected.Red) > 1 ||
                Math.Abs(actual.Green - expected.Green) > 1 ||
                Math.Abs(actual.Blue - expected.Blue) > 1)
                throw new InvalidOperationException("Marked-theme preview color changed at the wrong time.");
            var view = ObjC.Send(_shell.ProbeWindow, ObjC.Sel("contentView"));
            ObjC.Send(view, ObjC.Sel("displayIfNeeded"));
            var rect = MacOnScreenCanvasNative.GetRect(view, ObjC.Sel("bounds"));
            var bitmap = ObjC.Send(view,
                ObjC.Sel("bitmapImageRepForCachingDisplayInRect:"), rect);
            if (bitmap == 0) throw new IOException("AppKit could not allocate marked-theme raster.");
            MacOnScreenCanvasNative.Send(view,
                ObjC.Sel("cacheDisplayInRect:toBitmapImageRep:"), rect, bitmap);
            var png = ObjC.Send(bitmap, ObjC.Sel("representationUsingType:properties:"),
                4, ObjC.Send(ObjC.Class("NSDictionary"), ObjC.Sel("dictionary")));
            var image = $"composition-theme-{_mode}-{step}.png";
            var path = Path.Combine(_output, image);
            if (png == 0 || ObjC.Send(png, ObjC.Sel("writeToFile:atomically:"),
                ObjC.String(path), (byte)1) == 0)
                throw new IOException("AppKit could not write marked-theme raster.");
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 8 || !bytes.AsSpan(0, 8).SequenceEqual(
                new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                throw new IOException("Marked-theme raster is not PNG.");
            var selection = Selection();
            var sourceHash = Convert.ToHexString(SHA256.HashData(
                Encoding.Unicode.GetBytes(CurrentSource())));
            _phases.Add(new Phase(step, _shell.PrefersDark,
                _shell.ProbeThemeId ?? "unavailable", _shell.ProbeHasMarkedText,
                _appearanceCallbacks, _settledCallbacks, CurrentStamp().Generation,
                CurrentStamp().Version, selection.Anchor, selection.Active,
                sourceHash, actual.ToHex(), image,
                Convert.ToHexString(SHA256.HashData(bytes))));
        }

        private (int Anchor, int Active) Selection()
        {
            if (_mode == "canvas") return _shell.ProbeCanvasSelection;
            var native = ObjC.SendRange(_shell.ProbeEditorView, ObjC.Sel("selectedRange"));
            return (checked((int)native.Location),
                checked((int)(native.Location + native.Length)));
        }

        private ThemeColor PreviewColor(int index)
        {
            var storage = ObjC.Send(_shell.ProbePreviewView, ObjC.Sel("textStorage"));
            var handle = NativeLibrary.Load(AppKit);
            try
            {
                var key = Marshal.ReadIntPtr(NativeLibrary.GetExport(handle,
                    "NSForegroundColorAttributeName"));
                var native = ObjC.Send(storage, ObjC.Sel("attribute:atIndex:effectiveRange:"),
                    key, (nint)index, 0);
                native = ObjC.Send(native, ObjC.Sel("colorUsingColorSpace:"),
                    ObjC.Send(ObjC.Class("NSColorSpace"), ObjC.Sel("sRGBColorSpace")));
                if (native == 0) throw new InvalidOperationException("Preview color is unavailable.");
                static byte Channel(nint color, string selector) =>
                    checked((byte)Math.Clamp((int)Math.Round(
                        MacOnScreenCanvasNative.SendDouble(color, ObjC.Sel(selector)) * 255),
                        0, 255));
                return new ThemeColor(Channel(native, "redComponent"),
                    Channel(native, "greenComponent"), Channel(native, "blueComponent"));
            }
            finally { NativeLibrary.Free(handle); }
        }

        private void Fail(string check)
        {
            _check = check;
            Console.Error.WriteLine($"Mac composition-theme stage {_stage} check {_check} failed.");
            Finish(false);
        }

        private void Finish(bool success)
        {
            _done = true;
            Succeeded = success;
            if (_shell.ProbeHasMarkedText)
            {
                try
                {
                    ObjC.Send(_shell.ProbeEditorView, ObjC.Sel("unmarkText"));
                    _shell.CommitPendingText();
                }
                catch (Exception) { /* The outer runner still bounds a failed native probe. */ }
            }
            _shell.ProbeApproveDiscardOnce();
            _shell.Close();
        }

        internal void WriteResult(bool inputUnchanged)
        {
            using var stream = File.Create(Path.Combine(_output,
                $"composition-theme-{_mode}.json"));
            using var writer = new Utf8JsonWriter(stream);
            writer.WriteStartObject();
            writer.WriteString("Mode", _mode);
            writer.WriteBoolean("Succeeded", Succeeded);
            writer.WriteNumber("LastStage", _stage);
            writer.WriteBoolean("PreeditIsolated", _preeditIsolated);
            writer.WriteBoolean("PolicyDeferred", _policyDeferred);
            writer.WriteBoolean("CancelIsolated", _cancelIsolated);
            writer.WriteBoolean("CancelVersionUnchanged", _cancelVersionUnchanged);
            writer.WriteBoolean("CancelSelectionPreserved", _cancelSelectionPreserved);
            writer.WriteBoolean("UndoRestored", _undoRestored);
            writer.WriteBoolean("RedoRestored", _redoRestored);
            writer.WriteNumber("UndoSelectionAnchor", _undoSelection.Anchor);
            writer.WriteNumber("UndoSelectionActive", _undoSelection.Active);
            writer.WriteNumber("RedoSelectionAnchor", _redoSelection.Anchor);
            writer.WriteNumber("RedoSelectionActive", _redoSelection.Active);
            writer.WriteBoolean("InputUnchanged", inputUnchanged);
            writer.WriteNumber("AppearanceCallbacks", _appearanceCallbacks);
            writer.WriteNumber("CompositionSettledCallbacks", _settledCallbacks);
            writer.WriteNumber("InsertionOffset", _insertionOffset);
            writer.WriteString("SelectionSpace", _mode == "canvas"
                ? "global-source" : "native-text-view");
            writer.WriteString("SourceHashEncoding", "UTF-16LE-no-BOM");
            writer.WriteString("Scope", "synthetic-appkit-marked-text-window-appearance");
            writer.WriteStartArray("Phases");
            foreach (var phase in _phases)
            {
                writer.WriteStartObject();
                writer.WriteString("Step", phase.Step);
                writer.WriteBoolean("OsPrefersDark", phase.OsPrefersDark);
                writer.WriteString("AppliedThemeId", phase.AppliedThemeId);
                writer.WriteBoolean("HasMarkedText", phase.HasMarkedText);
                writer.WriteNumber("AppearanceCallbacks", phase.AppearanceCallbacks);
                writer.WriteNumber("CompositionSettledCallbacks", phase.CompositionSettledCallbacks);
                writer.WriteNumber("Generation", phase.Generation);
                writer.WriteNumber("Version", phase.Version);
                writer.WriteNumber("SelectionAnchor", phase.SelectionAnchor);
                writer.WriteNumber("SelectionActive", phase.SelectionActive);
                writer.WriteString("SourceSha256", phase.SourceSha256);
                writer.WriteString("PreviewHeadingRgb", phase.PreviewHeadingRgb);
                writer.WriteString("Image", phase.Image);
                writer.WriteString("ImageSha256", phase.ImageSha256);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }

    /// <summary>Content-free AppKit/source-state observation at one marked-text phase.</summary>
    private sealed record Phase(string Step, bool OsPrefersDark, string AppliedThemeId,
        bool HasMarkedText, int AppearanceCallbacks, int CompositionSettledCallbacks,
        long Generation, long Version, int SelectionAnchor, int SelectionActive,
        string SourceSha256, string PreviewHeadingRgb, string Image,
        string ImageSha256);
}
