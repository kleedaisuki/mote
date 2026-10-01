using Mote.Engine;
using Mote.Formats;
using Mote.Telemetry;

namespace Mote.Native;

internal sealed partial class NativeEditorController
{
    /// <summary>Only the admitted request owns busy release; rejected requests cannot release it.</summary>
    private long _saveAttemptSerial;
    /// <summary>Enabled-only request lifetime; disposal/replacement ends UI intent, not the file commit.</summary>
    private TelemetryRequest? _activeSaveTrace;

    /// <summary>Admits the target callback without changing composition, picker, or recovery policy.</summary>
    private void StartSave(NativeSaveRequest request)
    {
        var trace = request.Trace;
        trace?.Checkpoint(TelemetryEvent.SaveControllerEntered);
        if (_disposed) { trace?.EndOnce(TelemetryStatus.Cancelled, TelemetryReason.LifetimeEnded); return; }
        var document = _document;
        var generation = _canvasGeneration;
        var settled = _shell.CommitPendingText();
        if (!SaveStillCurrent(document, generation, trace, default)) return;
        if (!settled)
        {
            trace?.Checkpoint(TelemetryEvent.SaveCompositionBlocked, status: TelemetryStatus.Skipped);
            trace?.EndOnce(TelemetryStatus.Skipped, TelemetryReason.CompositionBlocked);
            return;
        }
        trace?.Checkpoint(TelemetryEvent.SaveCompositionSettled);
        if (_saving) { trace?.EndOnce(TelemetryStatus.Skipped, TelemetryReason.AlreadySaving); return; }
        if (_document.PendingSaveRecovery is { } recovery)
        {
            trace?.EndOnce(TelemetryStatus.Skipped, TelemetryReason.RecoveryRedirected);
            StartRecoveryExport(_document, recovery);
            return;
        }
        var pickerWasUsed = request.Kind == NativeSaveKind.SaveAs || _document.FilePath is null;
        var path = pickerWasUsed ? _shell.PickSaveFile(_document.FilePath) : _document.FilePath;
        if (!SaveStillCurrent(document, generation, trace, default)) return;
        if (path is null) { trace?.EndOnce(TelemetryStatus.Cancelled, TelemetryReason.PickerCancelled); return; }
        // A modal picker can reenter command dispatch and admit another Save.
        if (_saving) { trace?.EndOnce(TelemetryStatus.Skipped, TelemetryReason.AlreadySaving); return; }
        var samePath = document.FilePath is { } current && string.Equals(
            Path.GetFullPath(current), Path.GetFullPath(path),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        _saving = true;
        _activeSaveTrace = trace;
        var attempt = ++_saveAttemptSerial;
        trace?.Checkpoint(TelemetryEvent.SaveAdmitted, Dimensions(document.Snapshot));
        NativeSaveDiagnostic.Record(NativeSaveDiagnosticStage.ControllerAdmitted);
        _ = Task.Run(() => RunSaveAsync(document, generation, attempt, path, pickerWasUsed, samePath, trace));
    }

    /// <summary>Explicit context crosses the worker boundary; no ambient request Activity is retained.</summary>
    private async Task RunSaveAsync(Document document, long generation, long attempt, string path,
        bool pickerWasUsed, bool samePath, TelemetryRequest? trace)
    {
        trace?.Checkpoint(TelemetryEvent.SaveWorkerStarted);
        var saveMark = trace?.BeginPhase(TelemetryOperation.Save) ?? default;
        var observer = trace is null ? null : new NativeSaveObserver(trace, saveMark);
        var status = TelemetryStatus.Cancelled;
        var reason = TelemetryReason.OverwriteDeclined;
        Exception? error = null;
        try
        {
            var approved = true;
            FileOverwriteToken? overwrite = null;
            if (pickerWasUsed && !samePath && File.Exists(path))
            {
                trace?.Checkpoint(TelemetryEvent.SaveOverwriteRequested);
                overwrite = await FileOverwriteToken.CaptureAsync(path).ConfigureAwait(false);
                approved = await ConfirmSaveOverwriteAsync(path, document, generation, trace).ConfigureAwait(false);
                trace?.Checkpoint(approved ? TelemetryEvent.SaveOverwriteApproved : TelemetryEvent.SaveOverwriteDeclined,
                    status: approved ? TelemetryStatus.Success : TelemetryStatus.Cancelled);
            }
            if (approved)
            {
                if (overwrite is null) await document.SaveAsync(path, default, observer).ConfigureAwait(false);
                else await document.SaveOverAsync(overwrite, default, observer).ConfigureAwait(false);
                status = TelemetryStatus.Success;
                reason = TelemetryReason.Completed;
            }
        }
        catch (OperationCanceledException ex)
        {
            error = ex;
            status = TelemetryStatus.Cancelled;
            reason = TelemetryReason.OperationCancelled;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            error = ex;
            status = TelemetryStatus.Failure;
            reason = TelemetryReason.SaveFailed;
            MoteTelemetry.RecordSaveFailure(ex, saveMark,
                new TelemetryDimensions(Version: observer?.SnapshotVersion));
        }
        finally
        {
            MoteTelemetry.EndPhase(TelemetryOperation.Save, saveMark,
                new TelemetryDimensions(Version: observer?.SnapshotVersion), status,
                error is IOException or UnauthorizedAccessException ? error.HResult : null);
        }
        var savedVersion = observer?.SnapshotVersion;
        if (!TryPost(() => CompleteSave(document, generation, attempt, trace, savedVersion, error, status, reason)))
        {
            trace?.EndOnce(TelemetryStatus.Failure, TelemetryReason.UiPostFailed,
                new TelemetryDimensions(Version: savedVersion));
            return;
        }
        trace?.Checkpoint(TelemetryEvent.SaveUiPostReturned, new TelemetryDimensions(Version: savedVersion));
    }

    /// <summary>
    /// Receipt means callback entry only; stale work cannot select policy or publish success.
    /// Nonfatal presentation failures end the request without unwinding into the native dispatcher;
    /// they never undo a file commit or recursively retry a failed error dialog.
    /// </summary>
    private void CompleteSave(Document document, long generation, long attempt, TelemetryRequest? trace,
        long? savedVersion, Exception? error, TelemetryStatus status, TelemetryReason reason)
    {
        var dimensions = new TelemetryDimensions(Version: savedVersion);
        trace?.Checkpoint(TelemetryEvent.SaveUiStarted, dimensions);
        if (attempt == _saveAttemptSerial) { _saving = false; _activeSaveTrace = null; }
        if (!SaveStillCurrent(document, generation, trace, dimensions)) return;
        try
        {
            if (error is not null) { _shell.ShowError(SaveFailureMessage(error)); trace?.EndOnce(status, reason, dimensions); return; }
            if (status != TelemetryStatus.Success) { trace?.EndOnce(status, reason, dimensions); return; }
            if (document.FilePath is { } savedPath) SelectPolicy(DocumentPolicies.ForPath(savedPath));
            if (!SaveStillCurrent(document, generation, trace, dimensions)) return;
            ScheduleAnalysis();
            if (!SaveStillCurrent(document, generation, trace, dimensions)) return;
            var settled = SettleInputBeforeAsyncResult();
            if (!SaveStillCurrent(document, generation, trace, dimensions)) return;
            if (!settled)
            {
                _operationStatus = "Save view update postponed during text composition.";
                ShowDocument();
                if (!SaveStillCurrent(document, generation, trace, dimensions)) return;
                trace?.Checkpoint(TelemetryEvent.SaveUiDeferred, dimensions);
                trace?.Checkpoint(TelemetryEvent.SaveCompleted, dimensions);
                trace?.EndOnce(TelemetryStatus.Success, TelemetryReason.ViewDeferred, dimensions);
                return;
            }
            if (!document.IsModified) _recoveryExported = false;
            if (_operationStatus == "Save view update postponed during text composition.") _operationStatus = "";
            ShowDocument();
            if (!SaveStillCurrent(document, generation, trace, dimensions)) return;
            trace?.Checkpoint(TelemetryEvent.SaveCompleted, dimensions);
            trace?.EndOnce(TelemetryStatus.Success, TelemetryReason.Completed, dimensions);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            trace?.EndOnce(TelemetryStatus.Failure, TelemetryReason.CallbackFailed, dimensions);
            // This callback returns through the native UI dispatcher. A committed
            // file is not rolled back by presentation failure, and retrying error
            // UI here could fail recursively. Retain the failed UI terminal only.
        }
    }

    /// <summary>Native calls may reenter; revalidate before touching the current view or certifying completion.</summary>
    private bool SaveStillCurrent(Document document, long generation, TelemetryRequest? trace,
        TelemetryDimensions dimensions)
    {
        if (_disposed)
        { trace?.EndOnce(TelemetryStatus.Cancelled, TelemetryReason.LifetimeEnded, dimensions); return false; }
        if (ReferenceEquals(document, _document) && generation == _canvasGeneration) return true;
        trace?.EndOnce(TelemetryStatus.Cancelled, TelemetryReason.StaleDocument, dimensions);
        return false;
    }

    /// <summary>Approval remains on the UI thread and is revoked by replacement or closure.</summary>
    private Task<bool> ConfirmSaveOverwriteAsync(string path, Document document, long generation, TelemetryRequest? trace)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!TryPost(() =>
        {
            try
            {
                if (!SaveStillCurrent(document, generation, trace, default))
                { completion.TrySetResult(false); return; }
                var approved = _shell.ConfirmOverwrite(path);
                // Native modal approval can dispatch replacement/closure before returning.
                completion.TrySetResult(SaveStillCurrent(document, generation, trace, default) && approved);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            { completion.TrySetException(error); }
        }))
        {
            trace?.EndOnce(TelemetryStatus.Failure, TelemetryReason.UiPostFailed);
            completion.TrySetResult(false);
        }
        return completion.Task;
    }

