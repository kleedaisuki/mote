using Mote.Native.Mac;

namespace Mote.Tests;

/// <summary>Pure bounded geometry checks; these do not load AppKit or certify native selector ABI.</summary>
public sealed class MacGridAccessibilityGeometryTests
{
#pragma warning disable CA1416 // Pure managed clipping only; no macOS API is invoked.
    [Fact]
    public void ClipUsesActualIntersection()
    {
        var rect = MacCsvGrid.ClipAccessibilityRect(new(5, 8, 20, 12), new(10, 10, 10, 6));
        Assert.Equal(new ObjC.Rect(10, 10, 10, 6), rect);
    }

    [Fact]
    public void ClipPreservesNegativeCoordinateSpace()
    {
        var rect = MacCsvGrid.ClipAccessibilityRect(new(-20, -10, 12, 20), new(-16, -8, 16, 8));
        Assert.Equal(new ObjC.Rect(-16, -8, 8, 8), rect);
    }

    [Fact]
    public void NonoverlappingOrEdgeTouchingBoundsAreEmpty()
    {
        Assert.Equal(default, MacCsvGrid.ClipAccessibilityRect(new(0, 0, 10, 10), new(10, 0, 5, 5)));
        Assert.Equal(default, MacCsvGrid.ClipAccessibilityRect(new(0, 0, 10, 10), new(11, 12, 5, 5)));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void UnusableNativeGeometryNeverClaimsAScreenHit(double invalid)
    {
        Assert.Equal(default, MacCsvGrid.ClipAccessibilityRect(new(invalid, 0, 10, 10), new(0, 0, 10, 10)));
        Assert.Equal(default, MacCsvGrid.ClipAccessibilityRect(new(0, 0, invalid, 10), new(0, 0, 10, 10)));
    }
#pragma warning restore CA1416
}
