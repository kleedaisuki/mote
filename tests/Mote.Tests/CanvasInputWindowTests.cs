using Mote.Engine;
using Mote.Native;
using Mote.Native.Viewport;

namespace Mote.Tests;

/// <summary>Source-backed input-island windows never become a second whole-line document.</summary>
public sealed class CanvasInputWindowTests
{
    /// <summary>Empty and terminal lines bind as empty source windows without borrowing delimiters.</summary>
    [Theory]
    [InlineData("", 0, 0, "")]
    [InlineData("a\n", 2, 2, "")]
    [InlineData("a\r", 2, 2, "")]
    [InlineData("a\r\n", 3, 3, "")]
    [InlineData("a\r\nb", 3, 3, "b")]
    public void Empty_and_trailing_lines_have_exact_source_windows(
        string source, int caret, int expectedStart, string expectedText)
    {
        using var document = new Document(source);
        var window = CanvasInputWindowSelector.Select(document.Snapshot, caret);
        Assert.Equal(expectedStart, window.SourceStart);
        Assert.Equal(expectedText, window.SourceText);
        Assert.Equal(expectedStart + expectedText.Length, window.SourceEnd);
    }

    /// <summary>A caret in the middle of CRLF never causes the host to own half a delimiter.</summary>
    [Fact]
    public void CrLf_internal_caret_is_clamped_to_one_line_content()
    {
        const string source = "left\r\nright";
        using var document = new Document(source);
        var window = CanvasInputWindowSelector.Select(document.Snapshot, source.IndexOf('\n'));
        Assert.DoesNotContain('\r', window.SourceText);
        Assert.DoesNotContain('\n', window.SourceText);
        Assert.True(window.SourceStart == 0 || window.SourceStart == source.IndexOf("right", StringComparison.Ordinal));
        Assert.Equal(source.Substring(window.SourceStart, window.SourceText.Length), window.SourceText);
    }

    /// <summary>Even a 50 MiB logical line contributes only a caret-local 16 Ki UTF-16 window.</summary>
    [Fact]
    public void Fifty_mebibyte_line_far_caret_is_bounded_and_source_exact()
    {
        const int size = 50 * 1024 * 1024;
        using var document = new Document(new string('x', size));
        var caret = size * 3 / 4;
        var window = CanvasInputWindowSelector.Select(document.Snapshot, caret);
        Assert.InRange(window.SourceText.Length, 1, CanvasInputWindowSelector.MaxLength);
        Assert.InRange(caret, window.SourceStart, window.SourceEnd);
        Assert.True(window.SourceStart > 0);
        Assert.True(window.SourceEnd < size);
        Assert.Equal(new string('x', window.SourceText.Length), window.SourceText);

        var smaller = CanvasInputWindowSelector.Select(document.Snapshot, caret, 2048);
        Assert.InRange(smaller.SourceText.Length, 1, 2048);
        Assert.InRange(caret, smaller.SourceStart, smaller.SourceEnd);
        Assert.Equal(new string('x', smaller.SourceText.Length), smaller.SourceText);

        var macBinding = CanvasInputWindowSelector.Select(document.Snapshot, caret, 8192);
        Assert.Equal(8192, macBinding.SourceText.Length);
        Assert.InRange(caret, macBinding.SourceStart, macBinding.SourceEnd);
        Assert.Equal(new string('x', macBinding.SourceText.Length), macBinding.SourceText);
    }

    /// <summary>Neither edge of the native window may bisect an astral Unicode scalar.</summary>
    [Fact]
    public void Surrogate_pairs_at_start_or_end_are_not_split()
    {
        var startSource = new string('a', 8191) + "😀" + new string('b', 40_000);
        using var startDocument = new Document(startSource);
        var startWindow = CanvasInputWindowSelector.Select(startDocument.Snapshot, 16_384);
        Assert.Equal(8191, startWindow.SourceStart);
        Assert.StartsWith("😀", startWindow.SourceText);

        var endSource = new string('a', CanvasInputWindowSelector.MaxLength - 1) + "😀tail";
        using var endDocument = new Document(endSource);
        var endWindow = CanvasInputWindowSelector.Select(endDocument.Snapshot, 0);
        Assert.Equal(0, endWindow.SourceStart);
        Assert.Equal(CanvasInputWindowSelector.MaxLength - 1, endWindow.SourceEnd);
        Assert.DoesNotContain('\ud83d', endWindow.SourceText);
        Assert.Equal(endSource[..endWindow.SourceEnd], endWindow.SourceText);
    }

