using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mote.Telemetry;

namespace Mote.Native.Mac;

/// <summary>One opt-in local key-down observer. It cannot dispatch, replace or consume an event.</summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MacLocalInputMonitor : IDisposable
{
    /// <summary>AppKit UI-thread owner; a failed removal leaves it permanently passive.</summary>
    private static Admission? s_admission;
    /// <summary>One-owner slot; portable tests use a private slot, never the real callback singleton.</summary>
    internal sealed class Admission
    {
        /// <summary>A failed removal keeps this owner present and permanently passive.</summary>
        internal MacLocalInputMonitor? Current;
    }
    /// <summary>Admission slot whose owner must survive partial teardown failure.</summary>
    private readonly Admission _admission;
    /// <summary>Only setup/teardown and the enabled callback use this narrow native seam.</summary>
    private readonly IApi _api;
    /// <summary>Borrowed token plus, once retained, exactly one explicit owned reference.</summary>
    private nint _token;
    /// <summary>Whether this owner acquired the explicit reference, rather than the borrowed token.</summary>
    private bool _retained;
    /// <summary>Passivated before removal so callbacks cannot observe torn-down shell state.</summary>
    private bool _active;
    /// <summary>Teardown is attempted only once, including when native removal fails.</summary>
    private bool _disposed;

    /// <summary>ABI control readback: disposal completed removal and released the explicit reference.</summary>
    internal bool RemovedSuccessfully => _disposed && _token == 0;

    /// <summary>Constructs an enabled-only owner; no shell or event is captured.</summary>
    private MacLocalInputMonitor(IApi api, Admission admission)
    { _api = api; _admission = admission; }

    /// <summary>Installs only after a healthy trace and owned menu exist, on the AppKit UI thread.</summary>
    internal static MacLocalInputMonitor? TryInstall(bool menuReady)
    {
        var health = MoteTelemetry.Health;
        if (!health.Enabled || health.SinkFaulted) return null;
        return Install(NativeApi.Instance, true, menuReady, s_admission ??= new Admission());
    }

    /// <summary>Internal test seam; disabled calls neither allocate an owner nor touch the API.</summary>
    internal static MacLocalInputMonitor? Install(IApi api, bool enabled, bool menuReady, Admission admission)
    {
        if (!enabled) return null;
        if (!menuReady || admission.Current is not null)
        {
            Record(api, TelemetryEvent.NativeInputMonitorUnavailable);
            return null;
        }
        var owner = new MacLocalInputMonitor(api, admission);
        // Reserve admission before Add: partial setup must never admit a second monitor.
        admission.Current = owner;
        try
        {
            owner._token = api.Add();
            if (owner._token == 0) throw new InvalidOperationException("Local monitor unavailable.");
            api.Retain(owner._token);
            owner._retained = true;
            owner._active = true;
            Record(api, TelemetryEvent.NativeInputMonitorReady);
            return owner;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Record(api, TelemetryEvent.NativeInputMonitorUnavailable);
            owner.Dispose();
            return null;
        }
    }

    /// <summary>Always returns the identical borrowed event, even when observation throws.</summary>
    internal nint Observe(nint originalEvent)
    {
        if (!_active) return originalEvent;
        try
        {
            if (_api.IsCandidate(originalEvent))
                Record(_api, TelemetryEvent.NativeInputSaveFamilyCandidate);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { Record(_api, TelemetryEvent.NativeInputMonitorCallbackFailed); }
        return originalEvent;
    }

    /// <summary>Passivates and removes before shell/native pool/telemetry teardown, on the UI thread.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _active = false;
        if (_token == 0)
        {
            if (ReferenceEquals(_admission.Current, this)) _admission.Current = null;
            return;
        }
        try
        {
            _api.Remove(_token);
            if (_retained) _api.Release(_token);
            _token = 0;
            _retained = false;
            if (ReferenceEquals(_admission.Current, this)) _admission.Current = null;
            Record(_api, TelemetryEvent.NativeInputMonitorRemoved);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Keep the token/owner alive and refuse reinstallation. Never retry a failed remove.
            Record(_api, TelemetryEvent.NativeInputMonitorRemovalFailed);
        }
    }

    /// <summary>Optional queue failures must not escape an unmanaged AppKit callback.</summary>
    private static void Record(IApi api, TelemetryEvent kind)
    {
        try { api.Record(kind); }
        catch (Exception error) when (error is not OutOfMemoryException) { }
    }

    /// <summary>Capture-free native Block invoke; no ambient Activity or event survives this call.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint Invoke(nint block, nint originalEvent) =>
        s_admission?.Current?.Observe(originalEvent) ?? originalEvent;

    /// <summary>Explicit in-process ABI control only; production calls this solely while enabling tracing.</summary>
    internal static nint BlockForAbiControl() => NativeApi.Block();

    /// <summary>Explicit diagnostic-only ABI control; caller requires tracing off and posts no input.</summary>
    internal static MacLocalInputMonitor? InstallForAbiControl() =>
        Install(NativeApi.Instance, true, true, s_admission ??= new Admission());

    /// <summary>Narrow internal observation seam, not an application monitor framework.</summary>
    internal interface IApi
    {
        /// <summary>Adds a key-down-only monitor and returns its borrowed opaque token.</summary>
        nint Add();
        /// <summary>Takes one explicit token reference; the original return is not owned.</summary>
        void Retain(nint token);
        /// <summary>Removes the monitor once, after callback passivation.</summary>
        void Remove(nint token);
        /// <summary>Releases only the explicit reference acquired by Retain.</summary>
        void Release(nint token);
        /// <summary>Classifies a borrowed event without retaining or logging its contents.</summary>
        bool IsCandidate(nint originalEvent);
        /// <summary>Enqueues only the closed content-free session checkpoints.</summary>
        void Record(TelemetryEvent kind);
    }

    /// <summary>Clang no-capture global Block ABI: exactly 32 bytes on both supported macOS RIDs.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Literal
    {
        /// <summary>Address of system _NSConcreteGlobalBlock, not its first pointer value.</summary>
        internal nint Isa;
        /// <summary>Only BLOCK_IS_GLOBAL and BLOCK_HAS_SIGNATURE are set.</summary>
        internal int Flags;
        /// <summary>ABI reserved field, zero.</summary>
        internal int Reserved;
        /// <summary>Typed nint invoke(nint block,nint event), C calling convention.</summary>
        internal nint Invoke;
        /// <summary>Stable unmanaged signature descriptor.</summary>
        internal nint Descriptor;
    }

    /// <summary>Signature-bearing descriptor without capture helpers: exactly 24 bytes on 64-bit.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Descriptor
    {
        /// <summary>ABI reserved field, zero.</summary>
        internal nuint Reserved;
        /// <summary>Size of the literal, excluding this descriptor.</summary>
        internal nuint Size;
        /// <summary>NUL-terminated Objective-C signature @16@?0@8.</summary>
        internal nint Signature;
    }

    /// <summary>Enabled-only system interop and process-lifetime Block storage.</summary>
    private sealed class NativeApi : IApi
    {
        /// <summary>Allocated only after the trace-enabled guard.</summary>
        internal static readonly NativeApi Instance = new();
        /// <summary>Prevents beforefieldinit from constructing the API before the enabled guard.</summary>
        static NativeApi() { }
        /// <summary>Never freed: the global Block can outlive shell teardown or failed removal.</summary>
        private static nint s_block;

        /// <summary>Exact NSUInteger-mask plus Block-pointer Objective-C signature.</summary>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern nint AddMonitor(nint cls, nint selector, nuint mask, nint block);

        /// <inheritdoc />
        public nint Add() => AddMonitor(ObjC.Class("NSEvent"),
            ObjC.Sel("addLocalMonitorForEventsMatchingMask:handler:"), 1u << 10, Block());
        /// <inheritdoc />
        public void Retain(nint token) => ObjC.Send(token, ObjC.Sel("retain"));
        /// <inheritdoc />
        public void Remove(nint token) => ObjC.Send(ObjC.Class("NSEvent"), ObjC.Sel("removeMonitor:"), token);
        /// <inheritdoc />
        public void Release(nint token) => ObjC.Send(token, ObjC.Sel("release"));
        /// <inheritdoc />
        public bool IsCandidate(nint originalEvent) => MacNativeSaveCandidate.Matches(originalEvent);
        /// <inheritdoc />
        public void Record(TelemetryEvent kind) => MoteTelemetry.RecordNativeInputCheckpoint(kind);

        /// <summary>Constructs stable unmanaged no-capture storage once on the AppKit UI thread.</summary>
        internal static nint Block()
        {
            if (s_block != 0) return s_block;
            if (sizeof(nint) != 8 || sizeof(Literal) != 32 || sizeof(Descriptor) != 24)
                throw new PlatformNotSupportedException("Global Block ABI unavailable.");
            var system = NativeLibrary.Load("/usr/lib/libSystem.B.dylib");
            var isa = NativeLibrary.GetExport(system, "_NSConcreteGlobalBlock");
            var signature = Marshal.StringToCoTaskMemUTF8("@16@?0@8");
            var descriptor = (Descriptor*)NativeMemory.AllocZeroed((nuint)sizeof(Descriptor));
            descriptor->Size = (nuint)sizeof(Literal);
            descriptor->Signature = signature;
            var literal = (Literal*)NativeMemory.AllocZeroed((nuint)sizeof(Literal));
            literal->Isa = isa;
            literal->Flags = (1 << 28) | (1 << 30);
            literal->Invoke = (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint>)&Invoke;
            literal->Descriptor = (nint)descriptor;
            s_block = (nint)literal;
            return s_block;
        }
    }
}
