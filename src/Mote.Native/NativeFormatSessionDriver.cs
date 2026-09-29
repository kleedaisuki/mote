using Mote.Engine;
using Mote.Formats;

namespace Mote.Native;

/// <summary>
/// Owns one format session and transports ordered document edits from the UI thread
/// to serialized background analyses. A missing edit chain rebuilds parser state
/// from the authoritative snapshot rather than risking stale semantic facts.
/// </summary>
/// <remarks>
/// The driver belongs to exactly one document lifetime. Call <see cref="Record"/>
/// for every <see cref="Document.Changed"/> event before requesting analysis of
/// that version. Results are not published here: the caller must reject results
/// whose document identity or version is no longer current.
/// </remarks>
internal sealed class NativeFormatSessionDriver : IDisposable
{
    private const int MaxPendingEdits = 256;
    private const int MaxPendingInsertUnits = 1024 * 1024;

    private readonly object _gate = new();
    private readonly SemaphoreSlim _analysisGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly IIncrementalDocumentPolicy _policy;
    private readonly List<VersionedEdit> _edits = [];
    private IFormatSession? _session;
    private long? _committedVersion;
    private int _pendingInsertUnits;
    private int _pendingAnalyses;
    private bool _disposed;
    private bool _cancellationFinished;

    /// <summary>Creates an isolated parser session for a single open document.</summary>
    public NativeFormatSessionDriver(IIncrementalDocumentPolicy policy)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _session = policy.CreateSession();
    }

    /// <summary>
    /// Records a committed mutation in delivery order. History is bounded; exceeding
    /// the bound only forfeits incremental reuse, never correctness.
    /// </summary>
    public void Record(DocumentChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        lock (_gate)
        {
            if (_disposed) return;
            var before = change.Before.Version;
            var after = change.After.Version;
            if (after <= before || change.Change.InsertText is null)
            {
                ClearEdits();
                return;
            }
            if (_committedVersion is long committed && after <= committed) return;
            if (_edits.Count > 0 && _edits[^1].AfterVersion != before) ClearEdits();
            if (_edits.Count == MaxPendingEdits ||
                change.Change.InsertText.Length > MaxPendingInsertUnits - _pendingInsertUnits)
                ClearEdits();
            // A single oversized insertion cannot fit the budget; a subsequent
            // request will rebuild from its immutable snapshot instead.
            if (change.Change.InsertText.Length > MaxPendingInsertUnits) return;
            _edits.Add(new VersionedEdit(before, after, change.Change));
            _pendingInsertUnits += change.Change.InsertText.Length;
        }
    }

    /// <summary>
    /// Analyzes on the thread pool without blocking native event dispatch. A canceled
    /// call is not published by this driver; cancellation and close are advisory to
    /// the format session, which cooperatively observes the supplied token.
    /// </summary>
    public Task<DocumentAnalysis> AnalyzeAsync(TextSnapshot snapshot, AnalysisRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ++_pendingAnalyses;
        }
        return Task.Run(() => AnalyzeCoreAsync(snapshot, request, cancellationToken));
    }

    private async Task<DocumentAnalysis> AnalyzeCoreAsync(TextSnapshot snapshot,
        AnalysisRequest request, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
            _lifetime.Token);
        var entered = false;
        try
        {
            await _analysisGate.WaitAsync(linked.Token).ConfigureAwait(false);
            entered = true;
            VersionedEdit[] changes;
            bool rebuild;
            lock (_gate)
            {
                linked.Token.ThrowIfCancellationRequested();
                if (_committedVersion is long committed && snapshot.Version < committed)
                    throw new OperationCanceledException("The analysis snapshot was superseded.", linked.Token);
                changes = ChangesTo(snapshot.Version, out rebuild);
            }

            if (rebuild) RebuildSession();
            linked.Token.ThrowIfCancellationRequested();
            DocumentAnalysis result;
            try
            {
                result = _session!.Analyze(snapshot, changes, request, linked.Token);
                if (result.Version != snapshot.Version)
                    throw new InvalidOperationException("The format session returned a different snapshot version.");
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                // IFormatSession promises canceled calls do not commit. Retain its
                // prior state; queued requests still have the same edit baseline.
                throw;
            }
            catch
            {
                // A fault may leave policy-private state ambiguous. Discard it so
                // the next request starts from an authoritative snapshot.
                RetireSession();
                throw;
            }

            lock (_gate)
            {
                _committedVersion = snapshot.Version;
                DropCommittedEdits(snapshot.Version);
            }
            return result;
        }
        finally
        {
            if (entered) _analysisGate.Release();
            ReleaseAnalysis();
        }
    }

    /// <summary>Copies only the contiguous chain ending at the requested snapshot.</summary>
    private VersionedEdit[] ChangesTo(long version, out bool rebuild)
    {
        if (_committedVersion is null)
        {
            rebuild = _session is null;
            return [];
        }
        var expected = _committedVersion.Value;
        if (version == expected)
        {
            rebuild = _session is null;
            return [];
        }
        var changes = new List<VersionedEdit>();
        foreach (var edit in _edits)
        {
            if (edit.AfterVersion > version) break;
            if (edit.BeforeVersion != expected || edit.AfterVersion <= expected)
            {
                rebuild = true;
                return [];
            }
            changes.Add(edit);
            expected = edit.AfterVersion;
        }
        rebuild = _session is null || expected != version;
        return rebuild ? [] : changes.ToArray();
    }

    /// <summary>Replaces uncertain parser state while no other analysis uses it.</summary>
    private void RebuildSession()
    {
        RetireSession();
        var session = _policy.CreateSession();
        lock (_gate) _session = session;
    }

    private void RetireSession()
    {
        IFormatSession? retired;
        lock (_gate)
        {
            retired = _session;
            _session = null;
            _committedVersion = null;
        }
        retired?.Dispose();
    }

    /// <summary>Releases payloads no longer needed for a future version chain.</summary>
    private void DropCommittedEdits(long version)
    {
        var count = 0;
        while (count < _edits.Count && _edits[count].AfterVersion <= version)
        {
            _pendingInsertUnits -= _edits[count].Change.InsertText.Length;
            ++count;
        }
        if (count != 0) _edits.RemoveRange(0, count);
    }

    /// <summary>Forfeits incremental reuse without affecting snapshot correctness.</summary>
    private void ClearEdits()
    {
        _edits.Clear();
        _pendingInsertUnits = 0;
    }

    /// <summary>Retires a queued or active request and finishes deferred disposal.</summary>
    private void ReleaseAnalysis()
    {
        lock (_gate)
        {
            --_pendingAnalyses;
            if (_disposed && _cancellationFinished && _pendingAnalyses == 0)
                ReleaseResources();
        }
    }

    /// <summary>
    /// Cancels pending work and retires the parser after its in-flight call exits;
    /// never waits for a slow or noncooperative parser on the UI thread.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            ClearEdits();
        }
        _ = CancelAndReleaseAsync();
    }

    /// <summary>Runs policy cancellation callbacks away from native event dispatch.</summary>
    private async Task CancelAndReleaseAsync()
    {
        try
        {
            // Cancellation callbacks may be policy-owned. Do not run them in a
            // native close event or while holding the driver's state lock.
            await _lifetime.CancelAsync().ConfigureAwait(false);
        }
        catch (AggregateException)
        {
            // A faulty callback must not prevent eventual parser retirement.
        }
        finally
        {
            lock (_gate)
            {
                _cancellationFinished = true;
                if (_pendingAnalyses == 0) ReleaseResources();
            }
        }
    }

    /// <summary>Releases resources only after all users and callbacks have exited.</summary>
    private void ReleaseResources()
    {
        _session?.Dispose();
        _session = null;
        _lifetime.Dispose();
        _analysisGate.Dispose();
    }
}