    /// <summary>Combining marks, ZWJ emoji, and flags remain whole at both island edges.</summary>
    [Theory]
    [InlineData("a\u0301", 1)]
    [InlineData("👩‍💻", 5)]
    [InlineData("🇨🇳", 4)]
    public void Extended_graphemes_are_not_split_at_window_edges(string grapheme, int units)
    {
        var startAt = 8192 - units / 2;
        var startSource = new string('x', startAt) + grapheme + new string('y', 40_000);
        using var startDocument = new Document(startSource);
        var startWindow = CanvasInputWindowSelector.Select(startDocument.Snapshot, 16_384);
        Assert.Equal(startAt, startWindow.SourceStart);
        Assert.StartsWith(grapheme, startWindow.SourceText);

        var endAt = CanvasInputWindowSelector.MaxLength - units / 2;
        var endSource = new string('x', endAt) + grapheme + "tail";
        using var endDocument = new Document(endSource);
        var endWindow = CanvasInputWindowSelector.Select(endDocument.Snapshot, 0);
        Assert.Equal(endAt, endWindow.SourceEnd);
        Assert.Equal(endSource[..endAt], endWindow.SourceText);
    }

    /// <summary>An over-budget single grapheme cannot silently bind a truncated native host.</summary>
    [Fact]
    public void Oversized_combining_cluster_fails_with_recoverable_boundary_error()
    {
        using var document = new Document("a" + new string('\u0301', CanvasInputWindowSelector.MaxLength));
        var error = Assert.Throws<CanvasInputWindowBoundaryException>(() =>
            CanvasInputWindowSelector.Select(document.Snapshot, 0));
        Assert.Equal(0, error.Caret);
    }

    /// <summary>A caret inside a multicodepoint grapheme is rejected before editing state changes.</summary>
    [Theory]
    [InlineData("a\u0301", 1)]
    [InlineData("👩‍💻", 2)]
    [InlineData("🇨🇳", 2)]
    public void Caret_inside_grapheme_is_not_bound(string grapheme, int inside)
    {
        using var document = new Document("X" + grapheme + "Y");
        Assert.Throws<CanvasInputWindowBoundaryException>(() =>
            CanvasInputWindowSelector.Select(document.Snapshot, 1 + inside));
    }

    /// <summary>The controller transforms a shared global selection once; canvas rebind is geometry-only.</summary>
    [Fact]
    public void Canvas_edit_rebind_does_not_transform_shared_selection_twice()
    {
        using var document = new Document("abc\ndef");
        var before = document.Snapshot;
        var selection = new NativeNavigationModel();
        selection.MoveCaret(before, 4);
        var canvas = new CanvasInteraction(before, 18, 36, selection: selection);
        var edit = new TextChange(0, 0, "X");
        document.Apply(edit);
        var after = document.Snapshot;
        selection.ApplyChange(edit, after);
        canvas.ApplyEdit(after, edit);

        Assert.Same(selection, canvas.Selection);
        Assert.Equal(5, selection.Active);
        Assert.Equal(5, canvas.Frame().SelectionActive);
        Assert.Equal(after.Version, canvas.Frame().Version);
        Assert.Equal("Xabc\ndef", after.GetText());
    }

    /// <summary>A configurable 2 Ki island still honors whole Unicode graphemes at both edges.</summary>
    [Theory]
    [InlineData("a\u0301", 2)]
    [InlineData("👩‍💻", 5)]
    [InlineData("🇨🇳", 4)]
    public void Custom_2048_window_preserves_combining_zwj_and_flag_clusters(string grapheme, int units)
    {
        const int limit = 2048;
        var startAt = limit / 2 - units / 2;
        var source = new string('x', startAt) + grapheme + new string('y', 10_000);
        using var document = new Document(source);
        var window = CanvasInputWindowSelector.Select(document.Snapshot, limit, limit);
        Assert.InRange(window.SourceText.Length, 1, limit);
        Assert.Equal(startAt, window.SourceStart);
        Assert.StartsWith(grapheme, window.SourceText);

        var endAt = limit - units / 2;
        using var endDocument = new Document(new string('x', endAt) + grapheme + "tail");
        var endWindow = CanvasInputWindowSelector.Select(endDocument.Snapshot, 0, limit);
        Assert.Equal(endAt, endWindow.SourceEnd);
        Assert.DoesNotContain(grapheme, endWindow.SourceText, StringComparison.Ordinal);
    }

