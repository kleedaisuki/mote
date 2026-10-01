using Mote.Native.Mac.Canvas;

namespace Mote.Native.Mac;

/// <summary>One-shot process-local release diagnostics; ordinary windows never set these answers.</summary>
internal sealed unsafe partial class MacEditorShell
{
    /// <summary>Consumed by the next real Find command, avoiding modal automation in hosted diagnostics.</summary>
    private string? _releaseFindQuery;
    /// <summary>Consumed by the next real Go To Line command.</summary>
    private int? _releaseGoToLine;
    /// <summary>One diagnostic-only discard answer; production windows always display their native alert.</summary>
    private bool? _releaseDiscardAnswer;
    /// <summary>Records actual controller discard confirmation rather than assuming performClose reached it.</summary>
    private bool _releaseDiscardObserved;

    /// <summary>Supplies only the next native prompt result; does not execute a controller command.</summary>
    internal void ProbeReleaseFind(string query) => _releaseFindQuery = query;
    /// <summary>Supplies only the next native prompt result; all navigation remains controller-owned.</summary>
    internal void ProbeReleaseGoToLine(int line) => _releaseGoToLine = line;
    /// <summary>Invokes the actual text responder history selector rather than bypassing the shell.</summary>
    internal void ProbeReleaseHistory(bool redo) => ObjC.Send(_editor, ObjC.Sel(redo ? "redo:" : "undo:"), 0);
    /// <summary>Reads the actual AppKit ordered selection, without inferring an active endpoint.</summary>
    internal ObjC.Range ProbeReleaseSelection => ObjC.SendRange(_editor, ObjC.Sel("selectedRange"));
    /// <summary>Attempts the real window close path with a one-shot Cancel answer.</summary>
    internal void ProbeReleaseCancelClose()
    {
        _releaseDiscardObserved = false;
        _releaseDiscardAnswer = false;
        ObjC.Send(_window, ObjC.Sel("performClose:"), 0);
    }
    /// <summary>True only when the real dirty-close request consumed Cancel and the window remains visible.</summary>
    internal bool ProbeReleaseCloseCancelled => _releaseDiscardObserved && ObjC.Send(_window, ObjC.Sel("isVisible")) != 0;
    /// <summary>Rejects an actual external-open callback through the dirty-document Cancel barrier.</summary>
    internal bool ProbeReleaseCancelExternalOpen(string path)
    {
        _releaseDiscardObserved = false;
        _releaseDiscardAnswer = false;
        var admitted = ProbeReleaseExternalOpen(path);
        return !admitted && _releaseDiscardObserved;
    }
    /// <summary>Replaces a known fixture range through the real native text-input protocol.</summary>
    internal void ProbeReleaseReplace(int start, int length, string value)
    {
        ObjC.Send(_editor, ObjC.Sel("setSelectedRange:"), new ObjC.Range((nuint)start, (nuint)length));
        ObjC.Send(_editor, ObjC.Sel("insertText:replacementRange:"), ObjC.String(value), new ObjC.Range(nuint.MaxValue, 0));
    }
    /// <summary>Stages marked input at the fixture's edited marker without touching system input sources.</summary>
    internal void ProbeReleaseMarked(int caret, string value)
    {
        ObjC.Send(_editor, ObjC.Sel("setSelectedRange:"), new ObjC.Range((nuint)caret, 0));
        ObjC.Send(_editor, ObjC.Sel("setMarkedText:selectedRange:replacementRange:"), ObjC.String(value),
            new ObjC.Range((nuint)value.Length, 0), new ObjC.Range(nuint.MaxValue, 0));
    }

    /// <summary>Caches this window's actual AppKit content view as PNG, without global screen capture or permissions.</summary>
    internal void ProbeReleaseCapture(string path)
    {
        MacReleaseWorkflowProbe.ValidateOutput(path);
        var view = ObjC.Send(_window, ObjC.Sel("contentView"));
        var bounds = MacOnScreenCanvasNative.GetRect(view, ObjC.Sel("bounds"));
        if (bounds.Size.Width <= 0 || bounds.Size.Height <= 0 || bounds.Size.Width > 4096 || bounds.Size.Height > 4096)
            throw new InvalidOperationException("Product capture bounds are invalid.");
        var bitmap = ObjC.Send(view, ObjC.Sel("bitmapImageRepForCachingDisplayInRect:"), bounds);
        if (bitmap == 0) throw new InvalidOperationException("AppKit product bitmap allocation failed.");
        MacOnScreenCanvasNative.Send(view, ObjC.Sel("cacheDisplayInRect:toBitmapImageRep:"), bounds, bitmap);
        var png = ObjC.Send(bitmap, ObjC.Sel("representationUsingType:properties:"), 4,
            ObjC.Send(ObjC.Class("NSDictionary"), ObjC.Sel("dictionary")));
        if (png == 0 || ObjC.Send(png, ObjC.Sel("writeToFile:atomically:"), ObjC.String(path), 1) == 0)
            throw new IOException("AppKit product PNG write failed.");
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 8 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new IOException("AppKit product capture is not PNG.");
    }
}
