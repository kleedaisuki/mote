using System.Collections.Concurrent;
using Mote.Configuration;
using Mote.Native;
using Mote.Themes;

namespace Mote.Tests;

/// <summary>Override reload transactions preserve canonical source and native preedit.</summary>
public sealed partial class NativeControllerTests
{
    /// <summary>Same ID/different values apply, equal values do not; Undo stays an engine operation.</summary>
    [Fact]
    public void Theme_override_reload_uses_values_and_preserves_source()
    {
        using var temp = new RepoTemp();
        var initial = OverrideConfig(temp, "#181818");
        var next = initial with { ThemeOverrides = OverrideColors("#101010") };
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = new NativeEditorController(shell, initial, ThemePolicies.Get(initial.ThemeId),
            null, settingsLoader: () => next);
        controller.Run();
        shell.Edit("private source");
        var document = shell.Document;
        Assert.Equal(new ThemeColor(0x18, 0x18, 0x18), shell.Theme!.Palette.PreviewBackground);
        controller.RequestSettingsReload();
        PumpOverride(shell, () => shell.Theme!.Palette.PreviewBackground == new ThemeColor(0x10, 0x10, 0x10));
        Assert.Same(document, shell.Document);
        Assert.Equal(2, shell.ThemeAttempts.Count);
        var notices = shell.StatusNotices.Count;
        controller.RequestSettingsReload();
        PumpOverride(shell, () => shell.StatusNotices.Count > notices);
        Assert.Equal(2, shell.ThemeAttempts.Count);
        shell.RequestUndo();
        Assert.Equal("", shell.Document!.Text);
    }

    /// <summary>Natural composition settlement applies only the last requested map.</summary>
    [Fact]
    public void Theme_override_reload_defers_during_composition()
    {
        using var temp = new RepoTemp();
        var initial = OverrideConfig(temp, "#181818");
        var next = initial with { ThemeOverrides = OverrideColors("#101010") };
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = new NativeEditorController(shell, initial, ThemePolicies.Get(initial.ThemeId),
            null, settingsLoader: () => next);
        controller.Run();
        PumpOverride(shell, () => shell.Analysis is not null);
        shell.IsTextComposing = true;
        controller.RequestSettingsReload();
        var posted = shell.WaitForPostedAsync();
        Assert.True(SpinWait.SpinUntil(() => posted.IsCompleted, TimeSpan.FromSeconds(10)));
        Assert.True(posted.IsCompletedSuccessfully);
        shell.Pump();
        Assert.Single(shell.ThemeAttempts);
        shell.IsTextComposing = false;
        shell.SettleComposition();
        Assert.Equal(new ThemeColor(0x10, 0x10, 0x10), shell.Theme!.Palette.PreviewBackground);
        Assert.Equal(2, shell.ThemeAttempts.Count);
    }

    /// <summary>Rejected file and invalid maps retain the installed palette; missing restores conventions.</summary>
    [Fact]
    public void Theme_override_reload_rejection_then_deletion_is_transactional()
    {
        using var temp = new RepoTemp();
        var initial = OverrideConfig(temp, "#181818");
        var next = initial with { ReadDisposition = ConfigReadDisposition.Rejected };
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = new NativeEditorController(shell, initial, ThemePolicies.Get(initial.ThemeId),
            null, settingsLoader: () => next);
        controller.Run();
        controller.RequestSettingsReload();
        PumpOverride(shell, () => shell.StatusNotice?.Contains("reload rejected") == true);
        Assert.Single(shell.ThemeAttempts);
        next = initial with { ThemeOverrides = OverrideColors("#FFFFFF") };
        controller.RequestSettingsReload();
        PumpOverride(shell, () => shell.StatusNotice?.Contains("previous theme retained") == true);
        Assert.Single(shell.ThemeAttempts);
        next = initial with { ReadDisposition = ConfigReadDisposition.Missing, ThemeOverrides = ThemeOverrideData.Empty };
        controller.RequestSettingsReload();
        PumpOverride(shell, () => shell.ThemeAttempts.Count == 2);
        Assert.Equal(ThemePolicies.Get(initial.ThemeId).Palette, shell.Theme!.Palette);
    }

    /// <summary>A dark-only map remains requested and falls back to the actual new base on OS transition.</summary>
    [Fact]
    public void Theme_override_system_transition_recomposes_same_data()
    {
        using var temp = new RepoTemp();
        var initial = OverrideConfig(temp, "#181818") with { ThemeId = ThemePolicies.SystemId };
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { PrefersDark = true };
        using var controller = new NativeEditorController(shell, initial, ThemePolicies.Get(ThemePolicies.DarkId), null);
        controller.Run();
        shell.ChangeAppearance(false);
        Assert.Equal(ThemePolicies.Get(ThemePolicies.LightId).Palette, shell.Theme!.Palette);
        Assert.Contains("CONFIG_THEME_CONTRAST", shell.StatusNotice!);
        shell.ChangeAppearance(true);
        Assert.Equal(new ThemeColor(0x18, 0x18, 0x18), shell.Theme!.Palette.PreviewBackground);
        Assert.Null(shell.StatusNotice);
    }

