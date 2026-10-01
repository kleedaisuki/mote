using Mote.Engine;

namespace Mote.Native;

/// <summary>Pure prepared edit; only the controller may apply Change to the document.</summary>
internal sealed record NativeSourcePreparedEdit(TextChange? Change, NativeTextProjection Projection,
    int Anchor, int Active, bool DirectionKnown);

/// <summary>
/// One certified whole-source native replica. Preparation is pure and validates the
/// original snapshot/nonce before deriving a scalar-safe engine change. No document
/// mutation, callbacks, file I/O or native calls occur in this model.
/// </summary>
internal sealed class NativeSourceBinding
{
    /// <summary>Creates an immutable full projection with canonical selection.</summary>
    internal NativeSourceBinding(TextSnapshot snapshot, long generation, long nonce,
        NativeLineEndingMode mode, int anchor, int active)
        : this(new NativeSourceInstallation(snapshot, new(generation, snapshot.Version), nonce,
            new NativeTextProjection(snapshot.GetText(), mode), anchor, active), mode, true)
    { }

    /// <summary>Reuses the prepared complete projection after the one actual engine commit.</summary>
    private NativeSourceBinding(NativeSourceInstallation installation, NativeLineEndingMode mode,
        bool directionKnown)
    {
        if (installation.Stamp.Generation <= 0 || installation.Nonce <= 0 ||
            installation.Stamp.Version != installation.Snapshot.Version)
            throw new ArgumentOutOfRangeException(nameof(installation));
        if ((uint)installation.Anchor > installation.Snapshot.Length ||
            (uint)installation.Active > installation.Snapshot.Length)
            throw new ArgumentOutOfRangeException(nameof(installation));
        if (installation.Projection.Source.Contains('\0'))
            throw new NotSupportedException("The native source candidate cannot certify embedded NUL.");
        Installation = installation;
        Mode = mode;
        DirectionKnown = directionKnown;
    }

    /// <summary>Source identity, immutable projection and canonical selection.</summary>
    internal NativeSourceInstallation Installation { get; }
    /// <summary>Window-lifetime native newline contract.</summary>
    internal NativeLineEndingMode Mode { get; }
    /// <summary>False for an ordered native range whose active endpoint was not observed.</summary>
    internal bool DirectionKnown { get; }

    /// <summary>Certifies whole native import; partial/truncated import never becomes editable.</summary>
    internal bool Matches(NativeSourceObservation? observation) => observation is not null &&
        string.Equals(observation.Display, Installation.Projection.Display, StringComparison.Ordinal) &&
        TrySelection(Installation.Projection, observation.Selection, out _, out _, out _);

    /// <summary>Derives an edit without modifying or advancing the baseline.</summary>
    internal NativeSourcePreparedEdit? Prepare(TextSnapshot current, NativeSourceCandidate candidate)
    {
        if (!ReferenceEquals(current, Installation.Snapshot) || candidate.Stamp != Installation.Stamp ||
            candidate.Nonce != Installation.Nonce || candidate.Display.Contains('\0') ||
            !WellFormed(candidate.Display)) return null;
        var change = Installation.Projection.Difference(candidate.Display);
        var source = change is { } edit
            ? string.Concat(Installation.Projection.Source.AsSpan(0, edit.Start), edit.InsertText,
                Installation.Projection.Source.AsSpan(edit.Start + edit.DeleteLength))
            : Installation.Projection.Source;
        var projection = change is null ? Installation.Projection : new NativeTextProjection(source, Mode);
        if (!string.Equals(projection.Display, candidate.Display, StringComparison.Ordinal) ||
            !TrySelection(projection, candidate.Selection, out var anchor, out var active, out var known))
            return null;
        return new(change, projection, anchor, active, known);
    }

