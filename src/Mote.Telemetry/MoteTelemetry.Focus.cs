using System.Diagnostics;

namespace Mote.Telemetry;

public static partial class MoteTelemetry
{
    /// <summary>
    /// Records an adapter-specific receipt under the session, independent of ambient Activity.
    /// Disabled tracing returns null before inspecting evidence and allocates nothing.
    /// No producer lease survives this call; this does not observe earlier provider refusals.
    /// </summary>
    /// <example><code>
    /// var request = MoteTelemetry.BeginNativeGridFocus(TelemetryFocusTarget.Table, sample);
    /// request?.EndOnce(TelemetryFocusOutcome.Applied, TelemetryFocusPane.Table);
    /// </code></example>
    public static NativeGridFocusRequest? BeginNativeGridFocus(
        TelemetryFocusTarget target, TelemetryFocusSample before)
    {
        var sink = Volatile.Read(ref _sink);
        if (sink is null || sink.IsFaulted || !sink.TryAcquireProducer()) return null;
        try
        {
            ValidateFocus(target, nameof(target));
            ValidateFocus(before.NativeThreadRelation, nameof(before));
            ValidateFocus(before.ManagedAdmissionRelation, nameof(before));
            ValidateFocus(before.Pane, nameof(before));
            var mark = new TelemetryMark(sink, Stopwatch.GetTimestamp(), sink.SessionTraceId,
                ActivitySpanId.CreateRandom(), sink.SessionSpanId);
            var attributes = new NativeGridFocusAttributes(target, before);
            var request = new NativeGridFocusRequest(mark, attributes);
            sink.TryRecord(new TraceRecord(DateTimeOffset.UtcNow, mark.TraceId, mark.SpanId,
                mark.ParentSpanId, "native.grid.focus.adapter.received", 0,
                TelemetryStatus.Success, default, Focus: attributes));
            return request;
        }
        finally { sink.ReleaseProducer(); }
    }

    /// <summary>Rejects non-domain enum values before they can reach the writer.</summary>
    internal static void ValidateFocus<T>(T value, string name) where T : struct, Enum
    {
        if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(name);
    }

    /// <summary>Attempts a terminal child only in its original session; no ambient fallback is allowed.</summary>
    internal static void EndNativeGridFocus(TelemetryMark mark, NativeGridFocusAttributes attributes)
    {
        if (!TryAcquireOriginal(mark, out var sink)) return;
        try
        {
            var status = attributes.Outcome is TelemetryFocusOutcome.Applied or TelemetryFocusOutcome.NoChange
                ? TelemetryStatus.Success : TelemetryStatus.Failure;
            sink!.TryRecord(new TraceRecord(DateTimeOffset.UtcNow, mark.TraceId,
                ActivitySpanId.CreateRandom(), mark.SpanId, "native.grid.focus.adapter",
                Stopwatch.GetElapsedTime(mark.Timestamp).Ticks / 10, status, default, Focus: attributes));
        }
        finally { sink!.ReleaseProducer(); }
    }
}

/// <summary>Enabled-only receipt owner holding fixed evidence, not native objects or a long producer lease.</summary>
public sealed class NativeGridFocusRequest
{
    /// <summary>The original receipt identity and timestamp.</summary>
    private readonly TelemetryMark _mark;
    /// <summary>Closed evidence retained for the terminal child.</summary>
    private readonly NativeGridFocusAttributes _attributes;
    /// <summary>Atomic terminal-attempt ownership.</summary>
    private int _ended;

    /// <summary>Captures the immutable receipt context.</summary>
    internal NativeGridFocusRequest(TelemetryMark mark, NativeGridFocusAttributes attributes)
    {
        _mark = mark;
        _attributes = attributes;
    }

    /// <summary>
    /// Selects one terminal enqueue attempt. True means ownership, not persistence.
    /// Applied and NoChange succeed; all other results, including Fault, fail.
    /// Invalid evidence is rejected without consuming terminal ownership.
    /// </summary>
    public bool EndOnce(TelemetryFocusOutcome outcome, TelemetryFocusPane after)
    {
        MoteTelemetry.ValidateFocus(outcome, nameof(outcome));
        MoteTelemetry.ValidateFocus(after, nameof(after));
        if (Interlocked.CompareExchange(ref _ended, 1, 0) != 0) return false;
        MoteTelemetry.EndNativeGridFocus(_mark, _attributes with { Outcome = outcome, After = after });
        return true;
    }
}
