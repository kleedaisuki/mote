using System.Diagnostics;

namespace Mote.Telemetry;

/// <summary>Session-only, content-free native menu observation checkpoints.</summary>
public static partial class MoteTelemetry
{
    /// <summary>
    /// Records one of the five fixed native menu checkpoints under the current
    /// session, never an ambient operation. Entry and return are independent
    /// positives: no menu-to-command or entry-to-return identity is invented.
    /// </summary>
    /// <remarks>Success means checkpoint execution, including a false menu return.
    /// No input contents, event identity or caller dimensions are accepted.</remarks>
    public static void RecordNativeMenuCheckpoint(TelemetryEvent kind)
    {
        if (kind is not (TelemetryEvent.NativeMenuObservationReady or
            TelemetryEvent.NativeMenuObservationUnavailable or
            TelemetryEvent.NativeMenuSaveFamilyEntered or
            TelemetryEvent.NativeMenuSaveFamilyReturnedTrue or
            TelemetryEvent.NativeMenuSaveFamilyReturnedFalse))
            throw new ArgumentOutOfRangeException(nameof(kind));
        var sink = Volatile.Read(ref _sink);
        if (sink is null || sink.IsFaulted || !sink.TryAcquireProducer()) return;
        try
        {
            sink.TryRecord(new TraceRecord(DateTimeOffset.UtcNow, sink.SessionTraceId,
                ActivitySpanId.CreateRandom(), sink.SessionSpanId, EventName(kind),
                0, TelemetryStatus.Success, default));
        }
        finally { sink.ReleaseProducer(); }
    }
}
