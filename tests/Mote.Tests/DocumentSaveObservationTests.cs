using Mote.Engine;

namespace Mote.Tests;

/// <summary>Phase evidence preserves persistence policy, original errors and immutable saved identity.</summary>
public sealed class DocumentSaveObservationTests
{
    /// <summary>A new target has one balanced non-overwriting commit and no invented replacement check.</summary>
    [Fact]
    public async Task New_target_reports_balanced_actual_version_and_exact_bytes()
    {
        using var temp = new RepoTemp();
        using var document = new Document("initial");
        document.Apply(new TextChange(0, 7, "saved"));
        var version = document.Snapshot.Version;
        var observer = new Observer();
        var path = temp.File("target.txt");
        await document.SaveAsync(path, default, observer);
        Assert.Equal("saved", await File.ReadAllTextAsync(path));
        Assert.False(document.IsModified);
        Assert.Equal(new[] { DocumentSavePhase.GateWait, DocumentSavePhase.SnapshotCapture,
            DocumentSavePhase.TargetCheck, DocumentSavePhase.TempEncodeWrite, DocumentSavePhase.TempFlush,
            DocumentSavePhase.TempHash, DocumentSavePhase.CommitMove, DocumentSavePhase.SavedStamp,
            DocumentSavePhase.Bookkeeping }, observer.Events.Where(e => e.Edge == DocumentSaveEdge.Entered).Select(e => e.Phase));
        AssertBalanced(observer);
        Assert.All(observer.Events.Take(3), e => Assert.Null(e.SnapshotVersion));
        Assert.All(observer.Events.Skip(3), e => Assert.Equal(version, e.SnapshotVersion));
    }

    /// <summary>The explicitly approved route performs final verification and replacement, not a new-file move.</summary>
    [Fact]
    public async Task Approved_existing_target_reports_replace()
    {
        using var temp = new RepoTemp();
        var path = temp.File("target.txt");
        await File.WriteAllTextAsync(path, "old");
        var token = await FileOverwriteToken.CaptureAsync(path);
        using var document = new Document("new");
        var observer = new Observer();
        await document.SaveOverAsync(token, default, observer);
        Assert.Equal("new", await File.ReadAllTextAsync(path));
        Assert.Contains(observer.Events, e => e.Phase == DocumentSavePhase.FinalTargetCheck && e.Edge == DocumentSaveEdge.Succeeded);
        Assert.Contains(observer.Events, e => e.Phase == DocumentSavePhase.CommitReplace && e.Edge == DocumentSaveEdge.Succeeded);
        Assert.DoesNotContain(observer.Events, e => e.Phase == DocumentSavePhase.CommitMove);
        AssertBalanced(observer);
    }

    /// <summary>Queued Save reports the snapshot acquired after its gate, not the pre-admission snapshot.</summary>
    [Fact]
    public async Task Queued_save_captures_later_version_after_gate()
    {
        using var temp = new RepoTemp();
        using var document = new Document("one");
        var operations = new BlockingMove();
        document.SaveOperations = operations;
        var first = Task.Run(() => document.SaveAsync(temp.File("one.txt")));
        Assert.True(operations.Entered.Wait(TimeSpan.FromSeconds(5)));
        try
        {
            var observer = new Observer();
            var second = document.SaveAsync(temp.File("two.txt"), default, observer);
            Assert.Single(observer.Events);
            Assert.Equal(DocumentSavePhase.GateWait, observer.Events[0].Phase);
            document.Apply(new TextChange(0, 3, "two"));
            var version = document.Snapshot.Version;
            operations.Release.Set();
            await first;
            await second;
            Assert.Equal("two", await File.ReadAllTextAsync(temp.File("two.txt")));
            Assert.Equal(version, observer.Events.Single(e => e.Phase == DocumentSavePhase.SnapshotCapture && e.Edge == DocumentSaveEdge.Succeeded).SnapshotVersion);
            AssertBalanced(observer);
        }
        finally { operations.Release.Set(); await first; }
    }

    /// <summary>Concurrent edits do not change captured bytes or falsely mark their new state clean.</summary>
    [Fact]
    public async Task Observer_can_edit_after_capture_without_changing_saved_snapshot()
    {
        using var temp = new RepoTemp();
        using var document = new Document("before");
        var version = document.Snapshot.Version;
        var observer = new Observer(e =>
        {
            if (e.Phase == DocumentSavePhase.SnapshotCapture && e.Edge == DocumentSaveEdge.Succeeded)
                document.Apply(new TextChange(0, 6, "after"));
        });
        var path = temp.File("target.txt");
        await document.SaveAsync(path, default, observer);
        Assert.Equal("before", await File.ReadAllTextAsync(path));
        Assert.Equal("after", document.Snapshot.GetText());
        Assert.True(document.IsModified);
        Assert.All(observer.Events.Skip(3), e => Assert.Equal(version, e.SnapshotVersion));
    }

