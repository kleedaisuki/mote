using System.Runtime.Versioning;

namespace Mote.Native.Mac;

/// <summary>Temporary multiline decoded-value input; never a second document or Undo owner.</summary>
[SupportedOSPlatform("macos")]
internal static class MacGridReplacement
{
    /// <summary>Creates a plain multiline editor and verifies exact CR/LF and UTF-16 roundtrip.</summary>
    internal static nint CreateEditor(string value)
    {
        var editor = ObjC.Send(ObjC.Send(ObjC.Class("NSTextView"), ObjC.Sel("alloc")),
            ObjC.Sel("initWithFrame:"), new ObjC.Rect(0,0,420,180));
        try
        {
            ObjC.Send(editor, ObjC.Sel("setRichText:"), 0);
            ObjC.Send(editor, ObjC.Sel("setAllowsUndo:"), 0);
            ObjC.Send(editor, ObjC.Sel("setAutomaticQuoteSubstitutionEnabled:"), 0);
            ObjC.Send(editor, ObjC.Sel("setAutomaticDashSubstitutionEnabled:"), 0);
            ObjC.Send(editor, ObjC.Sel("setAutomaticTextReplacementEnabled:"), 0);
            ObjC.Send(editor, ObjC.Sel("setString:"), ObjC.String(value));
            if (!string.Equals(value, ObjC.ManagedString(ObjC.Send(editor, ObjC.Sel("string"))), StringComparison.Ordinal))
                throw new InvalidOperationException("The native decoded-value editor could not retain exact line endings.");
            return editor;
        }
        catch { ObjC.Send(editor, ObjC.Sel("release")); throw; }
    }

    /// <summary>Returns exact edited text only after explicit Replace; Escape/Cancel leaves source unchanged.</summary>
    internal static string? Prompt(string value)
    {
        var alert = ObjC.New("NSAlert");
        var editor = CreateEditor(value);
        var scroll = ObjC.Send(ObjC.Send(ObjC.Class("NSScrollView"), ObjC.Sel("alloc")),
            ObjC.Sel("initWithFrame:"), new ObjC.Rect(0,0,420,180));
        try
        {
            ObjC.Send(alert, ObjC.Sel("setMessageText:"), ObjC.String("Replace CSV cell"));
            ObjC.Send(alert, ObjC.Sel("setInformativeText:"), ObjC.String(
                "Enter decoded text, including newlines. The CSV policy validates and quotes one Engine transaction. ⌘Return replaces; Escape cancels."));
            ObjC.Send(scroll, ObjC.Sel("setHasVerticalScroller:"), 1);
            ObjC.Send(scroll, ObjC.Sel("setDocumentView:"), editor);
            ObjC.Send(alert, ObjC.Sel("setAccessoryView:"), scroll);
            var accept = ObjC.Send(alert, ObjC.Sel("addButtonWithTitle:"), ObjC.String("Replace"));
            ObjC.Send(accept, ObjC.Sel("setKeyEquivalent:"), ObjC.String("\r"));
            ObjC.Send(accept, ObjC.Sel("setKeyEquivalentModifierMask:"), (nint)(1u << 20));
            var cancel = ObjC.Send(alert, ObjC.Sel("addButtonWithTitle:"), ObjC.String("Cancel"));
            ObjC.Send(cancel, ObjC.Sel("setKeyEquivalent:"), ObjC.String("\u001b"));
            ObjC.Send(ObjC.Send(alert, ObjC.Sel("window")), ObjC.Sel("makeFirstResponder:"), editor);
            if (ObjC.Send(alert, ObjC.Sel("runModal")) != 1000) return null;
            if (ObjC.Send(editor, ObjC.Sel("hasMarkedText")) != 0)
            {
                ObjC.Send(editor, ObjC.Sel("unmarkText"));
                if (ObjC.Send(editor, ObjC.Sel("hasMarkedText")) != 0) return null;
            }
            return ObjC.ManagedString(ObjC.Send(editor, ObjC.Sel("string")));
        }
        finally
        {
            ObjC.Send(alert, ObjC.Sel("release"));
            ObjC.Send(scroll, ObjC.Sel("release"));
            ObjC.Send(editor, ObjC.Sel("release"));
        }
    }
}
