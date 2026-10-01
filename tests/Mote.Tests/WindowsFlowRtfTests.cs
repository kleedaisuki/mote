using System.Reflection;
using System.Runtime.InteropServices;
using Mote.Native;
using Mote.Formats;
using Mote.Native.Windows;
using Mote.Themes;

namespace Mote.Tests;

/// <summary>Pure serialization contracts for inert bounded native Flow rich text.</summary>
public sealed class WindowsFlowRtfTests
{
    /// <summary>RTF metacharacters and UTF-16 surrogate halves remain literal display text.</summary>
    [Fact]
    public void Escape_unicode_and_rtf_syntax_without_emitting_active_destinations()
    {
        const string text = "中文😀 {\\field} https://example.com\n\tend";
        var rtf = WindowsFlowRtf.Build(Flow(text), ThemePolicies.Get(ThemePolicies.DefaultId));
        Assert.Contains("\\u20013?", rtf);
        Assert.Contains("\\u-10179?\\u-8704?", rtf);
        Assert.Contains(@"\{\\field\}", rtf);
        Assert.Contains("\\par\n\\pard", rtf);
        Assert.Contains("\\tab ", rtf);
        Assert.DoesNotContain(@"{\field", rtf);
        Assert.DoesNotContain("\\object", rtf);
        Assert.DoesNotContain("\\pict", rtf);
        Assert.DoesNotContain("\\ul", rtf);
    }

    /// <summary>Flat flags and semantic paragraph layout are deterministic and reset between runs.</summary>
    [Fact]
    public void Theme_layout_and_inline_flags_are_flattened_without_selection_operations()
    {
        const string text = "Heading\nstrong code\nquote";
        var flow = new FlowRenderProjection(1, text,
            [new(new(8, 6), new(20, 6), "text", FlowInlineStyle.Strong, RenderOriginPrecision.ExactText),
             new(new(15, 4), new(30, 4), "code", FlowInlineStyle.Code | FlowInlineStyle.Emphasis, RenderOriginPrecision.ExactText)],
            [new(new(0, 7), new(0, 9), "heading", 1),
             new(new(8, 11), new(20, 14), "paragraph"),
             new(new(20, 5), new(40, 7), "quote", Depth: 1)], false, AnalysisCompleteness.Complete);
        var rtf = WindowsFlowRtf.Build(flow, ThemePolicies.Get(ThemePolicies.DefaultId));
        Assert.Contains("\\b ", rtf);
        Assert.Contains("\\i \\f1 ", rtf);
        Assert.Contains("\\b0 ", rtf);
        Assert.Contains("\\li480", rtf);
        Assert.Contains("\\fs38", rtf); // Default 12-point UI body scaled for h1.
    }

    /// <summary>A paragraph cannot begin inside a displayed native paragraph.</summary>
    [Fact]
    public void Non_boundary_paragraph_is_rejected_before_install()
    {
        var flow = new FlowRenderProjection(1, "abcd", [], [new(new(1, 2), new(1, 2), "heading", 1)],
            false, AnalysisCompleteness.Complete);
        Assert.False(WindowsFlowRtf.IsValid(flow));
        Assert.Throws<ArgumentException>(() => WindowsFlowRtf.Build(flow, ThemePolicies.Get(ThemePolicies.DefaultId)));
    }

    /// <summary>Embedded NUL cannot silently truncate native installation while retaining a map.</summary>
    [Fact]
    public void Embedded_nul_is_not_installable()
    {
        Assert.False(WindowsFlowRtf.IsValid(Flow("a\0b")));
    }

    /// <summary>CRLF emits one paragraph delimiter, not an extra empty paragraph.</summary>
    [Fact]
    public void CrLf_is_one_rtf_paragraph()
    {
        var rtf = WindowsFlowRtf.Build(Flow("a\r\nb"), ThemePolicies.Get(ThemePolicies.DefaultId));
        Assert.Equal(1, rtf.Split("\\par\n").Length - 1);
    }

