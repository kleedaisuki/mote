using System.Collections.Concurrent;
using Mote.Engine;

namespace Mote.Tests;

/// <summary>Independent contract tests for additive snapshot/range notifications.</summary>
public sealed class RangeNotificationTests
{
    /// <summary>Both event forms describe exactly the same edits and retain their ordered delivery.</summary>
    [Fact]
    public void Range_precedes_exact_legacy_payload_for_each_version()
    {
        using var document = new Document("abc");
        var sequence = new List<string>();
        var ranges = new List<DocumentChangedRangeEventArgs>();
        var legacy = new List<DocumentChangedEventArgs>();
        document.ChangedRange += (_, change) =>
        {
            sequence.Add($"range:{change.After.Version}");
            ranges.Add(change);
        };
        document.Changed += (_, change) =>
        {
            sequence.Add($"legacy:{change.After.Version}");
            legacy.Add(change);
        };

        document.Apply(new TextChange(1, 1, "XY"));
        Assert.True(document.Undo());
        Assert.True(document.Redo());

        Assert.Equal(["range:1", "legacy:1", "range:2", "legacy:2", "range:3", "legacy:3"], sequence);
        Assert.Equal([new TextChangeRange(1, 1, 2), new TextChangeRange(1, 2, 1),
            new TextChangeRange(1, 1, 2)], ranges.Select(change => change.Change));
        Assert.Equal([new TextChange(1, 1, "XY"), new TextChange(1, 2, "b"),
            new TextChange(1, 1, "XY")], legacy.Select(change => change.Change));
        for (var i = 0; i < ranges.Count; i++)
        {
            Assert.Same(ranges[i].Before, legacy[i].Before);
            Assert.Same(ranges[i].After, legacy[i].After);
            Assert.Equal(ranges[i].After.Length,
                ranges[i].Before.Length - ranges[i].Change.DeleteLength + ranges[i].Change.InsertLength);
        }
    }

    /// <summary>One drainer completes the parent mutation before publishing a reentrant edit.</summary>
    [Fact]
    public void Reentrant_range_handler_keeps_both_event_kinds_in_version_order()
    {
        using var document = new Document();
        var sequence = new List<string>();
        document.ChangedRange += (_, change) =>
        {
            sequence.Add($"range:{change.After.Version}");
            if (change.After.Version == 1) document.Apply(new TextChange(1, 0, "b"));
        };
        document.Changed += (_, change) => sequence.Add($"legacy:{change.After.Version}");

        document.Apply(new TextChange(0, 0, "a"));

        Assert.Equal("ab", document.Snapshot.GetText());
        Assert.Equal(["range:1", "legacy:1", "range:2", "legacy:2"], sequence);
    }

    /// <summary>A legacy observer added after an edit is queued still receives exact text at dispatch.</summary>
    [Fact]
    public void Late_legacy_subscriber_gets_lazy_text_for_queued_edit()
    {
        using var document = new Document();
        var seen = new List<TextChange>();
        document.ChangedRange += (_, change) =>
        {
            if (change.After.Version != 1) return;
            document.Apply(new TextChange(1, 0, "later"));
            document.Changed += (_, legacy) => seen.Add(legacy.Change);
        };

        document.Apply(new TextChange(0, 0, "a"));

        Assert.Equal([new TextChange(1, 0, "later")], seen);
        Assert.Equal("alater", document.Snapshot.GetText());
    }

    /// <summary>An observer failure cannot strand later versions or starve legacy subscribers.</summary>
    [Fact]
    public void Range_handler_failure_does_not_strand_legacy_or_reentrant_events()
    {
        using var document = new Document();
        var sequence = new List<string>();
        document.ChangedRange += (_, change) =>
        {
            sequence.Add($"range:{change.After.Version}");
            if (change.After.Version != 1) return;
            document.Apply(new TextChange(1, 0, "b"));
            throw new InvalidOperationException("observer fault");
        };
        document.Changed += (_, change) => sequence.Add($"legacy:{change.After.Version}");

        Assert.Throws<InvalidOperationException>(() => document.Apply(new TextChange(0, 0, "a")));

        Assert.Equal("ab", document.Snapshot.GetText());
        Assert.Equal(["range:1", "legacy:1", "range:2", "legacy:2"], sequence);
    }

    /// <summary>Concurrent mutation order is identical in both ordered event streams.</summary>
    [Fact]
    public void Concurrent_edits_publish_one_monotonic_version_chain()
    {
        using var document = new Document();
        var ranges = new ConcurrentQueue<long>();
        var legacy = new ConcurrentQueue<long>();
        document.ChangedRange += (_, change) => ranges.Enqueue(change.After.Version);
        document.Changed += (_, change) => legacy.Enqueue(change.After.Version);

        Parallel.For(0, 64, _ => document.Apply(new TextChange(0, 0, "x")));

        Assert.Equal(Enumerable.Range(1, 64).Select(i => (long)i), ranges);
        Assert.Equal(Enumerable.Range(1, 64).Select(i => (long)i), legacy);
        Assert.Equal(64, document.Snapshot.Length);
    }

    /// <summary>Large Redo needs no whole inserted-text copy when only range observers exist.</summary>
    [Fact]
    public void Giant_redo_with_range_only_observer_allocates_no_contiguous_payload()
    {
        using var document = new Document();
        var text = new string('x', 50 * 1024 * 1024);
        var observedLength = -1;
        document.ChangedRange += (_, change) => observedLength = change.Change.InsertLength;
        document.Apply(new TextChange(0, 0, text));
        Assert.True(document.Undo());

        var before = GC.GetAllocatedBytesForCurrentThread();
        Assert.True(document.Redo());
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(text.Length, observedLength);
        Assert.Equal(text.Length, document.Snapshot.Length);
        Assert.True(allocated < 2L * 1024 * 1024,
            $"Range-only Redo allocated {allocated:N0} bytes; it likely materialized inserted text.");
    }

    /// <summary>Save and Undo reentered from an unlocked range callback preserve dirty-state identity.</summary>
    [Fact]
    public async Task Range_handler_can_save_and_undo_without_deadlocking_or_losing_events()
    {
        using var temp = new RepoTemp();
        var path = temp.File("range-save.txt");
        using var document = new Document("abc");
        var versions = new List<long>();
        document.ChangedRange += (_, change) =>
        {
            versions.Add(change.After.Version);
            if (change.After.Version != 1) return;
            document.SaveAsync(path).GetAwaiter().GetResult();
            Assert.True(document.Undo());
        };

        document.Apply(new TextChange(3, 0, "X"));

        Assert.Equal([1L, 2L], versions);
        Assert.Equal("abcX", await File.ReadAllTextAsync(path));
        Assert.Equal("abc", document.Snapshot.GetText());
        Assert.True(document.IsModified);
    }
}
