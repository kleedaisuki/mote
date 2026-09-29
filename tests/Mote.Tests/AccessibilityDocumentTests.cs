using Mote.Engine;
using System.Runtime.CompilerServices;
using Mote.Native;
using Mote.Native.Accessibility;
using Mote.Native.Mac.Accessibility;
using Mote.Native.Viewport;
using Mote.Native.Windows.Accessibility;

namespace Mote.Tests;

/// <summary>Checks source-wide AX semantics without claiming a live OS provider.</summary>
public sealed class AccessibilityDocumentTests
{
    /// <summary>Offscreen text and selection use global UTF-16 offsets, not island offsets.</summary>
    [Fact]
    public void Offscreen_text_and_reverse_selection_are_global()
    {
        using var document = new Document("top\r\n" + new string('x', 100_000) + "\nlast😀");
        var snapshot = document.Snapshot;
        var model = new AccessibleDocument(Bind(snapshot, 50_000, 3));
        var end = snapshot.Length;

        Assert.Equal(end, model.DocumentRange.End);
        Assert.True(model.IsReverseSelection);
        Assert.Equal(3, model.Selection.Start);
        Assert.Equal("last😀", model.GetText(model.MakeRange(end - 6, end)));
        Assert.Single(model.VisibleRanges());
        Assert.Equal(0, model.VisibleRanges()[0].Start);
    }

    /// <summary>One request never silently truncates an unlimited UIA range.</summary>
    [Fact]
    public void Oversized_request_fails_but_explicit_chunk_can_be_read()
    {
        using var document = new Document(new string('q', AccessibleDocument.MaxTextRequest + 1));
        var model = new AccessibleDocument(Bind(document.Snapshot, 0, 0));
        var provider = new WindowsTextProviderCore(model, new ViewportStub());

        Assert.Throws<AccessibleRequestTooLargeException>(() =>
            provider.GetText(provider.DocumentRange, -1));
        var oversized = provider.TryGetText(provider.DocumentRange, -1);
        Assert.Equal(WindowsTextResult.UIA_E_INVALIDOPERATION, oversized.HResult);
        Assert.Null(oversized.Text);
        Assert.Equal(WindowsTextResult.S_OK,
            provider.TryGetText(provider.DocumentRange, 100).HResult);
        Assert.Equal(new string('q', 100), provider.GetText(provider.DocumentRange, 100));
        Assert.Equal("q", provider.GetText(model.MakeRange(document.Snapshot.Length - 1,
            document.Snapshot.Length), -1));
    }

    /// <summary>Mac line access retains exact source delimiters, including CRLF.</summary>
    [Fact]
    public void Mac_line_ranges_cover_original_delimiters()
    {
        using var document = new Document("a\r\nb\rc\n");
        var model = new AccessibleDocument(Bind(document.Snapshot, 0, 0));
        var provider = new MacAccessibilityTextCore(model, new ViewportStub());

        Assert.Equal(document.Snapshot.Length, provider.CharacterCount);
        Assert.Equal("a\r\n", provider.StringForRange(provider.RangeForLine(0)));
        Assert.Equal("b\r", provider.StringForRange(provider.RangeForLine(1)));
        Assert.Equal("c\n", provider.StringForRange(provider.RangeForLine(2)));
        Assert.Equal("", provider.StringForRange(provider.RangeForLine(3)));
        Assert.Equal(2, provider.LineForIndex(5));
    }

    /// <summary>Published frame changes visibility but cannot change source version.</summary>
    [Fact]
    public void Frame_update_is_version_checked_and_changes_visible_ranges()
    {
        using var document = new Document("first\nsecond\nthird");
        var snapshot = document.Snapshot;
        var model = new AccessibleDocument(Bind(snapshot, 0, 0));
        var later = new CanvasFrame(snapshot.Version, new ViewportAnchor(13, 0), 32,
            [new ViewportSlice(2, 13, 5, 0, 16, false, false)], 13, 13);
        model.Publish(Bind(snapshot, 13, 13) with { Frame = later });

        Assert.Equal(13, model.Selection.Start);
        Assert.Equal("third", model.GetText(model.VisibleRanges()[0]));
        Assert.Throws<ArgumentException>(() => model.Publish(
            Bind(snapshot, 13, 13) with { Frame = later with { Version = snapshot.Version + 1 } }));
        Assert.Throws<InvalidOperationException>(() => model.Publish(
            Bind(snapshot, 13, 13, generation: 0) with { Frame = later }));
    }

