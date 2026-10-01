using System.Runtime.Versioning;

namespace Mote.Native.Windows;

/// <summary>
/// One owner-thread TOM attribute transaction for the full-source experiment.
/// Owns a document reference, a non-selection range and a detached font duplicate;
/// it never imports text, selects a range or records Engine history.
/// </summary>
/// <remarks>
/// Direct COM calls follow Windows SDK 10.0.26100.0 tom.h. IDispatch occupies
/// slots 0–6; Windows LONG and HRESULT remain 32-bit on x64 and ARM64. No RCW,
/// reflection COM activation or runtime-generated interop is required by AOT.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed unsafe class WindowsRichEditForegroundRange : IDisposable
{
    /// <summary>EM_GETOLEINTERFACE returns one owned IRichEditOle reference.</summary>
    private const int GetOleInterface = 0x0400 + 60;
    /// <summary>Only duplicate fonts may reset every property to undefined.</summary>
    private const int Undefined = -9999999;
    /// <summary>SDK ITextDocument IID, not an ITextDocument2 interface.</summary>
    private static readonly Guid DocumentId = new("8CC497C0-A1DF-11CE-8098-00AA0047BE5D");
    /// <summary>All interface use and release remain on the acquiring UI thread.</summary>
    private readonly int _owner = Environment.CurrentManagedThreadId;
    /// <summary>Exactly one owned reference per nonzero interface field.</summary>
    private nint _document, _range, _font;
    /// <summary>Count returned by our successful Freeze; zero means no lease.</summary>
    private int _freezeCount;
    /// <summary>Disposal is idempotent, including after an Unfreeze failure.</summary>
    private bool _disposed;

    /// <summary>Acquires a detached formatting cursor and exactly one checked Freeze lease.</summary>
    internal WindowsRichEditForegroundRange(nint control)
    {
        if (control == 0) throw new ArgumentOutOfRangeException(nameof(control));
        try
        {
            _document = Document(control);
            nint range = 0;
            var create = (delegate* unmanaged[Stdcall]<nint, int, int, nint*, int>)Slot(_document, 24);
            var result = create(_document, 0, 0, &range);
            _range = RequireInterface(range, result, "ITextDocument.Range");
            _font = DuplicateFont(_range);
            var reset = (delegate* unmanaged[Stdcall]<nint, int, int>)Slot(_font, 11);
            RequireOk(reset(_font, Undefined), "ITextFont.Reset");
            Freeze();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Changes only foreground on a retargeted, non-selection range in input order.</summary>
    internal void Apply(int start, int end, uint foreground)
    {
        CheckOwner();
        Retarget(start, end);
        var color = (delegate* unmanaged[Stdcall]<nint, int, int>)Slot(_font, 25);
        RequireOk(color(_font, checked((int)foreground)), "ITextFont.SetForeColor");
        var set = (delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(_range, 19);
        RequireOk(set(_range, _font), "ITextRange.SetFont");
    }

    /// <summary>Reads actual native foreground; the detached font is not a readback oracle.</summary>
    internal uint Read(int start, int end)
    {
        CheckOwner();
        Retarget(start, end);
        var font = RangeFont(_range);
        try
        {
            int value = 0;
            var get = (delegate* unmanaged[Stdcall]<nint, int*, int>)Slot(font, 24);
            RequireOk(get(font, &value), "ITextFont.GetForeColor");
            // Negative auto/undefined values cannot prove the explicit COLORREF
            // installed by this transaction; do not silently substitute a theme.
            if (value < 0 || value > 0xFFFFFF)
                throw new InvalidOperationException("TOM foreground readback is not an explicit COLORREF.");
            return (uint)value;
        }
        finally { Release(font); }
    }

    /// <summary>Balances only our successful Freeze, then releases all references even on failure.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        CheckOwner();
        _disposed = true;
        try
        {
            if (_freezeCount != 0) Unfreeze();
        }
        finally
        {
            Release(_font);
            Release(_range);
            Release(_document);
            _font = _range = _document = 0;
        }
    }

    /// <summary>Range endpoints are native paragraph UTF-16 positions, never CRLF display offsets.</summary>
    private void Retarget(int start, int end)
    {
        if (start < 0 || end < start) throw new ArgumentOutOfRangeException(nameof(start));
        var set = (delegate* unmanaged[Stdcall]<nint, int, int, int>)Slot(_range, 28);
        RequireOk(set(_range, start, end), "ITextRange.SetRange");
    }

    /// <summary>Retains successful acquisition before validating its reported positive count.</summary>
    private void Freeze()
    {
        int count = 0;
        var freeze = (delegate* unmanaged[Stdcall]<nint, int*, int>)Slot(_document, 18);
        var result = freeze(_document, &count);
        if (result < 0) throw Failure("ITextDocument.Freeze", result);
        _freezeCount = count > 0 ? count : 0;
        if (count <= 0) throw new InvalidOperationException("TOM Freeze did not acquire a positive count.");
    }

    /// <summary>Unfreeze may return S_FALSE for a remaining freeze; require one actual decrement.</summary>
    private void Unfreeze()
    {
        int count = -1;
        var unfreeze = (delegate* unmanaged[Stdcall]<nint, int*, int>)Slot(_document, 19);
        var result = unfreeze(_document, &count);
        if (result < 0) throw Failure("ITextDocument.Unfreeze", result);
        if (count != _freezeCount - 1)
            throw new InvalidOperationException("TOM Unfreeze did not balance this transaction's count.");
        _freezeCount = 0;
    }

    /// <summary>Obtains an explicit TOM IID from RichEdit OLE, releasing the temporary reference.</summary>
    private static nint Document(nint control)
    {
        nint unknown = 0;
        if (Win32.SendMessageW(control, GetOleInterface, 0, (nint)(&unknown)) == 0 || unknown == 0)
        {
            Release(unknown);
            throw new InvalidOperationException("RichEdit TOM interface is unavailable.");
        }
        try
        {
            var id = DocumentId;
            nint document = 0;
            var query = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot(unknown, 0);
            var result = query(unknown, &id, &document);
            return RequireInterface(document, result, "IUnknown.QueryInterface(ITextDocument)");
        }
        finally { Release(unknown); }
    }

    /// <summary>Creates an independent reusable font and immediately releases the attached getter object.</summary>
    private static nint DuplicateFont(nint range)
    {
        var attached = RangeFont(range);
        try
        {
            nint duplicate = 0;
            var get = (delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(attached, 7);
            var result = get(attached, &duplicate);
            return RequireInterface(duplicate, result, "ITextFont.GetDuplicate");
        }
        finally { Release(attached); }
    }

    /// <summary>Each getter returns a separately owned reference, never a cached retargeting assumption.</summary>
    private static nint RangeFont(nint range)
    {
        nint font = 0;
        var get = (delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(range, 18);
        var result = get(range, &font);
        return RequireInterface(font, result, "ITextRange.GetFont");
    }

    /// <summary>Even a failing COM call's nonnull output is released before reporting failure.</summary>
    private static nint RequireInterface(nint value, int result, string operation)
    {
        if (result == 0 && value != 0) return value;
        Release(value);
        throw Failure(operation, result);
    }

    /// <summary>S_FALSE may mean protected/no change, so mutations and readback require S_OK.</summary>
    private static void RequireOk(int result, string operation)
    {
        if (result != 0) throw Failure(operation, result);
    }

    /// <summary>Reports the exact failing HRESULT rather than silently falling back to selection formatting.</summary>
    private static InvalidOperationException Failure(string operation, int result) =>
        new($"{operation} failed (HRESULT 0x{result:X8}).");

    /// <summary>Dereferences only an acquired, live interface's SDK-verified vtable slot.</summary>
    private static nint Slot(nint instance, int index) => (*(nint**)instance)[index];

    /// <summary>Releases one owned reference through IUnknown::Release, including failed acquisition cleanup.</summary>
    private static void Release(nint instance)
    {
        if (instance != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(instance, 2))(instance);
    }

    /// <summary>Rejects use after disposal and any interface transfer to another thread.</summary>
    private void CheckOwner()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId != _owner)
            throw new InvalidOperationException("TOM formatting requires its acquiring UI thread.");
    }
}
