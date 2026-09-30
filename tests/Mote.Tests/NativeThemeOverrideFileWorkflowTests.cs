using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Mote.Configuration;
using Mote.Engine;
using Mote.Native;
using Mote.Native.Windows;
using Mote.Themes;

namespace Mote.Tests;

/// <summary>Real TOML reads and production Windows/controller transactions with hidden native controls.</summary>
public sealed class NativeThemeOverrideFileWorkflowTests
{
    /// <summary>File-backed startup/reload is atomic, recoverable, and never rewrites source or history.</summary>
    [Fact]
    public void Real_file_reload_retains_native_state_on_half_write_and_read_failure_then_recovers()
    {
        if (OperatingSystem.IsWindows()) RunWindowsWorkflow();
    }

    /// <summary>Windows-only orchestration keeps platform guards visible to analyzer closures.</summary>
    [SupportedOSPlatform("windows")]
    private static void RunWindowsWorkflow()
    {
        using var temp = new RepoTemp();
        var options = new MoteConfigLoadOptions { UserHomeDirectory = temp.Path, UseEnvironmentOverride = false };
        var defaults = MoteConfigLoader.Load(options);
        Assert.Equal(Path.Combine(temp.Path, ".mote"), defaults.HomeDirectory);
        Directory.CreateDirectory(defaults.HomeDirectory);
        Write(defaults.ConfigPath, "#181818");
        var startup = MoteConfigLoader.Load(options);
        Assert.Equal(ConfigReadDisposition.Loaded, startup.ReadDisposition);
        Assert.Empty(startup.Diagnostics);
        using var native = new HiddenNative();
        using var controller = new NativeEditorController(native.Shell, startup,
            ThemePolicies.Get(ThemePolicies.DarkId), null);
        native.Show();
        native.AssertBackground("#181818");
        native.Edit("private 中文😀 source");
        native.Select(2, 7);
        var document = (Document)ControllerField("_document").GetValue(controller)!;
        var snapshot = document.Snapshot;
        Assert.NotEqual(0, Win32.SendMessageW(native.Editor, 0x00C6, 0, 0)); // EM_CANUNDO.
        Assert.True(document.CanUndo);
        Assert.False(document.CanRedo);
        var view = native.View;
        var selection = native.Selection;
        var originalThemeId = native.Theme.Id;

        Write(startup.ConfigPath, "#101820");
        native.Reload();
        native.PumpUntil(() => native.Theme.Palette.PreviewBackground.ToHex() == "#101820");
        Assert.Equal(originalThemeId, native.Theme.Id);
        native.AssertBackground("#101820");
        AssertUnchanged();

        // Deliberately leave a real, syntactically incomplete file; no substitute loader result.
        File.WriteAllText(startup.ConfigPath, "[appearance.colors]\n'preview.background' = '");
        native.Reload();
        native.PumpUntil(() => native.Notice?.Contains("reload rejected") == true);
        Assert.Contains("CONFIG_", native.Notice!);
        AssertRejectedRemainsPersistent();

        Write(startup.ConfigPath, "#121A22");
        using (var lockFile = new FileStream(startup.ConfigPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Equal(ConfigReadDisposition.Rejected, MoteConfigLoader.Load(options).ReadDisposition);
            var previous = native.Notice;
            native.Reload();
            native.PumpUntil(() => native.Notice?.Contains("CONFIG_READ") == true && native.Notice != previous);
            AssertRejectedRemainsPersistent();
        }
        native.Reload();
        native.PumpUntil(() => native.Theme.Palette.PreviewBackground.ToHex() == "#121A22" && native.Notice is null);
        native.AssertBackground("#121A22");
        AssertUnchanged();

        Write(startup.ConfigPath, "#121A22", paths: true);
        native.Reload();
        native.PumpUntil(() => native.Notice?.Contains("next launch") == true);
        var live = (MoteConfiguration)ControllerField("_configuration").GetValue(controller)!;
        Assert.Equal(startup.CacheDirectory, live.CacheDirectory);
        Assert.Equal(startup.TraceDirectory, live.TraceDirectory);
        Assert.False(live.TraceEnabled);
        var nextLaunch = MoteConfigLoader.Load(options);
        Assert.Equal(Path.Combine(startup.HomeDirectory, "next-cache"), nextLaunch.CacheDirectory);
        Assert.Equal(Path.Combine(startup.HomeDirectory, "next-traces"), nextLaunch.TraceDirectory);
        Assert.True(nextLaunch.TraceEnabled);
        Assert.False(Directory.Exists(nextLaunch.CacheDirectory));
        Assert.False(Directory.Exists(nextLaunch.TraceDirectory));
        AssertUnchanged();
        Assert.NotEqual(0, Win32.SendMessageW(native.Editor, 0x00C7, 0, 0)); // Native EM_UNDO is still the text unit.
        Assert.Equal("", native.Read(native.Editor));
        Assert.NotEqual(0, Win32.SendMessageW(native.Editor, 0x0400 + 84, 0, 0)); // EM_REDO.
        Assert.Equal(snapshot.GetText(), native.Read(native.Editor));
        native.Command(206); // Production Undo command, engine-owned history.
        Assert.Equal("", document.Snapshot.GetText());
        Assert.True(document.CanRedo);
        native.Command(207);
        Assert.Equal(snapshot.GetText(), document.Snapshot.GetText());

        void AssertUnchanged()
        {
            Assert.Same(snapshot, document.Snapshot);
            Assert.Equal(view.Stamp, native.View.Stamp);
            Assert.Equal(view.Text, native.Read(native.Editor));
            Assert.Equal(selection, native.Selection);
            Assert.NotEqual(0, Win32.SendMessageW(native.Editor, 0x00C6, 0, 0));
            Assert.True(document.CanUndo);
            Assert.False(document.CanRedo);
        }
        void AssertRejectedRemainsPersistent()
        {
            native.AssertBackground("#101820");
            var notice = native.Notice;
            Assert.Contains(notice!, native.Read(native.Status));
            native.Shell.SetDocument(native.View with { Status = "ordinary status refresh" });
            native.Appearance();
            Assert.Equal(notice, native.Notice);
            Assert.Contains(notice!, native.Read(native.Status));
            AssertUnchanged();
        }
    }

    /// <summary>Fixture configuration uses conventional home and relative next-launch directories.</summary>
    private static void Write(string path, string color, bool paths = false) => File.WriteAllText(path,
        "[appearance]\ntheme = 'mote-dark'\n[appearance.colors]\n'preview.background' = '" + color + "'\n" +
        (paths ? "[paths]\ncache = 'next-cache'\ntraces = 'next-traces'\n[telemetry]\nenabled = true\n" : ""));
    private static FieldInfo ControllerField(string name) => typeof(NativeEditorController).GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>
    /// Hidden HWND harness uses production command, edit and WM_APP handlers on one thread.
    /// Reflection replaces only window creation/event-loop startup; parsing and application are real.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private sealed class HiddenNative : IDisposable
    {
        private nint _library;
        private nint _window;
        /// <summary>Production adapter under test, without a visible top-level window.</summary>
        internal WindowsEditorShell Shell { get; } = new();
        internal nint Editor => (nint)Field("_editor").GetValue(Shell)!;
        internal nint Status => (nint)Field("_status").GetValue(Shell)!;
        internal NativeDocumentView View => (NativeDocumentView)Field("_document").GetValue(Shell)!;
        internal IThemePolicy Theme => (IThemePolicy)Field("_theme").GetValue(Shell)!;
        internal string? Notice => (string?)Field("_statusNotice").GetValue(Shell);
        internal (int, int) Selection
        {
            get
            {
                var range = new Win32.CharacterRange();
                Win32.SendMessageW(Editor, Win32.EM_EXGETSEL, 0, ref range);
                return (range.Min, range.Max);
            }
        }

        /// <summary>Creates and owns all handles even if setup fails part way through.</summary>
        internal HiddenNative()
        {
            try
            {
                _library = Win32.LoadLibraryW("msftedit.dll");
                Assert.NotEqual(0, _library);
                var module = Win32.GetModuleHandleW(null);
                _window = Win32.CreateWindowExW(0, "STATIC", "", 0, 0, 0, 600, 300, 0, 0, module, 0);
                Assert.NotEqual(0, _window);
                Field("_window").SetValue(Shell, _window);
                foreach (var (name, klass, readOnly) in new[] {
                    ("_editor", "RICHEDIT50W", false), ("_preview", "RICHEDIT50W", true), ("_status", "STATIC", false) })
                {
                    var control = Win32.CreateWindowExW(0, klass, "", Win32.WS_CHILD | (klass == "RICHEDIT50W" ? Win32.ES_MULTILINE : 0) |
                        (readOnly ? Win32.ES_READONLY : 0), 0, 0, 250, 250, _window, 0, module, 0);
                    Assert.NotEqual(0, control);
                    Field(name).SetValue(Shell, control);
                }
                Invoke("InstallMenu");
            }
            catch { Dispose(); throw; }
        }
        internal void Show() => ((Action)Field("Shown").GetValue(Shell)!)();
        internal void Appearance() => ((Action)Field("AppearanceChanged").GetValue(Shell)!)();
        internal void Reload() => Command(217);
        internal void Command(int id) => Message(Win32.WM_COMMAND, (nuint)id);
        internal void Edit(string text)
        {
            SendText(Editor, 0x00C2, 1, text); // EM_REPLACESEL is a genuine RichEdit undoable edit.
            Invoke("OnTextChanged"); // Deliver its parent notification without a registered visible shell.
        }
        internal void Select(int start, int end)
        {
            Shell.SetSelection(start, end);
            Invoke("FlushSelection");
        }
        internal void PumpUntil(Func<bool> condition) => Assert.True(SpinWait.SpinUntil(() =>
        {
            Message(Win32.WM_APP, 0);
            return condition();
        }, TimeSpan.FromSeconds(10)), "Native file-backed reload did not reach its expected state.");
        internal string Read(nint handle)
        {
            var text = new StringBuilder(GetWindowTextLength(handle) + 1);
            GetWindowText(handle, text, text.Capacity);
            return text.ToString();
        }
        internal void AssertBackground(string hex)
        {
            var expected = Convert.ToUInt32(hex[1..], 16);
            var colorRef = (expected & 0xff) << 16 | (expected & 0xff00) | expected >> 16;
            var preview = (nint)Field("_preview").GetValue(Shell)!;
            var previous = unchecked((uint)(long)Win32.SendMessageW(preview, Win32.EM_SETBKGNDCOLOR, 0, (nint)colorRef));
            Assert.Equal(colorRef, previous); // Same-value setter returns the installed native color.
        }
        private void Message(uint message, nuint wParam) => Invoke("WindowMessage", _window, message, wParam, (nint)0);
        private void Invoke(string name, params object?[] args) => typeof(WindowsEditorShell).GetMethod(name,
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Shell, args);
        private static FieldInfo Field(string name) => typeof(WindowsEditorShell).GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        /// <summary>Releases owned child windows and palette resources; no process/system state is touched.</summary>
        public void Dispose()
        {
            if (_window != 0) { Win32.DestroyWindow(_window); _window = 0; }
            foreach (var name in new[] { "_editorFont", "_uiFont", "_statusBrush" })
                if (Field(name).GetValue(Shell) is nint resource && resource != 0)
                { Win32.DeleteObject(resource); Field(name).SetValue(Shell, (nint)0); }
            if (_library != 0) { FreeLibrary(_library); _library = 0; }
        }
        [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
        private static extern nint SendText(nint control, uint message, nuint wParam, string text);
        [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(nint window);
        [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(nint window, StringBuilder text, int capacity);
        [DllImport("kernel32.dll")]
        private static extern bool FreeLibrary(nint library);
    }
}
