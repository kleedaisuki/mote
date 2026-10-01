using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mote.Formats;
using Mote.Native.Mac.Canvas;

namespace Mote.Native.Mac;

/// <summary>Disposable C0/P0 experiment seams; absent from ordinary launches and never alter AX returns.</summary>
internal sealed unsafe partial class MacCsvGrid
{
    /// <summary>All entries share one process-lifetime recorder, including detached/off-main refusals.</summary>
    private static readonly MacGridPairObservation? PairObservation =
        Environment.GetEnvironmentVariable("MOTE_NATIVE_GRID_ACCESSIBILITY") == "1" &&
        Environment.GetEnvironmentVariable("MOTE_NATIVE_GRID_SHOWMENU_DISCRIMINATOR") == "1" ? new() : null;
    /// <summary>Owned timer and internal ready root; neither native identity is serialized.</summary>
    private nint _pairTimer, _pairBaselineRoot;
    /// <summary>Internal ready-frame equality baseline, never exported as an epoch number.</summary>
    private GridAccessibilityId? _pairBaselineFrame;
    /// <summary>Verified existing repository cache session; ordinary launches leave it null.</summary>
    private string? _pairSession;
    /// <summary>Monotonic owner timer ceiling, independent of external AX/client deadlines.</summary>
    private long _pairStarted;

    /// <summary>NSTimer construction uses native BOOL, double and object argument register classes.</summary>
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint PairTimerCreate(nint receiver, nint selector, double interval,
        nint target, nint action, nint info, byte repeats);

