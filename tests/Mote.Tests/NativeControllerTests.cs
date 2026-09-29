using System.Collections.Concurrent;
using System.Diagnostics;
using Mote.Configuration;
using Mote.Formats;
using Mote.Native;
using Mote.Themes;

namespace Mote.Tests;

/// <summary>UI-independent tests of source/native projection and editor orchestration.</summary>
public sealed class NativeControllerTests
{
    /// <summary>RichEdit CRLF coordinates preserve mixed original line endings outside an edit.</summary>
    [Fact]
    public void Projection_maps_mixed_newlines_without_rewriting_unedited_source()
    {
        const string source = "a\nb\rc\r\nd";
        var projection = new NativeTextProjection(source, NativeLineEndingMode.CrLf);
        Assert.Equal("a\r\nb\r\nc\r\nd", projection.Display);
        Assert.Null(projection.Difference(projection.Display));
        Assert.Equal(3, projection.ToDisplay(2));
        var change = projection.Difference("a\r\nB\r\nc\r\nd");
        Assert.NotNull(change);
        Assert.Equal(source.IndexOf('b'), change.Value.Start);
        Assert.Equal(1, change.Value.DeleteLength);
        Assert.Equal("B", change.Value.InsertText);
        Assert.Equal("a\nB\rc\r\nd", source.Remove(change.Value.Start, change.Value.DeleteLength)
            .Insert(change.Value.Start, change.Value.InsertText));
    }

    /// <summary>Insertion after a projected newline adopts nearby source spelling.</summary>
    [Theory]
    [InlineData("a\nb", "a\r\nb\r\nc", "a\nb\nc")]
    [InlineData("a\rb", "a\r\nb\r\nc", "a\rb\rc")]
    [InlineData("a\r\nb", "a\r\nb\r\nc", "a\r\nb\r\nc")]
    public void Projection_normalizes_only_newly_inserted_line_breaks(string source, string displayEdit, string expected)
    {
        var projection = new NativeTextProjection(source, NativeLineEndingMode.CrLf);
        var change = projection.Difference(displayEdit);
        Assert.NotNull(change);
        Assert.Equal(expected, source.Remove(change.Value.Start, change.Value.DeleteLength)
            .Insert(change.Value.Start, change.Value.InsertText));
    }

    /// <summary>Deleting text between distinct CR and LF breaks must not erase a displayed blank line.</summary>
    [Fact]
    public void Projection_preserves_two_breaks_when_edit_joins_cr_and_lf()
    {
        const string source = "\ra\n";
        var projection = new NativeTextProjection(source, NativeLineEndingMode.CrLf);
        Assert.Equal("\r\na\r\n", projection.Display);
        const string editedDisplay = "\r\n\r\n";
        var change = projection.Difference(editedDisplay);
        Assert.NotNull(change);
        var result = source.Remove(change.Value.Start, change.Value.DeleteLength)
            .Insert(change.Value.Start, change.Value.InsertText);
        Assert.Equal(editedDisplay, new NativeTextProjection(result, NativeLineEndingMode.CrLf).Display);
        using var document = new Mote.Engine.Document(result);
        Assert.Equal(3, document.Snapshot.LineCount);
    }

    /// <summary>Any canonical native edit round-trips through the engine's source representation.</summary>
    [Fact]
    public void Randomized_projection_edits_round_trip_canonical_display()
    {
        var random = new Random(0x4E415449);
        var atoms = new[] { "a", "b", "😀", "\r", "\n", "\r\n" };
        var inserts = new[] { "", "X", "😀", "\r\n", "Y\r\nZ" };
        for (var caseNumber = 0; caseNumber < 5_000; caseNumber++)
        {
            var source = string.Concat(Enumerable.Range(0, random.Next(1, 18))
                .Select(_ => atoms[random.Next(atoms.Length)]));
            var projection = new NativeTextProjection(source, NativeLineEndingMode.CrLf);
            var display = projection.Display;
            var boundaries = Enumerable.Range(0, display.Length + 1).Where(index =>
                !SplitsPair(display, index) && !SplitsCrLf(display, index)).ToArray();
            var left = boundaries[random.Next(boundaries.Length)];
            var right = boundaries[random.Next(boundaries.Length)];
            if (left > right) (left, right) = (right, left);
            var edited = display[..left] + inserts[random.Next(inserts.Length)] + display[right..];
            var change = projection.Difference(edited);
            if (edited == display) { Assert.Null(change); continue; }
            Assert.NotNull(change);
            var result = source.Remove(change.Value.Start, change.Value.DeleteLength)
                .Insert(change.Value.Start, change.Value.InsertText);
            var reprojection = new NativeTextProjection(result, NativeLineEndingMode.CrLf).Display;
            Assert.True(edited == reprojection,
                $"Case {caseNumber}: source={Escape(source)}, edited={Escape(edited)}, got={Escape(reprojection)}");
        }
    }

