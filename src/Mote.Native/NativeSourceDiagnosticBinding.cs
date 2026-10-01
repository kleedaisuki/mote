using Mote.Engine;

namespace Mote.Native;

/// <summary>Closed outcomes for the exact full-native controlled-edit reference.</summary>
internal enum NativeSourceDiagnosticEditOutcome
{
    /// <summary>One source replacement entered engine history.</summary>
    Applied,
    /// <summary>Final native text is unchanged; only observed selection advances.</summary>
    NoChange,
    /// <summary>The old version, generation, installation or snapshot cannot admit this callback.</summary>
    Stale,
    /// <summary>Installation, lifetime or offset exactness is not certified.</summary>
    Unavailable
}

/// <summary>One admitted result; sorted observed ranges do not certify physical selection direction.</summary>
internal sealed record NativeSourceDiagnosticEdit(
    NativeSourceDiagnosticEditOutcome Outcome,
    NativeSourceDiagnosticBinding? Binding,
    TextChange? Change);

/// <summary>
/// Explicit whole-source diagnostic binding, never a bounded LegacyPage view.
/// The engine owns the snapshot/history; this object owns one immutable complete
/// replica projection and its import certificate. Its UI owner alone reconciles
/// final native text. Reconciliation is deliberately O(n), not an edit journal.
/// </summary>
internal sealed class NativeSourceDiagnosticBinding : IDisposable
{
    /// <summary>Native newline interpretation fixed for this adapter installation.</summary>
    private readonly NativeLineEndingMode _mode;
    /// <summary>True only after the installed native text passed exact readback.</summary>
    private bool _certified;
    /// <summary>A consumed old binding cannot admit a repeated native callback.</summary>
    private bool _retired;
    /// <summary>Disposal invalidates admission without disposing the engine-owned document.</summary>
    private bool _disposed;

    /// <summary>Captures the complete immutable engine source and canonical selection for one installation.</summary>
    internal NativeSourceDiagnosticBinding(TextSnapshot snapshot, long generation,
        long installationNonce, NativeLineEndingMode mode, int anchor, int active)
        : this(snapshot, generation, installationNonce, mode,
            CreateProjection(snapshot, mode), anchor, active, false)
    {
    }

    /// <summary>Creates an already verified successor without constructing another complete projection.</summary>
    private NativeSourceDiagnosticBinding(TextSnapshot snapshot, long generation,
        long installationNonce, NativeLineEndingMode mode, NativeTextProjection projection,
        int anchor, int active, bool certified)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (generation <= 0 || installationNonce <= 0)
            throw new ArgumentOutOfRangeException(nameof(generation));
        if ((uint)anchor > snapshot.Length || (uint)active > snapshot.Length)
            throw new ArgumentOutOfRangeException(nameof(anchor));
        if (projection.Source.Contains('\0'))
            throw new NotSupportedException("Native source diagnostic does not certify embedded NUL.");
        Snapshot = snapshot;
        Stamp = new NativeDocumentStamp(generation, snapshot.Version);
        InstallationNonce = installationNonce;
        _mode = mode;
        Projection = projection;
        Anchor = anchor;
        Active = active;
        _certified = certified;
    }

    /// <summary>Exact immutable canonical source owned by the engine.</summary>
    internal TextSnapshot Snapshot { get; }
    /// <summary>Document generation and original mutation version.</summary>
    internal NativeDocumentStamp Stamp { get; }
    /// <summary>Installation identity; an accepted native edit does not reinstall the control.</summary>
    internal long InstallationNonce { get; }
    /// <summary>Whole-source extent always starts at zero, never at a page or viewport origin.</summary>
    internal int SourceStart => 0;
    /// <summary>Whole-source extent, including offscreen text.</summary>
    internal int SourceLength => Snapshot.Length;
    /// <summary>Only this binding's complete native display/source projection.</summary>
    internal NativeTextProjection Projection { get; }
    /// <summary>Canonical global selection anchor, not a native paragraph index.</summary>
    internal int Anchor { get; }
    /// <summary>Canonical global selection active endpoint.</summary>
    internal int Active { get; }

    /// <summary>Certifies exact import or permanently refuses this attempted installation.</summary>
    internal bool CertifyInstalled(string readback)
    {
        if (_disposed || _retired) return false;
        _certified = string.Equals(readback, Projection.Display, StringComparison.Ordinal);
        _retired = !_certified;
        return _certified;
    }

    /// <summary>
    /// Reconciles one complete final readback against this exact original binding.
    /// Cancellation is observed before expensive work and immediately before
    /// the atomic engine Apply; after Apply no cancellation disguises a commit.
    /// A failed import/map, retired binding or stale document cannot mutate text.
    /// </summary>
    internal NativeSourceDiagnosticEdit Reconcile(Document document,
        NativeDocumentStamp expectedStamp, long expectedNonce, string finalDisplay,
        NativeSourceDiagnosticRange nativeSelection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(finalDisplay);
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed || !_certified)
            return new(NativeSourceDiagnosticEditOutcome.Unavailable, null, null);
        if (_retired || expectedStamp != Stamp || expectedNonce != InstallationNonce ||
            !ReferenceEquals(document.Snapshot, Snapshot))
            return new(NativeSourceDiagnosticEditOutcome.Stale, null, null);
        if (finalDisplay.Contains('\0') || nativeSelection.Start < 0 || nativeSelection.Length < 0 ||
            nativeSelection.Start > finalDisplay.Length - nativeSelection.Length)
            return new(NativeSourceDiagnosticEditOutcome.Unavailable, null, null);

        var change = Projection.Difference(finalDisplay);
        var projection = change is null ? Projection : new NativeTextProjection(
            string.Concat(Projection.Source.AsSpan(0, change.Value.Start), change.Value.InsertText,
                Projection.Source.AsSpan(change.Value.Start + change.Value.DeleteLength)), _mode);
        if (!string.Equals(projection.Display, finalDisplay, StringComparison.Ordinal))
            return new(NativeSourceDiagnosticEditOutcome.Unavailable, null, null);
        var sourceAnchor = projection.ToSourceBoundary(nativeSelection.Start);
        var sourceActive = projection.ToSourceBoundary(nativeSelection.Start + nativeSelection.Length, true);
        if (projection.ToDisplay(sourceAnchor) != nativeSelection.Start ||
            projection.ToDisplay(sourceActive) != nativeSelection.Start + nativeSelection.Length)
            return new(NativeSourceDiagnosticEditOutcome.Unavailable, null, null);

        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = change is null ? Snapshot : document.Apply(change.Value);
        var next = new NativeSourceDiagnosticBinding(snapshot, Stamp.Generation,
            InstallationNonce, _mode, projection, sourceAnchor, sourceActive, true);
        _retired = true;
        return new(change is null ? NativeSourceDiagnosticEditOutcome.NoChange :
            NativeSourceDiagnosticEditOutcome.Applied, next, change);
    }

    /// <summary>Revokes this certificate; the containing adapter owns native lifetime and the engine document.</summary>
    public void Dispose()
    {
        _disposed = true;
        _certified = false;
    }

    /// <summary>Validates the logical newline mode before copying the diagnostic's complete source.</summary>
    private static NativeTextProjection CreateProjection(TextSnapshot snapshot, NativeLineEndingMode mode)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (mode is not (NativeLineEndingMode.Preserve or NativeLineEndingMode.CrLf))
            throw new ArgumentOutOfRangeException(nameof(mode));
        return new NativeTextProjection(snapshot.GetText(), mode);
    }
}
