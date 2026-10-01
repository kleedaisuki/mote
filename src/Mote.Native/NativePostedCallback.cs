using Mote.Telemetry;

namespace Mote.Native;

/// <summary>Contains nonfatal posted UI faults without retrying callbacks or changing disk results.</summary>
internal static class NativePostedCallback
{
    /// <summary>
    /// Invokes exactly once, returning the original nonfatal exception or null.
    /// Success performs no instrumentation or allocation. Out-of-memory failures
    /// retain the native adapter's fatal policy and are never converted to notices.
    /// </summary>
    internal static Exception? Invoke(Action callback)
    {
        try { callback(); return null; }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            RecordFailure(TelemetryEvent.NativePostedCallbackFailed);
            return error;
        }
    }

    /// <summary>Attempts optional presentation once; secondary nonfatal faults cannot escape the pump.</summary>
    internal static void Report(Exception error, Action<Exception> reporter)
    {
        try { reporter(error); }
        catch (Exception secondary) when (secondary is not OutOfMemoryException)
        { RecordFailure(TelemetryEvent.NativePostedCallbackReportFailed); }
    }

    /// <summary>Observability is optional and never reads exception text, Data, or user content.</summary>
    private static void RecordFailure(TelemetryEvent kind)
    {
        try { MoteTelemetry.RecordNativePostedFailure(kind); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { /* A nonfatal telemetry failure must not become a native callback failure. */ }
    }
}
