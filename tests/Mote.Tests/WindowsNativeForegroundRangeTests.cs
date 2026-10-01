using System.Reflection;
using Mote.Native;
using Mote.Native.Windows;
using Mote.Themes;

// Only pure managed helpers of the Windows-annotated probe are called here.
// No platform check is appropriate: these contracts must also execute on macOS.
#pragma warning disable CA1416

namespace Mote.Tests;

/// <summary>Portable foreground witness contracts; no native control or COM object is constructed.</summary>
public sealed class WindowsNativeForegroundRangeTests
{
    /// <summary>Distinct colors expose input order and gaps without depending on a built-in theme.</summary>
    private static readonly ThemeColor Default = new(10, 20, 30), Red = new(201, 2, 3), Blue = new(4, 5, 202);

    /// <summary>Document-only witnesses are bounded, unique and absent for empty source.</summary>
    [Theory]
    [InlineData("", new int[0])]
    [InlineData("x", new[] { 0 })]
    [InlineData("ab", new[] { 0, 1 })]
    [InlineData("abcdef", new[] { 0, 3, 5 })]
    public void DocumentSamples(string display, int[] expected) =>
        Assert.Equal(expected, WindowsNativeSourceCapabilityProbe.ForegroundSampleOffsets(display, []));

    /// <summary>A single semantic run includes both neighboring default-color witnesses.</summary>
    [Fact]
    public void RunInteriorAndAdjacentGapsAreSampled()
    {
        NativeSourceDiagnosticStyle[] styles = [new(3, 4, Red)];
        Assert.Equal(new[] { 0, 5, 9, 2, 3, 6, 7 },
            WindowsNativeSourceCapabilityProbe.ForegroundSampleOffsets("0123456789", styles));
        Assert.Equal(Default, WindowsNativeSourceCapabilityProbe.ExpectedForeground(2, styles, Default));
        Assert.Equal(Red, WindowsNativeSourceCapabilityProbe.ExpectedForeground(3, styles, Default));
        Assert.Equal(Red, WindowsNativeSourceCapabilityProbe.ExpectedForeground(6, styles, Default));
        Assert.Equal(Default, WindowsNativeSourceCapabilityProbe.ExpectedForeground(7, styles, Default));
    }