    /// <summary>UIA gets a degenerate range when nothing is painted.</summary>
    [Fact]
    public void Empty_visibility_returns_one_degenerate_range()
    {
        using var document = new Document("abc");
        var binding = Bind(document.Snapshot, 0, 0) with
        {
            Frame = new CanvasFrame(document.Snapshot.Version, new ViewportAnchor(2, 0), 0,
                [], 0, 0)
        };
        var visible = new AccessibleDocument(binding).VisibleRanges();
        Assert.Single(visible);
        Assert.Equal(2, visible[0].Start);
        Assert.Equal(2, visible[0].End);
        var mac = new MacAccessibilityTextCore(new AccessibleDocument(binding), new ViewportStub());
        Assert.Equal(visible[0], mac.VisibleCharacterRange);
    }

    /// <summary>UIA adjacent complete lines form one visible span with original delimiter.</summary>
    [Fact]
    public void Adjacent_lines_coalesce_but_clipped_windows_do_not()
    {
        using var document = new Document("ab\r\ncd\nef");
        var snapshot = document.Snapshot;
        var frame = new CanvasFrame(snapshot.Version, new ViewportAnchor(0, 0), 0,
            [new ViewportSlice(0, 0, 2, 0, 16, false, false),
                new ViewportSlice(1, 4, 2, 16, 16, false, false),
                new ViewportSlice(2, 7, 1, 32, 16, false, true)], 0, 0);
        var model = new AccessibleDocument(Bind(snapshot, 0, 0) with { Frame = frame });
        var visible = model.VisibleRanges();
        Assert.Single(visible);
        Assert.Equal("ab\r\ncd\ne", model.GetText(visible[0]));

        var clipped = frame with { Slices =
            [new ViewportSlice(0, 0, 1, 0, 16, false, true),
                new ViewportSlice(1, 4, 2, 16, 16, false, false)] };
        model.Publish(Bind(snapshot, 0, 0) with { Frame = clipped });
        Assert.Equal(2, model.VisibleRanges().Count);
    }

    /// <summary>A maxLength cutoff cannot manufacture half of a surrogate or CRLF.</summary>
    [Theory]
    [InlineData("A😀B", 2, "A")]
    [InlineData("A\r\nB", 2, "A")]
    public void Bounded_text_cutoff_preserves_atomic_source_boundary(
        string source, int maxLength, string expected)
    {
        using var document = new Document(source);
        var model = new AccessibleDocument(Bind(document.Snapshot, 0, 0));
        Assert.Equal(expected, model.GetText(model.DocumentRange, maxLength));
    }

    /// <summary>AppKit visible range names the whole clipped line without reading it.</summary>
    [Fact]
    public void Mac_visible_range_includes_horizontally_clipped_long_line()
    {
        using var document = new Document(new string('a', 100_000) + "\nnext");
        var snapshot = document.Snapshot;
        var frame = new CanvasFrame(snapshot.Version, new ViewportAnchor(50_000, 0), 0,
            [new ViewportSlice(0, 50_000, 256, 0, 16, true, true)], 50_000, 50_000);
        var binding = Bind(snapshot, 50_000, 50_000) with { Frame = frame };
        var model = new AccessibleDocument(binding);
        var provider = new MacAccessibilityTextCore(model, new ViewportStub());

        Assert.Equal(0, provider.VisibleCharacterRange.Start);
        Assert.Equal(100_001, provider.VisibleCharacterRange.End);
        Assert.Equal(256, model.VisibleRanges()[0].Length);
        Assert.Throws<AccessibleRequestTooLargeException>(() =>
            provider.StringForRange(provider.VisibleCharacterRange));
    }

    /// <summary>No painted slice must not advertise a 50 MiB line as visible on macOS.</summary>
    [Fact]
    public void Mac_empty_viewport_stays_degenerate_on_fifty_mebibyte_line()
    {
        using var document = new Document(new string('a', 50 * 1024 * 1024));
        var snapshot = document.Snapshot;
        var frame = new CanvasFrame(snapshot.Version, new ViewportAnchor(25_000_000, 0), 0,
            [], 25_000_000, 25_000_000);
        var model = new AccessibleDocument(Bind(snapshot, 25_000_000, 25_000_000)
            with { Frame = frame });
        var mac = new MacAccessibilityTextCore(model, new ViewportStub());

        Assert.Equal(25_000_000, mac.VisibleCharacterRange.Start);
        Assert.Equal(0, mac.VisibleCharacterRange.Length);
        Assert.Equal("", mac.StringForRange(mac.VisibleCharacterRange));
        Assert.Equal("a", model.GetText(model.MakeRange(snapshot.Length - 1, snapshot.Length)));
    }

