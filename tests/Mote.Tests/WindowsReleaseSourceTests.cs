using System.Reflection;
using System.Runtime.Versioning;
using Mote.Engine;
using Mote.Formats;
using Mote.Native;
using Mote.Native.Windows;
using Mote.Themes;

namespace Mote.Tests;

/// <summary>Hidden real RichEdit controls check release source transfer and deferred admission without OS settings changes.</summary>
public sealed class WindowsReleaseSourceTests
{
    /// <summary>Source import is literal even when the file starts with a valid or incomplete RTF header.</summary>
    [Theory]
    [InlineData(@"{\rtf1\ansi literal}")]
    [InlineData(@"{\rtf1")]
    [InlineData("中文😀\r\nsecond\r\n")]
    [InlineData("中文😀\nsecond\n")]
    [InlineData("")]
    public void InstallationPreservesLiteralTextAndDisablesNativeHistory(string source)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var host = new Host();
        using var document = new Document(source);
        var binding = new NativeSourceBinding(document.Snapshot, 1, 1, NativeLineEndingMode.CrLf, source.Length, source.Length);
        var observation = host.Shell.InstallSource(binding.Installation);
        Assert.True(binding.Matches(observation));
        Assert.Equal(binding.Installation.Projection.Display, observation!.Display);
        Assert.Equal(observation.Display.Length, observation.Selection.Start);
        Assert.Equal(observation.Display.Length, observation.Selection.End);
        Assert.True(NativeSourceBinding.TrySelection(binding.Installation.Projection, observation.Selection,
            out var anchor, out var active, out var known));
        Assert.Equal(source.Length, anchor);
        Assert.Equal(source.Length, active);
        Assert.True(known);
        Assert.Equal(0, Win32.SendMessageW(host.Editor, 0x00C6, 0, 0)); // EM_CANUNDO.
        Assert.True(host.Shell.CommitPendingText());
    }

    /// <summary>Selection-before-edit notifications cannot publish new-text offsets against an old certified snapshot.</summary>
    [Theory]
    [InlineData("a", "ab")]
    [InlineData("a\r\n", "a\r\nb")]
    [InlineData("a\n", "a\nb")]
    public void PendingCandidateDefersSelectionUntilExactAdmission(string source, string expected)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var host = new Host();
        using var document = new Document(source);
        var binding = new NativeSourceBinding(document.Snapshot, 7, 11, NativeLineEndingMode.CrLf, source.Length, source.Length);
        Assert.True(binding.Matches(host.Shell.InstallSource(binding.Installation)));
        var views = new List<NativeSourceViewObservation>();
        host.Shell.SourceViewChanged += views.Add;
        host.Shell.SourceCandidate += Admit;
        void Admit(NativeSourceCandidate candidate)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            var prepared = binding.Prepare(document.Snapshot, candidate);
            Assert.NotNull(prepared);
            Assert.NotNull(prepared.Change);
            document.Apply(prepared.Change.Value);
            binding = binding.Advance(document.Snapshot, prepared);
            Assert.True(host.Shell.AcknowledgeSource(binding.Installation));
        }
        Win32.SendMessageW(host.Editor, (int)Win32.WM_CHAR, 'b', 0);
        host.Invoke("OnTextChanged"); // Hidden STATIC parent has no product notification procedure.
        host.Invoke("PublishSourceView");
        Assert.Empty(views);
        host.Invoke("WindowMessage", host.Parent, (uint)0x8003, (nuint)0, (nint)0);
        host.Invoke("PublishSourceView");
        var view = Assert.Single(views);
        Assert.Equal(expected, document.Snapshot.GetText());
        Assert.Equal(document.Snapshot.Version, view.Stamp.Version);
        Assert.Equal(binding.Installation.Projection.Display.Length, view.Selection.Start);
        Assert.Equal(binding.Installation.Projection.Display.Length, view.Selection.End);
        Assert.Equal(0, Win32.SendMessageW(host.Editor, 0x00C6, 0, 0));
    }

    /// <summary>Actual detached TOM foreground changes and neutral revocation preserve source, selection and sole engine history.</summary>
    [Fact]
    public void DecorationAndNeutralRevocationDoNotMutateTextSelectionOrHistory()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var host = new Host();
        using var document = new Document("\"中文😀\"");
        var binding = new NativeSourceBinding(document.Snapshot, 3, 9, NativeLineEndingMode.CrLf,
            document.Snapshot.Length, document.Snapshot.Length);
        Assert.True(binding.Matches(host.Shell.InstallSource(binding.Installation)));
        var snapshot = document.Snapshot;
        var palette = ThemePolicies.Get(ThemePolicies.DefaultId);
        host.Shell.SetSourceSemantics(new(binding.Installation.Stamp, binding.Installation.Nonce, 1,
            AnalysisCompleteness.Complete, new TextSpan(0, snapshot.Length),
            [new SemanticToken("string", new TextSpan(0, snapshot.Length))], []));
        host.Invoke("ApplySourceStyleTurn");
        using (var range = new WindowsRichEditForegroundRange(host.Editor))
            Assert.Equal(ColorRef(palette.SemanticColor("string")), range.Read(0, 1));
        host.Shell.SetSourceSemantics(new(binding.Installation.Stamp, binding.Installation.Nonce, 2,
            AnalysisCompleteness.Complete, new TextSpan(0, snapshot.Length), [], []));
        host.Invoke("ApplySourceStyleTurn");
        using (var range = new WindowsRichEditForegroundRange(host.Editor))
            Assert.Equal(ColorRef(palette.Palette.EditorForeground), range.Read(0, 1));
        var observation = (NativeSourceObservation)host.Invoke("ObserveSource")!;
        Assert.True(binding.Matches(observation));
        Assert.Equal(snapshot.Length, observation.Selection.Start);
        Assert.Equal(snapshot.Length, observation.Selection.End);
        Assert.Same(snapshot, document.Snapshot);
        Assert.False(document.CanUndo);
        Assert.Equal(0, Win32.SendMessageW(host.Editor, 0x00C6, 0, 0));
    }

    /// <summary>Windows COLORREF packs the theme's RGB bytes without alpha or sign extension.</summary>
    private static uint ColorRef(ThemeColor color) => (uint)(color.Red | color.Green << 8 | color.Blue << 16);

    /// <summary>One owner-thread hidden control host, with no clipboard, input-source, font or focus mutation.</summary>
    [SupportedOSPlatform("windows")]
    private sealed class Host : IDisposable
    {
        /// <summary>Hidden parent bounds native viewport queries and owns its child lifetime.</summary>
        internal nint Parent { get; }
        /// <summary>Actual system RichEdit instance; not a mocked text buffer.</summary>
        internal nint Editor { get; }
        /// <summary>Product source adapter attached to hidden native handles.</summary>
        internal WindowsEditorShell Shell { get; } = new(nativeSource: true);

        /// <summary>Creates only process-owned windows; all test work remains on the constructing thread.</summary>
        internal Host()
        {
            Assert.NotEqual(0, Win32.LoadLibraryW("msftedit.dll"));
            var module = Win32.GetModuleHandleW(null);
            Parent = Win32.CreateWindowExW(0, "STATIC", "", 0, 0, 0, 600, 300, 0, 0, module, 0);
            Assert.NotEqual(0, Parent);
            Editor = Win32.CreateWindowExW(0, "RICHEDIT50W", "", Win32.WS_CHILD | Win32.ES_MULTILINE,
                0, 0, 600, 300, Parent, 101, module, 0);
            Assert.NotEqual(0, Editor);
            Field("_window").SetValue(Shell, Parent);
            Field("_editor").SetValue(Shell, Editor);
            Win32.SendMessageW(Editor, Win32.EM_EXLIMITTEXT, 0, int.MaxValue);
        }

        /// <summary>Calls internal product handlers without installing global subclasses or running a second UI loop.</summary>
        internal object? Invoke(string method, params object[] arguments) => typeof(WindowsEditorShell)
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Shell, arguments);

        /// <summary>Stops process-owned pending timers before destroying the whole native child tree.</summary>
        public void Dispose()
        {
            Win32.KillTimer(Parent, 2);
            Win32.DestroyWindow(Parent);
        }

        /// <summary>Only private handle wiring is reflected; text transfer and certification remain actual product code.</summary>
        private static FieldInfo Field(string name) => typeof(WindowsEditorShell)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    }
}
