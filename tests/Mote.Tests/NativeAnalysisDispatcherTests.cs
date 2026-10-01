using Mote.Engine;
using Mote.Formats;
using Mote.Native;

namespace Mote.Tests;

/// <summary>Certifies bounded latest-interest dispatch, Full priority and demand promotion.</summary>
[Collection("Native idle scheduler")]
public sealed class NativeAnalysisDispatcherTests
{
    /// <summary>Navigation cannot move Full behind a stream of same-version interests or reset content delay.</summary>
    [Fact]
    public async Task Reserved_full_follows_one_content_before_latest_viewport()
    {
        using var document = new Document("0123456789");
        var policy = new ProbePolicy();
        using var driver = new NativeFormatSessionDriver(policy);
        using var dispatcher = new NativeAnalysisDispatcher(driver, _ => true);
        var snapshot = document.Snapshot;
        var first = dispatcher.AnalyzeAsync(snapshot, Visible(0), default);
        await policy.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var content = dispatcher.AnalyzeAsync(snapshot, Visible(1), default, content: true, delayMilliseconds: 80);
        var full = dispatcher.ReserveFullAsync(snapshot, new TextSpan(2, 1), default);
        var oldViewport = dispatcher.AnalyzeAsync(snapshot, Visible(3), default);
        var lastViewport = dispatcher.AnalyzeAsync(snapshot, Visible(4), default);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => oldViewport);
        policy.Release.Set();
        await Task.WhenAll(first, content, full, lastViewport).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "Visible:0", "Visible:1", "Full:2", "Visible:4" }, policy.Calls);
        Assert.Equal(1, policy.MaximumActive);
    }

    /// <summary>An actual edit retires old Full reservation before driver execution.</summary>
    [Fact]
    public async Task Edit_cancels_old_full_and_dispatches_latest_content()
    {
        using var document = new Document("0123456789");
        var policy = new ProbePolicy();
        using var driver = new NativeFormatSessionDriver(policy);
        using var dispatcher = new NativeAnalysisDispatcher(driver, _ => true);
        var first = dispatcher.AnalyzeAsync(document.Snapshot, Visible(0), default);
        await policy.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var full = dispatcher.ReserveFullAsync(document.Snapshot, new TextSpan(2, 1), default);
        var next = document.Apply(new TextChange(0, 1, "a"));
        var content = dispatcher.AnalyzeAsync(next, Visible(1), default, content: true);
        policy.Release.Set();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => full);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await content.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("Full:2", policy.Calls);
    }

    /// <summary>Memory admission is repeated when the reservation reaches service, not only at demand time.</summary>
    [Fact]
    public async Task Dispatch_resource_refusal_is_explicit_and_does_not_run_full()
    {
        using var document = new Document("0123456789");
        var policy = new ProbePolicy();
        using var driver = new NativeFormatSessionDriver(policy);
        using var dispatcher = new NativeAnalysisDispatcher(driver, _ => false);
        await Assert.ThrowsAsync<NativeFullAnalysisDeferredException>(() =>
            dispatcher.ReserveFullAsync(document.Snapshot, new TextSpan(0, 1), default));
        Assert.Empty(policy.Calls);
    }

    /// <summary>Demand wakes an existing idle delay without another timer or a second Full attempt.</summary>
    [Fact]
    public async Task Demand_promotes_idle_timer_and_preserves_terminal_outcome()
    {
        using var document = new Document("0123456789");
        var policy = new ProbePolicy(blockFirst: false);
        using var driver = new NativeFormatSessionDriver(policy);
        using var dispatcher = new NativeAnalysisDispatcher(driver, _ => true);
        var timers = 0;
        var published = new TaskCompletionSource<DocumentAnalysis>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var idle = new NativeIdleFullAnalysis(driver, DocumentKind.Csv,
            (_, analysis, _) => published.TrySetResult(analysis),
            delay: (_, token) => { Interlocked.Increment(ref timers); return Task.Delay(Timeout.Infinite, token); },
            dispatcher: dispatcher);
        var snapshot = document.Snapshot;
        var visible = new DocumentAnalysis(snapshot.Version, new TextSpan(0, 1),
            AnalysisCompleteness.Provisional, new SemanticNode("document", new TextSpan(0, snapshot.Length)), [], [], null);
        Assert.Equal(IdleFullOffer.Scheduled, idle.Offer(snapshot, visible, new TextSpan(0, 1)));
        Assert.Equal(IdleFullOffer.Scheduled, idle.Demand(snapshot, new TextSpan(0, 1)));
        await published.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, timers);
        Assert.Equal(new[] { "Full:0" }, policy.Calls);
        Assert.Equal(IdleFullOffer.Complete, idle.Demand(snapshot, new TextSpan(0, 1), retry: true));
    }

    /// <summary>Navigation cannot silently restart deferred Full; explicit retry rechecks admission once.</summary>
    [Fact]
    public async Task Deferred_demand_stays_deferred_until_explicit_retry()
    {
        using var document = new Document("0123456789");
        var policy = new ProbePolicy(blockFirst: false);
        using var driver = new NativeFormatSessionDriver(policy);
        var admit = false;
        using var dispatcher = new NativeAnalysisDispatcher(driver, _ => Volatile.Read(ref admit));
        var deferred = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var published = new TaskCompletionSource<DocumentAnalysis>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var idle = new NativeIdleFullAnalysis(driver, DocumentKind.Csv,
            (_, analysis, _) => published.TrySetResult(analysis),
            onError: (_, error) => deferred.TrySetResult(error), dispatcher: dispatcher);
        var range = new TextSpan(0, 1);
        Assert.Equal(IdleFullOffer.Scheduled, idle.Demand(document.Snapshot, range));
        Assert.IsType<NativeFullAnalysisDeferredException>(await deferred.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Volatile.Write(ref admit, true);
        Assert.Equal(IdleFullOffer.Deferred, idle.Demand(document.Snapshot, range));
        Assert.Empty(policy.Calls);
        Assert.Equal(IdleFullOffer.Scheduled, idle.Demand(document.Snapshot, range, retry: true));
        await published.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "Full:0" }, policy.Calls);
    }

    /// <summary>A parser fault is visible and remains Failed until explicit retry, never navigation.</summary>
    [Fact]
    public async Task Failed_demand_requires_explicit_retry()
    {
        using var document = new Document("0123456789");
        var policy = new ProbePolicy(blockFirst: false) { FailFull = true };
        using var driver = new NativeFormatSessionDriver(policy);
        using var dispatcher = new NativeAnalysisDispatcher(driver, _ => true);
        var failed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var published = new TaskCompletionSource<DocumentAnalysis>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var idle = new NativeIdleFullAnalysis(driver, DocumentKind.Csv,
            (_, analysis, _) => published.TrySetResult(analysis),
            onError: (_, error) => failed.TrySetResult(error), dispatcher: dispatcher);
        var range = new TextSpan(0, 1);
        Assert.Equal(IdleFullOffer.Scheduled, idle.Demand(document.Snapshot, range));
        Assert.IsType<InvalidOperationException>(await failed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(IdleFullOffer.Failed, idle.Demand(document.Snapshot, range));
        Assert.Single(policy.Calls);
        policy.FailFull = false;
        Assert.Equal(IdleFullOffer.Scheduled, idle.Demand(document.Snapshot, range, retry: true));
        await published.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "Full:0", "Full:0" }, policy.Calls);
    }

    /// <summary>Creates distinguishable visible requests without parser-specific state.</summary>
    private static AnalysisRequest Visible(int start) => new(new TextSpan(start, 1), AnalysisScope.Visible);

    /// <summary>A cancellation-aware blocking first turn makes dispatch ordering deterministic.</summary>
    private sealed class ProbePolicy(bool blockFirst = true) : IIncrementalDocumentPolicy
    {
        public DocumentKind Kind => DocumentKind.Csv;
        public string DisplayName => "Dispatcher probe";
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new(false);
        public List<string> Calls { get; } = [];
        public bool FailFull { get; set; }
        public int MaximumActive { get; private set; }
        private bool BlockFirst { get; } = blockFirst;
        private int _active;
        public IFormatSession CreateSession() => new Session(this);
        public FormatAnalysis Analyze(string text, CancellationToken cancellationToken = default) =>
            new(text, new SemanticNode("document", new TextSpan(0, text.Length)), [], []);
        public string Format(string text) => text;
        public string RenderHtml(FormatAnalysis analysis) => string.Empty;

        private sealed class Session(ProbePolicy owner) : IFormatSession
        {
            public DocumentAnalysis Analyze(TextSnapshot snapshot, IReadOnlyList<VersionedEdit> changes,
                AnalysisRequest request, CancellationToken cancellationToken = default)
            {
                var active = Interlocked.Increment(ref owner._active);
                owner.MaximumActive = Math.Max(active, owner.MaximumActive);
                try
                {
                    owner.Calls.Add($"{request.Scope}:{request.VisibleRange.Start}");
                    if (owner.BlockFirst && owner.Calls.Count == 1)
                    {
                        owner.Entered.TrySetResult();
                        owner.Release.Wait(cancellationToken);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    if (owner.FailFull && request.Scope == AnalysisScope.Full)
                        throw new InvalidOperationException("Probe parser fault.");
                    return new DocumentAnalysis(snapshot.Version, new TextSpan(0, snapshot.Length),
                        AnalysisCompleteness.Complete, new SemanticNode("document", new TextSpan(0, snapshot.Length)), [], [], 0);
                }
                finally { Interlocked.Decrement(ref owner._active); }
            }
            public void Dispose() { }
        }
    }
}
