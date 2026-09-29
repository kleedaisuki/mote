namespace Mote.Native.Accessibility;

/// <summary>
/// UI-thread bridge from an accessibility request to the controller's one
/// continuous source viewport. Implementations must not rebind a composing island.
/// </summary>
internal interface IAccessibleViewport
{
    /// <summary>Reveals an absolute source interval and returns its new visible frame.</summary>
    /// <remarks>
    /// The controller should settle or veto any operation that would move an active
    /// IME candidate. A false return is an honest accessibility failure, not a
    /// reason to present stale geometry as if scrolling succeeded.
    /// </remarks>
    bool TryScrollIntoView(AccessibleRange range, bool alignToTop);
}
