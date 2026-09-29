using Mote.Configuration;
using Mote.Engine;
using Mote.Native;
using Mote.Themes;

namespace Mote.Tests;

/// <summary>Controller-level theme transitions keep the engine independent of OS appearance.</summary>
public sealed partial class NativeControllerTests
{
    /// <summary>System appearance changes exactly once per effective policy transition.</summary>
    [Fact]
    public void Runtime_system_theme_tracks_dark_light_dark_without_redundant_apply()
    {
        using var temp = new RepoTemp();
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { PrefersDark = true };
        using var controller = NewThemeController(shell, temp, ThemePolicies.SystemId);
        controller.Run();
        var first = shell.Theme;
        Assert.Equal(ThemePolicies.DarkId, first?.Id);
        Assert.Equal([ThemePolicies.DarkId], shell.ThemeAttempts);

        shell.ChangeAppearance(true);
        Assert.Single(shell.ThemeAttempts);
        shell.ChangeAppearance(false);
        Assert.Equal(ThemePolicies.LightId, shell.Theme?.Id);
        shell.ChangeAppearance(false);
        Assert.Equal(2, shell.ThemeAttempts.Count);
        shell.ChangeAppearance(true);
        Assert.Same(first, shell.Theme);
        Assert.Equal([ThemePolicies.DarkId, ThemePolicies.LightId, ThemePolicies.DarkId],
            shell.ThemeAttempts);
        Assert.Empty(shell.Errors);
    }

    /// <summary>Unknown IDs use the OS fallback while explicit IDs ignore subsequent OS changes.</summary>
    [Theory]
    [InlineData("mote-drak", ThemePolicies.DarkId, ThemePolicies.LightId)]
    [InlineData(ThemePolicies.HighContrastDarkId,
        ThemePolicies.HighContrastDarkId, ThemePolicies.HighContrastDarkId)]
    [InlineData(ThemePolicies.LightId, ThemePolicies.LightId, ThemePolicies.LightId)]
    public void Runtime_theme_resolution_preserves_explicit_override_or_unknown_fallback(
        string configured, string initially, string afterLight)
    {
        using var temp = new RepoTemp();
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { PrefersDark = true };
        using var controller = NewThemeController(shell, temp, configured);
        controller.Run();
        Assert.Equal(initially, shell.Theme?.Id);

        shell.ChangeAppearance(false);
        Assert.Equal(afterLight, shell.Theme?.Id);
        Assert.Equal(initially == afterLight ? 1 : 2, shell.ThemeAttempts.Count);
        shell.ChangeAppearance(true);
        Assert.Equal(initially, shell.Theme?.Id);
        Assert.Equal(initially == afterLight ? 1 : 3, shell.ThemeAttempts.Count);
    }

    /// <summary>Preedit defers palette mutation; a return to the old OS color cancels the pending change.</summary>
    [Fact]
    public void Runtime_composition_coalesces_and_cancels_obsolete_appearance()
    {
        using var temp = new RepoTemp();
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { PrefersDark = true };
        using var controller = NewThemeController(shell, temp, ThemePolicies.SystemId);
        controller.Run();
        shell.IsTextComposing = true;

        shell.ChangeAppearance(false);
        shell.ChangeAppearance(true);
        Assert.Equal(ThemePolicies.DarkId, shell.Theme?.Id);
        Assert.Single(shell.ThemeAttempts);

        shell.IsTextComposing = false;
        shell.SettleComposition();
        Assert.Single(shell.ThemeAttempts);
        Assert.Empty(shell.Errors);
    }

    /// <summary>Either native IME completion path applies only the last deferred OS palette.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Runtime_composition_settlement_applies_latest_theme_once(bool commitsText)
    {
        using var temp = new RepoTemp();
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { PrefersDark = true };
        using var controller = NewThemeController(shell, temp, ThemePolicies.SystemId);
        controller.Run();
        shell.IsTextComposing = true;
        shell.ChangeAppearance(false);
        shell.ChangeAppearance(true);
        shell.ChangeAppearance(false);
        Assert.Single(shell.ThemeAttempts);

        if (commitsText) shell.Edit("中");
        var beforeSettle = shell.Document;
        shell.IsTextComposing = false;
        shell.SettleComposition();
        Assert.Equal(ThemePolicies.LightId, shell.Theme?.Id);
        Assert.Equal([ThemePolicies.DarkId, ThemePolicies.LightId], shell.ThemeAttempts);
        Assert.Same(beforeSettle, shell.Document);
        Assert.Equal(commitsText ? "中" : "", shell.Document?.Text);
        shell.SettleComposition();
        Assert.Equal(2, shell.ThemeAttempts.Count);
    }

