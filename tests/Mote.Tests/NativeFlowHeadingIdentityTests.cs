using Mote.Engine;
using Mote.Formats;
using Mote.Native;
using Mote.Native.Windows;
using Mote.Themes;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Mote.Tests;

/// <summary>Verifies block identity survives the real Markdown-to-native Flow adapter.</summary>
public sealed class NativeFlowHeadingIdentityTests
{
    /// <summary>Heading ordinary text inherits identity while explicit inline roles remain distinct.</summary>
    [Theory]
    [InlineData("# Plain\n", 1)]
    [InlineData("## **Strong** and *gentle*\n", 2)]
    [InlineData("### Plain `code` [link](https://example.com)\n", 3)]
    [InlineData("Plain 😀\n========\n", 1)]
    [InlineData("> ## Quoted\n", 2)]
    [InlineData("- ## Listed\n", 2)]
    public async Task Markdown_heading_preserves_block_and_inline_contracts(string source, int level)
    {
        using var document = new Document(source);
        using var driver = new NativeFormatSessionDriver(
            (IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Markdown));
        var result = await driver.AnalyzePresentationAsync(document.Snapshot,
            new AnalysisRequest(new TextSpan(0, source.Length), AnalysisScope.Full), default);
        var flow = result.Preview.Flow!;
        var heading = Assert.Single(flow.Paragraphs, paragraph => paragraph.Kind == "heading");
        Assert.Equal(level, heading.Level);
        var originalRuns = flow.Runs.ToArray();
        var preview = NativePreviewBuilder.FromFlow(flow);
        Assert.Same(flow, preview.Flow);
        Assert.Equal(flow.Text, preview.Text);
        Assert.Equal(originalRuns, flow.Runs);
        Assert.Equal(flow.Runs.Count, preview.Spans.Count);
        Assert.Contains(preview.Spans, span => span.Kind == "heading");
        for (var i = 0; i < flow.Runs.Count; i++)
        {
            var run = flow.Runs[i];
            var span = preview.Spans[i];
            Assert.Equal(run.DisplayRange.Start, span.Start);
            Assert.Equal(run.DisplayRange.Length, span.Length);
            Assert.Equal(run.Navigable, span.Navigable);
            Assert.Equal(heading.SourceRange, span.SourceSpan);
            Assert.True(span.Emphasis);
            Assert.Equal(run.Role is "text" or "paragraph" ? "heading" : run.Role, span.Kind);
            if (run.Precision == RenderOriginPrecision.ExactText)
                Assert.Equal(source.Substring(run.SourceRange.Start, run.SourceRange.Length),
                    flow.Text.Substring(run.DisplayRange.Start, run.DisplayRange.Length));
        }
        var theme = ThemePolicies.Get(ThemePolicies.DefaultId);
        var accent = theme.Palette.Accent;
        var rtf = WindowsFlowRtf.Build(flow, theme);
        Assert.Contains($"\\red{accent.Red}\\green{accent.Green}\\blue{accent.Blue};", rtf);
    }

    /// <summary>Non-heading blocks are differential controls: every original run role and origin survives.</summary>
    [Theory]
    [InlineData("Body **strong** and `code`.\n")]
    [InlineData("- First\n- Second\n")]
    [InlineData("```text\ncode {literal}\n```\n")]
    public async Task Non_heading_blocks_preserve_every_run(string source)
    {
        using var document = new Document(source);
        using var driver = new NativeFormatSessionDriver(
            (IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Markdown));
        var result = await driver.AnalyzePresentationAsync(document.Snapshot,
            new AnalysisRequest(new TextSpan(0, source.Length), AnalysisScope.Full), default);
        var flow = result.Preview.Flow!;
        Assert.DoesNotContain(flow.Paragraphs, paragraph => paragraph.Kind == "heading");
        var preview = NativePreviewBuilder.FromFlow(flow);
        for (var i = 0; i < flow.Runs.Count; i++)
        {
            var run = flow.Runs[i];
            Assert.Equal(run.Role, preview.Spans[i].Kind);
            Assert.Equal(run.SourceRange, preview.Spans[i].SourceSpan);
            Assert.Equal((run.Style & FlowInlineStyle.Strong) != 0, preview.Spans[i].Emphasis);
        }
    }

