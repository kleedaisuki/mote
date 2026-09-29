using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Mote.Configuration;
using Mote.Engine;
using Mote.Themes;

namespace Mote.Native.Mac.Canvas;

/// <summary>
/// Published-Mach-O AppKit diagnostic for bounded horizontal canvas geometry.
/// It drives the real shell and NSView but its source jump/pan calls are not
/// evidence of an external trackpad, physical pointer, IME, or VoiceOver.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacCanvasHorizontalProbe
{
    private const string ShortMarker = "MOTE_HORIZ_3000";
    private const string RemoteMarker = "MOTE_HORIZ_REMOTE";
    private const int ShortOffset = 3000;
    private const int RemoteOffset = 40 * 1024 * 1024;

    /// <summary>
    /// Opens two ASCII single-line fixtures under .temp, captures only bounded
    /// native view rasters under .cache, and verifies both source files remain
    /// byte-for-byte unchanged.
    /// </summary>
    internal static int Run(string shortPath, string longPath, string outputDirectory)
    {
        Workflow? workflow = null;
        try
        {
            var shortFile = Fixture(shortPath, 16 * 1024, 128 * 1024,
                ShortOffset, ShortMarker);
            var longFile = Fixture(longPath, 50L * 1024 * 1024,
                64L * 1024 * 1024, RemoteOffset, RemoteMarker);
            var output = OutputDirectory(outputDirectory);
            var shortHash = Hash(shortFile);
            var longHash = Hash(longFile);
            var shell = new MacEditorShell(experimentalCanvas: true);
            var config = MoteConfigLoader.Load();
            var theme = ThemePolicies.Resolve(config.ThemeId, shell.PrefersDark);
            using var controller = new NativeEditorController(shell, config, theme, shortFile);
            workflow = new Workflow(shell, shortFile, longFile, output, theme.Id);
            shell.Shown += workflow.Start;
            controller.Run();
            workflow.WriteMetrics();
            return workflow.Succeeded && Hash(shortFile).AsSpan().SequenceEqual(shortHash) &&
                Hash(longFile).AsSpan().SequenceEqual(longHash) ? 0 : 1;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"Mac horizontal probe failed: {error.GetType().Name}.");
            return 1;
        }
    }

    private static string Fixture(string path, long minLength, long maxLength,
        int markerOffset, string marker)
    {
        var full = Path.GetFullPath(path);
        var temp = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".temp"));
        var file = new FileInfo(full);
        if (!string.Equals(Path.GetDirectoryName(full), temp, StringComparison.Ordinal) ||
            !file.Exists || file.LinkTarget is not null || file.Length < minLength ||
            file.Length > maxLength || markerOffset + marker.Length > file.Length)
            throw new ArgumentException("Horizontal probe fixture path or length is invalid.");
        using var stream = File.OpenRead(full);
        stream.Position = markerOffset;
        var bytes = new byte[marker.Length];
        stream.ReadExactly(bytes);
        if (!bytes.AsSpan().SequenceEqual(Encoding.ASCII.GetBytes(marker)))
            throw new ArgumentException("Horizontal probe marker is absent.");
        return full;
    }

    private static string OutputDirectory(string path)
    {
        var full = Path.GetFullPath(path);
        var cache = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".cache"));
        if (!string.Equals(Path.GetDirectoryName(full), cache, StringComparison.Ordinal) ||
            Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any())
            throw new ArgumentException("Horizontal probe output must be an empty .cache child.");
        Directory.CreateDirectory(full);
        if (new DirectoryInfo(full).LinkTarget is not null)
            throw new ArgumentException("Horizontal probe output must not be a symlink.");
        return full;
    }

    private static byte[] Hash(string path)
    {
        using var file = File.OpenRead(path);
        return SHA256.HashData(file);
    }

    private sealed class Workflow
    {
        private readonly MacEditorShell _shell;
        private readonly string _shortPath;
        private readonly string _longPath;
        private readonly string _output;
        private readonly DateTime _deadline = DateTime.UtcNow.AddSeconds(120);
        private readonly List<string> _metrics = [];
        private readonly Dictionary<string, string> _imageHashes = [];
        private int _stage;
        private string _check = "startup";
        private bool _done;
        private double _beforeX;
        private double _normalCanvasHeight;
        private double _normalCanvasWidth;
        private int _shortHit;
        private int _remoteHit;

        internal Workflow(MacEditorShell shell, string shortPath, string longPath,
            string output, string themeId)
        {
            _shell = shell;
            _shortPath = shortPath;
            _longPath = longPath;
            _output = output;
            _metrics.Add($"theme-id={themeId}");
            _metrics.Add($"os={Environment.OSVersion.Version}");
            if (Environment.ProcessPath is { } executable)
                _metrics.Add($"executable-sha256={Convert.ToHexString(Hash(executable))}");
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
            if (DateTime.UtcNow >= _deadline) { Finish(false); return; }
            try
            {
                switch (_stage)
                {
                    case 0 when _shell.ProbeTitle.Contains(Path.GetFileName(_shortPath),
                        StringComparison.Ordinal) &&
                        _shell.ProbeCanvasSnapshot is { LineCount: 1 } shortSource &&
                        shortSource.Length >= 16 * 1024 &&
                        _shell.ProbeCanvasFrame is { } shortFrame:
                        if (shortSource.GetText(ShortOffset, ShortMarker.Length) != ShortMarker)
                            throw new InvalidOperationException("Short source marker changed.");
                        CheckBounded(shortFrame);
                        _metrics.Add($"canvas-width={CanvasWidth().ToString("F3", CultureInfo.InvariantCulture)}");
                        var scale = MacOnScreenCanvasNative.SendDouble(_shell.ProbeWindow,
                            ObjC.Sel("backingScaleFactor"));
                        _metrics.Add($"backing-scale={scale.ToString("F3", CultureInfo.InvariantCulture)}");
                        var focus = _shell.ProbeAppKitFocus;
                        _metrics.Add($"app-active={focus.AppActive}");
                        _metrics.Add($"activation-policy={focus.ActivationPolicy}");
                        _metrics.Add($"window-key={focus.WindowKey}");
                        _metrics.Add($"window-visible={focus.WindowVisible}");
                        _metrics.Add($"editor-first-responder={focus.EditorFirstResponder}");
                        _metrics.Add($"ax-focus={_shell.ProbeCanvasAxFocusTrace}");
                        var before = _shell.GetCanvasCaretGeometry(shortFrame, ShortOffset);
                        if (before is not { IsVisible: false } ||
                            before.Value.X <= CanvasWidth() + 1)
                            throw new InvalidOperationException("Short caret was not physically offscreen.");
                        _beforeX = before.Value.X;
                        Capture("short-before.png");
                        _shell.ProbeCanvasPanHorizontal(_beforeX - CanvasWidth() * 0.5);
                        _stage = 1;
                        break;
                    case 1 when _shell.ProbeCanvasFrame is { } panned &&
                        panned.Horizontal.SourceBoundary > 0:
                        _check = "S1-bounded";
                        CheckBounded(panned);
                        _check = "S1-source-caret";
                        var after = _shell.GetCanvasCaretGeometry(panned, ShortOffset);
                        if (after is not { IsVisible: true })
                            throw new InvalidOperationException("Short caret was not visibly reanchored.");
                        _check = "S1-ribbon-caret-focus";
                        var host = _shell.ProbeCanvasHostGeometry(0);
                        if (host is not { Aligned: true, Hidden: false } ||
                            !_shell.ProbeCanvasInputFocused ||
                            host.Value.NativeX - host.Value.ClipX < 0 ||
                            host.Value.NativeX - host.Value.ClipX > CanvasWidth() - 148)
                            throw new InvalidOperationException("Visible input ribbon lost its caret or focus.");
                        // Raster comparison must isolate panning from the later
                        // selection/copy test; selection alone cannot prove scroll.
                        _check = "S1-before-after-raster";
                        Capture("short-after.png");
                        RequireDifferent("short-before.png", "short-after.png");
                        _check = "S1-source-hit";
                        _shortHit = _shell.ProbeCanvasHitSource(after.Value.X + 1,
                            after.Value.Y + after.Value.Height * 0.5);
                        if (_shortHit != ShortOffset)
                            throw new InvalidOperationException("Short pointer source mapping failed.");
                        _check = "S1-global-selection";
                        _shell.ProbeCanvasSelectGlobal(_shortHit, _shortHit + ShortMarker.Length);
                        if (_shell.ProbeCanvasSelection !=
                            (ShortOffset, ShortOffset + ShortMarker.Length))
                            throw new InvalidOperationException("Short global selection failed.");
                        _check = "S1-global-copy";
                        _metrics.Add($"short-before-x={_beforeX.ToString("F3", CultureInfo.InvariantCulture)}");
                        _metrics.Add($"short-after-x={after.Value.X.ToString("F3", CultureInfo.InvariantCulture)}");
                        _metrics.Add($"host-native-x={host.Value.NativeX.ToString("F3", CultureInfo.InvariantCulture)}");
                        _metrics.Add($"host-clip-x={host.Value.ClipX.ToString("F3", CultureInfo.InvariantCulture)}");
                        _metrics.Add($"host-ribbon-caret-x={(host.Value.NativeX - host.Value.ClipX).ToString("F3", CultureInfo.InvariantCulture)}");
                        var board = ObjC.Send(ObjC.Class("NSPasteboard"),
                            ObjC.Sel("generalPasteboard"));
                        ObjC.Send(board, ObjC.Sel("clearContents"));
                        _shell.ProbeInvokeMenu("moteCopy:");
                        _stage = 11;
                        break;
                    case 11:
                        _check = "S11-await-async-copy";
                        var copyBoard = ObjC.Send(ObjC.Class("NSPasteboard"),
                            ObjC.Sel("generalPasteboard"));
                        var copied = ObjC.Send(copyBoard, ObjC.Sel("stringForType:"),
                            ObjC.String("public.utf8-plain-text"));
                        if (ObjC.ManagedString(copied) != ShortMarker) break;
                        _shell.ProbePickOpen(_longPath);
                        _shell.ProbeInvokeMenu("moteOpen:");
                        _stage = 2;
                        break;
                    case 2 when _shell.ProbeTitle.Contains(Path.GetFileName(_longPath),
                        StringComparison.Ordinal) &&
                        _shell.ProbeCanvasSnapshot is { LineCount: 1 } longSource &&
                        longSource.Length >= 50 * 1024 * 1024:
                        if (longSource.GetText(RemoteOffset, RemoteMarker.Length) != RemoteMarker)
                            throw new InvalidOperationException("Remote source marker changed.");
                        _shell.ProbeCanvasHorizontalAnchor(RemoteOffset);
                        _stage = 3;
                        break;
                    case 3 when _shell.ProbeCanvasFrame is { } remoteFrame &&
                        remoteFrame.Horizontal.SourceBoundary == RemoteOffset:
                        CheckBounded(remoteFrame);
                        var remote = _shell.GetCanvasCaretGeometry(remoteFrame, RemoteOffset);
                        if (remote is not { IsVisible: true })
                            throw new InvalidOperationException("Remote source marker is not visible.");
                        if (_shell.ProbeCanvasHostGeometry(0) is not
                            { Aligned: true, Hidden: false } ||
                            !_shell.ProbeCanvasInputFocused)
                            throw new InvalidOperationException("Remote pan hid the focused ribbon.");
                        _remoteHit = _shell.ProbeCanvasHitSource(remote.Value.X + 1,
                            remote.Value.Y + remote.Value.Height * 0.5);
                        if (_remoteHit != RemoteOffset)
                            throw new InvalidOperationException("Remote pointer source mapping failed.");
                        _metrics.Add($"remote-left={remoteFrame.RowWindows[0].LeftEdgeSourceBoundary}");
                        _metrics.Add($"remote-x={remote.Value.X.ToString("F3", CultureInfo.InvariantCulture)}");
                        var remoteFocus = _shell.ProbeAppKitFocus;
                        _metrics.Add($"remote-app-active={remoteFocus.AppActive}");
                        _metrics.Add($"remote-window-key={remoteFocus.WindowKey}");
                        _metrics.Add($"remote-editor-first-responder={remoteFocus.EditorFirstResponder}");
                        _metrics.Add($"remote-ax-focus={_shell.ProbeCanvasAxFocusTrace}");
                        Capture("remote-before.png");
                        _shell.ProbeCanvasPanHorizontal(96);
                        _stage = 4;
                        break;
                    case 4 when _shell.ProbeCanvasFrame is { } finalFrame &&
                        finalFrame.Horizontal.SourceBoundary > RemoteOffset:
                        CheckBounded(finalFrame);
                        if (_shell.ProbeCanvasHostGeometry(0) is not
                            { Aligned: true, Hidden: false } ||
                            !_shell.ProbeCanvasInputFocused)
                            throw new InvalidOperationException("Horizontal wheel lost ribbon focus.");
                        _metrics.Add($"remote-after-left={finalFrame.RowWindows[0].LeftEdgeSourceBoundary}");
                        _metrics.Add($"short-hit={_shortHit}");
                        _metrics.Add($"remote-hit={_remoteHit}");
                        Capture("remote-after.png");
                        RequireDifferent("remote-before.png", "remote-after.png");
                        _shell.ProbeInvokeMenu("moteNew:");
                        _stage = 5;
                        break;
                    case 5 when _shell.ProbeCanvasSnapshot is { Length: 0 } empty &&
                        _shell.ProbeNativeText.Length == 0 &&
                        _shell.ProbeCanvasVersion == empty.Version:
                        _shell.ProbeInsertAtEnd("abc");
                        _stage = 6;
                        break;
                    case 6 when _shell.ProbeCanvasSnapshot?.GetText() == "abc" &&
                        _shell.ProbeCanvasFrame is { } smallFrame:
                        CheckBounded(smallFrame);
                        var smallHost = _shell.ProbeCanvasHostGeometry(0);
                        var smallCaret = _shell.GetCanvasCaretGeometry(smallFrame, 0);
                        if (smallHost is not { Aligned: true, Hidden: false } ||
                            smallCaret is not { IsVisible: true } ||
                            !_shell.ProbeCanvasInputFocused ||
                            smallHost.Value.NativeX - smallHost.Value.ClipX < 0 ||
                            smallHost.Value.NativeX - smallHost.Value.ClipX > CanvasWidth() - 148)
                            throw new InvalidOperationException("Short input ribbon is not usable.");
                        _metrics.Add($"small-host-native-x={smallHost.Value.NativeX.ToString("F3", CultureInfo.InvariantCulture)}");
                        _metrics.Add($"small-host-clip-x={smallHost.Value.ClipX.ToString("F3", CultureInfo.InvariantCulture)}");
                        _metrics.Add($"small-caret-x={smallCaret.Value.X.ToString("F3", CultureInfo.InvariantCulture)}");
                        ObjC.Send(_shell.ProbeWindow, ObjC.Sel("setContentSize:"),
                            new ObjC.Size(100, 50));
                        _stage = 7;
                        break;
                    case 7 when _shell.ProbeCanvasFrame is { } tinyFrame:
                        ObjC.Send(_shell.ProbeCanvasView, ObjC.Sel("displayIfNeeded"));
                        var bounds = MacOnScreenCanvasNative.GetRect(_shell.ProbeCanvasView,
                            ObjC.Sel("bounds"));
                        if (bounds.Size.Height <= 37 ||
                            _shell.ProbeCanvasHostGeometry(0) is not
                                { Aligned: true, Hidden: false } ||
                            !_shell.ProbeCanvasInputFocused ||
                            _shell.GetCanvasCaretGeometry(tinyFrame, 0) is not
                                { IsVisible: true })
                            throw new InvalidOperationException("Minimum window lost body or input.");
                        _metrics.Add($"minimum-canvas-height={bounds.Size.Height.ToString("F3", CultureInfo.InvariantCulture)}");
                        _metrics.Add($"minimum-body-height={(bounds.Size.Height - 36).ToString("F3", CultureInfo.InvariantCulture)}");
                        _normalCanvasHeight = bounds.Size.Height;
                        _normalCanvasWidth = bounds.Size.Width;
                        ObjC.Send(_shell.ProbeCanvasView, ObjC.Sel("setFrameSize:"),
                            new ObjC.Size(bounds.Size.Width, 36));
                        _shell.ProbeCanvasPublishBodyHeight();
                        _stage = 8;
                        break;
                    case 8 when _shell.ProbeCanvasFrame is { } zeroFrame &&
                        zeroFrame.RowWindows.Length == 0 &&
                        _shell.ProbeCanvasBodyRect.Size.Height == 0:
                        if (_shell.ProbeCanvasHostGeometry(0) is not
                                { Hidden: false } ||
                            !_shell.ProbeCanvasInputFocused ||
                            _shell.GetCanvasCaretGeometry(zeroFrame, 0) is not null)
                            throw new InvalidOperationException("Zero body advertised source or lost input.");
                        _metrics.Add("zero-body-visible-rows=0");
                        ObjC.Send(_shell.ProbeCanvasView, ObjC.Sel("setFrameSize:"),
                            new ObjC.Size(_normalCanvasWidth, _normalCanvasHeight));
                        _shell.ProbeCanvasPublishBodyHeight();
                        _stage = 9;
                        break;
                    case 9 when _shell.ProbeCanvasFrame is { } restoredFrame &&
                        restoredFrame.RowWindows.Length > 0 &&
                        _shell.ProbeCanvasBodyRect.Size.Height > 0:
                        if (_shell.ProbeCanvasHostGeometry(0) is not
                                { Aligned: true, Hidden: false } ||
                            !_shell.ProbeCanvasInputFocused ||
                            _shell.GetCanvasCaretGeometry(restoredFrame, 0) is not
                                { IsVisible: true })
                            throw new InvalidOperationException("Restored body lost source or ribbon.");
                        _metrics.Add($"restored-body-height={_shell.ProbeCanvasBodyRect.Size.Height.ToString("F3", CultureInfo.InvariantCulture)}");
                        Finish(true);
                        return;
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                Console.Error.WriteLine($"Mac horizontal stage {_stage} check {_check}: " +
                    $"{error.GetType().Name}.");
                Finish(false);
                return;
            }
            Schedule();
        }

        private double CanvasWidth()
        {
            var view = _shell.ProbeCanvasView;
            return view == 0 ? 0 : MacOnScreenCanvasNative.GetRect(view,
                ObjC.Sel("bounds")).Size.Width;
        }

        private void CheckBounded(Mote.Native.Viewport.CanvasFrame frame)
        {
            if (frame.RowWindows.Length == 0 || frame.Slices.Count != frame.RowWindows.Length ||
                frame.Slices.Any(slice => slice.SourceLength > 16 * 1024) ||
                _shell.ProbeNativeText.Length > 16 * 1024)
                throw new InvalidOperationException("Canvas source/input interval exceeded 16 Ki.");
            _metrics.Add($"max-slice={frame.Slices.Max(static slice => slice.SourceLength)}");
        }

        private void Capture(string name)
        {
            var view = _shell.ProbeCanvasView;
            if (view == 0) throw new InvalidOperationException("Canvas NSView is unavailable.");
            ObjC.Send(view, ObjC.Sel("displayIfNeeded"));
            var rect = MacOnScreenCanvasNative.GetRect(view, ObjC.Sel("bounds"));
            var bitmap = ObjC.Send(view,
                ObjC.Sel("bitmapImageRepForCachingDisplayInRect:"), rect);
            if (bitmap == 0) throw new IOException("AppKit could not allocate a canvas bitmap.");
            MacOnScreenCanvasNative.Send(view,
                ObjC.Sel("cacheDisplayInRect:toBitmapImageRep:"), rect, bitmap);
            var properties = ObjC.Send(ObjC.Class("NSDictionary"), ObjC.Sel("dictionary"));
            var png = ObjC.Send(bitmap, ObjC.Sel("representationUsingType:properties:"),
                4, properties);
            var path = Path.Combine(_output, name);
            if (png == 0 || ObjC.Send(png, ObjC.Sel("writeToFile:atomically:"),
                ObjC.String(path), (byte)1) == 0)
                throw new IOException("AppKit could not write canvas raster.");
            using (var file = File.OpenRead(path))
            {
                Span<byte> header = stackalloc byte[8];
                file.ReadExactly(header);
                if (!header.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                    throw new IOException("Canvas raster is not PNG.");
            }
            var hash = Convert.ToHexString(Hash(path));
            _imageHashes.Add(name, hash);
            _metrics.Add($"{name}-sha256={hash}");
        }

        private void RequireDifferent(string before, string after)
        {
            if (!_imageHashes.TryGetValue(before, out var first) ||
                !_imageHashes.TryGetValue(after, out var second) || first == second)
                throw new InvalidOperationException("Horizontal raster did not change.");
        }

        private void Finish(bool success)
        {
            _done = true;
            Succeeded = success;
            if (!success) Console.Error.WriteLine($"Mac horizontal stage {_stage} failed.");
            _shell.Close();
        }

        internal void WriteMetrics()
        {
            _metrics.Add($"probe-succeeded={Succeeded}");
            _metrics.Add($"last-stage={_stage}");
            File.WriteAllLines(Path.Combine(_output, "metrics.txt"), _metrics);
        }
    }
}
