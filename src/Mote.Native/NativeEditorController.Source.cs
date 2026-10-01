using Mote.Engine;
using Mote.Formats;
using Mote.Telemetry;

namespace Mote.Native;

internal sealed partial class NativeEditorController
{
    /// <summary>Optional full source capability fixed for this window lifetime.</summary>
    private readonly INativeSourceShell? _sourceShell;
    /// <summary>One certified complete projection, independent of the bounded preview interest.</summary>
    private NativeSourceBinding? _sourceBinding;
    /// <summary>New/Open installations cannot reuse old callback identities.</summary>
    private long _sourceNonce;
    /// <summary>Synchronous engine ChangedRange for an admitted native candidate is already reflected.</summary>
    private bool _sourceAdmitting;
    /// <summary>Programmatic callbacks are observations, not a second engine transaction.</summary>
    private bool _sourcePublishing;
    /// <summary>Failed certification retains canonical engine text but disables this replica.</summary>
    private bool _sourceUnavailable;
    /// <summary>Unadmitted native input remains unresolved until a certified, consented recovery succeeds.</summary>
    private NativeSourceFailure _sourceFailure;
    /// <summary>A failed consented reinstall cannot accidentally clear an unresolved native-input barrier.</summary>
    private bool _sourceRecovering;
    /// <summary>Native viewport sequence is independent of mutation/history version.</summary>
    private long _sourceViewportSequence;
    /// <summary>Last actual native visible interest; page commands cannot erase this independent viewport.</summary>
    private TextSpan _sourceVisibleDisplay;

    /// <summary>Installs only for a new document; chrome refresh never replaces matching source.</summary>
    private void ShowSourceDocument(TextSnapshot snapshot, string title)
    {
        if (_sourceShell is null) return;
        if (!_sourceUnavailable && (_sourceBinding is null ||
            !ReferenceEquals(_sourceBinding.Installation.Snapshot, snapshot)))
        {
            try
            {
                var binding = new NativeSourceBinding(snapshot, _canvasGeneration, ++_sourceNonce,
                    _shell.LineEndingMode, _navigation.Anchor, _navigation.Active);
                _sourcePublishing = true;
                NativeSourceObservation? observed;
                try
                {
                    using var install = MoteTelemetry.Start(TelemetryOperation.NativeSourceInstall, Dimensions(snapshot));
                    install?.SetStatus(TelemetryStatus.Failure);
                    observed = _sourceShell.InstallSource(binding.Installation);
                    if (binding.Matches(observed)) install?.SetStatus(TelemetryStatus.Success);
                }
                finally { _sourcePublishing = false; }
                if (!binding.Matches(observed)) { SourceUnavailable(); return; }
                _sourceBinding = binding;
                _sourceViewportSequence = 0;
                UpdateSourceInterest(observed!.VisibleDisplay);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                SourceUnavailable();
                return;
            }
        }
        if (!_sourceUnavailable) UpdateSourceInterest(_sourceVisibleDisplay);
        _sourceShell.SetSourceChrome(title, _sourceUnavailable
            ? "Native source unavailable; canonical document retained. Save or open another document."
            : $"{snapshot.LineCount:N0} lines · native source candidate", _document.IsModified,
            _document.CanUndo, _document.CanRedo);
    }

