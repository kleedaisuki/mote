namespace Mote.Engine;

/// <summary>Closed, content-free boundaries of one serialized Save attempt.</summary>
public enum DocumentSavePhase
{
    /// <summary>Admission to the document's serialized Save gate.</summary>
    GateWait,
    /// <summary>Immutable snapshot and persistence state captured under the state lock.</summary>
    SnapshotCapture,
    /// <summary>Initial target identity and overwrite approval validation.</summary>
    TargetCheck,
    /// <summary>Creation and encoding of the owned temporary file.</summary>
    TempEncodeWrite,
    /// <summary>Flushing encoded bytes and the temporary file to the filesystem.</summary>
    TempFlush,
    /// <summary>Fingerprinting the exact temporary bytes.</summary>
    TempHash,
    /// <summary>Final existing-target identity verification before replacement.</summary>
    FinalTargetCheck,
    /// <summary>Non-overwriting installation of a new target.</summary>
    CommitMove,
    /// <summary>Replacement of an explicitly approved existing target.</summary>
    CommitReplace,
    /// <summary>Reading target metadata after a returned commit.</summary>
    SavedStamp,
    /// <summary>Updating saved state without marking concurrent edits clean.</summary>
    Bookkeeping,
    /// <summary>Deleting an owned temporary file before a commit attempt.</summary>
    FailureCleanup,
    /// <summary>Best-effort outcome inspection and retained-recovery bookkeeping.</summary>
    FailureInspection
}

/// <summary>Observed entry or terminal outcome of a Save phase, not the whole attempt.</summary>
public enum DocumentSaveEdge
{
    /// <summary>The instrumented boundary was entered.</summary>
    Entered,
    /// <summary>The phase returned normally.</summary>
    Succeeded,
    /// <summary>The phase threw a non-cancellation error.</summary>
    Failed,
    /// <summary>The phase threw cancellation.</summary>
    Cancelled,
    /// <summary>Bookkeeping was not applied because document lifetime ended.</summary>
    Skipped
}

/// <summary>Fixed phase evidence with the actual captured mutation version, never document contents.</summary>
/// <param name="Phase">The observed boundary.</param>
/// <param name="Edge">Entry or terminal outcome.</param>
/// <param name="SnapshotVersion">Null until snapshot capture has actually completed.</param>
/// <param name="HResult">Only a primary filesystem exception code; no exception text or data.</param>
public readonly record struct DocumentSaveObservation(
    DocumentSavePhase Phase, DocumentSaveEdge Edge, long? SnapshotVersion = null, int? HResult = null);

/// <summary>Optional content-free Save observations; this interface supplies neither policy nor I/O.</summary>
/// <remarks>
/// Callbacks are serial for an attempt and can run on background threads. They never run under
/// the document state lock, but can run while its Save gate is held. Callbacks must not block,
/// synchronously await another Save on this document, or acquire UI locks. Nonfatal observer
/// exceptions are ignored so diagnostics cannot change persistence outcomes.
/// </remarks>
public interface IDocumentSaveObserver
{
    /// <summary>Receives one fixed boundary and optional numeric evidence.</summary>
    void Observe(in DocumentSaveObservation observation);
}

/// <summary>Enabled-only phase bookkeeping; never retains document, snapshot, path or exception.</summary>
internal sealed class DocumentSaveObservationState(IDocumentSaveObserver observer)
{
    /// <summary>The sole currently entered phase, cleared before its terminal callback.</summary>
    private DocumentSavePhase? _active;
    /// <summary>Immutable captured mutation version; absent before actual capture succeeds.</summary>
    private long? _version;

    /// <summary>Records entry before the corresponding work executes.</summary>
    internal void Begin(DocumentSavePhase phase)
    {
        _active = phase;
        Emit(phase, DocumentSaveEdge.Entered);
    }

    /// <summary>Records completion, publishing a captured version only after the state lock is released.</summary>
    internal void Complete(long? capturedVersion = null, bool skipped = false)
    {
        if (capturedVersion is not null) _version = capturedVersion;
        if (_active is not { } phase) return;
        _active = null;
        Emit(phase, skipped ? DocumentSaveEdge.Skipped : DocumentSaveEdge.Succeeded);
    }

    /// <summary>Records the original active-phase failure without keeping or inspecting arbitrary error data.</summary>
    internal void Fail(Exception error)
    {
        if (_active is not { } phase) return;
        _active = null;
        Emit(phase, error is OperationCanceledException ? DocumentSaveEdge.Cancelled : DocumentSaveEdge.Failed,
            error is IOException or UnauthorizedAccessException ? error.HResult : null);
    }

    /// <summary>Diagnostics cannot turn a returned commit into an apparent Save failure.</summary>
    private void Emit(DocumentSavePhase phase, DocumentSaveEdge edge, int? hResult = null)
    {
        var observation = new DocumentSaveObservation(phase, edge, _version, hResult);
        try { observer.Observe(in observation); }
        catch (Exception error) when (error is not OutOfMemoryException) { }
    }
}