    /// <summary>Custom limits validate both API bounds and recoverably reject over-budget clusters.</summary>
    [Fact]
    public void Custom_island_limit_rejects_out_of_range_or_overbudget_grapheme()
    {
        using var small = new Document("a\r\nb");
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CanvasInputWindowSelector.Select(small.Snapshot, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CanvasInputWindowSelector.Select(small.Snapshot, 0, 16_385));
        var crlf = CanvasInputWindowSelector.Select(small.Snapshot, 2, 2048);
        Assert.DoesNotContain('\r', crlf.SourceText);
        Assert.DoesNotContain('\n', crlf.SourceText);

        using var giant = new Document("a" + new string('\u0301', 2048));
        Assert.Throws<CanvasInputWindowBoundaryException>(() =>
            CanvasInputWindowSelector.Select(giant.Snapshot, 0, 2048));
        Assert.InRange(CanvasInputWindowSelector.Select(giant.Snapshot, 0).SourceText.Length,
            1, CanvasInputWindowSelector.MaxLength);
    }

    /// <summary>A Mac-sized source binding leaves space below the separate 16 Ki native edit limit.</summary>
    [Theory]
    [InlineData(8192)]
    [InlineData(16383)]
    [InlineData(16384)]
    public void Mac_binding_limit_does_not_fill_native_edit_capacity(int sourceLength)
    {
        const int bindingLimit = 8192;
        const int nativeEditLimit = 16384;
        using var document = new Document(new string('x', sourceLength));
        var window = CanvasInputWindowSelector.Select(document.Snapshot, sourceLength / 2, bindingLimit);

        Assert.InRange(window.SourceText.Length, 1, bindingLimit);
        Assert.Equal(document.Snapshot.GetText(window.SourceStart, window.SourceText.Length), window.SourceText);
        Assert.InRange(sourceLength / 2, window.SourceStart, window.SourceEnd);
        Assert.True(window.SourceText.Length + 1 <= nativeEditLimit);
    }

    /// <summary>The 8 Ki Mac request never splits Unicode clusters to manufacture edit slack.</summary>
    [Theory]
    [InlineData("a\u0301", 2)]
    [InlineData("👩‍💻", 5)]
    [InlineData("🇨🇳", 4)]
    public void Mac_binding_limit_preserves_grapheme_edges(string grapheme, int units)
    {
        const int bindingLimit = 8192;
        var startAt = bindingLimit / 2 - units / 2;
        using var startDocument = new Document(new string('x', startAt) + grapheme + new string('y', 20_000));
        var startWindow = CanvasInputWindowSelector.Select(startDocument.Snapshot, bindingLimit, bindingLimit);
        Assert.InRange(startWindow.SourceText.Length, 1, bindingLimit);
        Assert.Equal(startAt, startWindow.SourceStart);
        Assert.StartsWith(grapheme, startWindow.SourceText);

        var endAt = bindingLimit - units / 2;
        using var endDocument = new Document(new string('x', endAt) + grapheme + "tail");
        var endWindow = CanvasInputWindowSelector.Select(endDocument.Snapshot, 0, bindingLimit);
        Assert.Equal(endAt, endWindow.SourceEnd);
        Assert.DoesNotContain(grapheme, endWindow.SourceText, StringComparison.Ordinal);
    }

    /// <summary>An 8 Ki grapheme dependency fails closed instead of binding partial native text.</summary>
    [Fact]
    public void Mac_binding_limit_rejects_oversized_cluster_without_document_change()
    {
        var source = "a" + new string('\u0301', 8192);
        using var document = new Document(source);
        var before = document.Snapshot;

        Assert.Throws<CanvasInputWindowBoundaryException>(() =>
            CanvasInputWindowSelector.Select(before, 0, 8192));
        Assert.Same(before, document.Snapshot);
        Assert.Equal(source, document.Snapshot.GetText());
    }
}