    /// <summary>Maps serial Engine observations to explicit child phases without retaining source or paths.</summary>
    private sealed class NativeSaveObserver(TelemetryRequest request, TelemetryMark parent) : IDocumentSaveObserver
    {
        private TelemetryMark _phase;
        /// <summary>The exact snapshot captured by the Engine, never a later UI version.</summary>
        internal long? SnapshotVersion { get; private set; }

        /// <inheritdoc />
        public void Observe(in DocumentSaveObservation observation)
        {
            var operation = Operation(observation.Phase);
            var dimensions = new TelemetryDimensions(Version: observation.SnapshotVersion ?? SnapshotVersion);
            if (observation.Edge == DocumentSaveEdge.Entered)
            { _phase = MoteTelemetry.BeginPhase(operation, parent, dimensions); return; }
            if (observation.Phase == DocumentSavePhase.SnapshotCapture && observation.Edge == DocumentSaveEdge.Succeeded)
            {
                SnapshotVersion = observation.SnapshotVersion;
                request.Checkpoint(TelemetryEvent.SaveSnapshotCaptured, dimensions);
            }
            var status = observation.Edge switch
            {
                DocumentSaveEdge.Succeeded => TelemetryStatus.Success,
                DocumentSaveEdge.Cancelled => TelemetryStatus.Cancelled,
                DocumentSaveEdge.Skipped => TelemetryStatus.Skipped,
                _ => TelemetryStatus.Failure
            };
            MoteTelemetry.EndPhase(operation, _phase, dimensions, status, observation.HResult);
            _phase = default;
        }

