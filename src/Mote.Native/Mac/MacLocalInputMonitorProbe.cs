using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mote.Telemetry;

namespace Mote.Native.Mac;

/// <summary>Real system Block copy/invoke and monitor API control, never an input-routing certificate.</summary>
[SupportedOSPlatform("macos")]
internal static unsafe class MacLocalInputMonitorProbe
{
    /// <summary>Copies using the system Blocks runtime, which must preserve a global literal.</summary>
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "_Block_copy")]
    private static extern nint Copy(nint block);
    /// <summary>Releases the copy operation's result using the matching system runtime.</summary>
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "_Block_release")]
    private static extern void Release(nint block);

    /// <summary>Checks enabled ABI storage/API installation on the AppKit thread without posting events.</summary>
    internal static void Verify()
    {
        if (MoteTelemetry.Health.Enabled)
            throw new InvalidOperationException("Local monitor ABI control requires tracing disabled.");
        var block = MacLocalInputMonitor.BlockForAbiControl();
        var literal = (MacLocalInputMonitor.Literal*)block;
        var descriptor = (MacLocalInputMonitor.Descriptor*)literal->Descriptor;
        var system = NativeLibrary.Load("/usr/lib/libSystem.B.dylib");
        if (sizeof(MacLocalInputMonitor.Literal) != 32 || sizeof(MacLocalInputMonitor.Descriptor) != 24 ||
            literal->Isa != NativeLibrary.GetExport(system, "_NSConcreteGlobalBlock") ||
            literal->Flags != ((1 << 28) | (1 << 30)) || literal->Reserved != 0 ||
            descriptor->Reserved != 0 || descriptor->Size != 32 ||
            Marshal.PtrToStringUTF8(descriptor->Signature) != "@16@?0@8")
            throw new InvalidOperationException("Local monitor Block ABI control failed.");
        var copy = Copy(block);
        try
        {
            if (copy != block) throw new InvalidOperationException("Global Block copy identity failed.");
            var invoke = (delegate* unmanaged[Cdecl]<nint, nint, nint>)literal->Invoke;
            nint[] originals = [MacMenuObservationProbe.CreateKey("s"),
                MacMenuObservationProbe.CreateKey("S"), MacMenuObservationProbe.CreateKey("x")];
            if (invoke(copy, 0) != 0 || originals.Any(e => InvokeCopied(copy, e) != e))
                throw new InvalidOperationException("Local monitor typed invoke identity failed.");
            // No event is posted, routed or consumed. This tests only the real Add/Remove API.
            using var monitor = MacLocalInputMonitor.InstallForAbiControl();
            if (monitor is null) throw new InvalidOperationException("Local monitor install control failed.");
            if (invoke(copy, 0) != 0 || originals.Any(e => InvokeCopied(copy, e) != e))
                throw new InvalidOperationException("Installed local monitor event identity failed.");
            monitor.Dispose();
            if (!monitor.RemovedSuccessfully || invoke(copy, 0) != 0 ||
                originals.Any(e => InvokeCopied(copy, e) != e))
                throw new InvalidOperationException("Local monitor removal/passive invoke control failed.");
        }
        finally { Release(copy); NativeLibrary.Free(system); }
        Console.WriteLine("Mac local input monitor global Block ABI/install/remove control passed.");
    }

    /// <summary>Uses the copied native literal's exact invoke ABI, not a managed delegate conversion.</summary>
    private static nint InvokeCopied(nint block, nint originalEvent) =>
        ((delegate* unmanaged[Cdecl]<nint, nint, nint>)((MacLocalInputMonitor.Literal*)block)->Invoke)(block, originalEvent);
}
