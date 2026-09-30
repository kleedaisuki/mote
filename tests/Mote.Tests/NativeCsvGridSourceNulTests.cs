using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mote.Configuration;
using Mote.Engine;
using Mote.Native;
using Mote.Native.Windows;
using Mote.Native.Windows.Canvas;
using Mote.Native.Viewport;
using Mote.Themes;

namespace Mote.Tests;

/// <summary>Failure-directed hidden RichEdit source-safety reproductions, without desktop input.</summary>
public sealed class NativeCsvGridSourceNulTests
{
    /// <summary>Native source installation must retain its complete projected display, including the suffix.</summary>
    [Theory]
    [InlineData("a\0b")]
    [InlineData("\"a\0b\",tail\r\n")]
    [InlineData("a\0␀b")]
    [InlineData("a␀\0b")]
    [InlineData("a\0")]
    public void Source_install_retains_embedded_nul_and_suffix(string source)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new HiddenSource();
        var display = new NativeTextProjection(source, NativeLineEndingMode.CrLf).Display;
        fixture.Shell.SetDocument(new("NUL probe", display, 0, source.Length, false, "", new(1, 0)));
        Assert.Equal(display.Replace('\0', '\u2400'), fixture.Read());
    }

    /// <summary>NUL-bearing native pages refuse edits instead of guessing an inverse for display-only markers.</summary>
    [Theory]
    [InlineData("a\0b")]
    [InlineData("\"a\0b\",tail\r\n")]
    [InlineData("a\0␀b")]
    [InlineData("a␀\0b")]
    [InlineData("a\0")]
    public void Prefix_edit_through_real_controller_preserves_hidden_suffix(string source)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new HiddenSource();
        var config = MoteConfigLoader.Load(new MoteConfigLoadOptions
        {
            MoteHomeOverride = Path.GetFullPath(Path.Combine(".temp", "source-nul-probe")),
            UseEnvironmentOverride = false
        });
        using var controller = new NativeEditorController(fixture.Shell, config,
            ThemePolicies.Get(config.ThemeId), null);
        var document = new Document(source);
        typeof(NativeEditorController).GetMethod("ReplaceDocument", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(controller, [document, default(Mote.Telemetry.TelemetryMark), 0]);
        var version = document.Snapshot.Version;
        AttemptEdits(fixture.Editor, source.Length, () =>
        {
            if (!OperatingSystem.IsWindows()) return;
            typeof(WindowsEditorShell).GetMethod("OnTextChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(fixture.Shell, null);
            Assert.Equal(source, document.Snapshot.GetText());
            Assert.Equal(version, document.Snapshot.Version);
            Assert.True(fixture.Shell.CommitPendingText());
            Assert.Equal(new NativeTextProjection(source, NativeLineEndingMode.CrLf).Display.Replace('\0', '\u2400'),
                fixture.Read());
        });
    }

    /// <summary>The Continuous RichEdit island refuses all edits of a NUL-bearing bounded source interval.</summary>
    [Theory]
    [InlineData("a\0b")]
    [InlineData("a\0␀b")]
    [InlineData("a␀\0b")]
    [InlineData("a\0")]
    public void Continuous_island_prefix_edit_preserves_embedded_nul_suffix(string source)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new HiddenSource();
        using var document = new Document(source);
        using var island = new WindowsRichEditIsland(fixture.Parent, ThemePolicies.Get(ThemePolicies.DefaultId));
        var frame = new CanvasInteraction(document.Snapshot, 20, 200).Frame();
        island.Bind(new(1, document.Snapshot.Version, 1, document.Snapshot, frame,
            0, source, 0, 0, "NUL probe", "", false));
        CanvasCommittedEdit? committed = null;
        island.EditCommitted += edit => committed = edit;
        AttemptEdits(island.InputHandle, source.Length, () =>
        {
            if (!OperatingSystem.IsWindows()) return;
            typeof(WindowsRichEditIsland).GetMethod("CommitFinalText", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(island, null);
            Assert.Null(committed);
            Assert.Equal(source, document.Snapshot.GetText());
            Assert.True(island.FlushPendingText());
            Assert.Equal(source.Replace('\0', '\u2400'), fixture.Read(island.InputHandle));
        });
    }

    /// <summary>A literal U+2400 without NUL remains ordinary editable source, never decoded as NUL.</summary>
    [Fact]
    public void Literal_control_picture_remains_editable_and_literal()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new HiddenSource();
        var config = Configuration();
        using var controller = new NativeEditorController(fixture.Shell, config,
            ThemePolicies.Get(config.ThemeId), null);
        var document = new Document("a␀b");
        Replace(controller, document);
        var selection = new Win32.CharacterRange { Min = 0, Max = 0 };
        Win32.SendMessageW(fixture.Editor, Win32.EM_EXSETSEL, 0, ref selection);
        Win32.SendMessageW(fixture.Editor, 0x00C2, 1, "␀");
        typeof(WindowsEditorShell).GetMethod("OnTextChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(fixture.Shell, null);
        Assert.Equal("␀a␀b", document.Snapshot.GetText());
        Assert.DoesNotContain('\0', document.Snapshot.GetText());
    }

    /// <summary>Refused native edits and pending-host commit preserve exact UTF-8 file bytes through Save/reopen.</summary>
    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "xUnit1031",
        Justification = "Keep hidden HWND creation, controller callbacks and destruction on one thread; engine I/O does not marshal back to that thread.")]
    public void Refused_native_edits_save_and_reopen_exact_original_bytes()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory = Path.GetFullPath(Path.Combine(".temp", "source-nul-probe", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "source.csv");
        const string source = "\"a\0␀b\",tail\r\nEOF\0";
        var bytes = new System.Text.UTF8Encoding(false, true).GetBytes(source);
        File.WriteAllBytes(path, bytes);
        try
        {
            using var fixture = new HiddenSource();
            var config = Configuration();
            using var controller = new NativeEditorController(fixture.Shell, config,
                ThemePolicies.Get(config.ThemeId), null);
            var document = Document.OpenAsync(path).GetAwaiter().GetResult();
            Replace(controller, document);
            var originalVersion = document.Snapshot.Version;
            AttemptEdits(fixture.Editor, source.Length, () =>
            {
                if (!OperatingSystem.IsWindows()) return;
                typeof(WindowsEditorShell).GetMethod("OnTextChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(fixture.Shell, null);
                Assert.Equal(source, document.Snapshot.GetText());
            });
            Assert.True(fixture.Shell.CommitPendingText());
            Assert.Equal(originalVersion, document.Snapshot.Version);
            Assert.False(document.IsModified);
            document.SaveAsync().GetAwaiter().GetResult();
            Assert.Equal(bytes, File.ReadAllBytes(path));
            using var reopened = Document.OpenAsync(path).GetAwaiter().GetResult();
            Assert.Equal(source, reopened.Snapshot.GetText());
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    /// <summary>Loads defaults from a repository-local nonexistent root without touching the user profile.</summary>
    private static MoteConfiguration Configuration() => MoteConfigLoader.Load(new MoteConfigLoadOptions
    {
        MoteHomeOverride = Path.GetFullPath(Path.Combine(".temp", "source-nul-probe")),
        UseEnvironmentOverride = false
    });

    /// <summary>Uses the real replacement lane while avoiding native file pickers and a visible Run loop.</summary>
    private static void Replace(NativeEditorController controller, Document document) =>
        typeof(NativeEditorController).GetMethod("ReplaceDocument", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(controller, [document, default(Mote.Telemetry.TelemetryMark), 0]);

    /// <summary>A same-display NUL-to-literal transition clears only the NUL guard and resumes safe source editing.</summary>
    [Fact]
    public void Same_display_transition_to_literal_marker_resumes_editing()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new HiddenSource();
        var config = Configuration();
        using var controller = new NativeEditorController(fixture.Shell, config,
            ThemePolicies.Get(config.ThemeId), null);
        Replace(controller, new Document("a\0b"));
        var literal = new Document("a␀b");
        Replace(controller, literal);
        var selection = new Win32.CharacterRange { Min = 0, Max = 0 };
        Win32.SendMessageW(fixture.Editor, Win32.EM_EXSETSEL, 0, ref selection);
        Win32.SendMessageW(fixture.Editor, 0x00C2, 1, "X");
        typeof(WindowsEditorShell).GetMethod("OnTextChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(fixture.Shell, null);
        Assert.Equal("Xa␀b", literal.Snapshot.GetText());
    }

    /// <summary>Continuous rebind from NUL to literal marker resumes exact source callbacks.</summary>
    [Fact]
    public void Continuous_rebind_to_literal_marker_resumes_editing()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new HiddenSource();
        using var nul = new Document("a\0b");
        using var literal = new Document("a␀b");
        using var island = new WindowsRichEditIsland(fixture.Parent, ThemePolicies.Get(ThemePolicies.DefaultId));
        island.Bind(new(1, 0, 1, nul.Snapshot, new CanvasInteraction(nul.Snapshot, 20, 200).Frame(),
            0, "a\0b", 0, 0, "NUL", "", false));
        island.Bind(new(2, 0, 2, literal.Snapshot, new CanvasInteraction(literal.Snapshot, 20, 200).Frame(),
            0, "a␀b", 0, 0, "literal", "", false));
        CanvasCommittedEdit? committed = null;
        island.EditCommitted += edit => committed = edit;
        Win32.SendMessageW(island.InputHandle, 0x00C2, 1, "X");
        typeof(WindowsRichEditIsland).GetMethod("CommitFinalText", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(island, null);
        Assert.NotNull(committed);
        literal.Apply(committed.Value.Change);
        Assert.Equal("Xa␀b", literal.Snapshot.GetText());
    }

    /// <summary>An uncertified source map cannot enable editing merely because the requested text matches its cache.</summary>
    [Fact]
    public void Identical_display_does_not_reenable_uncertified_source_map()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new HiddenSource();
        var view = new NativeDocumentView("normal", "abc", 0, 3, false, "", new(1, 0));
        fixture.Shell.SetDocument(view);
        typeof(WindowsEditorShell).GetField("_sourceMapInstalled", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(fixture.Shell, false);
        fixture.Shell.SetDocument(view);
        var certified = (bool)typeof(WindowsEditorShell)
            .GetField("_sourceMapInstalled", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Shell)!;
        var readOnly = (bool)typeof(WindowsEditorShell)
            .GetField("_sourceInputReadOnly", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Shell)!;
        Assert.True(readOnly || certified && fixture.Read() == "abc");
    }

    /// <summary>A failed source import cannot retain a certified editable map; restoring the host permits recertification.</summary>
    [Fact]
    public void Failed_source_import_is_readonly_until_successful_reimport()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new HiddenSource();
        var editorField = typeof(WindowsEditorShell).GetField("_editor", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var view = new NativeDocumentView("normal", "abc", 0, 3, false, "", new(1, 0));
        editorField.SetValue(fixture.Shell, (nint)(-1));
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
            { if (OperatingSystem.IsWindows()) fixture.Shell.SetDocument(view); });
        }
        finally { editorField.SetValue(fixture.Shell, fixture.Editor); }
        Assert.False((bool)typeof(WindowsEditorShell).GetField("_sourceMapInstalled", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(fixture.Shell)!);
        Assert.True((bool)typeof(WindowsEditorShell).GetField("_sourceInputReadOnly", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(fixture.Shell)!);
        fixture.Shell.SetDocument(view);
        Assert.Equal("abc", fixture.Read());
        Assert.True((bool)typeof(WindowsEditorShell).GetField("_sourceMapInstalled", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(fixture.Shell)!);
    }

    /// <summary>Failed Continuous readback clears binding authority and prevents commit callbacks.</summary>
    [Fact]
    public void Failed_continuous_import_clears_binding_and_refuses_commit()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new HiddenSource();
        using var document = new Document("abc");
        using var island = new WindowsRichEditIsland(fixture.Parent, ThemePolicies.Get(ThemePolicies.DefaultId));
        var inputField = typeof(WindowsRichEditIsland).GetField("_input", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var originalInput = island.InputHandle;
        inputField.SetValue(island, (nint)(-1));
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
            {
                if (OperatingSystem.IsWindows()) island.Bind(new(1, 0, 1, document.Snapshot,
                    new CanvasInteraction(document.Snapshot, 20, 200).Frame(), 0, "abc", 0, 0, "probe", "", false));
            });
            Assert.True(island.IsInputReadOnly);
            Assert.Null(typeof(WindowsRichEditIsland).GetField("_binding", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(island));
        }
        finally { inputField.SetValue(island, originalInput); }
        CanvasCommittedEdit? committed = null;
        island.EditCommitted += edit => committed = edit;
        typeof(WindowsRichEditIsland).GetMethod("CommitFinalText", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(island, null);
        Assert.Null(committed);
        Assert.Equal("abc", document.Snapshot.GetText());
    }

    /// <summary>A late native notification after failed new import cannot certify the prior page as the new source.</summary>
    [Fact]
    public void Failed_new_import_notification_does_not_recertify_old_page()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new HiddenSource();
        var config = Configuration();
        using var controller = new NativeEditorController(fixture.Shell, config,
            ThemePolicies.Get(config.ThemeId), null);
        Replace(controller, new Document("old source"));
        var replacement = new Document("new source");
        var editorField = typeof(WindowsEditorShell).GetField("_editor", BindingFlags.Instance | BindingFlags.NonPublic)!;
        editorField.SetValue(fixture.Shell, (nint)(-1));
        try
        {
            var error = Assert.Throws<TargetInvocationException>(() => Replace(controller, replacement));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
        finally { editorField.SetValue(fixture.Shell, fixture.Editor); }
        typeof(WindowsEditorShell).GetMethod("OnTextChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(fixture.Shell, null);
        Assert.False((bool)typeof(WindowsEditorShell).GetField("_sourceMapInstalled", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(fixture.Shell)!);
        Assert.True((bool)typeof(WindowsEditorShell).GetField("_sourceInputReadOnly", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(fixture.Shell)!);
        Assert.Equal("new source", replacement.Snapshot.GetText());
        Assert.Equal(0, replacement.Snapshot.Version);

        typeof(NativeEditorController).GetMethod("ShowDocument", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(controller, [default(Mote.Telemetry.TelemetryMark)]);
        Assert.Equal("new source", fixture.Read());
        var selection = new Win32.CharacterRange { Min = 0, Max = 0 };
        Win32.SendMessageW(fixture.Editor, Win32.EM_EXSETSEL, 0, ref selection);
        Win32.SendMessageW(fixture.Editor, 0x00C2, 1, "X");
        typeof(WindowsEditorShell).GetMethod("OnTextChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(fixture.Shell, null);
        Assert.Equal("Xnew source", replacement.Snapshot.GetText());
    }

    /// <summary>Native prefix/glyph insertion, deletion across markers, EOF edits and undo/redo stay inert.</summary>
    [SupportedOSPlatform("windows")]
    private static void AttemptEdits(nint control, int length, Action verify)
    {
        foreach (var (start, end, text) in new[]
        { (0, 0, "X"), (1, 1, "␀"), (0, length, ""), (1, length, "Q"), (length, length, "Z") })
        {
            var selection = new Win32.CharacterRange { Min = start, Max = end };
            Win32.SendMessageW(control, Win32.EM_EXSETSEL, 0, ref selection);
            Win32.SendMessageW(control, 0x00C2 /* EM_REPLACESEL */, 1, text);
            verify();
            Win32.SendMessageW(control, 0x0102 /* WM_CHAR */, 'X', 0);
            verify();
            Win32.SendMessageW(control, 0x0100 /* WM_KEYDOWN */, 0x2E /* Delete */, 0);
            verify();
            Win32.SendMessageW(control, 0x00C7 /* EM_UNDO */, 0, 0);
            verify();
            Win32.SendMessageW(control, 0x0454 /* EM_REDO */, 0, 0);
            verify();
        }
    }

    /// <summary>Owns only hidden HWNDs; length-based reads expose actual import truncation.</summary>
    [SupportedOSPlatform("windows")]
    private sealed class HiddenSource : IDisposable
    {
        private readonly nint _library;
        private readonly nint _parent;
        public WindowsEditorShell Shell { get; } = new();
        public nint Editor { get; }
        public nint Parent => _parent;

        public HiddenSource()
        {
            _library = Win32.LoadLibraryW("msftedit.dll");
            _parent = Win32.CreateWindowExW(0, "STATIC", "", 0, 0, 0, 400, 200, 0, 0,
                Win32.GetModuleHandleW(null), 0);
            Editor = Win32.CreateWindowExW(0, "RICHEDIT50W", "", Win32.ES_MULTILINE,
                0, 0, 400, 200, _parent, 0, Win32.GetModuleHandleW(null), 0);
            Assert.NotEqual(0, Editor);
            typeof(WindowsEditorShell).GetField("_window", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Shell, _parent);
            typeof(WindowsEditorShell).GetField("_editor", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Shell, Editor);
        }

        /// <summary>Uses returned character count, never NUL-terminated managed string conversion.</summary>
        public string Read(nint control = 0)
        {
            var buffer = Marshal.AllocHGlobal(4096);
            try
            {
                var request = new Win32.GetTextEx
                { ByteCapacity = 4096, Flags = Win32.GT_USECRLF, CodePage = Win32.CP_UNICODE };
                var count = checked((int)Win32.SendMessageW(control == 0 ? Editor : control, Win32.EM_GETTEXTEX, ref request, buffer));
                return Marshal.PtrToStringUni(buffer, count)!;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        public void Dispose()
        {
            Win32.DestroyWindow(Editor);
            Win32.DestroyWindow(_parent);
            FreeLibrary(_library);
        }

        /// <summary>Releases the module reference acquired only for this hidden-control probe.</summary>
        [DllImport("kernel32.dll")]
        private static extern bool FreeLibrary(nint module);
    }
}