    /// <summary>Half-open spans obey publication order, not spatial sorting or longest-run priority.</summary>
    [Fact]
    public void OverlapIsLastWriterWinsAndZeroLengthCannotOverride()
    {
        NativeSourceDiagnosticStyle[] styles = [new(1, 7, Red), new(3, 2, Blue), new(3, 0, Default)];
        ThemeColor[] expected = [Default, Red, Red, Blue, Blue, Red, Red, Red, Default];
        for (var i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], WindowsNativeSourceCapabilityProbe.ExpectedForeground(i, styles, Default));
        NativeSourceDiagnosticStyle[] reverse = [styles[1], styles[0]];
        Assert.Equal(Red, WindowsNativeSourceCapabilityProbe.ExpectedForeground(3, reverse, Default));
    }

    /// <summary>Empty publications certify default foreground expectation rather than retaining prior styles.</summary>
    [Fact]
    public void EmptyAndZeroLengthPublicationExpectDefault()
    {
        NativeSourceDiagnosticStyle[] styles = [new(0, 0, Red), new(2, 0, Blue), new(4, 0, Red)];
        Assert.Equal(new[] { 0, 2, 3 }, WindowsNativeSourceCapabilityProbe.ForegroundSampleOffsets("abcd", styles));
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(Default, WindowsNativeSourceCapabilityProbe.ExpectedForeground(i, [], Default));
            Assert.Equal(Default, WindowsNativeSourceCapabilityProbe.ExpectedForeground(i, styles, Default));
        }
    }

    /// <summary>Scalar and paragraph witnesses use complete UTF-16 units; combining marks remain separate scalars.</summary>
    [Theory]
    [InlineData("😀", new[] { 0 }, new[] { 2 })]
    [InlineData("\r\n", new[] { 0 }, new[] { 2 })]
    [InlineData("A😀\r\nB", new[] { 0, 3, 5 }, new[] { 1, 5, 6 })]
    [InlineData("e\u0301", new[] { 0, 1 }, new[] { 1, 2 })]
    public void SamplesNeverReadHalfAScalarOrParagraph(string display, int[] offsets, int[] ends)
    {
        Assert.Equal(offsets, WindowsNativeSourceCapabilityProbe.ForegroundSampleOffsets(display, []));
        for (var i = 0; i < offsets.Length; i++)
            Assert.Equal(ends[i], WindowsNativeSourceCapabilityProbe.ForegroundSampleEnd(display, offsets[i]));
    }

    /// <summary>One emoji preserves two native UTF-16 units while one CRLF collapses to one paragraph unit.</summary>
    [Fact]
    public void SampleIntervalsMapToRichEditParagraphOffsets()
    {
        const string display = "A😀\r\nB";
        NativeSourceDiagnosticStyle[] styles = [new(1, 2, Red), new(3, 2, Blue)];
        Validate(display, styles);
        var map = new RichEditOffsetMap(display);
        Assert.Equal(1, map.ToNative(1));
        Assert.Equal(3, map.ToNative(WindowsNativeSourceCapabilityProbe.ForegroundSampleEnd(display, 1)));
        Assert.Equal(3, map.ToNative(3));
        Assert.Equal(4, map.ToNative(WindowsNativeSourceCapabilityProbe.ForegroundSampleEnd(display, 3)));
        foreach (var start in WindowsNativeSourceCapabilityProbe.ForegroundSampleOffsets(display, styles))
        {
            Assert.False(start == 2 || start == 4);
            Assert.True(map.ToNative(WindowsNativeSourceCapabilityProbe.ForegroundSampleEnd(display, start)) > map.ToNative(start));
        }
    }

    /// <summary>Invalid bounds and split source units must fail before publication, including zero-length split spans.</summary>
    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, -1)]
    [InlineData(7, 0)]
    [InlineData(6, 1)]
    [InlineData(1, int.MaxValue)]
    [InlineData(2, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(4, 0)]
    [InlineData(3, 1)]
    [InlineData(4, 1)]
    public void InvalidPublicationSpansAreRejected(int start, int length) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Validate("A😀\r\nB", [new(start, length, Red)]));

    /// <summary>Valid empty endpoints and whole units are admitted, including empty source.</summary>
    [Fact]
    public void ValidPublicationBoundariesAreAccepted()
    {
        Validate("", [new(0, 0, Red)]);
        Validate("A😀\r\nB", [new(0, 6, Red), new(1, 2, Blue), new(3, 2, Blue), new(6, 0, Red)]);
    }

    /// <summary>A deterministic forward-paint oracle independently checks arbitrary overlap resolution.</summary>
    [Fact]
    public void RandomOverlapsMatchForwardPaintingAndRemainBounded()
    {
        const int length = 257;
        var display = new string('x', length);
        var random = new Random(73041);
        for (var trial = 0; trial < 100; trial++)
        {
            var styles = new List<NativeSourceDiagnosticStyle>();
            var painted = Enumerable.Repeat(Default, length).ToArray();
            for (var j = 0; j < 43; j++)
            {
                var start = random.Next(length + 1);
                var count = random.Next(length - start + 1);
                var color = new ThemeColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
                styles.Add(new(start, count, color));
                for (var k = start; k < start + count; k++) painted[k] = color;
            }
            Validate(display, styles);
            for (var k = 0; k < length; k++)
                Assert.Equal(painted[k], WindowsNativeSourceCapabilityProbe.ExpectedForeground(k, styles, Default));
            var samples = WindowsNativeSourceCapabilityProbe.ForegroundSampleOffsets(display, styles);
            Assert.InRange(samples.Length, 1, 18);
            Assert.Equal(samples.Length, samples.Distinct().Count());
            Assert.All(samples, sample => Assert.InRange(sample, 0, length - 1));
        }
    }

    /// <summary>Invokes the production pre-mutation validator without creating its private native Host.</summary>
    private static void Validate(string display, IReadOnlyList<NativeSourceDiagnosticStyle> styles)
    {
        var host = typeof(WindowsNativeSourceCapabilityProbe).GetNestedType("Host", BindingFlags.NonPublic)!;
        var method = host.GetMethod("ValidateStyles", BindingFlags.Static | BindingFlags.NonPublic)!;
        try { method.Invoke(null, [display, styles]); }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }
}
