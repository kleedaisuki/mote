using Mote.Engine;
using Mote.Native.Viewport;

namespace Mote.Tests;

/// <summary>Independent source-coordinate and sparse-height checks for the continuous native viewport.</summary>
public sealed class NativeViewportTests
{
    /// <summary>Measured heights above the viewport change document Y but not its source anchor.</summary>
    [Fact]
    public void Sparse_heights_and_reflow_preserve_source_anchor()
    {
        using var document = new Document("zero\none\ntwo\nthree\n");
        var snapshot = document.Snapshot;
        var view = new ContinuousViewport(snapshot, 18);
        var three = snapshot.GetLineStartOffset(3);
        view.ScrollToSource(three + 2, 5.5);
        Assert.Equal(new ViewportAnchor(three + 2, 5.5), view.TopAnchor);
        Assert.Equal(3 * 18, view.GetDocumentY(three));

        view.SetMeasuredHeight(0, 30);
        view.SetMeasuredHeight(2, 24);
        Assert.Equal(2, view.SparseHeightCount);
        Assert.Equal(3 * 18 + 12 + 6, view.GetDocumentY(three));
        Assert.Equal(new ViewportAnchor(three + 2, 5.5), view.TopAnchor);
        Assert.Equal(view.GetDocumentY(three) + 5.5, view.ScrollY);
        var secondRowY = view.GetDocumentY(snapshot.GetLineStartOffset(2)) + 9;
        Assert.Equal(new ViewportAnchor(snapshot.GetLineStartOffset(2), 9),
            view.GetSourceAtDocumentY(secondRowY));

        view.SetMeasuredHeight(0, 18);
        Assert.Equal(1, view.SparseHeightCount);
        view.Reflow(20);
        Assert.Equal(0, view.SparseHeightCount);
        Assert.Equal(20, view.LineHeight);
        Assert.Equal(new ViewportAnchor(three + 2, 5.5), view.TopAnchor);
        Assert.Equal(snapshot.LineCount * 20, view.DocumentHeight);
    }

    /// <summary>CR, LF, CRLF, terminal newline, and astral characters have exact source-only row spans.</summary>
    [Fact]
    public void Visible_slices_exclude_mixed_delimiters_and_keep_utf16_spans()
    {
        const string source = "A\r\n😀B\rC\nD";
        using var document = new Document(source);
        var view = new ContinuousViewport(document.Snapshot, 12, maxSliceLength: 4);
        var slices = view.GetVisibleSlices(48);
        Assert.Equal(4, slices.Count);
        Assert.Equal(new[] { "A", "😀B", "C", "D" },
            slices.Select(slice => source.Substring(slice.SourceStart, slice.SourceLength)));
        Assert.Equal(new[] { 0, 3, 7, 9 }, slices.Select(slice => slice.SourceStart));
        Assert.Equal(new[] { 1, 3, 1, 1 }, slices.Select(slice => slice.SourceLength));
        Assert.Equal(new[] { 0.0, 12.0, 24.0, 36.0 }, slices.Select(slice => slice.TopY));
        Assert.All(slices, slice =>
        {
            Assert.False(slice.HasHiddenPrefix);
            Assert.False(slice.HasHiddenSuffix);
        });
    }

    /// <summary>Y-to-source lookup matches a simple independent prefix-sum oracle.</summary>
    [Fact]
    public void Random_sparse_heights_match_reference_prefix_sums()
    {
        const int lines = 20_000;
        using var document = new Document(string.Concat(Enumerable.Repeat("x\n", lines)));
        var snapshot = document.Snapshot;
        var view = new ContinuousViewport(snapshot, 18);
        var heights = Enumerable.Repeat(18.0, snapshot.LineCount).ToArray();
        var random = new Random(0x56494557);
        for (var i = 0; i < 500; i++)
        {
            var line = random.Next(snapshot.LineCount);
            var height = 8 + random.Next(41);
            heights[line] = height;
            view.SetMeasuredHeight(line, height);
        }

        var starts = new double[heights.Length + 1];
        for (var i = 0; i < heights.Length; i++) starts[i + 1] = starts[i] + heights[i];
        Assert.Equal(starts[^1], view.DocumentHeight);
        for (var i = 0; i < 1_000; i++)
        {
            var line = random.Next(snapshot.LineCount);
            var source = snapshot.GetLineStartOffset(line);
            Assert.Equal(starts[line], view.GetDocumentY(source));
            var intra = heights[line] / 3;
            var located = view.GetSourceAtDocumentY(starts[line] + intra);
            Assert.Equal(source, located.SourceOffset);
            Assert.InRange(Math.Abs(located.IntraRowY - intra), 0, 1e-8);
        }
    }