    /// <summary>Cross-thread state reads can finish during every callback; the state lock is never held.</summary>
    [Fact]
    public async Task Callbacks_are_outside_document_state_lock()
    {
        using var temp = new RepoTemp();
        using var document = new Document("bytes");
        var readsCompleted = true;
        var observer = new Observer(_ => readsCompleted &= Task.Run(() => document.Snapshot.Version).Wait(TimeSpan.FromSeconds(2)));
        await document.SaveAsync(temp.File("target.txt"), default, observer);
        Assert.True(readsCompleted);
    }

    /// <summary>Throwing observers cannot change a successful commit or later save-gate usability.</summary>
    [Fact]
    public async Task Observer_errors_never_change_success()
    {
        using var temp = new RepoTemp();
        using var document = new Document("bytes");
        var observer = new Observer(_ => throw new InvalidOperationException("diagnostic failure"));
        var path = temp.File("target.txt");
        await document.SaveAsync(path, default, observer);
        Assert.Equal("bytes", await File.ReadAllTextAsync(path));
        Assert.False(document.IsModified);
        await document.SaveAsync(null, default, observer);
        AssertBalanced(observer, attempts: 2);
    }

    /// <summary>Commit and post-commit faults retain the exact primary exception and existing recovery policy.</summary>
    [Theory]
    [InlineData(DocumentSavePhase.CommitMove)]
    [InlineData(DocumentSavePhase.CommitReplace)]
    [InlineData(DocumentSavePhase.SavedStamp)]
    public async Task Filesystem_faults_report_actual_phase_and_preserve_exception(DocumentSavePhase phase)
    {
        using var temp = new RepoTemp();
        var path = temp.File("target.txt");
        if (phase == DocumentSavePhase.CommitReplace) await File.WriteAllTextAsync(path, "old");
        using var document = phase == DocumentSavePhase.CommitReplace ? await Document.OpenAsync(path) : new Document("new");
        if (phase == DocumentSavePhase.CommitReplace) document.Apply(new TextChange(0, 3, "new"));
        var primary = new IOException("must never become telemetry text", unchecked((int)0x80070005));
        document.SaveOperations = new FaultOperations(phase, primary);
        var observer = new Observer(_ => throw new Exception("ignored"));
        Assert.Same(primary, await Assert.ThrowsAsync<IOException>(() => document.SaveAsync(path, default, observer)));
        var failed = Assert.Single(observer.Events, e => e.Edge == DocumentSaveEdge.Failed);
        Assert.Equal(phase, failed.Phase);
        Assert.Equal(primary.HResult, failed.HResult);
        Assert.Contains(observer.Events, e => e.Phase == DocumentSavePhase.FailureInspection && e.Edge == DocumentSaveEdge.Succeeded);
        AssertBalanced(observer);
        Assert.True(document.IsModified);
    }

    /// <summary>Initial target rejection never pretends a temporary file or a commit was attempted.</summary>
    [Fact]
    public async Task Unapproved_target_reports_only_initial_check_failure()
    {
        using var temp = new RepoTemp();
        var path = temp.File("target.txt");
        await File.WriteAllTextAsync(path, "old");
        using var document = new Document("new");
        var observer = new Observer();
        await Assert.ThrowsAsync<IOException>(() => document.SaveAsync(path, default, observer));
        Assert.Equal(DocumentSavePhase.TargetCheck, observer.Events.Last().Phase);
        Assert.Equal(DocumentSaveEdge.Failed, observer.Events.Last().Edge);
        Assert.DoesNotContain(observer.Events, e => e.Phase == DocumentSavePhase.TempEncodeWrite);
        AssertBalanced(observer);
    }

