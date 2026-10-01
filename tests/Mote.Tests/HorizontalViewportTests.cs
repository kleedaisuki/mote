using Mote.Engine;
using Mote.Native.Viewport;

namespace Mote.Tests;

/// <summary>Pure source-bound horizontal viewport checks, independent of pixel shaping.</summary>
public sealed class HorizontalViewportTests
{
    /// <summary>A far caret in a 50 Mi-unit line yields bounded row windows, never a copied prefix.</summary>
    [Fact]
    public void Far_long_line_anchor_and_reveal_remain_bounded_without_claiming_pixel_visibility()
    {
        const int lineLength = 50 * 1024 * 1024;
        using var document = new Document(new string('a', lineLength) + "\ntail");
        var canvas = new CanvasInteraction(document.Snapshot, 20, 200, maxSliceLength: 4096);
        const int target = 40 * 1024 * 1024;
        canvas.SetHorizontalAnchor(target, HorizontalCaretAffinity.Trailing, 3.25);
        var frame = canvas.Frame();

        Assert.Equal(document.Snapshot.Version, frame.Version);
        Assert.Equal(target, frame.Horizontal.SourceBoundary);
        Assert.Equal(HorizontalCaretAffinity.Trailing, frame.Horizontal.Affinity);
        Assert.Equal(frame.RowWindows.Select(row => row.Slice), frame.Slices);
        Assert.Contains(frame.RowWindows, row => row.LeftEdgeSourceBoundary == target &&
            row.Slice.SourceStart <= target && target <= row.Slice.SourceStart + row.Slice.SourceLength);
        Assert.All(frame.RowWindows, row => Assert.InRange(row.Slice.SourceLength, 0, 4096));
        Assert.Equal(HorizontalRevealStatus.NeedsPixelVisibilityProof, canvas.Reveal(target));

        const int later = 45 * 1024 * 1024;
        Assert.Equal(HorizontalRevealStatus.SourceWindowMoved, canvas.Reveal(later));
        var moved = canvas.Frame();
        Assert.Equal(later, moved.Horizontal.SourceBoundary);
        Assert.Contains(moved.RowWindows, row => row.LeftEdgeSourceBoundary == later);
        Assert.Equal(document.Snapshot.Version, moved.Version);
    }

    /// <summary>Vertical scrolling projects a stable source column into shorter rows and back.</summary>
    [Fact]
    public void Vertical_scroll_preserves_reference_column_and_short_line_fallback()
    {
        using var document = new Document("abcdefghij\nxy\n123456789012\n");
        var canvas = new CanvasInteraction(document.Snapshot, 20, 20, maxSliceLength: 16);
        canvas.SetHorizontalAnchor(5, HorizontalCaretAffinity.Trailing, 2.5, 42);
        var before = canvas.Frame();
        canvas.ScrollBy(20);
        var shortRow = canvas.Frame();
        Assert.Equal(before.Horizontal, shortRow.Horizontal);
        Assert.Equal(13, Assert.Single(shortRow.RowWindows).LeftEdgeSourceBoundary);
        Assert.Equal(0, shortRow.RowWindows[0].IntraClusterPixels);
        canvas.ScrollBy(20);
        var longRow = canvas.Frame();
        Assert.Equal(19, Assert.Single(longRow.RowWindows).LeftEdgeSourceBoundary);
        Assert.Equal(2.5, longRow.RowWindows[0].IntraClusterPixels);
        canvas.ScrollBy(-40);
        var returned = canvas.Frame();
        Assert.Equal(5, Assert.Single(returned.RowWindows).LeftEdgeSourceBoundary);
        Assert.Equal(before.Version, returned.Version);
        Assert.Equal(before.Horizontal, returned.Horizontal);
        Assert.Equal(returned.RowWindows.Select(row => row.Slice), returned.Slices);
    }

