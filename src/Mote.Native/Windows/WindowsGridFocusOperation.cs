using Mote.Telemetry;

namespace Mote.Native.Windows;

/// <summary>Enabled-only evidence source; reads never grant focus or source command authority.</summary>
internal interface IWindowsGridFocusEvidence
{
    /// <summary>Samples checked native pane and two independent owner relationships without retaining handles.</summary>
    TelemetryFocusSample CaptureFocusEvidence();
}

/// <summary>Transparent adapter-attempt observation; early provider refusals are outside this boundary.</summary>
internal static class WindowsGridFocusOperation
{
    /// <summary>Invokes the original action once, with no evidence query or allocation on the disabled path.</summary>
    internal static GridAccessibilityResult Invoke(IGridAccessibilityActions? actions, GridAccessibilityId id, GridCoordinate? cell)
    {
        var health = MoteTelemetry.Health;
        if (!health.Enabled || health.SinkFaulted)
            return actions?.Focus(id, cell) ?? GridAccessibilityResult.Unavailable;
        return Observe(actions, id, cell);
    }

    /// <summary>Contains optional observations separately from the original action's return and exception.</summary>
    private static GridAccessibilityResult Observe(IGridAccessibilityActions? actions, GridAccessibilityId id, GridCoordinate? cell)
    {
        var evidence = actions as IWindowsGridFocusEvidence;
        var request = Begin(evidence, cell is null ? TelemetryFocusTarget.Table : TelemetryFocusTarget.Cell);
        try
        {
            var result = actions?.Focus(id, cell) ?? GridAccessibilityResult.Unavailable;
            End(request, evidence, result);
            return result;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            EndFault(request, evidence);
            throw;
        }
    }

    /// <summary>Unknown observations remain unknown; query failures do not change the action outcome.</summary>
    private static TelemetryFocusSample Capture(IWindowsGridFocusEvidence? evidence)
    {
        try { return evidence?.CaptureFocusEvidence() ?? default; }
        catch (Exception error) when (error is not OutOfMemoryException) { return default; }
    }

    /// <summary>Starts a session-owned receipt without installing ambient activity or retaining native evidence.</summary>
    private static NativeGridFocusRequest? Begin(IWindowsGridFocusEvidence? evidence, TelemetryFocusTarget target)
    {
        try { return MoteTelemetry.BeginNativeGridFocus(target, Capture(evidence)); }
        catch (Exception error) when (error is not OutOfMemoryException) { return null; }
    }

    /// <summary>Preserves the exact closed adapter result before its existing HRESULT translation.</summary>
    private static void End(NativeGridFocusRequest? request, IWindowsGridFocusEvidence? evidence, GridAccessibilityResult result)
    {
        if (request is null) return;
        try { request.EndOnce(Outcome(result), Capture(evidence).Pane); }
        catch (Exception error) when (error is not OutOfMemoryException) { /* Optional evidence never changes the action return. */ }
    }

    /// <summary>Records an original action exception as Fault, not as an invented adapter result.</summary>
    private static void EndFault(NativeGridFocusRequest? request, IWindowsGridFocusEvidence? evidence)
    {
        if (request is null) return;
        try { request.EndOnce(TelemetryFocusOutcome.Fault, Capture(evidence).Pane); }
        catch (Exception error) when (error is not OutOfMemoryException) { /* Preserve the original throw and COM conversion. */ }
    }

    /// <summary>Rejects unknown values without fabricating an adapter outcome or changing the original return.</summary>
    private static TelemetryFocusOutcome Outcome(GridAccessibilityResult result) => result switch
    {
        GridAccessibilityResult.Applied => TelemetryFocusOutcome.Applied,
        GridAccessibilityResult.NoChange => TelemetryFocusOutcome.NoChange,
        GridAccessibilityResult.Unsupported => TelemetryFocusOutcome.Unsupported,
        GridAccessibilityResult.Stale => TelemetryFocusOutcome.Stale,
        GridAccessibilityResult.NotReady => TelemetryFocusOutcome.NotReady,
        GridAccessibilityResult.InvalidCoordinate => TelemetryFocusOutcome.InvalidCoordinate,
        GridAccessibilityResult.Unavailable => TelemetryFocusOutcome.Unavailable,
        GridAccessibilityResult.CompositionBlocked => TelemetryFocusOutcome.CompositionBlocked,
        _ => throw new ArgumentOutOfRangeException(nameof(result))
    };
}
