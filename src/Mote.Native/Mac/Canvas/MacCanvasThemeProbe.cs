using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using Mote.Configuration;
using Mote.Themes;

namespace Mote.Native.Mac.Canvas;

/// <summary>
/// Published-Mach-O probe for process-local AppKit effective-appearance changes.
/// It observes real view callbacks and native attributed text; it is not a test
/// of global macOS settings, VoiceOver, or a physical input method.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacCanvasThemeProbe
{
    private const string Heading = "MOTE_THEME_HEADING";
    private const string Body = "MOTE_THEME_BODY";
    private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";

    /// <summary>
    /// Opens one direct .temp Markdown fixture, switches the window DarkAqua →
    /// Aqua → DarkAqua, and writes bounded PNG/JSON observations to one empty
    /// direct .cache child. No user configuration or input file is modified.
    /// </summary>
    internal static int Run(string input, string outputDirectory, string mode)
    {
        try
        {
            if (mode is not ("default" or "canvas")) return 2;
            var path = Fixture(input);
            var output = Output(outputDirectory);
            var before = SHA256.HashData(File.ReadAllBytes(path));
            var temp = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".temp"));
            var config = MoteConfigLoader.Load(new MoteConfigLoadOptions
            {
                MoteHomeOverride = Path.Combine(temp,
                    $"theme-probe-config-{mode}-{Environment.ProcessId}"),
                UseEnvironmentOverride = false
            }) with { ThemeId = ThemePolicies.SystemId, TraceEnabled = false };
            var shell = new MacEditorShell(experimentalCanvas: mode == "canvas");
            var theme = ThemePolicies.Resolve(config.ThemeId, shell.PrefersDark);
            using var controller = new NativeEditorController(shell, config, theme, path);
            var workflow = new Workflow(shell, path, output, mode);
            shell.Shown += workflow.Start;
            controller.Run();
            var inputUnchanged = SHA256.HashData(File.ReadAllBytes(path))
                .AsSpan().SequenceEqual(before);
            workflow.WriteResult(inputUnchanged);
            return workflow.Succeeded && inputUnchanged ? 0 : 1;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"Mac theme probe failed: {error.GetType().Name}.");
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
            throw new ArgumentException("Theme probe requires a bounded direct .temp Markdown fixture.");
        var text = File.ReadAllText(full);
        if (!text.StartsWith($"# {Heading}", StringComparison.Ordinal) ||
            !text.Contains(Body, StringComparison.Ordinal))
            throw new ArgumentException("Theme probe fixture markers are absent.");
        return full;
    }

    private static string Output(string path)
    {
        var full = Path.GetFullPath(path);
        var cache = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".cache"));
        if (!string.Equals(Path.GetDirectoryName(full), cache, StringComparison.Ordinal) ||
            Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any())
            throw new ArgumentException("Theme probe output must be an empty .cache child.");
        Directory.CreateDirectory(full);
        if (new DirectoryInfo(full).LinkTarget is not null)
            throw new ArgumentException("Theme probe output cannot be a symlink.");
        return full;
    }

    private sealed class Workflow
    {
        private readonly MacEditorShell _shell;
        private readonly string _input;
        private readonly string _output;
        private readonly string _mode;
        private readonly DateTime _deadline = DateTime.UtcNow.AddSeconds(90);
        private readonly List<ThemeState> _states = [];
        private readonly byte[] _sourceHash;
        private int _stage;
        private int _callbackCount;
        private int _lastCallbackCount;
        private long _baseVersion;
        private long _editedVersion;
        private long _undoVersion;
        private int _insertionOffset;
        private NativeDocumentStamp _editedStamp;
        private (int Anchor, int Active) _selection;
        private string _initialSource = string.Empty;
        private string _expectedEditedSource = string.Empty;
        private string _editedSource = string.Empty;
        private bool _done;
        private bool _undoRestored;
        private bool _redoRestored;
        private string _check = "startup";

        internal Workflow(MacEditorShell shell, string input, string output, string mode)
        {
            _shell = shell;
            _input = input;
            _output = output;
            _mode = mode;
            _sourceHash = SHA256.HashData(File.ReadAllBytes(input));
            shell.AppearanceChanged += () => ++_callbackCount;
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
                if (_stage == 0)
                    Console.Error.WriteLine("Mac theme readiness: " +
                        $"stamp={CurrentStampNullable() is not null};" +
                        $"analysis={_shell.ProbeAnalysis is not null};" +
                        $"analysis-ready={AnalysisReady()};" +
                        $"heading={CurrentSource().Contains(Heading, StringComparison.Ordinal)};" +
                        $"body={CurrentSource().Contains(Body, StringComparison.Ordinal)};");
                else if (_stage == 1)
                    Console.Error.WriteLine("Mac theme dark readiness: " +
                        $"prefers-dark={_shell.PrefersDark};" +
                        $"policy-dark={_shell.ProbeThemeId == ThemePolicies.DarkId};" +
                        $"analysis-ready={AnalysisReady()};" +
                        $"stamp={CurrentStampNullable() is not null};" +
                        $"analysis={_shell.ProbeAnalysis is not null};" +
                        $"heading-spans={_shell.ProbeAnalysis?.PreviewSpans?.Count(static span => span.Kind == "heading") ?? 0};");
                else if (_stage == 2)
                    Console.Error.WriteLine("Mac theme edit readiness: " +
                        $"version-advanced={CurrentVersion() > _baseVersion};" +
                        $"analysis-ready={AnalysisReady()};" +
                        $"exact-edit={CurrentSource() == _expectedEditedSource};" +
                        $"source-ends-z={CurrentSource().EndsWith('Z')};" +
                        $"input-start={_shell.ProbeCanvasInputStart};" +
                        $"input-length={_shell.ProbeNativeText.Length};" +
                        $"insertion-offset={_insertionOffset};");
                Fail("deadline");
                return;
            }
            try
            {
                switch (_stage)
                {
                    case 0 when Ready():
                        _check = "initial-dark";
                        _lastCallbackCount = _callbackCount;
                        _shell.ProbeSetWindowAppearance(true);
                        _stage = 1;
                        break;
                    case 1 when _shell.PrefersDark &&
                        _shell.ProbeThemeId == ThemePolicies.DarkId && AnalysisReady():
                        _check = "native-edit";
                        _baseVersion = CurrentVersion();
                        _initialSource = CurrentSource();
                        _insertionOffset = _mode == "canvas"
                            ? checked(_shell.ProbeCanvasInputStart + _shell.ProbeNativeText.Length)
                            : _initialSource.Length;
                        if (_insertionOffset < 0 || _insertionOffset > _initialSource.Length)
                            throw new InvalidOperationException("Theme input offset is outside source.");
                        _expectedEditedSource = _initialSource.Insert(_insertionOffset, "Z");
                        _shell.ProbeInsertAtEnd("Z");
                        _stage = 2;
                        break;
                    case 2 when CurrentVersion() > _baseVersion && AnalysisReady() &&
                        CurrentSource() == _expectedEditedSource:
                        _check = "dark-before";
                        _editedVersion = CurrentVersion();
                        _editedStamp = CurrentStamp();
                        _selection = CurrentSelection();
                        _editedSource = CurrentSource();
                        Capture("dark-before", ThemePolicies.DarkId);
                        _lastCallbackCount = _callbackCount;
                        _shell.ProbeSetWindowAppearance(false);
                        _stage = 3;
                        break;
                    case 3 when !_shell.PrefersDark &&
                        _shell.ProbeThemeId == ThemePolicies.LightId &&
                        _callbackCount > _lastCallbackCount:
                        _check = "light";
                        CheckUnchanged();
                        Capture("light", ThemePolicies.LightId);
                        _lastCallbackCount = _callbackCount;
                        _shell.ProbeSetWindowAppearance(true);
                        _stage = 4;
                        break;
                    case 4 when _shell.PrefersDark &&
                        _shell.ProbeThemeId == ThemePolicies.DarkId &&
                        _callbackCount > _lastCallbackCount:
                        _check = "dark-after";
                        CheckUnchanged();
                        Capture("dark-after", ThemePolicies.DarkId);
                        if (_states[0].ImageSha256 == _states[1].ImageSha256 ||
                            _states[1].ImageSha256 == _states[2].ImageSha256)
                            throw new InvalidOperationException("AppKit raster did not change with theme.");
                        _shell.ProbeInvokeMenu("moteUndo:");
                        _stage = 5;
                        break;
                    case 5 when CurrentVersion() > _editedVersion &&
                        CurrentSource() == _initialSource:
                        _check = "undo";
                        _undoVersion = CurrentVersion();
                        _undoRestored = true;
                        _shell.ProbeInvokeMenu("moteRedo:");
                        _stage = 6;
                        break;
                    case 6 when CurrentVersion() > _undoVersion &&
                        CurrentSource() == _editedSource:
                        _check = "redo";
                        _redoRestored = true;
                        if (!SHA256.HashData(File.ReadAllBytes(_input)).AsSpan()
                            .SequenceEqual(_sourceHash))
                            throw new InvalidOperationException("Theme probe modified its input file.");
                        Finish(true);
                        return;
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                Console.Error.WriteLine($"Mac theme stage {_stage} check {_check}: " +
                    $"{error.GetType().Name}.");
                Finish(false);
                return;
            }
            Schedule();
        }

        private bool Ready() => CurrentStampNullable() is not null &&
            _shell.ProbeAnalysis is not null && AnalysisReady() &&
            CurrentSource().Contains(Heading, StringComparison.Ordinal) &&
            CurrentSource().Contains(Body, StringComparison.Ordinal);

        private bool AnalysisReady() => _shell.ProbeAnalysis is { } analysis &&
            analysis.Stamp == CurrentStamp() &&
            analysis.PreviewSpans?.Any(static span => span.Kind == "heading") == true &&
            (_mode == "canvas" ||
                analysis.Tokens.Any(static token => token.Kind == "heading"));

        private NativeDocumentStamp? CurrentStampNullable() => _mode == "canvas"
            ? _shell.ProbeCanvasStamp : _shell.ProbeDocumentStamp;

        private NativeDocumentStamp CurrentStamp() => CurrentStampNullable() ?? default;

        private long CurrentVersion() => _mode == "canvas"
            ? _shell.ProbeCanvasVersion : CurrentStamp().Version;

        private string CurrentSource() => _mode == "canvas"
            ? _shell.ProbeCanvasSnapshot?.GetText() ?? string.Empty
            : _shell.ProbeProjectedText;

        private (int Anchor, int Active) CurrentSelection()
        {
            if (_mode == "canvas") return _shell.ProbeCanvasSelection;
            var selected = ObjC.SendRange(_shell.ProbeEditorView, ObjC.Sel("selectedRange"));
            return (checked((int)selected.Location),
                checked((int)(selected.Location + selected.Length)));
        }

        private void CheckUnchanged()
        {
            if (CurrentVersion() != _editedVersion || CurrentStamp() != _editedStamp ||
                CurrentSelection() != _selection || CurrentSource() != _editedSource ||
                !AnalysisReady())
                throw new InvalidOperationException("Theme switch changed source state.");
        }

        private void Capture(string step, string expectedId)
        {
            var analysis = _shell.ProbeAnalysis ??
                throw new InvalidOperationException("Theme analysis is absent.");
            var policy = ThemePolicies.Get(expectedId);
            var heading = analysis.PreviewSpans!.First(static span => span.Kind == "heading");
            _check = $"{step}-preview-read";
            var preview = NativeColor(_shell.ProbePreviewView, heading.Start);
            _check = $"{step}-preview-color";
            RequireColor(preview, policy.Palette.Accent, _check);
            ThemeColor? editor = null;
            ThemeColor? marker = null;
            if (_mode == "default")
            {
                var token = analysis.Tokens.First(static token => token.Kind == "heading");
                if (token.Span.Length < 3)
                    throw new InvalidOperationException("Theme heading has no interior source glyph.");
                _check = $"{step}-editor-marker-read";
                marker = NativeColor(_shell.ProbeEditorView, token.Span.Start);
                _check = $"{step}-editor-interior-read";
                editor = NativeColor(_shell.ProbeEditorView,
                    token.Span.Start + token.Span.Length / 2);
                _check = $"{step}-editor-color";
                if (!ColorsMatch(editor.Value, policy.SemanticColor("heading")))
                    LogHeadingSamples(token.Span.Start, token.Span.Length);
                RequireColor(editor.Value, policy.SemanticColor("heading"), _check);
            }
            _check = $"{step}-raster";
            var imageName = $"theme-{_mode}-{step}.png";
            var view = ObjC.Send(_shell.ProbeWindow, ObjC.Sel("contentView"));
            ObjC.Send(view, ObjC.Sel("displayIfNeeded"));
            var rect = MacOnScreenCanvasNative.GetRect(view, ObjC.Sel("bounds"));
            var bitmap = ObjC.Send(view,
                ObjC.Sel("bitmapImageRepForCachingDisplayInRect:"), rect);
            if (bitmap == 0) throw new IOException("AppKit did not allocate a theme raster.");
            MacOnScreenCanvasNative.Send(view,
                ObjC.Sel("cacheDisplayInRect:toBitmapImageRep:"), rect, bitmap);
            var png = ObjC.Send(bitmap, ObjC.Sel("representationUsingType:properties:"),
                4, ObjC.Send(ObjC.Class("NSDictionary"), ObjC.Sel("dictionary")));
            var imagePath = Path.Combine(_output, imageName);
            if (png == 0 || ObjC.Send(png, ObjC.Sel("writeToFile:atomically:"),
                ObjC.String(imagePath), (byte)1) == 0)
                throw new IOException("AppKit did not write a theme raster.");
            var bytes = File.ReadAllBytes(imagePath);
            if (bytes.Length < 8 || !bytes.AsSpan(0, 8).SequenceEqual(
                new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                throw new IOException("Theme raster is not PNG.");
            _states.Add(new ThemeState(step, expectedId, _callbackCount,
                CurrentStamp().Generation, CurrentVersion(), _selection.Anchor,
                _selection.Active, preview.ToHex(), editor?.ToHex(), marker?.ToHex(), imageName,
                Convert.ToHexString(SHA256.HashData(bytes))));
        }

        private static ThemeColor NativeColor(nint textView, int index)
        {
            if (textView == 0 || index < 0) throw new InvalidOperationException("Theme text view is unavailable.");
            var storage = ObjC.Send(textView, ObjC.Sel("textStorage"));
            var handle = NativeLibrary.Load(AppKit);
            try
            {
                var symbol = NativeLibrary.GetExport(handle, "NSForegroundColorAttributeName");
                var key = Marshal.ReadIntPtr(symbol);
                var color = ObjC.Send(storage, ObjC.Sel("attribute:atIndex:effectiveRange:"),
                    key, (nint)index, 0);
                if (color == 0) throw new InvalidOperationException("Theme text foreground is absent.");
                color = ObjC.Send(color, ObjC.Sel("colorUsingColorSpace:"),
                    ObjC.Send(ObjC.Class("NSColorSpace"), ObjC.Sel("sRGBColorSpace")));
                if (color == 0) throw new InvalidOperationException("Theme text color cannot convert to sRGB.");
                return new ThemeColor(Channel(color, "redComponent"),
                    Channel(color, "greenComponent"), Channel(color, "blueComponent"));
            }
            finally { NativeLibrary.Free(handle); }
        }

        private static byte Channel(nint color, string selector) => checked((byte)Math.Clamp(
            (int)Math.Round(MacOnScreenCanvasNative.SendDouble(color, ObjC.Sel(selector)) * 255),
            0, 255));

        private static void RequireColor(ThemeColor actual, ThemeColor expected,
            string check)
        {
            if (!ColorsMatch(actual, expected))
            {
                Console.Error.WriteLine($"Mac theme color {check}: " +
                    $"actual={actual.ToHex()} expected={expected.ToHex()}.");
                throw new InvalidOperationException("Native attributed foreground mismatches policy.");
            }
        }

        private static bool ColorsMatch(ThemeColor actual, ThemeColor expected) =>
            Math.Abs(actual.Red - expected.Red) <= 1 &&
            Math.Abs(actual.Green - expected.Green) <= 1 &&
            Math.Abs(actual.Blue - expected.Blue) <= 1;

        private void LogHeadingSamples(int start, int length)
        {
            Console.Error.WriteLine($"Mac theme heading span: start={start};length={length};");
            if (length <= 0) return;
            foreach (var (name, offset) in new[]
            {
                ("start", start),
                ("middle", start + length / 2),
                ("end", start + length - 1)
            })
            {
                try
                {
                    var color = NativeColor(_shell.ProbeEditorView, offset);
                    Console.Error.WriteLine($"Mac theme heading {name}-rgb={color.ToHex()};");
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    Console.Error.WriteLine($"Mac theme heading {name}-error={error.GetType().Name};");
                }
            }
        }

        private void Fail(string check)
        {
            _check = check;
            Console.Error.WriteLine($"Mac theme stage {_stage} check {_check} failed.");
            Finish(false);
        }

        private void Finish(bool success)
        {
            _done = true;
            Succeeded = success;
            _shell.ProbeApproveDiscardOnce();
            _shell.Close();
        }

        internal void WriteResult(bool inputUnchanged)
        {
            var result = new ThemeResult(_mode, Succeeded, _stage,
                _callbackCount, _undoRestored, _redoRestored,
                inputUnchanged, _states, "window-local-appkit-appearance");
            using var stream = File.Create(Path.Combine(_output, $"theme-{_mode}.json"));
            using var writer = new Utf8JsonWriter(stream);
            writer.WriteStartObject();
            writer.WriteString("Mode", result.Mode);
            writer.WriteBoolean("Succeeded", result.Succeeded);
            writer.WriteNumber("LastStage", result.LastStage);
            writer.WriteNumber("CallbackCount", result.CallbackCount);
            writer.WriteBoolean("UndoRestored", result.UndoRestored);
            writer.WriteBoolean("RedoRestored", result.RedoRestored);
            writer.WriteBoolean("InputUnchanged", result.InputUnchanged);
            writer.WriteString("AppearanceScope", result.AppearanceScope);
            writer.WriteStartArray("States");
            foreach (var state in result.States)
            {
                writer.WriteStartObject();
                writer.WriteString("Step", state.Step);
                writer.WriteString("ThemeId", state.ThemeId);
                writer.WriteNumber("CallbackCount", state.CallbackCount);
                writer.WriteNumber("Generation", state.Generation);
                writer.WriteNumber("Version", state.Version);
                writer.WriteNumber("SelectionAnchor", state.SelectionAnchor);
                writer.WriteNumber("SelectionActive", state.SelectionActive);
                writer.WriteString("PreviewHeadingRgb", state.PreviewHeadingRgb);
                if (state.EditorHeadingRgb is null)
                    writer.WriteNull("EditorHeadingRgb");
                else writer.WriteString("EditorHeadingRgb", state.EditorHeadingRgb);
                if (state.EditorMarkerRgb is null)
                    writer.WriteNull("EditorMarkerRgb");
                else writer.WriteString("EditorMarkerRgb", state.EditorMarkerRgb);
                writer.WriteString("Image", state.Image);
                writer.WriteString("ImageSha256", state.ImageSha256);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }

    /// <summary>Content-free native paint and source-state observation at one theme step.</summary>
    private sealed record ThemeState(string Step, string ThemeId, int CallbackCount,
        long Generation, long Version, int SelectionAnchor, int SelectionActive,
        string PreviewHeadingRgb, string? EditorHeadingRgb, string? EditorMarkerRgb,
        string Image,
        string ImageSha256);

    /// <summary>Bounded JSON result for the two-RID published-binary harness.</summary>
    private sealed record ThemeResult(string Mode, bool Succeeded, int LastStage,
        int CallbackCount, bool UndoRestored, bool RedoRestored,
        bool InputUnchanged, IReadOnlyList<ThemeState> States,
        string AppearanceScope);
}