    /// <summary>Horizontal anchors cannot split source delimiters or scalar pairs; clamping is explicit.</summary>
    [Fact]
    public void Empty_crlf_and_surrogate_edges_are_checked_without_mutating_source()
    {
        using var document = new Document("a\r\n😀z\n");
        var canvas = new CanvasInteraction(document.Snapshot, 20, 100, maxSliceLength: 16);
        canvas.SetHorizontalAnchor(1);
        Assert.Throws<ArgumentException>(() => canvas.SetHorizontalAnchor(2));
        Assert.Throws<ArgumentException>(() => canvas.SetHorizontalAnchor(4));
        canvas.AnchorHorizontalAtSource(2);
        Assert.Equal(1, canvas.HorizontalAnchor.SourceBoundary);
        canvas.AnchorHorizontalAtSource(4);
        Assert.Equal(3, canvas.HorizontalAnchor.SourceBoundary);
        canvas.SetHorizontalAnchor(document.Snapshot.Length);
        Assert.Equal(document.Snapshot.Length, canvas.HorizontalAnchor.SourceBoundary);
        Assert.Equal(0, canvas.Frame().RowWindows[^1].Slice.SourceLength);

        using var empty = new Document("\n");
        var emptyCanvas = new CanvasInteraction(empty.Snapshot, 20, 100);
        emptyCanvas.SetHorizontalAnchor(0);
        emptyCanvas.SetHorizontalAnchor(1);
        Assert.Equal(1, emptyCanvas.HorizontalAnchor.SourceBoundary);
    }

    /// <summary>Edits before the horizontal edge shift it; insertion at the edge remains left-biased.</summary>
    [Fact]
    public void Edits_before_at_and_after_anchor_transform_without_preserving_stale_pixel_width()
    {
        using var document = new Document("abcdef\nsecond");
        var canvas = new CanvasInteraction(document.Snapshot, 20, 100);
        canvas.SetHorizontalAnchor(4, HorizontalCaretAffinity.Trailing, 2.5, 20);

        Apply(new TextChange(2, 0, "XX"));
        Assert.Equal(6, canvas.HorizontalAnchor.SourceBoundary);
        Assert.Equal(0, canvas.HorizontalAnchor.IntraClusterPixels);
        Assert.Null(canvas.HorizontalAnchor.MeasuredXFromStart);

        Apply(new TextChange(6, 0, "Y"));
        Assert.Equal(6, canvas.HorizontalAnchor.SourceBoundary);
        Assert.Equal(0, canvas.HorizontalAnchor.IntraClusterPixels);
        canvas.SetHorizontalAnchor(7, HorizontalCaretAffinity.Trailing, 3.5, 24);
        Apply(new TextChange(document.Snapshot.GetLineStartOffset(1), 0, "Z"));
        Assert.Equal(7, canvas.HorizontalAnchor.SourceBoundary);
        Assert.Equal(3.5, canvas.HorizontalAnchor.IntraClusterPixels);
        Assert.Null(canvas.HorizontalAnchor.MeasuredXFromStart);
        Assert.Equal(HorizontalCaretAffinity.Trailing, canvas.HorizontalAnchor.Affinity);

        void Apply(TextChange change)
        {
            var after = document.Apply(change);
            canvas.ApplyEdit(after, new TextChangeRange(change.Start, change.DeleteLength, change.InsertText.Length));
            Assert.Equal(after.Version, canvas.Frame().Version);
            Assert.Equal(canvas.Frame().RowWindows.Select(row => row.Slice), canvas.Frame().Slices);
        }
    }

    /// <summary>Joining the anchor's line with an earlier line invalidates its old local pixel residual.</summary>
    [Fact]
    public void Deleting_newline_before_reference_row_resets_horizontal_residual()
    {
        using var document = new Document("ب\nببب");
        var canvas = new CanvasInteraction(document.Snapshot, 20, 100);
        canvas.SetHorizontalAnchor(4, HorizontalCaretAffinity.Trailing, 2.5, 14);
        Assert.Equal(2, canvas.HorizontalAnchor.ReferenceLineStart);

        var after = document.Apply(new TextChange(1, 1, ""));
        canvas.ApplyEdit(after, new TextChangeRange(1, 1, 0));

        Assert.Equal("بببب", after.GetText());
        Assert.Equal(0, canvas.HorizontalAnchor.ReferenceLineStart);
        Assert.Equal(3, canvas.HorizontalAnchor.SourceBoundary);
        Assert.Equal(0, canvas.HorizontalAnchor.IntraClusterPixels);
        Assert.Null(canvas.HorizontalAnchor.MeasuredXFromStart);
        Assert.Equal(HorizontalCaretAffinity.Trailing, canvas.HorizontalAnchor.Affinity);
        Assert.Equal(after.Version, canvas.Frame().Version);
    }

