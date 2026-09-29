using Mote.Engine;
using Mote.Formats;
using Mote.Native;

namespace Mote.Tests;

/// <summary>Deterministic scheduling tests for opportunistic, versioned full analysis.</summary>
public sealed class NativeIdleAnalysisTests
{
    /// <summary>Repeated visible offers coalesce into one Full pass for one version.</summary>
    [Fact]
    public async Task Idle_offer_coalesces_and_promotes_one_complete_result()
    {
        using var document = new Document("{\"x\":1}");
        var policy = new ProbePolicy(DocumentKind.Json, AnalysisCompleteness.Complete);
        using var driver = new NativeFormatSessionDriver(policy);
        var delay = new ControlledDelay();
        var published = new TaskCompletionSource<DocumentAnalysis>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var idle = new NativeIdleFullAnalysis(driver, policy.Kind,
            (_, result, _) => published.TrySetResult(result), delay: delay.WaitAsync);
        var snapshot = document.Snapshot;
        var visible = Visible(snapshot);
        var range = new TextSpan(0, snapshot.Length);
        Assert.Equal(IdleFullOffer.Scheduled, idle.Offer(snapshot, visible, range));
        Assert.Equal(IdleFullOffer.AlreadyPendingOrAttempted, idle.Offer(snapshot, visible, range));
        Assert.Equal(TimeSpan.FromSeconds(1), Assert.Single(delay.Requests).Duration);
        delay.Complete(0);
        var result = await published.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(snapshot.Version, result.Version);
        Assert.Equal(AnalysisCompleteness.Complete, result.Completeness);
        Assert.Equal(0, result.TotalDiagnosticCount);
        Assert.Equal(1, policy.FullCalls);
        Assert.Equal(IdleFullOffer.AlreadyPendingOrAttempted, idle.Offer(snapshot, visible, range));
    }