    /// <summary>AX source publication does not require any input-island binding.</summary>
    [Fact]
    public void Publishing_new_document_requires_only_engine_snapshot_and_canvas_frame()
    {
        using var first = new Document("first");
        using var second = new Document("second-offscreen");
        var model = new AccessibleDocument(Bind(first.Snapshot, 0, 0, generation: 5));
        var old = model.DocumentRange;

        model.Publish(Bind(second.Snapshot, 0, 0, generation: 6));

        Assert.Equal(second.Snapshot.Length, model.DocumentRange.End);
        Assert.Equal("offscreen", model.GetText(model.MakeRange(7, 16)));
        Assert.Throws<InvalidOperationException>(() => model.GetText(old));
    }

    /// <summary>A one- or two-unit gap is visible only if it is actually a delimiter.</summary>
    [Fact]
    public void Adjacent_line_merge_does_not_claim_hidden_prefix_text()
    {
        using var document = new Document("abc\nnext");
        var snapshot = document.Snapshot;
        var frame = new CanvasFrame(snapshot.Version, new ViewportAnchor(0, 0), 0,
            [new ViewportSlice(0, 0, 2, 0, 16, false, false),
                new ViewportSlice(1, 4, 4, 16, 16, false, false)], 0, 0);
        var model = new AccessibleDocument(new AccessibleCanvasState(1, snapshot, frame));

        Assert.Equal(2, model.VisibleRanges().Count); // Hidden "c\n" is not a delimiter.
    }

    /// <summary>Old range handles cannot describe a newly bound source document.</summary>
    [Fact]
    public void Rebind_rejects_stale_range_and_scrolls_only_current_source()
    {
        using var first = new Document("one");
        using var second = new Document("two");
        var model = new AccessibleDocument(Bind(first.Snapshot, 0, 0, generation: 1));
        var stale = model.DocumentRange;
        model.Publish(Bind(second.Snapshot, 0, 0, generation: 2));
        var viewport = new ViewportStub();
        var provider = new WindowsTextProviderCore(model, viewport);

        Assert.Throws<InvalidOperationException>(() => provider.GetText(stale, -1));
        Assert.Equal(WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE,
            provider.TryGetText(stale, -1).HResult);
        Assert.Throws<InvalidOperationException>(() => provider.ScrollIntoView(stale, false));
        Assert.Equal(AccessibleRevealResult.Revealed,
            provider.ScrollIntoView(model.MakeRange(2, 3), true));
        Assert.Equal(2, viewport.LastRange?.Start);
        Assert.True(viewport.AlignToTop);
    }

    /// <summary>Retained OS range handles cannot read a closed editor's snapshot.</summary>
    [Fact]
    public void Detach_denies_retained_range_text_and_scroll_without_closing_shared_document()
    {
        using var document = new Document("secret");
        var model = new AccessibleDocument(Bind(document.Snapshot, 0, 0));
        var provider = new WindowsTextProviderCore(model, new ViewportStub());
        var retained = provider.DocumentRange;
        provider.Detach();

        Assert.Equal(WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE,
            provider.TryGetText(retained, -1).HResult);
        Assert.Throws<InvalidOperationException>(() => provider.ScrollIntoView(retained, false));
        Assert.Equal("secret", model.GetText(model.DocumentRange));
        var nativePattern = new UiaEditorObject(provider);
        Assert.Equal(WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE,
            nativePattern.GetSelection(out var selected));
        Assert.Equal(0, selected);
        Assert.Equal(WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE,
            nativePattern.GetVisibleRanges(out var visible));
        Assert.Equal(0, visible);
        Assert.Equal(WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE,
            nativePattern.GetDocumentRange(out var documentRange));
        Assert.Equal(0, documentRange);
        Assert.Equal(WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE,
            new UiaRangeObject(provider, retained).Clone(out var clone));
        Assert.Equal(0, clone);
    }

