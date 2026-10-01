using System.Reflection;
using System.Text;
using System.Runtime.InteropServices;
using Mote.Native.Windows;
using Mote.Engine;
using Mote.Native;
using Mote.Formats;
using Mote.Themes;

namespace Mote.Tests;

/// <summary>Actual hidden Windows controls verify same-ID palette replacement without OS mutation.</summary>
public sealed class NativeThemeOverrideWindowsTests
{
    /// <summary>Composed preview colors reach RichEdit while source text, selection and engine-owned undo survive.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Same_id_override_updates_native_background_without_source_or_history_changes(bool semantic)
    {
        if (!OperatingSystem.IsWindows()) return;
        var library = Win32.LoadLibraryW("msftedit.dll");
        Assert.NotEqual(0, library);
        var module = Win32.GetModuleHandleW(null);
        var parent = Win32.CreateWindowExW(0, "STATIC", "", 0, 0, 0, 600, 300, 0, 0, module, 0);
        var editor = Win32.CreateWindowExW(0, "RICHEDIT50W", "", Win32.WS_CHILD | Win32.ES_MULTILINE,
            0, 0, 250, 250, parent, 0, module, 0);
        var preview = Win32.CreateWindowExW(0, "RICHEDIT50W", "", Win32.WS_CHILD | Win32.ES_MULTILINE |
            Win32.ES_READONLY, 250, 0, 250, 250, parent, 0, module, 0);
        var status = Win32.CreateWindowExW(0, "STATIC", "", Win32.WS_CHILD,
            0, 250, 500, 30, parent, 0, module, 0);
        var shell = new WindowsEditorShell();
        using var document = new Document("private 中文😀");
        document.Apply(new TextChange(0, document.Snapshot.Length, "changed 中文😀"));
        shell.UndoRequested += () => Assert.True(document.Undo());
        try
        {
            Assert.NotEqual(0, parent);
            Assert.NotEqual(0, editor);
            Assert.NotEqual(0, preview);
            Assert.NotEqual(0, status);
            Field("_window").SetValue(shell, parent);
            Field("_editor").SetValue(shell, editor);
            Field("_preview").SetValue(shell, preview);
            Field("_status").SetValue(shell, status);
            typeof(WindowsEditorShell).GetMethod("InstallMenu", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(shell, null);
            var menuLabel = new StringBuilder(64);
            Assert.NotEqual(0, GetMenuString(GetMenu(parent), 217, menuLabel, menuLabel.Capacity, 0));
            Assert.Equal("&Reload Settings", menuLabel.ToString());
            var reloadRequests = 0;
            shell.ReloadSettingsRequested += () => reloadRequests++;
            typeof(WindowsEditorShell).GetMethod("HandleCommand", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(shell, [217]);
            Assert.Equal(1, reloadRequests);
            var original = ThemePolicies.Get(ThemePolicies.DarkId);
            AssertOwnedRichEdit(editor);
            AssertOwnedRichEdit(preview);
            shell.SetTheme(original);
            SendText(editor, 0x000C, 0, "private 中文😀"); // WM_SETTEXT resets native undo.
            Win32.SendMessageW(editor, 0x00B1, 0, -1); // EM_SETSEL selects all.
            SendText(editor, 0x00C2, 1, "changed 中文😀"); // EM_REPLACESEL records an undo unit.
            var selected = new Win32.CharacterRange { Min = 2, Max = 7 };
            Win32.SendMessageW(editor, Win32.EM_EXSETSEL, 0, ref selected);
            Assert.NotEqual(0, Win32.SendMessageW(editor, 0x00C6, 0, 0)); // EM_CANUNDO.
            var before = Read(shell, editor);
            var engineSnapshot = document.Snapshot;
            if (semantic)
            {
                var stamp = new NativeDocumentStamp(3, 1);
                Field("_visibleText").SetValue(shell, before);
                Field("_editorOffsets").SetValue(shell, new RichEditOffsetMap(before));
                Field("_styleText").SetValue(shell, before);
                Field("_document").SetValue(shell, new NativeDocumentView("", before, 0, before.Length, true, "", stamp));
                Field("_analysis").SetValue(shell, new NativeAnalysisView([new("string", new TextSpan(0, 7))], "", "preview", "", stamp));
            }
            var beforeBackground = Background(preview, ColorRef(original.Palette.PreviewBackground));
            Assert.Equal(ColorRef(original.Palette.PreviewBackground), beforeBackground);
            Assert.True(ThemeOverrideData.TryCreate(
                [new KeyValuePair<string, string>("preview.background", "#101820")], out var data, out var issues));
            Assert.Empty(issues);
            var composed = ThemeComposer.Compose(original, data);
            Assert.Empty(composed.Issues);
            Assert.Equal(original.Id, composed.Theme.Id);
            AssertOwnedRichEdit(editor);
            AssertOwnedRichEdit(preview);
            shell.SetTheme(composed.Theme);
            Assert.Equal(ColorRef(composed.Theme.Palette.PreviewBackground),
                Background(preview, ColorRef(composed.Theme.Palette.PreviewBackground)));
            Assert.NotEqual(beforeBackground, ColorRef(composed.Theme.Palette.PreviewBackground));
            Assert.Equal(before, Read(shell, editor));
            var afterSelection = new Win32.CharacterRange();
            Win32.SendMessageW(editor, Win32.EM_EXGETSEL, 0, ref afterSelection);
            Assert.Equal(selected.Min, afterSelection.Min);
            Assert.Equal(selected.Max, afterSelection.Max);
            if (semantic)
            {
                // The CI access violation occurred in this literal-preview import path.
                // Exercise balanced native/TOM leases and marshalled payload lifetime
                // repeatedly without replacing the handles, source or undo branch.
                for (var iteration = 0; iteration < 8; iteration++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    AssertOwnedRichEdit(editor);
                    AssertOwnedRichEdit(preview);
                    shell.SetTheme(iteration % 2 == 0 ? original : composed.Theme);
                    Assert.Equal(before, Read(shell, editor));
                    Assert.Equal("preview", Read(shell, preview));
                    Win32.SendMessageW(editor, Win32.EM_EXGETSEL, 0, ref afterSelection);
                    Assert.Equal(selected.Min, afterSelection.Min);
                    Assert.Equal(selected.Max, afterSelection.Max);
                }
            }
            Assert.NotEqual(0, Win32.SendMessageW(editor, 0x00C6, 0, 0));
            Assert.NotEqual(0, Win32.SendMessageW(editor, 0x00C7, 0, 0)); // EM_UNDO must undo text, not palette formatting.
            Assert.Equal("private 中文😀", Read(shell, editor));
            // A real native edit invalidates its previous same-text analysis; mirror that for this hidden fixture.
            Field("_analysis").SetValue(shell, null);
            shell.SetTheme(original); // A palette update also preserves an existing redo branch.
            Assert.Equal("private 中文😀", Read(shell, editor));
            Assert.NotEqual(0, Win32.SendMessageW(editor, 0x0400 + 84, 0, 0)); // EM_REDO restores the same unit.
            Assert.Equal(before, Read(shell, editor));
            var scope = (WindowsRichEditUndoScope)Field("_editorUndo").GetValue(shell)!;
            Assert.Throws<InvalidOperationException>((Action)(() =>
            {
                if (!OperatingSystem.IsWindows()) return;
                using var outer = scope.Suspend(editor);
                using var inner = scope.Suspend(editor);
                throw new InvalidOperationException("injected formatting failure");
            }));
            Win32.SendMessageW(editor, 0x00B1, 0, -1);
            SendText(editor, 0x00C2, 1, "later edit");
            Assert.NotEqual(0, Win32.SendMessageW(editor, 0x00C7, 0, 0));
            Assert.Equal(before, Read(shell, editor)); // Recording resumed after nested exceptional exit.
            Assert.Same(engineSnapshot, document.Snapshot);
            Assert.True(document.CanUndo);
            // Production Undo is an engine request, not RichEdit EM_UNDO (formatting has native undo units).
            typeof(WindowsEditorShell).GetMethod("HandleCommand", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(shell, [206]);
            Assert.Equal("private 中文😀", document.Snapshot.GetText());
            Assert.True(document.CanRedo);
            Assert.False(document.CanUndo);
        }
        finally
        {
            if (parent != 0) Win32.DestroyWindow(parent);
            foreach (var name in new[] { "_editorFont", "_uiFont", "_statusBrush" })
                if (Field(name).GetValue(shell) is nint resource && resource != 0) Win32.DeleteObject(resource);
            FreeLibrary(library);
        }
    }

    /// <summary>Missing TOM fails before any native palette or resource mutation.</summary>
    [Fact]
    public void Unavailable_undo_interface_rejects_palette_before_mutation()
    {
        if (!OperatingSystem.IsWindows()) return;
        var control = Win32.CreateWindowExW(0, "STATIC", "", 0, 0, 0, 100, 100, 0, 0,
            Win32.GetModuleHandleW(null), 0);
        try
        {
            Assert.NotEqual(0, control);
            var shell = new WindowsEditorShell();
            Field("_window").SetValue(shell, control);
            Field("_editor").SetValue(shell, control);
            var original = Field("_theme").GetValue(shell);
            Assert.Throws<InvalidOperationException>(() =>
            {
                if (OperatingSystem.IsWindows()) shell.SetTheme(ThemePolicies.Get(ThemePolicies.LightId));
            });
            Assert.Same(original, Field("_theme").GetValue(shell));
            Assert.Equal((nint)0, Field("_statusBrush").GetValue(shell));
            Assert.Equal((nint)0, Field("_editorFont").GetValue(shell));
        }
        finally { if (control != 0) Win32.DestroyWindow(control); }
    }

    /// <summary>The documented setter returns the previous native background; writing the same value is inert.</summary>
    private static uint Background(nint control, uint expected) =>
        unchecked((uint)(long)Win32.SendMessageW(control, Win32.EM_SETBKGNDCOLOR, 0, (nint)expected));
    private static uint ColorRef(ThemeColor color) => (uint)(color.Red | color.Green << 8 | color.Blue << 16);
    /// <summary>Distinguishes invalid, foreign-thread and wrong-class fixture handles before native import.</summary>
    /// <remarks>These owner-thread diagnostics do not claim IsWindow is a cross-thread lifetime lock.</remarks>
    private static void AssertOwnedRichEdit(nint control)
    {
        Assert.Equal(GetCurrentThreadId(), GetWindowThreadProcessId(control, out var process));
        Assert.Equal((uint)Environment.ProcessId, process);
        var name = new StringBuilder(64);
        Assert.NotEqual(0, GetClassName(control, name, name.Capacity));
        Assert.Equal("RICHEDIT50W", name.ToString());
    }
    private static FieldInfo Field(string name) => typeof(WindowsEditorShell).GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static string Read(WindowsEditorShell shell, nint control) =>
        (string)typeof(WindowsEditorShell).GetMethod("ReadControlText", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(shell, [control])!;
    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    private static extern nint SendText(nint control, uint message, nuint wParam, string text);
    [DllImport("user32.dll")]
    private static extern nint GetMenu(nint window);
    [DllImport("user32.dll", EntryPoint = "GetMenuStringW", CharSet = CharSet.Unicode)]
    private static extern int GetMenuString(nint menu, uint item, StringBuilder text, int capacity, uint flags);
    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(nint library);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint control, out uint process);
    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint control, StringBuilder name, int capacity);
}
