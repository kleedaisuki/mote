using Mote.Formats;
using Mote.Native;
using Mote.Native.Mac;

namespace Mote.Tests;

/// <summary>Portable checks of AppKit's actual part ordinals and frozen logical-axis mapping.</summary>
public sealed class MacGridScrollInteropTests
{
    /// <summary>Each native part has an independent action; knob is not a decrement-line alias.</summary>
    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 90)]
    [InlineData(3, 50)]
    [InlineData(4, 39)]
    [InlineData(5, 41)]
    [InlineData(6, 90)]
    public void Parts_map_to_correct_logical_actions(int part, int expected)
    {
        var axis = NativeGridScrollAxis.Create(NativeGridExtentKind.Exact, 100, 10, 40);
        Assert.Equal(expected, MacGridScrollInterop.Target(part, axis, 40, 1));
    }

    /// <summary>Invalid parts and nonfinite knob values have no coordinate authority.</summary>
    [Fact]
    public void Invalid_input_is_not_navigation()
    {
        var axis = NativeGridScrollAxis.Create(NativeGridExtentKind.Prefix, 100, 10, 40);
        Assert.Null(MacGridScrollInterop.Target(0, axis, 40, 0));
        Assert.Null(MacGridScrollInterop.Target(99, axis, 40, 0));
        Assert.Null(MacGridScrollInterop.Target(2, axis, 40, double.NaN));
        Assert.Null(MacGridScrollInterop.Target(6, axis, 40, double.PositiveInfinity));
    }

    /// <summary>Repeated page actions use the captured previous origin and cannot wrap at int limits.</summary>
    [Fact]
    public void Repeated_pages_saturate_at_frozen_extent()
    {
        var axis = NativeGridScrollAxis.Create(NativeGridExtentKind.Exact, int.MaxValue, 256, 0);
        Assert.Equal(256, MacGridScrollInterop.Target(3, axis, 0, 0));
        Assert.Equal(512, MacGridScrollInterop.Target(3, axis, 256, 0));
        Assert.Equal(axis.Last, MacGridScrollInterop.Target(3, axis, int.MaxValue, 0));
        Assert.Equal(0, MacGridScrollInterop.Target(1, axis, 0, 0));
    }
    /// <summary>Pending native rows preserve only certified ordinal slots, never source-backed descriptors.</summary>
    [Theory]
    [InlineData(40, 10, 100, 10)]
    [InlineData(90, 20, 100, 10)]
    [InlineData(100, 10, 100, 0)]
    public void Pending_slots_are_bounded_and_origin_free(int first, int capacity, int count, int expected)
    {
        var slots = NativeGridPlanner.Slots(null, new GridRange(first, capacity), count);
        Assert.Equal(expected, slots.Length);
        Assert.All(slots, slot => Assert.Null(slot));
    }
}
