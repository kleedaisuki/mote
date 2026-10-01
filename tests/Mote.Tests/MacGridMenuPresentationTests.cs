using System.Reflection;
using Mote.Native.Mac;

namespace Mote.Tests;

/// <summary>Portable deferred-request/geometry/ABI contracts; real NSMenu tracking remains target-only.</summary>
public sealed class MacGridMenuPresentationTests
{
    [Fact]
    public void QueueIsSingleUseAndRejectsDuplicateOrReentrantPresentation()
    {
        var state = new MacCsvGridMenuPresentation();
        Assert.True(state.TryQueue(7));
        Assert.False(state.TryQueue(7));
        Assert.True(state.TryBegin(7, true));
        Assert.False(state.TryQueue(7));
        Assert.False(state.TryBegin(7, true));
        state.End();
        Assert.True(state.TryQueue(8));
        Assert.True(state.TryBegin(8, true));
    }

    [Theory]
    [InlineData(8, true)]
    [InlineData(7, false)]
    public void DetachedOrSupersededRequestIsConsumedWithoutPresentation(long current, bool attached)
    {
        var state = new MacCsvGridMenuPresentation();
        Assert.True(state.TryQueue(7));
        Assert.False(state.TryBegin(current, attached));
        Assert.False(state.TryBegin(7, true));
        Assert.True(state.TryQueue(9));
    }

    [Fact]
    public void CancellationInvalidatesOnlyPendingState()
    {
        var state = new MacCsvGridMenuPresentation();
        Assert.True(state.TryQueue(7));
        state.CancelPending();
        Assert.False(state.TryBegin(7, true));
        Assert.True(state.TryQueue(8));
        Assert.True(state.TryBegin(8, true));
        state.CancelPending();
        Assert.False(state.TryQueue(9));
        state.End();
        Assert.True(state.TryQueue(9));
    }

    [Fact]
    public void PopupAnchorUsesActualVisibleRectangleAndRefusesNonfiniteOrEmptyBounds()
    {
#pragma warning disable CA1416 // Pure coordinate arithmetic does not call AppKit.
        Assert.True(MacCsvGrid.TryAccessibilityMenuAnchor(new(120, 640, 80, 40), out var point));
        Assert.Equal(new ObjC.Point(160, 660), point);
        foreach (var rect in new[] { new ObjC.Rect(0, 0, 0, 1), new(0, 0, 1, -1),
            new(double.NaN, 0, 1, 1), new(0, 0, double.PositiveInfinity, 1),
            new(double.MaxValue, 0, double.MaxValue, 1) })
            Assert.False(MacCsvGrid.TryAccessibilityMenuAnchor(rect, out _));
#pragma warning restore CA1416
    }

    [Fact]
    public void PopupBridgeUsesByteReturnAndNativePointArgument()
    {
#pragma warning disable CA1416 // Metadata inspection does not execute the native ABI.
        var method = typeof(MacCsvGrid).GetMethod("AccessibilityPopUpMenu", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Equal(typeof(byte), method.ReturnType);
        Assert.Equal([typeof(nint), typeof(nint), typeof(nint), typeof(ObjC.Point), typeof(nint)],
            method.GetParameters().Select(p => p.ParameterType));
#pragma warning restore CA1416
    }

    [Theory]
    [InlineData((int)MacCsvGridMenuPhase.ScheduleReturn, "schedule-return")]
    [InlineData((int)MacCsvGridMenuPhase.PopupReturn, "popup-return")]
    public void AdmissionAndPopupReturnsDoNotInventOpenCallbacks(int phaseValue, string name)
    {
        var phase = (MacCsvGridMenuPhase)phaseValue;
        var counter = new MacCsvGridMenuDiagnostic();
        Assert.True(counter.TryNext(phase, out var trace));
        Assert.Equal(0, trace.Opens);
        Assert.Equal(0, trace.Closes);
        Assert.False(trace.Open);
        Assert.Contains($"phase={name}", trace.Format(new(false, 0, false, false, false, false, false), false));
        Assert.Throws<ArgumentException>(() => trace.Format(new(false, 0, false, false, false, false, false)));
    }
}