    /// <summary>Reading conventional missing settings never creates a user data tree.</summary>
    [Fact]
    public void Theme_override_reload_missing_config_writes_nothing()
    {
        using var temp = new RepoTemp();
        var initial = OverrideConfig(temp, "#181818");
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = new NativeEditorController(shell, initial, ThemePolicies.Get(initial.ThemeId), null);
        controller.Run();
        controller.RequestSettingsReload();
        PumpOverride(shell, () => shell.ThemeAttempts.Count == 2);
        Assert.False(Directory.Exists(initial.HomeDirectory));
        Assert.False(File.Exists(initial.ConfigPath));
    }

    /// <summary>Nonvisual writer changes are explicitly next-launch, not a silent live sink relocation.</summary>
    [Fact]
    public void Theme_override_reload_reports_writer_lifetime()
    {
        using var temp = new RepoTemp();
        var initial = OverrideConfig(temp, "#181818");
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = new NativeEditorController(shell, initial, ThemePolicies.Get(initial.ThemeId), null,
            settingsLoader: () => initial with { TraceEnabled = true, TraceDirectory = temp.File("new-traces") });
        controller.Run();
        controller.RequestSettingsReload();
        PumpOverride(shell, () => shell.StatusNotice?.Contains("next launch") == true);
        Assert.Single(shell.ThemeAttempts);
        Assert.False(Directory.Exists(temp.File("new-traces")));
    }

    private static MoteConfiguration OverrideConfig(RepoTemp temp, string background) =>
        MoteConfigLoader.Load(new MoteConfigLoadOptions { UserHomeDirectory = temp.Path,
            MoteHomeOverride = temp.File("settings"), UseEnvironmentOverride = false }) with
        { ReadDisposition = ConfigReadDisposition.Loaded, ThemeOverrides = OverrideColors(background) };

    private static ThemeOverrideData OverrideColors(string background)
    {
        Assert.True(ThemeOverrideData.TryCreate([new("preview.background", background)], out var data, out _));
        return data;
    }

    private static void PumpOverride(FakeShell shell, Func<bool> condition) =>
        Assert.True(SpinWait.SpinUntil(() => { shell.Pump(); return condition(); }, TimeSpan.FromSeconds(10)));
}

/// <summary>Discriminating reload ordering tests do not rely on filesystem scheduling or timers.</summary>
public sealed class NativeThemeOverrideReloadTests
{
    /// <summary>Stale A is skipped; the only admitted completion is the coalesced latest B.</summary>
    [Fact]
    public void Reload_publishes_only_latest_read()
    {
        using var temp = new RepoTemp();
        var config = MoteConfigLoader.Load(new MoteConfigLoadOptions { UserHomeDirectory = temp.Path,
            UseEnvironmentOverride = false });
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var queue = new ConcurrentQueue<Action>();
        var reads = 0;
        MoteConfiguration? published = null;
        using var reload = new NativeSettingsReload(() =>
        {
            var read = Interlocked.Increment(ref reads);
            if (read == 1) { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); }
            return config with { ThemeId = read == 1 ? ThemePolicies.LightId : ThemePolicies.DarkId };
        }, queue.Enqueue, (value, error) => { Assert.Null(error); published = value; });
        reload.Request();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        reload.Request();
        release.Set();
        Assert.True(SpinWait.SpinUntil(() => queue.Count > 0, TimeSpan.FromSeconds(10)));
        Assert.True(queue.TryDequeue(out var first));
        first();
        Assert.Null(published);
        Assert.True(SpinWait.SpinUntil(() => queue.Count > 0, TimeSpan.FromSeconds(10)));
        Assert.True(queue.TryDequeue(out var latest));
        latest();
        Assert.Equal(ThemePolicies.DarkId, published!.ThemeId);
        Assert.Equal(2, reads);
    }

    /// <summary>One slow A followed by many requests starts only B and never publishes A.</summary>
    [Fact]
    public void Reload_coalesces_stale_read_and_disposal()
    {
        using var temp = new RepoTemp();
        var config = MoteConfigLoader.Load(new MoteConfigLoadOptions { UserHomeDirectory = temp.Path,
            UseEnvironmentOverride = false });
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var queue = new ConcurrentQueue<Action>();
        var reads = 0;
        var publications = 0;
        using var reload = new NativeSettingsReload(() =>
        {
            if (Interlocked.Increment(ref reads) == 1) { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); }
            return config;
        }, queue.Enqueue, (_, _) => ++publications);
        reload.Request();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        for (var i = 0; i < 100; i++) reload.Request();
        release.Set();
        Assert.True(SpinWait.SpinUntil(() => queue.Count > 0, TimeSpan.FromSeconds(10)));
        Assert.True(queue.TryDequeue(out var first));
        first();
        Assert.Equal(0, publications);
        Assert.True(SpinWait.SpinUntil(() => queue.Count > 0, TimeSpan.FromSeconds(10)));
        Assert.True(queue.TryDequeue(out var latest));
        reload.Dispose();
        latest();
        Assert.Equal(0, publications);
        Assert.Equal(2, reads);
    }
}
