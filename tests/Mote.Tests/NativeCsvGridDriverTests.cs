using Mote.Engine;
using Mote.Formats;
using Mote.Native;

namespace Mote.Tests;

/// <summary>CSV source and Grid share one serialized policy update and edit baseline.</summary>
public sealed class NativeCsvGridDriverTests
{
    /// <summary>Grid delivery never invokes a second ordinary or windowed parser update.</summary>
    [Fact]
    public async Task Grid_request_updates_once_and_transports_source_truth()
    {
        using var document = new Document("a,b\n1\n");
        var policy = new ProbePolicy();
        using var driver = new NativeFormatSessionDriver(policy);
        var snapshot = document.Snapshot;
        var result = await driver.AnalyzePresentationAsync(snapshot, Request(snapshot), default, Grid(snapshot));
        Assert.Equal(1, policy.GridCalls);
        Assert.Equal(0, policy.AnalyzeCalls);
        Assert.Equal(0, policy.WindowCalls);
        Assert.NotNull(result.Grid);
        Assert.Null(result.Preview.Flow);
        Assert.Empty(result.Preview.Text);
        Assert.Equal(snapshot.Version, result.Grid.Version);
        Assert.Equal(AnalysisCompleteness.Complete, result.Analysis.Completeness);
        Assert.Equal(result.Grid.TotalDiagnosticCount, result.Analysis.TotalDiagnosticCount);
        Assert.Contains(result.Analysis.Diagnostics, diagnostic => diagnostic.Code == "CSV004");
        Assert.Equal(2, result.Grid.Extent.ExactRowCount);
    }

    /// <summary>Recorded source edits reach Grid once; same-version refresh has an empty chain.</summary>
    [Fact]
    public async Task Grid_refresh_preserves_ordered_edits_and_same_version_baseline()
    {
        using var document = new Document("a,b\n");
        var policy = new ProbePolicy();
        using var driver = new NativeFormatSessionDriver(policy);
        document.ChangedRange += (_, change) => driver.Record(change);
        await driver.AnalyzePresentationAsync(document.Snapshot, Request(document.Snapshot), default, Grid(document.Snapshot));
        document.Apply(new TextChange(0, 1, "x"));
        var snapshot = document.Snapshot;
        var updated = await driver.AnalyzePresentationAsync(snapshot, Request(snapshot), default, Grid(snapshot));
        Assert.Equal("x", Assert.Single(policy.LastChanges).Change.InsertText);
        Assert.Contains("x", updated.Grid!.DisplayText, StringComparison.Ordinal);
        await driver.AnalyzePresentationAsync(snapshot, Request(snapshot), default, Grid(snapshot));
        Assert.Empty(policy.LastChanges);
        Assert.Equal(3, policy.GridCalls);
        Assert.Equal(0, policy.AnalyzeCalls);
    }

    /// <summary>Analysis-only Full remains a normal session call, not a Grid or Flow operation.</summary>
    [Fact]
    public async Task Analysis_only_full_keeps_idle_contract()
    {
        using var document = new Document("a,b\n");
        var policy = new ProbePolicy();
        using var driver = new NativeFormatSessionDriver(policy);
        var result = await driver.AnalyzeAsync(document.Snapshot, Request(document.Snapshot), default);
        Assert.Equal(AnalysisCompleteness.Complete, result.Completeness);
        Assert.Equal(1, policy.AnalyzeCalls);
        Assert.Equal(0, policy.GridCalls);
    }

    /// <summary>No explicit Grid request preserves the established CSV Flow API.</summary>
    [Fact]
    public async Task Missing_grid_request_preserves_csv_flow()
    {
        using var document = new Document("a,b\n");
        var policy = new ProbePolicy();
        using var driver = new NativeFormatSessionDriver(policy);
        var result = await driver.AnalyzePresentationAsync(document.Snapshot, Request(document.Snapshot), default);
        Assert.Null(result.Grid);
        Assert.NotNull(result.Preview.Flow);
        Assert.Equal(1, policy.AnalyzeCalls);
        Assert.Equal(0, policy.GridCalls);
    }

    /// <summary>Unsupported formats ignore the optional CSV request and keep native Flow.</summary>
    [Fact]
    public async Task Non_grid_format_retains_flow()
    {
        using var document = new Document("# title\n");
        using var driver = new NativeFormatSessionDriver(
            (IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Markdown));
        var result = await driver.AnalyzePresentationAsync(document.Snapshot, Request(document.Snapshot), default,
            Grid(document.Snapshot));
        Assert.Null(result.Grid);
        Assert.NotNull(result.Preview.Flow);
        Assert.Contains("title", result.Preview.Text, StringComparison.Ordinal);
    }

