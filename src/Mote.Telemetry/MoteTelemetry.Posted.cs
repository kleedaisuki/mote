using System.Diagnostics;

namespace Mote.Telemetry;

/// <summary>Content-free failure checkpoints for native posted UI callbacks.</summary>
public static partial class MoteTelemetry
{
    /// <summary>
    /// Records one fixed posted-callback failure under the current session.
    /// No exception metadata, request identity, duration, or disk result is accepted.
    /// </summary>
    public static void RecordNativePostedFailure(TelemetryEvent kind)
    {
        if (kind is not (TelemetryEvent.NativePostedCallbackFailed or
            TelemetryEvent.NativePostedCallbackReportFailed))
            throw new ArgumentOutOfRangeException(nameof(kind));
        RecordSessionCheckpoint(kind, TelemetryStatus.Failure);
    }

    /// <summary>Writes a fixed checkpoint independent of ambient request context with a short producer lease.</summary>
    private static void RecordSessionCheckpoint(TelemetryEvent kind, TelemetryStatus status)
    {
        var sink = Volatile.Read(ref _sink);
        if (sink is null || sink.IsFaulted || !sink.TryAcquireProducer()) return;
        try
        {
            sink.TryRecord(new TraceRecord(DateTimeOffset.UtcNow, sink.SessionTraceId,
                ActivitySpanId.CreateRandom(), sink.SessionSpanId, EventName(kind),
                0, status, default));
        }
        finally { sink.ReleaseProducer(); }
    }
}
