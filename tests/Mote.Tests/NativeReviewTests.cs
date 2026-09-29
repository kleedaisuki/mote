using Mote.Native;
using Mote.Native.Windows;

namespace Mote.Tests;

/// <summary>Regression checks for native-control coordinate spaces.</summary>
public sealed class NativeReviewTests
{
    /// <summary>RichEdit's single-CR positions differ from CRLF display offsets after each line.</summary>
    [Fact]
    public void Rich_edit_offsets_account_for_every_display_newline()
    {
        const string display = "a\r\nb\r\n😀c";
        var offsets = new RichEditOffsetMap(display);

        Assert.Equal(2, offsets.NewlineCount);
        Assert.Equal(2, offsets.ToNative(3)); // b, after the first CRLF.
        Assert.Equal(4, offsets.ToNative(6)); // emoji, after the second CRLF.
        Assert.Equal(6, offsets.ToNative(8)); // c, after a surrogate pair.
        Assert.Equal(3, offsets.ToDisplay(2));
        Assert.Equal(6, offsets.ToDisplay(4));
        Assert.Equal(8, offsets.ToDisplay(6));
    }

    /// <summary>All native caret positions map back to exactly one valid display boundary.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("one line")]
    [InlineData("\r\n")]
    [InlineData("a\r\nb\r\nc")]
    [InlineData("😀\r\nx\r\n")]
    public void Rich_edit_native_selection_round_trips(string display)
    {
        var offsets = new RichEditOffsetMap(display);
        var nativeLength = display.Length - offsets.NewlineCount;
        for (var native = 0; native <= nativeLength; native++)
            Assert.Equal(native, offsets.ToNative(offsets.ToDisplay(native)));
    }

    /// <summary>Preview spans must cross both source-to-display and display-to-native maps.</summary>
    [Fact]
    public void Rich_edit_preview_span_maps_source_after_mixed_newlines()
    {
        const string sourcePreview = "head\nbody\r\ntail";
        var projection = new NativeTextProjection(sourcePreview, NativeLineEndingMode.CrLf);
        var offsets = new RichEditOffsetMap(projection.Display);
        var sourceStart = sourcePreview.IndexOf("tail", StringComparison.Ordinal);
        var displayStart = projection.ToDisplay(sourceStart);

        Assert.Equal("head\r\nbody\r\ntail", projection.Display);
        Assert.Equal(12, displayStart);
        Assert.Equal(10, offsets.ToNative(displayStart));
        Assert.Equal(displayStart, offsets.ToDisplay(offsets.ToNative(displayStart)));
    }
}