    /// <summary>A canceled old timer cannot publish; a newer version can schedule independently.</summary>
    [Fact]
    public async Task Idle_cancel_discards_old_version_and_allows_new_offer()
    {
        using var document = new Document("{\"x\":1}");
        var policy = new ProbePolicy(DocumentKind.Json, AnalysisCompleteness.Complete);
        using var driver = new NativeFormatSessionDriver(policy);
        document.Changed += (_, change) => driver.Record(change);
        var delay = new ControlledDelay();
        var versions = new List<long>();
        using var idle = new NativeIdleFullAnalysis(driver, policy.Kind,
            (_, result, _) => { lock (versions) versions.Add(result.Version); }, delay: delay.WaitAsync);
        var old = document.Snapshot;
        Assert.Equal(IdleFullOffer.Scheduled, idle.Offer(old, Visible(old), new TextSpan(0, old.Length)));
        idle.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await delay.Requests[0].Task);
        var next = document.Apply(new TextChange(old.Length - 1, 0, " "));
        Assert.Equal(IdleFullOffer.Scheduled, idle.Offer(next, Visible(next), new TextSpan(0, next.Length)));
        // The original scheduler owns the second offer; complete only its second timer.
        delay.Complete(1);
        await SpinUntilAsync(() => { lock (versions) return versions.Contains(next.Version); });
        lock (versions) Assert.Equal(new[] { next.Version }, versions);
        Assert.Equal(1, policy.FullCalls);
    }

    /// <summary>A Full request may still be provisional and must carry no global count.</summary>
    [Fact]
    public async Task Idle_full_provisional_result_never_claims_document_count()
    {
        using var document = new Document("x: [");
        var policy = new ProbePolicy(DocumentKind.Yaml, AnalysisCompleteness.Provisional);
        using var driver = new NativeFormatSessionDriver(policy);
        var delay = new ControlledDelay();
        var published = new TaskCompletionSource<DocumentAnalysis>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var idle = new NativeIdleFullAnalysis(driver, policy.Kind,
            (_, result, _) => published.TrySetResult(result), delay: delay.WaitAsync);
        var snapshot = document.Snapshot;
        Assert.Equal(IdleFullOffer.Scheduled, idle.Offer(snapshot, Visible(snapshot),
            new TextSpan(0, snapshot.Length)));
        delay.Complete(0);
        var result = await published.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(AnalysisCompleteness.Provisional, result.Completeness);
        Assert.Null(result.TotalDiagnosticCount);
        Assert.Equal(1, policy.FullCalls);
        Assert.Equal(IdleFullOffer.AlreadyPendingOrAttempted,
            idle.Offer(snapshot, Visible(snapshot), new TextSpan(0, snapshot.Length)));
    }

    /// <summary>Policy and memory guards defer expensive Full work without claiming validity.</summary>
    [Fact]
    public void Idle_policy_and_memory_budget_are_explicit()
    {
        using var document = new Document("plain");
        var policy = new ProbePolicy(DocumentKind.PlainText, AnalysisCompleteness.Complete);
        using var driver = new NativeFormatSessionDriver(policy);
        using var idle = new NativeIdleFullAnalysis(driver, policy.Kind, (_, _, _) => { });
        var snapshot = document.Snapshot;
        Assert.Equal(IdleFullOffer.PolicyLimited,
            idle.Offer(snapshot, Visible(snapshot), new TextSpan(0, snapshot.Length)));
        Assert.False(NativeIdleFullAnalysis.HasMemoryBudget(DocumentKind.Toml,
            100 * 1024 * 1024, 512L * 1024 * 1024, 0, 0));
        Assert.True(NativeIdleFullAnalysis.HasMemoryBudget(DocumentKind.Json,
            1024 * 1024, 8L * 1024 * 1024 * 1024, 0, 0));
        Assert.False(NativeIdleFullAnalysis.HasMemoryBudget(DocumentKind.Json,
            32 * 1024 * 1024, 0, 200L * 1024 * 1024, 150L * 1024 * 1024));
    }

    /// <summary>Builds a truthfully provisional visible result for scheduler offers.</summary>
    private static DocumentAnalysis Visible(TextSnapshot snapshot) => new(snapshot.Version,
        new TextSpan(0, snapshot.Length), AnalysisCompleteness.Provisional,
        new SemanticNode("document", new TextSpan(0, snapshot.Length)), [], [], null);

    /// <summary>Waits for a callback transition without a fixed parser/timer sleep.</summary>
    private static async Task SpinUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(condition(), "Expected one idle Full callback.");
    }

    /// <summary>Captures requested delays and completes them only when the test chooses.</summary>
    private sealed class ControlledDelay
    {
        public List<Request> Requests { get; } = [];

        public Task WaitAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            Requests.Add(new Request(duration, completion.Task, completion));
            return completion.Task;
        }

        public void Complete(int index) => Requests[index].Completion.TrySetResult();

        public sealed record Request(TimeSpan Duration, Task Task, TaskCompletionSource Completion);
    }

    /// <summary>Counts actual Full calls independently of the scheduler's offer state.</summary>
    private sealed class ProbePolicy(DocumentKind kind, AnalysisCompleteness full) : IIncrementalDocumentPolicy
    {
        private int _fullCalls;
        public DocumentKind Kind => kind;
        public string DisplayName => "Probe";
        public int FullCalls => Volatile.Read(ref _fullCalls);
        public IFormatSession CreateSession() => new ProbeSession(this, full);
        public FormatAnalysis Analyze(string text, CancellationToken cancellationToken = default) =>
            new(text, new SemanticNode("document", new TextSpan(0, text.Length)), [], []);
        public string Format(string text) => text;
        public string RenderHtml(FormatAnalysis analysis) => string.Empty;

        private sealed class ProbeSession(ProbePolicy owner, AnalysisCompleteness full) : IFormatSession
        {
            public DocumentAnalysis Analyze(TextSnapshot snapshot, IReadOnlyList<VersionedEdit> changesSinceCommittedState,
                AnalysisRequest request, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (request.Scope == AnalysisScope.Full) Interlocked.Increment(ref owner._fullCalls);
                return new DocumentAnalysis(snapshot.Version,
                    full == AnalysisCompleteness.Complete ? new TextSpan(0, snapshot.Length) : request.VisibleRange,
                    full, new SemanticNode("document", new TextSpan(0, snapshot.Length)), [], [],
                    full == AnalysisCompleteness.Complete ? 0 : null);
            }

            public void Dispose() { }
        }
    }
}