    /// <summary>Detaching a faulty provider does not stop later edit/New publications.</summary>
    [Fact]
    public void Provider_detach_preserves_shared_document_for_edit_and_new()
    {
        using var first = new Document("before");
        using var second = new Document("new-document");
        var model = new AccessibleDocument(Bind(first.Snapshot, 0, 0));
        var staleProvider = new WindowsTextProviderCore(model, new ViewportStub());
        var oldRange = staleProvider.DocumentRange;
        staleProvider.Detach();

        var edited = first.Apply(new TextChange(first.Snapshot.Length, 0, "-edited"));
        model.Publish(Bind(edited, 0, 0));
        Assert.Equal("before-edited", model.GetText(model.DocumentRange));
        model.Publish(Bind(second.Snapshot, 0, 0, generation: 2));
        Assert.Equal("new-document", model.GetText(model.DocumentRange));
        Assert.Equal(WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE,
            staleProvider.TryGetText(oldRange, -1).HResult);
        var newProvider = new WindowsTextProviderCore(model, new ViewportStub());
        Assert.Equal("new-document", newProvider.GetText(newProvider.DocumentRange, -1));
    }

    /// <summary>Mac AX selector fault detachment cannot poison controller publication.</summary>
    [Fact]
    public void Mac_provider_detach_preserves_shared_document_for_new()
    {
        using var first = new Document("old");
        using var second = new Document("next");
        var model = new AccessibleDocument(Bind(first.Snapshot, 0, 0));
        var staleProvider = new MacAccessibilityTextCore(model, new ViewportStub());
        var oldRange = staleProvider.RangeForLine(0);
        staleProvider.Detach();

        model.Publish(Bind(second.Snapshot, 0, 0, generation: 2));
        Assert.Equal("next", model.GetText(model.DocumentRange));
        Assert.Throws<InvalidOperationException>(() => staleProvider.StringForRange(oldRange));
        var newProvider = new MacAccessibilityTextCore(model, new ViewportStub());
        Assert.Equal("next", newProvider.StringForRange(newProvider.RangeForLine(0)));
    }

    /// <summary>IME/thread/stale failures never report successful UIA ScrollIntoView.</summary>
    [Theory]
    [InlineData(1, WindowsTextResult.UIA_E_INVALIDOPERATION)] // CompositionBlocked
    [InlineData(3, WindowsTextResult.UIA_E_INVALIDOPERATION)] // NotVisible
    [InlineData(4, WindowsTextResult.UIA_E_INVALIDOPERATION)] // WrongThread
    [InlineData(2, WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE)] // StaleRange
    public void Reveal_failure_maps_to_explicit_uia_failure(
        int outcome, int expectedHResult)
    {
        using var document = new Document("offscreen");
        var model = new AccessibleDocument(Bind(document.Snapshot, 0, 0));
        var range = model.MakeRange(3, 6);
        var core = new WindowsTextProviderCore(model, new ViewportStub((AccessibleRevealResult)outcome));
        var nativeRange = new UiaRangeObject(core, range);

        Assert.Equal(expectedHResult, nativeRange.ScrollIntoView(0));
    }

    /// <summary>Closed COM ranges cannot keep a large engine snapshot alive.</summary>
    [Fact]
    public void Invalidate_releases_snapshot_even_with_retained_range_provider()
    {
        var (provider, range, snapshot) = ClosedRangeWithWeakSnapshot();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(snapshot.TryGetTarget(out _));
        Assert.Equal(WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE,
            provider.TryGetText(range, -1).HResult);
        GC.KeepAlive(provider);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WindowsTextProviderCore, AccessibleRange, WeakReference<TextSnapshot>)
        ClosedRangeWithWeakSnapshot()
    {
        using var document = new Document(new string('s', 4 * 1024 * 1024));
        var snapshot = document.Snapshot;
        var weak = new WeakReference<TextSnapshot>(snapshot);
        var model = new AccessibleDocument(Bind(snapshot, 0, 0));
        var provider = new WindowsTextProviderCore(model, new ViewportStub());
        var range = provider.DocumentRange;
        model.Invalidate();
        return (provider, range, weak);
    }

    private static AccessibleCanvasState Bind(TextSnapshot snapshot, int anchor, int active,
        long generation = 1)
    {
        var frame = new CanvasFrame(snapshot.Version, new ViewportAnchor(0, 0), 0,
            [new ViewportSlice(0, 0, Math.Min(snapshot.Length, 3), 0, 16, false, false)],
            anchor, active);
        return new AccessibleCanvasState(generation, snapshot, frame);
    }

    private sealed class ViewportStub : IAccessibleViewport
    {
        private readonly AccessibleRevealResult _result;
        internal ViewportStub(AccessibleRevealResult result = AccessibleRevealResult.Revealed) =>
            _result = result;
        internal AccessibleRange? LastRange { get; private set; }
        internal bool AlignToTop { get; private set; }

        public AccessibleRevealResult TryReveal(AccessibleRange range, bool alignToTop)
        {
            LastRange = range;
            AlignToTop = alignToTop;
            return _result;
        }
    }
}
