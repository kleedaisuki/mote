using Mote.Configuration;
using Mote.Engine;
using Mote.Native;
using Mote.Themes;

namespace Mote.Tests;

/// <summary>Product Continuous surfaces source-AX degradation without changing user data.</summary>
public sealed partial class NativeControllerTests
{
    /// <summary>An attach exception keeps startup Open, editing, Undo, and the rollback hint usable.</summary>
    [Fact]
    public async Task Product_continuous_attach_failure_retains_source_and_rollback_notice()
    {
        using var temp = new RepoTemp();
        var path = temp.File("private-ax-source.txt");
        const string source = "private AX source";
        await File.WriteAllTextAsync(path, source);
        var shell = new FakeShell(NativeLineEndingMode.Preserve)
        {
            CanvasEnabled = true,
            RejectAccessibilityAttach = true
        };
        using var controller = NewProductController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.CanvasBinding?.Snapshot.GetText() == source);

        var opened = shell.CanvasBinding!;
        Assert.Equal((0, 0), (opened.Anchor, opened.Active));
        Assert.Null(shell.CanvasAccessibilityDocument);
        AssertProductAccessibilityNotice(shell, path, source);

        shell.SelectCanvas(1, 1);
        var selected = shell.CanvasBinding!;
        shell.CommitCanvasEdit(new CanvasCommittedEdit(selected.DocumentGeneration,
            selected.BaseVersion, selected.BindingNonce, new TextChange(1, 0, "X"), 2));
        Assert.Equal("pXrivate AX source", shell.CanvasBinding!.Snapshot.GetText());
        Assert.Equal(2, shell.CanvasBinding.Active);
        shell.RequestUndo();
        Assert.Equal(source, shell.CanvasBinding!.Snapshot.GetText());
        AssertProductAccessibilityNotice(shell, path, source);
        Assert.Empty(shell.Errors);
    }

    /// <summary>A provider fault is persistent across edit history, New, and another Open.</summary>
    [Fact]
    public async Task Product_continuous_runtime_ax_fault_keeps_edit_history_and_new_open()
    {
        using var temp = new RepoTemp();
        var path = temp.File("private-next-source.txt");
        const string next = "another private source";
        await File.WriteAllTextAsync(path, next);
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = true };
        using var controller = NewProductController(shell, temp.Path, null);
        controller.Run();
        Assert.NotNull(shell.CanvasAccessibilityDocument);

        var initial = shell.CanvasBinding!;
        shell.CommitCanvasEdit(new CanvasCommittedEdit(initial.DocumentGeneration,
            initial.BaseVersion, initial.BindingNonce, new TextChange(0, 0, "abc"), 3));
        var edited = shell.CanvasBinding!;
        shell.FailCanvasAccessibility();
        shell.Pump();
        Assert.Equal("abc", shell.CanvasBinding!.Snapshot.GetText());
        Assert.Equal((edited.BaseVersion, 3, 3),
            (shell.CanvasBinding.BaseVersion, shell.CanvasBinding.Anchor, shell.CanvasBinding.Active));
        AssertProductAccessibilityNotice(shell, path, next);

        shell.RequestUndo();
        Assert.Equal("", shell.CanvasBinding!.Snapshot.GetText());
        shell.RequestRedo();
        Assert.Equal("abc", shell.CanvasBinding!.Snapshot.GetText());
        shell.RequestNew();
        Assert.Equal("", shell.CanvasBinding!.Snapshot.GetText());
        AssertProductAccessibilityNotice(shell, path, next);

        shell.OpenPath = path;
        shell.RequestOpen();
        await shell.PumpUntilAsync(() => shell.CanvasBinding?.Snapshot.GetText() == next);
        AssertProductAccessibilityNotice(shell, path, next);
        Assert.Empty(shell.Errors);
    }

    /// <summary>Rejects a profile/shell mismatch before creating an editor session.</summary>
    [Theory]
    [InlineData(false, "continuous")]
    [InlineData(true, "legacy")]
    public void Product_profile_must_match_shell_presentation(
        bool canvas, string profileId)
    {
        using var temp = new RepoTemp();
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = canvas };
        var config = TestConfiguration(temp.Path);
        var profile = profileId == "continuous"
            ? EditorPresentationProfile.Continuous : EditorPresentationProfile.LegacyPage;

        Assert.Throws<ArgumentException>(() => new NativeEditorController(shell,
            config, ThemePolicies.Get(config.ThemeId), null, profile));
    }

    /// <summary>Instantiates only the opt-in product route, not a diagnostic canvas.</summary>
    private static NativeEditorController NewProductController(
        FakeShell shell, string home, string? path)
    {
        var config = TestConfiguration(home);
        return new NativeEditorController(shell, config,
            ThemePolicies.Get(config.ThemeId), path, EditorPresentationProfile.Continuous);
    }

    /// <summary>Loads a synthetic configuration without reading user files or environment overrides.</summary>
    private static MoteConfiguration TestConfiguration(string home) =>
        MoteConfigLoader.Load(new MoteConfigLoadOptions
        {
            UserHomeDirectory = home,
            UseEnvironmentOverride = false
        });

    /// <summary>Requires a persistent, content-free action rather than a silent diagnostic.</summary>
    private static void AssertProductAccessibilityNotice(
        FakeShell shell, string privatePath, string privateText)
    {
        Assert.Contains("AX unavailable: save, restart --legacy-page",
            shell.CanvasStatus);
        Assert.Contains("--legacy-page", shell.CanvasStatus);
        Assert.Contains("AX unavailable: save, restart --legacy-page", shell.StatusNotice);
        Assert.Single(shell.CanvasStatus!.Split("AX unavailable: save, restart --legacy-page").Skip(1));
        Assert.DoesNotContain("(experimental)", shell.CanvasStatus);
        Assert.DoesNotContain("Accessibility provider unavailable", shell.CanvasStatus);
        Assert.DoesNotContain(privatePath, shell.CanvasStatus, StringComparison.Ordinal);
        Assert.DoesNotContain(privateText, shell.CanvasStatus, StringComparison.Ordinal);
    }
}
