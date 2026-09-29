using System.Runtime.Versioning;
using System.Security.Cryptography;
using Mote.Configuration;
using Mote.Engine;
using Mote.Themes;

namespace Mote.Native.Mac.Canvas;

/// <summary>Published-binary AppKit regression for deleting off-host selections.</summary>
[SupportedOSPlatform("macos")]
internal static class MacCanvasDeleteProbe
{
    /// <summary>
    /// Tests forward and reverse global selection deletion on a real empty
    /// LF/CRLF input host while leaving the source file unchanged.
    /// </summary>
    internal static int Run(string input)
    {
        try
        {
            var path = Path.GetFullPath(input);
            var root = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".temp"));
            var file = new FileInfo(path);
            if (!string.Equals(Path.GetDirectoryName(path), root, StringComparison.Ordinal) ||
                !file.Exists || file.LinkTarget is not null)
                return 2;
            using var source = Document.OpenAsync(path).GetAwaiter().GetResult();
            var original = source.Snapshot.GetText();
            var emptyStart = original switch
            {
                "a\n\n" => 2,
                "a\r\n\r\n" => 3,
                _ => -1
            };
            if (emptyStart < 0) return 2;
            var expected = original[emptyStart..];
            var hash = SHA256.HashData(File.ReadAllBytes(path));
            var shell = new MacEditorShell(experimentalCanvas: true);
            var config = MoteConfigLoader.Load();
            var theme = ThemePolicies.Resolve(config.ThemeId, shell.PrefersDark);
            using var controller = new NativeEditorController(shell, config, theme, path);
            var workflow = new Workflow(shell, path, original, expected, emptyStart);
            shell.Shown += workflow.Start;
            controller.Run();
            return workflow.Succeeded &&
                SHA256.HashData(File.ReadAllBytes(path)).AsSpan().SequenceEqual(hash) ? 0 : 1;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"Mac canvas delete probe failed: {error.GetType().Name}.");
            return 1;
        }
    }

    private sealed class Workflow
    {
        private readonly MacEditorShell _shell;
        private readonly string _path;
        private readonly string _original;
        private readonly string _expected;
        private readonly int _emptyStart;
        private readonly DateTime _deadline = DateTime.UtcNow.AddSeconds(60);
        private long _baseVersion;
        private long _firstDeleteVersion;
        private long _secondDeleteVersion;
        private (int Anchor, int Active) _reverseImmediately;
        private int _stage;
        private bool _done;

        internal Workflow(MacEditorShell shell, string path, string original,
            string expected, int emptyStart)
        {
            _shell = shell;
            _path = path;
            _original = original;
            _expected = expected;
            _emptyStart = emptyStart;
        }

        internal bool Succeeded { get; private set; }

        internal void Start() => Schedule();

        private void Schedule()
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(40).ConfigureAwait(false);
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
                    case 0 when _shell.ProbeTitle.Contains(Path.GetFileName(_path),
                        StringComparison.Ordinal) &&
                        _shell.ProbeCanvasSnapshot?.GetText() == _original:
                        _baseVersion = _shell.ProbeCanvasVersion;
                        _shell.ProbeCaptureCanvasErrors();
                        _shell.ProbeCanvasSelectGlobal(0, _emptyStart);
                        _stage = 1;
                        break;
                    case 1 when _shell.ProbeCanvasSelection == (0, _emptyStart) &&
                        _shell.ProbeNativeText.Length == 0:
                        _shell.ProbeCanvasDelete(forward: false);
                        _stage = 2;
                        break;
                    case 2 when _shell.ProbeCanvasSnapshot?.GetText() == _expected &&
                        _shell.ProbeCanvasVersion > _baseVersion:
                        _firstDeleteVersion = _shell.ProbeCanvasVersion;
                        _shell.ProbeInvokeMenu("moteUndo:");
                        _stage = 3;
                        break;
                    case 3 when _shell.ProbeCanvasSnapshot?.GetText() == _original &&
                        _shell.ProbeCanvasVersion > _firstDeleteVersion:
                        _shell.ProbeCanvasSelectGlobal(_emptyStart, 0);
                        _reverseImmediately = _shell.ProbeCanvasSelection;
                        _stage = 4;
                        break;
                    case 4 when _shell.ProbeCanvasSelection == (_emptyStart, 0):
                        _shell.ProbeCanvasDelete(forward: true);
                        _stage = 5;
                        break;
                    case 5 when _shell.ProbeCanvasSnapshot?.GetText() == _expected &&
                        _shell.ProbeCanvasVersion > _firstDeleteVersion:
                        _secondDeleteVersion = _shell.ProbeCanvasVersion;
                        _shell.ProbeInvokeMenu("moteUndo:");
                        _stage = 6;
                        break;
                    case 6 when _shell.ProbeCanvasSnapshot?.GetText() == _original &&
                        _shell.ProbeCanvasVersion > _secondDeleteVersion:
                        _shell.ProbeCanvasSelectGlobal(_emptyStart, 0);
                        _stage = 7;
                        break;
                    case 7 when _shell.ProbeCanvasSelection == (_emptyStart, 0) &&
                        _shell.ProbeNativeText == "a":
                        _shell.ProbeCanvasSelectionEcho(0, 0);
                        _shell.ProbeCanvasSelectionEcho(0, 0);
                        _shell.ProbeCanvasSelectionEcho(0, 0);
                        _stage = 8;
                        break;
                    case 8 when _shell.ProbeCanvasInputDisabled &&
                        _shell.ProbeCanvasSelection == (_emptyStart, 0) &&
                        _shell.ProbeCanvasSnapshot?.GetText() == _original &&
                        _shell.ProbeCanvasError?.Contains("did not reconcile",
                            StringComparison.Ordinal) == true:
                        Succeeded = true;
                        Finish(true);
                        return;
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                Console.Error.WriteLine($"Mac canvas delete stage {_stage}: {error.GetType().Name}.");
                Finish(false);
                return;
            }
            Schedule();
        }

        private void Finish(bool success)
        {
            _done = true;
            Succeeded = success;
            if (!success)
            {
                var selection = _shell.ProbeCanvasSelection;
                Console.Error.WriteLine($"Mac canvas delete stage {_stage} failed: " +
                    $"selection={selection.Anchor},{selection.Active}; " +
                    $"reverse-immediate={_reverseImmediately.Anchor},{_reverseImmediately.Active}; " +
                    $"version={_shell.ProbeCanvasVersion}; " +
                    $"snapshot-version={_shell.ProbeCanvasSnapshot?.Version ?? -1}; " +
                    $"input-start={_shell.ProbeCanvasInputStart}; " +
                    $"nonce={_shell.ProbeCanvasNonce}; " +
                    $"native-length={_shell.ProbeNativeText.Length}; " +
                    $"marked={_shell.ProbeHasMarkedText}; " +
                    _shell.ProbeCanvasNativeSelectionTrace + ".");
            }
            _shell.Close();
        }
    }
}
