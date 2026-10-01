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

/// <summary>Optional sparse projection of one snapshot through one format session.</summary>
/// <remarks>Windows are absolute UTF-16 source ranges, not independently parsed documents.</remarks>
public interface IWindowedFormatSession : IFormatSession
{
    /// <summary>
    /// Updates the session once, then projects at most eight windows from that version.
    /// Callers serialize requests and must discard results for obsolete versions.
    /// </summary>
    WindowedAnalysis AnalyzeWindows(TextSnapshot snapshot,
        IReadOnlyList<VersionedEdit> changesSinceCommittedState, IReadOnlyList<TextSpan> windows,
        AnalysisScope scope, CancellationToken cancellationToken = default);
}

/// <summary>Delivery state of one normalized requested source window.</summary>
/// <param name="SourceRange">Requested half-open absolute UTF-16 interval after overlap merging.</param>
/// <param name="ProjectedRowCount">Logical rows represented in the shared projection.</param>
/// <param name="SourceIndexed">Whether the session reached this entire interval at this version.</param>
/// <param name="Truncated">True if rows or bounded payload were omitted, or source is not indexed.</param>
public readonly record struct WindowProjection(TextSpan SourceRange, int ProjectedRowCount,
    bool SourceIndexed, bool Truncated);

/// <summary>Versioned sparse projection with explicit, non-convex certified coverage.</summary>
/// <remarks>
/// Complete means whole-file semantic checks and an exact total, not that all source rows
/// were projected. CoveredRegion certifies only the listed intervals; gaps are not covered.
/// The projection retains no source snapshot or format-session cache.
/// </remarks>
public sealed class WindowedAnalysis
{
    /// <summary>Constructs an immutable presentation result from policy-owned facts.</summary>
    public WindowedAnalysis(long version, AnalysisCompleteness completeness,
        IReadOnlyList<TextSpan> certifiedCoverage, int? totalDiagnosticCount,
        SemanticNode root, IReadOnlyList<Diagnostic> diagnostics, IReadOnlyList<SemanticToken> tokens,
        IReadOnlyList<WindowProjection> windows)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(version);
        ArgumentNullException.ThrowIfNull(certifiedCoverage);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(windows);
        if (completeness is not (AnalysisCompleteness.Provisional or
            AnalysisCompleteness.CoveredRegion or AnalysisCompleteness.Complete))
            throw new ArgumentOutOfRangeException(nameof(completeness));
        if ((completeness == AnalysisCompleteness.Complete) != totalDiagnosticCount.HasValue ||
            totalDiagnosticCount is < 0)
            throw new ArgumentException("Only complete analysis has an exact nonnegative total.", nameof(totalDiagnosticCount));
        var previousEnd = -1;
        foreach (var span in certifiedCoverage)
        {
            if (span.Start < 0 || span.Length < 0 || span.End > root.Span.End || span.Start < previousEnd)
                throw new ArgumentException("Coverage must be sorted and nonoverlapping.", nameof(certifiedCoverage));
            previousEnd = span.End;
        }
        if (completeness == AnalysisCompleteness.Complete &&
            (certifiedCoverage.Count != 1 || certifiedCoverage[0] != root.Span))
            throw new ArgumentException("Complete analysis must certify the whole root span.", nameof(certifiedCoverage));
        if (windows.Count == 0)
            throw new ArgumentException("At least one requested window is required.", nameof(windows));
        previousEnd = -1;
        foreach (var window in windows)
        {
            var range = window.SourceRange;
            if (range.Start < 0 || range.Length < 0 || range.End > root.Span.End ||
                range.Start <= previousEnd || window.ProjectedRowCount < 0 ||
                !window.SourceIndexed && !window.Truncated ||
                completeness == AnalysisCompleteness.Complete && !window.SourceIndexed)
                throw new ArgumentException("Window delivery is not normalized or truthful.", nameof(windows));
            previousEnd = range.End;
        }
        Version = version;
        Completeness = completeness;
        CertifiedCoverage = new ReadOnlyCollection<TextSpan>(certifiedCoverage.ToArray());
        TotalDiagnosticCount = totalDiagnosticCount;
        Root = root;
        Diagnostics = new ReadOnlyCollection<Diagnostic>(diagnostics.ToArray());
        Tokens = new ReadOnlyCollection<SemanticToken>(tokens.ToArray());
        Windows = new ReadOnlyCollection<WindowProjection>(windows.ToArray());
    }

    /// <summary>Version of every absolute source span in this result.</summary>
    public long Version { get; }
    /// <summary>Semantic validity; independent of the number of projected rows.</summary>
    public AnalysisCompleteness Completeness { get; }
    /// <summary>Sorted certified intervals without a claim about disjoint gaps.</summary>
    public IReadOnlyList<TextSpan> CertifiedCoverage { get; }
    /// <summary>Exact whole-file diagnostic count only for complete analysis.</summary>
    public int? TotalDiagnosticCount { get; }
    /// <summary>Bounded, source-anchored semantic projection.</summary>
    public SemanticNode Root { get; }
    /// <summary>Diagnostics belonging to projected source owners.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    /// <summary>Tokens belonging to projected source owners.</summary>
    public IReadOnlyList<SemanticToken> Tokens { get; }
    /// <summary>Per-window delivery, distinct from semantic certification and global count.</summary>
    public IReadOnlyList<WindowProjection> Windows { get; }
    /// <summary>True when any requested window lacks a fully indexed, bounded projection.</summary>
    public bool ProjectionTruncated => Windows.Any(window => window.Truncated);
}
