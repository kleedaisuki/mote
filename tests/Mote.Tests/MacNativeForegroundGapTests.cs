using Mote.Native;
using Mote.Native.Mac;
using Mote.Themes;

namespace Mote.Tests;

/// <summary>Portable contracts for complete Mac foreground coverage; no AppKit calls occur.</summary>
public sealed class MacNativeForegroundGapTests
{
    /// <summary>Default foreground differs from both overlay colors.</summary>
    private static readonly ThemeColor Default = new(30, 30, 30);
    /// <summary>First overlay value used in overlap and equality contracts.</summary>
    private static readonly ThemeColor Red = new(255, 0, 0);
    /// <summary>Second overlay value exposes incorrect overlay reordering.</summary>
    private static readonly ThemeColor Blue = new(0, 0, 255);

    /// <summary>Unsorted nested, touching and zero-width overlays retain every uncovered boundary.</summary>
    [Fact]
    public void GapsAreTheComplementOfTheWholeUnion()
    {
        NativeSourceDiagnosticStyle[] styles =
        [new(8, 2, Red), new(3, 2, Red), new(4, 4, Blue), new(2, 0, Red)];
        Assert.Equal([new NativeSourceDiagnosticRange(0, 3), new(10, 2)],
            MacNativeForegroundPublication.UncoveredRanges(12, styles));
        Assert.Equal([new NativeSourceDiagnosticRange(0, 12)],
            MacNativeForegroundPublication.UncoveredRanges(12, []));
        Assert.Empty(MacNativeForegroundPublication.UncoveredRanges(0, [new(0, 0, Red)]));
        Assert.Empty(MacNativeForegroundPublication.UncoveredRanges(12, [new(0, 12, Red)]));
    }

