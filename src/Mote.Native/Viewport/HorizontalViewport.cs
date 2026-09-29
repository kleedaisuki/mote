namespace Mote.Native.Viewport;

/// <summary>The visual side of a source boundary when a shaped run has two caret edges.</summary>
internal enum HorizontalCaretAffinity
{
    /// <summary>The leading visual edge selected by the platform shaper.</summary>
    Leading,
    /// <summary>The trailing visual edge selected by the platform shaper.</summary>
    Trailing,
}

/// <summary>
/// Result of source-only reveal. Neither value proves physical pixel visibility;
/// the shell must shape and inspect current-version caret geometry.
/// </summary>
internal enum HorizontalRevealStatus
{
    /// <summary>The target lay outside its bounded source row and the anchor moved to it.</summary>
    SourceWindowMoved,
    /// <summary>The target lies in a bounded source row, but may still be offscreen in pixels.</summary>
    NeedsPixelVisibilityProof,
}

/// <summary>
/// One source-bound horizontal position. Its reference line and source boundary
/// move together, so a pixel scalar cannot drift away from the selected text.
/// </summary>
/// <param name="ReferenceLineStart">Source start of the line used for fallback source-column projection.</param>
/// <param name="SourceBoundary">Grapheme-safe source boundary in that line's content.</param>
/// <param name="Affinity">Visual caret side at a bidirectional or wrap edge.</param>
/// <param name="IntraClusterPixels">Local pixel displacement from the shaped caret edge.</param>
/// <param name="MeasuredXFromStart">Exact prefix width only when the entire prefix was measured; otherwise null.</param>
/// <remarks>
/// The pure model validates scalar and delimiter boundaries. The platform shaper
/// must supply a grapheme/shape-safe edge and normalize the residual against its
/// local cluster advance. It must not invent a 50 MiB prefix width.
/// </remarks>
internal readonly record struct HorizontalAnchor(
    int ReferenceLineStart,
    int SourceBoundary,
    HorizontalCaretAffinity Affinity,
    double IntraClusterPixels,
    double? MeasuredXFromStart);

/// <summary>
/// One immutable visible-row source window and the local source edge at its left.
/// The OS shaper derives the screen transform from the same row, not from a second
/// controller or accessibility offset.
/// A source-column projection into a different row is only UTF-16-scalar-safe;
/// the platform must refine it to a grapheme/shape-safe edge before using it for
/// exact paint or hit-test geometry.
/// </summary>
internal readonly record struct HorizontalRowWindow(
    ViewportSlice Slice,
    int LeftEdgeSourceBoundary,
    HorizontalCaretAffinity Affinity,
    double IntraClusterPixels);