    /// <summary>The first hidden code unit at a clipped suffix is outside the current source window.</summary>
    [Fact]
    public void Reveal_at_hidden_suffix_boundary_reanchors_instead_of_claiming_window_membership()
    {
        using var document = new Document(new string('a', 10_000));
        var canvas = new CanvasInteraction(document.Snapshot, 20, 100, maxSliceLength: 4096);
        var before = Assert.Single(canvas.Frame().RowWindows).Slice;
        Assert.Equal(0, before.SourceStart);
        Assert.Equal(4096, before.SourceLength);
        Assert.True(before.HasHiddenSuffix);

        Assert.Equal(HorizontalRevealStatus.SourceWindowMoved, canvas.Reveal(4096));
        var after = Assert.Single(canvas.Frame().RowWindows).Slice;
        Assert.True(after.SourceStart <= 4096 && 4096 < after.SourceStart + after.SourceLength);
        Assert.InRange(after.SourceLength, 0, 4096);
        Assert.Equal(document.Snapshot.Version, canvas.Frame().Version);
    }

    /// <summary>A ribbon-only tiny window exposes no source row and retains canonical navigation.</summary>
    [Fact]
    public void Zero_body_height_hides_rows_without_changing_source_or_navigation()
    {
        using var document = new Document("alpha\nbeta\n");
        var canvas = new CanvasInteraction(document.Snapshot, 20, 40);
        canvas.SetHorizontalAnchor(3, HorizontalCaretAffinity.Trailing, 1.5);
        canvas.BeginSelection(1);
        canvas.ExtendSelection(7);
        canvas.EndSelection();
        var before = canvas.Frame();
        Assert.NotEmpty(before.Slices);

        canvas.Resize(0);
        var hidden = canvas.Frame();
        Assert.Empty(hidden.Slices);
        Assert.Empty(hidden.RowWindows);
        Assert.Equal(before.Version, hidden.Version);
        Assert.Equal(before.TopAnchor, hidden.TopAnchor);
        Assert.Equal(before.Horizontal, hidden.Horizontal);
        Assert.Equal((before.SelectionAnchor, before.SelectionActive),
            (hidden.SelectionAnchor, hidden.SelectionActive));
        Assert.Equal(document.Snapshot.GetText(), canvas.Snapshot.GetText());
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.Resize(-0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.Resize(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.Resize(double.PositiveInfinity));

        canvas.Resize(40);
        var restored = canvas.Frame();
        Assert.NotEmpty(restored.RowWindows);
        Assert.Equal(before.Version, restored.Version);
        Assert.Equal(before.Horizontal, restored.Horizontal);
        Assert.Equal((before.SelectionAnchor, before.SelectionActive),
            (restored.SelectionAnchor, restored.SelectionActive));

        var initiallyHidden = new CanvasInteraction(document.Snapshot, 20, 0);
        Assert.Empty(initiallyHidden.Frame().Slices);
        Assert.Empty(initiallyHidden.Frame().RowWindows);
        initiallyHidden.Resize(20);
        Assert.NotEmpty(initiallyHidden.Frame().Slices);
    }

    /// <summary>An empty New buffer keeps the horizontal viewport at its source start after typing.</summary>
    [Fact]
    public void Inserting_at_empty_left_edge_keeps_source_zero_visible_and_vertical_anchor_right_affine()
    {
        using var document = new Document();
        var canvas = new CanvasInteraction(document.Snapshot, 20, 40);
        Assert.Equal(0, canvas.HorizontalAnchor.SourceBoundary);
        Assert.Equal(0, canvas.TopAnchor.SourceOffset);

        var after = document.Apply(new TextChange(0, 0, "abc"));
        canvas.ApplyEdit(after, new TextChangeRange(0, 0, 3));

        Assert.Equal(0, canvas.HorizontalAnchor.SourceBoundary);
        Assert.Equal(0, canvas.HorizontalAnchor.ReferenceLineStart);
        Assert.Equal(3, canvas.TopAnchor.SourceOffset);
        Assert.Null(canvas.HorizontalAnchor.MeasuredXFromStart);
        Assert.Equal(0, Assert.Single(canvas.Frame().RowWindows).LeftEdgeSourceBoundary);
        Assert.Equal("abc", after.GetText());
    }

    /// <summary>The horizontal edge tracks surviving content but never jumps over new text at itself.</summary>
    [Fact]
    public void Before_and_overlapping_edits_transform_horizontal_edge_independently_of_vertical_anchor()
    {
        using var document = new Document("abcdefghijkl");
        var canvas = new CanvasInteraction(document.Snapshot, 20, 40, maxSliceLength: 8);
        canvas.SetHorizontalAnchor(6, HorizontalCaretAffinity.Trailing, 2.5, 32);
        canvas.ApplyEdit(document.Snapshot, new TextChangeRange(6, 0, 0));
        Assert.Equal(6, canvas.HorizontalAnchor.SourceBoundary);
        Assert.Equal(document.Snapshot.Version, canvas.Frame().Version);

        Apply(new TextChange(4, 2, "")); // deletion ends exactly at old edge
        Assert.Equal(4, canvas.HorizontalAnchor.SourceBoundary);
        Assert.Equal(0, canvas.HorizontalAnchor.IntraClusterPixels);
        Assert.Null(canvas.HorizontalAnchor.MeasuredXFromStart);

        Apply(new TextChange(2, 4, "Z")); // replacement spans the current edge
        Assert.Equal(2, canvas.HorizontalAnchor.SourceBoundary);
        Assert.Equal(0, canvas.HorizontalAnchor.ReferenceLineStart);
        Assert.Equal(0, canvas.HorizontalAnchor.IntraClusterPixels);
        Assert.Null(canvas.HorizontalAnchor.MeasuredXFromStart);

        void Apply(TextChange change)
        {
            var after = document.Apply(change);
            canvas.ApplyEdit(after,
                new TextChangeRange(change.Start, change.DeleteLength, change.InsertText.Length));
            Assert.Equal(after.Version, canvas.Frame().Version);
        }
    }

    /// <summary>Typing at a 40 Mi-unit pan edge cannot shift the bounded visible window right.</summary>
    [Fact]
    public void Far_line_insert_at_horizontal_edge_remains_bounded_and_left_biased()
    {
        const int target = 40 * 1024 * 1024;
        using var document = new Document(new string('a', 50 * 1024 * 1024));
        var canvas = new CanvasInteraction(document.Snapshot, 20, 40, maxSliceLength: 4096);
        canvas.SetHorizontalAnchor(target, HorizontalCaretAffinity.Leading, 1.25, 250);

        var after = document.Apply(new TextChange(target, 0, "Z"));
        canvas.ApplyEdit(after, new TextChangeRange(target, 0, 1));
        var frame = canvas.Frame();

        Assert.Equal(target, frame.Horizontal.SourceBoundary);
        Assert.Equal(0, frame.Horizontal.IntraClusterPixels);
        Assert.Null(frame.Horizontal.MeasuredXFromStart);
        Assert.Contains(frame.RowWindows, row => row.LeftEdgeSourceBoundary == target &&
            row.Slice.SourceStart <= target && target < row.Slice.SourceStart + row.Slice.SourceLength);
        Assert.All(frame.Slices, slice => Assert.InRange(slice.SourceLength, 0, 4096));
        Assert.Equal("Z", after.GetText(target, 1));
    }
}
