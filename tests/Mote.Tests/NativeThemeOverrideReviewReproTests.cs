using Mote.Configuration;
using Mote.Native;
using Mote.Themes;

namespace Mote.Tests;

/// <summary>Independent injected-failure evidence for settings reload publication contracts.</summary>
public sealed partial class NativeControllerTests
{
    /// <summary>A newer read invalidates a staged preedit snapshot before the newer read completes.</summary>
    [Fact]
    public void Review_new_request_invalidates_composition_deferred_settings()
    {
        using var temp = new RepoTemp();
        var initial = OverrideConfig(temp, "#181818");
        var first = initial with { ThemeOverrides = OverrideColors("#101010") };
        var second = initial with { ThemeOverrides = OverrideColors("#202020") };
        using var secondEntered = new ManualResetEventSlim();
        using var releaseSecond = new ManualResetEventSlim();
        var reads = 0;
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = new NativeEditorController(shell, initial, ThemePolicies.Get(initial.ThemeId),
            null, settingsLoader: () =>
            {
                if (Interlocked.Increment(ref reads) == 1) return first;
                secondEntered.Set();
                Assert.True(releaseSecond.Wait(TimeSpan.FromSeconds(10)));
                return second;
            });
        controller.Run();
        PumpOverride(shell, () => shell.Analysis is not null);
        shell.IsTextComposing = true;
        controller.RequestSettingsReload();
        var posted = shell.WaitForPostedAsync();
        Assert.True(SpinWait.SpinUntil(() => posted.IsCompleted, TimeSpan.FromSeconds(10)));
        shell.Pump();
        controller.RequestSettingsReload();
        try
        {
            Assert.True(secondEntered.Wait(TimeSpan.FromSeconds(10)));
            shell.IsTextComposing = false;
            shell.SettleComposition();
            Assert.Single(shell.ThemeAttempts);
            Assert.Equal(new ThemeColor(0x18, 0x18, 0x18), shell.Theme!.Palette.PreviewBackground);
        }
        finally { releaseSecond.Set(); }
        PumpOverride(shell, () => shell.Theme!.Palette.PreviewBackground == new ThemeColor(0x20, 0x20, 0x20));
        Assert.Equal(2, shell.ThemeAttempts.Count);
    }

    /// <summary>A failed read stays visible through an unrelated OS appearance transition.</summary>
    [Fact]
    public void Review_rejected_reload_notice_survives_appearance_change()
    {
        using var temp = new RepoTemp();
        var initial = OverrideConfig(temp, "#181818") with { ThemeId = ThemePolicies.SystemId };
        var rejected = initial with { ReadDisposition = ConfigReadDisposition.Rejected };
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { PrefersDark = true };
        using var controller = new NativeEditorController(shell, initial, ThemePolicies.Get(ThemePolicies.DarkId),
            null, settingsLoader: () => rejected);
        controller.Run();
        controller.RequestSettingsReload();
        PumpOverride(shell, () => shell.StatusNotice?.Contains("reload rejected") == true);
        shell.ChangeAppearance(false);
        Assert.Contains("reload rejected", shell.StatusNotice ?? "");
        shell.ChangeAppearance(true);
        Assert.Contains("reload rejected", shell.StatusNotice ?? "");
    }

    /// <summary>A partial native layout install is rolled back, not merely its configuration preference.</summary>
    [Fact]
    public void Review_failed_preview_reload_reinstalls_previous_native_presentation()
    {
        using var temp = new RepoTemp();
        var initial = OverrideConfig(temp, "#181818") with { PreviewLayout = PreviewLayoutPreference.SourceOnly };
        var next = initial with { PreviewLayout = PreviewLayoutPreference.Split };
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = new NativeEditorController(shell, initial, ThemePolicies.Get(initial.ThemeId),
            null, settingsLoader: () => next);
        controller.Run();
        PumpOverride(shell, () => shell.Analysis is not null);
        Assert.False(shell.Analysis!.ShowPreview);
        var failedOnce = false;
        shell.DuringAnalysisApply = view =>
        {
            if (!view.ShowPreview || failedOnce) return;
            failedOnce = true;
            throw new InvalidOperationException("Injected failure after native pane visibility changed.");
        };
        controller.RequestSettingsReload();
        PumpOverride(shell, () => shell.StatusNotice?.Contains("Preview layout update unavailable") == true);
        Assert.True(failedOnce);
        Assert.False(shell.Analysis!.ShowPreview);
        shell.ChangeAppearance(false);
        Assert.Contains("Preview layout update unavailable", shell.StatusNotice ?? "");
    }
}