    /// <summary>Rejects nonexistent/symlinked session paths before any diagnostic file operation.</summary>
    internal static string ResolvePairSession(string root, string session)
    {
        var cache = Path.GetFullPath(Path.Combine(root, ".cache"));
        var path = Path.GetFullPath(session);
        if (!Path.IsPathFullyQualified(session) || !path.StartsWith(cache + Path.DirectorySeparatorChar,
                StringComparison.Ordinal) || !Directory.Exists(path))
            throw new ArgumentException("Grid pair session must be an existing repository .cache descendant.");
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0 || directory.LinkTarget is not null)
                throw new ArgumentException("Grid pair session cannot traverse links.");
            if (directory.FullName == cache) return path;
        }
        throw new ArgumentException("Grid pair session is not repository confined.");
    }

    /// <summary>Diagnostic timer runs in tracking and default modes; it never synthesizes input.</summary>
    private void StartPairDiagnostic()
    {
        if (PairObservation is null) return;
        _pairSession = ResolvePairSession(Directory.GetCurrentDirectory(),
            Environment.GetEnvironmentVariable("MOTE_NATIVE_GRID_SHOWMENU_SESSION") ?? "");
        _pairStarted = Environment.TickCount64;
        _pairTimer = PairTimerCreate(ObjC.Class("NSTimer"),
            ObjC.Sel("timerWithTimeInterval:target:selector:userInfo:repeats:"), .05,
            _delegate, ObjC.Sel("moteGridPairTick:"), 0, 1);
        ObjC.Send(_pairTimer, ObjC.Sel("retain"));
        var runLoop = ObjC.Send(ObjC.Class("NSRunLoop"), ObjC.Sel("mainRunLoop"));
        ObjC.Send(runLoop, ObjC.Sel("addTimer:forMode:"), _pairTimer,
            PairRunLoopMode("/System/Library/Frameworks/Foundation.framework/Foundation", "NSDefaultRunLoopMode"));
        ObjC.Send(runLoop, ObjC.Sel("addTimer:forMode:"), _pairTimer,
            PairRunLoopMode("/System/Library/Frameworks/AppKit.framework/AppKit", "NSEventTrackingRunLoopMode"));
    }

    /// <summary>Uses framework constants rather than guessing their private NSString wire values.</summary>
    private static nint PairRunLoopMode(string libraryPath, string symbol)
    {
        var library = NativeLibrary.Load(libraryPath);
        try { return Marshal.ReadIntPtr(NativeLibrary.GetExport(library, symbol)); }
        finally { NativeLibrary.Free(library); }
    }

    /// <summary>Timer callback owns I/O; AX callbacks only fill preallocated facts.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PairTick(nint self, nint selector, nint timer)
    {
        try
        {
            if (AccessibilityMainThread && Instances.TryGetValue(self, out var owner)) owner.PollPairDiagnostic();
        }
        catch { /* An unavailable experiment is not a product repair or a fabricated AX reply. */ }
    }

    /// <summary>Ready is an internal predicate, not a warmed action or queue reservation.</summary>
    private void PollPairDiagnostic()
    {
        if (PairObservation is not { } facts || _pairSession is null) return;
        if (Environment.TickCount64 - _pairStarted > 75000) { StopPairDiagnostic(); ExportPairDiagnostic(); return; }
        if (!facts.Ready && !_installing && _accessibilityFrame is { } frame &&
            _accessibilityTable != 0 && _table != 0 && _delegate != 0)
        {
            var window = ObjC.Send(_table, ObjC.Sel("window"));
            if (window != 0 && AccessibilityNativeBool(window, ObjC.Sel("isVisible")) != 0 &&
                ObjC.Send(_table, ObjC.Sel("menu")) != 0 && TryAccessibilityMenuAnchor(
                    MacOnScreenCanvasNative.GetRect(_table, ObjC.Sel("visibleRect")), out _))
            {
                _pairBaselineRoot = _accessibilityTable; _pairBaselineFrame = frame.Id;
                WritePairFile("ready", "mote-grid-pair-ready-v1\n"u8.ToArray()); facts.Ready = true;
            }
        }
        var finish = Path.Combine(_pairSession, "finish");
        if (!File.Exists(finish) || new FileInfo(finish) is not { Length: 25, LinkTarget: null } info ||
            (info.Attributes & FileAttributes.ReparsePoint) != 0 ||
            File.ReadAllText(finish) != "mote-grid-pair-finish-v1\n") return;
        facts.Finish = true;
        StopPairDiagnostic();
        _accessibilityMenuPresentation.CancelPending();
        ObjC.Send(ObjC.Class("NSObject"), ObjC.Sel("cancelPreviousPerformRequestsWithTarget:selector:object:"),
            _delegate, ObjC.Sel("moteGridShowMenu:"), 0);
        var shown = ObjC.Send(_table, ObjC.Sel("accessibilityShownMenu"));
        if (shown != 0 && shown == ObjC.Send(_table, ObjC.Sel("menu")))
            ObjC.Send(shown, ObjC.Sel("cancelTracking"));
        // Default-mode close waits until nested tracking and popup participant retains have unwound.
        // Do not clear shownMenu here: its existing did-close callback owns that relation/counter.
        ObjC.Send(_delegate, ObjC.Sel("performSelector:withObject:afterDelay:"),
            ObjC.Sel("moteGridPairClose:"), 0, 0d);
    }

    /// <summary>Closes only the synthetic owned window after menu tracking has returned.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PairClose(nint self, nint selector, nint ignored)
    {
        try
        {
            if (!AccessibilityMainThread || !Instances.TryGetValue(self, out var owner) || PairObservation is not { Finish: true } facts) return;
            var ownedWindow = ObjC.Send(owner._table, ObjC.Sel("window"));
            facts.NormalShutdown = ownedWindow != 0;
            owner.ExportPairDiagnostic();
            if (ownedWindow != 0) ObjC.Send(ownedWindow, ObjC.Sel("performClose:"), 0);
        }
        catch { /* Failed cleanup remains unavailable/forced in the independent process ledger. */ }
    }

    /// <summary>Invalidates the only diagnostic timer before the native delegate is released.</summary>
    private void StopPairDiagnostic()
    {
        if (_pairTimer == 0) return;
        ObjC.Send(_pairTimer, ObjC.Sel("invalidate")); ObjC.Send(_pairTimer, ObjC.Sel("release")); _pairTimer = 0;
    }

    /// <summary>Disposal cannot leave diagnostic delayed callbacks aimed at a released delegate.</summary>
    private void CancelPairClose()
    {
        if (PairObservation is not null && _delegate != 0)
            ObjC.Send(ObjC.Class("NSObject"), ObjC.Sel("cancelPreviousPerformRequestsWithTarget:selector:object:"),
                _delegate, ObjC.Sel("moteGridPairClose:"), 0);
    }

    /// <summary>Only fixed filenames in the verified session can be exported; existing links are refused.</summary>
    private void WritePairFile(string name, byte[] bytes)
    {
        if (_pairSession is null) return;
        ResolvePairSession(Directory.GetCurrentDirectory(), _pairSession);
        var path = Path.Combine(_pairSession, name);
        if (File.Exists(path) && ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).LinkTarget is not null))
            throw new InvalidOperationException("Grid pair report link refused.");
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>Exports after normal disposal or timeout; driver independently certifies actual process exit.</summary>
    private void ExportPairDiagnostic()
    { if (PairObservation is { } facts && _pairSession is not null) WritePairFile("server.json", facts.Export()); }

    /// <summary>Samples only managed lifetime facts; never logs handles, revisions, coordinates or source.</summary>
    private MacGridPairObservation.Entry PairEntry(nint receiver, bool main) => new(main, true,
        receiver == _accessibilityTable, _accessibilityTable != 0 && _table != 0, _installing,
        _accessibilityFrame is not null, PairObservation!.Ready,
        PairObservation.Ready ? _pairBaselineRoot == _accessibilityTable : null,
        PairObservation.Ready ? _pairBaselineFrame == _accessibilityFrame?.Id : null, null, null, false);

    /// <summary>Queue/dispatch/menu/detach observations do not invoke framework getters.</summary>
    private void PairTransition(int phase)
    {
        PairObservation?.Transition(phase, PairObservation.Ready && _pairBaselineRoot == _accessibilityTable,
            PairObservation.Ready && _pairBaselineFrame == _accessibilityFrame?.Id,
            _accessibilityTable != 0 && _pairBaselineRoot == _accessibilityTable,
            _accessibilityTable != 0 && _table != 0);
    }
}
