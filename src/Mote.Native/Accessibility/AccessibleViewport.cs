namespace Mote.Native.Accessibility;

/// <summary>Outcome of a synchronous source-viewport reveal request.</summary>
internal enum AccessibleRevealResult
{
    /// <summary>The controller revealed the range and published a matching frame.</summary>
    Revealed,
    /// <summary>Moving the focused input host would disrupt active composition.</summary>
    CompositionBlocked,
    /// <summary>The range no longer matches the controller's document generation/version.</summary>
    StaleRange,
    /// <summary>A valid source range could not be included in the published bounded frame.</summary>
    NotVisible,
    /// <summary>The caller is not on the owning UI thread; no post-and-wait is attempted.</summary>
    WrongThread
}

/// <summary>
/// UI-thread bridge from an accessibility request to the controller's one
/// continuous source viewport. Implementations must not rebind a composing island.
/// </summary>
internal interface IAccessibleViewport
{
    /// <summary>Synchronously reveals a generation/version-bound source interval.</summary>
    /// <remarks>
    /// This callback runs only on the owning UI thread. An off-thread COM/AppKit
    /// call must return WrongThread, never post and synchronously wait on the UI
    /// thread: that can deadlock an accessibility client. During composition the
    /// controller returns CompositionBlocked without forcing an IME commit/cancel;
    /// it returns StaleRange when a concurrent source transition invalidated G/V.
    /// It returns NotVisible if the valid range cannot be represented by the
    /// bounded painted slices, never claiming a successful but invisible reveal.
    /// Only Revealed means the matching CanvasFrame was published before return.
    /// </remarks>
    AccessibleRevealResult TryReveal(AccessibleRange range, bool alignToTop);
}
