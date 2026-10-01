namespace Mote.Telemetry;

/// <summary>Session-only, content-free checkpoints for an optional native local input monitor.</summary>
public static partial class MoteTelemetry
{
    /// <summary>
    /// Records one fixed local-monitor checkpoint under the current session,
    /// independent of ambient operations. No event identity, input contents,
    /// dimensions, or menu-to-command correlation is accepted.
    /// </summary>
    /// <remarks>
    /// Success certifies ready, candidate observation, or removal return only;
    /// failure certifies an instrumentation setup, callback, or removal failure.
    /// Neither outcome determines whether Save was requested or completed.
    /// Valid calls are allocation-free when tracing is disabled.
    /// </remarks>
    public static void RecordNativeInputCheckpoint(TelemetryEvent kind)
    {
        var status = kind switch
        {
            TelemetryEvent.NativeInputMonitorReady or
            TelemetryEvent.NativeInputSaveFamilyCandidate or
            TelemetryEvent.NativeInputMonitorRemoved => TelemetryStatus.Success,
            TelemetryEvent.NativeInputMonitorUnavailable or
            TelemetryEvent.NativeInputMonitorCallbackFailed or
            TelemetryEvent.NativeInputMonitorRemovalFailed => TelemetryStatus.Failure,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        RecordSessionCheckpoint(kind, status);
    }
}