        /// <summary>Only closed engine phases can determine operation names.</summary>
        private static TelemetryOperation Operation(DocumentSavePhase phase) => phase switch
        {
            DocumentSavePhase.GateWait => TelemetryOperation.SaveGateWait,
            DocumentSavePhase.SnapshotCapture => TelemetryOperation.SaveSnapshotCapture,
            DocumentSavePhase.TargetCheck => TelemetryOperation.SaveTargetCheck,
            DocumentSavePhase.TempEncodeWrite => TelemetryOperation.SaveTempEncodeWrite,
            DocumentSavePhase.TempFlush => TelemetryOperation.SaveTempFlush,
            DocumentSavePhase.TempHash => TelemetryOperation.SaveTempHash,
            DocumentSavePhase.FinalTargetCheck => TelemetryOperation.SaveFinalTargetCheck,
            DocumentSavePhase.CommitMove => TelemetryOperation.SaveCommitMove,
            DocumentSavePhase.CommitReplace => TelemetryOperation.SaveCommitReplace,
            DocumentSavePhase.SavedStamp => TelemetryOperation.SaveSavedStamp,
            DocumentSavePhase.Bookkeeping => TelemetryOperation.SaveBookkeeping,
            DocumentSavePhase.FailureCleanup => TelemetryOperation.SaveFailureCleanup,
            DocumentSavePhase.FailureInspection => TelemetryOperation.SaveFailureInspection,
            _ => throw new ArgumentOutOfRangeException(nameof(phase))
        };
    }
}
