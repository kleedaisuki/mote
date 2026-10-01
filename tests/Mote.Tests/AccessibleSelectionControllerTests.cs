using Mote.Engine;
using Mote.Native;
using Mote.Native.Accessibility;

namespace Mote.Tests;

/// <summary>Exercises the optional source-selection bridge with the shared fake UI shell.</summary>
public sealed partial class NativeControllerTests
{
    /// <summary>Selection publishes global endpoints without changing source, history, or focus.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 7)]
    [InlineData(7, 7)]
    public void Accessible_selection_publishes_without_edit_or_focus(int start, int end)
    {
        using var temp = new RepoTemp();
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = true };
        using var controller = NewController(shell, temp.Path, null);
        controller.Run();
        SeedAccessibleSelection(shell, "abcdefg");
        var accessible = shell.CanvasAccessibilityDocument!;
        var range = accessible.MakeRange(start, end);
        var before = shell.CanvasBinding!.Snapshot;

        Assert.Equal(AccessibleSelectionResult.Selected, controller.TrySelect(range));
        Assert.Equal(range, accessible.Selection);
        Assert.Equal((start, end),
            (shell.CanvasFrame!.SelectionAnchor, shell.CanvasFrame.SelectionActive));
        Assert.Same(before, shell.CanvasBinding!.Snapshot);
        Assert.Equal(0, shell.FocusSourceCount);
        Assert.Equal(0, shell.CommitCalls);
        shell.RequestUndo();
        Assert.Equal("", shell.CanvasBinding!.Snapshot.GetText());
        shell.RequestRedo();
        Assert.Equal("abcdefg", shell.CanvasBinding!.Snapshot.GetText());
    }

    /// <summary>Both native composition sources veto every selection and viewport mutation.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Accessible_selection_rejects_composition_without_mutation(bool text, bool canvas)
    {
        using var temp = new RepoTemp();
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = true };
        using var controller = NewController(shell, temp.Path, null);
        controller.Run();
        SeedAccessibleSelection(shell, "abcdefg");
        var accessible = shell.CanvasAccessibilityDocument!;
        var before = shell.CanvasFrame;
        var selection = accessible.Selection;
        shell.IsTextComposing = text;
        shell.IsCanvasComposing = canvas;

        Assert.Equal(AccessibleSelectionResult.CompositionBlocked,
            controller.TrySelect(accessible.MakeRange(1, 3)));
        Assert.Same(before, shell.CanvasFrame);
        Assert.Equal(selection, accessible.Selection);
        Assert.Equal(0, shell.CommitCalls);
        Assert.Equal(0, shell.FocusSourceCount);
    }

    /// <summary>Each endpoint independently rejects surrogate and CRLF interiors.</summary>
    [Theory]
    [InlineData(2, 4)]
    [InlineData(0, 2)]
    [InlineData(5, 6)]
    [InlineData(0, 5)]
    public void Accessible_selection_rejects_unsafe_boundaries(int start, int end)
    {
        using var temp = new RepoTemp();
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = true };
        using var controller = NewController(shell, temp.Path, null);
        controller.Run();
        SeedAccessibleSelection(shell, "a😀b\r\nz");
        var accessible = shell.CanvasAccessibilityDocument!;
        var before = shell.CanvasFrame;
        Assert.Equal(AccessibleSelectionResult.InvalidBoundary,
            controller.TrySelect(accessible.MakeRange(start, end)));
        Assert.Same(before, shell.CanvasFrame);
    }

    /// <summary>Stale identity and malformed absolute bounds never move the selection.</summary>
    [Fact]
    public void Accessible_selection_rejects_stale_identity_and_bounds()
    {
        using var temp = new RepoTemp();
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = true };
        using var controller = NewController(shell, temp.Path, null);
        controller.Run();
        var staleVersion = shell.CanvasAccessibilityDocument!.DocumentRange;
        SeedAccessibleSelection(shell, "abcdefg");
        var range = shell.CanvasAccessibilityDocument!.DocumentRange;
        var before = shell.CanvasFrame;
        AccessibleRange[] rejected = [staleVersion, range with { Generation = range.Generation + 1 },
            range with { Start = -1 }, range with { Start = 5, End = 4 }, range with { End = 8 }];
        foreach (var invalid in rejected)
            Assert.Equal(AccessibleSelectionResult.StaleRange, controller.TrySelect(invalid));
        Assert.Same(before, shell.CanvasFrame);
        shell.RequestNew();
        Assert.Equal(AccessibleSelectionResult.StaleRange, controller.TrySelect(range));
        controller.Dispose();
        Assert.Equal(AccessibleSelectionResult.StaleRange, controller.TrySelect(range));
    }

    /// <summary>Off-thread requests return immediately without dispatching to the UI queue.</summary>
    [Fact]
    public void Accessible_selection_rejects_other_thread_without_post_wait()
    {
        using var temp = new RepoTemp();
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = true };
        using var controller = NewController(shell, temp.Path, null);
        controller.Run();
        SeedAccessibleSelection(shell, "abcdefg");
        var range = shell.CanvasAccessibilityDocument!.MakeRange(1, 3);
        var before = shell.CanvasFrame;
        AccessibleSelectionResult? result = null;
        var thread = new Thread(() => result = controller.TrySelect(range));
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Equal(AccessibleSelectionResult.WrongThread, result);
        shell.Pump();
        Assert.Same(before, shell.CanvasFrame);
    }

    /// <summary>An offscreen active endpoint is revealed while the global range remains exact.</summary>
    [Fact]
    public void Accessible_selection_reveals_offscreen_endpoint_without_truncating_range()
    {
        using var temp = new RepoTemp();
        var shell = new FakeShell(NativeLineEndingMode.Preserve) { CanvasEnabled = true };
        using var controller = NewController(shell, temp.Path, null);
        controller.Run();
        var source = string.Concat(Enumerable.Repeat("abcdef\n", 20_000));
        SeedAccessibleSelection(shell, source);
        var accessible = shell.CanvasAccessibilityDocument!;
        Assert.Equal(AccessibleSelectionResult.Selected,
            controller.TrySelect(accessible.MakeRange(0, 0)));
        var before = shell.CanvasFrame!;
        Assert.Equal(AccessibleSelectionResult.Selected,
            controller.TrySelect(accessible.DocumentRange));
        Assert.Equal(accessible.DocumentRange, accessible.Selection);
        Assert.Equal(source.Length, shell.CanvasFrame!.SelectionActive);
        Assert.True(shell.CanvasFrame.TopAnchor.SourceOffset > before.TopAnchor.SourceOffset);
        Assert.Equal(source, shell.CanvasBinding!.Snapshot.GetText());
        Assert.Equal(0, shell.FocusSourceCount);
    }

    /// <summary>A legacy page controller cannot accidentally offer a canvas selection bridge.</summary>
    [Fact]
    public void Accessible_selection_rejects_legacy_presentation()
    {
        using var temp = new RepoTemp();
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        using var controller = NewController(shell, temp.Path, null);
        controller.Run();
        Assert.Equal(AccessibleSelectionResult.StaleRange,
            controller.TrySelect(new AccessibleRange(1, 0, 0, 0)));
        Assert.Equal(0, shell.FocusSourceCount);
    }

    /// <summary>Seeds source with one canonical edit whose history must survive selection.</summary>
    private static void SeedAccessibleSelection(FakeShell shell, string source)
    {
        var binding = shell.CanvasBinding!;
        shell.CommitCanvasEdit(new CanvasCommittedEdit(binding.DocumentGeneration,
            binding.BaseVersion, binding.BindingNonce, new TextChange(0, 0, source), source.Length));
    }
}