    /// <summary>Cancellation before gate admission has no fictional snapshot or filesystem HResult.</summary>
    [Fact]
    public async Task Cancelled_gate_reports_no_snapshot()
    {
        using var document = new Document("bytes");
        var observer = new Observer();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => document.SaveAsync(null, cancellation.Token, observer));
        Assert.Equal(DocumentSaveEdge.Cancelled, observer.Events.Last().Edge);
        Assert.All(observer.Events, e => { Assert.Null(e.SnapshotVersion); Assert.Null(e.HResult); });
        AssertBalanced(observer);
    }

    /// <summary>Cancellation at commit entry cleans owned temporary bytes and leaves original bytes untouched.</summary>
    [Fact]
    public async Task Cancelled_commit_reports_cleanup_and_preserves_target()
    {
        using var temp = new RepoTemp();
        using var document = new Document("bytes");
        using var cancellation = new CancellationTokenSource();
        var observer = new Observer(e =>
        {
            if (e.Phase == DocumentSavePhase.CommitMove && e.Edge == DocumentSaveEdge.Entered) cancellation.Cancel();
        });
        var path = temp.File("target.txt");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => document.SaveAsync(path, cancellation.Token, observer));
        Assert.Contains(observer.Events, e => e.Phase == DocumentSavePhase.CommitMove && e.Edge == DocumentSaveEdge.Cancelled);
        Assert.Contains(observer.Events, e => e.Phase == DocumentSavePhase.FailureCleanup && e.Edge == DocumentSaveEdge.Succeeded);
        Assert.False(File.Exists(path));
        Assert.Null(document.PendingSaveRecovery);
        AssertBalanced(observer);
    }

    /// <summary>Disposed bookkeeping is observed as skipped although bytes were successfully committed.</summary>
    [Fact]
    public async Task Disposed_after_commit_skips_bookkeeping()
    {
        using var temp = new RepoTemp();
        using var document = new Document("bytes");
        var observer = new Observer(e =>
        {
            if (e.Phase == DocumentSavePhase.Bookkeeping && e.Edge == DocumentSaveEdge.Entered) document.Dispose();
        });
        var path = temp.File("target.txt");
        await document.SaveAsync(path, default, observer);
        Assert.Equal(DocumentSaveEdge.Skipped, observer.Events.Last().Edge);
        Assert.Equal("bytes", await File.ReadAllTextAsync(path));
        AssertBalanced(observer);
    }

    /// <summary>Encoding/flush/hash cancellation is attributed before owned-temp cleanup executes.</summary>
    [Theory]
    [InlineData(DocumentSavePhase.TempEncodeWrite)]
    [InlineData(DocumentSavePhase.TempFlush)]
    [InlineData(DocumentSavePhase.TempHash)]
    public async Task Temporary_phase_cancellation_is_balanced(DocumentSavePhase phase)
    {
        using var temp = new RepoTemp();
        using var document = new Document("bytes");
        using var cancellation = new CancellationTokenSource();
        var observer = new Observer(e =>
        {
            if (e.Phase == phase && e.Edge == DocumentSaveEdge.Entered) cancellation.Cancel();
        });
        var path = temp.File("target.txt");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => document.SaveAsync(path, cancellation.Token, observer));
        Assert.Contains(observer.Events, e => e.Phase == phase && e.Edge == DocumentSaveEdge.Cancelled);
        Assert.Contains(observer.Events, e => e.Phase == DocumentSavePhase.FailureCleanup && e.Edge == DocumentSaveEdge.Succeeded);
        Assert.False(File.Exists(path));
        AssertBalanced(observer);
    }

    /// <summary>Existing staging bytes are never adopted; creation failure emits no fictional cleanup.</summary>
    [Fact]
    public async Task Existing_temp_reports_encode_failure_without_cleanup()
    {
        using var temp = new RepoTemp();
        using var document = new Document("bytes");
        var path = temp.File("target.txt");
        var stage = Document.GetSaveRecoveryPath(path);
        await File.WriteAllTextAsync(stage, "foreign");
        var observer = new Observer();
        await Assert.ThrowsAsync<IOException>(() => document.SaveAsync(path, default, observer));
        Assert.Contains(observer.Events, e => e.Phase == DocumentSavePhase.TempEncodeWrite && e.Edge == DocumentSaveEdge.Failed);
        Assert.DoesNotContain(observer.Events, e => e.Phase == DocumentSavePhase.FailureCleanup);
        Assert.Equal("foreign", await File.ReadAllTextAsync(stage));
        AssertBalanced(observer);
    }

    /// <summary>Secondary cleanup error does not replace the fingerprint conflict or its original phase.</summary>
    [Fact]
    public async Task Final_conflict_and_cleanup_fault_are_distinct()
    {
        using var temp = new RepoTemp();
        var path = temp.File("target.txt");
        await File.WriteAllTextAsync(path, "AAAA");
        using var document = await Document.OpenAsync(path);
        document.Apply(new TextChange(0, 4, "CCCC"));
        var cleanup = new IOException("cleanup", unchecked((int)0x80070005));
        document.SaveOperations = new CleanupFault(cleanup);
        var observer = new Observer(e =>
        {
            if (e.Phase != DocumentSavePhase.FinalTargetCheck || e.Edge != DocumentSaveEdge.Entered) return;
            var stamp = File.GetLastWriteTimeUtc(path);
            File.WriteAllText(path, "BBBB");
            File.SetLastWriteTimeUtc(path, stamp);
        });
        var primary = await Assert.ThrowsAsync<IOException>(() => document.SaveAsync(null, default, observer));
        Assert.NotSame(cleanup, primary);
        Assert.Equal("FinalTargetCheck", primary.Data["Mote.Engine.SavePhase"]);
        Assert.Equal(cleanup.HResult, primary.Data["Mote.Engine.SaveCleanupHResult"]);
        Assert.Contains(observer.Events, e => e.Phase == DocumentSavePhase.FinalTargetCheck && e.Edge == DocumentSaveEdge.Failed);
        Assert.Contains(observer.Events, e => e.Phase == DocumentSavePhase.FailureCleanup && e.Edge == DocumentSaveEdge.Failed && e.HResult == cleanup.HResult);
        Assert.DoesNotContain(observer.Events, e => e.Phase == DocumentSavePhase.CommitReplace);
        Assert.NotNull(document.PendingSaveRecovery);
        AssertBalanced(observer);
    }

    /// <summary>Failure before immutable capture never fabricates a version or an arbitrary exception HResult.</summary>
    [Fact]
    public async Task Missing_path_fails_capture_with_no_version_or_hresult()
    {
        using var document = new Document("bytes");
        var observer = new Observer();
        await Assert.ThrowsAsync<InvalidOperationException>(() => document.SaveAsync(null, default, observer));
        var failure = observer.Events.Last();
        Assert.Equal(DocumentSavePhase.SnapshotCapture, failure.Phase);
        Assert.Null(failure.SnapshotVersion);
        Assert.Null(failure.HResult);
        AssertBalanced(observer);
    }

    /// <summary>Deletes fail without ownership policy or target modifications changing.</summary>
    private sealed class CleanupFault(IOException error) : DocumentSaveOperations
    {
        internal override void Delete(string stage) => throw error;
    }

    /// <summary>Every entry has one corresponding terminal edge, with no overlapping Engine phases.</summary>
    private static void AssertBalanced(Observer observer, int attempts = 1)
    {
        Assert.Equal(attempts, observer.Events.Count(e => e.Phase == DocumentSavePhase.GateWait && e.Edge == DocumentSaveEdge.Entered));
        Assert.Equal(0, observer.Events.Count % 2);
        for (var i = 0; i < observer.Events.Count; i += 2)
        {
            Assert.Equal(DocumentSaveEdge.Entered, observer.Events[i].Edge);
            Assert.Equal(observer.Events[i].Phase, observer.Events[i + 1].Phase);
            Assert.NotEqual(DocumentSaveEdge.Entered, observer.Events[i + 1].Edge);
        }
    }

    /// <summary>Collects fixed numeric evidence; optional callback exercises observer contracts.</summary>
    private sealed class Observer(Action<DocumentSaveObservation>? action = null) : IDocumentSaveObserver
    {
        internal readonly List<DocumentSaveObservation> Events = [];
        public void Observe(in DocumentSaveObservation observation)
        {
            Events.Add(observation);
            action?.Invoke(observation);
        }
    }

    /// <summary>Blocks only the first commit so queued snapshot capture is deterministic.</summary>
    private sealed class BlockingMove : DocumentSaveOperations
    {
        internal readonly ManualResetEventSlim Entered = new();
        internal readonly ManualResetEventSlim Release = new();
        private int _calls;
        internal override void Move(string stage, string target)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                Entered.Set();
                if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("test commit gate");
            }
            base.Move(stage, target);
        }
    }

    /// <summary>Injects a deterministic primary filesystem error without mutating commit policy.</summary>
    private sealed class FaultOperations(DocumentSavePhase phase, IOException primary) : DocumentSaveOperations
    {
        internal override void Move(string stage, string target)
        {
            if (phase == DocumentSavePhase.CommitMove) throw primary;
            base.Move(stage, target);
        }
        internal override void Replace(string stage, string target)
        {
            if (phase == DocumentSavePhase.CommitReplace) throw primary;
            base.Replace(stage, target);
        }
        internal override FileStamp ReadSavedStamp(string target)
        {
            if (phase == DocumentSavePhase.SavedStamp) throw primary;
            return base.ReadSavedStamp(target);
        }
    }
}
