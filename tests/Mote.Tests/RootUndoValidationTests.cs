using Mote.Engine;

namespace Mote.Tests;

/// <summary>
/// Independent public-contract checks for persistent-root undo history, including
/// edits that exceeded the former inverse-string history budget.
/// </summary>
[Collection("Root undo validation")]
public sealed class RootUndoValidationTests
{
    private const int Mib = 1024 * 1024;

    /// <summary>A 50 Mi-code-unit paste is reversible, including after an ordinary later edit.</summary>
    [Fact]
    public void Fifty_mib_insert_and_following_small_edit_remain_undoable()
    {
        using var document = new Document("seed");
        var initial = document.Snapshot;
        var paste = new string('x', 50 * Mib);
        var pasted = document.Apply(new TextChange(4, 0, paste));
        Assert.Equal(50 * Mib + 4, pasted.Length);
        Assert.Equal("seed", pasted.GetText(0, 4));
        Assert.Equal("x", pasted.GetText(pasted.Length - 1, 1));
        Assert.True(document.CanUndo);

        document.Apply(new TextChange(document.Snapshot.Length, 0, "!"));
        Assert.True(document.Undo());
        Assert.Equal(pasted.Length, document.Snapshot.Length);
        Assert.True(document.Undo());
        Assert.Equal("seed", document.Snapshot.GetText());
        Assert.False(document.CanUndo);
        Assert.True(document.Redo());
        Assert.Equal(pasted.Length, document.Snapshot.Length);
        Assert.True(document.Redo());
        Assert.Equal("!", document.Snapshot.GetText(document.Snapshot.Length - 1, 1));
        Assert.True(initial.Version < pasted.Version);

        Assert.True(document.Undo());
        document.Apply(new TextChange(document.Snapshot.Length, 0, "?"));
        Assert.False(document.CanRedo);
        Assert.True(document.Undo());
        Assert.True(document.Undo());
        Assert.Equal("seed", document.Snapshot.GetText());
    }

    /// <summary>A large destructive Delete must restore the exact removed source on Undo.</summary>
    [Fact]
    public void Fifty_mib_delete_is_undoable_and_old_snapshot_survives_pruning_and_disposal()
    {
        var text = new string('a', 25 * Mib) + "😀\r\n" + new string('b', 25 * Mib);
        var document = new Document(text);
        var before = document.Snapshot;
        var eventVersions = new List<long>();
        document.Changed += (_, change) =>
        {
            eventVersions.Add(change.After.Version);
            if (change.After.Version == 2)
            {
                Assert.Equal(new TextChange(0, 0, text), change.Change);
                Assert.Equal(0, change.Before.Length);
                Assert.Equal(text.Length, change.After.Length);
            }
        };
        var deleted = document.Apply(new TextChange(0, before.Length, ""));
        Assert.Equal(0, deleted.Length);
        Assert.True(document.CanUndo);
        Assert.True(document.Undo());
        Assert.Equal(before.Length, document.Snapshot.Length);
        Assert.Equal("😀\r\n", document.Snapshot.GetText(25 * Mib, 4));
        Assert.Equal(2, document.Snapshot.LineCount);
        Assert.True(document.Redo());
        Assert.Equal(0, document.Snapshot.Length);
        Assert.Equal(new long[] { 1, 2, 3 }, eventVersions);
        document.Dispose();
        Assert.Equal("a", before.GetText(0, 1));
        Assert.Equal("b", before.GetText(before.Length - 1, 1));
        Assert.Equal(0, deleted.Length);
    }

    /// <summary>A second oversized action may evict the previous one, never itself.</summary>
    [Fact]
    public void Sequential_oversized_actions_keep_latest_action()
    {
        using var document = new Document();
        var giant = new string('g', 20 * Mib);
        document.Apply(new TextChange(0, 0, giant));
        document.Apply(new TextChange(document.Snapshot.Length, 0, giant));
        Assert.True(document.CanUndo);
        Assert.True(document.Undo());
        Assert.Equal(giant.Length, document.Snapshot.Length);
        Assert.Equal("g", document.Snapshot.GetText(giant.Length - 1, 1));
        Assert.False(document.CanUndo);
        Assert.True(document.Redo());
        Assert.Equal(2 * giant.Length, document.Snapshot.Length);
    }

    /// <summary>
    /// Once edits after a giant action exceed the soft budget, contiguous suffix history
    /// takes priority: drop the giant barrier and oldest ordinary action, not newest edits.
    /// </summary>
    [Fact]
    public void Ordinary_suffix_over_budget_evicts_prior_giant_and_oldest_ordinary_action()
    {
        using var document = new Document();
        document.Apply(new TextChange(0, 0, new string('g', 20 * Mib)));
        foreach (var marker in new[] { 'a', 'b', 'c' })
            document.Apply(new TextChange(document.Snapshot.Length, 0, new string(marker, 8 * Mib)));

        Assert.Equal(44 * Mib, document.Snapshot.Length);
        Assert.Equal("g", document.Snapshot.GetText(0, 1));
        Assert.Equal("a", document.Snapshot.GetText(20 * Mib, 1));
        Assert.Equal("b", document.Snapshot.GetText(28 * Mib, 1));
        Assert.Equal("c", document.Snapshot.GetText(36 * Mib, 1));

        Assert.True(document.Undo());
        Assert.Equal(36 * Mib, document.Snapshot.Length);
        Assert.Equal("b", document.Snapshot.GetText(document.Snapshot.Length - 1, 1));
        Assert.True(document.Undo());
        Assert.Equal(28 * Mib, document.Snapshot.Length);
        Assert.Equal("a", document.Snapshot.GetText(document.Snapshot.Length - 1, 1));
        Assert.False(document.CanUndo);
        Assert.False(document.Undo());
        Assert.True(document.Redo());
        Assert.Equal(36 * Mib, document.Snapshot.Length);
        Assert.True(document.Redo());
        Assert.Equal(44 * Mib, document.Snapshot.Length);
        Assert.Equal("c", document.Snapshot.GetText(document.Snapshot.Length - 1, 1));
    }

