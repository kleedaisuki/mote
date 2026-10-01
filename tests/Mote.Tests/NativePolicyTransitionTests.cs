using Mote.Formats;
using Mote.Native;

namespace Mote.Tests;

/// <summary>Save As format-policy changes cannot display stale semantic facts at the same version.</summary>
public sealed partial class NativeControllerTests
{
    /// <summary>The save completion clears old JSON tokens before Markdown analysis can publish.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_as_new_format_clears_old_semantics_before_background_result(bool canvas)
    {
        using var temp = new RepoTemp();
        var original = temp.File("policy-source.json");
        var destination = temp.File("policy-target.md");
        await File.WriteAllTextAsync(original, "{\"a\":1}");
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = canvas };
        using var controller = NewController(shell, temp.Path, original);
        controller.Run();
        await shell.PumpUntilAsync(() => (canvas
                ? shell.CanvasBinding?.Snapshot.Length == 7
                : shell.Document?.Title.Contains("policy-source.json") == true) &&
            shell.Analysis?.Status.Contains("JSON · Complete", StringComparison.Ordinal) == true);
        Assert.NotEmpty(shell.Analysis!.Tokens);

        if (canvas)
        {
            var binding = shell.CanvasBinding!;
            shell.CommitCanvasEdit(new CanvasCommittedEdit(binding.DocumentGeneration,
                binding.BaseVersion, binding.BindingNonce, new Mote.Engine.TextChange(5, 1, "2"), 6));
        }
        else shell.Edit("{\"a\":2}");
        await shell.PumpUntilAsync(() => shell.Analysis?.Status.Contains("JSON · Complete · v1",
            StringComparison.Ordinal) == true);

        if (canvas) shell.SelectCanvas(2, 5);
        else shell.ChangeSelection(2, 5);
        var stamp = shell.Analysis.Stamp;
        var source = shell.CanvasBinding?.Snapshot.GetText() ?? shell.Document!.Text;
        var selection = canvas
            ? (shell.CanvasFrame!.SelectionAnchor, shell.CanvasFrame.SelectionActive)
            : (shell.DisplaySelection!.Value.Anchor, shell.DisplaySelection.Value.Active);
        shell.SavePath = destination;
        shell.RequestSaveAs();
        await PumpUntilPolicyPendingAsync(shell);

        var pending = shell.Analysis!;
        Assert.Contains("Markdown · analyzing", pending.Status, StringComparison.Ordinal);
        Assert.Equal(stamp, pending.Stamp);
        Assert.Empty(pending.Tokens);
        Assert.Equal("", pending.PreviewText);
        Assert.Contains("global diagnostics unknown", pending.DiagnosticsSummary,
            StringComparison.Ordinal);
        if (canvas) Assert.Equal(stamp.Version, shell.CanvasBinding!.BaseVersion);
        else Assert.Equal(stamp, shell.Document!.Stamp);
        Assert.Equal(source, shell.CanvasBinding?.Snapshot.GetText() ?? shell.Document!.Text);
        if (canvas)
        {
            Assert.Equal(selection,
                (shell.CanvasFrame!.SelectionAnchor, shell.CanvasFrame.SelectionActive));
            Assert.Equal(AnalysisCompleteness.Provisional, shell.CanvasSemantics!.Completeness);
            Assert.Empty(shell.CanvasSemantics.Tokens);
            Assert.Empty(shell.CanvasSemantics.Diagnostics);
            Assert.Equal(0, shell.CanvasSemantics.Coverage.Length);
        }
        else Assert.Equal(selection,
            (shell.DisplaySelection!.Value.Anchor, shell.DisplaySelection.Value.Active));
        Assert.Equal(source, await File.ReadAllTextAsync(destination));
        Assert.Empty(shell.Errors);
        shell.RequestUndo();
        Assert.Equal("{\"a\":1}", shell.CanvasBinding?.Snapshot.GetText() ?? shell.Document!.Text);
    }

    /// <summary>Changing only a pathname under one policy does not invalidate same-version facts.</summary>
    [Fact]
    public async Task Save_as_same_format_never_emits_a_false_policy_pending_view()
    {
        using var temp = new RepoTemp();
        var original = temp.File("same-source.json");
        var destination = temp.File("same-target.json");
        await File.WriteAllTextAsync(original, "{\"a\":1}");
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = NewController(shell, temp.Path, original);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Analysis?.Status.Contains("JSON · Complete",
            StringComparison.Ordinal) == true);
        var before = shell.Analyses.Count;
        var stamp = shell.Document!.Stamp;
        shell.SavePath = destination;
        shell.RequestSaveAs();
        await PumpUntilSavedTitleAsync(shell, "same-target.json");

        Assert.DoesNotContain(shell.Analyses.Skip(before), analysis =>
            analysis.DiagnosticsSummary.Contains("Format analysis pending",
                StringComparison.Ordinal));
        Assert.Equal(stamp, shell.Document!.Stamp);
        Assert.Equal("{\"a\":1}", await File.ReadAllTextAsync(destination));
        Assert.Empty(shell.Errors);
    }

    /// <summary>Stops at the exact queued save callback; future analyzer callbacks stay undelivered.</summary>
    private static async Task PumpUntilPolicyPendingAsync(FakeShell shell)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await shell.WaitForPostedAsync();
            shell.PumpOne();
            if (shell.Analysis?.DiagnosticsSummary.Contains("Format analysis pending",
                StringComparison.Ordinal) == true) return;
        }
        Assert.Fail("Save As never published its policy-transition invalidation.");
    }

    /// <summary>Drains only enough queued callbacks to observe the new file identity.</summary>
    private static async Task PumpUntilSavedTitleAsync(FakeShell shell, string fileName)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await shell.WaitForPostedAsync();
            shell.PumpOne();
            if (shell.Document?.Title.Contains(fileName, StringComparison.Ordinal) == true) return;
        }
        Assert.Fail("Save As did not publish the new file identity.");
    }
}