    /// <summary>A crossing run has no heading owner, even if its first character lies in a heading.</summary>
    [Fact]
    public void Crossing_run_does_not_inherit_partial_heading_identity()
    {
        var run = new FlowRun(new(0, 5), new(0, 5), "text", FlowInlineStyle.None,
            RenderOriginPrecision.ExactText);
        var flow = new FlowRenderProjection(0, "abcde", [run],
            [new(new(0, 3), new(0, 3), "heading", 1)], false, AnalysisCompleteness.Complete);
        Assert.Null(NativeFlowPresentation.ContainingParagraph(flow, run));
        Assert.Equal("text", Assert.Single(NativePreviewBuilder.FromFlow(flow).Spans).Kind);
    }

    /// <summary>Both native color adapters use this block-aware role, with inline precedence intact.</summary>
    [Theory]
    [InlineData("text", "heading")]
    [InlineData("paragraph", "heading")]
    [InlineData("code", "code")]
    [InlineData("link", "link")]
    [InlineData("list-marker", "list-marker")]
    [InlineData("notice", "notice")]
    public void Shared_role_preserves_explicit_inline_identity(string role, string expected)
    {
        var run = new FlowRun(new(0, 3), new(0, 3), role, FlowInlineStyle.None,
            RenderOriginPrecision.ExactText);
        Assert.Equal(expected, NativeFlowPresentation.Role(run,
            new FlowParagraph(new(0, 3), new(0, 3), "heading", 1)));
    }

    /// <summary>A real hidden RichEdit renders ordinary heading text in the current theme accent.</summary>
    /// <remarks>No foreground, focus, clipboard or physical input changes are made.</remarks>
    [Fact]
    public void Windows_native_heading_color_uses_block_role_not_plain_inline_role()
    {
        if (!OperatingSystem.IsWindows()) return;
        var library = Win32.LoadLibraryW("msftedit.dll");
        var control = Win32.CreateWindowExW(0, "RICHEDIT50W", "",
            Win32.ES_MULTILINE | Win32.ES_READONLY, 0, 0, 400, 100,
            0, 0, Win32.GetModuleHandleW(null), 0);
        try
        {
            Assert.NotEqual(0, control);
            var shell = new WindowsEditorShell();
            typeof(WindowsEditorShell).GetField("_preview", BindingFlags.Instance |
                BindingFlags.NonPublic)!.SetValue(shell, control);
            var flow = new FlowRenderProjection(1, "Heading\nBody",
                [new(new(0, 7), new(2, 7), "text", FlowInlineStyle.None, RenderOriginPrecision.ExactText),
                 new(new(8, 4), new(11, 4), "text", FlowInlineStyle.None, RenderOriginPrecision.ExactText)],
                [new(new(0, 7), new(0, 9), "heading", 2),
                 new(new(8, 4), new(11, 4), "paragraph")], false, AnalysisCompleteness.Complete);
            foreach (var id in new[] { ThemePolicies.DefaultId, ThemePolicies.LightId })
            {
                var theme = ThemePolicies.Get(id);
                shell.SetTheme(theme);
                var view = new NativeAnalysisView([], "", flow.Text, "", new(1, 1), Flow: flow);
                typeof(WindowsEditorShell).GetMethod("InstallPreview", BindingFlags.Instance |
                    BindingFlags.NonPublic)!.Invoke(shell, [view]);
                var selection = new Win32.CharacterRange { Min = 0, Max = 7 };
                Win32.SendMessageW(control, Win32.EM_EXSETSEL, 0, ref selection);
                var format = new Win32.CharacterFormat
                { Size = (uint)Marshal.SizeOf<Win32.CharacterFormat>(), FaceName = "" };
                Win32.SendMessageW(control, Win32.EM_GETCHARFORMAT, Win32.SCF_SELECTION, ref format);
                var accent = theme.Palette.Accent;
                Assert.Equal((uint)(accent.Red | accent.Green << 8 | accent.Blue << 16), format.TextColor);
                Assert.NotEqual(0u, format.Effects & Win32.CFE_BOLD);
            }
        }
        finally
        {
            if (control != 0) Win32.DestroyWindow(control);
            if (library != 0) FreeLibrary(library);
        }
    }

    /// <summary>Releases the RichEdit module after all isolated controls are destroyed.</summary>
    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(nint library);
}