    /// <summary>Soft byte pruning and the hard action count preserve the newest operations.</summary>
    [Fact]
    public void Ordinary_byte_budget_and_action_count_drop_oldest_only()
    {
        using (var document = new Document())
        {
            var part = new string('p', 8 * Mib);
            for (var i = 0; i < 3; i++)
                document.Apply(new TextChange(document.Snapshot.Length, 0, part));
            Assert.True(document.Undo());
            Assert.True(document.Undo());
            Assert.False(document.CanUndo);
            Assert.Equal(part.Length, document.Snapshot.Length);
        }

        using (var document = new Document())
        {
            for (var i = 0; i < 513; i++)
                document.Apply(new TextChange(document.Snapshot.Length, 0, "x"));
            for (var i = 0; i < 512; i++) Assert.True(document.Undo());
            Assert.False(document.Undo());
            Assert.Equal("x", document.Snapshot.GetText());
            Assert.Equal(1_025, document.Snapshot.Version);
        }
    }

    /// <summary>Undo/redo events carry exact prior-coordinate deltas and monotone snapshots.</summary>
    [Fact]
    public void Replacement_events_preserve_exact_inserted_text_and_utf16_offsets()
    {
        using var document = new Document("a😀\r\nb");
        var events = new List<DocumentChangedEventArgs>();
        document.Changed += (_, e) => events.Add(e);
        var initial = document.Snapshot;
        var after = document.Apply(new TextChange(1, 4, "Ω\n"));
        Assert.Equal("aΩ\nb", after.GetText());
        Assert.True(document.Undo());
        Assert.True(document.Redo());
        Assert.Equal(3, events.Count);
        Assert.Equal(new TextChange(1, 4, "Ω\n"), events[0].Change);
        Assert.Equal(new TextChange(1, 2, "😀\r\n"), events[1].Change);
        Assert.Equal(new TextChange(1, 4, "Ω\n"), events[2].Change);
        Assert.Same(initial, events[0].Before);
        Assert.Same(after, events[0].After);
        for (var i = 0; i < events.Count; i++)
        {
            Assert.Equal(i, events[i].Before.Version);
            Assert.Equal(i + 1, events[i].After.Version);
            if (i > 0) Assert.Same(events[i - 1].After, events[i].Before);
        }
        Assert.Equal("a😀\r\nb", events[1].After.GetText());
        Assert.Equal("aΩ\nb", events[2].After.GetText());
    }

    /// <summary>No-op and rejected edits do not consume history or invalidate Redo.</summary>
    [Fact]
    public void Noop_and_invalid_edit_leave_snapshot_events_and_redo_unchanged()
    {
        using var document = new Document("😀");
        document.Apply(new TextChange(2, 0, "x"));
        Assert.True(document.Undo());
        var before = document.Snapshot;
        var count = 0;
        document.Changed += (_, _) => count++;
        Assert.Same(before, document.Apply(new TextChange(0, 0, "")));
        Assert.Throws<ArgumentException>(() => document.Apply(new TextChange(1, 0, "z")));
        Assert.Same(before, document.Snapshot);
        Assert.True(document.CanRedo);
        Assert.Equal(0, count);
        Assert.True(document.Redo());
        Assert.Equal("😀x", document.Snapshot.GetText());
        Assert.Equal(1, count);
    }

    /// <summary>Undoing a large edit restores the successfully saved clean state.</summary>
    [Fact]
    public async Task Large_edit_undo_restores_saved_state_and_cancelled_save_does_not_move_it()
    {
        using var temp = new RepoTemp();
        using var document = new Document("saved");
        await document.SaveAsync(temp.File("root-history.txt"));
        var saved = document.Snapshot;
        Assert.False(document.IsModified);
        document.Apply(new TextChange(5, 0, new string('z', 20 * Mib)));
        Assert.True(document.IsModified);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => document.SaveAsync(cancellationToken: cancellation.Token));
        Assert.True(document.IsModified);
        Assert.True(document.Undo());
        Assert.False(document.IsModified);
        Assert.Equal("saved", document.Snapshot.GetText());
        Assert.True(document.Redo());
        Assert.True(document.IsModified);
        Assert.Equal("saved", saved.GetText());
        Assert.Equal("saved", await File.ReadAllTextAsync(temp.File("root-history.txt")));
    }
}
