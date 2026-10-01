using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mote.Engine;

namespace Mote.Native.Mac;

/// <summary>Collects an explicit codec choice without inspecting or changing a document.</summary>
[SupportedOSPlatform("macos")]
internal static class MacOpenEncodingPrompt
{
    /// <summary>Second-button response: the first/default button deliberately cancels.</summary>
    private const nint OpenResponse = 1001;

    /// <summary>Sends CGRect followed by Objective-C BOOL, avoiding an integer or scalar ABI substitution.</summary>
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint InitializePopup(nint receiver, nint selector, ObjC.Rect frame, byte pullsDown);

    /// <summary>
    /// Returns only a deliberately selected codec. Cancel returns null; accepting the
    /// placeholder or a native allocation failure throws an actionable UI error.
    /// The caller owns error presentation and file opening, including preservation of
    /// the current document if selection or subsequent decoding fails.
    /// </summary>
    internal static DocumentTextEncoding? Choose()
    {
        nint alert = 0;
        nint popup = 0;
        try
        {
            alert = ObjC.New("NSAlert");
            RequireControl(alert);
            popup = CreatePopup();
            ObjC.Send(alert, ObjC.Sel("setMessageText:"), ObjC.String("Open with Encoding"));
            ObjC.Send(alert, ObjC.Sel("setInformativeText:"),
                ObjC.String("Choose the file's encoding explicitly. mote will not guess or replace invalid characters."));
            ObjC.Send(popup, ObjC.Sel("addItemWithTitle:"), ObjC.String("Choose an encoding…"));
            foreach (var choice in NativeOpenEncodingChoices.All)
                ObjC.Send(popup, ObjC.Sel("addItemWithTitle:"), ObjC.String(choice.Label));
            ObjC.Send(popup, ObjC.Sel("selectItemAtIndex:"), (nint)0);
            ObjC.Send(popup, ObjC.Sel("setAccessibilityLabel:"), ObjC.String("File encoding"));
            ObjC.Send(alert, ObjC.Sel("setAccessoryView:"), popup);
            RequireControl(ObjC.Send(alert, ObjC.Sel("addButtonWithTitle:"), ObjC.String("Cancel")));
            RequireControl(ObjC.Send(alert, ObjC.Sel("addButtonWithTitle:"), ObjC.String("Open")));
            if (ObjC.Send(alert, ObjC.Sel("runModal")) != OpenResponse) return null;
            var index = ObjC.Send(popup, ObjC.Sel("indexOfSelectedItem"));
            if (index <= 0 || index > NativeOpenEncodingChoices.All.Count)
                throw new InvalidOperationException("Choose an encoding before opening the file.");
            return NativeOpenEncodingChoices.All[checked((int)index - 1)].Encoding;
        }
        finally
        {
            // The alert retains its accessory; both independent +1 ownerships must end.
            if (alert != 0) ObjC.Send(alert, ObjC.Sel("release"));
            if (popup != 0) ObjC.Send(popup, ObjC.Sel("release"));
        }
    }

    /// <summary>Creates a pop-up, not a pull-down command menu; init owns allocation failure semantics.</summary>
    private static nint CreatePopup()
    {
        var selector = ObjC.Sel("initWithFrame:pullsDown:");
        var popup = ObjC.Send(ObjC.Class("NSPopUpButton"), ObjC.Sel("alloc"));
        RequireControl(popup);
        try
        {
            var initialized = InitializePopup(popup, selector, new ObjC.Rect(0, 0, 360, 28), 0);
            // Cocoa init consumes the allocation even when returning nil.
            popup = 0;
            RequireControl(initialized);
            return initialized;
        }
        finally
        {
            if (popup != 0) ObjC.Send(popup, ObjC.Sel("release"));
        }
    }

    /// <summary>Native control failure is not a healthy user cancellation.</summary>
    private static void RequireControl(nint control)
    {
        if (control == 0)
            throw new InvalidOperationException("The native encoding chooser could not be created. The current document was not changed.");
    }
}
