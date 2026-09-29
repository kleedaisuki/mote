using Mote.Engine;
using Mote.Native.Viewport;

namespace Mote.Tests;

/// <summary>Independent source-coordinate checks for the bounded read-only canvas model.</summary>
public sealed class CanvasInteractionTests
{
    /// <summary>Wheel movement crosses the former native page without resetting source selection or version.</summary>
    [Fact]
    public void Scroll_and_resize_preserve_global_selection_across_old_page_boundary()
    {
        var source = string.Concat(Enumerable.Repeat("x\n", 40_000));
        using var document = new Document(source);
        var canvas = new CanvasInteraction(document.Snapshot, 16, 160);
        var version = document.Snapshot.Version;
        canvas.BeginSelection(65_520);
        canvas.ExtendSelection(65_550);
        canvas.EndSelection();
        canvas.Reveal(65_520);
        var before = canvas.Frame();

        canvas.ScrollBy(16 * 20);
        canvas.Resize(320);
        var after = canvas.Frame();

        Assert.True(after.TopAnchor.SourceOffset > 65_536);
        Assert.Equal(version, before.Version);
        Assert.Equal(version, after.Version);
        Assert.Equal((65_520, 65_550), (after.SelectionAnchor, after.SelectionActive));
        Assert.Equal(30, after.SelectionLength);
        Assert.All(after.Slices, slice => Assert.InRange(slice.SourceLength, 0, 4096));
        Assert.Equal(source, document.Snapshot.GetText());
    }

    /// <summary>Clipboard streaming uses canonical source bytes, including CRLF and astral UTF-16 units.</summary>
    [Fact]
    public void Pointer_drag_streams_exact_selected_source_across_mixed_newlines()
    {
        const string source = "zero\r\none😀\ntwo\rthree\r\nfour";
        using var document = new Document(source);
        var canvas = new CanvasInteraction(document.Snapshot, 18, 90);
        var start = source.IndexOf("one", StringComparison.Ordinal);
        var end = source.IndexOf("four", StringComparison.Ordinal) + 4;
        canvas.BeginSelection(end);
        canvas.ExtendSelection(start);
        canvas.EndSelection();

        using var writer = new StringWriter();
        canvas.WriteSelection(writer);
        var frame = canvas.Frame();
        Assert.Equal(source[start..end], writer.ToString());
        Assert.Equal(start, frame.SelectionStart);
        Assert.Equal(end - start, frame.SelectionLength);
        Assert.Equal((end, start), (frame.SelectionAnchor, frame.SelectionActive));
    }

    /// <summary>Pointer geometry cannot invent a caret inside a CRLF or surrogate pair.</summary>
    [Fact]
    public void Pointer_rejects_nonvisible_utf16_boundaries_without_changing_selection()
    {
        const string source = "A\r\n😀B";
        using var document = new Document(source);
        var canvas = new CanvasInteraction(document.Snapshot, 18, 36);
        canvas.BeginSelection(0);
        canvas.EndSelection();
        Assert.Throws<ArgumentException>(() => canvas.BeginSelection(2)); // CR | LF
        Assert.Throws<ArgumentException>(() => canvas.BeginSelection(4)); // high | low surrogate
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.BeginSelection(source.Length + 1));
        Assert.Equal((0, 0), (canvas.Frame().SelectionAnchor, canvas.Frame().SelectionActive));

        canvas.BeginSelection(1);
        Assert.Throws<ArgumentException>(() => canvas.ExtendSelection(4));
        Assert.Equal((1, 1), (canvas.Frame().SelectionAnchor, canvas.Frame().SelectionActive));
    }

    /// <summary>Empty visual rows have a source anchor, and delimiter selection remains exact.</summary>
    [Fact]
    public void Blank_row_frame_and_delimiter_selection_keep_original_newline()
    {
        const string source = "a\n\nb\r\n\r\nc";
        using var document = new Document(source);
        var canvas = new CanvasInteraction(document.Snapshot, 18, 90);
        canvas.Reveal(2);
        var frame = canvas.Frame();
        Assert.Contains(frame.Slices, slice => slice.SourceStart == 2 && slice.SourceLength == 0);
        canvas.BeginSelection(2);
        canvas.ExtendSelection(3);
        canvas.EndSelection();
        using var writer = new StringWriter();
        canvas.WriteSelection(writer);
        Assert.Equal("\n", writer.ToString());
    }

    /// <summary>A pathological row remains sliced even when the pointer is at its far end.</summary>
    [Fact]
    public void Fifty_mebibyte_line_frame_never_exposes_more_than_sixteen_kib_per_slice()
    {
        const int length = 50 * 1024 * 1024;
        using var document = new Document(new string('a', length));
        var canvas = new CanvasInteraction(document.Snapshot, 18, 36, maxSliceLength: 16 * 1024);
        var version = document.Snapshot.Version;
        canvas.Reveal(length - 1);
        var frame = canvas.Frame();

        Assert.Equal(version, frame.Version);
        Assert.NotEmpty(frame.Slices);
        Assert.All(frame.Slices, slice => Assert.InRange(slice.SourceLength, 0, 16 * 1024));
        Assert.True(frame.Slices.Sum(slice => (long)slice.SourceLength) <= 512L * 16 * 1024);
        Assert.Contains(frame.Slices, slice => slice.SourceStart <= length - 1 &&
            slice.SourceStart + slice.SourceLength >= length - 1);
    }
}
