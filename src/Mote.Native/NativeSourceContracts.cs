using Mote.Engine;
using Mote.Formats;

namespace Mote.Native;

/// <summary>Exact whole-source installation; never a page or a canvas input island.</summary>
internal sealed record NativeSourceInstallation(TextSnapshot Snapshot, NativeDocumentStamp Stamp,
    long Nonce, NativeTextProjection Projection, int Anchor, int Active);

/// <summary>Ordered native display range; Active is supplied only with an actual endpoint witness.</summary>
internal readonly record struct NativeSourceSelection(int Start, int End, int? Active = null);

/// <summary>Settled readback and native selection against one original installation identity.</summary>
internal sealed record NativeSourceCandidate(NativeDocumentStamp Stamp, long Nonce,
    string Display, NativeSourceSelection Selection);

/// <summary>Exact native observations returned only after successful import or guarded replacement.</summary>
internal sealed record NativeSourceObservation(string Display, NativeSourceSelection Selection,
    TextSpan VisibleDisplay);

/// <summary>Version-bound native selection/viewport; viewport sequence is not a text version.</summary>
internal sealed record NativeSourceViewObservation(NativeDocumentStamp Stamp, long Nonce,
    NativeSourceSelection Selection, TextSpan VisibleDisplay, long ViewportSequence);

/// <summary>Engine-originated display replacement expanded safely across newline/scalar seams.</summary>
internal sealed record NativeSourceReplacement(NativeSourceInstallation Before,
    NativeSourceInstallation After, int DisplayStart, int DisplayDeleteLength, string DisplayInsert);

/// <summary>Absolute canonical semantic facts; native adapters decorate only their actual visible ranges.</summary>
internal sealed record NativeSourceSemantics(NativeDocumentStamp Stamp, long Nonce,
    long PresentationSequence, AnalysisCompleteness Completeness, TextSpan Coverage,
    IReadOnlyList<SemanticToken> Tokens, IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>Whether recovery commands may use canonical text after a native replica failure.</summary>
internal enum NativeSourceFailure
{
    /// <summary>The complete current source is in the engine; disabling input does not veto a later explicit Save.</summary>
    CanonicalRetained,
    /// <summary>A native candidate was not committed; commands cannot silently discard those pending characters.</summary>
    UnadmittedNativeText
}

/// <summary>
/// Optional window-lifetime full native source capability. The engine owns text/history/I/O;
/// this adapter alone owns source layout, native caret, input and viewport. Failed exact
/// import or replacement disables input and never exposes a partial editable replica.
/// </summary>
internal interface INativeSourceShell
{
    /// <summary>True only for the explicit product candidate, never inferred from file size.</summary>
    bool NativeSourceEnabled { get; }
    /// <summary>Actual provisional native marked text, distinct from a settled command barrier still unwinding.</summary>
    bool HasSourceMarkedText { get; }
    /// <summary>Settled final native text; no bounded TextChanged event is also emitted.</summary>
    event Action<NativeSourceCandidate>? SourceCandidate;
    /// <summary>Actual native selection/visible display range, suppressed during provisional composition.</summary>
    event Action<NativeSourceViewObservation>? SourceViewChanged;
    /// <summary>After explicit discard-native consent, requests a fresh certified canonical installation.</summary>
    event Func<bool>? SourceRecoveryRequested;
    /// <summary>Imports once for New/Open/recovery and certifies complete exact readback.</summary>
    NativeSourceObservation? InstallSource(NativeSourceInstallation installation);
    /// <summary>Advances identity after an admitted native edit without replacing matching native text.</summary>
    bool AcknowledgeSource(NativeSourceInstallation installation);
    /// <summary>Applies one guarded range replacement without native undo or edit echoes.</summary>
    NativeSourceObservation? ApplySourceChange(NativeSourceReplacement replacement);
    /// <summary>Publishes canonical selection and optionally scrolls it into view; never reimports text.</summary>
    void SetSourceSelection(NativeSourceInstallation installation, bool reveal);
    /// <summary>Applies visible attributes only; keeps text, selection, composition and scroll intact.</summary>
    void SetSourceSemantics(NativeSourceSemantics semantics);
    /// <summary>Updates product chrome independently of the source string.</summary>
    void SetSourceChrome(string title, string status, bool modified, bool canUndo = false, bool canRedo = false);
    /// <summary>Disables native editing without changing committed canonical source/history.</summary>
    void SetSourceUnavailable(string reason, NativeSourceFailure failure = NativeSourceFailure.CanonicalRetained);
}
