using Mote.Engine;

namespace Mote.Tests;

/// <summary>Independent boundary tests preserve returned commits and primary failure identity.</summary>
public sealed class EngineSaveObserverIndependentTests
{
    /// <summary>Cancellation after the move returned must not retroactively fail a durable Save.</summary>
    [Fact]
    public async Task Cancellation_after_returned_commit_does_not_invent_cancelled_save()
    {
        using var temp = new RepoTemp();
        using var document = new Document("exact bytes");
        using var cancellation = new CancellationTokenSource();
        var observer = new RecordingObserver(e =>
        {
            if (e.Phase == DocumentSavePhase.CommitMove && e.Edge == DocumentSaveEdge.Succeeded)
                cancellation.Cancel();
        });
        var target = temp.File("target.txt");
        await document.SaveAsync(target, cancellation.Token, observer);
        Assert.Equal("exact bytes", await File.ReadAllTextAsync(target));
        Assert.False(document.IsModified);
        Assert.DoesNotContain(observer.Events, e => e.Edge is DocumentSaveEdge.Cancelled or DocumentSaveEdge.Failed);
        Assert.Equal(DocumentSavePhase.Bookkeeping, observer.Events.Last().Phase);
        Assert.Equal(DocumentSaveEdge.Succeeded, observer.Events.Last().Edge);
    }

    /// <summary>Uncommon post-commit provider errors keep their identity and do not fabricate recovery work.</summary>
    [Fact]
    public async Task Non_filesystem_saved_stamp_error_preserves_original_contract()
    {
        using var temp = new RepoTemp();
        using var document = new Document("exact bytes");
        var error = new InvalidOperationException("provider failure must not become content");
        document.SaveOperations = new StampFault(error);
        var observer = new RecordingObserver(_ => throw new OperationCanceledException("observer only"));
        var target = temp.File("target.txt");
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => document.SaveAsync(target, default, observer));
        Assert.Same(error, actual);
        Assert.Equal("exact bytes", await File.ReadAllTextAsync(target));
        Assert.True(document.IsModified);
        Assert.Null(document.FilePath);
        Assert.Null(document.PendingSaveRecovery);
        Assert.Null(actual.Data[SaveFailureInfo.DataKey]);
        var failure = Assert.Single(observer.Events, e => e.Edge == DocumentSaveEdge.Failed);
        Assert.Equal(DocumentSavePhase.SavedStamp, failure.Phase);
        Assert.Null(failure.HResult);
        Assert.DoesNotContain(observer.Events, e => e.Phase is DocumentSavePhase.FailureCleanup or DocumentSavePhase.FailureInspection);
        document.SaveOperations = new DocumentSaveOperations();
        await document.SaveAsync(temp.File("second.txt"));
        Assert.False(document.IsModified);
    }

    /// <summary>Failure inspection errors and a throwing observer cannot substitute for the primary commit error.</summary>
    [Fact]
    public async Task Inspection_failure_is_secondary_and_observer_does_not_change_recovery_ownership()
    {
        using var temp = new RepoTemp();
        using var document = new Document("exact bytes");
        var primary = new IOException("primary", unchecked((int)0x80070005));
        var secondary = new InvalidOperationException("inspection");
        document.SaveOperations = new CommitAndInspectionFault(primary, secondary);
        var observer = new RecordingObserver(_ => throw new InvalidOperationException("observer"));
        var target = temp.File("target.txt");
        Assert.Same(primary, await Assert.ThrowsAsync<IOException>(() => document.SaveAsync(target, default, observer)));
        Assert.False(File.Exists(target));
        var recovery = Assert.IsType<SaveRecovery>(document.PendingSaveRecovery);
        Assert.Equal("exact bytes", await File.ReadAllTextAsync(recovery.Path));
        var info = Assert.IsType<SaveFailureInfo>(primary.Data[SaveFailureInfo.DataKey]);
        Assert.Equal(SaveFileOutcome.Unknown, info.TargetOutcome);
        Assert.Equal(SaveFileOutcome.Unknown, info.RecoveryOutcome);
        Assert.Equal(secondary.HResult, info.SecondaryHResult);
        Assert.Contains(observer.Events, e => e.Phase == DocumentSavePhase.FailureInspection && e.Edge == DocumentSaveEdge.Succeeded);
        Assert.DoesNotContain(observer.Events, e => e.Phase == DocumentSavePhase.FailureCleanup);
    }

    /// <summary>Captures observations before injecting a nonfatal observer error.</summary>
    private sealed class RecordingObserver(Action<DocumentSaveObservation> action) : IDocumentSaveObserver
    {
        /// <summary>Serial callback evidence, consumed only after Save has returned.</summary>
        internal readonly List<DocumentSaveObservation> Events = [];

        /// <summary>Records the fixed schema without reading exception text or document content.</summary>
        public void Observe(in DocumentSaveObservation observation)
        {
            Events.Add(observation);
            action(observation);
        }
    }

    /// <summary>Models an uncommon provider failure after the target bytes were installed.</summary>
    private sealed class StampFault(Exception error) : DocumentSaveOperations
    {
        /// <summary>Throws the exact injected instance without modifying the successful move.</summary>
        internal override FileStamp ReadSavedStamp(string target) => throw error;
    }

    /// <summary>Models commit failure with a separately unavailable outcome inspector.</summary>
    private sealed class CommitAndInspectionFault(IOException primary, Exception secondary) : DocumentSaveOperations
    {
        /// <summary>Leaves the owned recovery file in place and throws the original commit error.</summary>
        internal override void Move(string stage, string target) => throw primary;

        /// <summary>Degrades inspection to Unknown through the established recovery boundary.</summary>
        internal override Task<(SaveFileOutcome Outcome, int? Error)> InspectAsync(string path, byte[]? original, byte[]? saved) =>
            throw secondary;
    }
}