    /// <summary>Admits one settled native edit through the existing traced canonical mutation.</summary>
    private void SourceEdited(NativeSourceCandidate candidate)
    {
        // SourceCandidate is the shell's settled ingress certificate. Windows may
        // still label its command barrier Settling until this synchronous admission
        // returns; marked text itself must already have ended before emission.
        if (_disposed || _sourcePublishing || _sourceUnavailable || _sourceBinding is null ||
            _sourceShell!.HasSourceMarkedText) return;
        var before = _sourceBinding;
        // Delayed callbacks from retired document/nonce/version never replace current source.
        if (candidate.Stamp != before.Installation.Stamp || candidate.Nonce != before.Installation.Nonce)
            return;
        using var reconcile = MoteTelemetry.Start(TelemetryOperation.NativeSourceReconcile, Dimensions(_document.Snapshot));
        reconcile?.SetStatus(TelemetryStatus.Failure);
        var prepared = before.Prepare(_document.Snapshot, candidate);
        if (prepared is null) { SourceUnavailable(NativeSourceFailure.UnadmittedNativeText); return; }
        var mark = MoteTelemetry.Mark();
        try
        {
            if (prepared.Change is { } change)
            {
                _sourceAdmitting = true;
                try { ApplyTraced(change, mark); }
                finally { _sourceAdmitting = false; }
            }
            if (_disposed || !ReferenceEquals(before, _sourceBinding)) return;
            var next = before.Advance(_document.Snapshot, prepared);
            _navigation.SetSelection(_document.Snapshot, prepared.Anchor, prepared.Active);
            _sourceBinding = next;
            _sourcePublishing = true;
            bool acknowledged;
            try { acknowledged = _sourceShell!.AcknowledgeSource(next.Installation); }
            finally { _sourcePublishing = false; }
            if (!acknowledged) { SourceUnavailable(); return; }
            reconcile?.SetStatus(TelemetryStatus.Success);
            if (prepared.Change is null) return;
            TraceSourceDraw(MoteTelemetry.Fork(mark));
            InvalidateFind();
            RefreshSourceInterest();
            MoteTelemetry.Record(TelemetryEvent.EditCommitted, dimensions: Dimensions(_document.Snapshot));
            ShowDocument(mark);
            ScheduleAnalysis(mark);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // A committed engine change is never rolled back to disguise adapter failure.
            SourceUnavailable(ReferenceEquals(_document.Snapshot, before.Installation.Snapshot) &&
                prepared.Change is not null ? NativeSourceFailure.UnadmittedNativeText : NativeSourceFailure.CanonicalRetained);
        }
    }

    /// <summary>Publishes engine Undo/Redo/Format/Grid changes as guarded native ranges, never a full import.</summary>
    private void SourceDocumentChanged(DocumentChangedRangeEventArgs change)
    {
        if (_sourceShell is null || _sourceAdmitting || _sourceUnavailable || _sourceBinding is null) return;
        if (!ReferenceEquals(_sourceBinding.Installation.Snapshot, change.Before) || _shell.IsTextComposing)
        { SourceUnavailable(); return; }
        try
        {
            using var publication = MoteTelemetry.Start(TelemetryOperation.NativeSourceRangePublish, Dimensions(change.After));
            publication?.SetStatus(TelemetryStatus.Failure);
            var replacement = _sourceBinding.Replacement(change.After, _navigation.Anchor, _navigation.Active);
            _sourcePublishing = true;
            NativeSourceObservation? observation;
            try { observation = _sourceShell.ApplySourceChange(replacement); }
            finally { _sourcePublishing = false; }
            var next = new NativeSourceBinding(change.After, _canvasGeneration,
                replacement.After.Nonce, _shell.LineEndingMode, _navigation.Anchor, _navigation.Active);
            if (!next.Matches(observation)) { SourceUnavailable(); return; }
            _sourceBinding = next;
            UpdateSourceInterest(observation!.VisibleDisplay);
            publication?.SetStatus(TelemetryStatus.Success);
        }
        catch (Exception error) when (error is not OutOfMemoryException) { SourceUnavailable(); }
    }

    /// <summary>Accepts actual native observations only against the current certified replica.</summary>
    private void SourceViewChanged(NativeSourceViewObservation observation)
    {
        if (_disposed || _sourcePublishing || _sourceUnavailable || _sourceBinding is null ||
            _shell.IsTextComposing || observation.Stamp != _sourceBinding.Installation.Stamp ||
            observation.Nonce != _sourceBinding.Installation.Nonce ||
            observation.ViewportSequence < _sourceViewportSequence) return;
        if (!NativeSourceBinding.TrySelection(_sourceBinding.Installation.Projection,
            observation.Selection, out var anchor, out var active, out var known)) return;
        // A delayed Find belongs to the selection from which it started, not
        // to a newer native caret. Scrolling alone must not cancel the search.
        var selectionChanged = anchor != _navigation.Anchor || active != _navigation.Active;
        var showedSearch = selectionChanged && _operationStatus == "Searching document…";
        if (selectionChanged) InvalidateFind();
        _sourceViewportSequence = observation.ViewportSequence;
        _sourceBinding = _sourceBinding.Select(anchor, active, known);
        _navigation.SetSelection(_document.Snapshot, anchor, active);
        if (UpdateSourceInterest(observation.VisibleDisplay)) ScheduleAnalysis();
        if (showedSearch) ShowDocument();
    }

