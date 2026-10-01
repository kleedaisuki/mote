using System.Reflection;
using Mote.Engine;
using Mote.Formats;
using Mote.Native;

namespace Mote.Tests;

/// <summary>Challenges the narrow partial-Full TOML publisher without replacing native rendering.</summary>
public sealed partial class NativeControllerTests
{
    /// <summary>A known error augments the source overlay while preserving every visible rendering payload.</summary>
    [Fact]
    public async Task Toml_known_error_merges_once_without_replacing_visible_rendering()
    {
        using var temp = new RepoTemp();
        var path = temp.File("witness.toml");
        await File.WriteAllTextAsync(path, "title = 'visible'\nvalue = 1\n");
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = true };
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Analysis?.Status.Contains("TOML · Complete") == true);
        var before = shell.Analysis!;
        var beforeSource = shell.CanvasFrame;
        var beforeSemantics = shell.CanvasSemantics!;
        var document = KnownErrorField<Document>(controller, "_document");
        var witness = new Diagnostic(DiagnosticSeverity.Error, "TOML_OWNERSHIP", "Already defined.", new TextSpan(18, 5));
        var result = KnownErrorResult(document.Snapshot, witness);

        PublishKnownError(controller, result);
        AssertKnownErrorRenderingPreserved(before, shell.Analysis!);
        Assert.Same(beforeSource, shell.CanvasFrame);
        Assert.Same(beforeSemantics.Tokens, shell.CanvasSemantics!.Tokens);
        Assert.Equal(AnalysisCompleteness.Provisional, shell.CanvasSemantics.Completeness);
        Assert.Equal(witness, Assert.Single(shell.CanvasSemantics.Diagnostics));
        Assert.Contains("global diagnostics unknown", shell.Analysis!.DiagnosticsSummary);
        Assert.Contains("TOML_OWNERSHIP", shell.Analysis.DiagnosticsSummary);
        Assert.DoesNotContain("document diagnostics", shell.Analysis.DiagnosticsSummary);
        Assert.Equal(0, document.Snapshot.Version);
        Assert.Equal("title = 'visible'\nvalue = 1\n", document.Snapshot.GetText());

        PublishKnownError(controller, result);
        Assert.Single(shell.CanvasSemantics.Diagnostics);
        AssertKnownErrorRenderingPreserved(before, shell.Analysis!);
    }

    /// <summary>Idle publication refuses every stale identity and does not install an obsolete source overlay.</summary>
    [Theory]
    [InlineData("document")]
    [InlineData("driver")]
    [InlineData("policy")]
    [InlineData("snapshot")]
    [InlineData("result")]
    [InlineData("viewport-start")]
    [InlineData("viewport-length")]
    [InlineData("generation")]
    [InlineData("disposed")]
    public async Task Toml_known_error_refuses_stale_publication(string mismatch)
    {
        using var temp = new RepoTemp();
        var path = temp.File("identity.toml");
        await File.WriteAllTextAsync(path, "key = 1\n");
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = true };
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Analysis?.Status.Contains("TOML · Complete") == true);
        var beforeSemantics = shell.CanvasSemantics;
        var document = KnownErrorField<Document>(controller, "_document");
        var driver = KnownErrorField<NativeFormatSessionDriver>(controller, "_sessionDriver");
        var policy = KnownErrorField<IDocumentPolicy>(controller, "_policy");
        var snapshot = document.Snapshot;
        var range = KnownErrorViewport(controller);
        using var otherDocument = new Document("key = 1\n");
        using var otherDriver = new NativeFormatSessionDriver((IIncrementalDocumentPolicy)policy);
        var result = KnownErrorResult(snapshot, new Diagnostic(DiagnosticSeverity.Error,
            "TOML_OWNERSHIP", "Already defined.", new TextSpan(0, 3)));
        switch (mismatch)
        {
            case "document": document = otherDocument; break;
            case "driver": driver = otherDriver; break;
            case "policy": policy = DocumentPolicies.ForKind(DocumentKind.Json); break;
            case "snapshot": otherDocument.Apply(new TextChange(0, 0, " ")); snapshot = otherDocument.Snapshot; break;
            case "result": result = new DocumentAnalysis(1, range, AnalysisCompleteness.Provisional,
                result.Root, result.Diagnostics, [], null); break;
            case "viewport-start": range = range with { Start = range.Start + 1 }; break;
            case "viewport-length": range = range with { Length = range.Length + 1 }; break;
            case "generation": KnownErrorSetField(controller, "_canvasGeneration",
                KnownErrorField<long>(controller, "_canvasGeneration") + 1); break;
            case "disposed": controller.Dispose(); break;
        }
        PublishKnownError(controller, result, document, driver, policy, snapshot, range);
        Assert.Same(beforeSemantics, shell.CanvasSemantics);
        Assert.DoesNotContain("TOML_OWNERSHIP", shell.Analysis!.DiagnosticsSummary);
    }

    /// <summary>Unrelated partial diagnostics and offscreen witnesses cannot fabricate visible errors or global counts.</summary>
    [Theory]
    [InlineData("unrelated")]
    [InlineData("offscreen")]
    [InlineData("warning")]
    [InlineData("covered")]
    public async Task Toml_known_error_keeps_unrelated_or_offscreen_partial_facts(string reason)
    {
        using var temp = new RepoTemp();
        var path = temp.File("narrow.toml");
        await File.WriteAllTextAsync(path, "key = 1\n");
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = true };
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Analysis?.Status.Contains("TOML · Complete") == true);
        var before = shell.Analysis!;
        var beforeSemantics = shell.CanvasSemantics;
        var document = KnownErrorField<Document>(controller, "_document");
        var witness = new Diagnostic(reason == "warning" ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error,
            reason == "unrelated" ? "TOML_SYNTAX" : "TOML_OWNERSHIP", "Not merged.",
            reason == "offscreen" ? new TextSpan(document.Snapshot.Length + 1, 1) : new TextSpan(0, 3));
        var result = KnownErrorResult(document.Snapshot, witness,
            reason == "covered" ? AnalysisCompleteness.CoveredRegion : AnalysisCompleteness.Provisional);
        PublishKnownError(controller, result);
        AssertKnownErrorRenderingPreserved(before, shell.Analysis!);
        Assert.Equal(before.DiagnosticsSummary, shell.Analysis!.DiagnosticsSummary);
        Assert.Same(beforeSemantics, shell.CanvasSemantics);
        Assert.Contains("global diagnostics unknown", shell.Analysis.Status);
    }

    /// <summary>Scheduling a new source version clears the paired frame before the next background turn.</summary>
    [Fact]
    public async Task Toml_known_error_is_not_retained_across_edit_undo_and_new()
    {
        using var temp = new RepoTemp();
        var path = temp.File("lifetime.toml");
        await File.WriteAllTextAsync(path, "key = 1\n");
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = true };
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Analysis?.Status.Contains("TOML · Complete") == true);
        var document = KnownErrorField<Document>(controller, "_document");
        var result = KnownErrorResult(document.Snapshot, new Diagnostic(DiagnosticSeverity.Error,
            "TOML_OWNERSHIP", "Already defined.", new TextSpan(0, 3)));
        PublishKnownError(controller, result);
        var binding = shell.CanvasBinding!;
        shell.CommitCanvasEdit(new CanvasCommittedEdit(binding.DocumentGeneration,
            binding.BaseVersion, binding.BindingNonce, new TextChange(0, 3, "other"), 5));
        Assert.Null(KnownErrorField<object?>(controller, "_visibleSessionAnalysis"));
        await shell.PumpUntilAsync(() => shell.Analysis?.Status.Contains("Complete · v1") == true);
        Assert.Empty(shell.CanvasSemantics!.Diagnostics);
        shell.RequestUndo();
        Assert.Null(KnownErrorField<object?>(controller, "_visibleSessionAnalysis"));
        await shell.PumpUntilAsync(() => shell.Analysis?.Status.Contains("Complete · v2") == true);
        Assert.Empty(shell.CanvasSemantics!.Diagnostics);
        shell.RequestNew();
        Assert.Null(KnownErrorField<object?>(controller, "_visibleSessionAnalysis"));
        PublishKnownError(controller, result);
        Assert.DoesNotContain("TOML_OWNERSHIP", shell.Analysis!.DiagnosticsSummary);
    }

    /// <summary>Exercises the actual large TOML Visible-to-idle-Full pipeline, repair, Undo and Redo.</summary>
    [Fact]
    public async Task Toml_known_error_actual_large_idle_pipeline_repairs_and_rechecks_versions()
    {
        using var temp = new RepoTemp();
        var path = temp.File("actual-large.toml");
        var source = "a=1\na=2\n" + string.Concat(Enumerable.Repeat("#" + new string('x', 4094) + "\n", 1025));
        Assert.True(source.Length > 4 * 1024 * 1024);
        await File.WriteAllTextAsync(path, source);
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = true };
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Analysis?.Status.Contains("TOML · Provisional · v0") == true);
        var visible = shell.Analysis!;
        Assert.Empty(shell.CanvasSemantics!.Diagnostics);
        await shell.PumpUntilAsync(() => shell.Analysis?.DiagnosticsSummary.Contains("TOML_OWNERSHIP") == true);
        AssertKnownErrorRenderingPreserved(visible, shell.Analysis!);
        Assert.Contains("Full pass Provisional", shell.Analysis!.Status);
        Assert.Contains("global diagnostics unknown", shell.Analysis.DiagnosticsSummary);
        Assert.Equal(new TextSpan(4, 1), Assert.Single(shell.CanvasSemantics!.Diagnostics).Span);
        shell.SelectCanvas(4, 4);
        var binding = shell.CanvasBinding!;
        Assert.Equal(KnownErrorField<long>(controller, "_canvasBindingNonce"), binding.BindingNonce);
        Assert.InRange(4, binding.InputSourceStart, binding.InputSourceStart + binding.InputSourceText.Length);
        shell.CommitCanvasEdit(new CanvasCommittedEdit(binding.DocumentGeneration,
            binding.BaseVersion, binding.BindingNonce, new TextChange(4, 1, "b"), 5));
        Assert.Empty(shell.Errors);
        Assert.Null(KnownErrorField<object?>(controller, "_visibleSessionAnalysis"));
        await shell.PumpUntilAsync(() => shell.Analysis?.Status.Contains("TOML · Complete · v1") == true);
        Assert.Empty(shell.CanvasSemantics!.Diagnostics);
        shell.RequestUndo();
        await shell.PumpUntilAsync(() => shell.Analysis?.Status.Contains("v2") == true &&
            shell.Analysis.DiagnosticsSummary.Contains("TOML_OWNERSHIP"));
        Assert.Equal(new TextSpan(4, 1), Assert.Single(shell.CanvasSemantics!.Diagnostics).Span);
        shell.RequestRedo();
        await shell.PumpUntilAsync(() => shell.Analysis?.Status.Contains("TOML · Complete · v3") == true);
        Assert.Empty(shell.CanvasSemantics!.Diagnostics);
        Assert.Equal(source, await File.ReadAllTextAsync(path));
        Assert.Empty(shell.Errors);
    }

    /// <summary>Moving away and reopening another policy cannot retain a source-page witness.</summary>
    [Fact]
    public async Task Toml_known_error_actual_page_open_and_policy_transitions_invalidate_witness()
    {
        using var temp = new RepoTemp();
        var path = temp.File("transitions.toml");
        var source = "a=1\na=2\n" + string.Concat(Enumerable.Repeat("#" + new string('x', 4094) + "\n", 1025));
        await File.WriteAllTextAsync(path, source);
        var json = temp.File("different.json");
        await File.WriteAllTextAsync(json, "{\"valid\":true}");
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Analysis?.DiagnosticsSummary.Contains("TOML_OWNERSHIP") == true);
        shell.LinePrompt = 1027;
        shell.RequestGoToLine();
        Assert.Null(KnownErrorField<object?>(controller, "_visibleSessionAnalysis"));
        await shell.PumpUntilAsync(() => shell.Analysis?.Status.Contains("TOML · Provisional · v0") == true &&
            !shell.Analysis.DiagnosticsSummary.Contains("TOML_OWNERSHIP"));
        Assert.Contains("global diagnostics unknown", shell.Analysis!.DiagnosticsSummary);
        shell.LinePrompt = 1;
        shell.RequestGoToLine();
        await shell.PumpUntilAsync(() => shell.Analysis?.DiagnosticsSummary.Contains("TOML_OWNERSHIP") == true);
        Assert.Contains("global diagnostics unknown", shell.Analysis!.DiagnosticsSummary);
        var oldDocument = KnownErrorField<Document>(controller, "_document");
        var oldSnapshot = oldDocument.Snapshot;
        var oldDriver = KnownErrorField<NativeFormatSessionDriver>(controller, "_sessionDriver");
        var oldPolicy = KnownErrorField<IDocumentPolicy>(controller, "_policy");
        var oldRange = KnownErrorViewport(controller);
        var oldResult = KnownErrorResult(oldSnapshot, new Diagnostic(DiagnosticSeverity.Error,
            "TOML_OWNERSHIP", "Already defined.", new TextSpan(4, 1)));
        shell.OpenPath = json;
        shell.RequestOpen();
        await shell.PumpUntilAsync(() => shell.Analysis?.Status.Contains("JSON · Complete") == true);
        var jsonView = shell.Analysis;
        PublishKnownError(controller, oldResult, oldDocument, oldDriver, oldPolicy, oldSnapshot, oldRange);
        Assert.Same(jsonView, shell.Analysis);
        Assert.DoesNotContain("TOML_OWNERSHIP", shell.Analysis!.DiagnosticsSummary);
        Assert.Equal(source, await File.ReadAllTextAsync(path));
    }

    /// <summary>An initially offscreen proved conflict is retained as one bounded witness and shown when visited.</summary>
    [Fact]
    public async Task Toml_known_error_actual_initially_offscreen_witness_is_projected_on_navigation()
    {
        using var temp = new RepoTemp();
        var path = temp.File("offscreen.toml");
        var padding = string.Concat(Enumerable.Repeat("#" + new string('x', 4094) + "\n", 1025));
        var source = "a=1\n" + padding + "a=2\n";
        await File.WriteAllTextAsync(path, source);
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = true };
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Analysis?.Status.Contains("Full pass Provisional") == true);
        Assert.DoesNotContain("TOML_OWNERSHIP", shell.Analysis!.DiagnosticsSummary);
        Assert.Empty(shell.CanvasSemantics!.Diagnostics);
        Assert.NotNull(KnownErrorField<object?>(controller, "_tomlKnownError"));
        shell.LinePrompt = 1027;
        shell.RequestGoToLine();
        await shell.PumpUntilAsync(() => shell.Analysis?.DiagnosticsSummary.Contains("TOML_OWNERSHIP") == true);
        var diagnostic = Assert.Single(shell.CanvasSemantics!.Diagnostics);
        Assert.Equal(new TextSpan(4 + padding.Length, 1), diagnostic.Span);
        Assert.Contains("global diagnostics unknown", shell.Analysis!.DiagnosticsSummary);
        Assert.DoesNotContain("document diagnostics", shell.Analysis.DiagnosticsSummary);
        Assert.Equal(0, KnownErrorField<Document>(controller, "_document").Snapshot.Version);
    }

    /// <summary>Cancelling the document lifetime before idle Full publication leaves no late known-error UI.</summary>
    [Fact]
    public async Task Toml_known_error_cancelled_idle_full_publishes_nothing()
    {
        using var temp = new RepoTemp();
        var path = temp.File("cancelled.toml");
        var source = "a=1\na=2\n" + string.Concat(Enumerable.Repeat("#" + new string('x', 4094) + "\n", 1025));
        await File.WriteAllTextAsync(path, source);
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Analysis?.Status.Contains("TOML · Provisional · v0") == true);
        var beforeCount = shell.Analyses.Count;
        controller.Dispose();
        await Task.Delay(1600);
        shell.Pump();
        Assert.Equal(beforeCount, shell.Analyses.Count);
        Assert.DoesNotContain("TOML_OWNERSHIP", shell.Analysis!.DiagnosticsSummary);
        Assert.Null(KnownErrorField<object?>(controller, "_visibleSessionAnalysis"));
    }

    /// <summary>A synchronous source edit during native presentation cannot install the old-version witness.</summary>
    [Fact]
    public async Task Toml_known_error_reentrant_edit_refuses_old_source_overlay()
    {
        using var temp = new RepoTemp();
        var path = temp.File("reentrant.toml");
        await File.WriteAllTextAsync(path, "key = 1\n");
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = true };
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Analysis?.Status.Contains("TOML · Complete") == true);
        var document = KnownErrorField<Document>(controller, "_document");
        var result = KnownErrorResult(document.Snapshot, new Diagnostic(DiagnosticSeverity.Error,
            "TOML_OWNERSHIP", "Already defined.", new TextSpan(0, 3)));
        shell.DuringAnalysisApply = _ =>
        {
            shell.DuringAnalysisApply = null;
            var binding = shell.CanvasBinding!;
            shell.CommitCanvasEdit(new CanvasCommittedEdit(binding.DocumentGeneration,
                binding.BaseVersion, binding.BindingNonce, new TextChange(0, 3, "other"), 5));
        };
        PublishKnownError(controller, result);
        Assert.Equal(1, document.Snapshot.Version);
        Assert.DoesNotContain(shell.CanvasSemantics!.Diagnostics, d => d.Code == "TOML_OWNERSHIP");
        Assert.Null(KnownErrorField<object?>(controller, "_visibleSessionAnalysis"));
        await shell.PumpUntilAsync(() => shell.Analysis?.Status.Contains("Complete · v1") == true);
        Assert.Empty(shell.CanvasSemantics!.Diagnostics);
    }

    /// <summary>Builds a synthetic partial result; its empty rendering must never replace the visible frame.</summary>
    private static DocumentAnalysis KnownErrorResult(TextSnapshot snapshot, Diagnostic witness,
        AnalysisCompleteness completeness = AnalysisCompleteness.Provisional) =>
        new(snapshot.Version, new TextSpan(0, snapshot.Length), completeness,
            new SemanticNode("document", new TextSpan(0, snapshot.Length)), [witness], [], null);

    /// <summary>Invokes the private publication boundary after normal asynchronous Visible publication.</summary>
    private static void PublishKnownError(NativeEditorController controller, DocumentAnalysis result,
        Document? document = null, NativeFormatSessionDriver? driver = null, IDocumentPolicy? policy = null,
        TextSnapshot? snapshot = null, TextSpan? range = null)
    {
        document ??= KnownErrorField<Document>(controller, "_document");
        driver ??= KnownErrorField<NativeFormatSessionDriver>(controller, "_sessionDriver");
        policy ??= KnownErrorField<IDocumentPolicy>(controller, "_policy");
        snapshot ??= document.Snapshot;
        typeof(NativeEditorController).GetMethod("PublishIdleFullAnalysis", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(controller, [driver, document, policy, snapshot, result, range ?? KnownErrorViewport(controller),
                new NativePreview("must not replace visible preview", [])]);
    }

    /// <summary>Reads controller internals only in this publication-focused test seam.</summary>
    private static T KnownErrorField<T>(NativeEditorController controller, string name) =>
        (T)typeof(NativeEditorController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;

    /// <summary>Changes only the generation stamp to challenge a same-version stale presentation.</summary>
    private static void KnownErrorSetField(NativeEditorController controller, string name, object value) =>
        typeof(NativeEditorController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(controller, value);

    /// <summary>Gets the current exact source viewport, not a parsed status string.</summary>
    private static TextSpan KnownErrorViewport(NativeEditorController controller) =>
        new(KnownErrorField<int>(controller, "_pageStart"), KnownErrorField<int>(controller, "_pageLength"));

    /// <summary>Checks reference identity for tokens, maps, Flow and source stamp and exact preview text.</summary>
    private static void AssertKnownErrorRenderingPreserved(NativeAnalysisView before, NativeAnalysisView after)
    {
        Assert.Same(before.Tokens, after.Tokens);
        Assert.Same(before.PreviewSpans, after.PreviewSpans);
        Assert.Same(before.Flow, after.Flow);
        Assert.Same(before.Grid, after.Grid);
        Assert.Equal(before.PreviewText, after.PreviewText);
        Assert.Equal(before.Stamp, after.Stamp);
        Assert.Equal(before.ShowPreview, after.ShowPreview);
    }
}