    /// <summary>Actual hidden RichEdit imports preserve Unicode, native selection and retained identity.</summary>
    /// <remarks>No focus changes, foreground window, clipboard writes, or physical input are used.</remarks>
    [Fact]
    public void Windows_hidden_Hwnd_installs_flow_and_preserves_same_text_selection()
    {
        if (!OperatingSystem.IsWindows()) return;
        var library = Win32.LoadLibraryW("msftedit.dll");
        Assert.NotEqual(0, library);
        var control = Win32.CreateWindowExW(0, "RICHEDIT50W", "",
            Win32.ES_MULTILINE | Win32.ES_READONLY | Win32.WS_VSCROLL,
            0, 0, 400, 100, 0, 0, Win32.GetModuleHandleW(null), 0);
        try
        {
            Assert.NotEqual(0, control);
            var shell = new WindowsEditorShell();
            Field("_preview").SetValue(shell, control);
            var text = "Heading 中文😀\n" + string.Join('\n', Enumerable.Range(0, 35).Select(i => $"line {i}"));
            var flow = new FlowRenderProjection(1, text,
                [new(new(0, 7), new(0, 7), "heading", FlowInlineStyle.Strong, RenderOriginPrecision.ExactText)],
                [new(new(0, 12), new(0, 12), "heading", 1)], false, AnalysisCompleteness.Complete);
            var view = new NativeAnalysisView([], "", text, "", new(7, 1), PresentationSequence: 3, Flow: flow);
            Assert.True(WindowsFlowRtf.IsValid(flow));
            Install(shell, view);
            Assert.Equal(text.Replace("\n", "\r\n"), Read(shell, control));
            Assert.Equal(view.Identity, Field("_previewIdentity").GetValue(shell));
            var selection = new Win32.CharacterRange { Min = 0, Max = 7 };
            Win32.SendMessageW(control, Win32.EM_EXSETSEL, 0, ref selection);
            var format = new Win32.CharacterFormat { Size = (uint)Marshal.SizeOf<Win32.CharacterFormat>(), FaceName = "" };
            Win32.SendMessageW(control, Win32.EM_GETCHARFORMAT, Win32.SCF_SELECTION, ref format);
            Assert.NotEqual(0u, format.Effects & Win32.CFE_BOLD);
            Assert.Equal(380, format.Height);
            var copyPayload = Marshal.AllocHGlobal(64);
            try
            {
                // EM_GETSELTEXT obtains the native text Copy would consume without writing the clipboard.
                SendPointer(control, 0x0400 + 62, 0, copyPayload);
                Assert.Equal("Heading", Marshal.PtrToStringUni(copyPayload));
            }
            finally { Marshal.FreeHGlobal(copyPayload); }
            Win32.SendMessageW(control, Win32.EM_LINESCROLL, 0, 5);
            var scroll = new Win32.Point();
            Win32.SendMessageW(control, Win32.EM_GETSCROLLPOS, 0, ref scroll);
            shell.SetTheme(ThemePolicies.Get(ThemePolicies.LightId));
            Install(shell, view with { PresentationSequence = 4 });
            var retained = new Win32.CharacterRange();
            Win32.SendMessageW(control, Win32.EM_EXGETSEL, 0, ref retained);
            Assert.Equal(selection.Min, retained.Min);
            Assert.Equal(selection.Max, retained.Max);
            var afterScroll = new Win32.Point();
            Win32.SendMessageW(control, Win32.EM_GETSCROLLPOS, 0, ref afterScroll);
            Assert.Equal(scroll.X, afterScroll.X);
            Assert.Equal(scroll.Y, afterScroll.Y);
            Assert.Equal(text.Replace("\n", "\r\n"), Read(shell, control));
            selection = new Win32.CharacterRange { Min = 2, Max = 2 };
            Win32.SendMessageW(control, Win32.EM_EXSETSEL, 0, ref selection);
            NativePreviewActivation? activation = null;
            shell.PreviewActivated += value => activation = value;
            Method("ActivatePreview").Invoke(shell, null);
            Assert.Equal(new NativePreviewActivation(view.Stamp, 2, 4), activation);
            Install(shell, view with { Flow = new FlowRenderProjection(2, text, [], [], false, AnalysisCompleteness.Complete) });
            Assert.Null(Field("_previewIdentity").GetValue(shell));
        }
        finally
        {
            if (control != 0) Win32.DestroyWindow(control);
            FreeLibrary(library);
        }
    }