    /// <summary>Ten million logical rows need only implicit height state and continuous scrolling.</summary>
    [Fact]
    public void Ten_million_lines_scroll_without_page_boundaries_or_per_line_measurements()
    {
        const int rows = 10_000_000;
        using var document = new Document(string.Concat(Enumerable.Repeat("x\n", rows)));
        var view = new ContinuousViewport(document.Snapshot, 18);
        Assert.Equal(rows + 1, document.Snapshot.LineCount);
        Assert.Equal(0, view.SparseHeightCount);
        Assert.Equal((rows + 1) * 18.0, view.DocumentHeight);
        var target = 9_000_000;
        view.ScrollToSource(2 * target);
        Assert.Equal(target * 18.0, view.ScrollY);
        view.ScrollBy(180);
        Assert.Equal(2 * (target + 10), view.TopAnchor.SourceOffset);
        var visible = view.GetVisibleSlices(180, maxSlices: 20);
        Assert.InRange(visible.Count, 10, 11);
        Assert.Equal(target + 10, visible[0].Line);
        Assert.All(visible, slice => Assert.InRange(slice.SourceLength, 0, 1));
        view.SetMeasuredHeight(target - 1, 30);
        Assert.Equal(1, view.SparseHeightCount);
        Assert.Equal(2 * (target + 10), view.TopAnchor.SourceOffset);
    }

    /// <summary>A 50 MiB unbroken row is exposed only through bounded surrogate-safe windows.</summary>
    [Fact]
    public void Fifty_mebibyte_single_line_never_creates_an_unbounded_slice()
    {
        const int size = 50 * 1024 * 1024;
        var source = new string('a', 4095) + "😀" + new string('b', size - 4097);
        Assert.Equal(size, source.Length);
        using var document = new Document(source);
        var view = new ContinuousViewport(document.Snapshot, 18, maxSliceLength: 4096);
        Assert.Equal(1, document.Snapshot.LineCount);
        foreach (var focus in new[] { 0, 4095, 4096, size / 2, size - 1, size })
        {
            var slice = Assert.Single(view.GetVisibleSlices(18, focusSourceOffset: focus));
            Assert.InRange(slice.SourceLength, 1, 4096);
            Assert.InRange(slice.SourceStart, 0, source.Length - slice.SourceLength);
            Assert.False(SplitsPair(source, slice.SourceStart));
            Assert.False(SplitsPair(source, slice.SourceStart + slice.SourceLength));
            Assert.Equal(focus > 0, slice.HasHiddenPrefix || !slice.HasHiddenSuffix);
        }
    }

    /// <summary>Random edits transform a source anchor with right affinity and invalidate heights.</summary>
    [Fact]
    public void Random_edits_keep_anchor_and_slices_in_authoritative_snapshot()
    {
        using var document = new Document("A\r\n😀B\rC\nD");
        var view = new ContinuousViewport(document.Snapshot, 16, maxSliceLength: 4);
        var random = new Random(0x45444954);
        var insertions = new[] { "", "x", "😀", "\r", "\n", "\r\n" };
        for (var i = 0; i < 500; i++)
        {
            var before = document.Snapshot;
            var source = before.GetText();
            var boundaries = Enumerable.Range(0, source.Length + 1)
                .Where(index => !SplitsPair(source, index)).ToArray();
            var left = boundaries[random.Next(boundaries.Length)];
            var right = boundaries[random.Next(boundaries.Length)];
            if (left > right) (left, right) = (right, left);
            var anchor = boundaries[random.Next(boundaries.Length)];
            view.ScrollToSource(anchor, 3);
            view.SetMeasuredHeight(0, 21);
            var change = new TextChange(left, right - left, insertions[random.Next(insertions.Length)]);
            var after = document.Apply(change);
            view.ApplyEdit(after, change);
            var expectedAnchor = anchor < left ? anchor :
                anchor <= right ? left + change.InsertText.Length :
                anchor + change.InsertText.Length - change.DeleteLength;
            Assert.Equal(expectedAnchor, view.TopAnchor.SourceOffset);
            Assert.Same(after, view.Snapshot);
            Assert.Equal(0, view.SparseHeightCount);
            foreach (var slice in view.GetVisibleSlices(64))
            {
                Assert.InRange(slice.SourceLength, 0, 4);
                Assert.False(SplitsPair(after.GetText(), slice.SourceStart));
                Assert.False(SplitsPair(after.GetText(), slice.SourceStart + slice.SourceLength));
            }
        }
    }

    /// <summary>Tests only UTF-16 scalar boundaries, not grapheme-cluster boundaries.</summary>
    private static bool SplitsPair(string text, int offset) => offset > 0 && offset < text.Length &&
        char.IsHighSurrogate(text[offset - 1]) && char.IsLowSurrogate(text[offset]);
}
