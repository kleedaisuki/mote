using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mote.Native.Mac;
using Mote.Telemetry;

namespace Mote.Tests;

/// <summary>Checks ownership and event identity without loading AppKit on the test host.</summary>
[SupportedOSPlatform("macos")]
public sealed class MacLocalInputMonitorTests
{
    /// <summary>Runs failed-removal last because its deliberately passive owner lives forever.</summary>
    [Fact]
    public void AdmissionObservationAndTeardownPreserveBorrowedOwnership()
    {
        var unavailable = new FakeApi();
        Assert.Null(MacLocalInputMonitor.Install(unavailable, false, true));
        Assert.Empty(unavailable.Events);
        Assert.Null(MacLocalInputMonitor.Install(unavailable, true, false));
        Assert.Equal(0, unavailable.Adds);
        Assert.Equal([TelemetryEvent.NativeInputMonitorUnavailable], unavailable.Events);

        var nil = new FakeApi { Token = 0 };
        Assert.Null(MacLocalInputMonitor.Install(nil, true, true));
        Assert.Equal(1, nil.Adds);
        Assert.Equal(0, nil.Retains);
        Assert.Equal(0, nil.Removes);
        Assert.Equal(0, nil.Releases);
        Assert.Equal([TelemetryEvent.NativeInputMonitorUnavailable], nil.Events);

        var addFailure = new FakeApi { ThrowAdd = true };
        Assert.Null(MacLocalInputMonitor.Install(addFailure, true, true));
        Assert.Equal(1, addFailure.Adds);
        Assert.Equal(0, addFailure.Removes);
        Assert.Equal(0, addFailure.Releases);

        var retainFailure = new FakeApi { ThrowRetain = true };
        Assert.Null(MacLocalInputMonitor.Install(retainFailure, true, true));
        Assert.Equal(1, retainFailure.Adds);
        Assert.Equal(1, retainFailure.Retains);
        Assert.Equal(1, retainFailure.Removes);
        Assert.Equal(0, retainFailure.Releases);
        Assert.Equal([TelemetryEvent.NativeInputMonitorUnavailable,
            TelemetryEvent.NativeInputMonitorRemoved], retainFailure.Events);

        var success = new FakeApi();
        var owner = Assert.IsType<MacLocalInputMonitor>(MacLocalInputMonitor.Install(success, true, true));
        Assert.Equal([TelemetryEvent.NativeInputMonitorReady], success.Events);
        var competing = new FakeApi();
        Assert.Null(MacLocalInputMonitor.Install(competing, true, true));
        Assert.Equal(0, competing.Adds);
        Assert.Equal([TelemetryEvent.NativeInputMonitorUnavailable], competing.Events);

        Assert.Equal((nint)0, owner.Observe(0));
        Assert.Equal((nint)12345, owner.Observe(12345));
        Assert.Equal(new nint[] { 0, 12345 }, success.Observed);
        Assert.Equal(1, success.Events.Count(e => e == TelemetryEvent.NativeInputSaveFamilyCandidate));
        success.ThrowClassify = true;
        Assert.Equal((nint)54321, owner.Observe(54321));
        Assert.Equal(TelemetryEvent.NativeInputMonitorCallbackFailed, success.Events[^1]);
        success.ThrowClassify = false;
        success.ThrowRecord = true;
        Assert.Equal((nint)67890, owner.Observe(67890));
        success.ThrowRecord = false;

        var observationsBeforeRemove = success.Observed.Count;
        success.DuringRemove = () => Assert.Equal((nint)111, owner.Observe(111));
        owner.Dispose();
        Assert.Equal(observationsBeforeRemove, success.Observed.Count);
        Assert.Equal(1, success.Adds);
        Assert.Equal(1, success.Retains);
        Assert.Equal(1, success.Removes);
        Assert.Equal(1, success.Releases);
        Assert.Equal(new[] { "add", "retain", "remove", "release" }, success.Ownership);
        Assert.Equal(TelemetryEvent.NativeInputMonitorRemoved, success.Events[^1]);
        owner.Dispose();
        Assert.Equal(1, success.Removes);
        Assert.Equal(1, success.Releases);
        Assert.Equal((nint)222, owner.Observe(222));
        Assert.Equal(observationsBeforeRemove, success.Observed.Count);

        // A throwing telemetry sink must not prevent successful admission or cleanup.
        var recordFailure = new FakeApi { ThrowRecord = true };
        var recordingOwner = Assert.IsType<MacLocalInputMonitor>(
            MacLocalInputMonitor.Install(recordFailure, true, true));
        recordingOwner.Dispose();
        Assert.Equal(1, recordFailure.Removes);
        Assert.Equal(1, recordFailure.Releases);

        // This scenario must remain last: failed native removal permanently reserves admission.
        var removalFailure = new FakeApi { ThrowRemove = true };
        var passiveOwner = Assert.IsType<MacLocalInputMonitor>(
            MacLocalInputMonitor.Install(removalFailure, true, true));
        removalFailure.DuringRemove = () => Assert.Equal((nint)333, passiveOwner.Observe(333));
        passiveOwner.Dispose();
        Assert.Empty(removalFailure.Observed);
        Assert.Equal(1, removalFailure.Removes);
        Assert.Equal(0, removalFailure.Releases);
        Assert.Equal(TelemetryEvent.NativeInputMonitorRemovalFailed, removalFailure.Events[^1]);
        passiveOwner.Dispose();
        Assert.Equal((nint)444, passiveOwner.Observe(444));
        Assert.Empty(removalFailure.Observed);
        Assert.Equal(1, removalFailure.Removes);
        Assert.Equal(0, removalFailure.Releases);
        var forbidden = new FakeApi();
        Assert.Null(MacLocalInputMonitor.Install(forbidden, true, true));
        Assert.Equal(0, forbidden.Adds);
        Assert.Equal([TelemetryEvent.NativeInputMonitorUnavailable], forbidden.Events);
    }

