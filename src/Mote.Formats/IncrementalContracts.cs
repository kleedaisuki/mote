using System.Collections.ObjectModel;
using Mote.Engine;

namespace Mote.Formats;

/// <summary>Optional policy capability for per-document, versioned semantic analysis.</summary>
/// <remarks>Implementations remain stateless; every open document owns an isolated session.</remarks>
public interface IIncrementalDocumentPolicy : IDocumentPolicy
{
    /// <summary>Creates a fresh session for exactly one document lifetime.</summary>
    IFormatSession CreateSession();
}

/// <summary>One committed edit in coordinates of <see cref="BeforeVersion"/>.</summary>
public readonly record struct VersionedEdit(long BeforeVersion, long AfterVersion, TextChange Change);

/// <summary>Desired analysis breadth. A full request may still return bounded coverage.</summary>
public enum AnalysisScope { Visible, Full }

/// <summary>Truthfulness of an analysis result at its exact document version.</summary>
public enum AnalysisCompleteness
{
    /// <summary>Only tentative syntax/style facts; no claim of semantic validity.</summary>
    Provisional,
    /// <summary>Semantic facts are valid inside Coverage, not necessarily elsewhere.</summary>
    CoveredRegion,
    /// <summary>Whole-document syntax and semantic checks are complete.</summary>
    Complete
}

/// <summary>A viewport plus an optional request for whole-document semantics.</summary>
/// <param name="VisibleRange">Half-open absolute UTF-16 range of user interest.</param>
/// <param name="Scope">Whether full semantics are requested.</param>
public readonly record struct AnalysisRequest(TextSpan VisibleRange, AnalysisScope Scope);

/// <summary>
/// Immutable semantic projection for one engine snapshot. It retains no source string;
/// every span is absolute in that snapshot, even when the projection is bounded.
/// </summary>
public sealed class DocumentAnalysis
{
    /// <summary>Constructs a version-tagged projection from policy-owned results.</summary>
    public DocumentAnalysis(long version, TextSpan coverage, AnalysisCompleteness completeness,
        SemanticNode root, IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyList<SemanticToken> tokens, int? totalDiagnosticCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(version);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(tokens);
        if (coverage.Start < 0 || coverage.Length < 0)
            throw new ArgumentOutOfRangeException(nameof(coverage));
        if (completeness is not (AnalysisCompleteness.Provisional or
            AnalysisCompleteness.CoveredRegion or AnalysisCompleteness.Complete))
            throw new ArgumentOutOfRangeException(nameof(completeness));
        if (totalDiagnosticCount is < 0)
            throw new ArgumentOutOfRangeException(nameof(totalDiagnosticCount));
        if (completeness == AnalysisCompleteness.Complete && totalDiagnosticCount is null)
            throw new ArgumentException("Complete analysis requires a total diagnostic count.", nameof(totalDiagnosticCount));
        if (completeness != AnalysisCompleteness.Complete && totalDiagnosticCount is not null)
            throw new ArgumentException("Only complete analysis may assert a total diagnostic count.", nameof(totalDiagnosticCount));
        Version = version;
        Coverage = coverage;
        Completeness = completeness;
        Root = root;
        Diagnostics = new ReadOnlyCollection<Diagnostic>(diagnostics.ToArray());
        Tokens = new ReadOnlyCollection<SemanticToken>(tokens.ToArray());
        TotalDiagnosticCount = totalDiagnosticCount;
    }

    /// <summary>Monotonic version of the analyzed snapshot.</summary>
    public long Version { get; }
    /// <summary>Range over which semantic validity can be asserted.</summary>
    public TextSpan Coverage { get; }
    /// <summary>Completeness of the result, not merely of displayed nodes.</summary>
    public AnalysisCompleteness Completeness { get; }
    /// <summary>Source-anchored projection; children may be a bounded visible subset.</summary>
    public SemanticNode Root { get; }
    /// <summary>Diagnostics included in the projection.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    /// <summary>Semantic classifications included in the projection.</summary>
    public IReadOnlyList<SemanticToken> Tokens { get; }
    /// <summary>Whole-document count when and only when Completeness is Complete.</summary>
    public int? TotalDiagnosticCount { get; }
}

/// <summary>One-document stateful analyzer; callers serialize calls and dispose on close.</summary>
public interface IFormatSession : IDisposable
{
    /// <summary>
    /// Analyzes an immutable snapshot after a contiguous edit chain from the last committed
    /// state. A gap must trigger safe rebuild or explicitly partial output, never stale facts.
    /// Canceled calls must not publish a new committed version.
    /// </summary>
    DocumentAnalysis Analyze(TextSnapshot snapshot, IReadOnlyList<VersionedEdit> changesSinceCommittedState,
        AnalysisRequest request, CancellationToken cancellationToken = default);
}
