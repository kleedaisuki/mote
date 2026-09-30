namespace Mote.Telemetry;

/// <summary>
/// A UI-thread-confined, single-pending causal interval for source drawing.
/// Identity is internal correlation only: generation is never serialized.
/// Replaced or closed intervals are cancelled, not assigned the next revision's draw.
/// </summary>
public sealed class NativeDrawTrace
{
    private TelemetryMark _mark;
    private TelemetryOperation _operation;
    private TelemetryDimensions _dimensions;
    private long _generation;
    private long _version;
    private long _sequence;

    /// <summary>Cheap UI-thread fast path; inactive tracing must not trigger OS paint queries.</summary>
    public bool IsPending => _mark.IsActive;

    /// <summary>
    /// Arms one interval before installing its source revision. The supplied mark
    /// must belong only to this endpoint (normally MoteTelemetry.Fork(editMark)).
    /// No queue, clock read, or allocation occurs when the mark is inactive.
    /// </summary>
    public void Arm(TelemetryOperation operation, TelemetryMark mark,
        long generation, long version, TelemetryDimensions dimensions = default)
    {
        Cancel();
        if (!mark.IsActive) return;
        if (operation is not (TelemetryOperation.EditToDrawSubmission or
            TelemetryOperation.OpenToDrawSubmission))
            throw new ArgumentOutOfRangeException(nameof(operation));
        _operation = operation;
        _mark = mark;
        _generation = generation;
        _version = version;
        _dimensions = dimensions with { Version = version };
        ++_sequence;
    }

    /// <summary>Cancels pending work only if a different document revision is being installed.</summary>
    public void ObserveDocument(long generation, long version)
    {
        if (_mark.IsActive && (generation != _generation || version != _version)) Cancel();
    }

    /// <summary>
    /// Captures a draw ticket only after the adapter has installed this exact
    /// source revision. Call before native drawing, never for preview or chrome.
    /// </summary>
    public long BeginDraw(long generation, long version) =>
        _mark.IsActive && generation == _generation && version == _version ? _sequence : 0;

    /// <summary>
    /// Completes after native drawing returns normally and the installed identity
    /// is unchanged. A stale, duplicate, failed, or reentrant draw cannot complete it.
    /// This is CPU draw submission, not compositor presentation or physical paint.
    /// </summary>
    public void CompleteDraw(long ticket, long generation, long version)
    {
        if (ticket == 0 || ticket != _sequence || BeginDraw(generation, version) != ticket) return;
        var mark = _mark;
        _mark = default;
        MoteTelemetry.RecordElapsed(_operation, mark, _dimensions);
    }

    /// <summary>Ends one replaced or closing interval without waiting for disk; repeated calls are inert.</summary>
    public void Cancel()
    {
        if (!_mark.IsActive) return;
        var mark = _mark;
        _mark = default;
        MoteTelemetry.RecordElapsed(_operation, mark, _dimensions, TelemetryStatus.Cancelled);
    }
}