    /// <summary>Short combinations exhaust seam joins, empty edits, and surrogate-safe boundaries.</summary>
    [Fact]
    public void Exhaustive_short_projection_edits_round_trip()
    {
        var atoms = new[] { "", "a", "😀", "\r", "\n", "\r\n" };
        var inserts = new[] { "", "X", "\r\n", "😀" };
        foreach (var first in atoms)
        foreach (var middle in atoms)
        foreach (var last in atoms)
        {
            var source = first + middle + last;
            var projection = new NativeTextProjection(source, NativeLineEndingMode.CrLf);
            var display = projection.Display;
            var boundaries = Enumerable.Range(0, display.Length + 1).Where(index =>
                !SplitsPair(display, index) && !SplitsCrLf(display, index)).ToArray();
            foreach (var left in boundaries)
            foreach (var right in boundaries.Where(index => index >= left))
            foreach (var insert in inserts)
            {
                var edited = display[..left] + insert + display[right..];
                var change = projection.Difference(edited);
                if (edited == display) { Assert.Null(change); continue; }
                Assert.NotNull(change);
                var result = source.Remove(change.Value.Start, change.Value.DeleteLength)
                    .Insert(change.Value.Start, change.Value.InsertText);
                var reprojected = new NativeTextProjection(result, NativeLineEndingMode.CrLf).Display;
                Assert.True(edited == reprojected,
                    $"source={Escape(source)}, edit={Escape(edited)}, got={Escape(reprojected)}");
            }
        }
    }

    /// <summary>Fake native shell drives open/edit/undo/redo/save with exact source persistence.</summary>
    [Fact]
    public async Task Controller_open_edit_undo_redo_save_preserves_source_newlines()
    {
        using var temp = new RepoTemp();
        var path = temp.File("note.txt");
        await File.WriteAllTextAsync(path, "a\nb\r\n");
        var shell = new FakeShell(NativeLineEndingMode.CrLf);
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.Title.Contains("note.txt") == true);
        Assert.Equal("a\r\nb\r\n", shell.Document!.Text);
        Assert.Equal(ThemePolicies.DarkId, shell.Theme?.Id);

