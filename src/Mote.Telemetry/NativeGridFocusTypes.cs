namespace Mote.Telemetry;

/// <summary>Closed thread relationship; no thread identity is retained.</summary>
public enum TelemetryFocusThreadRelation
{
    /// <summary>Evidence is unavailable.</summary>
    Unknown,
    /// <summary>The observed thread matches the owner.</summary>
    Owner,
    /// <summary>The observed thread differs from the owner.</summary>
    NonOwner
}

/// <summary>Identity-checked physical focus category, never a window handle.</summary>
public enum TelemetryFocusPane
{
    /// <summary>Native evidence could not be obtained.</summary>
    Unavailable,
    /// <summary>No native focus window exists.</summary>
    None,
    /// <summary>The source editor owns focus.</summary>
    Source,
    /// <summary>The table owns focus.</summary>
    Table,
    /// <summary>The row scroller owns focus.</summary>
    RowScroller,
    /// <summary>The column scroller owns focus.</summary>
    ColumnScroller,
    /// <summary>The coordinate control owns focus.</summary>
    Coordinate,
    /// <summary>Another identity-checked application window owns focus.</summary>
    OwnedOther,
    /// <summary>A window outside the checked application owns focus.</summary>
    Outside
}

/// <summary>Closed adapter focus target without cell coordinates.</summary>
public enum TelemetryFocusTarget
{
    /// <summary>The table is the requested target.</summary>
    Table,
    /// <summary>A cell is the requested target.</summary>
    Cell
}

/// <summary>Actual adapter result before HRESULT mapping; Fault denotes an exception, not an adapter result.</summary>
public enum TelemetryFocusOutcome
{
    /// <summary>The adapter applied the request.</summary>
    Applied,
    /// <summary>The adapter required no change.</summary>
    NoChange,
    /// <summary>The adapter does not support the request.</summary>
    Unsupported,
    /// <summary>The adapter identity is stale.</summary>
    Stale,
    /// <summary>The adapter is not ready.</summary>
    NotReady,
    /// <summary>The adapter rejected the coordinate.</summary>
    InvalidCoordinate,
    /// <summary>The adapter is unavailable.</summary>
    Unavailable,
    /// <summary>Composition blocked the request.</summary>
    CompositionBlocked,
    /// <summary>The adapter invocation threw an exception.</summary>
    Fault
}

/// <summary>Content-free admission and native focus evidence sampled before the adapter call.</summary>
/// <param name="NativeThreadRelation">Callback thread versus native window creation thread.</param>
/// <param name="ManagedAdmissionRelation">Callback thread versus existing managed owner admission.</param>
/// <param name="Pane">Identity-checked physical focus category.</param>
public readonly record struct TelemetryFocusSample(
    TelemetryFocusThreadRelation NativeThreadRelation,
    TelemetryFocusThreadRelation ManagedAdmissionRelation,
    TelemetryFocusPane Pane);

/// <summary>Writer-only typed payload for the two closed focus operations.</summary>
internal readonly record struct NativeGridFocusAttributes(TelemetryFocusTarget Target,
    TelemetryFocusSample Before, TelemetryFocusOutcome? Outcome = null, TelemetryFocusPane? After = null);