    /// <summary>Maps native visible interest to a bounded canonical analysis/preview span, not a text page.</summary>
    private bool UpdateSourceInterest(TextSpan display)
    {
        if (_sourceBinding is null) return false;
        var projection = _sourceBinding.Installation.Projection;
        if (display.Start < 0 || display.Length < 0 || display.End > projection.Display.Length) return false;
        _sourceVisibleDisplay = display;
        var start = SafeBoundary(_document.Snapshot, projection.ToSourceBoundary(display.Start), true);
        var end = SafeBoundary(_document.Snapshot,
            projection.ToSourceBoundary(display.End, true), false);
        end = SafeBoundary(_document.Snapshot, Math.Min(end, start + PageSize), true);
        var changed = start != _pageStart || end - start != _pageLength;
        _pageStart = start;
        _pageLength = Math.Max(0, end - start);
        _projection = new NativeTextProjection(_document.Snapshot.GetText(start, _pageLength), _shell.LineEndingMode);
        return changed;
    }

    /// <summary>Refreshes the bounded semantic bridge after a committed edit without moving the native viewport.</summary>
    private void RefreshSourceInterest()
    {
        var snapshot = _document.Snapshot;
        _pageStart = SafeBoundary(snapshot, Math.Min(_pageStart, snapshot.Length), true);
        _pageLength = SafeBoundary(snapshot, Math.Min(snapshot.Length, _pageStart + _pageLength), false) - _pageStart;
        _projection = new NativeTextProjection(snapshot.GetText(_pageStart, _pageLength), _shell.LineEndingMode);
    }

    /// <summary>Global selection publication uses the full native map, never bounded page offsets.</summary>
    private void ProjectSourceSelection(bool reveal)
    {
        if (_sourceBinding is null || _sourceUnavailable || _shell.IsTextComposing) return;
        _sourceBinding = _sourceBinding.Select(_navigation.Anchor, _navigation.Active);
        _sourcePublishing = true;
        try { _sourceShell!.SetSourceSelection(_sourceBinding.Installation, reveal); }
        finally { _sourcePublishing = false; }
    }

    /// <summary>Unknown native selection direction follows its ordered range start, never an invented active caret.</summary>
    private int SourceFollowOffset() => _sourceShell is not null && _sourceBinding is { DirectionKnown: false }
        ? _navigation.SelectionStart : _navigation.Active;

    /// <summary>Explicit native-input discard never discards canonical dirty text, history or file identity.</summary>
    private bool RecoverSource()
    {
        if (_disposed || _sourceShell is null || _sourceShell.HasSourceMarkedText || _sourcePublishing) return false;
        _sourceUnavailable = false;
        _sourceBinding = null;
        _sourceRecovering = true;
        try { ShowDocument(); }
        finally { _sourceRecovering = false; }
        if (_sourceUnavailable || _sourceBinding is null) return false;
        _sourceFailure = NativeSourceFailure.CanonicalRetained;
        ScheduleAnalysis();
        return true;
    }

    /// <summary>Shares semantic truth with the native source; adapters intersect actual visible geometry.</summary>
    private void PublishSourceSemantics(long version, AnalysisCompleteness completeness, TextSpan coverage,
        IReadOnlyList<SemanticToken> tokens, IReadOnlyList<Diagnostic> diagnostics)
    {
        if (_sourceBinding is null || _sourceUnavailable || _shell.IsTextComposing ||
            version != _sourceBinding.Installation.Stamp.Version) return;
        _sourceShell!.SetSourceSemantics(new(_sourceBinding.Installation.Stamp,
            _sourceBinding.Installation.Nonce, _presentationSequence, completeness, coverage, tokens, diagnostics));
    }

    /// <summary>Failed native certification keeps the engine and recovery commands authoritative.</summary>
    private void SourceUnavailable(NativeSourceFailure failure = NativeSourceFailure.CanonicalRetained)
    {
        if (_sourceRecovering && _sourceFailure == NativeSourceFailure.UnadmittedNativeText)
            failure = NativeSourceFailure.UnadmittedNativeText;
        _sourceFailure = failure;
        _sourceUnavailable = true;
        _sourceBinding = null;
        var snapshot = _document.Snapshot;
        _pageStart = 0;
        _pageLength = SafeBoundary(snapshot, Math.Min(snapshot.Length, PageSize), true);
        _projection = new NativeTextProjection(snapshot.GetText(0, _pageLength), _shell.LineEndingMode);
        _sourceShell?.SetSourceUnavailable("Exact native source unavailable; canonical document retained.", failure);
        // Dirty/history/title state remains truthful even when a failed native
        // acknowledgment happens after the canonical transaction has committed.
        ShowDocument();
    }
}