        shell.Edit("a\r\nB\r\n");
        Assert.True(shell.Document!.IsModified);
        shell.RequestUndo();
        Assert.Equal("a\r\nb\r\n", shell.Document!.Text);
        shell.RequestRedo();
        Assert.Equal("a\r\nB\r\n", shell.Document!.Text);
        shell.RequestSave();
        await shell.PumpUntilAsync(() => shell.Document?.IsModified == false &&
            File.ReadAllText(path) == "a\nB\r\n");
        Assert.Empty(shell.Errors);
    }

    /// <summary>Startup Open must settle native preedit into the old document before its disposal.</summary>
    [Fact]
    public async Task Controller_open_completion_commits_pending_text_before_document_swap()
    {
        using var temp = new RepoTemp();
        var path = temp.File("replacement.txt");
        await File.WriteAllTextAsync(path, "disk");
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        shell.PendingText = "中";
        await shell.PumpUntilAsync(() => shell.Document?.Title.Contains("replacement.txt") == true);
        Assert.Contains("中", shell.CommittedTexts);
        Assert.Equal("disk", shell.Document!.Text);
        Assert.Equal("disk", await File.ReadAllTextAsync(path));
    }

    /// <summary>An uncommittable input composition vetoes an in-flight document replacement.</summary>
    [Fact]
    public async Task Controller_open_completion_drops_new_file_when_pending_text_cannot_commit()
    {
        using var temp = new RepoTemp();
        var path = temp.File("replacement.txt");
        await File.WriteAllTextAsync(path, "disk");
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { VetoPendingCommit = true };
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.CommitCalls > 0);
        Assert.Equal("", shell.Document!.Text);
        Assert.DoesNotContain("replacement.txt", shell.Document.Title, StringComparison.Ordinal);
        Assert.Empty(shell.CommittedTexts);
        Assert.Equal("disk", await File.ReadAllTextAsync(path));
    }

    /// <summary>A format result prepared before IME commit cannot overwrite newly committed text.</summary>
    [Fact]
    public async Task Controller_pending_text_during_format_completion_discards_stale_format()
    {
        using var temp = new RepoTemp();
        var path = temp.File("format-ime.json");
        await File.WriteAllTextAsync(path, "{\"a\":1}");
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.Text == "{\"a\":1}" &&
            shell.Analysis?.Status.Contains("v0") == true);
        shell.RequestFormat();
        await shell.WaitForPostedAsync();
        shell.PendingText = "中";
        shell.Pump();
        Assert.Contains("{\"a\":1}中", shell.CommittedTexts);
        Assert.Equal("{\"a\":1}中", shell.Document!.Text);
        Assert.True(shell.Document.IsModified);
    }

    /// <summary>Cut prepared before IME commit must not delete the old selection afterward.</summary>
    [Fact]
    public async Task Controller_pending_text_during_cut_completion_preserves_old_selection()
    {
        using var temp = new RepoTemp();
        var path = temp.File("cut-ime.txt");
        await File.WriteAllTextAsync(path, "ABCD");
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.Text == "ABCD" &&
            shell.Analysis?.Status.Contains("v0") == true);
        shell.ChangeSelection(1, 2);
        shell.RequestCut();
        await shell.WaitForPostedAsync();
        shell.PendingText = "中";
        shell.Pump();
        Assert.Contains("ABCD中", shell.CommittedTexts);
        Assert.Equal("ABCD中", shell.Document!.Text);
        Assert.Null(shell.ClipboardText);
        Assert.True(shell.Document.IsModified);
    }

    /// <summary>Only the corrected document version may populate the analysis view.</summary>
    [Fact]
    public async Task Controller_discards_stale_analysis_after_rapid_edit()
    {
        using var temp = new RepoTemp();
        var path = temp.File("data.json");
        await File.WriteAllTextAsync(path, "{\"x\":");
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.Title.Contains("data.json") == true);
        shell.Edit("{\"x\":1}");
        await shell.PumpUntilAsync(() => shell.Analysis?.Status.Contains("v1") == true &&
            shell.Analysis.DiagnosticsSummary == "No diagnostics.");
        await Task.Delay(150);
        shell.Pump();
        Assert.Equal("No diagnostics.", shell.Analysis!.DiagnosticsSummary);
        Assert.DoesNotContain(shell.Analyses, a => a.Status.Contains("v0") &&
            a.DiagnosticsSummary != "No diagnostics.");
    }

    /// <summary>An edit on the next bounded page changes only its source range.</summary>
    [Fact]
    public async Task Controller_edits_across_crlf_page_boundary_without_corrupting_prefix()
    {
        using var temp = new RepoTemp();
        var path = temp.File("large.txt");
        var prefix = new string('a', NativeEditorController.PageSize - 1) + "\r\n";
        await File.WriteAllTextAsync(path, prefix + "b");
        var shell = new FakeShell(NativeLineEndingMode.CrLf);
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.TotalLength == prefix.Length + 1);
        Assert.Equal(prefix, shell.Document!.Text);
        shell.RequestNextPage();
        Assert.Equal(prefix.Length, shell.Document!.PageStart);
        Assert.Equal("b", shell.Document.Text);
        shell.Edit("B");
        shell.RequestSave();
        await shell.PumpUntilAsync(() => shell.Document?.IsModified == false &&
            File.ReadAllText(path) == prefix + "B");
        Assert.Empty(shell.Errors);
    }

    /// <summary>An already queued open cannot dispose the document while Save As is in flight.</summary>
    [Fact]
    public async Task Controller_rejects_inflight_open_completion_during_save()
    {
        using var temp = new RepoTemp();
        var current = temp.File("current.txt");
        var pendingOpen = temp.File("other.txt");
        var saveTarget = temp.File("target.txt");
        await File.WriteAllTextAsync(current, "old");
        await File.WriteAllTextAsync(pendingOpen, "other");
        await File.WriteAllTextAsync(saveTarget, "target before");
        var shell = new FakeShell(NativeLineEndingMode.Preserve)
        {
            OpenPath = pendingOpen,
            SavePath = saveTarget
        };
        using var controller = NewController(shell, temp.Path, current);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.Title.Contains("current.txt") == true);
        shell.Edit("local edit");
        await shell.PumpUntilAsync(() => shell.Analysis?.Status.Contains("v1") == true);

        shell.RequestOpen();
        await shell.WaitForPostedAsync(); // Open completion is queued but not yet dispatched.
        shell.RequestSaveAs();             // _saving becomes true before the queued open runs.
        shell.PumpOne();
        Assert.Contains(shell.Errors, error => error.Contains("save", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("current.txt", shell.Document!.Title);
        await shell.PumpUntilAsync(() => shell.Document?.Title.Contains("target.txt") == true &&
            shell.Document.IsModified == false);
        Assert.Equal("local edit", await File.ReadAllTextAsync(saveTarget));
        Assert.Equal("old", await File.ReadAllTextAsync(current));
        Assert.Equal("other", await File.ReadAllTextAsync(pendingOpen));
    }

    /// <summary>Save on an untitled buffer can replace an existing picked target only after approval.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Controller_untitled_save_to_existing_target_honors_overwrite_decision(bool approve)
    {
        using var temp = new RepoTemp();
        var target = temp.File("existing.txt");
        await File.WriteAllTextAsync(target, "original");
        var shell = new FakeShell(NativeLineEndingMode.Preserve)
        {
            SavePath = target,
            OverwriteApproved = approve
        };
        using var controller = NewController(shell, temp.Path, null);
        controller.Run();
        shell.Edit("new text");
        shell.RequestSave();
        await shell.PumpUntilAsync(() => shell.OverwritePromptCount == 1);
        if (approve)
        {
            await shell.PumpUntilAsync(() => shell.Document?.Title.Contains("existing.txt") == true &&
                shell.Document.IsModified == false);
            Assert.Equal("new text", await File.ReadAllTextAsync(target));
        }
        else
        {
            Assert.Equal("original", await File.ReadAllTextAsync(target));
            Assert.True(shell.Document!.IsModified);
            Assert.Contains("Untitled", shell.Document.Title);
            // A denied prompt is not a permanent busy state: a later approval can save.
            await Task.Delay(50);
            shell.Pump();
            shell.OverwriteApproved = true;
            shell.RequestSave();
            await shell.PumpUntilAsync(() => shell.Document?.Title.Contains("existing.txt") == true &&
                shell.Document.IsModified == false);
            Assert.Equal("new text", await File.ReadAllTextAsync(target));
        }
    }

    /// <summary>Typing at a full page end must advance the visible caret, not reverse later inserts.</summary>
    [Fact]
    public async Task Controller_hundred_inserts_at_full_page_end_preserve_global_order()
    {
        using var temp = new RepoTemp();
        var path = temp.File("page-end.txt");
        var prefix = new string('a', NativeEditorController.PageSize);
        await File.WriteAllTextAsync(path, prefix);
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.TotalLength == prefix.Length);
        var inserted = new string(Enumerable.Range(0, 100).Select(i => (char)('A' + i % 26)).ToArray());
        var watch = Stopwatch.StartNew();
        foreach (var ch in inserted) shell.Edit(shell.Document!.Text + ch);
        watch.Stop();
        Assert.Equal(prefix.Length + inserted.Length, shell.Document!.TotalLength);
        Assert.EndsWith(inserted, shell.Document.Text, StringComparison.Ordinal);
        shell.RequestSave();
        await shell.PumpUntilAsync(() => shell.Document?.IsModified == false);
        Assert.Equal(prefix + inserted, await File.ReadAllTextAsync(path));
        Assert.True(watch.Elapsed < TimeSpan.FromMinutes(1), "Typing smoke exceeded a minute; no strict CI latency target is asserted.");
    }

    /// <summary>Crossing page slack rebases the viewport without losing a large paste or caret.</summary>
    [Fact]
    public async Task Controller_rebases_after_page_slack_exhaustion_and_keeps_edit_order()
    {
        using var temp = new RepoTemp();
        var path = temp.File("page-rebase.txt");
        var prefix = new string('a', NativeEditorController.PageSize);
        await File.WriteAllTextAsync(path, prefix);
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.TotalLength == prefix.Length);
        var paste = new string('Z', NativeEditorController.PageSlack + 1024);
        shell.Edit(shell.Document!.Text + paste);
        Assert.True(shell.Document!.PageStart > 0, "Exceeding page slack should rebase the viewport.");
        Assert.InRange(shell.Document.FocusDisplayOffset ?? -1, 0, shell.Document.Text.Length);
        var tail = new string(Enumerable.Range(0, 100).Select(i => (char)('A' + i % 26)).ToArray());
        foreach (var ch in tail) shell.Edit(shell.Document!.Text + ch);
        Assert.EndsWith(paste + tail, shell.Document!.Text, StringComparison.Ordinal);
        shell.RequestSave();
        await shell.PumpUntilAsync(() => shell.Document?.IsModified == false);
        Assert.Equal(prefix + paste + tail, await File.ReadAllTextAsync(path));
    }

    /// <summary>Select All and Copy include off-page text with its original line-ending spelling.</summary>
    [Fact]
    public async Task Controller_select_all_copy_streams_entire_source_across_pages()
    {
        using var temp = new RepoTemp();
        var path = temp.File("global-copy.txt");
        var source = new string('a', NativeEditorController.PageSize + 42) + "\r\n尾😀\n";
        await File.WriteAllTextAsync(path, source);
        var shell = new FakeShell(NativeLineEndingMode.CrLf);
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.TotalLength == source.Length);
        Assert.True(shell.Document!.Text.Length < source.Length);
        shell.RequestSelectAll();
        shell.RequestCopy();
        await shell.PumpUntilAsync(() => shell.ClipboardText == source);
        Assert.Equal(source, shell.ClipboardText);
    }

    /// <summary>Find Next crosses page/chunk boundaries, wraps, and selection survives paging.</summary>
    [Fact]
    public async Task Controller_find_next_wraps_global_matches_across_pages()
    {
        using var temp = new RepoTemp();
        var path = temp.File("global-find.txt");
        var first = NativeEditorController.PageSize - 3;
        var second = first + "needle".Length + 40;
        var source = new string('x', first) + "needle" + new string('y', 40) + "needle";
        await File.WriteAllTextAsync(path, source);
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { SearchQuery = "needle" };
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.TotalLength == source.Length);
        shell.RequestFind();
        await shell.PumpUntilAsync(() => shell.DisplaySelection is not null && GlobalSelectionStart(shell) == first);
        Assert.Equal(first, GlobalSelectionStart(shell));
        shell.RequestFindNext();
        await shell.PumpUntilAsync(() => shell.DisplaySelection is not null && GlobalSelectionStart(shell) == second);
        Assert.Equal(second, GlobalSelectionStart(shell));
        shell.RequestPreviousPage();
        shell.RequestCopy();
        await shell.PumpUntilAsync(() => shell.ClipboardText == "needle");
        Assert.Equal("needle", shell.ClipboardText);
        shell.RequestFindNext();
        await shell.PumpUntilAsync(() => shell.DisplaySelection is not null && GlobalSelectionStart(shell) == first);
        Assert.Equal(first, GlobalSelectionStart(shell));
    }

    /// <summary>Go To Line relocates the viewport to a line outside the current page.</summary>
    [Fact]
    public async Task Controller_go_to_line_uses_global_engine_offset()
    {
        using var temp = new RepoTemp();
        var path = temp.File("global-line.txt");
        var source = string.Concat(Enumerable.Repeat("line\n", 30_000));
        await File.WriteAllTextAsync(path, source);
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { LinePrompt = 20_000 };
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.TotalLength == source.Length);
        shell.RequestGoToLine();
        await shell.PumpUntilAsync(() => shell.DisplaySelection is not null &&
            GlobalCaret(shell) == (20_000 - 1) * "line\n".Length);
        Assert.True(shell.Document!.PageStart > 0);
        Assert.Equal((20_000 - 1) * "line\n".Length, GlobalCaret(shell));
    }

    /// <summary>User selection in CRLF display positions copies the original mixed-newline source.</summary>
    [Fact]
    public async Task Controller_maps_native_crlf_selection_back_to_source()
    {
        using var temp = new RepoTemp();
        var path = temp.File("mixed.txt");
        await File.WriteAllTextAsync(path, "A\nB\rC\r\nD");
        var shell = new FakeShell(NativeLineEndingMode.CrLf);
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.Title.Contains("mixed.txt") == true);
        Assert.Equal("A\r\nB\r\nC\r\nD", shell.Document!.Text);
        shell.ChangeSelection(1, 9);
        shell.RequestCopy();
        await shell.PumpUntilAsync(() => shell.ClipboardText == "\nB\rC\r\n");
        Assert.Equal("\nB\rC\r\n", shell.ClipboardText);
    }

    /// <summary>Global Cut removes the whole off-page selection only after clipboard preparation.</summary>
    [Fact]
    public async Task Controller_select_all_cut_removes_entire_large_document()
    {
        using var temp = new RepoTemp();
        var path = temp.File("global-cut.txt");
        var source = new string('a', NativeEditorController.PageSize + 100) + "\r\nlast";
        await File.WriteAllTextAsync(path, source);
        var shell = new FakeShell(NativeLineEndingMode.CrLf);
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.TotalLength == source.Length);
        shell.RequestSelectAll();
        shell.RequestCut();
        await shell.PumpUntilAsync(() => shell.ClipboardText == source && shell.Document?.TotalLength == 0);
        Assert.True(shell.Document!.IsModified);
        shell.RequestSave();
        await shell.PumpUntilAsync(() => shell.Document?.IsModified == false);
        Assert.Equal("", await File.ReadAllTextAsync(path));
    }

    /// <summary>A rejected OS clipboard must never turn Copy or Cut into silent data loss.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Controller_clipboard_failure_preserves_document_and_reports_error(bool cut)
    {
        using var temp = new RepoTemp();
        var path = temp.File("clipboard-failure.txt");
        const string source = "one\r\ntwo😀\nthree";
        await File.WriteAllTextAsync(path, source);
        var shell = new FakeShell(NativeLineEndingMode.CrLf) { RejectClipboard = true };
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.TotalLength == source.Length);
        shell.RequestSelectAll();
        if (cut) shell.RequestCut();
        else shell.RequestCopy();
        await shell.PumpUntilAsync(() => shell.Errors.Count > 0);
        Assert.Contains("clipboard", shell.Errors[0], StringComparison.OrdinalIgnoreCase);
        Assert.Null(shell.ClipboardText);
        Assert.Equal(source.Length, shell.Document!.TotalLength);
        Assert.False(shell.Document.IsModified);
        Assert.Equal(source, await File.ReadAllTextAsync(path));
    }

    /// <summary>A delayed old Copy completion cannot overwrite or undo a newer Cut operation.</summary>
    [Fact]
    public async Task Controller_late_copy_completion_does_not_overwrite_newer_cut()
    {
        using var temp = new RepoTemp();
        var path = temp.File("copy-cut-order.txt");
        await File.WriteAllTextAsync(path, "ABCD");
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.Text == "ABCD" &&
            shell.Analysis?.Status.Contains("v0") == true);
        shell.ChangeSelection(0, 1);
        shell.RequestCopy();
        shell.ChangeSelection(1, 2);
        shell.RequestCut();
        await shell.WaitForPostedCountAsync(2);
        shell.PumpReverse();
        Assert.Equal("B", shell.ClipboardText);
        Assert.Equal("ACD", shell.Document!.Text);
        Assert.True(shell.Document.IsModified);
    }

    /// <summary>A late search result must not yank the caret after a newer manual selection.</summary>
    [Fact]
    public async Task Controller_late_find_completion_preserves_newer_user_selection()
    {
        using var temp = new RepoTemp();
        var path = temp.File("find-race.txt");
        await File.WriteAllTextAsync(path, "xx needle xx");
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { SearchQuery = "needle" };
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.Text == "xx needle xx" &&
            shell.Analysis?.Status.Contains("v0") == true);
        shell.RequestFind();
        await shell.WaitForPostedAsync();
        shell.ChangeSelection(1, 1);
        shell.Pump();
        Assert.Equal(1, GlobalCaret(shell));
        Assert.Equal(1, GlobalSelectionStart(shell));
    }

    /// <summary>Typing over a global selection replaces off-page text, not just visible page text.</summary>
    [Fact]
    public async Task Controller_typing_over_select_all_replaces_entire_large_document()
    {
        using var temp = new RepoTemp();
        var path = temp.File("global-replace.txt");
        await File.WriteAllTextAsync(path, new string('a', NativeEditorController.PageSize + 100));
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.TotalLength == NativeEditorController.PageSize + 100);
        shell.RequestSelectAll();
        shell.Edit("Z");
        Assert.Equal("Z", shell.Document!.Text);
        Assert.Equal(1, shell.Document.TotalLength);
        shell.RequestSave();
        await shell.PumpUntilAsync(() => shell.Document?.IsModified == false);
        Assert.Equal("Z", await File.ReadAllTextAsync(path));
    }

    /// <summary>Large plain text remains editable while its session reports complete coverage.</summary>
    [Fact]
    public async Task Controller_large_plain_session_reports_complete_and_keeps_editable_page()
    {
        using var temp = new RepoTemp();
        var path = temp.File("large.txt");
        await File.WriteAllTextAsync(path, new string('a', 2 * 1024 * 1024 + 1));
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.TotalLength == 2 * 1024 * 1024 + 1);
        await shell.PumpUntilAsync(() => shell.Analysis?.Status.Contains("Complete") == true &&
            shell.Analysis.Status.Contains("v0"));
        Assert.Equal("No diagnostics.", shell.Analysis!.DiagnosticsSummary);
        shell.Edit("Z" + shell.Document!.Text[1..]);
        await shell.PumpUntilAsync(() => shell.Analysis?.Status.Contains("Complete") == true &&
            shell.Analysis.Status.Contains("v1"));
        Assert.True(shell.Document!.IsModified);
        Assert.StartsWith("Z", shell.Document.Text, StringComparison.Ordinal);
    }

    /// <summary>Large Markdown visible analysis must not claim complete global diagnostics.</summary>
    [Fact]
    public async Task Controller_large_markdown_session_labels_visible_result_provisional()
    {
        using var temp = new RepoTemp();
        var path = temp.File("large.md");
        await File.WriteAllTextAsync(path, new string('x', 5 * 1024 * 1024));
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.TotalLength == 5 * 1024 * 1024);
        await shell.PumpUntilAsync(() => shell.Analysis?.Status.Contains("Provisional") == true);
        Assert.Contains("global diagnostics unknown", shell.Analysis!.DiagnosticsSummary,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Complete", shell.Analysis.Status, StringComparison.Ordinal);
    }

    /// <summary>CSV session counts an off-page ragged row while showing a bounded editable page.</summary>
    [Fact]
    public async Task Controller_large_csv_session_shows_exact_global_diagnostic_count()
    {
        using var temp = new RepoTemp();
        var path = temp.File("large.csv");
        var source = "a,b\n" + string.Concat(Enumerable.Repeat("1,2\n", 550_000)) + "bad\n";
        await File.WriteAllTextAsync(path, source);
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.TotalLength == source.Length);
        Assert.True(shell.Document!.Text.Length < source.Length);
        await shell.PumpUntilAsync(() => shell.Analysis?.Status.Contains("Complete") == true &&
            shell.Analysis.Status.Contains("CSV"));
        Assert.Contains("1 document diagnostics", shell.Analysis!.DiagnosticsSummary,
            StringComparison.Ordinal);
        Assert.Contains("none in displayed viewport", shell.Analysis.DiagnosticsSummary,
            StringComparison.Ordinal);
    }

    /// <summary>Rapid CSV correction publishes only current version, not stale ragged diagnostics.</summary>
    [Fact]
    public async Task Controller_csv_session_discards_superseded_analysis()
    {
        using var temp = new RepoTemp();
        var path = temp.File("ragged.csv");
        await File.WriteAllTextAsync(path, "a,b\n1\n");
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.Title.Contains("ragged.csv") == true);
        shell.Edit("a,b\n1,2\n");
        var analysesAfterEdit = shell.Analyses.Count;
        await shell.PumpUntilAsync(() => shell.Analysis?.Status.Contains("CSV") == true &&
            shell.Analysis.Status.Contains("v1") && shell.Analysis.DiagnosticsSummary == "No diagnostics.");
        await Task.Delay(150);
        shell.Pump();
        Assert.DoesNotContain(shell.Analyses.Skip(analysesAfterEdit), a => a.Status.Contains("CSV") &&
            a.Status.Contains("v0") && a.DiagnosticsSummary.Contains("diagnostics", StringComparison.Ordinal) &&
            a.DiagnosticsSummary != "No diagnostics.");
    }

    /// <summary>Idle YAML Full analysis promotes exact off-page diagnostics without blocking its visible page.</summary>
    [Fact]
    public async Task Controller_idle_yaml_full_pass_promotes_exact_offscreen_diagnostic()
    {
        using var temp = new RepoTemp();
        var path = temp.File("idle.yaml");
        var source = "0xB: a\n" + string.Concat(Enumerable.Repeat("# padding\n", 220_000)) + "11: b\n";
        await File.WriteAllTextAsync(path, source);
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.TotalLength == source.Length);
        await shell.PumpUntilAsync(() => shell.Analysis?.Status.Contains("YAML · Provisional · v0") == true);
        Assert.True(shell.Document!.Text.Length < source.Length);
        await shell.PumpUntilAsync(() => shell.Analysis?.Status.Contains("YAML · Complete · v0") == true);
        Assert.Contains("1 document diagnostics", shell.Analysis!.DiagnosticsSummary,
            StringComparison.Ordinal);
        Assert.Contains("none in displayed viewport", shell.Analysis.DiagnosticsSummary,
            StringComparison.Ordinal);
    }

    /// <summary>An uncertifiable idle TOML Full pass keeps the visible presentation intact.</summary>
    [Fact]
    public async Task Controller_idle_toml_provisional_full_pass_preserves_visible_facts()
    {
        using var temp = new RepoTemp();
        var path = temp.File("idle.toml");
        var value = new string('x', 100);
        var source = "title = 'start'\n" + string.Concat(Enumerable.Range(0, 40_000)
            .Select(index => $"k{index:D5} = '{value}'\n")) +
            "[[items]]\nname = 'x'\n[[items.child]]\ny = 1\n";
        await File.WriteAllTextAsync(path, source);
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.TotalLength == source.Length);
        await shell.PumpUntilAsync(() => shell.Analysis?.Status.Contains("TOML · Provisional · v0") == true);
        var visible = shell.Analysis!;
        await shell.PumpUntilAsync(() => shell.Analysis?.Status.Contains("Full pass Provisional") == true);
        Assert.Contains("global diagnostics unknown", shell.Analysis!.Status,
            StringComparison.Ordinal);
        Assert.Equal(visible.DiagnosticsSummary, shell.Analysis.DiagnosticsSummary);
        Assert.Equal(visible.PreviewText, shell.Analysis.PreviewText);
        Assert.Equal(visible.Tokens, shell.Analysis.Tokens);
    }

    /// <summary>Builds a controller with project-local, side-effect-free configuration.</summary>
    private static NativeEditorController NewController(FakeShell shell, string userHome, string? path)
    {
        var config = MoteConfigLoader.Load(new MoteConfigLoadOptions
        {
            UserHomeDirectory = userHome,
            UseEnvironmentOverride = false
        });
        return new NativeEditorController(shell, config, ThemePolicies.Get(config.ThemeId), path);
    }

    /// <summary>Gets the global start of a visible selection in Preserve mode.</summary>
    private static int GlobalSelectionStart(FakeShell shell) => shell.Document!.PageStart +
        Math.Min(shell.DisplaySelection!.Value.Anchor, shell.DisplaySelection.Value.Active);

    /// <summary>Gets the global active caret position in Preserve mode.</summary>
    private static int GlobalCaret(FakeShell shell) => shell.Document!.PageStart +
        shell.DisplaySelection!.Value.Active;

    /// <summary>Rejects a boundary inside one surrogate pair.</summary>
    private static bool SplitsPair(string text, int index) => index > 0 && index < text.Length &&
        char.IsHighSurrogate(text[index - 1]) && char.IsLowSurrogate(text[index]);

    /// <summary>Rejects a boundary inside one projected CRLF delimiter.</summary>
    private static bool SplitsCrLf(string text, int index) => index > 0 && index < text.Length &&
        text[index - 1] == '\r' && text[index] == '\n';

    /// <summary>Shows control characters in a failing randomized case.</summary>
    private static string Escape(string value) => value.Replace("\r", "\\r").Replace("\n", "\\n");

    /// <summary>A deterministic event queue standing in for the native UI dispatcher.</summary>
    private sealed class FakeShell(NativeLineEndingMode lineEndingMode) : INativeEditorShell
    {
        private readonly ConcurrentQueue<Action> _posted = new();

        /// <inheritdoc />
        public NativeLineEndingMode LineEndingMode { get; } = lineEndingMode;
        /// <inheritdoc />
        public bool PrefersDark => true;
        /// <inheritdoc />
        public event Action<string>? TextChanged;
        /// <inheritdoc />
        public event Action<int, int>? SelectionChanged;
        /// <inheritdoc />
        public event Action? NewRequested;
        /// <inheritdoc />
        public event Action? OpenRequested;
        /// <inheritdoc />
        public event Action? SaveRequested;
        /// <inheritdoc />
        public event Action? SaveAsRequested;
        /// <inheritdoc />
        public event Action? UndoRequested;
        /// <inheritdoc />
        public event Action? RedoRequested;
        /// <inheritdoc />
        public event Action? FormatRequested;
        /// <inheritdoc />
        public event Action? PagePreviousRequested;
        /// <inheritdoc />
        public event Action? PageNextRequested;
        /// <inheritdoc />
        public event Action? FindRequested;
        /// <inheritdoc />
        public event Action? FindNextRequested;
        /// <inheritdoc />
        public event Action? GoToLineRequested;
        /// <inheritdoc />
        public event Action? SelectAllRequested;
        /// <inheritdoc />
        public event Action? CopyRequested;
        /// <inheritdoc />
        public event Action? CutRequested;
        /// <inheritdoc />
        public event EventHandler<NativeClosingEventArgs>? ClosingRequested;
        /// <inheritdoc />
        public event Action? Shown;

        /// <summary>The current fake text viewport.</summary>
        public NativeDocumentView? Document { get; private set; }
        /// <summary>The current fake semantic presentation.</summary>
        public NativeAnalysisView? Analysis { get; private set; }
        /// <summary>All presentations, for stale-result assertions.</summary>
        public List<NativeAnalysisView> Analyses { get; } = [];
        /// <summary>All recoverable UI errors.</summary>
        public List<string> Errors { get; } = [];
        /// <summary>The applied compile-time theme.</summary>
        public IThemePolicy? Theme { get; private set; }
        /// <summary>Next path returned by the Open dialog.</summary>
        public string? OpenPath { get; set; }
        /// <summary>Next path returned by the Save As dialog.</summary>
        public string? SavePath { get; set; }
        /// <summary>Decision returned by the next existing-target overwrite prompt.</summary>
        public bool OverwriteApproved { get; set; } = true;
        /// <summary>Number of explicit overwrite prompts shown.</summary>
        public int OverwritePromptCount { get; private set; }
        /// <summary>Next search term returned to the controller.</summary>
        public string? SearchQuery { get; set; }
        /// <summary>Next one-based line number returned to the controller.</summary>
        public int? LinePrompt { get; set; }
        /// <summary>Last projected visible selection from the controller.</summary>
        public NativeProjectedSelection? DisplaySelection { get; private set; }
        /// <summary>Last exact string sent to the fake clipboard.</summary>
        public string? ClipboardText { get; private set; }
        /// <summary>Simulates an OS clipboard service refusing a write.</summary>
        public bool RejectClipboard { get; set; }
        /// <summary>Native IME text not yet represented in the engine snapshot.</summary>
        public string? PendingText { get; set; }
        /// <summary>Models an input method that cannot finish composition for the command.</summary>
        public bool VetoPendingCommit { get; set; }
        /// <summary>Text observed immediately after synchronous IME commit.</summary>
        public List<string> CommittedTexts { get; } = [];
        /// <summary>Counts synchronous settle attempts at the native boundary.</summary>
        public int CommitCalls { get; private set; }

        /// <inheritdoc />
        public void Run() => Shown?.Invoke();
        /// <inheritdoc />
        public void SetDocument(NativeDocumentView view) => Document = view;
        /// <inheritdoc />
        public bool CommitPendingText()
        {
            CommitCalls++;
            if (VetoPendingCommit) return false;
            if (PendingText is not { } pending) return true;
            PendingText = null;
            Edit((Document?.Text ?? "") + pending);
            if (Document is { } view) CommittedTexts.Add(view.Text);
            return true;
        }
        /// <inheritdoc />
        public void SetAnalysis(NativeAnalysisView view) { Analysis = view; Analyses.Add(view); }
        /// <inheritdoc />
        public void SetTheme(IThemePolicy theme) => Theme = theme;
        /// <inheritdoc />
        public void SetSelection(int displayAnchor, int displayActive) =>
            DisplaySelection = new NativeProjectedSelection(displayAnchor, displayActive);
        /// <inheritdoc />
        public string? PromptFind() => SearchQuery;
        /// <inheritdoc />
        public int? PromptGoToLine() => LinePrompt;
        /// <inheritdoc />
        public void SetClipboardText(string text)
        {
            if (RejectClipboard) throw new InvalidOperationException("Clipboard unavailable.");
            ClipboardText = text;
        }
        /// <inheritdoc />
        public string? PickOpenFile() => OpenPath;
        /// <inheritdoc />
        public string? PickSaveFile(string? currentPath) => SavePath ?? currentPath;
        /// <inheritdoc />
        public bool ConfirmOverwrite(string path)
        {
            OverwritePromptCount++;
            return OverwriteApproved;
        }
        /// <inheritdoc />
        public bool ConfirmDiscard() => true;
        /// <inheritdoc />
        public void ShowError(string message) => Errors.Add(message);
        /// <inheritdoc />
        public void Post(Action action) => _posted.Enqueue(action);
        /// <inheritdoc />
        public void Close() { }

        /// <summary>Raises one native text-edit notification.</summary>
        public void Edit(string text) => TextChanged?.Invoke(text);
        /// <summary>Raises the native Save command.</summary>
        public void RequestSave() => SaveRequested?.Invoke();
        /// <summary>Raises the native Save As command.</summary>
        public void RequestSaveAs() => SaveAsRequested?.Invoke();
        /// <summary>Raises the native Open command.</summary>
        public void RequestOpen() => OpenRequested?.Invoke();
        /// <summary>Raises the native Undo command.</summary>
        public void RequestUndo() => UndoRequested?.Invoke();
        /// <summary>Raises the native Redo command.</summary>
        public void RequestRedo() => RedoRequested?.Invoke();
        /// <summary>Raises native next-page navigation.</summary>
        public void RequestNextPage() => PageNextRequested?.Invoke();
        /// <summary>Raises the native New command.</summary>
        public void RequestNew() => NewRequested?.Invoke();
        /// <summary>Raises the native Format command.</summary>
        public void RequestFormat() => FormatRequested?.Invoke();
        /// <summary>Raises previous-page navigation.</summary>
        public void RequestPreviousPage() => PagePreviousRequested?.Invoke();
        /// <summary>Raises a user selection change in current-page display coordinates.</summary>
        public void ChangeSelection(int anchor, int active)
        {
            DisplaySelection = new NativeProjectedSelection(anchor, active);
            SelectionChanged?.Invoke(anchor, active);
        }
        /// <summary>Raises a new global search.</summary>
        public void RequestFind() => FindRequested?.Invoke();
        /// <summary>Repeats the last global search.</summary>
        public void RequestFindNext() => FindNextRequested?.Invoke();
        /// <summary>Raises a one-based global line navigation request.</summary>
        public void RequestGoToLine() => GoToLineRequested?.Invoke();
        /// <summary>Raises global Select All.</summary>
        public void RequestSelectAll() => SelectAllRequested?.Invoke();
        /// <summary>Raises global Copy.</summary>
        public void RequestCopy() => CopyRequested?.Invoke();
        /// <summary>Raises global Cut.</summary>
        public void RequestCut() => CutRequested?.Invoke();
        /// <summary>Asks the controller whether closing is permitted.</summary>
        public bool RequestClose()
        {
            var args = new NativeClosingEventArgs();
            ClosingRequested?.Invoke(this, args);
            return !args.Cancel;
        }

        /// <summary>Executes posted callbacks until the queue is empty.</summary>
        public void Pump()
        {
            while (_posted.TryDequeue(out var action)) action();
        }

        /// <summary>Executes exactly one queued UI callback.</summary>
        public void PumpOne()
        {
            Assert.True(_posted.TryDequeue(out var action), "Expected one queued UI callback.");
            action();
        }

        /// <summary>Waits for a background completion without draining it.</summary>
        public async Task WaitForPostedAsync()
        {
            var stopwatch = Stopwatch.StartNew();
            while (_posted.IsEmpty && stopwatch.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(10);
            Assert.False(_posted.IsEmpty, "Background completion was not posted.");
        }

        /// <summary>Waits until several asynchronous operations can be delivered out of order.</summary>
        public async Task WaitForPostedCountAsync(int count)
        {
            var stopwatch = Stopwatch.StartNew();
            while (_posted.Count < count && stopwatch.Elapsed < TimeSpan.FromSeconds(10))
                await Task.Delay(10);
            Assert.True(_posted.Count >= count, $"Expected {count} queued UI callbacks.");
        }

        /// <summary>Delivers queued callbacks in reverse completion order to expose stale-write races.</summary>
        public void PumpReverse()
        {
            var actions = new List<Action>();
            while (_posted.TryDequeue(out var action)) actions.Add(action);
            for (var index = actions.Count - 1; index >= 0; index--) actions[index]();
        }

        /// <summary>Waits for background work by draining only the fake UI queue.</summary>
        public async Task PumpUntilAsync(Func<bool> condition)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < TimeSpan.FromSeconds(10))
            {
                Pump();
                if (condition()) return;
                await Task.Delay(10);
            }
            Pump();
            Assert.True(condition(), "Timed out waiting for a native-controller state transition.");
        }
    }
}
