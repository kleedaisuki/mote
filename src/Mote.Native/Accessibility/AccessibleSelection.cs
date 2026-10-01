namespace Mote.Native.Accessibility;

/// <summary>Outcome of a synchronous canonical source-selection request.</summary>
internal enum AccessibleSelectionResult
{
    /// <summary>The selection and matching source-versioned frame were published.</summary>
    Selected,
    /// <summary>The range has stale identity or invalid source bounds.</summary>
    StaleRange,
    /// <summary>Changing selection would disturb active native text composition.</summary>
    CompositionBlocked,
    /// <summary>An endpoint splits a surrogate pair or CRLF delimiter.</summary>
    InvalidBoundary,
    /// <summary>The caller is not on the owning UI thread.</summary>
    WrongThread
}

/// <summary>Optional accessibility bridge to the controller's canonical selection.</summary>
internal interface IAccessibleSelection
{
    /// <summary>Selects a half-open absolute UTF-16 interval without editing source.</summary>
    /// <remarks>
    /// Runs synchronously on the owning UI thread; never posts and waits. Both
    /// endpoints must match the current document identity and safe source boundaries.
    /// Active native composition is rejected before navigation or viewport mutation.
    /// Selected means the matching selection frame is published before return, not
    /// that keyboard focus was acquired. No history entry or document edit is made.
    /// </remarks>
    AccessibleSelectionResult TrySelect(AccessibleRange range);
}
