using Mote.Engine;
using Mote.Native;

namespace Mote.Tests;

/// <summary>Global navigation remains canonical even when the text control shows one page.</summary>
public sealed class NativeNavigationTests
{
    /// <summary>Find traverses rope chunks and wraps without materializing the whole file.</summary>
    [Fact]
    public void Find_next_crosses_chunk_boundary_and_wraps()
    {
        var prefix = new string('x', 16_383);
        using var document = new Document(prefix + "needle xx needle");
        var navigation = new NativeNavigationModel();
        Assert.True(navigation.FindNext(document.Snapshot, "needle"));
        Assert.Equal(16_383, navigation.SelectionStart);
        Assert.Equal(6, navigation.SelectionLength);
        Assert.True(navigation.FindNext(document.Snapshot, "needle"));
        Assert.Equal(16_393, navigation.SelectionStart);
        Assert.True(navigation.FindNext(document.Snapshot, "needle"));
        Assert.Equal(16_383, navigation.SelectionStart);
    }

    /// <summary>Global selection direction survives clipping and CRLF display projection.</summary>
    [Fact]
    public void Project_clips_reverse_selection_to_display_page()
    {
        using var document = new Document("a\nb\nc");
        var navigation = new NativeNavigationModel();
        navigation.SetSelection(document.Snapshot, anchor: 5, active: 1);
        var projection = new NativeTextProjection("b\n", NativeLineEndingMode.CrLf);
        var clipped = navigation.Project(pageStart: 2, projection);
        Assert.NotNull(clipped);
        Assert.Equal(3, clipped.Value.Anchor);
        Assert.Equal(0, clipped.Value.Active);
        Assert.Equal(1, navigation.SelectionStart);
        Assert.Equal(4, navigation.SelectionLength);
    }

    /// <summary>Copy streams original bytes' text form across pages, preserving CR/LF spelling.</summary>
    [Fact]
    public void Copy_global_selection_preserves_mixed_newlines()
    {
        var source = new string('a', 16_383) + "\r\nb\nc";
        using var document = new Document(source);
        var navigation = new NativeNavigationModel();
        navigation.SetSelection(document.Snapshot, 16_382, source.Length);
        using var writer = new StringWriter();
        navigation.WriteSelection(document.Snapshot, writer);
        Assert.Equal("a\r\nb\nc", writer.ToString());
    }

    /// <summary>Go-to-line uses indexed logical lines including CRLF and trailing empty line.</summary>
    [Fact]
    public void Go_to_line_tracks_engine_line_index()
    {
        using var document = new Document("a\r\nb\nc\r");
        var navigation = new NativeNavigationModel();
        navigation.GoToLine(document.Snapshot, 4);
        Assert.Equal(document.Snapshot.Length, navigation.Active);
        Assert.Throws<ArgumentOutOfRangeException>(() => navigation.GoToLine(document.Snapshot, 5));
    }

    /// <summary>Edits transform both directed endpoints in global UTF-16 coordinates.</summary>
    [Fact]
    public void Edit_transforms_selection_endpoints()
    {
        using var document = new Document("abcdef");
        var navigation = new NativeNavigationModel();
        navigation.SetSelection(document.Snapshot, 5, 2);
        var change = new TextChange(1, 2, "XYZW");
        var after = document.Apply(change);
        navigation.ApplyChange(change, after);
        Assert.Equal(7, navigation.Anchor);
        Assert.Equal(5, navigation.Active);
    }

    /// <summary>A canceled large search returns no published new selection.</summary>
    [Fact]
    public void Canceled_search_keeps_selection()
    {
        using var document = new Document(new string('x', 100_000));
        var navigation = new NativeNavigationModel();
        navigation.MoveCaret(document.Snapshot, 123);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            navigation.FindNext(document.Snapshot, "not-present", cancellationToken: cancellation.Token));
        Assert.Equal(123, navigation.Active);
        Assert.Equal(123, navigation.Anchor);
    }
}