    /// <summary>All invalid spans fail during planning, before a native caller can begin mutation.</summary>
    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, -1)]
    [InlineData(13, 0)]
    [InlineData(12, 1)]
    [InlineData(1, int.MaxValue)]
    [InlineData(int.MaxValue, 1)]
    public void InvalidSpansAreRefused(int start, int length) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MacNativeForegroundPublication.UncoveredRanges(12, [new(start, length, Red)]));

    /// <summary>A maximal valid UTF-16 length uses subtraction, not overflowing end arithmetic.</summary>
    [Fact]
    public void MaximumLengthRemainsRepresentable()
    {
        Assert.Equal([new NativeSourceDiagnosticRange(0, int.MaxValue - 1)],
            MacNativeForegroundPublication.UncoveredRanges(int.MaxValue, [new(int.MaxValue - 1, 1, Red)]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MacNativeForegroundPublication.UncoveredRanges(-1, []));
        Assert.Throws<ArgumentNullException>(() =>
            MacNativeForegroundPublication.UncoveredRanges(0, null!));
    }

    /// <summary>Native effective runs may start earlier and be shorter than the requested style.</summary>
    [Fact]
    public void EffectiveRangesClipAndAdvance()
    {
        Assert.Equal(5, MacNativeForegroundPublication.EffectiveEnd(12, 3, 10, 0, 5));
        Assert.Equal(10, MacNativeForegroundPublication.EffectiveEnd(12, 3, 10, 3, 9));
        Assert.Equal(4, MacNativeForegroundPublication.EffectiveEnd(12, 3, 10, 3, 1));
        Assert.Equal(int.MaxValue, MacNativeForegroundPublication.EffectiveEnd(
            int.MaxValue, int.MaxValue - 1, int.MaxValue, 0, int.MaxValue));
    }

    /// <summary>Empty, disjoint, overflowing or out-of-document native ranges cannot make traversal loop.</summary>
    [Theory]
    [InlineData(3, 0)]
    [InlineData(0, 3)]
    [InlineData(4, 1)]
    [InlineData(3, 10)]
    [InlineData(13, 1)]
    public void InvalidEffectiveRangesFail(int location, int count) =>
        Assert.Throws<InvalidOperationException>(() =>
            MacNativeForegroundPublication.EffectiveEnd(12, 3, 10, (nuint)location, (nuint)count));

    /// <summary>Native unsigned overflow and inconsistent caller boundaries are refused.</summary>
    [Fact]
    public void EffectiveRangeUnsignedLimitsFail()
    {
        Assert.Throws<InvalidOperationException>(() =>
            MacNativeForegroundPublication.EffectiveEnd(12, 3, 10, nuint.MaxValue, 2));
        Assert.Throws<InvalidOperationException>(() =>
            MacNativeForegroundPublication.EffectiveEnd(12, 3, 10, 3, nuint.MaxValue));
        Assert.Throws<InvalidOperationException>(() =>
            MacNativeForegroundPublication.EffectiveEnd(12, 10, 10, 0, 12));
        Assert.Throws<InvalidOperationException>(() =>
            MacNativeForegroundPublication.EffectiveEnd(12, 3, 13, 0, 12));
    }

    /// <summary>UTF-16 coordinates, including CRLF and surrogate locations, are not rewritten by planning.</summary>
    [Fact]
    public void UnicodeAndDelimiterCoordinatesStayExact()
    {
        var text = "中\r\n😀e\u0301\r尾\n";
        NativeSourceDiagnosticStyle[] styles = [new(3, 2, Red), new(5, 2, Blue)];
        Assert.Equal([new NativeSourceDiagnosticRange(0, 3), new(7, text.Length - 7)],
            MacNativeForegroundPublication.UncoveredRanges(text.Length, styles));
    }

    /// <summary>
    /// Arbitrary initial foregrounds and arbitrary overlay order yield exactly
    /// the old full-default-reset result at every position. Colors equal to
    /// default, disappeared tokens, nested/crossing overlaps and gaps participate.
    /// </summary>
    [Fact]
    public void RandomizedUnionPublicationMatchesTheOriginalOverlay()
    {
        var random = new Random(20261002);
        ThemeColor[] palette = [Default, Red, Blue];
        for (var iteration = 0; iteration < 3000; iteration++)
        {
            var length = random.Next(0, 128);
            var initial = Enumerable.Range(0, length).Select(_ => palette[random.Next(palette.Length)]).ToArray();
            var styles = Enumerable.Range(0, random.Next(0, 60)).Select(_ =>
            {
                var start = random.Next(length + 1);
                return new NativeSourceDiagnosticStyle(start, random.Next(length - start + 1),
                    palette[random.Next(palette.Length)]);
            }).ToArray();
            var expected = Enumerable.Repeat(Default, length).ToArray();
            foreach (var style in styles) Paint(expected, style.Start, style.Length, style.Foreground);
            var actual = initial.ToArray();
            var gaps = MacNativeForegroundPublication.UncoveredRanges(length, styles);
            foreach (var gap in gaps) Paint(actual, gap.Start, gap.Length, Default);
            foreach (var style in styles) Paint(actual, style.Start, style.Length, style.Foreground);
            Assert.Equal(expected, actual);
            foreach (var position in Enumerable.Range(0, length))
                Assert.Equal(expected[position], MacNativeForegroundPublication.ExpectedColor(position, Default, styles));
            var samples = MacNativeForegroundPublication.SampleLocations(length, styles, gaps);
            Assert.True(samples.Length <= 21);
            Assert.All(samples, position => Assert.InRange(position, 0, length - 1));
            Assert.Equal(samples.Distinct().Order().ToArray(), samples);
        }
    }

    /// <summary>Readback includes ordinary styled and unstyled locations without sampling every character.</summary>
    [Fact]
    public void BoundedSamplesIncludeStyledAndUnstyledLocations()
    {
        NativeSourceDiagnosticStyle[] styles = [new(3, 3, Red), new(8, 1, Blue), new(11, 3, Red)];
        var gaps = MacNativeForegroundPublication.UncoveredRanges(18, styles);
        var samples = MacNativeForegroundPublication.SampleLocations(18, styles, gaps);
        Assert.Contains(0, samples);
        Assert.Contains(4, samples);
        Assert.Contains(8, samples);
        Assert.Contains(12, samples);
        Assert.Contains(17, samples);
    }

    /// <summary>A model mutation skips equal locations without altering its final foreground.</summary>
    private static void Paint(ThemeColor[] colors, int start, int length, ThemeColor color)
    {
        for (var position = start; position < start + length; position++)
            if (colors[position] != color) colors[position] = color;
    }
}
