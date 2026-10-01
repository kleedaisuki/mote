using System.Globalization;
using Mote.Engine;
using Mote.Native;
using Mote.Native.Mac.Canvas;
using Mote.Native.Viewport;

namespace Mote.Tests;

/// <summary>Pure ribbon/source projection checks; no AppKit object or native host is created.</summary>
public sealed class MacCanvasRibbonContextTests
{
    /// <summary>A retained input interval must not retain the displayed caret offset.</summary>
    [Fact]
    public void Retained_binding_uses_current_frame_for_label_and_selection()
    {
        using var document = new Document(new string('x', 2_000));
        var original = Frame(document, 1_200, 1_200);
        var binding = Bind(document, original, 1_190, "abcdefghij01234567890");
        var moved = original with { SelectionAnchor = 1_207, SelectionActive = 1_207 };

        AssertContext(binding, original, "Input @ 1,200", 10, 0);
        AssertContext(binding, moved, "Input @ 1,207", 17, 0);
        Assert.Equal(1_200, binding.Active);
        Assert.Equal(original, binding.Frame);
        // The pre-fix binding.Active label cannot satisfy the moved-frame assertion.
        Assert.NotEqual("Input @ 1,207", $"Input @ {binding.Active.ToString("N0", CultureInfo.InvariantCulture)}");
    }

    /// <summary>Expected offsets are explicit intersections with the retained [100,110] interval.</summary>
    [Theory]
    [InlineData(102, 108, 2, 6)]
    [InlineData(108, 102, 2, 6)]
    [InlineData(95, 105, 0, 5)]
    [InlineData(105, 95, 0, 5)]
    [InlineData(105, 115, 5, 5)]
    [InlineData(115, 105, 5, 5)]
    [InlineData(90, 120, 0, 10)]
    [InlineData(120, 90, 0, 10)]
    [InlineData(80, 90, 0, 0)]
    [InlineData(130, 120, 10, 0)]
    [InlineData(100, 100, 0, 0)]
    [InlineData(110, 110, 10, 0)]
    public void Selection_projects_exact_forward_reverse_partial_and_outside_ranges(
        int anchor, int active, int location, int length)
    {
        using var document = new Document(new string('x', 200));
        var original = Frame(document, 100, 100);
        var binding = Bind(document, original, 100, "0123456789");
        var current = original with { SelectionAnchor = anchor, SelectionActive = active };

        AssertContext(binding, current, $"Input @ {active}", location, length);
    }

    /// <summary>Missing or mismatched current state cannot publish a replacement label or selection.</summary>
    [Theory]
    [InlineData("binding")]
    [InlineData("frame")]
    [InlineData("both")]
    [InlineData("stale")]
    public void Unavailable_context_returns_empty_default_outputs(string missing)
    {
        using var document = new Document("abc");
        var frame = Frame(document, 1, 1);
        var binding = Bind(document, frame, 0, "abc");
        if (missing == "stale") frame = frame with { Version = frame.Version + 1 };

#pragma warning disable CA1416 // This static helper only formats strings and projects integer offsets.
        var available = MacTextInputIsland.TryGetRibbonContext(
            missing is "binding" or "both" ? null : binding,
            missing is "frame" or "both" ? null : frame,
            out var label, out var selection);
        Assert.False(available);
        Assert.Equal(string.Empty, label);
        Assert.Equal((nuint)0, selection.Location);
        Assert.Equal((nuint)0, selection.Length);
#pragma warning restore CA1416
    }

    /// <summary>Ribbon grouping stays invariant without changing process-wide default culture.</summary>
    [Fact]
    public void Label_uses_invariant_grouping_under_non_English_current_culture()
    {
        using var document = new Document(new string('x', 2_000));
        var frame = Frame(document, 1_234, 1_234);
        var binding = Bind(document, frame, 1_230, "0123456789");
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("1.234", 1_234.ToString("N0"));
            AssertContext(binding, frame, "Input @ 1,234", 4, 0);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>Calls only the production pure boundary, never the native island constructor.</summary>
    private static void AssertContext(NativeCanvasBinding binding, CanvasFrame frame,
        string expectedLabel, int expectedLocation, int expectedLength)
    {
#pragma warning disable CA1416 // Pure arithmetic/string helper; no native entry point is exercised.
        Assert.True(MacTextInputIsland.TryGetRibbonContext(binding, frame, out var label, out var selection));
        Assert.Equal(expectedLabel, label);
        Assert.Equal((nuint)expectedLocation, selection.Location);
        Assert.Equal((nuint)expectedLength, selection.Length);
#pragma warning restore CA1416
    }

    /// <summary>Constructs a same-version frame independently of native geometry or rendering.</summary>
    private static CanvasFrame Frame(Document document, int anchor, int active) =>
        new(document.Snapshot.Version, new ViewportAnchor(0, 0), 0, [], anchor, active);

    /// <summary>Captures the original frame and caret, permitting later same-interval movement.</summary>
    private static NativeCanvasBinding Bind(Document document, CanvasFrame frame, int start, string text) =>
        new(1, document.Snapshot.Version, 1, document.Snapshot, frame, start,
            document.Snapshot.GetText(start, text.Length),
            frame.SelectionAnchor, frame.SelectionActive, "", "", false);
}
