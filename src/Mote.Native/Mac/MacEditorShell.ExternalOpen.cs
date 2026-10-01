using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mote.Native.Mac;

/// <summary>Launch Services file delivery goes through the same canonical replacement barrier as product Open.</summary>
internal sealed unsafe partial class MacEditorShell
{
    /// <inheritdoc />
    public event Func<string, bool>? ExternalOpenRequested;

    /// <summary>Dispatches a genuine AppKit filename without converting it into a file-picker diagnostic answer.</summary>
    private bool RequestExternalOpen(nint filename)
    {
        if (filename == 0) return false;
        var path = ObjC.ManagedString(filename);
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\0')) return false;
        return ExternalOpenRequested?.Invoke(path) == true;
    }

    /// <summary>BOOL return certifies synchronous command admission, not completion of asynchronous disk I/O.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte ApplicationOpenFile(nint self, nint selector, nint application, nint filename)
    {
        try { return s_current?.RequestExternalOpen(filename) == true ? (byte)1 : (byte)0; }
        catch { s_current?.ReportExternalOpenFailure(); return 0; }
    }

    /// <summary>Explicitly rejects multiple documents and acknowledges AppKit's open-files protocol exactly once.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ApplicationOpenFiles(nint self, nint selector, nint application, nint filenames)
    {
        var admitted = false;
        try
        {
            var shell = s_current;
            var count = filenames == 0 ? 0 : (nuint)ObjC.Send(filenames, ObjC.Sel("count"));
            if (MacExternalOpenModel.AcceptFileCount(count))
                admitted = shell?.RequestExternalOpen(ObjC.Send(filenames, ObjC.Sel("objectAtIndex:"), 0)) == true;
            else if (count > 1)
                shell?.ShowError("mote opens one document at a time. Open one file instead of a multiple-file selection.");
        }
        catch { s_current?.ReportExternalOpenFailure(); }
        finally
        {
            // NSApplicationDelegateReplySuccess = 0; Failure = 2. A Cancelled
            // replacement is a rejected request, never a fabricated successful open.
            try { ObjC.Send(application, ObjC.Sel("replyToOpenOrPrint:"), admitted ? 0 : 2); }
            catch { /* Native acknowledgments must not unwind managed exceptions through AppKit. */ }
        }
    }

    /// <summary>Contains secondary presentation failure and never exposes filenames or native exception messages.</summary>
    private void ReportExternalOpenFailure()
    {
        try { ShowError("The external file-open request could not be accepted."); }
        catch { /* Neither the original callback nor its error UI may unwind into native code. */ }
    }

    /// <summary>Controlled diagnostics call the registered application delegate selector, not the event directly.</summary>
    internal bool ProbeReleaseExternalOpen(string path) =>
        ExternalOpenReply(_delegate, ObjC.Sel("application:openFile:"), _application, ObjC.String(path)) != 0;

    /// <summary>BOOL-returning application delegate dispatch uses its actual native byte ABI.</summary>
    [DllImport(ObjcRuntime, EntryPoint = "objc_msgSend")]
    private static extern byte ExternalOpenReply(nint receiver, nint selector, nint application, nint filename);
}

/// <summary>Portable single-document admission policy for native Finder file delivery.</summary>
internal static class MacExternalOpenModel
{
    /// <summary>True only for one incoming document; never silently selects a first or last member.</summary>
    internal static bool AcceptFileCount(nuint count) => count == 1;
}
