using System.Runtime.InteropServices;
using Mote.Formats;
using Mote.Native;
using Mote.Native.Mac;

namespace Mote.Tests;

/// <summary>Portable fixture and native aggregate layout checks; no native ABI execution is claimed.</summary>
public sealed class MacGridAccessibilityInteropTests
{
#pragma warning disable CA1416 // Fixture construction and managed layout inspection do not call AppKit.
    [Fact]
    public void TargetProbeFixtureHasValidSparseReadyAndMissingOrigins()
    {
        var projection = MacCsvGridAccessibilityProbe.CreateProjection();
        Assert.Equal(new GridRange(1000, 3), projection.RequestedRows);
        Assert.Equal(new GridRange(16, 2), projection.RequestedColumns);
        Assert.Equal([1000, 1002], projection.Rows.Select(r => r.Ordinal));
        var missing = projection.Rows[0].Cells[1];
        Assert.Equal(GridValueState.Missing, missing.State);
        Assert.Null(missing.SourceRange);
        Assert.True(missing.Column >= projection.Rows[0].Width);
        var oversized = projection.Rows[1].Cells[1];
        Assert.Equal(GridValueState.Oversized, oversized.State);
        Assert.NotNull(oversized.SourceRange);
        Assert.True(projection.Rows[0].RecordDelimiter.End <= projection.Rows[1].SourceRange.Start);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FocusedNativeIntentPreservesMissingAdmissionButRefusesPending(bool replace)
    {
        var kind = replace ? NativeGridIntentKind.Replace : NativeGridIntentKind.Reveal;
        var projection = MacCsvGridAccessibilityProbe.CreateProjection();
        var opening = new NativePresentationId(new(51, 1), 2);
        foreach (var coordinate in new[] { new GridCoordinate(1000, 16), new(1000, 17), new(1002, 17) })
        {
            var captured = MacCsvGrid.CaptureFocusedIntent(projection, opening, kind, coordinate);
            Assert.Equal(new NativeGridIntent(opening, kind, coordinate.Row, coordinate.Column), captured);
        }
        Assert.Null(MacCsvGrid.CaptureFocusedIntent(projection, opening, kind, new(1001, 16)));
        Assert.Null(MacCsvGrid.CaptureFocusedIntent(projection, opening, kind, new(1000, 18)));
        Assert.Null(MacCsvGrid.CaptureFocusedIntent(projection, opening, kind, new(1003, 16)));
    }

    [Fact]
    public void NativeAggregatesUsePointerSizedRangesAndDoubleGeometry()
    {
        Assert.Equal(16, Marshal.SizeOf<ObjC.Range>());
        Assert.Equal(16, Marshal.SizeOf<ObjC.Point>());
        Assert.Equal(16, Marshal.SizeOf<ObjC.Size>());
        Assert.Equal(32, Marshal.SizeOf<ObjC.Rect>());
    }
#pragma warning restore CA1416
}