    /// <summary>A nested OS callback is re-resolved after the native palette transaction commits.</summary>
    [Fact]
    public void Runtime_reentrant_appearance_during_settheme_finishes_at_latest_os_policy()
    {
        using var temp = new RepoTemp();
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { PrefersDark = true };
        using var controller = NewThemeController(shell, temp, ThemePolicies.SystemId);
        controller.Run();
        shell.DuringThemeApply = theme =>
        {
            if (theme.Id != ThemePolicies.LightId) return;
            shell.DuringThemeApply = null;
            shell.ChangeAppearance(true);
        };

        shell.ChangeAppearance(false);
        shell.Pump();
        Assert.True(shell.PrefersDark);
        Assert.Equal(ThemePolicies.DarkId, shell.Theme?.Id);
        Assert.Equal([ThemePolicies.DarkId, ThemePolicies.LightId, ThemePolicies.DarkId],
            shell.ThemeAttempts);
        Assert.Empty(shell.Errors);
    }

    /// <summary>A late native IME start defers the whole palette and retries after final composition.</summary>
    [Fact]
    public void Runtime_late_composition_defers_without_partial_theme_or_false_warning()
    {
        using var temp = new RepoTemp();
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { PrefersDark = true };
        using var controller = NewThemeController(shell, temp, ThemePolicies.SystemId);
        controller.Run();
        var oldPolicy = shell.Theme;
        var document = shell.Document;
        var analysisCount = shell.Analyses.Count;
        shell.DeferNextThemeId = ThemePolicies.LightId;

        shell.ChangeAppearance(false);
        shell.Pump();
        Assert.True(shell.IsTextComposing);
        Assert.Same(oldPolicy, shell.Theme);
        Assert.Same(document, shell.Document);
        Assert.Equal(analysisCount, shell.Analyses.Count);
        Assert.Equal([ThemePolicies.DarkId, ThemePolicies.LightId], shell.ThemeAttempts);
        Assert.Null(shell.StatusNotice);
        Assert.Empty(shell.Errors);

        shell.IsTextComposing = false;
        shell.SettleComposition();
        shell.Pump();
        Assert.Equal(ThemePolicies.LightId, shell.Theme?.Id);
        Assert.Equal([ThemePolicies.DarkId, ThemePolicies.LightId, ThemePolicies.LightId],
            shell.ThemeAttempts);
        Assert.Same(document, shell.Document);
        Assert.Null(shell.StatusNotice);
        Assert.Empty(shell.Errors);
    }

    /// <summary>A native palette allocation failure rolls back and reports without touching source.</summary>
    [Fact]
    public void Runtime_failed_theme_apply_retains_old_policy_and_recovers_on_next_event()
    {
        using var temp = new RepoTemp();
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { PrefersDark = true };
        using var controller = NewThemeController(shell, temp, ThemePolicies.SystemId);
        controller.Run();
        shell.Edit("private marker");
        var document = shell.Document;
        var oldPolicy = shell.Theme;
        shell.RejectThemeId = ThemePolicies.LightId;

        shell.ChangeAppearance(false);
        shell.Pump();
        Assert.Same(oldPolicy, shell.Theme);
        Assert.Same(document, shell.Document);
        Assert.Equal("private marker", shell.Document?.Text);
        Assert.Contains("Theme update unavailable", shell.StatusNotice!);
        Assert.Equal([ThemePolicies.DarkId, ThemePolicies.LightId, ThemePolicies.DarkId],
            shell.ThemeAttempts);
        Assert.DoesNotContain("private marker", shell.StatusNotice!, StringComparison.Ordinal);
        Assert.Empty(shell.Errors);

        shell.RejectThemeId = null;
        shell.ChangeAppearance(false);
        Assert.Equal(ThemePolicies.LightId, shell.Theme?.Id);
        Assert.Null(shell.StatusNotice);
        Assert.Equal("private marker", shell.Document?.Text);
        shell.RequestUndo();
        Assert.Equal("", shell.Document?.Text);
    }

    /// <summary>A recoverable palette warning waits until preedit is fully committed or canceled.</summary>
    [Fact]
    public void Runtime_failed_theme_warning_does_not_interrupt_unsettled_preedit()
    {
        using var temp = new RepoTemp();
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { PrefersDark = true };
        using var controller = NewThemeController(shell, temp, ThemePolicies.SystemId);
        controller.Run();
        shell.IsTextComposing = true;
        shell.RejectThemeId = ThemePolicies.LightId;
        var before = shell.Document;

        shell.ChangeAppearance(false);
        shell.Pump();
        Assert.Same(before, shell.Document);
        Assert.Equal(ThemePolicies.DarkId, shell.Theme?.Id);
        Assert.Single(shell.ThemeAttempts);
        Assert.Empty(shell.StatusNotices);
        Assert.Empty(shell.Errors);

        shell.IsTextComposing = false;
        shell.SettleComposition();
        shell.Pump();
        Assert.Equal(ThemePolicies.DarkId, shell.Theme?.Id);
        Assert.Contains("Theme update unavailable", shell.StatusNotice!);
        Assert.NotEmpty(shell.StatusNotices);
        Assert.Same(before, shell.Document);
        Assert.Empty(shell.Errors);
    }

