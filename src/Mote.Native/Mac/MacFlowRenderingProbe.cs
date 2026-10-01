using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mote.Formats;
using Mote.Native.Mac.Canvas;
using Mote.Themes;

namespace Mote.Native.Mac;

/// <summary>In-process AppKit Flow acceptance with real native attributes and layout.</summary>
/// <remarks>No files, input-source mutation, external input, TCC grants or clipboard writes.</remarks>
[SupportedOSPlatform("macos")]
internal static class MacFlowRenderingProbe
{
    private const string Runtime = "/usr/lib/libobjc.A.dylib";
    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    private static extern nint Attribute(nint storage, nint selector, nint key,
        nuint index, nint effectiveRange);
    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    private static extern double Scalar(nint receiver, nint selector);

    /// <summary>Opens one isolated legacy AppKit shell and checks installed typed presentation.</summary>
    internal static int Run()
    {
        var shell = new MacEditorShell();
        var passed = false;
        shell.Shown += () => shell.Post(() =>
        {
            try { Check(shell); passed = true; }
            catch (Exception error) when (error is not OutOfMemoryException)
            { Console.Error.WriteLine($"Mac Flow check failed: {error.Message}"); }
            finally { shell.Close(); }
        });
        shell.Run();
        return passed ? 0 : 1;
    }

