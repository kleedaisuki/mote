using System.Security.Cryptography;
using System.Runtime.Versioning;
using Mote.Configuration;
using Mote.Engine;
using Mote.Themes;

namespace Mote.Native.Mac;

/// <summary>
/// Published-binary AppKit integration probe using a real NSTextView and the
/// production controller. It is in-process, not external keyboard or IME input.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacNativeWorkflowProbe
{
    private const string InsertedText = "\n# mote-native-workflow";
    private const string MarkedText = "中";
    private const string DiscardedMarkedText = "弃";

    /// <summary>
    /// Opens a small input, inserts via NSTextView, saves through the native
    /// Save As menu action, creates a new document, and reopens the saved file.
    /// The output must be a new file under this repository's .temp directory.
    /// </summary>
    internal static int Run(string input, string output)
    {
        try
        {
            var inputPath = Path.GetFullPath(input);
            var outputPath = Path.GetFullPath(output);
            var tempRoot = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".temp"));
            var tempDirectory = new DirectoryInfo(tempRoot);
            var outputFile = new FileInfo(outputPath);
            if (!tempDirectory.Exists || tempDirectory.LinkTarget is not null ||
                !string.Equals(Path.GetDirectoryName(outputPath), tempRoot, StringComparison.Ordinal) ||
                outputFile.Exists || outputFile.LinkTarget is not null || !File.Exists(inputPath) ||
                string.Equals(inputPath, outputPath, StringComparison.Ordinal))
            {
                Console.Error.WriteLine("Mac workflow output must be a new file under repository .temp.");
                return 2;
            }

            var before = SHA256.HashData(File.ReadAllBytes(inputPath));
            using var inputDocument = Document.OpenAsync(inputPath).GetAwaiter().GetResult();
            if (inputDocument.Snapshot.Length >= NativeEditorController.PageSize / 2)
            {
                Console.Error.WriteLine("Mac workflow input exceeds the bounded probe size.");
                return 2;
            }
            var original = inputDocument.Snapshot.GetText();
            var expected = original + InsertedText + MarkedText;
            var shell = new MacEditorShell();
            var configuration = MoteConfigLoader.Load();
            var theme = ThemePolicies.Resolve(configuration.ThemeId, shell.PrefersDark);
            using var controller = new NativeEditorController(shell, configuration, theme, inputPath);
            var workflow = new Workflow(shell, inputPath, outputPath, original,
                original + InsertedText, expected);
            shell.Shown += workflow.Start;
            controller.Run();

            if (!workflow.Succeeded || !SHA256.HashData(File.ReadAllBytes(inputPath)).AsSpan().SequenceEqual(before))
            {
                Console.Error.WriteLine("Mac workflow did not complete or changed its input.");
                return 1;
            }
            using var reopened = Document.OpenAsync(outputPath).GetAwaiter().GetResult();
            if (reopened.Snapshot.GetText() != expected)
            {
                Console.Error.WriteLine("Mac workflow saved bytes do not match the native edit.");
                return 1;
            }
            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"Mac workflow failed: {error.GetType().Name}.");
            return 1;
        }
    }

    private sealed class Workflow
    {
        private readonly MacEditorShell _shell;
        private readonly string _input;
        private readonly string _output;
        private readonly string _original;
        private readonly string _plainEdited;
        private readonly string _expected;
        private readonly DateTime _deadline = DateTime.UtcNow.AddSeconds(40);
        private int _stage;
        private bool _done;

        internal Workflow(MacEditorShell shell, string input, string output,
            string original, string plainEdited, string expected)
        {
            _shell = shell;
            _input = input;
            _output = output;
            _original = original;
            _plainEdited = plainEdited;
            _expected = expected;
        }

        internal bool Succeeded { get; private set; }

        internal void Start() => Schedule();

        private void Schedule()
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(50).ConfigureAwait(false);
                if (!_done) _shell.Post(Tick);
            });
        }

        private void Tick()
        {
            if (_done) return;
            if (DateTime.UtcNow >= _deadline)
            {
                Finish(false);
                return;
            }
            try
            {
                switch (_stage)
                {
                    case 0 when _shell.ProbeTitle.Contains(Path.GetFileName(_input),
                        StringComparison.Ordinal) && _shell.ProbeNativeText == _original:
                        // Query AppKit's actual legacy source view without changing
                        // its value, selection, first responder, or input state.
                        if (ObjC.ManagedString(ObjC.Send(_shell.ProbeEditorView,
                            ObjC.Sel("accessibilityLabel"))) != "Mote editor")
                            throw new InvalidOperationException("Legacy source AX label is missing.");
                        _shell.ProbeInsertAtEnd(InsertedText);
                        _stage = 1;
                        break;
                    case 1 when _shell.ProbeNativeText == _plainEdited && _shell.ProbeIsModified:
                        _shell.ProbeSetMarkedAtEnd(MarkedText);
                        _stage = 2;
                        break;
                    case 2 when _shell.ProbeNativeText == _expected &&
                        _shell.ProbeHasMarkedText && _shell.ProbeProjectedText == _plainEdited:
                        if (File.Exists(_output)) { Finish(false); return; }
                        _shell.ProbePickSave(_output);
                        _shell.ProbeInvokeMenu("moteSaveAs:");
                        _stage = 3;
                        break;
                    case 3 when File.Exists(_output) && !_shell.ProbeIsModified &&
                        !_shell.ProbeHasMarkedText:
                        _shell.ProbeSetMarkedAtEnd(DiscardedMarkedText);
                        _stage = 4;
                        break;
                    case 4 when _shell.ProbeHasMarkedText &&
                        _shell.ProbeNativeText == _expected + DiscardedMarkedText &&
                        _shell.ProbeProjectedText == _expected:
                        _shell.ProbeApproveDiscardOnce();
                        _shell.ProbeInvokeMenu("moteNew:");
                        _stage = 5;
                        break;
                    case 5 when _shell.ProbeNativeText.Length == 0 &&
                        _shell.ProbeDidConfirmDiscard &&
                        _shell.ProbeTitle.Contains("Untitled", StringComparison.Ordinal):
                        _shell.ProbePickOpen(_output);
                        _shell.ProbeInvokeMenu("moteOpen:");
                        _stage = 6;
                        break;
                    case 6 when _shell.ProbeTitle.Contains(Path.GetFileName(_output),
                        StringComparison.Ordinal) && _shell.ProbeNativeText == _expected:
                        Succeeded = true;
                        Finish(true);
                        return;
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                Console.Error.WriteLine($"Mac workflow stage {_stage} failed: {error.GetType().Name}.");
                Finish(false);
                return;
            }
            Schedule();
        }

        private void Finish(bool success)
        {
            _done = true;
            Succeeded = success;
            if (!success) Console.Error.WriteLine($"Mac workflow stage {_stage} did not complete.");
            _shell.Close();
        }
    }
}
