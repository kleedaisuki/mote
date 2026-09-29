using Mote.Native.Accessibility;

namespace Mote.Native.Mac.Accessibility;

/// <summary>
/// Source-coordinate core for AppKit's text accessibility selectors. An actual
/// NSAccessibility element must be registered on the canvas view separately.
/// </summary>
/// <remarks>
/// The AppKit bridge must expose one editor element, suppress the input island
/// as a second editor, and convert NSRange/NSInteger without narrowing overflow.
/// It must not use accessibilityValue to materialize the whole document.
/// </remarks>
internal sealed class MacAccessibilityTextCore
{
    private readonly AccessibleDocument _document;
    private readonly IAccessibleViewport _viewport;
    private int _detached;

    /// <summary>Creates an AppKit-facing source range adapter.</summary>
    internal MacAccessibilityTextCore(AccessibleDocument document, IAccessibleViewport viewport)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
    }

    /// <summary>Rejects this AX element's requests without closing the shared document.</summary>
    internal void Detach() => Volatile.Write(ref _detached, 1);

    private void EnsureAttached()
    {
        if (Volatile.Read(ref _detached) != 0)
            throw new InvalidOperationException("AppKit AX element is detached.");
    }

    /// <summary>The document's total UTF-16 length, not the island's length.</summary>
    internal int CharacterCount
    {
        get { EnsureAttached(); return _document.DocumentRange.Length; }
    }

    /// <summary>Equivalent of accessibilitySelectedTextRange.</summary>
    internal AccessibleRange SelectedTextRange
    {
        get { EnsureAttached(); return _document.Selection; }
    }

    /// <summary>Equivalent of accessibilityVisibleCharacterRange, including full logical lines.</summary>
    /// <remarks>Horizontal clipping does not reduce this range per AppKit's contract.</remarks>
    internal AccessibleRange VisibleCharacterRange
    {
        get { EnsureAttached(); return _document.VisibleLogicalLineRange(); }
    }

    /// <summary>Equivalent of accessibilityStringForRange; supports offscreen source.</summary>
    internal string StringForRange(AccessibleRange range)
    {
        EnsureAttached();
        return _document.GetText(range);
    }

    /// <summary>Equivalent of accessibilityLineForIndex.</summary>
    internal int LineForIndex(int offset)
    {
        EnsureAttached();
        return _document.LineFromOffset(offset);
    }

    /// <summary>Equivalent of accessibilityRangeForLine.</summary>
    internal AccessibleRange RangeForLine(int line)
    {
        EnsureAttached();
        return _document.LineRange(line);
    }

    /// <summary>Checks an AppKit NSRange against the current source snapshot.</summary>
    internal AccessibleRange MakeRange(int start, int end)
    {
        EnsureAttached();
        return _document.MakeRange(start, end);
    }

    /// <summary>Requests source-coordinate scroll from the controller.</summary>
    internal AccessibleRevealResult ScrollIntoView(AccessibleRange range)
    {
        EnsureAttached();
        _document.ValidateRange(range);
        return _viewport.TryReveal(range, alignToTop: false);
    }
}
