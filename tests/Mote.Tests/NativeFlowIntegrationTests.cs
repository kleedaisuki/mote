using Mote.Configuration;
using Mote.Formats;
using Mote.Native;
using Mote.Themes;

namespace Mote.Tests;

/// <summary>Exercises the actual controller/session render path without native window side effects.</summary>
public sealed partial class NativeControllerTests
{
    /// <summary>Same-version refreshes invalidate the old installed map, without mutating source.</summary>
    [Fact]
    public async Task Flow_same_version_presentation_identity_rejects_old_activation()
    {
        using var temp = new RepoTemp();
        var path = temp.File("flow-identity.md");
        const string source = "## Destination\n\nHello **strong** and *gentle*.\n";
        await File.WriteAllTextAsync(path, source);
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Analysis?.Flow is not null);
        var view = shell.Analysis!;
        Assert.True(view.PresentationSequence > 0);
        Assert.Contains(view.Flow!.Runs, run => (run.Style & FlowInlineStyle.Strong) != 0);
        Assert.Contains(view.Flow.Runs, run => (run.Style & FlowInlineStyle.Emphasis) != 0);
        var offset = view.PreviewText.IndexOf("Destination", StringComparison.Ordinal);
        var literal = Assert.Single(view.Flow.Runs, run =>
            run.DisplayRange.Start <= offset && run.DisplayRange.End > offset);
        Assert.Equal(3, literal.SourceRange.Start);
        Assert.Equal(RenderOriginPrecision.ExactText, literal.Precision);
        shell.ActivatePreview(offset, sequence: view.PresentationSequence - 1);
        Assert.Equal(0, shell.FocusSourceCount);
        shell.ActivatePreview(offset);
        Assert.Equal(1, shell.FocusSourceCount);
        Assert.Equal(0, shell.DisplaySelection!.Value.Active);
        Assert.Equal(source, await File.ReadAllTextAsync(path));
    }

    /// <summary>Policy auto conventions apply only to ordinary Continuous; explicit choice wins.</summary>
    [Theory]
    [InlineData(true, "txt", PreviewLayoutPreference.Auto, false)]
    [InlineData(false, "txt", PreviewLayoutPreference.Auto, true)]
    [InlineData(true, "txt", PreviewLayoutPreference.Split, true)]
    [InlineData(false, "md", PreviewLayoutPreference.SourceOnly, false)]
    [InlineData(true, "md", PreviewLayoutPreference.Auto, true)]
    public async Task Flow_preview_layout_applies_policy_then_explicit_preference(bool continuous,
        string extension, PreviewLayoutPreference preference, bool expectedPreview)
    {
        using var temp = new RepoTemp();
        var path = temp.File("layout." + extension);
        await File.WriteAllTextAsync(path, "Readable text\n");
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = continuous };
        var config = MoteConfigLoader.Load(new MoteConfigLoadOptions
        {
            UserHomeDirectory = temp.Path, UseEnvironmentOverride = false
        }) with { PreviewLayout = preference };
        using var controller = new NativeEditorController(shell, config,
            ThemePolicies.Get(config.ThemeId), path, continuous ?
                EditorPresentationProfile.Continuous : EditorPresentationProfile.LegacyPage);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Analysis?.Flow is not null);
        Assert.Equal(expectedPreview, shell.Analysis!.ShowPreview);
        Assert.Equal("Readable text\n", await File.ReadAllTextAsync(path));
    }
    /// <summary>An immediate source-only layout can resize reentrantly without publishing the old map.</summary>
    [Fact]
    public async Task Flow_initial_pending_layout_reentrant_canvas_resize_keeps_latest_request()
    {
        using var temp = new RepoTemp();
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = true };
        var resized = false;
        shell.DuringAnalysisApply = view =>
        {
            if (resized || view.ShowPreview) return;
            resized = true;
            shell.ResizeCanvas(320);
        };
        var config = MoteConfigLoader.Load(new MoteConfigLoadOptions
        { UserHomeDirectory = temp.Path, UseEnvironmentOverride = false });
        using var controller = new NativeEditorController(shell, config, ThemePolicies.Get(config.ThemeId),
            null, EditorPresentationProfile.Continuous);
        controller.Run();
        Assert.True(resized);
        Assert.False(shell.Analysis!.ShowPreview);
        await shell.PumpUntilAsync(() => shell.Analysis?.Flow is not null);
        Assert.Equal(shell.Analyses.Max(view => view.PresentationSequence), shell.Analysis!.PresentationSequence);
        Assert.Empty(shell.Errors);
    }

    /// <summary>A real same-version viewport refresh cannot reactivate the earlier installed map.</summary>
    [Fact]
    public async Task Flow_viewport_refresh_invalidates_same_document_version_activation()
    {
        using var temp = new RepoTemp();
        var path = temp.File("same-version.md");
        await File.WriteAllTextAsync(path, "## Destination\n\nBody text.\n");
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = true };
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Analysis?.Flow is not null);
        var old = shell.Analysis!;
        shell.ResizeCanvas(320);
        await shell.PumpUntilAsync(() => shell.Analysis?.Flow is not null &&
            shell.Analysis.PresentationSequence > old.PresentationSequence);
        var current = shell.Analysis!;
        Assert.Equal(old.Stamp, current.Stamp);
        var offset = current.PreviewText.IndexOf("Destination", StringComparison.Ordinal);
        shell.ActivatePreview(offset, old.Stamp, old.PresentationSequence);
        Assert.Equal(0, shell.FocusSourceCount);
        shell.ActivatePreview(offset);
        Assert.Equal(1, shell.FocusSourceCount);
        Assert.Equal(0, shell.CanvasFrame!.SelectionActive);
    }
}
