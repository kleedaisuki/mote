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

    /// <summary>Creates an AppKit-facing source range adapter.</summary>
    internal MacAccessibilityTextCore(AccessibleDocument document, IAccessibleViewport viewport)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
    }

    /// <summary>Rejects retained AX requests after the native editor closes.</summary>
    internal void Invalidate() => _document.Invalidate();

    /// <summary>The document's total UTF-16 length, not the island's length.</summary>
    internal int CharacterCount => _document.DocumentRange.Length;

    /// <summary>Equivalent of accessibilitySelectedTextRange.</summary>
    internal AccessibleRange SelectedTextRange => _document.Selection;

    /// <summary>Equivalent of accessibilityVisibleCharacterRange, including full logical lines.</summary>
    /// <remarks>Horizontal clipping does not reduce this range per AppKit's contract.</remarks>
    internal AccessibleRange VisibleCharacterRange => _document.VisibleLogicalLineRange();

    /// <summary>Equivalent of accessibilityStringForRange; supports offscreen source.</summary>
    internal string StringForRange(AccessibleRange range) => _document.GetText(range);

    /// <summary>Equivalent of accessibilityLineForIndex.</summary>
    internal int LineForIndex(int offset) => _document.LineFromOffset(offset);

    /// <summary>Equivalent of accessibilityRangeForLine.</summary>
    internal AccessibleRange RangeForLine(int line) => _document.LineRange(line);

    /// <summary>Checks an AppKit NSRange against the current source snapshot.</summary>
    internal AccessibleRange MakeRange(int start, int end) => _document.MakeRange(start, end);

    /// <summary>Requests source-coordinate scroll from the controller.</summary>
    internal bool ScrollIntoView(AccessibleRange range)
    {
        _document.ValidateRange(range);
        return _viewport.TryScrollIntoView(range, alignToTop: false);
    }
}
