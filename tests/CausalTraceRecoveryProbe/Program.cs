using Mote.Telemetry;

/// <summary>
/// Owned diagnostic child; emits a received request and nested phase entry before
/// deliberately waiting forever. It performs no document or user-profile I/O.
/// The parent must observe the live trace before terminating this process.
/// </summary>
internal static class Program
{
    /// <summary>Accepts an isolated repository-local trace directory and held/normal mode.</summary>
    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 2 || args[1] is not ("held" or "normal" or "receipt-save" or "receipt-save-as")) return 2;
        MoteTelemetry.Configure(new TelemetryOptions { Enabled = true, OutputDirectory = Path.GetFullPath(args[0]) });
        var request = MoteTelemetry.BeginRequest(args[1] == "receipt-save-as"
            ? TelemetryOperation.CommandSaveAs : TelemetryOperation.CommandSave);
        if (request is null) return 3;
        if (args[1] is "receipt-save" or "receipt-save-as")
        {
            await Task.Delay(Timeout.InfiniteTimeSpan);
            return 4;
        }
        request.Checkpoint(TelemetryEvent.SaveAdmitted);
        request.Checkpoint(TelemetryEvent.SaveWorkerStarted);
        var coarse = request.BeginPhase(TelemetryOperation.Save);
        var phase = MoteTelemetry.BeginPhase(TelemetryOperation.SaveTempFlush, coarse);
        if (args[1] == "held")
        {
            await Task.Delay(Timeout.InfiniteTimeSpan);
            return 4;
        }
        MoteTelemetry.EndPhase(TelemetryOperation.SaveTempFlush, phase);
        MoteTelemetry.EndPhase(TelemetryOperation.Save, coarse);
        request.Checkpoint(TelemetryEvent.SaveSnapshotCaptured, new TelemetryDimensions(Version: 7));
        request.Checkpoint(TelemetryEvent.SaveUiStarted);
        request.Checkpoint(TelemetryEvent.SaveCompleted);
        request.EndOnce(TelemetryStatus.Success, TelemetryReason.Completed);
        await MoteTelemetry.ShutdownAsync();
        return 0;
    }
}
