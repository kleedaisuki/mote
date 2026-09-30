using System.Runtime.Versioning;

namespace Mote.Native.Windows;

/// <summary>Suspends RichEdit formatting undo recording without clearing native undo/redo history.</summary>
/// <remarks>
/// UI-thread-only, per input control. Nested leases share one TOM suspension. Acquisition fails
/// before the caller changes its palette; the final lease always resumes and releases the COM reference.
/// This helper does not own source editing or native history content.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed unsafe class WindowsRichEditUndoScope
{
    private const int EmGetOleInterface = 0x0400 + 60;
    private const int TomSuspend = -9999995;
    private const int TomResume = -9999994;
    private const int UndoSlot = 22;
    private static readonly Guid TextDocumentId = new("8CC497C0-A1DF-11CE-8098-00AA0047BE5D");
    private nint _document;
    private nint _control;
    private int _depth;

    /// <summary>Acquires one balanced lease, or returns null for an input control not yet created.</summary>
    internal IDisposable? Suspend(nint control)
    {
        if (control == 0) return null;
        if (_depth != 0)
        {
            if (_control != control) throw new InvalidOperationException("Undo scope cannot span input controls.");
            _depth++;
            return new Lease(this);
        }
        var document = GetDocument(control);
        try { Undo(document, TomSuspend); }
        catch { Release(document); throw; }
        _document = document;
        _control = control;
        _depth = 1;
        return new Lease(this);
    }

    /// <summary>Resumes only on the final release; cleanup does not depend on resume succeeding.</summary>
    private void Resume()
    {
        if (--_depth != 0) return;
        var document = _document;
        _document = 0;
        _control = 0;
        try { Undo(document, TomResume); }
        finally { Release(document); }
    }

    /// <summary>Obtains TOM through the documented RichEdit OLE interface with no runtime COM wrappers.</summary>
    private static nint GetDocument(nint control)
    {
        nint unknown = 0;
        if (Win32.SendMessageW(control, EmGetOleInterface, 0, (nint)(&unknown)) == 0 || unknown == 0)
        {
            if (unknown != 0) Release(unknown);
            throw new InvalidOperationException("RichEdit undo recording interface is unavailable.");
        }
        try
        {
            var id = TextDocumentId;
            nint document = 0;
            var query = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(nint**)unknown)[0];
            var result = query(unknown, &id, &document);
            if (result < 0 || document == 0)
            {
                if (document != 0) Release(document);
                throw new InvalidOperationException("RichEdit text document interface is unavailable.");
            }
            return document;
        }
        finally { Release(unknown); }
    }

    /// <summary>TOM.h specifies IDispatch slots 0–6 and ITextDocument::Undo at slot 22.</summary>
    private static void Undo(nint document, int operation)
    {
        var undo = (delegate* unmanaged[Stdcall]<nint, int, int*, int>)(*(nint**)document)[UndoSlot];
        var result = undo(document, operation, null);
        if (result < 0) throw new InvalidOperationException("RichEdit undo recording transition failed.");
    }

    /// <summary>Releases exactly one owned native reference using the IUnknown ABI.</summary>
    private static void Release(nint instance) =>
        ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)instance)[2])(instance);

    /// <summary>An idempotent lease prevents accidental double-resume.</summary>
    private sealed class Lease(WindowsRichEditUndoScope owner) : IDisposable
    {
        private WindowsRichEditUndoScope? _owner = owner;
        public void Dispose()
        {
            var owner = _owner;
            _owner = null;
            owner?.Resume();
        }
    }
}
