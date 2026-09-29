using System.Runtime.Versioning;
using System.Security.Cryptography;
using Mote.Configuration;
using Mote.Engine;
using Mote.Themes;

namespace Mote.Native.Mac.Canvas;

/// <summary>
/// Published-binary AppKit clipboard gate for bounded input and undo-safe paste.
/// This exercises a real NSTextView and controller, not external keyboard or IME.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacCanvasClipboardProbe
{
    private const int LargeClipboardLength = 50 * 1024 * 1024;
    private const string RichPayload = "{\\rtf1\\ansi\\b RTF must not be imported\\b0}";

    /// <summary>
    /// Accepts 40 Ki mixed-format paste, rejects a 50 MiB transaction before
    /// losing Undo, then checks selected replacement and Save As/reopen.
    /// </summary>
    internal static int Run(string input, string output)
    {
        try
        {
            var inputPath = Path.GetFullPath(input);
            var outputPath = Path.GetFullPath(output);
            var tempRoot = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".temp"));
            var root = new DirectoryInfo(tempRoot);
            var inputFile = new FileInfo(inputPath);
            var outputFile = new FileInfo(outputPath);
            if (!root.Exists || root.LinkTarget is not null ||
                !string.Equals(Path.GetDirectoryName(inputPath), tempRoot,
                    StringComparison.Ordinal) ||
                !string.Equals(Path.GetDirectoryName(outputPath), tempRoot,
                    StringComparison.Ordinal) ||
                !inputFile.Exists || inputFile.LinkTarget is not null ||
                outputFile.Exists || outputFile.LinkTarget is not null ||
                string.Equals(inputPath, outputPath, StringComparison.Ordinal))
                return 2;
            using var originalDocument = Document.OpenAsync(inputPath).GetAwaiter().GetResult();
            var original = originalDocument.Snapshot.GetText();
            if (original != "abc")
                return 2;
            var inputHash = SHA256.HashData(File.ReadAllBytes(inputPath));
            var shell = new MacEditorShell(experimentalCanvas: true);
            var config = MoteConfigLoader.Load();
            var theme = ThemePolicies.Resolve(config.ThemeId, shell.PrefersDark);
            using var controller = new NativeEditorController(shell, config, theme, inputPath);
            var workflow = new Workflow(shell, inputPath, outputPath, original, inputHash);
            shell.Shown += workflow.Start;
            controller.Run();
            if (!workflow.Succeeded || !SameInput(inputPath, inputHash) || !File.Exists(outputPath))
                return 1;
            using var reopened = Document.OpenAsync(outputPath).GetAwaiter().GetResult();
            return reopened.Snapshot.GetText() == "Zac" ? 0 : 1;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"Mac canvas clipboard probe failed: {error.GetType().Name}.");
            return 1;
        }
    }

    private static bool SameInput(string path, byte[] hash) =>
        SHA256.HashData(File.ReadAllBytes(path)).AsSpan().SequenceEqual(hash);

    private sealed class Workflow
    {
        private readonly MacEditorShell _shell;
        private readonly string _input;
        private readonly string _output;
        private readonly string _original;
        private readonly byte[] _hash;
        private readonly DateTime _deadline = DateTime.UtcNow.AddMinutes(5);
        private long _baseVersion = -1;
        private long _largeVersion = -1;
        private long _undoVersion = -1;
        private long _redoVersion = -1;
        private long _selectedVersion = -1;
        private int _stage;
        private bool _done;

        internal Workflow(MacEditorShell shell, string input, string output,
            string original, byte[] hash)
        {
            _shell = shell;
            _input = input;
            _output = output;
            _original = original;
            _hash = hash;
        }

        internal bool Succeeded { get; private set; }

        internal void Start() => Schedule();

        private void Schedule()
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(60).ConfigureAwait(false);
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
                    case 0 when _shell.ProbeTitle.Contains(Path.GetFileName(_input),
                        StringComparison.Ordinal) && _shell.ProbeNativeText == _original &&
                        _shell.ProbeCanvasVersion >= 0:
                        _baseVersion = _shell.ProbeCanvasVersion;
                        _shell.ProbeCaptureCanvasErrors();
                        _shell.ProbeInvokeMenu("moteSelectAll:");
                        _stage = 1;
                        break;
                    case 1 when _shell.ProbeCanvasSelection == (0, 3):
                        _shell.ProbeInvokeMenu("moteSave:");
                        _stage = 2;
                        break;
                    case 2:
                        if (_shell.ProbeCanvasVersion != _baseVersion ||
                            _shell.ProbeNativeText != _original ||
                            _shell.ProbeCanvasError is not null || !SameInput(_input, _hash))
                        { Finish(false); return; }
                        _shell.ProbeCanvasSelectGlobal(0, 0);
                        _shell.ProbeCanvasPaste(new string('X', 40 * 1024), RichPayload);
                        _stage = 3;
                        break;
                    case 3 when _shell.ProbeCanvasSnapshot is { } large &&
                        large.Version > _baseVersion:
                        if (large.GetText() != new string('X', 40 * 1024) + _original ||
                            _shell.ProbeNativeText.Length > 16 * 1024 ||
                            _shell.ProbeCanvasError is not null || !SameInput(_input, _hash))
                        { Finish(false); return; }
                        _largeVersion = large.Version;
                        _shell.ProbeInvokeMenu("moteUndo:");
                        _stage = 4;
                        break;
                    case 4 when _shell.ProbeCanvasSnapshot is { } restored &&
                        restored.Version > _largeVersion && restored.GetText() == _original:
                        _undoVersion = restored.Version;
                        _shell.ProbeCanvasPaste(new string('Y', LargeClipboardLength), null);
                        _stage = 5;
                        break;
                    case 5 when _shell.ProbeCanvasError is { } refusal:
                        if (!refusal.Contains("undo-history budget", StringComparison.Ordinal) ||
                            _shell.ProbeCanvasSnapshot?.GetText() != _original ||
                            _shell.ProbeCanvasVersion != _undoVersion ||
                            _shell.ProbeNativeText != _original || !SameInput(_input, _hash))
                        { Finish(false); return; }
                        _shell.ProbeInvokeMenu("moteRedo:");
                        _stage = 6;
                        break;
                    case 6 when _shell.ProbeCanvasSnapshot is { } redone &&
                        redone.Version > _undoVersion &&
                        redone.GetText() == new string('X', 40 * 1024) + _original:
                        _redoVersion = redone.Version;
                        _shell.ProbeInvokeMenu("moteUndo:");
                        _stage = 7;
                        break;
                    case 7 when _shell.ProbeCanvasSnapshot is { } again &&
                        again.Version > _redoVersion && again.GetText() == _original:
                        _baseVersion = again.Version;
                        _shell.ProbeCanvasSelect(0, 2);
                        _stage = 8;
                        break;
                    case 8 when _shell.ProbeCanvasSelection == (0, 2):
                        // Minimal diff of "abc" -> "ac" deletes b and inserts
                        // nothing; the exact selected payload is still "a".
                        _shell.ProbeCanvasPaste("a", RichPayload);
                        _stage = 9;
                        break;
                    case 9 when _shell.ProbeNativeText == "ac" &&
                        _shell.ProbeCanvasVersion > _baseVersion &&
                        _shell.ProbeCanvasError is null && SameInput(_input, _hash):
                        _selectedVersion = _shell.ProbeCanvasVersion;
                        _shell.ProbeCanvasSelect(0, 0);
                        _stage = 10;
                        break;
                    case 10 when _shell.ProbeCanvasSelection == (0, 0):
                        _shell.ProbeCanvasPaste("Z", RichPayload);
                        _stage = 11;
                        break;
                    case 11 when _shell.ProbeNativeText == "Zac" &&
                        _shell.ProbeCanvasVersion > _selectedVersion &&
                        _shell.ProbeCanvasError is null && SameInput(_input, _hash):
                        _shell.ProbePickSave(_output);
                        _shell.ProbeInvokeMenu("moteSaveAs:");
                        _stage = 12;
                        break;
                    case 12 when File.Exists(_output) && !_shell.ProbeCanvasIsModified:
                        _shell.ProbeInvokeMenu("moteSelectAll:");
                        _stage = 13;
                        break;
                    case 13 when _shell.ProbeCanvasSelection == (0, 3):
                        if (!_shell.CommitPendingText() ||
                            _shell.ProbeCanvasSnapshot?.GetText() != "Zac")
                        { Finish(false); return; }
                        Succeeded = true;
                        Finish(true);
                        return;
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                Console.Error.WriteLine($"Mac canvas clipboard stage {_stage}: {error.GetType().Name}.");
                Finish(false);
                return;
            }
            Schedule();
        }

        private void Finish(bool success)
        {
            _done = true;
            Succeeded = success;
            if (!success) Console.Error.WriteLine($"Mac canvas clipboard stage {_stage} failed.");
            _shell.Close();
        }
    }
}
