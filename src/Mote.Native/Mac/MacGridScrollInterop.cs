namespace Mote.Native.Mac;

/// <summary>Pure AppKit part translation, independent of process-local native handles.</summary>
/// <remarks>NSScrollerPart ordinals follow the AppKit header, not Windows scrollbar constants.</remarks>
internal static class MacGridScrollInterop
{
    /// <summary>Translates one action against the gesture's frozen denominator and previous origin.</summary>
    internal static int? Target(int part, NativeGridScrollAxis axis, int previous, double normalized)
    {
        previous = Math.Clamp(previous, 0, axis.Last);
        return part switch
        {
            1 => (int)Math.Clamp((long)previous - axis.Page, 0, axis.Last), // NSScrollerDecrementPage
            2 or 6 => axis.FromNormalized(normalized), // NSScrollerKnob / NSScrollerKnobSlot
            3 => (int)Math.Clamp((long)previous + axis.Page, 0, axis.Last), // NSScrollerIncrementPage
            4 => Math.Max(0, previous - 1), // NSScrollerDecrementLine
            5 => (int)Math.Clamp((long)previous + 1, 0, axis.Last), // NSScrollerIncrementLine
            _ => null
        };
    }
}