    /// <summary>Advances only to the actual engine snapshot corresponding to the prepared change.</summary>
    internal NativeSourceBinding Advance(TextSnapshot snapshot, NativeSourcePreparedEdit prepared)
    {
        if (prepared.Change is null ? !ReferenceEquals(snapshot, Installation.Snapshot) :
            snapshot.Version != checked(Installation.Snapshot.Version + 1))
            throw new InvalidOperationException("Native admission must advance the original engine version once.");
        if (!string.Equals(snapshot.GetText(), prepared.Projection.Source, StringComparison.Ordinal))
            throw new InvalidOperationException("Native preparation and committed source disagree.");
        return new(new(snapshot, new(Installation.Stamp.Generation, snapshot.Version), Installation.Nonce,
            prepared.Projection, prepared.Anchor, prepared.Active), Mode, prepared.DirectionKnown);
    }

    /// <summary>Changes only canonical selection, retaining text and installation nonce.</summary>
    internal NativeSourceBinding Select(int anchor, int active, bool directionKnown = true) =>
        new(Installation with { Anchor = anchor, Active = active }, Mode, directionKnown);

    /// <summary>Creates a safe native display range replacement for an engine-originated version.</summary>
    internal NativeSourceReplacement Replacement(TextSnapshot after, int anchor, int active)
    {
        var next = new NativeSourceBinding(after, Installation.Stamp.Generation,
            Installation.Nonce, Mode, anchor, active);
        var old = Installation.Projection.Display;
        var text = next.Installation.Projection.Display;
        var diff = new NativeTextProjection(old, NativeLineEndingMode.Preserve).Difference(text);
        var start = diff?.Start ?? 0;
        var oldEnd = start + (diff?.DeleteLength ?? 0);
        var newEnd = start + (diff?.InsertText.Length ?? 0);
        // Never replace half of the native paragraph delimiter at a changed seam.
        if (InsideCrLf(old, start) || InsideCrLf(text, start)) start--;
        if (InsideCrLf(old, oldEnd) || InsideCrLf(text, newEnd)) { oldEnd++; newEnd++; }
        return new(Installation, next.Installation, start, oldEnd - start,
            text.Substring(start, newEnd - start));
    }

    /// <summary>Certifies a source-position observation without manufacturing selection direction.</summary>
    internal static bool TrySelection(NativeTextProjection projection, NativeSourceSelection selection,
        out int anchor, out int active, out bool known)
    {
        anchor = active = 0;
        known = selection.Start == selection.End || selection.Active is not null;
        if (selection.Start < 0 || selection.End < selection.Start || selection.End > projection.Display.Length ||
            selection.Active is { } endpoint && endpoint != selection.Start && endpoint != selection.End)
            return false;
        var start = projection.ToSourceBoundary(selection.Start);
        var end = projection.ToSourceBoundary(selection.End, true);
        if (projection.ToDisplay(start) != selection.Start || projection.ToDisplay(end) != selection.End ||
            !ScalarBoundary(projection.Source, start) || !ScalarBoundary(projection.Source, end)) return false;
        var backward = selection.Active == selection.Start && selection.Start != selection.End;
        anchor = backward ? end : start;
        active = backward ? start : end;
        return true;
    }

    /// <summary>Rejects native strings that would be rejected by the canonical engine.</summary>
    private static bool WellFormed(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsLowSurrogate(text[i])) return false;
            if (!char.IsHighSurrogate(text[i])) continue;
            if (++i == text.Length || !char.IsLowSurrogate(text[i])) return false;
        }
        return true;
    }

    /// <summary>Engine offsets cannot split a UTF-16 scalar.</summary>
    private static bool ScalarBoundary(string text, int offset) => offset == 0 || offset == text.Length ||
        !char.IsHighSurrogate(text[offset - 1]) || !char.IsLowSurrogate(text[offset]);

    /// <summary>Native range replacement keeps CRLF pairs indivisible.</summary>
    private static bool InsideCrLf(string text, int offset) => offset > 0 && offset < text.Length &&
        text[offset - 1] == '\r' && text[offset] == '\n';
}
