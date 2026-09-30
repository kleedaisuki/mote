using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mote.Configuration;
using Mote.Themes;

namespace Mote.Native.Mac;

/// <summary>Isolated AppKit acceptance of startup colors and explicit settings reload.</summary>
/// <remarks>
/// Uses the production controller and process-local menu dispatch, not external input.
/// No configuration files, clipboard, input sources or system settings are accessed.
/// </remarks>
[SupportedOSPlatform("macos")]
internal static class MacThemeOverrideProbe
{
    private const string Runtime = "/usr/lib/libobjc.A.dylib";
    private const string Source = "theme reload 中 😀";

    /// <summary>Reads CGFloat results using the scalar ABI on both supported 64-bit Macs.</summary>
    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    private static extern double Scalar(nint receiver, nint selector);

    /// <summary>Runs one bounded native event loop with entirely in-memory settings.</summary>
    internal static int Run()
    {
        try
        {
            var initial = Configuration("#181818");
            var next = Configuration("#101010");
            var shell = new MacEditorShell();
            var reads = 0;
            using var controller = new NativeEditorController(shell, initial,
                ThemePolicies.Resolve(initial.ThemeId, true), null,
                EditorPresentationProfile.LegacyPage, () =>
                {
                    Interlocked.Increment(ref reads);
                    return next;
                });
            var workflow = new Workflow(shell, () => Volatile.Read(ref reads));
            shell.Shown += workflow.Start;
            controller.Run();
            return workflow.Succeeded ? 0 : 1;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"Mac theme override probe failed: {error.GetType().Name}.");
            return 1;
        }
    }

    /// <summary>Builds immutable settings without opening or creating the synthetic paths.</summary>
    private static MoteConfiguration Configuration(string previewBackground)
    {
        if (!ThemeOverrideData.TryCreate([new("preview.background", previewBackground)],
                out var colors, out _))
            throw new InvalidOperationException("Probe override schema was rejected.");
        var home = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory,
            ".temp", "mac-theme-probe-unused-home"));
        return new MoteConfiguration
        {
            ReadDisposition = ConfigReadDisposition.Loaded,
            HomeDirectory = home,
            ConfigPath = Path.Combine(home, "config.toml"),
            CacheDirectory = Path.Combine(home, "cache"),
            DataDirectory = Path.Combine(home, "data"),
            TraceDirectory = Path.Combine(home, "trace"),
            ThemeId = ThemePolicies.DarkId,
            ThemeOverrides = colors,
            TraceEnabled = false,
            PreviewLayout = PreviewLayoutPreference.Split,
            Diagnostics = []
        };
    }

    /// <summary>Polls UI-thread state without blocking AppKit or the reload worker.</summary>
    private sealed class Workflow(MacEditorShell shell, Func<int> reloadCount)
    {
        private readonly Stopwatch _elapsed = Stopwatch.StartNew();
        private NativeDocumentStamp? _beforeReload;
        private ObjC.Range _selection;
        private int _stage;
        private bool _done;

        /// <summary>True only after native color readback and routed undo/redo checks pass.</summary>
        internal bool Succeeded { get; private set; }

        /// <summary>Starts after the controller's normal Shown handler has initialized the view.</summary>
        internal void Start() => Schedule();

        private void Schedule()
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(50).ConfigureAwait(false);
                shell.Post(Tick);
            });
        }

        private void Tick()
        {
            if (_done) return;
            if (_elapsed.Elapsed >= TimeSpan.FromSeconds(20))
            {
                Finish(false);
                return;
            }
            try
            {
                Advance();
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                Console.Error.WriteLine($"Mac theme override stage {_stage}: {error.Message}");
                Finish(false);
            }
            if (!_done) Schedule();
        }

        private void Advance()
        {
            switch (_stage)
            {
                case 0 when shell.ProbeDocumentStamp is not null:
                    Require(shell.ProbeNativeText.Length == 0, "startup source is empty");
                    Require(HasPreviewColor(0x18), "startup override installed in NSTextView");
                    Require(reloadCount() == 0, "startup does not call reload loader");
                    shell.ProbeInsertAtEnd(Source);
                    _stage = 1;
                    break;
                case 1 when shell.ProbeNativeText == Source && shell.ProbeProjectedText == Source:
                    _beforeReload = shell.ProbeDocumentStamp;
                    Require(_beforeReload is not null, "edited document has identity");
                    _selection = new ObjC.Range(13, 1);
                    ObjC.Send(shell.ProbeEditorView, ObjC.Sel("setSelectedRange:"), _selection);
                    Require(ObjC.SendRange(shell.ProbeEditorView, ObjC.Sel("selectedRange")) ==
                        _selection, "source selection installed");
                    shell.ProbeInvokeMenu("moteReloadSettings:");
                    _stage = 2;
                    break;
                case 2 when reloadCount() == 1 && HasPreviewColor(0x10):
                    Require(shell.ProbeNativeText == Source && shell.ProbeProjectedText == Source,
                        "reload preserves native and engine source");
                    Require(shell.ProbeDocumentStamp == _beforeReload,
                        "reload does not create an engine transaction");
                    Require(ObjC.SendRange(shell.ProbeEditorView, ObjC.Sel("selectedRange")) ==
                        _selection, "reload preserves source selection");
                    shell.ProbeInvokeMenu("moteUndo:");
                    _stage = 3;
                    break;
                case 3 when shell.ProbeNativeText.Length == 0 && shell.ProbeProjectedText.Length == 0:
                    Require(HasPreviewColor(0x10), "undo keeps the reloaded palette");
                    Require(shell.ProbeDocumentStamp != _beforeReload, "undo changes document version");
                    shell.ProbeInvokeMenu("moteRedo:");
                    _stage = 4;
                    break;
                case 4 when shell.ProbeNativeText == Source && shell.ProbeProjectedText == Source:
                    Require(HasPreviewColor(0x10), "redo keeps the reloaded palette");
                    Require(reloadCount() == 1, "exactly one explicit reload snapshot");
                    Console.WriteLine("Mac theme overrides: startup/reload native color, source/stamp/selection, undo/redo passed.");
                    Finish(true);
                    break;
            }
        }

        /// <summary>Converts to sRGB before querying components; nil is a failed readback.</summary>
        private bool HasPreviewColor(byte gray)
        {
            var color = ObjC.Send(shell.ProbePreviewView, ObjC.Sel("backgroundColor"));
            var space = ObjC.Send(ObjC.Class("NSColorSpace"), ObjC.Sel("sRGBColorSpace"));
            var rgb = ObjC.Send(color, ObjC.Sel("colorUsingColorSpace:"), space);
            if (rgb == 0) return false;
            var expected = gray / 255.0;
            return Math.Abs(Scalar(rgb, ObjC.Sel("redComponent")) - expected) < 0.0001 &&
                Math.Abs(Scalar(rgb, ObjC.Sel("greenComponent")) - expected) < 0.0001 &&
                Math.Abs(Scalar(rgb, ObjC.Sel("blueComponent")) - expected) < 0.0001 &&
                Math.Abs(Scalar(rgb, ObjC.Sel("alphaComponent")) - 1) < 0.0001;
        }

        /// <summary>Discards this process-local untitled document without a modal prompt.</summary>
        private void Finish(bool success)
        {
            _done = true;
            Succeeded = success;
            if (!success) Console.Error.WriteLine($"Mac theme override stage {_stage} did not complete.");
            shell.ProbeApproveDiscardOnce();
            shell.Close();
        }
    }

    private static void Require(bool condition, string contract)
    {
        if (!condition) throw new InvalidOperationException(contract);
    }
}
