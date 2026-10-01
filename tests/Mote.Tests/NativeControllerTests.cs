using System.Collections.Concurrent;
using System.Diagnostics;
using Mote.Configuration;
using Mote.Engine;
using Mote.Formats;
using Mote.Native;
using Mote.Native.Accessibility;
using Mote.Native.Viewport;
using Mote.Themes;

namespace Mote.Tests;

/// <summary>UI-independent tests of source/native projection and editor orchestration.</summary>
public sealed partial class NativeControllerTests
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
            "[[items]]\nname = 'x'\n[[items.child]]\ny = 1\n[[items]]\nname = 'later'\n";
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

    /// <summary>Preview activation moves a global caret without committing IME text or changing source.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Controller_preview_navigation_is_versioned_and_nonmutating(bool canvas)
    {
        using var temp = new RepoTemp();
        var path = temp.File("preview-navigation.md");
        const string source = "# Top\n\n## Destination\n";
        await File.WriteAllTextAsync(path, source);
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = canvas };
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Analysis?.PreviewText.Contains("Destination") == true);
        var view = shell.Analysis!;
        var destination = view.PreviewText.IndexOf("Destination", StringComparison.Ordinal);
        var expected = source.IndexOf("## Destination", StringComparison.Ordinal);
        Assert.True(destination >= 0 && expected >= 0);
        var beforeVersion = view.Stamp.Version;
        var beforeCommitCalls = shell.CommitCalls;

        shell.ActivatePreview(destination, view.Stamp with { Version = view.Stamp.Version + 1 });
        Assert.Equal(0, shell.FocusSourceCount);
        shell.IsTextComposing = true;
        shell.ActivatePreview(destination);
        Assert.Equal(0, shell.FocusSourceCount);
        shell.IsTextComposing = false;
        shell.ActivatePreview(destination);

        Assert.Equal(1, shell.FocusSourceCount);
        Assert.Equal(beforeCommitCalls, shell.CommitCalls);
        Assert.Equal(beforeVersion, shell.Analysis!.Stamp.Version);
        Assert.Equal(source, await File.ReadAllTextAsync(path));
        if (canvas)
            Assert.Equal(expected, shell.CanvasFrame!.SelectionActive);
        else
            Assert.Equal(expected, shell.DisplaySelection!.Value.Active + shell.Document!.PageStart);
    }

    /// <summary>A bounded preview item can reveal an off-page source location in both editor profiles.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Controller_preview_navigation_reveals_global_off_page_source(bool canvas)
    {
        using var temp = new RepoTemp();
        var path = temp.File("preview-far.toml");
        var source = "#" + new string('x', 80_000) + "\nneedle = 1\n";
        await File.WriteAllTextAsync(path, source);
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = canvas };
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Analysis?.Status.Contains("TOML · Complete") == true);
        var view = shell.Analysis!;
        Assert.Contains("needle", view.PreviewText, StringComparison.Ordinal);
        var expected = source.IndexOf("needle", StringComparison.Ordinal);
        Assert.True(expected > NativeEditorController.PageSize);

        shell.ActivatePreview(view.PreviewText.IndexOf("needle", StringComparison.Ordinal));

        Assert.Equal(1, shell.FocusSourceCount);
        Assert.Equal(source, await File.ReadAllTextAsync(path));
        if (canvas)
        {
            Assert.Equal(expected, shell.CanvasFrame!.SelectionActive);
            Assert.Contains(shell.CanvasFrame.Slices,
                slice => expected >= slice.SourceStart &&
                    expected <= slice.SourceStart + slice.SourceLength);
        }
        else
        {
            Assert.True(shell.Document!.PageStart > 0);
            Assert.Equal(expected,
                shell.Document.PageStart + shell.DisplaySelection!.Value.Active);
        }
    }

    /// <summary>Visible rows beyond a long-line gap never expand the analysis bridge into that gap.</summary>
    [Fact]
    public async Task Canvas_controller_bounds_bridge_even_when_visible_slices_span_huge_gap()
    {
        using var temp = new RepoTemp();
        var path = temp.File("canvas-gap.txt");
        var source = "first\n" + new string('x', 8 * 1024 * 1024) + "\nlast";
        await File.WriteAllTextAsync(path, source);
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = true };
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.CanvasBinding?.Snapshot.Length == source.Length);

        var binding = shell.CanvasBinding!;
        Assert.Equal(source.Length, binding.Snapshot.Length);
        Assert.Contains(binding.Frame.Slices, slice => slice.SourceStart > 8 * 1024 * 1024);
        Assert.InRange(binding.InputSourceText.Length, 0, CanvasInputWindowSelector.MaxLength);
        Assert.NotNull(shell.CanvasSemantics);
        Assert.InRange(shell.CanvasSemantics!.Coverage.Length, 0, NativeEditorController.PageSize);
        Assert.True(shell.CanvasSemantics.Coverage.Length < source.Length / 16);

        shell.ScrollCanvas(18); // Warm the callback before checking bounded copy work.
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        shell.ScrollCanvas(18);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.InRange(allocated, 0, 2 * 1024 * 1024);
        Assert.Equal(source.Length, shell.CanvasBinding!.Snapshot.Length);
        Assert.DoesNotContain("discrete", shell.CanvasBinding.Status, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A platform's 8 Ki binding request leaves one-character edits below its 16 Ki native hard limit.</summary>
    [Theory]
    [InlineData(8192)]
    [InlineData(16383)]
    [InlineData(16384)]
    public async Task Canvas_controller_honors_smaller_native_input_binding_limit(int sourceLength)
    {
        using var temp = new RepoTemp();
        var path = temp.File("bounded-input.txt");
        var source = new string('x', sourceLength);
        await File.WriteAllTextAsync(path, source);
        var shell = new FakeShell(NativeLineEndingMode.Preserve)
        {
            CanvasEnabled = true,
            MaxCanvasInputLength = 8192
        };
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.CanvasBinding?.Snapshot.Length == sourceLength);

        var before = shell.CanvasBinding!;
        Assert.InRange(before.InputSourceText.Length, 1, 8192);
        Assert.True(before.InputSourceText.Length + 1 <= 16384);
        shell.CommitCanvasEdit(new CanvasCommittedEdit(before.DocumentGeneration,
            before.BaseVersion, before.BindingNonce, new TextChange(0, 0, "X"), 1));

        var after = shell.CanvasBinding!;
        Assert.Equal(sourceLength + 1, after.Snapshot.Length);
        Assert.Equal("X", after.Snapshot.GetText(0, 1));
        Assert.InRange(after.InputSourceText.Length, 1, 8192);
        Assert.Equal(source, await File.ReadAllTextAsync(path));
        Assert.Empty(shell.Errors);
        shell.RequestUndo();
        Assert.Equal(source, shell.CanvasBinding!.Snapshot.GetText());
    }

    /// <summary>Replacing repeated text uses the exact OS source transaction, not a guessed diff.</summary>
    [Fact]
    public async Task Canvas_selected_repeated_text_is_replaced_exactly_once()
    {
        using var temp = new RepoTemp();
        var path = temp.File("canvas-selection.txt");
        await File.WriteAllTextAsync(path, "abc");
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = true };
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.CanvasBinding?.Snapshot.GetText() == "abc");
        shell.SelectCanvas(0, 2);
        var binding = shell.CanvasBinding!;
        shell.CommitCanvasEdit(new CanvasCommittedEdit(binding.DocumentGeneration,
            binding.BaseVersion, binding.BindingNonce, new TextChange(0, 2, "a"), 1));

        Assert.Equal("ac", shell.CanvasBinding!.Snapshot.GetText());
        Assert.Equal(1, shell.CanvasFrame!.SelectionActive);
        Assert.Empty(shell.Errors);
    }

    /// <summary>A ribbon-only physical height publishes no source rows, then restores them.</summary>
    [Fact]
    public async Task Canvas_zero_body_resize_keeps_global_selection_and_document_version()
    {
        using var temp = new RepoTemp();
        var path = temp.File("tiny-canvas.txt");
        await File.WriteAllTextAsync(path, "alpha\nbeta\n");
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = true };
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.CanvasBinding?.Snapshot.GetText() == "alpha\nbeta\n");
        shell.SelectCanvas(1, 7);
        var before = shell.CanvasFrame!;
        Assert.NotEmpty(before.Slices);

        shell.ResizeCanvas(0);
        var hidden = shell.CanvasFrame!;
        Assert.Empty(hidden.Slices);
        Assert.Empty(hidden.RowWindows);
        Assert.Equal(before.Version, hidden.Version);
        Assert.Equal((before.SelectionAnchor, before.SelectionActive),
            (hidden.SelectionAnchor, hidden.SelectionActive));
        Assert.Equal("alpha\nbeta\n", shell.CanvasBinding!.Snapshot.GetText());

        shell.ResizeCanvas(100);
        var restored = shell.CanvasFrame!;
        Assert.NotEmpty(restored.Slices);
        Assert.Equal(before.Version, restored.Version);
        Assert.Equal((before.SelectionAnchor, before.SelectionActive),
            (restored.SelectionAnchor, restored.SelectionActive));
        Assert.Empty(shell.Errors);
    }

    /// <summary>A stale OS deletion disagreeing with global selection must not mutate source.</summary>
    [Fact]
    public async Task Canvas_selection_mismatch_rejects_and_rebinds_without_mutation()
    {
        using var temp = new RepoTemp();
        var path = temp.File("canvas-stale-selection.txt");
        await File.WriteAllTextAsync(path, "abc");
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = true };
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.CanvasBinding?.Snapshot.GetText() == "abc");
        shell.SelectCanvas(0, 2);
        var before = shell.CanvasBinding!;
        shell.CommitCanvasEdit(new CanvasCommittedEdit(before.DocumentGeneration,
            before.BaseVersion, before.BindingNonce, new TextChange(0, 1, "a"), 1));

        Assert.Equal("abc", shell.CanvasBinding!.Snapshot.GetText());
        Assert.Equal(before.BaseVersion, shell.CanvasBinding.BaseVersion);
        Assert.NotEqual(before.BindingNonce, shell.CanvasBinding.BindingNonce);
        Assert.Contains(shell.Errors, error => error.Contains("mapped", StringComparison.Ordinal));
    }

    /// <summary>An OS-emitted global Delete works even when the active native host has empty text.</summary>
    [Theory]
    [InlineData("a\n\n", 2, false)]
    [InlineData("a\n\n", 2, true)]
    [InlineData("a\r\n\r\n", 3, false)]
    [InlineData("a\r\n\r\n", 3, true)]
    public async Task Canvas_global_delete_replaces_empty_host_selection_in_both_directions(
        string source, int count, bool reverse)
    {
        using var temp = new RepoTemp();
        var path = temp.File("canvas-empty-host-delete.txt");
        await File.WriteAllTextAsync(path, source);
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = true };
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.CanvasBinding?.Snapshot.GetText() == source);
        shell.SelectCanvas(reverse ? count : 0, reverse ? 0 : count);
        var binding = shell.CanvasBinding!;
        shell.CommitCanvasEdit(new CanvasCommittedEdit(binding.DocumentGeneration,
            binding.BaseVersion, binding.BindingNonce, new TextChange(0, count, ""), 0));

        Assert.Equal(source[count..], shell.CanvasBinding!.Snapshot.GetText());
        Assert.Equal(0, shell.CanvasFrame!.SelectionActive);
        Assert.Empty(shell.Errors);
    }

    /// <summary>Large canvas edits remain exact and undoable alongside earlier small edits.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Canvas_large_edit_preserves_full_undo_redo_history(
        bool largeDelete)
    {
        using var temp = new RepoTemp();
        var path = temp.File("canvas-undo-budget.txt");
        var original = largeDelete ? new string('q', 17 * 1024 * 1024) : "abc";
        var afterSmallText = "z" + original;
        await File.WriteAllTextAsync(path, original);
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = true };
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.CanvasBinding?.Snapshot.Length == original.Length);

        var first = shell.CanvasBinding!;
        shell.CommitCanvasEdit(new CanvasCommittedEdit(first.DocumentGeneration,
            first.BaseVersion, first.BindingNonce, new TextChange(0, 0, "z"), 1));
        var beforeLarge = shell.CanvasBinding!;
        Assert.Equal(first.BaseVersion + 1, beforeLarge.BaseVersion);
        Assert.Equal(original.Length + 1, beforeLarge.Snapshot.Length);
        Assert.Equal("z", beforeLarge.Snapshot.GetText(0, 1));

        TextChange large;
        int proposedCaret;
        if (largeDelete)
        {
            var count = 16 * 1024 * 1024 + 1;
            shell.SelectCanvas(0, count);
            beforeLarge = shell.CanvasBinding!;
            large = new TextChange(0, count, "");
            proposedCaret = 0;
        }
        else
        {
            var huge = new string('x', 50 * 1024 * 1024);
            large = new TextChange(0, 0, huge);
            proposedCaret = huge.Length;
        }
        shell.CommitCanvasEdit(new CanvasCommittedEdit(beforeLarge.DocumentGeneration,
            beforeLarge.BaseVersion, beforeLarge.BindingNonce, large, proposedCaret));

        var afterLarge = shell.CanvasBinding!;
        Assert.Equal(beforeLarge.BaseVersion + 1, afterLarge.BaseVersion);
        AssertLargeState(afterLarge.Snapshot, largeDelete, original.Length);
        Assert.Empty(shell.Errors);
        Assert.Equal(original, await File.ReadAllTextAsync(path));

        var beforeUndoAllocation = GC.GetAllocatedBytesForCurrentThread();
        shell.RequestUndo();
        var undoAllocation = GC.GetAllocatedBytesForCurrentThread() - beforeUndoAllocation;
        if (!largeDelete)
            Assert.InRange(undoAllocation, 0, 64L * 1024 * 1024);
        var afterLargeUndo = shell.CanvasBinding!;
        Assert.Equal(afterLarge.BaseVersion + 1, afterLargeUndo.BaseVersion);
        AssertSnapshotMatches(afterLargeUndo.Snapshot, afterSmallText);

        shell.RequestUndo();
        var afterSmallUndo = shell.CanvasBinding!;
        Assert.Equal(afterLargeUndo.BaseVersion + 1, afterSmallUndo.BaseVersion);
        AssertSnapshotMatches(afterSmallUndo.Snapshot, original);

        shell.RequestRedo();
        var afterSmallRedo = shell.CanvasBinding!;
        Assert.Equal(afterSmallUndo.BaseVersion + 1, afterSmallRedo.BaseVersion);
        AssertSnapshotMatches(afterSmallRedo.Snapshot, afterSmallText);

        var beforeRedoAllocation = GC.GetAllocatedBytesForCurrentThread();
        shell.RequestRedo();
        var redoAllocation = GC.GetAllocatedBytesForCurrentThread() - beforeRedoAllocation;
        if (!largeDelete)
            Assert.InRange(redoAllocation, 0, 64L * 1024 * 1024);
        var afterLargeRedo = shell.CanvasBinding!;
        Assert.Equal(afterSmallRedo.BaseVersion + 1, afterLargeRedo.BaseVersion);
        AssertLargeState(afterLargeRedo.Snapshot, largeDelete, original.Length);
        Assert.Empty(shell.Errors);
        Assert.Equal(original, await File.ReadAllTextAsync(path));
    }

    /// <summary>Checks exact UTF-16 source units through rope chunks without flattening the snapshot.</summary>
    private static void AssertSnapshotMatches(TextSnapshot snapshot, string expected)
    {
        Assert.Equal(expected.Length, snapshot.Length);
        var offset = 0;
        foreach (var chunk in snapshot.GetChunks())
        {
            Assert.True(chunk.Span.SequenceEqual(expected.AsSpan(offset, chunk.Length)));
            offset += chunk.Length;
        }
        Assert.Equal(expected.Length, offset);
    }

    /// <summary>Checks every code unit without allocating a second giant contiguous string.</summary>
    private static void AssertLargeState(TextSnapshot snapshot, bool largeDelete, int originalLength)
    {
        var expectedLength = largeDelete ? originalLength - 16 * 1024 * 1024 : 50 * 1024 * 1024 + originalLength + 1;
        Assert.Equal(expectedLength, snapshot.Length);
        var offset = 0;
        foreach (var chunk in snapshot.GetChunks())
        {
            var span = chunk.Span;
            if (largeDelete)
            {
                Assert.Equal(-1, span.IndexOfAnyExcept('q'));
            }
            else
            {
                const int insertedLength = 50 * 1024 * 1024;
                var uniformLength = Math.Clamp(insertedLength - offset, 0, span.Length);
                Assert.Equal(-1, span[..uniformLength].IndexOfAnyExcept('x'));
                for (var i = uniformLength; i < span.Length; i++)
                    Assert.Equal("zabc"[offset + i - insertedLength], span[i]);
            }
            offset += span.Length;
        }
        Assert.Equal(expectedLength, offset);
    }

    /// <summary>AX reveal is versioned, synchronous, UI-thread-bound, and must not disturb IME composition.</summary>
    [Fact]
    public async Task Canvas_accessibility_reveal_rejects_stale_thread_and_composition_without_mirroring_text()
    {
        using var temp = new RepoTemp();
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = true };
        using var controller = NewController(shell, temp.Path, null);
        controller.Run();
        var accessible = Assert.IsType<AccessibleDocument>(shell.CanvasAccessibilityDocument);
        var viewport = Assert.IsAssignableFrom<IAccessibleViewport>(shell.CanvasAccessibilityViewport);
        var stale = accessible.MakeRange(0, 0);

        var source = string.Concat(Enumerable.Range(0, 12_000).Select(i => $"line{i:D5}\n"));
        var initial = shell.CanvasBinding!;
        shell.CommitCanvasEdit(new CanvasCommittedEdit(initial.DocumentGeneration,
            initial.BaseVersion, initial.BindingNonce, new TextChange(0, 0, source), source.Length));
        var target = source.IndexOf("line11000", StringComparison.Ordinal);
        var current = accessible.MakeRange(target, target + "line11000".Length);
        shell.CanvasGeometryProbe = ProveVisibleSourceSlice;
        var before = shell.CanvasFrame;

        Assert.Equal(AccessibleRevealResult.StaleRange, viewport.TryReveal(stale, true));
        Assert.Same(before, shell.CanvasFrame);
        shell.IsCanvasComposing = true;
        Assert.Equal(AccessibleRevealResult.CompositionBlocked, viewport.TryReveal(current, true));
        Assert.Same(before, shell.CanvasFrame);
        shell.IsCanvasComposing = false;

        Assert.Equal(AccessibleRevealResult.Revealed, viewport.TryReveal(current, true));
        Assert.Contains(shell.CanvasFrame!.Slices, slice => slice.SourceStart <= target &&
            target < slice.SourceStart + slice.SourceLength);
        Assert.Equal(current.Version, shell.CanvasBinding!.BaseVersion);
        Assert.InRange(shell.CanvasBinding.InputSourceText.Length, 0, 16 * 1024);
        Assert.Equal("line11000", accessible.GetText(current));

        var published = shell.CanvasFrame;
        var offThread = await Task.Run(() => viewport.TryReveal(current, true));
        Assert.Equal(AccessibleRevealResult.WrongThread, offThread);
        Assert.Same(published, shell.CanvasFrame);
    }

    /// <summary>A stale range from a previous document fails even when version numbers happen to match.</summary>
    [Fact]
    public void Canvas_accessibility_reveal_rejects_same_version_from_previous_document()
    {
        using var temp = new RepoTemp();
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = true };
        using var controller = NewController(shell, temp.Path, null);
        controller.Run();
        var accessible = Assert.IsType<AccessibleDocument>(shell.CanvasAccessibilityDocument);
        var viewport = Assert.IsAssignableFrom<IAccessibleViewport>(shell.CanvasAccessibilityViewport);
        var oldRange = accessible.MakeRange(0, 0);

        shell.RequestNew();
        var freshRange = accessible.MakeRange(0, 0);
        shell.CanvasGeometryProbe = ProveVisibleSourceSlice;
        Assert.Equal(oldRange.Version, freshRange.Version);
        Assert.NotEqual(oldRange.Generation, freshRange.Generation);
        var frame = shell.CanvasFrame;
        Assert.Equal(AccessibleRevealResult.StaleRange, viewport.TryReveal(oldRange, true));
        Assert.Same(frame, shell.CanvasFrame);
        Assert.Equal(AccessibleRevealResult.Revealed, viewport.TryReveal(freshRange, true));
    }

    /// <summary>Bottom-aligned reveal must paint the requested slice, not just scroll to its logical row.</summary>
    [Fact]
    public void Canvas_accessibility_bottom_reveal_paints_middle_of_long_line_with_bounded_slices()
    {
        using var temp = new RepoTemp();
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = true };
        using var controller = NewController(shell, temp.Path, null);
        controller.Run();
        shell.ResizeCanvas(200);
        var source = string.Concat(Enumerable.Repeat("short\n", 100)) + new string('x', 100_000);
        var initial = shell.CanvasBinding!;
        shell.CommitCanvasEdit(new CanvasCommittedEdit(initial.DocumentGeneration,
            initial.BaseVersion, initial.BindingNonce, new TextChange(0, 0, source), source.Length));

        const int target = 50_600;
        var accessible = Assert.IsType<AccessibleDocument>(shell.CanvasAccessibilityDocument);
        var range = accessible.MakeRange(target, target + 1);
        var viewport = Assert.IsAssignableFrom<IAccessibleViewport>(shell.CanvasAccessibilityViewport);
        shell.CanvasGeometryProbe = ProveVisibleSourceSlice;
        Assert.Equal(AccessibleRevealResult.Revealed, viewport.TryReveal(range, alignToTop: false));
        Assert.Contains(shell.CanvasFrame!.Slices, slice => slice.SourceStart <= target &&
            target < slice.SourceStart + slice.SourceLength);
        Assert.All(shell.CanvasFrame.Slices, slice => Assert.InRange(slice.SourceLength, 0, 16 * 1024));
        Assert.InRange(shell.CanvasBinding!.InputSourceText.Length, 0, 16 * 1024);
        Assert.Equal("x", accessible.GetText(range));
    }

    /// <summary>A 16 Ki source slice does not prove a caret at column 3,000 fits in 600 screen pixels.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Canvas_accessibility_reveal_requires_current_frame_pixel_proof(bool visibleAfterRebase)
    {
        using var temp = new RepoTemp();
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = true };
        using var controller = NewController(shell, temp.Path, null);
        controller.Run();
        shell.ResizeCanvas(600);
        var accessible = Assert.IsType<AccessibleDocument>(shell.CanvasAccessibilityDocument);
        var stale = accessible.MakeRange(0, 0);
        var first = shell.CanvasBinding!;
        var source = new string('a', 16 * 1024);
        shell.CommitCanvasEdit(new CanvasCommittedEdit(first.DocumentGeneration,
            first.BaseVersion, first.BindingNonce, new TextChange(0, 0, source), source.Length));
        const int target = 3000;
        var range = accessible.MakeRange(target, target + 1);
        var viewport = Assert.IsAssignableFrom<IAccessibleViewport>(shell.CanvasAccessibilityViewport);
        Assert.Contains(shell.CanvasFrame!.Slices, slice => slice.SourceStart <= target &&
            target < slice.SourceStart + slice.SourceLength);
        var version = shell.CanvasBinding!.BaseVersion;
        var calls = 0;
        shell.CanvasGeometryProbe = (frame, offset) =>
        {
            calls++;
            Assert.Equal(target, offset);
            return new CanvasCaretGeometry(3000, 0, 20,
                visibleAfterRebase && frame.Horizontal.SourceBoundary == target);
        };

        var before = shell.CanvasFrame;
        Assert.Equal(AccessibleRevealResult.StaleRange, viewport.TryReveal(stale, true));
        shell.IsCanvasComposing = true;
        Assert.Equal(AccessibleRevealResult.CompositionBlocked, viewport.TryReveal(range, true));
        shell.IsCanvasComposing = false;
        Assert.Same(before, shell.CanvasFrame);
        Assert.Equal(0, calls);

        var result = viewport.TryReveal(range, true);
        Assert.Equal(visibleAfterRebase ? AccessibleRevealResult.Revealed : AccessibleRevealResult.NotVisible,
            result);
        Assert.Equal(2, calls);
        Assert.Equal(target, shell.CanvasFrame!.Horizontal.SourceBoundary);
        Assert.Equal(version, shell.CanvasBinding!.BaseVersion);
        Assert.Equal(source.Length, shell.CanvasBinding.Snapshot.Length);
        Assert.Equal("a", shell.CanvasBinding.Snapshot.GetText(target, 1));
        Assert.All(shell.CanvasFrame.RowWindows, row =>
            Assert.InRange(row.Slice.SourceLength, 0, 16 * 1024));
        Assert.Empty(shell.Errors);
    }

    /// <summary>An unpaintable CRLF interior must not be falsely reported as revealed.</summary>
    [Fact]
    public void Canvas_accessibility_reveal_reports_unpaintable_crlf_interior()
    {
        using var temp = new RepoTemp();
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = true };
        using var controller = NewController(shell, temp.Path, null);
        controller.Run();
        var initial = shell.CanvasBinding!;
        shell.CommitCanvasEdit(new CanvasCommittedEdit(initial.DocumentGeneration,
            initial.BaseVersion, initial.BindingNonce, new TextChange(0, 0, "a\r\nb"), 4));

        var accessible = Assert.IsType<AccessibleDocument>(shell.CanvasAccessibilityDocument);
        var interior = accessible.MakeRange(2, 2);
        var viewport = Assert.IsAssignableFrom<IAccessibleViewport>(shell.CanvasAccessibilityViewport);
        var before = shell.CanvasFrame;
        Assert.Equal(AccessibleRevealResult.NotVisible, viewport.TryReveal(interior, alignToTop: true));
        Assert.Same(before, shell.CanvasFrame);
        Assert.DoesNotContain(shell.CanvasFrame!.Slices, slice =>
            slice.SourceStart <= 2 && 2 < slice.SourceStart + slice.SourceLength);
    }

    /// <summary>Undo/Redo must reveal a caret hidden inside the source gap of a long-line frame.</summary>
    [Fact]
    public void Canvas_undo_redo_reveals_caret_inside_hidden_long_line_gap()
    {
        using var temp = new RepoTemp();
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = true };
        using var controller = NewController(shell, temp.Path, null);
        controller.Run();
        shell.ResizeCanvas(200);
        var source = string.Concat(Enumerable.Repeat("short\n", 100)) +
            new string('x', 100_000) + "\nAFTER\n";
        var first = shell.CanvasBinding!;
        shell.CommitCanvasEdit(new CanvasCommittedEdit(first.DocumentGeneration,
            first.BaseVersion, first.BindingNonce, new TextChange(0, 0, source), source.Length));

        const int target = 50_600;
        shell.SelectCanvas(target, target);
        var nearTarget = shell.CanvasBinding!;
        shell.CommitCanvasEdit(new CanvasCommittedEdit(nearTarget.DocumentGeneration,
            nearTarget.BaseVersion, nearTarget.BindingNonce, new TextChange(target, 0, "Z"), target + 1));
        Assert.Equal("Z", shell.CanvasBinding!.Snapshot.GetText(target, 1));

        var accessible = Assert.IsType<AccessibleDocument>(shell.CanvasAccessibilityDocument);
        var viewport = Assert.IsAssignableFrom<IAccessibleViewport>(shell.CanvasAccessibilityViewport);
        var beginning = accessible.MakeRange(600, 601);
        shell.CanvasGeometryProbe = ProveVisibleSourceSlice;
        Assert.Equal(AccessibleRevealResult.Revealed, viewport.TryReveal(beginning, alignToTop: true));
        Assert.DoesNotContain(shell.CanvasFrame!.Slices, slice => Contains(slice, target + 1));
        Assert.Contains(shell.CanvasFrame.Slices, slice => slice.SourceStart > target + 1);

        shell.RequestUndo();
        Assert.Equal(source, shell.CanvasBinding!.Snapshot.GetText());
        Assert.Contains(shell.CanvasFrame!.Slices, slice => Contains(slice, target));
        Assert.All(shell.CanvasFrame.Slices, slice => Assert.InRange(slice.SourceLength, 0, 16 * 1024));

        shell.RequestRedo();
        Assert.Equal("Z", shell.CanvasBinding!.Snapshot.GetText(target, 1));
        Assert.Contains(shell.CanvasFrame!.Slices, slice => Contains(slice, target + 1));
        Assert.Empty(shell.Errors);

        static bool Contains(ViewportSlice slice, int offset) =>
            slice.SourceStart <= offset && offset < slice.SourceStart + slice.SourceLength;
    }

    /// <summary>A failed optional OS AX attachment leaves the editor alive and warns after startup Open.</summary>
    [Fact]
    public async Task Canvas_accessibility_attach_failure_survives_startup_open_with_persistent_warning()
    {
        using var temp = new RepoTemp();
        var path = temp.File("ax-attach.txt");
        await File.WriteAllTextAsync(path, "private source marker");
        var shell = new FakeShell(NativeLineEndingMode.Preserve)
        {
            CanvasEnabled = true,
            RejectAccessibilityAttach = true
        };
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.CanvasBinding?.Snapshot.GetText() == "private source marker");

        Assert.Null(shell.CanvasAccessibilityDocument);
        Assert.Contains("Accessibility provider unavailable", shell.CanvasStatus);
        Assert.Contains("Accessibility provider unavailable", shell.StatusNotice);
        Assert.DoesNotContain(path, shell.CanvasStatus, StringComparison.Ordinal);
        Assert.DoesNotContain("private source marker", shell.CanvasStatus, StringComparison.Ordinal);
        Assert.Empty(shell.Errors);
    }

    /// <summary>A later AX provider fault is isolated from edits and remains visible across document swaps.</summary>
    [Fact]
    public async Task Canvas_accessibility_runtime_failure_preserves_edit_undo_and_warning_across_new_open()
    {
        using var temp = new RepoTemp();
        var path = temp.File("ax-recovery.txt");
        await File.WriteAllTextAsync(path, "disk marker");
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = true };
        using var controller = NewController(shell, temp.Path, null);
        controller.Run();
        Assert.NotNull(shell.CanvasAccessibilityDocument);

        var initial = shell.CanvasBinding!;
        shell.CommitCanvasEdit(new CanvasCommittedEdit(initial.DocumentGeneration,
            initial.BaseVersion, initial.BindingNonce, new TextChange(0, 0, "abc"), 3));
        Assert.Equal("abc", shell.CanvasBinding!.Snapshot.GetText());
        shell.FailCanvasAccessibility();
        shell.Pump();
        Assert.Contains("Accessibility provider unavailable", shell.CanvasStatus);
        Assert.Equal("abc", shell.CanvasBinding!.Snapshot.GetText());

        shell.RequestUndo();
        Assert.Equal("", shell.CanvasBinding!.Snapshot.GetText());
        shell.RequestRedo();
        Assert.Equal("abc", shell.CanvasBinding!.Snapshot.GetText());
        shell.RequestNew();
        Assert.Equal("", shell.CanvasBinding!.Snapshot.GetText());
        Assert.Contains("Accessibility provider unavailable", shell.CanvasStatus);

        shell.OpenPath = path;
        shell.RequestOpen();
        await shell.PumpUntilAsync(() => shell.CanvasBinding?.Snapshot.GetText() == "disk marker");
        Assert.Contains("Accessibility provider unavailable", shell.CanvasStatus);
        Assert.DoesNotContain(path, shell.CanvasStatus, StringComparison.Ordinal);
        Assert.DoesNotContain("disk marker", shell.CanvasStatus, StringComparison.Ordinal);
        Assert.Empty(shell.Errors);
    }

    /// <summary>Opt-in fake OS proof for tests that intentionally certify a painted source edge.</summary>
    private static CanvasCaretGeometry? ProveVisibleSourceSlice(CanvasFrame frame, int sourceOffset) =>
        frame.Slices.Any(slice => sourceOffset >= slice.SourceStart &&
            (sourceOffset < slice.SourceStart + slice.SourceLength ||
             sourceOffset == slice.SourceStart + slice.SourceLength && !slice.HasHiddenSuffix))
            ? new CanvasCaretGeometry(0, 0, 20, true) : null;

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
    private sealed class FakeShell(NativeLineEndingMode lineEndingMode) : INativeCanvasShell
    {
        private readonly ConcurrentQueue<Action> _posted = new();

        /// <inheritdoc />
        public NativeLineEndingMode LineEndingMode { get; } = lineEndingMode;
        /// <inheritdoc />
        public bool CanvasEnabled { get; set; }
        /// <inheritdoc />
        public int MaxCanvasInputLength { get; set; } = CanvasInputWindowSelector.MaxLength;
        /// <inheritdoc />
        public bool IsCanvasComposing { get; set; }
        /// <inheritdoc />
        public bool PrefersDark { get; set; } = true;
        /// <inheritdoc />
        public bool IsTextComposing { get; set; }
        /// <inheritdoc />
        public event Action? AppearanceChanged;
        /// <inheritdoc />
        public event Action? CompositionSettled;
        /// <inheritdoc />
        public event Action<string>? TextChanged;
        /// <inheritdoc />
        public event Action<int, int>? SelectionChanged;
        /// <inheritdoc />
        public event Action<NativePreviewActivation>? PreviewActivated;
        /// <inheritdoc />
        public event Action? NewRequested;
        /// <inheritdoc />
        public event Action? OpenRequested;
        /// <inheritdoc />
        public event Action<NativeSaveRequest>? SaveRequested;
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
        /// <inheritdoc />
        public event Action<CanvasCommittedEdit>? CanvasEditCommitted;
        /// <inheritdoc />
        public event Action<double>? CanvasScrollRequested;
        /// <inheritdoc />
        public event Action<double>? CanvasViewportResized;
        /// <inheritdoc />
        public event Action<int, int>? CanvasSelectionRequested;
        /// <inheritdoc />
        public event Action<CanvasHorizontalAnchorRequest>? CanvasHorizontalAnchorRequested;
        /// <inheritdoc />
        public event Action? CanvasAccessibilityFailed;

        /// <summary>The current fake text viewport.</summary>
        public NativeDocumentView? Document { get; private set; }
        /// <summary>Counts complete text-page projections; status-only theme notices must not add one.</summary>
        public int DocumentSetCount { get; private set; }
        /// <summary>The current fake semantic presentation.</summary>
        public NativeAnalysisView? Analysis { get; private set; }
        /// <summary>The latest bounded input-island binding in opt-in canvas mode.</summary>
        public NativeCanvasBinding? CanvasBinding { get; private set; }
        /// <summary>The latest source-backed canvas frame, including far source slices.</summary>
        public CanvasFrame? CanvasFrame { get; private set; }
        /// <summary>The latest absolute semantic facts offered to the canvas.</summary>
        public NativeCanvasSemantics? CanvasSemantics { get; private set; }
        /// <summary>The engine-backed accessibility model installed by the controller.</summary>
        public AccessibleDocument? CanvasAccessibilityDocument { get; private set; }
        /// <summary>The UI-thread reveal bridge installed by the controller.</summary>
        public IAccessibleViewport? CanvasAccessibilityViewport { get; private set; }
        /// <summary>Models an OS accessibility provider that cannot attach.</summary>
        public bool RejectAccessibilityAttach { get; set; }
        /// <summary>An explicit fake OS glyph-geometry oracle; null means visibility is unproven.</summary>
        public Func<CanvasFrame, int, CanvasCaretGeometry?>? CanvasGeometryProbe { get; set; }
        /// <summary>Effective native status: analysis replaces chrome, but persistent notices survive.</summary>
        public string? CanvasStatus => StatusNotice is null ? _statusText :
            $"{_statusText}  ·  {StatusNotice}";
        /// <summary>Ordinary status, replaced by chrome and semantic analysis just like AppKit.</summary>
        private string? _statusText;
        /// <summary>A recoverable reason for withholding an unsafe input binding.</summary>
        public string? CanvasInputError { get; private set; }
        /// <summary>All presentations, for stale-result assertions.</summary>
        public List<NativeAnalysisView> Analyses { get; } = [];
        /// <summary>All recoverable UI errors.</summary>
        public List<string> Errors { get; } = [];
        /// <summary>The applied compile-time theme.</summary>
        public IThemePolicy? Theme { get; private set; }
        /// <summary>Nonmodal status-chrome notice, never part of document text or preview.</summary>
        public string? StatusNotice { get; private set; }
        /// <summary>All native status-notice updates for deferred-composition assertions.</summary>
        public List<string?> StatusNotices { get; } = [];
        /// <summary>Every attempted native theme application, including a failed attempt and rollback.</summary>
        public List<string> ThemeAttempts { get; } = [];
        /// <summary>One policy ID for which the fake OS resource allocator fails.</summary>
        public string? RejectThemeId { get; set; }
        /// <summary>One policy ID for which composition starts after the controller's preflight.</summary>
        public string? DeferNextThemeId { get; set; }
        /// <summary>One synchronous OS callback while the native palette is being applied.</summary>
        public Action<IThemePolicy>? DuringThemeApply { get; set; }
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
        /// <summary>Counts attempted clipboard publication, including rejected attempts.</summary>
        public int ClipboardSetCalls { get; private set; }
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
        /// <summary>Accepted preview navigation transfers keyboard focus once.</summary>
        public int FocusSourceCount { get; private set; }

        /// <inheritdoc />
        public void Run() => Shown?.Invoke();
        /// <inheritdoc />
        public void SetDocument(NativeDocumentView view)
        {
            Document = view;
            _statusText = view.Status;
            DocumentSetCount++;
            DuringDocumentSet?.Invoke(view);
        }
        /// <summary>Injects a single reentrant UI transition while a native source installation returns.</summary>
        public Action<NativeDocumentView>? DuringDocumentSet { get; set; }
        /// <inheritdoc />
        public void SetCanvasBinding(NativeCanvasBinding binding)
        {
            CanvasBinding = binding;
            CanvasFrame = binding.Frame;
            CanvasInputError = null;
        }
        /// <inheritdoc />
        public void SetCanvasFrame(CanvasFrame frame) => CanvasFrame = frame;
        /// <inheritdoc />
        public void SetCanvasInputUnavailable(TextSnapshot snapshot, CanvasFrame frame, string reason)
        {
            CanvasBinding = null;
            CanvasFrame = frame;
            CanvasInputError = reason;
        }
        /// <inheritdoc />
        public void SetCanvasChrome(string title, string status, bool isModified) => _statusText = status;
        /// <inheritdoc />
        public void SetCanvasSemantics(NativeCanvasSemantics semantics)
        {
            CanvasSemantics = semantics;
            DuringCanvasSemanticsApply?.Invoke(semantics);
        }
        /// <summary>Models a native synchronous callback during semantic overlay installation.</summary>
        public Action<NativeCanvasSemantics>? DuringCanvasSemanticsApply { get; set; }
        /// <inheritdoc />
        public void SetCanvasAccessibility(AccessibleDocument document, IAccessibleViewport viewport)
        {
            if (RejectAccessibilityAttach) throw new InvalidOperationException("Synthetic AX attach failure.");
            CanvasAccessibilityDocument = document;
            CanvasAccessibilityViewport = viewport;
        }
        /// <inheritdoc />
        public CanvasCaretGeometry? GetCanvasCaretGeometry(CanvasFrame frame, int sourceOffset) =>
            ReferenceEquals(frame, CanvasFrame) ? CanvasGeometryProbe?.Invoke(frame, sourceOffset) : null;
        /// <inheritdoc />
        public bool CommitPendingText()
        {
            CommitCalls++;
            DuringPendingCommit?.Invoke();
            if (VetoPendingCommit) return false;
            if (PendingText is not { } pending) return true;
            PendingText = null;
            Edit((Document?.Text ?? "") + pending);
            if (Document is { } view) CommittedTexts.Add(view.Text);
            return true;
        }
        /// <summary>Injects a single document lifetime transition during native composition settlement.</summary>
        public Action? DuringPendingCommit { get; set; }
        /// <inheritdoc />
        public void SetAnalysis(NativeAnalysisView view)
        {
            Analysis = view;
            _statusText = string.IsNullOrEmpty(view.DiagnosticsSummary) ? view.Status :
                $"{view.Status}  ·  {view.DiagnosticsSummary}";
            Analyses.Add(view);
            DuringAnalysisApply?.Invoke(view);
        }
        /// <summary>Models a UI-thread source resize reentrant inside a pane installation.</summary>
        public Action<NativeAnalysisView>? DuringAnalysisApply { get; set; }
        /// <inheritdoc />
        public void SetTheme(IThemePolicy theme)
        {
            ThemeAttempts.Add(theme.Id);
            DuringThemeApply?.Invoke(theme);
            if (string.Equals(theme.Id, DeferNextThemeId, StringComparison.Ordinal))
            {
                DeferNextThemeId = null;
                IsTextComposing = true;
                throw new NativeThemeDeferredException();
            }
            if (string.Equals(theme.Id, RejectThemeId, StringComparison.Ordinal))
                throw new InvalidOperationException("Synthetic palette resource failure.");
            Theme = theme;
        }
        /// <inheritdoc />
        public void SetStatusNotice(string? notice)
        {
            StatusNotice = notice;
            StatusNotices.Add(notice);
        }
        /// <inheritdoc />
        public void SetSelection(int displayAnchor, int displayActive) =>
            DisplaySelection = new NativeProjectedSelection(displayAnchor, displayActive);
        /// <inheritdoc />
        public void FocusSource() => FocusSourceCount++;
        /// <inheritdoc />
        public string? PromptFind() => SearchQuery;
        /// <inheritdoc />
        public int? PromptGoToLine() => LinePrompt;
        /// <inheritdoc />
        public void SetClipboardText(string text)
        {
            ClipboardSetCalls++;
            if (RejectClipboard) throw new InvalidOperationException("Clipboard unavailable.");
            ClipboardText = text;
        }
        /// <inheritdoc />
        public string? PickOpenFile() => OpenPath;
        /// <inheritdoc />
        public string? PickSaveFile(string? currentPath)
        {
            DuringSavePicker?.Invoke();
            return SavePath ?? currentPath;
        }
        /// <summary>Models a native modal picker dispatching another UI transition before returning.</summary>
        public Action? DuringSavePicker { get; set; }
        /// <inheritdoc />
        public bool ConfirmOverwrite(string path)
        {
            OverwritePromptCount++;
            DuringOverwriteConfirm?.Invoke();
            return OverwriteApproved;
        }
        /// <summary>Models a native modal approval dispatching another UI transition before returning.</summary>
        public Action? DuringOverwriteConfirm { get; set; }
        /// <inheritdoc />
        public bool ConfirmDiscard() => true;
        /// <inheritdoc />
        public void ShowError(string message) => Errors.Add(message);
        /// <inheritdoc />
        public void Post(Action action)
        {
            if (RejectPost) throw new InvalidOperationException("Synthetic UI queue refusal.");
            _posted.Enqueue(action);
        }
        /// <summary>Injects synchronous local queue refusal without pretending native delivery occurred.</summary>
        public bool RejectPost { get; set; }
        /// <inheritdoc />
        public void Close() { }

        /// <summary>Raises one native text-edit notification.</summary>
        public void Edit(string text) => TextChanged?.Invoke(text);
        /// <summary>Raises a final source-coordinate edit from the bound OS input host.</summary>
        public void CommitCanvasEdit(CanvasCommittedEdit edit) => CanvasEditCommitted?.Invoke(edit);
        /// <summary>Raises one canvas scroll delta without changing the editor document.</summary>
        public void ScrollCanvas(double pixels) => CanvasScrollRequested?.Invoke(pixels);
        /// <summary>Raises one canvas viewport resize.</summary>
        public void ResizeCanvas(double height) => CanvasViewportResized?.Invoke(height);
        /// <summary>Raises one pointer-resolved source selection.</summary>
        public void SelectCanvas(int anchor, int active) => CanvasSelectionRequested?.Invoke(anchor, active);
        /// <summary>Raises one source-bound horizontal pan request from a shaped platform edge.</summary>
        public void AnchorCanvasHorizontally(CanvasHorizontalAnchorRequest request) =>
            CanvasHorizontalAnchorRequested?.Invoke(request);
        /// <summary>Raises a recoverable native AX provider fault.</summary>
        public void FailCanvasAccessibility() => CanvasAccessibilityFailed?.Invoke();
        /// <summary>Raises one OS appearance notification after changing the queried preference.</summary>
        public void ChangeAppearance(bool prefersDark)
        {
            PrefersDark = prefersDark;
            AppearanceChanged?.Invoke();
        }
        /// <summary>Signals that a native IME commit or cancellation has finished its final edit callback.</summary>
        public void SettleComposition() => CompositionSettled?.Invoke();
        /// <summary>Raises the native Save command.</summary>
        public void RequestSave() => NativeSaveRequest.Receive(NativeSaveKind.Save).Dispatch(SaveRequested);
        /// <summary>Raises the native Save As command.</summary>
        public void RequestSaveAs() => NativeSaveRequest.Receive(NativeSaveKind.SaveAs).Dispatch(SaveRequested);
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
        /// <summary>Raises a native preview gesture with an explicit displayed-analysis stamp.</summary>
        public void ActivatePreview(int offset, NativeDocumentStamp? stamp = null,
            long? sequence = null) =>
            PreviewActivated?.Invoke(new NativePreviewActivation(stamp ?? Analysis!.Stamp, offset,
                sequence ?? Analysis!.PresentationSequence));
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
            Assert.True(condition(),
                $"Timed out waiting for a native-controller state transition; analysis={Analysis?.Status ?? "<none>"}; shell-errors={Errors.Count}.");
        }
    }
}
