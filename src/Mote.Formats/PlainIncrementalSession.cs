using Mote.Engine;

namespace Mote.Formats;

/// <summary>
/// Per-document plain-text analysis without a source copy or parser cache.
/// Plain text has no syntax or semantic validity rules, so its empty diagnostic
/// set is complete even when the requested projection covers only a viewport.
/// </summary>
internal sealed class PlainIncrementalSession : IFormatSession
{
    private long? _committedVersion;
    private bool _disposed;

    /// <inheritdoc />
    public DocumentAnalysis Analyze(TextSnapshot snapshot,
        IReadOnlyList<VersionedEdit> changesSinceCommittedState,
        AnalysisRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(changesSinceCommittedState);
        cancellationToken.ThrowIfCancellationRequested();

        var visible = request.VisibleRange;
        if (visible.Start < 0 || visible.Length < 0 ||
            visible.Start > snapshot.Length || visible.Length > snapshot.Length - visible.Start)
            throw new ArgumentOutOfRangeException(nameof(request), "Visible range exceeds the snapshot.");

        if (request.Scope is not (AnalysisScope.Visible or AnalysisScope.Full))
            throw new ArgumentOutOfRangeException(nameof(request), "Unknown analysis scope.");
        // Plain text has no hidden global constraints: the entire snapshot is semantically
        // covered even when the caller only requested a viewport projection.
        var coverage = new TextSpan(0, snapshot.Length);

        // A missing or malformed edit chain invalidates reusable state in other
        // policies. Here, the authoritative snapshot is already the entire model;
        // the fresh result is identical and needs no source materialization.
        var chainIsValid = HasContiguousHistory(changesSinceCommittedState, snapshot.Version);
        var result = new DocumentAnalysis(snapshot.Version, coverage,
            AnalysisCompleteness.Complete,
            new SemanticNode("document", new TextSpan(0, snapshot.Length)),
            Array.Empty<Diagnostic>(), Array.Empty<SemanticToken>(), 0);

        cancellationToken.ThrowIfCancellationRequested();
        // A gap takes the same fresh-snapshot path as a valid chain. An obsolete
        // result may be returned to a caller, but must not rewind session state.
        if (!chainIsValid && _committedVersion is long committed && snapshot.Version < committed)
            return result;
        _committedVersion = snapshot.Version;
        return result;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        _committedVersion = null;
    }

    /// <summary>Checks the edit history before advancing the session version.</summary>
    private bool HasContiguousHistory(IReadOnlyList<VersionedEdit> changes, long snapshotVersion)
    {
        if (changes.Count == 0)
            return _committedVersion is null || _committedVersion == snapshotVersion;

        var expected = _committedVersion ?? changes[0].BeforeVersion;
        foreach (var edit in changes)
        {
            if (edit.BeforeVersion != expected || edit.AfterVersion <= edit.BeforeVersion)
                return false;
            expected = edit.AfterVersion;
        }
        return expected == snapshotVersion;
    }
}
