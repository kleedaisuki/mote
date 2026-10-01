using System.Runtime.InteropServices;
using Mote.Engine;
using Mote.Native.Accessibility;
using Mote.Native.Viewport;
using Mote.Native.Windows.Accessibility;
using Xunit.Abstractions;

namespace Mote.Tests;

/// <summary>Checks range mutation and identity through the published COM ABI, not managed shortcuts.</summary>
public sealed class WindowsUiaRangeContractTests(ITestOutputHelper output)
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CloneAbi(nint self, out nint range);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CompareAbi(nint self, nint other, out int equal);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CompareEndpointsAbi(nint self, int endpoint, nint other, int otherEndpoint, out int result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int MoveEndpointAbi(nint self, int endpoint, nint target, int targetEndpoint);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int MoveUnitAbi(nint self, int endpoint, int unit, int count, out int moved);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ExpandAbi(nint self, int unit);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetTextAbi(nint self, int maximum, out nint text);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SelectAbi(nint self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int MoveAbi(nint self, int unit, int count, out int moved);

    /// <summary>A clone initially compares equal, but subsequent endpoint mutations must be independent.</summary>
    [Fact]
    public void Clone_is_independent_and_comparison_uses_current_endpoints()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture("abcdefgh");
        using var original = fixture.Range(1, 4);
        Assert.Equal(0, original.Call<CloneAbi>(3)(original.Pointer, out var clonedPointer));
        using var clone = new RangeLease(clonedPointer);
        Assert.Equal(0, original.Call<CompareAbi>(4)(original.Pointer, clone.Pointer, out var equal));
        Assert.Equal(1, equal);
        using var target = fixture.Range(6, 8);
        Assert.Equal(0, clone.Call<MoveEndpointAbi>(15)(clone.Pointer, 1, target.Pointer, 0));
        Assert.Equal("bcd", original.Text());
        Assert.Equal("bcdef", clone.Text());
        Assert.Equal(0, original.Call<CompareAbi>(4)(original.Pointer, clone.Pointer, out equal));
        Assert.Equal(0, equal);
        Assert.Equal(0, clone.Call<CompareEndpointsAbi>(5)(clone.Pointer, 1, original.Pointer, 1, out var delta));
        Assert.True(delta > 0);
    }

    /// <summary>Moving either endpoint beyond the other collapses both endpoints at the destination.</summary>
    [Theory]
    [InlineData(0, 6)]
    [InlineData(1, 0)]
    public void Crossing_endpoint_collapses_range(int endpoint, int destination)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture("abcdefgh");
        using var range = fixture.Range(2, 4);
        using var target = fixture.Range(destination, destination);
        Assert.Equal(0, range.Call<MoveEndpointAbi>(15)(range.Pointer, endpoint, target.Pointer, 0));
        Assert.Equal("", range.Text());
        Assert.Equal(0, range.Call<CompareEndpointsAbi>(5)(range.Pointer, 0, target.Pointer, 0, out var delta));
        Assert.Equal(0, delta);
        Assert.Equal(0, range.Call<CompareEndpointsAbi>(5)(range.Pointer, 1, target.Pointer, 0, out delta));
        Assert.Equal(0, delta);
    }

    /// <summary>Equal source identity numbers do not authorize comparison or mutation across separate editors.</summary>
    [Fact]
    public void Foreign_editor_and_null_peers_are_rejected_without_mutation()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var first = new Fixture("abcdefgh");
        using var second = new Fixture("abcdefgh");
        using var range = first.Range(1, 3);
        using var foreign = second.Range(5, 7);
        foreach (var peer in new[] { foreign.Pointer, (nint)0 })
        {
            Assert.Equal(WindowsTextResult.E_INVALIDARG,
                range.Call<CompareAbi>(4)(range.Pointer, peer, out var equal));
            Assert.Equal(0, equal);
            Assert.Equal(WindowsTextResult.E_INVALIDARG,
                range.Call<CompareEndpointsAbi>(5)(range.Pointer, 0, peer, 0, out var delta));
            Assert.Equal(0, delta);
            Assert.Equal(WindowsTextResult.E_INVALIDARG,
                range.Call<MoveEndpointAbi>(15)(range.Pointer, 1, peer, 0));
        }
        Assert.Equal("bc", range.Text());
    }

    /// <summary>Detached providers fail every identity-sensitive operation instead of mutating retained ranges.</summary>
    [Fact]
    public void Detached_range_operations_fail_with_element_not_available()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture("abc\ndef");
        using var range = fixture.Range(0, 3);
        using var target = fixture.Range(4, 7);
        fixture.Core.Detach();
        Assert.Equal(WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE,
            range.Call<CompareAbi>(4)(range.Pointer, target.Pointer, out var equal));
        Assert.Equal(0, equal);
        Assert.Equal(WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE,
            range.Call<MoveEndpointAbi>(15)(range.Pointer, 0, target.Pointer, 0));
        Assert.Equal(WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE,
            range.Call<MoveUnitAbi>(14)(range.Pointer, 1, 3, 1, out var moved));
        Assert.Equal(0, moved);
    }

    /// <summary>A peer from an earlier file generation is unavailable even when its coordinates still fit.</summary>
    [Fact]
    public void New_file_generation_rejects_retained_peer_identity()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture("abcdefgh");
        using var retained = fixture.Range(1, 3);
        fixture.Republish(2);
        using var current = fixture.Range(1, 3);
        Assert.Equal(WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE,
            current.Call<CompareAbi>(4)(current.Pointer, retained.Pointer, out var equal));
        Assert.Equal(0, equal);
        Assert.Equal(WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE,
            current.Call<MoveEndpointAbi>(15)(current.Pointer, 1, retained.Pointer, 1));
        Assert.Equal("bc", current.Text());
    }

    /// <summary>Line expansion includes the source delimiter; document expansion includes all offscreen text.</summary>
    [Fact]
    public void Expansion_uses_logical_line_and_document_boundaries()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture("ab\r\ncd\nef");
        using var range = fixture.Range(5, 5);
        Assert.Equal(0, range.Call<ExpandAbi>(6)(range.Pointer, 3));
        Assert.Equal("cd\n", range.Text());
        Assert.Equal(0, range.Call<ExpandAbi>(6)(range.Pointer, 6));
        Assert.Equal("ab\r\ncd\nef", range.Text());
    }

    /// <summary>Expansion preserves the original quantity when both ends already delimit complete units.</summary>
    [Theory]
    [InlineData(0, "e\u0301😀Z", 0, 4, "e\u0301😀")]
    [InlineData(3, "ab\ncd\nef", 0, 6, "ab\ncd\n")]
    public void Expand_preserves_aligned_multiple_complete_units(
        int unit, string source, int start, int end, string expected)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture(source);
        using var range = fixture.Range(start, end);
        Assert.Equal(0, range.Call<ExpandAbi>(6)(range.Pointer, unit));
        Assert.Equal(expected, range.Text());
    }

    /// <summary>A misaligned interval normalizes to the single enclosing unit at its start.</summary>
    [Theory]
    [InlineData(0, "e\u0301😀Z", 1, 4, "e\u0301")]
    [InlineData(3, "ab\ncd\nef", 1, 6, "ab\n")]
    public void Expand_normalizes_misaligned_range_to_start_unit(
        int unit, string source, int start, int end, string expected)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture(source);
        using var range = fixture.Range(start, end);
        Assert.Equal(0, range.Call<ExpandAbi>(6)(range.Pointer, unit));
        Assert.Equal(expected, range.Text());
    }

    /// <summary>Move normalizes a nonempty range to one unit, unlike aligned multi-unit Expand.</summary>
    [Theory]
    [InlineData(0, "e\u0301😀Z", 0, 4, "😀")]
    [InlineData(3, "ab\ncd\nef", 0, 6, "cd\n")]
    public void Move_normalizes_aligned_multiple_units_to_one_target_unit(
        int unit, string source, int start, int end, string expected)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture(source);
        using var range = fixture.Range(start, end);
        Assert.Equal(0, range.Call<MoveAbi>(13)(range.Pointer, unit, 1, out var moved));
        Assert.Equal(1, moved);
        Assert.Equal(expected, range.Text());
    }

    /// <summary>Endpoint movement reports actual units after clipping to the document boundary.</summary>
    [Fact]
    public void Endpoint_line_and_document_movement_reports_actual_units()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture("ab\ncd\nef");
        using var range = fixture.Range(0, 0);
        Assert.Equal(0, range.Call<MoveUnitAbi>(14)(range.Pointer, 1, 3, 1, out var moved));
        Assert.Equal(1, moved);
        Assert.Equal("ab\n", range.Text());
        Assert.Equal(0, range.Call<MoveUnitAbi>(14)(range.Pointer, 1, 6, 50, out moved));
        Assert.Equal(1, moved);
        Assert.Equal("ab\ncd\nef", range.Text());
        Assert.Equal(0, range.Call<MoveUnitAbi>(14)(range.Pointer, 1, 6, 1, out moved));
        Assert.Equal(0, moved);
    }

    /// <summary>Invalid endpoint or unit values fail before changing range coordinates.</summary>
    [Fact]
    public void Invalid_navigation_arguments_preserve_the_range()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture("abc\ndef");
        using var range = fixture.Range(1, 3);
        Assert.Equal(WindowsTextResult.E_INVALIDARG,
            range.Call<MoveUnitAbi>(14)(range.Pointer, 2, 3, 1, out var moved));
        Assert.Equal(0, moved);
        Assert.Equal(WindowsTextResult.E_INVALIDARG,
            range.Call<MoveUnitAbi>(14)(range.Pointer, 1, 7, 1, out moved));
        Assert.Equal(0, moved);
        Assert.Equal("bc", range.Text());
    }

    /// <summary>Character movement counts extended graphemes, not UTF-16 halves or CRLF halves.</summary>
    [Fact]
    public void Character_movement_preserves_combining_astral_and_crlf_clusters()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture("e\u0301😀\r\nZ");
        using var range = fixture.Range(0, 0);
        foreach (var expected in new[] { "e\u0301", "e\u0301😀", "e\u0301😀\r\n", "e\u0301😀\r\nZ" })
        {
            Assert.Equal(0, range.Call<MoveUnitAbi>(14)(range.Pointer, 1, 0, 1, out var moved));
            Assert.Equal(1, moved);
            Assert.Equal(expected, range.Text());
        }
        Assert.Equal(0, range.Call<MoveUnitAbi>(14)(range.Pointer, 1, 0, -2, out var reverse));
        Assert.Equal(-2, reverse);
        Assert.Equal("e\u0301😀", range.Text());
    }

    /// <summary>A grapheme operation exceeding the provider budget fails atomically, not with partial movement.</summary>
    [Fact]
    public void Oversized_character_navigation_fails_without_endpoint_changes()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture(new string('x', 65_537));
        using var range = fixture.Range(1, 3);
        Assert.Equal(WindowsTextResult.UIA_E_INVALIDOPERATION,
            range.Call<MoveUnitAbi>(14)(range.Pointer, 1, 0, 1, out var moved));
        Assert.Equal(0, moved);
        Assert.Equal("xx", range.Text());
        Assert.Equal(WindowsTextResult.UIA_E_INVALIDOPERATION,
            range.Call<ExpandAbi>(6)(range.Pointer, 0));
        Assert.Equal("xx", range.Text());
    }

    /// <summary>Select delegates the mutated absolute interval and exposes failure rather than false success.</summary>
    [Theory]
    [InlineData((int)AccessibleSelectionResult.Selected, 0)]
    [InlineData((int)AccessibleSelectionResult.StaleRange, WindowsTextResult.UIA_E_ELEMENTNOTAVAILABLE)]
    [InlineData((int)AccessibleSelectionResult.CompositionBlocked, WindowsTextResult.UIA_E_INVALIDOPERATION)]
    [InlineData((int)AccessibleSelectionResult.WrongThread, WindowsTextResult.UIA_E_INVALIDOPERATION)]
    [InlineData((int)AccessibleSelectionResult.InvalidBoundary, WindowsTextResult.UIA_E_INVALIDOPERATION)]
    public void Select_passes_current_range_to_optional_canonical_selection(
        int outcome, int expected)
    {
        if (!OperatingSystem.IsWindows()) return;
        var selection = new SelectionStub((AccessibleSelectionResult)outcome);
        using var fixture = new Fixture("abcdefgh", selection);
        using var range = fixture.Range(1, 3);
        using var target = fixture.Range(5, 5);
        Assert.Equal(0, range.Call<MoveEndpointAbi>(15)(range.Pointer, 1, target.Pointer, 0));
        Assert.Equal(expected, range.Call<SelectAbi>(16)(range.Pointer));
        Assert.Equal(1, selection.Calls);
        Assert.Equal(1, selection.Last.Start);
        Assert.Equal(5, selection.Last.End);
        Assert.Equal("bcde", range.Text());
    }

    /// <summary>An editor without canonical selection support must not claim that Select changed anything.</summary>
    [Fact]
    public void Select_without_callback_fails_explicitly()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture("abcdefgh");
        using var range = fixture.Range(1, 3);
        Assert.Equal(WindowsTextResult.UIA_E_INVALIDOPERATION, range.Call<SelectAbi>(16)(range.Pointer));
        Assert.Equal("bc", range.Text());
    }

    /// <summary>Nonempty movement normalizes to a unit and stops on the final nonempty unit.</summary>
    [Fact]
    public void Move_normalizes_nonempty_range_and_clips_at_last_unit()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture("ab\ncd\nef");
        using var range = fixture.Range(1, 2);
        Assert.Equal(0, range.Call<MoveAbi>(13)(range.Pointer, 3, 1, out var moved));
        Assert.Equal(1, moved);
        Assert.Equal("cd\n", range.Text());
        Assert.Equal(0, range.Call<MoveAbi>(13)(range.Pointer, 3, 100, out moved));
        Assert.Equal(1, moved);
        Assert.Equal("ef", range.Text());
        Assert.Equal(0, range.Call<MoveAbi>(13)(range.Pointer, 3, 1, out moved));
        Assert.Equal(0, moved);
        Assert.Equal("ef", range.Text());
    }

    /// <summary>Moving backwards from inside a line reaches its start as one unit.</summary>
    [Fact]
    public void Negative_line_endpoint_movement_from_interior_reaches_current_line_start()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture("ab\ncd\nef");
        using var range = fixture.Range(0, 5);
        Assert.Equal(0, range.Call<MoveUnitAbi>(14)(range.Pointer, 1, 3, -1, out var moved));
        Assert.Equal(-1, moved);
        Assert.Equal("ab\n", range.Text());
    }

    /// <summary>Zero-count navigation does not normalize, collapse, or otherwise change an existing range.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(6)]
    public void Zero_count_navigation_preserves_coordinates(int unit)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture("ab\ncd\nef");
        using var range = fixture.Range(1, 5);
        Assert.Equal(0, range.Call<MoveAbi>(13)(range.Pointer, unit, 0, out var moved));
        Assert.Equal(0, moved);
        Assert.Equal("b\ncd", range.Text());
        Assert.Equal(0, range.Call<MoveUnitAbi>(14)(range.Pointer, 0, unit, 0, out moved));
        Assert.Equal(0, moved);
        Assert.Equal("b\ncd", range.Text());
    }

    /// <summary>Many individually short lines do not bypass the total grapheme-navigation work budget.</summary>
    [Fact]
    public void Character_navigation_total_budget_failure_is_atomic()
    {
        if (!OperatingSystem.IsWindows()) return;
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        using var fixture = new Fixture(string.Concat(Enumerable.Repeat("a\n", 40_000)));
        using var range = fixture.Range(0, 1);
        var setupMilliseconds = elapsed.Elapsed.TotalMilliseconds;
        elapsed.Restart();
        Assert.Equal(WindowsTextResult.UIA_E_INVALIDOPERATION,
            range.Call<MoveUnitAbi>(14)(range.Pointer, 1, 0, int.MaxValue, out var moved));
        output.WriteLine($"Setup: {setupMilliseconds:F3} ms; navigation: {elapsed.Elapsed.TotalMilliseconds:F3} ms.");
        Assert.Equal(0, moved);
        Assert.Equal("a", range.Text());
    }

    /// <summary>Creates a source-only provider with no OS window or input focus dependencies.</summary>
    private sealed class Fixture : IDisposable
    {
        private readonly Document _source;
        private readonly AccessibleDocument _accessible;
        internal WindowsTextProviderCore Core { get; }

        internal Fixture(string text, IAccessibleViewport? viewport = null)
        {
            _source = new Document(text);
            var snapshot = _source.Snapshot;
            var frame = new CanvasFrame(snapshot.Version, new ViewportAnchor(0, 0), 0, [], 0, 0);
            _accessible = new AccessibleDocument(new AccessibleCanvasState(1, snapshot, frame));
            Core = new WindowsTextProviderCore(_accessible, viewport ?? new ViewportStub());
        }

        internal RangeLease Range(int start, int end) => new(UiaComInterface.Pointer(
            new UiaRangeObject(Core, _accessible.MakeRange(start, end)), typeof(ITextRangeProviderAbi).GUID));

        /// <summary>Changes file identity without changing text, exposing stale-peer checks independently.</summary>
        internal void Republish(long generation)
        {
            var snapshot = _source.Snapshot;
            var frame = new CanvasFrame(snapshot.Version, new ViewportAnchor(0, 0), 0, [], 0, 0);
            _accessible.Publish(new AccessibleCanvasState(generation, snapshot, frame));
        }

        public void Dispose() => _source.Dispose();
    }

    /// <summary>Owns exactly one COM reference and invokes the native range vtable directly.</summary>
    private sealed class RangeLease(nint pointer) : IDisposable
    {
        internal nint Pointer { get; } = pointer;
        internal T Call<T>(int slot) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(
            Marshal.ReadIntPtr(Marshal.ReadIntPtr(Pointer), slot * IntPtr.Size));

        internal string Text()
        {
            Assert.Equal(0, Call<GetTextAbi>(12)(Pointer, -1, out var text));
            try { return Marshal.PtrToStringBSTR(text); }
            finally { Marshal.FreeBSTR(text); }
        }

        public void Dispose() => Marshal.Release(Pointer);
    }

    /// <summary>A deterministic reveal sink; these tests never request native viewport changes.</summary>
    private sealed class ViewportStub : IAccessibleViewport
    {
        public AccessibleRevealResult TryReveal(AccessibleRange range, bool alignToTop) => AccessibleRevealResult.Revealed;
    }

    /// <summary>Records the exact range without introducing native selection or focus dependencies.</summary>
    private sealed class SelectionStub(AccessibleSelectionResult result) : IAccessibleViewport, IAccessibleSelection
    {
        internal int Calls { get; private set; }
        internal AccessibleRange Last { get; private set; }
        public AccessibleRevealResult TryReveal(AccessibleRange range, bool alignToTop) => AccessibleRevealResult.Revealed;
        public AccessibleSelectionResult TrySelect(AccessibleRange range)
        {
            Calls++;
            Last = range;
            return result;
        }
    }
}