    private static void Check(MacEditorShell shell)
    {
        MacMenuObservationProbe.VerifyForwarding();
        var text = "Heading\nbold italic code link\n• nested\nquote\n" +
            string.Concat(Enumerable.Repeat("scrollable body line\n", 65));
        var heading = new TextSpan(0, 8);
        var body = new TextSpan(8, 22);
        var nested = new TextSpan(30, 9);
        var quote = new TextSpan(39, 6);
        var runs = new[]
        {
            new FlowRun(new(0,7), new(0,7), "heading", FlowInlineStyle.None, RenderOriginPrecision.Item),
            new FlowRun(new(8,4), new(8,4), "text", FlowInlineStyle.Strong, RenderOriginPrecision.Item),
            new FlowRun(new(13,6), new(13,6), "text", FlowInlineStyle.Emphasis, RenderOriginPrecision.Item),
            new FlowRun(new(20,4), new(20,4), "code", FlowInlineStyle.Code, RenderOriginPrecision.Item),
            new FlowRun(new(25,4), new(25,4), "text", FlowInlineStyle.Link, RenderOriginPrecision.Item)
        };
        var flow = new FlowRenderProjection(1, text, runs,
            [new(heading, heading, "heading", 1), new(body, body, "paragraph"),
             new(nested, nested, "list-item", Depth: 2, Marker: "•"),
             new(quote, quote, "quote")], false, AnalysisCompleteness.Complete);
        var stamp = new NativeDocumentStamp(1,1);
        var view = new NativeAnalysisView([], "", text, "", stamp,
            PresentationSequence: 1, Flow: flow);
        shell.SetTheme(ThemePolicies.Resolve(ThemePolicies.DarkId, true));
        shell.SetDocument(new NativeDocumentView("Flow probe", "source", 0, 6, false, "", stamp));
        shell.SetAnalysis(view);
        var preview = shell.ProbePreviewView;
        var storage = ObjC.Send(preview, ObjC.Sel("textStorage"));
        Require(ObjC.ManagedString(ObjC.Send(preview, ObjC.Sel("string"))) == text, "native text");
        Require(shell.ProbePreviewIdentity == view.Identity, "installed identity");
        Require(ObjC.Send(preview, ObjC.Sel("isEditable")) == 0 &&
            ObjC.Send(preview, ObjC.Sel("isSelectable")) != 0, "read-only selectable preview");
        Require(ObjC.ManagedString(ObjC.Send(preview, ObjC.Sel("accessibilityLabel"))) ==
            "Mote preview", "accessibility identity");
        var headingFont = Get(storage, "NSFont", 0);
        var bodyFont = Get(storage, "NSFont", 35);
        Require(Scalar(headingFont, ObjC.Sel("pointSize")) >
            Scalar(bodyFont, ObjC.Sel("pointSize")), "heading size");
        var manager = ObjC.Send(ObjC.Class("NSFontManager"), ObjC.Sel("sharedFontManager"));
        Require(((long)ObjC.Send(manager, ObjC.Sel("traitsOfFont:"), Get(storage,"NSFont",8)) & 2) != 0,
            "strong trait");
        Require(((long)ObjC.Send(manager, ObjC.Sel("traitsOfFont:"), Get(storage,"NSFont",13)) & 1) != 0,
            "emphasis trait");
        Require(ObjC.Send(Get(storage,"NSFont",20),ObjC.Sel("isFixedPitch")) != 0,
            "code monospaced font");
        Require(Get(storage,"NSLink",25) == 0 && Get(storage,"NSUnderline",25) != 0,
            "link is appearance, not native launch");
        var listStyle = Get(storage,"NSParagraphStyle",32);
        var quoteStyle = Get(storage,"NSParagraphStyle",40);
        Require(Scalar(listStyle,ObjC.Sel("headIndent")) >
            Scalar(listStyle,ObjC.Sel("firstLineHeadIndent")), "hanging list indent");
        Require(Scalar(quoteStyle,ObjC.Sel("headIndent")) > 0, "quote indent");
        ObjC.Send(preview, ObjC.Sel("setSelectedRange:"), new ObjC.Range(8,4));
        var scroll = ObjC.Send(preview, ObjC.Sel("enclosingScrollView"));
        var clip = ObjC.Send(scroll,ObjC.Sel("contentView"));
        ObjC.Send(ObjC.Send(preview,ObjC.Sel("layoutManager")),
            ObjC.Sel("ensureLayoutForTextContainer:"), ObjC.Send(preview,ObjC.Sel("textContainer")));
        ObjC.Send(clip,ObjC.Sel("scrollToPoint:"),new ObjC.Point(0,100));
        var origin = MacOnScreenCanvasNative.GetRect(clip,ObjC.Sel("bounds")).Origin;
        shell.SetTheme(ThemePolicies.Resolve(ThemePolicies.LightId, false));
        var second = view with { PresentationSequence = 2 };
        shell.SetAnalysis(second);
        Require(ObjC.SendRange(preview,ObjC.Sel("selectedRange")) == new ObjC.Range(8,4),
            "selection survives theme restyle");
        var after = MacOnScreenCanvasNative.GetRect(clip,ObjC.Sel("bounds")).Origin;
        Require(Math.Abs(after.Y-origin.Y) < 1, "scroll survives same-text restyle");
        Require(shell.ProbePreviewIdentity == second.Identity, "same-version new identity");
        shell.SetAnalysis(second with { PresentationSequence = 5, Flow = null });
        Require(Get(storage,"NSUnderline",25) == 0, "legacy clears Flow underline");
        var legacyParagraph = Get(storage,"NSParagraphStyle",32);
        Require(legacyParagraph == 0 || Scalar(legacyParagraph,ObjC.Sel("headIndent")) == 0,
            "legacy clears Flow paragraph indent");
        Require(((long)ObjC.Send(manager, ObjC.Sel("traitsOfFont:"), Get(storage,"NSFont",8)) & 3) == 0,
            "legacy clears Flow font traits");
        Require(ObjC.SendRange(preview,ObjC.Sel("selectedRange")) == new ObjC.Range(8,4),
            "legacy transition preserves selection");
        shell.SetAnalysis(second with { PresentationSequence = 6 });
        Require(Get(storage,"NSUnderline",25) != 0, "Flow reinstalls underline");
        var layout = shell.ProbeLayoutViews;
        var width = MacOnScreenCanvasNative.GetRect(layout.Source,ObjC.Sel("frame")).Size.Width;
        shell.SetAnalysis(second with { PresentationSequence = 3, ShowPreview = false });
        Require(ObjC.Send(layout.Preview,ObjC.Sel("superview")) == 0, "source-only detached pane");
        var full = MacOnScreenCanvasNative.GetRect(layout.Source,ObjC.Sel("frame")).Size.Width;
        Require(full > width, "source-only expands source");
        shell.SetAnalysis(second with { PresentationSequence = 4, ShowPreview = true });
        Require(ObjC.Send(layout.Preview,ObjC.Sel("superview")) == layout.Split, "split restored");
        var restored = MacOnScreenCanvasNative.GetRect(layout.Source,ObjC.Sel("frame")).Size.Width;
        Require(Math.Abs(restored-width) < 2, "split proportion preserved");
        Require(shell.ProbeNativeText == "source", "rendering never edits source");
    }

    private static nint Get(nint storage, string key, nuint offset) => Attribute(storage,
        ObjC.Sel("attribute:atIndex:effectiveRange:"), ObjC.String(key), offset, 0);

    private static void Require(bool condition, string contract)
    {
        if (!condition) throw new InvalidOperationException(contract);
    }
}