    /// <summary>Untrusted legacy preview text is literal even when it begins with an RTF header.</summary>
    [Theory]
    [InlineData(@"{\rtf1\ansi injected}")]
    [InlineData("{\\rtf1\\ansi 中文😀}\nnext")]
    public void Windows_hidden_Hwnd_plain_fallback_never_parses_rtf(string text)
    {
        if (!OperatingSystem.IsWindows()) return;
        var library = Win32.LoadLibraryW("msftedit.dll");
        var control = Win32.CreateWindowExW(0, "RICHEDIT50W", "",
            Win32.ES_MULTILINE | Win32.ES_READONLY, 0, 0, 400, 100, 0, 0, Win32.GetModuleHandleW(null), 0);
        try
        {
            Assert.NotEqual(0, control);
            var shell = new WindowsEditorShell();
            Field("_preview").SetValue(shell, control);
            var view = new NativeAnalysisView([], "", text, "", new(7, 1), PresentationSequence: 5);
            Install(shell, view);
            Assert.Equal(text.Replace("\n", "\r\n"), Read(shell, control));
            Assert.Equal(view.Identity, Field("_previewIdentity").GetValue(shell));
            Method("ClearPreview").Invoke(shell, null);
            Assert.Equal("", Read(shell, control));
            Assert.Null(Field("_previewIdentity").GetValue(shell));
        }
        finally
        {
            if (control != 0) Win32.DestroyWindow(control);
            FreeLibrary(library);
        }
    }

    /// <summary>A synchronous native visibility callback cannot let an outer analysis overwrite a newer install.</summary>
    [Fact]
    public void Windows_hidden_Hwnd_reentrant_layout_keeps_newer_presentation()
    {
        if (!OperatingSystem.IsWindows()) return;
        var library = Win32.LoadLibraryW("msftedit.dll");
        var parent = Win32.CreateWindowExW(0, "STATIC", "", 0, 0, 0, 600, 300, 0, 0, Win32.GetModuleHandleW(null), 0);
        var editor = Win32.CreateWindowExW(0, "RICHEDIT50W", "", Win32.WS_CHILD | Win32.ES_MULTILINE,
            0, 0, 100, 100, parent, 0, Win32.GetModuleHandleW(null), 0);
        var preview = Win32.CreateWindowExW(0, "RICHEDIT50W", "", Win32.WS_CHILD | Win32.WS_VISIBLE |
            Win32.ES_MULTILINE | Win32.ES_READONLY, 100, 0, 100, 100, parent, 0, Win32.GetModuleHandleW(null), 0);
        var nested = false;
        Exception? callbackFailure = null;
        var shell = new WindowsEditorShell();
        var stamp = new NativeDocumentStamp(9, 1);
        var first = new NativeAnalysisView([], "", "old", "", stamp, PresentationSequence: 1, ShowPreview: false);
        var newer = first with { PreviewText = "newer", PresentationSequence = 2 };
        Win32.SubclassProcedure callback = (window, message, wParam, lParam, id, data) =>
        {
            if (!nested && message == 0x0018) // WM_SHOWWINDOW is synchronous inside SetAnalysis layout.
            {
                nested = true;
                try { if (OperatingSystem.IsWindows()) shell.SetAnalysis(newer); }
                catch (Exception ex) { callbackFailure = ex; }
            }
            return Win32.DefSubclassProc(window, message, wParam, lParam);
        };
        try
        {
            Assert.NotEqual(0, parent);
            Assert.NotEqual(0, editor);
            Assert.NotEqual(0, preview);
            Field("_window").SetValue(shell, parent);
            Field("_editor").SetValue(shell, editor);
            Field("_preview").SetValue(shell, preview);
            Field("_document").SetValue(shell, new NativeDocumentView("", "", 0, 0, false, "", stamp));
            Assert.True(Win32.SetWindowSubclass(preview, callback, 91, 0));
            shell.SetAnalysis(first);
            Assert.True(nested);
            Assert.Null(callbackFailure);
            Assert.Same(newer, Field("_analysis").GetValue(shell));
            Assert.Equal(newer.Identity, Field("_previewIdentity").GetValue(shell));
            Assert.Equal("newer", Read(shell, preview));
        }
        finally
        {
            if (parent != 0) Win32.DestroyWindow(parent);
            GC.KeepAlive(callback);
            FreeLibrary(library);
        }
    }

    private static FieldInfo Field(string name) => typeof(WindowsEditorShell).GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static MethodInfo Method(string name) => typeof(WindowsEditorShell).GetMethod(name,
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)!;
    private static void Install(WindowsEditorShell shell, NativeAnalysisView view) =>
        Method("InstallPreview").Invoke(shell, [view]);
    private static string Read(WindowsEditorShell shell, nint control) =>
        (string)Method("ReadControlText").Invoke(shell, [control])!;
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendPointer(nint control, int message, nuint wParam, nint lParam);
    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(nint library);

    private static FlowRenderProjection Flow(string text) => new(1, text, [], [], false,
        AnalysisCompleteness.Complete);
}
