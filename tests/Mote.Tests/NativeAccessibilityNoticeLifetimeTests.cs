using Mote.Engine;
using Mote.Native;
using Mote.Themes;

namespace Mote.Tests;

/// <summary>AX health survives replacement of ordinary native analysis status.</summary>
public sealed partial class NativeControllerTests
{
    /// <summary>Attachment and runtime faults keep exactly one warning through analysis, themes and document swaps.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Accessibility_notice_survives_native_status_replacement(bool continuous, bool attachFailure)
    {
        using var temp = new RepoTemp();
        var path = temp.File("private-health.txt");
        const string source = "private health marker";
        File.WriteAllText(path, source);
        var config = TestConfiguration(temp.Path) with { ThemeId = ThemePolicies.SystemId };
        var shell = new FakeShell(NativeLineEndingMode.Preserve)
        {
            CanvasEnabled = true, RejectAccessibilityAttach = attachFailure
        };
        var rejectReload = false;
        using var controller = new NativeEditorController(shell, config,
            ThemePolicies.Resolve(ThemePolicies.SystemId, shell.PrefersDark), null,
            continuous ? EditorPresentationProfile.Continuous : null,
            settingsLoader: () => rejectReload ? throw new IOException("private loader failure") : config);
        controller.Run();
        if (!attachFailure) { shell.FailCanvasAccessibility(); shell.Pump(); }
        var warning = continuous ? "AX unavailable: save, restart --legacy-page" :
            "Accessibility provider unavailable";
        CheckWarning();
        Ready();
        CheckWarning();

        shell.RequestNew();
        Assert.Contains("analyzing", shell.Analysis!.Status);
        CheckWarning(); // Synchronous pending publication previously erased the warning.
        Ready();
        CheckWarning();

        shell.OpenPath = path;
        shell.RequestOpen();
        PumpOverride(shell, () => shell.CanvasBinding?.Snapshot.GetText() == source);
        CheckWarning();
        Ready();
        CheckWarning();

        // Exercise the shell contract with deferred/error publications without injecting parser faults.
        var readyView = shell.Analysis!;
        foreach (var status in new[] { "Analysis deferred", "Semantic analysis unavailable" })
        {
            shell.SetAnalysis(readyView with { Status = status });
            Assert.StartsWith(status, shell.CanvasStatus);
            CheckWarning();
        }
        shell.SetAnalysis(readyView);
        var retained = shell.Analysis;
        shell.ChangeAppearance(!shell.PrefersDark);
        Assert.Equal(retained!.Status, shell.Analysis!.Status);
        Assert.Equal(retained.Stamp, shell.Analysis.Stamp); // Replay changes presentation sequence, not document identity.
        CheckWarning();
        shell.RejectThemeId = shell.PrefersDark ? ThemePolicies.LightId : ThemePolicies.DarkId;
        shell.ChangeAppearance(!shell.PrefersDark);
        shell.Pump();
        Assert.Contains("Theme update unavailable", shell.StatusNotice);
        CheckWarning();

        rejectReload = true;
        controller.RequestSettingsReload();
        PumpOverride(shell, () => shell.StatusNotice?.Contains("Settings reload failed") == true);
        CheckWarning();
        rejectReload = false;
        controller.RequestSettingsReload();
        PumpOverride(shell, () => shell.StatusNotice?.Contains("Settings reload failed") == false);
        CheckWarning();

        var binding = shell.CanvasBinding!;
        shell.CommitCanvasEdit(new CanvasCommittedEdit(binding.DocumentGeneration, binding.BaseVersion,
            binding.BindingNonce, new TextChange(0, 0, "X"), 1));
        CheckWarning();
        Ready();
        CheckWarning();
        shell.RequestUndo();
        Assert.Equal(source, shell.CanvasBinding!.Snapshot.GetText());
        CheckWarning();
        Assert.Equal(source, File.ReadAllText(path));
        Assert.Empty(shell.Errors);

        void Ready() => PumpOverride(shell, () => shell.Analysis?.Status.Contains(
            $"v{shell.CanvasBinding!.BaseVersion}") == true);

        void CheckWarning()
        {
            Assert.Contains(warning, shell.StatusNotice);
            Assert.Equal(1, shell.CanvasStatus!.Split(warning, StringSplitOptions.None).Length - 1);
            Assert.DoesNotContain(path, shell.CanvasStatus, StringComparison.Ordinal);
            Assert.DoesNotContain(source, shell.CanvasStatus, StringComparison.Ordinal);
            Assert.DoesNotContain("private loader failure", shell.CanvasStatus, StringComparison.Ordinal);
        }
    }

    /// <summary>A fault during preedit publishes only on composition settlement and never forces a commit.</summary>
    [Fact]
    public void Accessibility_notice_waits_for_composition_without_committing_input()
    {
        using var temp = new RepoTemp();
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = true };
        using var controller = NewController(shell, temp.Path, null);
        controller.Run();
        var commits = shell.CommitCalls;
        shell.IsTextComposing = true;
        shell.FailCanvasAccessibility();
        shell.Pump();
        Assert.Null(shell.StatusNotice);
        Assert.Equal(commits, shell.CommitCalls);
        shell.IsTextComposing = false;
        shell.SettleComposition();
        Assert.Contains("Accessibility provider unavailable", shell.StatusNotice);
        Assert.Equal(commits, shell.CommitCalls);
    }
}
