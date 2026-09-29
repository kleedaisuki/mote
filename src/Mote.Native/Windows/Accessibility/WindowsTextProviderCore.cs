using Mote.Native.Accessibility;

namespace Mote.Native.Windows.Accessibility;

/// <summary>One COM-ready text result: only S_OK carries an exact string.</summary>
internal readonly record struct WindowsTextResult(int HResult, string? Text)
{
    /// <summary>Standard COM success for a complete requested result.</summary>
    internal const int S_OK = 0;
    /// <summary>COM invalid-argument status.</summary>
    internal const int E_INVALIDARG = unchecked((int)0x80070057);
    /// <summary>The provider cannot allocate within its per-call budget.</summary>
    internal const int E_OUTOFMEMORY = unchecked((int)0x8007000E);
    /// <summary>A range belongs to a stale source document/version.</summary>
    internal const int UIA_E_ELEMENTNOTAVAILABLE = unchecked((int)0x80040201);
}

/// <summary>
/// Source-coordinate core for a future UI Automation ITextProvider and
/// ITextRangeProvider. It contains no runtime COM interop or OS handles.
/// </summary>
/// <remarks>
/// A native COM wrapper must forward WM_GETOBJECT through
/// UiaReturnRawElementProvider, implement every required pattern method, map
/// oversized requests to a failing HRESULT (not S_OK with truncated text), and
/// keep the caret-local RichEdit out of the accessibility tree. Those integration
/// gates are intentionally not claimed by this tested core.
/// </remarks>
internal sealed class WindowsTextProviderCore
{
    private readonly AccessibleDocument _document;
    private readonly IAccessibleViewport _viewport;

    /// <summary>Creates a UIA-facing range adapter over the canonical source.</summary>
    internal WindowsTextProviderCore(AccessibleDocument document, IAccessibleViewport viewport)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
    }

    /// <summary>Invalidates all outstanding ranges after the editor HWND closes.</summary>
    internal void Invalidate() => _document.Invalidate();

    /// <summary>Equivalent of ITextProvider.DocumentRange.</summary>
    internal AccessibleRange DocumentRange => _document.DocumentRange;

    /// <summary>Equivalent of ITextProvider.GetSelection, including a degenerate caret range.</summary>
    internal AccessibleRange Selection => _document.Selection;

    /// <summary>Equivalent of ITextProvider.GetVisibleRanges over painted source slices.</summary>
    internal IReadOnlyList<AccessibleRange> GetVisibleRanges() => _document.VisibleRanges();

    /// <summary>Equivalent of ITextRangeProvider.GetText with UIA's -1 convention.</summary>
    internal string GetText(AccessibleRange range, int maxLength) => _document.GetText(range, maxLength);

    /// <summary>
    /// Maps known range failures to HRESULT without ever returning S_OK for a
    /// truncated unbounded request. A COM vtable stub can return this result
    /// directly after allocating the successful string as a BSTR.
    /// </summary>
    internal WindowsTextResult TryGetText(AccessibleRange range, int maxLength)
    {
        try { return new WindowsTextResult(WindowsTextResult.S_OK, GetText(range, maxLength)); }
        catch (AccessibleRequestTooLargeException)
        {
            return new WindowsTextResult(WindowsTextResult.E_OUTOFMEMORY, null);
        }
        catch (OutOfMemoryException)
        {
            return new WindowsTextResult(WindowsTextResult.E_OUTOFMEMORY, null);
        }
        catch (InvalidOperationException)
        {
            return new WindowsTextResult(WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE, null);
        }
        catch (ArgumentOutOfRangeException)
        {
            return new WindowsTextResult(WindowsTextResult.E_INVALIDARG, null);
        }
    }

    /// <summary>Finds the logical source line containing an endpoint.</summary>
    internal int LineFromOffset(int offset) => _document.LineFromOffset(offset);

    /// <summary>Returns one logical source line for UIA line-unit navigation.</summary>
    internal AccessibleRange RangeForLine(int line) => _document.LineRange(line);

    /// <summary>Equivalent of ITextRangeProvider.ScrollIntoView; no input-island rebind occurs here.</summary>
    internal bool ScrollIntoView(AccessibleRange range, bool alignToTop)
    {
        _document.ValidateRange(range);
        return _viewport.TryScrollIntoView(range, alignToTop);
    }
}