    /// <summary>Cancellation before execution does not consume the ordered edit chain.</summary>
    [Fact]
    public async Task Precancelled_grid_keeps_edit_for_next_request()
    {
        using var document = new Document("a,b\n");
        var policy = new ProbePolicy();
        using var driver = new NativeFormatSessionDriver(policy);
        document.ChangedRange += (_, change) => driver.Record(change);
        await driver.AnalyzePresentationAsync(document.Snapshot, Request(document.Snapshot), default, Grid(document.Snapshot));
        document.Apply(new TextChange(0, 1, "x"));
        var snapshot = document.Snapshot;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => driver.AnalyzePresentationAsync(snapshot,
            Request(snapshot), new CancellationToken(true), Grid(snapshot)));
        Assert.Equal(1, policy.GridCalls);
        await driver.AnalyzePresentationAsync(snapshot, Request(snapshot), default, Grid(snapshot));
        Assert.Single(policy.LastChanges);
    }

    /// <summary>A mismatched bundle is rejected and its uncertain session is rebuilt.</summary>
    [Fact]
    public async Task Wrong_source_version_retires_session_before_retry()
    {
        using var document = new Document("a,b\n");
        var policy = new ProbePolicy { ReturnWrongSourceVersion = true };
        using var driver = new NativeFormatSessionDriver(policy);
        var snapshot = document.Snapshot;
        await Assert.ThrowsAsync<InvalidOperationException>(() => driver.AnalyzePresentationAsync(snapshot,
            Request(snapshot), default, Grid(snapshot)));
        policy.ReturnWrongSourceVersion = false;
        var result = await driver.AnalyzePresentationAsync(snapshot, Request(snapshot), default, Grid(snapshot));
        Assert.Equal(2, policy.CreatedSessions);
        Assert.Equal(snapshot.Version, result.Analysis.Version);
        Assert.Empty(policy.LastChanges);
    }

    /// <summary>Requests full semantics over this small fixture's source viewport.</summary>
    private static AnalysisRequest Request(TextSnapshot snapshot) =>
        new(new TextSpan(0, snapshot.Length), AnalysisScope.Full);

    /// <summary>Requests bounded real coordinates, without introducing another parser.</summary>
    private static CsvGridRequest Grid(TextSnapshot snapshot) =>
        new([new TextSpan(0, snapshot.Length)], new CsvGridAnchor.Source(0), 16,
            new GridRange(0, 8), AnalysisScope.Full);

    /// <summary>Counts capability calls around the actual production CSV session.</summary>
    private sealed class ProbePolicy : IIncrementalDocumentPolicy
    {
        private readonly IIncrementalDocumentPolicy _inner =
            (IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Csv);
        public DocumentKind Kind => DocumentKind.Csv;
        public string DisplayName => "CSV probe";
        public int AnalyzeCalls { get; private set; }
        public int WindowCalls { get; private set; }
        public int GridCalls { get; private set; }
        public int CreatedSessions { get; private set; }
        public bool ReturnWrongSourceVersion { get; set; }
        public IReadOnlyList<VersionedEdit> LastChanges { get; private set; } = [];
        public IFormatSession CreateSession()
        {
            CreatedSessions++;
            return new ProbeSession(this, (ICsvGridFormatSession)_inner.CreateSession());
        }
        public FormatAnalysis Analyze(string text, CancellationToken cancellationToken = default) => _inner.Analyze(text, cancellationToken);
        public string Format(string text) => _inner.Format(text);
        public string RenderHtml(FormatAnalysis analysis) => _inner.RenderHtml(analysis);

        /// <summary>Delegates grammar ownership while exposing update counts to assertions.</summary>
        private sealed class ProbeSession(ProbePolicy owner, ICsvGridFormatSession inner) : ICsvGridFormatSession
        {
            public DocumentAnalysis Analyze(TextSnapshot snapshot, IReadOnlyList<VersionedEdit> changesSinceCommittedState,
                AnalysisRequest request, CancellationToken cancellationToken = default)
            {
                owner.AnalyzeCalls++;
                return inner.Analyze(snapshot, changesSinceCommittedState, request, cancellationToken);
            }
            public WindowedAnalysis AnalyzeWindows(TextSnapshot snapshot, IReadOnlyList<VersionedEdit> changesSinceCommittedState,
                IReadOnlyList<TextSpan> windows, AnalysisScope scope, CancellationToken cancellationToken = default)
            {
                owner.WindowCalls++;
                return inner.AnalyzeWindows(snapshot, changesSinceCommittedState, windows, scope, cancellationToken);
            }
            public CsvGridAnalysis AnalyzeGrid(TextSnapshot snapshot, IReadOnlyList<VersionedEdit> changesSinceCommittedState,
                CsvGridRequest request, CancellationToken cancellationToken = default)
            {
                owner.GridCalls++;
                owner.LastChanges = changesSinceCommittedState.ToArray();
                var result = inner.AnalyzeGrid(snapshot, changesSinceCommittedState, request, cancellationToken);
                if (!owner.ReturnWrongSourceVersion) return result;
                var source = result.Source;
                return new CsvGridAnalysis(new WindowedAnalysis(source.Version + 1, source.Completeness,
                    source.CertifiedCoverage, source.TotalDiagnosticCount, source.Root, source.Diagnostics,
                    source.Tokens, source.Windows), result.Grid);
            }
            public void Dispose() => inner.Dispose();
        }
    }
}
