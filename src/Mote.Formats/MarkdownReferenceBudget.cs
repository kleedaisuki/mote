namespace Mote.Formats;

/// <summary>One explicit admission policy; accounting bytes and observed allocation are deliberately different quantities.</summary>
internal sealed record MarkdownReferenceLimits(long RetainedBytes = 8 * 1048576, long ThreadAllocatedBytes = 32 * 1048576,
    long WorkUnits = 1024L * 1048576, int ParserCalls = 4096, long ParserSourceUnits = 4 * 1048576);

/// <summary>Tracks an entire private build; no category may bypass the final publication gate.</summary>
internal sealed class MarkdownReferenceBudget(MarkdownReferenceLimits limits)
{
    /// <summary>Private-stage retained charge under the documented conservative accounting model, not measured heap.</summary>
    internal long Retained { get; private set; }
    /// <summary>Deterministic source/descriptor/hash/parser work charge.</summary>
    internal long Work { get; private set; }
    /// <summary>All parser calls, including definitions and independent blocks, not only masks.</summary>
    internal int Calls { get; private set; }
    /// <summary>Total input units of all charged opaque parser calls.</summary>
    internal long ParsedUnits { get; private set; }
    /// <summary>Allocation stamp is scoped to synchronous admission on this thread.</summary>
    private readonly long _allocatedStart = GC.GetAllocatedBytesForCurrentThread();
    /// <summary>Observed cumulative allocation; does not measure live heap, native memory or other threads.</summary>
    internal long Allocated => GC.GetAllocatedBytesForCurrentThread() - _allocatedStart;
    /// <summary>Charges before retaining a modeled object or growing its container.</summary>
    internal void Keep(long bytes)
    {
        if (bytes > limits.RetainedBytes - Retained) throw new Refused("retained-accounting");
        Retained += bytes;
    }
    /// <summary>Charges deterministic work before loops; exhaustion is not a source-grammar failure.</summary>
    internal void Visit(long units)
    {
        if (units > limits.WorkUnits - Work) throw new Refused("admission-work");
        Work += units;
    }
    /// <summary>Guards all opaque parser calls before and after; one in-progress call can exceed the observed threshold.</summary>
    internal T Parse<T>(int units, Func<T> call)
    {
        Poll();
        if (Calls >= limits.ParserCalls || units > limits.ParserSourceUnits - ParsedUnits) throw new Refused("parser-work");
        Visit(checked(32L * units)); Calls++; ParsedUnits += units;
        var value = call();
        Poll();
        return value;
    }
    /// <summary>Aborts at the first checked over-allocation step; this is detection, not a preemptive hard heap cap.</summary>
    internal void Poll()
    {
        if (Allocated > limits.ThreadAllocatedBytes) throw new Refused("thread-allocation");
    }
    /// <summary>Distinct fail-closed resource outcome; the caller preserves its committed state.</summary>
    internal sealed class Refused(string reason) : Exception(reason);
}