    /// <summary>Even a failed palette update changes only status chrome, not the ordinary text page.</summary>
    [Fact]
    public void Runtime_theme_success_and_failure_preserve_default_page_selection_and_history()
    {
        using var temp = new RepoTemp();
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { PrefersDark = true };
        using var controller = NewThemeController(shell, temp, ThemePolicies.SystemId);
        controller.Run();
        shell.Edit("alpha\nbeta");
        shell.ChangeSelection(2, 8);
        var page = shell.Document;
        var selection = shell.DisplaySelection;
        var pageCalls = shell.DocumentSetCount;

        shell.ChangeAppearance(false);
        shell.ChangeAppearance(true);
        Assert.Equal(ThemePolicies.DarkId, shell.Theme?.Id);
        Assert.Same(page, shell.Document);
        Assert.Equal(selection, shell.DisplaySelection);
        Assert.Equal(pageCalls, shell.DocumentSetCount);

        shell.RejectThemeId = ThemePolicies.LightId;
        shell.ChangeAppearance(false);
        shell.Pump();
        Assert.Equal(ThemePolicies.DarkId, shell.Theme?.Id);
        Assert.Same(page, shell.Document);
        Assert.Equal(selection, shell.DisplaySelection);
        Assert.Equal(pageCalls, shell.DocumentSetCount);
        Assert.Contains("Theme update unavailable", shell.StatusNotice!);
        Assert.Empty(shell.Errors);

        shell.RequestUndo();
        Assert.Equal("", shell.Document?.Text);
        shell.RequestRedo();
        Assert.Equal("alpha\nbeta", shell.Document?.Text);
    }

    /// <summary>Palette changes do not reproject a canvas or perturb global source history and selection.</summary>
    [Fact]
    public void Runtime_theme_change_preserves_canvas_snapshot_selection_viewport_and_undo()
    {
        using var temp = new RepoTemp();
        var shell = new FakeShell(NativeLineEndingMode.Preserve)
        {
            CanvasEnabled = true,
            PrefersDark = true
        };
        using var controller = NewThemeController(shell, temp, ThemePolicies.SystemId);
        controller.Run();
        var initial = shell.CanvasBinding!;
        shell.CommitCanvasEdit(new CanvasCommittedEdit(initial.DocumentGeneration,
            initial.BaseVersion, initial.BindingNonce, new TextChange(0, 0, "abc\nline"), 8));
        Assert.Empty(shell.Errors);
        shell.SelectCanvas(1, 3);
        var before = shell.CanvasBinding!;
        var frame = shell.CanvasFrame!;
        var version = before.Snapshot.Version;
        var source = before.Snapshot.GetText();
        var selection = (frame.SelectionAnchor, frame.SelectionActive);
        var top = frame.TopAnchor;
        var horizontal = frame.Horizontal;
        var analysisCount = shell.Analyses.Count;

        shell.ChangeAppearance(false);
        Assert.Equal(ThemePolicies.LightId, shell.Theme?.Id);
        Assert.Same(before, shell.CanvasBinding);
        Assert.Same(frame, shell.CanvasFrame);
        Assert.Equal(version, shell.CanvasBinding!.Snapshot.Version);
        Assert.Equal(source, shell.CanvasBinding.Snapshot.GetText());
        Assert.Equal(selection, (shell.CanvasFrame!.SelectionAnchor, shell.CanvasFrame.SelectionActive));
        Assert.Equal(top, shell.CanvasFrame.TopAnchor);
        Assert.Equal(horizontal, shell.CanvasFrame.Horizontal);
        Assert.Equal(analysisCount, shell.Analyses.Count);
        shell.RequestUndo();
        Assert.Equal("", shell.CanvasBinding!.Snapshot.GetText());
        shell.RequestRedo();
        Assert.Equal(source, shell.CanvasBinding!.Snapshot.GetText());
        Assert.Empty(shell.Errors);
    }

    /// <summary>Loads one isolated user preference without reading machine/user profile state.</summary>
    private static NativeEditorController NewThemeController(
        FakeShell shell, RepoTemp temp, string themeId)
    {
        var home = temp.File("theme-home");
        Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "config.toml"),
            $"[appearance]\ntheme = '{themeId}'\n");
        var config = MoteConfigLoader.Load(new MoteConfigLoadOptions
        {
            UserHomeDirectory = temp.Path,
            MoteHomeOverride = home,
            UseEnvironmentOverride = false
        });
        Assert.Equal(themeId, config.ThemeId);
        return new NativeEditorController(shell, config,
            ThemePolicies.Resolve(config.ThemeId, shell.PrefersDark), null);
    }
}