    /// <summary>Checks both supported 64-bit Block layouts against their ABI field offsets.</summary>
    [Fact]
    public void GlobalBlockLayoutsMatchThe64BitAbi()
    {
        Assert.Equal(8, IntPtr.Size);
        Assert.Equal(32, Marshal.SizeOf<MacLocalInputMonitor.Literal>());
        Assert.Equal(24, Marshal.SizeOf<MacLocalInputMonitor.Descriptor>());
        AssertOffset<MacLocalInputMonitor.Literal>("Isa", 0);
        AssertOffset<MacLocalInputMonitor.Literal>("Flags", 8);
        AssertOffset<MacLocalInputMonitor.Literal>("Reserved", 12);
        AssertOffset<MacLocalInputMonitor.Literal>("Invoke", 16);
        AssertOffset<MacLocalInputMonitor.Literal>("Descriptor", 24);
        AssertOffset<MacLocalInputMonitor.Descriptor>("Reserved", 0);
        AssertOffset<MacLocalInputMonitor.Descriptor>("Size", 8);
        AssertOffset<MacLocalInputMonitor.Descriptor>("Signature", 16);
    }

    /// <summary>Uses runtime marshaling metadata instead of loading the native Block implementation.</summary>
    private static void AssertOffset<T>(string field, int expected) where T : struct =>
        Assert.Equal((nint)expected, Marshal.OffsetOf<T>(field));

    /// <summary>Records exact token operations and simulates nonfatal native or sink failures.</summary>
    private sealed class FakeApi : MacLocalInputMonitor.IApi
    {
        public nint Token = 789;
        public bool ThrowAdd, ThrowRetain, ThrowRemove, ThrowClassify, ThrowRecord;
        public int Adds, Retains, Removes, Releases;
        public Action? DuringRemove;
        public readonly List<TelemetryEvent> Events = [];
        public readonly List<nint> Observed = [];
        public readonly List<string> Ownership = [];

        /// <inheritdoc />
        public nint Add()
        {
            Adds++;
            Ownership.Add("add");
            if (ThrowAdd) throw new InvalidOperationException("add failure");
            return Token;
        }

        /// <inheritdoc />
        public void Retain(nint token)
        {
            Assert.Equal(Token, token);
            Retains++;
            Ownership.Add("retain");
            if (ThrowRetain) throw new InvalidOperationException("retain failure");
        }

        /// <inheritdoc />
        public void Remove(nint token)
        {
            Assert.Equal(Token, token);
            Removes++;
            Ownership.Add("remove");
            DuringRemove?.Invoke();
            if (ThrowRemove) throw new InvalidOperationException("remove failure");
        }

        /// <inheritdoc />
        public void Release(nint token)
        {
            Assert.Equal(Token, token);
            Releases++;
            Ownership.Add("release");
        }

        /// <inheritdoc />
        public bool IsCandidate(nint originalEvent)
        {
            Observed.Add(originalEvent);
            if (ThrowClassify) throw new InvalidOperationException("classify failure");
            return originalEvent != 0;
        }

        /// <inheritdoc />
        public void Record(TelemetryEvent kind)
        {
            if (ThrowRecord) throw new InvalidOperationException("record failure");
            Events.Add(kind);
        }
    }
}
