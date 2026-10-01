using System.Runtime.Versioning;
using System.Security.Cryptography;
using Mote.Configuration;
using Mote.Engine;
using Mote.Themes;

namespace Mote.Native.Mac;

/// <summary>Bounded real-AppKit product workflows. Native protocol calls are not external keyboard or IME evidence.</summary>
internal static class MacReleaseWorkflowProbe
{
    /// <summary>Exactly one fixture marker is replaced through native input, preserving each format's valid structure.</summary>
    internal const string OriginalMarker = "mote-release-original";
    /// <summary>The independent hosted wrapper verifies this exact replacement, including marked-text commit.</summary>
    internal const string EditedMarker = "mote-release-edited中";

    /// <summary>Exercises production admission, responder history, navigation, theme reload and marked-input Save As.</summary>
    [SupportedOSPlatform("macos")]
    internal static int Run(string input, string output) => Execute(input, output);

    /// <summary>Creates a fresh GUI/controller and verifies complete decoded source against the saved file.</summary>
    [SupportedOSPlatform("macos")]
    internal static int RunReopen(string input) => Execute(input, null);

    /// <summary>Bounds diagnostic work, protects the original file, and requires a new guarded repository output.</summary>
    [SupportedOSPlatform("macos")]
    private static int Execute(string input, string? output)
    {
        try
        {
            var path = Path.GetFullPath(input);
            if (new FileInfo(path).Length > 1024 * 1024) throw new ArgumentException("Probe input exceeds its diagnostic bound.");
            var destination = output is null ? null : Path.GetFullPath(output);
            if (destination is not null) ValidateOutput(destination);
            var originalBytes = File.ReadAllBytes(path);
            using var originalDocument = Document.OpenAsync(path).GetAwaiter().GetResult();
            var original = originalDocument.Snapshot.GetText();
            if (destination is not null) ValidateMarker(original);
            var config = MoteConfigLoader.Load() with { ThemeId = ThemePolicies.DarkId };
            var shell = new MacEditorShell(nativeSource: true);
            using var controller = new NativeEditorController(shell, config,
                ThemePolicies.Resolve(config.ThemeId, shell.PrefersDark), path,
                EditorPresentationProfile.NativeSource,
                () => config with { ThemeId = ThemePolicies.LightId });
            var workflow = new Workflow(shell, path, destination, original);
            shell.Shown += workflow.Start;
            controller.Run();
            if (!workflow.Succeeded || !SHA256.HashData(File.ReadAllBytes(path)).AsSpan().SequenceEqual(SHA256.HashData(originalBytes)))
                return 1;
            if (destination is not null)
            {
                using var saved = Document.OpenAsync(destination).GetAwaiter().GetResult();
                if (saved.Snapshot.GetText() != original.Replace(OriginalMarker, EditedMarker, StringComparison.Ordinal)) return 1;
            }
            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"Mac release workflow failed: {error.GetType().Name}.");
            return 1;
        }
    }

    /// <summary>Rejects overwrite, linked fixture ancestors and destinations outside repository .temp/.cache.</summary>
    internal static void ValidateOutput(string destination, string? repositoryRoot = null)
    {
        destination = Path.GetFullPath(destination);
        var root = new[] { ".temp", ".cache" }.Select(name => Path.GetFullPath(Path.Combine(repositoryRoot ?? Environment.CurrentDirectory, name)))
            .FirstOrDefault(candidate => destination.StartsWith(candidate + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        if (root is null) throw new ArgumentException("Output must remain under repository .temp or .cache.");
        var file = new FileInfo(destination);
        if (file.Exists || file.LinkTarget is not null) throw new ArgumentException("Output must not overwrite a file or link.");
        var parent = file.Directory;
        while (parent is not null)
        {
            if (!parent.Exists || parent.LinkTarget is not null) throw new ArgumentException("Output ancestors must be existing real directories.");
            if (string.Equals(parent.FullName, root, StringComparison.Ordinal)) return;
            parent = parent.Parent;
        }
        throw new ArgumentException("Output escaped its repository fixture root.");
    }

    /// <summary>Requires one unambiguous ordinal fixture target before the native event loop starts.</summary>
    internal static void ValidateMarker(string original)
    {
        var first = original.IndexOf(OriginalMarker, StringComparison.Ordinal);
        if (first < 0 || first != original.LastIndexOf(OriginalMarker, StringComparison.Ordinal))
            throw new ArgumentException("Workflow requires exactly one fixture marker.");
    }

    /// <summary>One UI-thread state machine; watchdogs are liveness bounds, not experience targets.</summary>
    [SupportedOSPlatform("macos")]
    private sealed class Workflow(MacEditorShell shell, string input, string? output, string original)
    {
        /// <summary>Finite diagnostic deadline, independent from editor responsiveness criteria.</summary>
        private readonly DateTime _deadline = DateTime.UtcNow.AddSeconds(40);
        /// <summary>Only the native UI thread advances the task stage.</summary>
        private int _stage;
        /// <summary>Prevents late queued callbacks from touching a closed native window.</summary>
        private bool _done;
        /// <summary>True only after every requested native task and exact source witness succeeds.</summary>
        internal bool Succeeded { get; private set; }
        /// <summary>Starts after the actual product window has been shown.</summary>
        internal void Start() => Schedule();
        /// <summary>Waits off the UI thread, then posts one bounded observation to AppKit.</summary>
        private void Schedule() => _ = Task.Run(async () =>
        {
            await Task.Delay(50).ConfigureAwait(false);
            if (!_done) shell.Post(Tick);
        });

        /// <summary>Uses native input and actual menu/responder entrypoints, not direct engine mutation.</summary>
        private void Tick()
        {
            if (_done) return;
            if (DateTime.UtcNow >= _deadline) { Finish(false); return; }
            try
            {
                var markerStart = original.IndexOf(OriginalMarker, StringComparison.Ordinal);
                var prefix = EditedMarker[..^1];
                var plain = original.Replace(OriginalMarker, prefix, StringComparison.Ordinal);
                var expected = original.Replace(OriginalMarker, EditedMarker, StringComparison.Ordinal);
                switch (_stage)
                {
                    case 0 when shell.ProbeTitle.Contains(Path.GetFileName(input), StringComparison.Ordinal) &&
                        shell.ProbeNativeText == original && shell.ProbeProjectedText == original:
                        if (ObjC.ManagedString(ObjC.Send(shell.ProbeEditorView, ObjC.Sel("accessibilityLabel"))) != "Mote editor")
                            throw new InvalidOperationException("Source accessibility label missing.");
                        if (output is null) { Finish(true); return; }
                        shell.ProbeReleaseReplace(markerStart, OriginalMarker.Length, prefix);
                        _stage = 1;
                        break;
                    case 1 when shell.ProbeNativeText == plain && shell.ProbeProjectedText == plain && shell.ProbeIsModified:
                        shell.ProbeReleaseHistory(false);
                        _stage = 2;
                        break;
                    case 2 when shell.ProbeNativeText == original && shell.ProbeProjectedText == original:
                        shell.ProbeReleaseHistory(true);
                        _stage = 3;
                        break;
                    case 3 when shell.ProbeNativeText == plain && shell.ProbeProjectedText == plain:
                        shell.ProbeReleaseFind(prefix);
                        shell.ProbeInvokeMenu("moteFind:");
                        _stage = 4;
                        break;
                    case 4 when shell.ProbeReleaseSelection == new ObjC.Range((nuint)markerStart, (nuint)prefix.Length):
                        shell.ProbeReleaseGoToLine(1);
                        shell.ProbeInvokeMenu("moteGoToLine:");
                        _stage = 5;
                        break;
                    case 5 when shell.ProbeReleaseSelection == new ObjC.Range(0, 0):
                        shell.ProbeInvokeMenu("moteReloadSettings:");
                        _stage = 6;
                        break;
                    case 6 when shell.ProbeThemeId == ThemePolicies.LightId && shell.ProbeNativeText == plain:
                        shell.ProbeReleaseCapture(Path.Combine(Path.GetDirectoryName(output!)!, "native-product.png"));
                        shell.ProbeReleaseMarked(markerStart + prefix.Length, "中");
                        _stage = 7;
                        break;
                    case 7 when shell.ProbeHasMarkedText && shell.ProbeNativeText == expected &&
                        shell.ProbeProjectedText == plain:
                        shell.ProbePickSave(output!);
                        shell.ProbeInvokeMenu("moteSaveAs:");
                        _stage = 8;
                        break;
                    case 8 when File.Exists(output) && !shell.ProbeIsModified && !shell.ProbeHasMarkedText &&
                        shell.ProbeProjectedText == expected:
                        shell.ProbeSetMarkedAtEnd("弃");
                        shell.ProbeReleaseCancelClose();
                        _stage = 9;
                        break;
                    case 9 when shell.ProbeReleaseCloseCancelled && !shell.ProbeHasMarkedText && shell.ProbeIsModified &&
                        shell.ProbeNativeText == expected + "弃" && shell.ProbeProjectedText == expected + "弃":
                        if (!shell.ProbeReleaseCancelExternalOpen(output!) || shell.ProbeProjectedText != expected + "弃")
                            throw new InvalidOperationException("External open did not preserve the cancelled dirty document.");
                        shell.ProbeReleaseHistory(false);
                        _stage = 10;
                        break;
                    case 10 when shell.ProbeNativeText == expected && shell.ProbeProjectedText == expected:
                        shell.ProbeInvokeMenu("moteNew:");
                        _stage = 11;
                        break;
                    case 11 when shell.ProbeNativeText.Length == 0 && shell.ProbeTitle.Contains("Untitled", StringComparison.Ordinal):
                        if (!shell.ProbeReleaseExternalOpen(output!))
                            throw new InvalidOperationException("AppKit external-open delegate rejected the clean document.");
                        _stage = 12;
                        break;
                    case 12 when shell.ProbeTitle.Contains(Path.GetFileName(output!), StringComparison.Ordinal) &&
                        shell.ProbeNativeText == expected && shell.ProbeProjectedText == expected:
                        Finish(true);
                        return;
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                Console.Error.WriteLine($"Mac release workflow stage {_stage}: {error.GetType().Name}.");
                Finish(false);
                return;
            }
            Schedule();
        }

        /// <summary>Closes diagnostic UI only after recording the outcome; this is not dirty-close acceptance.</summary>
        private void Finish(bool success)
        {
            _done = true;
            Succeeded = success;
            if (!success) Console.Error.WriteLine($"Mac release workflow stage {_stage} did not complete.");
            shell.Close();
        }
    }
}
